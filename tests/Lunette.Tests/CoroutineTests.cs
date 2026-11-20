using Xunit;

namespace Lunette.Tests;

/// <summary>Coroutines observed through the Lua language: the seam is DoString plus print output.</summary>
public class CoroutineTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    /// <summary>Each nested resume costs a dispatch-loop frame on the .NET stack, so deep nesting needs more than the test host's default.</summary>
    private static string RunOnBigStack(string source)
    {
        string? result = null;
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try { result = Run(source); }
            catch (Exception e) { failure = e; }
        }, 64 * 1024 * 1024);
        t.Start();
        t.Join();
        if (failure is not null) throw failure;
        return result!;
    }

    [Fact]
    public void Resume_passes_values_in_and_yield_passes_values_out() =>
        Assert.Equal("true\t1\t2\ntrue\tx\ty\tdone\nfalse\tcannot resume dead coroutine\n", Run("""
            local co = coroutine.create(function(a, b)
              local c, d = coroutine.yield(a, b)
              return c, d, "done"
            end)
            print(coroutine.resume(co, 1, 2))
            print(coroutine.resume(co, "x", "y"))
            print(coroutine.resume(co))
            """));

    [Fact]
    public void Status_follows_the_coroutine_lifecycle() =>
        Assert.Equal("suspended\nrunning\nnormal\nsuspended\ndead\n", Run("""
            local co, outer
            co = coroutine.create(function()
              print(coroutine.status(co))
              outer = coroutine.create(function() print(coroutine.status(co)) coroutine.yield() end)
              coroutine.resume(outer)
              coroutine.yield()
            end)
            print(coroutine.status(co))
            coroutine.resume(co)
            print(coroutine.status(co))
            coroutine.resume(co)
            print(coroutine.status(co))
            """));

    [Fact]
    public void Resuming_a_dead_coroutine_reports_an_error() =>
        Assert.Equal("true\nfalse\tcannot resume dead coroutine\n", Run("""
            local co = coroutine.create(function() end)
            print(coroutine.resume(co))
            print(coroutine.resume(co))
            """));

    [Fact]
    public void Errors_inside_a_coroutine_kill_it_and_are_returned() =>
        Assert.Equal("false\tboom\ndead\n", Run("""
            local co = coroutine.create(function() error("boom", 0) end)
            print(coroutine.resume(co))
            print(coroutine.status(co))
            """));

    [Fact]
    public void Wrap_returns_values_and_rethrows_errors() =>
        Assert.Equal("1\n2\n3\nfalse\tx\n", Run("""
            local gen = coroutine.wrap(function() for i = 1, 3 do coroutine.yield(i) end end)
            print(gen()) print(gen()) print(gen())
            local bad = coroutine.wrap(function() error("x", 0) end)
            print(pcall(bad))
            """));

    [Fact]
    public void Wrap_works_as_a_generic_for_iterator() =>
        Assert.Equal("1\n2\n3\n", Run("""
            for v in coroutine.wrap(function() for i = 1, 3 do coroutine.yield(i) end end) do print(v) end
            """));

    [Fact]
    public void Yield_works_across_pcall() =>
        Assert.Equal("true\t1\ntrue\tr\nfalse\toops\n", Run("""
            local co = coroutine.wrap(function()
              local ok, v = pcall(function() local x = coroutine.yield(1) return x end)
              coroutine.yield(ok, v)
              print(pcall(function() coroutine.yield() error("oops", 0) end))
            end)
            print(true, co())
            print(co("r"))
            co() co()
            """));

    [Fact]
    public void Pcall_of_yield_itself_yields_and_returns_true_with_the_resume_values() =>
        Assert.Equal("true\tb\n", Run("""
            local co = coroutine.wrap(function() print(pcall(coroutine.yield, "a")) end)
            co()
            co("b")
            """));

    [Fact]
    public void Yield_works_across_metamethods() =>
        Assert.Equal("idx\t5\narith\t7\nlt\ttrue\neq\ttrue\nlen\t9\nunm\t-1\n", Run("""
            local mt = {
              __index = function(t, k) return coroutine.yield("idx") end,
              __add = function(a, b) return coroutine.yield("arith") end,
              __lt = function(a, b) return coroutine.yield("lt") end,
              __eq = function(a, b) return coroutine.yield("eq") end,
              __newindex = function(t, k, v) coroutine.yield("newindex") end,
              __len = function(t) return coroutine.yield("len") end,
              __unm = function(t) return coroutine.yield("unm") end,
            }
            local a, b = setmetatable({}, mt), setmetatable({}, mt)
            local co = coroutine.wrap(function()
              local v = a.x
              print("idx", v)
              print("arith", a + b)
              print("lt", a < b)
              print("eq", a == b)
              a.y = 1
              print("len", #a)
              print("unm", -a)
            end)
            co()
            co(5) co(7) co(1) co(true) co() co(9) co(-1)
            """));

    [Fact]
    public void Yield_outside_a_coroutine_is_an_error()
    {
        var ex = Assert.Throws<LuaException>(() => Run("coroutine.yield(1)"));
        Assert.Contains("attempt to yield from outside a coroutine", ex.Message);
    }

    [Fact]
    public void Yield_across_a_host_callback_boundary_is_an_error() =>
        Assert.Equal("false\tattempt to yield across a C-call boundary\n", Run("""
            local mt = setmetatable({}, {__tostring = function() coroutine.yield() return "x" end})
            local co = coroutine.create(function() return tostring(mt) end)
            local ok, msg = coroutine.resume(co)
            print(ok, msg)
            """));

    [Fact]
    public void Isyieldable_and_running_report_the_current_coroutine() =>
        Assert.Equal("false\ttrue\ntrue\tfalse\ttrue\n", Run("""
            local _, ismain = coroutine.running()
            print(coroutine.isyieldable(), ismain)
            local co = coroutine.wrap(function()
              local c, main = coroutine.running()
              print(coroutine.isyieldable(), main, type(c) == "thread")
            end)
            co()
            """));

    [Fact]
    public void Close_kills_a_suspended_coroutine() =>
        Assert.Equal("true\tdead\nfalse\tcannot resume dead coroutine\n", Run("""
            local co = coroutine.create(function() coroutine.yield() end)
            coroutine.resume(co)
            print(coroutine.close(co), coroutine.status(co))
            print(coroutine.resume(co))
            """));

    [Fact]
    public void Close_runs_pending_to_be_closed_handlers() =>
        Assert.Equal("closed\ntrue\n", Run("""
            local co = coroutine.create(function()
              local x <close> = setmetatable({}, {__close = function() print("closed") end})
              coroutine.yield()
            end)
            coroutine.resume(co)
            print(coroutine.close(co))
            """));

    [Fact]
    public void Upvalues_survive_across_yields() =>
        Assert.Equal("1\n2\n3\n", Run("""
            local function counter()
              local n = 0
              return coroutine.wrap(function() while true do n = n + 1 coroutine.yield(n) end end)
            end
            local c = counter()
            print(c()) print(c()) print(c())
            """));

    [Fact]
    public void Nested_resume_depth_is_limited()
    {
        string r = RunOnBigStack("""
            local function nest(n)
              local co = coroutine.create(nest)
              local ok, err = coroutine.resume(co, n + 1)
              if not ok then return err end
              return err
            end
            print(nest(1))
            """);
        Assert.Contains("C stack overflow", r);
    }

    [Fact]
    public void Type_of_a_coroutine_is_thread() =>
        Assert.Equal("thread\n", Run("print(type(coroutine.create(print)))"));
}
