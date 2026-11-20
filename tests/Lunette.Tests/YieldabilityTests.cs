using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Paths that PUC 5.4 lets yield (or errors cross) and that must do the same here: __concat, __close, pcall around host functions,
/// errors raised by host continuations, and __call on userdata. Each script runs twice, once with no hook and once with a hook
/// installed in the coroutine (<c>HOOK</c>), so both dispatch loops are covered.
/// </summary>
public class YieldabilityTests
{
    private sealed class Adder;

    private const string Hook = "debug.sethook(function() end, 'crl', 1)";

    public static IEnumerable<object[]> Modes() => [[""], [Hook]];

    private static string Run(string source, string hook, Action<LuaState>? setup = null)
    {
        var state = new LuaState();
        var sw = new StringWriter { NewLine = "\n" };
        state.Output = sw;
        state.Register("hostYield", (ref CallContext ctx) =>
        {
            ctx.Push("hy");
            return ctx.Yield();
        });
        state.Register("hostYieldK", (ref CallContext ctx) =>
        {
            ctx.Push("hyk");
            return ctx.Yield((ref CallContext k) =>
            {
                k.Push(k.Args[0].AsInteger * 2);
                return 1;
            });
        });
        state.Register("hostYieldThrow", (ref CallContext ctx) =>
        {
            ctx.Push("hyt");
            return ctx.Yield((ref CallContext k) => throw k.Error("kerr"));
        });
        state.Register("hostThrow", (ref CallContext ctx) => throw ctx.Error("plain"));
        setup?.Invoke(state);
        state.Call(state.Load(source.Replace("HOOK", hook)));
        return sw.ToString();
    }

    [Theory, MemberData(nameof(Modes))]
    public void Concat_metamethod_can_yield(string hook) =>
        Assert.Equal("cc\nres\nright\n", Run("""
            local mt = {__concat = function(a, b) return coroutine.yield("cc") end}
            local co = coroutine.wrap(function()
              HOOK
              local o = setmetatable({}, mt)
              print(o .. "x")
              print("l" .. setmetatable({}, {__concat = function(a, b) return "right" end}))
            end)
            print(co())
            co("res")
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Concat_results_flow_into_the_rest_of_the_expression(string hook) =>
        Assert.Equal("y\n[a-bc]\n", Run("""
            local mt = {__concat = function(a, b)
              coroutine.yield("y")
              return "[" .. a.n .. "-" .. b .. "]"
            end}
            local co = coroutine.wrap(function()
              HOOK
              return setmetatable({n = "a"}, mt) .. "b" .. "c"
            end)
            print(co())
            print(co())
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Close_handler_can_yield_on_block_exit(string hook) =>
        Assert.Equal("cl\nend\n", Run("""
            local co = coroutine.wrap(function()
              HOOK
              do local x <close> = setmetatable({}, {__close = function() coroutine.yield("cl") end}) end
              return "end"
            end)
            print(co())
            print(co())
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Close_handler_can_yield_on_return_and_keeps_the_results(string hook) =>
        Assert.Equal("b\na\n1\t2\t3\n", Run("""
            local function close(n) return setmetatable({}, {__close = function() coroutine.yield(n) end}) end
            local function three(...) return ... end
            local co = coroutine.wrap(function()
              HOOK
              local a <close> = close("a")
              local b <close> = close("b")
              return three(1, 2, 3)
            end)
            print(co())
            print(co())
            print(co())
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Close_handler_can_yield_while_an_error_unwinds_to_a_pcall(string hook) =>
        Assert.Equal("clboom\nfalse\tboom\n", Run("""
            local co = coroutine.wrap(function()
              HOOK
              return pcall(function()
                local x <close> = setmetatable({}, {__close = function(_, e) coroutine.yield("cl" .. tostring(e)) end})
                error("boom", 0)
              end)
            end)
            print(co())
            print(co())
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void An_error_in_a_yielding_close_handler_replaces_the_error(string hook) =>
        Assert.Equal("first\nsecond\nfalse\tsecond\n", Run("""
            local function h(name, fail) return setmetatable({}, {__close = function(_, e)
              coroutine.yield(name)
              if fail then error(fail, 0) end
            end}) end
            local co = coroutine.wrap(function()
              HOOK
              return pcall(function()
                local a <close> = h("second")
                local b <close> = h("first", "second")
                error("boom", 0)
              end)
            end)
            print(co())
            print(co())
            print(co())
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void A_yielding_close_handler_under_xpcall_still_returns_the_handled_error(string hook) =>
        Assert.Equal("cl\nfalse\tH:boom\n", Run("""
            local co = coroutine.wrap(function()
              HOOK
              return xpcall(function()
                local x <close> = setmetatable({}, {__close = function() coroutine.yield("cl") end})
                error("boom", 0)
              end, function(m) return "H:" .. m end)
            end)
            print(co())
            print(co())
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Close_handlers_outside_a_coroutine_still_run_without_yielding(string hook) =>
        Assert.Equal("closed\nfalse\tattempt to yield from outside a coroutine\n", Run("""
            HOOK
            print(pcall(function()
              local x <close> = setmetatable({}, {__close = function() print("closed") coroutine.yield() end})
            end))
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Xpcall_around_yield_passes_values_both_ways(string hook) =>
        Assert.Equal("1\t2\ntrue\t7\t8\n", Run("""
            local co = coroutine.wrap(function() HOOK return xpcall(coroutine.yield, print, 1, 2) end)
            print(co())
            print(co(7, 8))
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Pairs_metamethod_can_yield(string hook) =>
        Assert.Equal("pp\n0\n", Run("""
            local mt = {__pairs = function(t) coroutine.yield("pp") return next, {}, nil end}
            local co = coroutine.wrap(function()
              HOOK
              local n = 0
              for _ in pairs(setmetatable({}, mt)) do n = n + 1 end
              return n
            end)
            print(co())
            print(co())
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Pcall_around_a_yielding_host_function_can_yield(string hook) =>
        Assert.Equal("hy\ntrue\t5\n", Run("""
            local co = coroutine.wrap(function() HOOK return pcall(hostYield) end)
            print(co())
            print(co(5))
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Pcall_around_a_host_function_with_a_continuation_runs_the_continuation(string hook) =>
        Assert.Equal("hyk\ntrue\t42\n", Run("""
            local co = coroutine.wrap(function() HOOK return pcall(hostYieldK, 1) end)
            print(co())
            print(co(21))
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Xpcall_around_a_yielding_host_function_can_yield(string hook) =>
        Assert.Equal("hy\ntrue\t5\n", Run("""
            local co = coroutine.wrap(function() HOOK return xpcall(hostYield, print) end)
            print(co())
            print(co(5))
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Pcall_still_catches_an_error_from_a_host_function_without_a_position(string hook) =>
        Assert.Equal("false\tplain\n", Run("""
            HOOK
            print(pcall(hostThrow))
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void An_error_in_a_continuation_reaches_a_pcall_inside_the_coroutine(string hook) =>
        Assert.Equal("hyt\nfalse\tkerr\n", Run("""
            local co = coroutine.wrap(function()
              HOOK
              return pcall(function() return hostYieldThrow() end)
            end)
            print(co())
            local ok, msg = co()
            print(ok, (msg:gsub("^.-:%d+: ", "")))
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void An_error_in_a_continuation_through_pcall_of_the_host_function_itself(string hook) =>
        Assert.Equal("hyt\nfalse\tkerr\n", Run("""
            local co = coroutine.wrap(function() HOOK return pcall(hostYieldThrow) end)
            print(co())
            print(co())
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void An_error_in_a_continuation_without_a_pcall_kills_the_coroutine(string hook) =>
        Assert.Equal("hyt\nfalse\tdead\n", Run("""
            local co = coroutine.create(function() HOOK hostYieldThrow() end)
            print(select(2, coroutine.resume(co)))
            local ok, msg = coroutine.resume(co)
            print(ok, coroutine.status(co))
            """, hook));

    [Theory, MemberData(nameof(Modes))]
    public void Userdata_with_a_call_metamethod_can_be_called(string hook) =>
        Assert.Equal("6\n10\n", Run("""
            HOOK
            print(adder(3))
            print(select(2, pcall(adder, 5)))
            """, hook, state =>
        {
            var mt = new LuaTable();
            mt.Set("__call", LuaValue.FromFunction(state.CreateFunction((ref CallContext ctx) =>
            {
                ctx.Push(ctx.Args[1].AsInteger * 2);
                return 1;
            })));
            state.RegisterMetatable<Adder>(mt);
            state.SetGlobal("adder", LuaValue.FromUserdata(state.CreateUserdata(new Adder())));
        }));
}
