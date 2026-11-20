using Xunit;

namespace Lunette.Tests;

/// <summary>Seams: Lua-level collectgarbage, setmetatable with __mode / __gc, and LuaState.Dispose (ticket 12).</summary>
public class GcTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    // ---- collectgarbage ----

    [Fact]
    public void Collect_returns_zero_and_count_is_a_positive_float() =>
        Assert.Equal("0\tfloat\ttrue\n", Run("""
            print(collectgarbage(), math.type(collectgarbage("count")), collectgarbage("count") > 0)
            """));

    [Fact]
    public void Step_returns_true_and_stop_restart_toggle_isrunning() =>
        Assert.Equal("true\ttrue\nfalse\ttrue\n", Run("""
            print(collectgarbage("step"), collectgarbage("isrunning"))
            collectgarbage("stop")
            local stopped = collectgarbage("isrunning")
            collectgarbage("restart")
            print(stopped, collectgarbage("isrunning"))
            """));

    [Fact]
    public void Switching_modes_returns_the_previous_mode() =>
        Assert.Equal("incremental\tgenerational\tgenerational\tincremental\n", Run("""
            print(collectgarbage("generational"), collectgarbage("generational"),
                  collectgarbage("incremental"), collectgarbage("incremental"))
            """));

    [Fact]
    public void Setpause_and_setstepmul_return_the_previous_value() =>
        Assert.Equal("200\t50\t300\t100\n", Run("""
            local a = collectgarbage("setpause", 300)
            local b = collectgarbage("setstepmul", 50)
            print(a, collectgarbage("setstepmul", 70), collectgarbage("setpause", 100), b)
            """));

    [Fact]
    public void Unknown_option_is_an_argument_error() =>
        Assert.Contains("invalid option 'bogus'",
            Assert.Throws<LuaException>(() => Run("collectgarbage('bogus')")).Message);

    // ---- weak tables ----

    [Fact]
    public void Weak_values_are_cleared_on_collect_but_strings_and_numbers_stay() =>
        Assert.Equal("nil\tx\t5\n", Run("""
            local a = setmetatable({}, {__mode = "v"})
            a[1] = {}
            a.s = "x"
            a.n = 5
            collectgarbage()
            print(a[1], a.s, a.n)
            """));

    [Fact]
    public void Weak_values_that_are_still_referenced_survive() =>
        Assert.Equal("table\n", Run("""
            local a = setmetatable({}, {__mode = "v"})
            local keep = {}
            a.k = keep
            collectgarbage()
            print(type(a.k))
            """));

    [Fact]
    public void Weak_keys_are_cleared_on_collect_but_other_keys_stay() =>
        Assert.Equal("2\n", Run("""
            local a = setmetatable({}, {__mode = "k"})
            for i = 1, 10 do a[{}] = i end
            a[1] = true
            a.name = true
            collectgarbage()
            local n = 0
            for _ in pairs(a) do n = n + 1 end
            print(n)
            """));

    [Fact]
    public void Weak_key_table_keeps_live_keys_with_their_values() =>
        Assert.Equal("7\n", Run("""
            local a = setmetatable({}, {__mode = "k"})
            local k = {}
            a[k] = 7
            collectgarbage()
            print(a[k])
            """));

    [Fact]
    public void All_weak_table_drops_entries_with_either_part_dead() =>
        Assert.Equal("1\n", Run("""
            local a = setmetatable({}, {__mode = "kv"})
            local x = {}
            a[1] = x
            a[2] = {}
            a[{}] = 3
            a[{}] = {}
            collectgarbage()
            local n = 0
            for _ in pairs(a) do n = n + 1 end
            print(n)
            """));

    [Fact]
    public void Ephemeron_value_referring_to_its_own_key_does_not_keep_it_alive() =>
        Assert.Equal("nil\n", Run("""
            local a = setmetatable({}, {__mode = "k"})
            do local k = {}; a[k] = {k} end
            collectgarbage()
            print(next(a))
            """));

    [Fact]
    public void Ephemeron_chain_stays_alive_while_the_head_is_referenced()
    {
        Assert.Equal("100\tnil\n", Run("""
            local a = setmetatable({}, {__mode = "k"})
            local head
            local x = nil
            for i = 1, 100 do local n = {}; a[n] = {x}; x = n end
            head = x
            collectgarbage()
            local count, n = 0, head
            while n do n = a[n][1]; count = count + 1 end
            head, x, n = nil
            collectgarbage()
            print(count, next(a))
            """));
    }

    [Fact]
    public void Weak_table_remains_usable_after_collection() =>
        Assert.Equal("table\t3\n", Run("""
            local a = setmetatable({}, {__mode = "v"})
            for i = 1, 20 do a[i] = {} end
            collectgarbage()
            local keep = {}
            a[1] = keep; a.x = keep; a[3] = 3
            collectgarbage()
            print(type(a[1]), a[3])
            """));

    // ---- __gc ----

    [Fact]
    public void Gc_runs_on_collect_for_an_unreachable_object() =>
        Assert.Equal("false\ttrue\n", Run("""
            local ran = false
            setmetatable({}, {__gc = function() ran = true end})
            local before = ran
            collectgarbage()
            print(before, ran)
            """));

    [Fact]
    public void Gc_receives_the_object() =>
        Assert.Equal("42\n", Run("""
            local seen
            setmetatable({v = 42}, {__gc = function(o) seen = o.v end})
            collectgarbage()
            print(seen)
            """));

    [Fact]
    public void Gc_is_not_installed_when_the_field_is_absent_at_setmetatable()
    {
        Assert.Equal("false\n", Run("""
            local ran = false
            local mt = {}
            setmetatable({}, mt)
            mt.__gc = function() ran = true end
            collectgarbage()
            print(ran)
            """));
    }

    [Fact]
    public void Gc_runs_in_reverse_order_of_marking() =>
        Assert.Equal("3 2 1\n", Run("""
            local order = {}
            local mt = {__gc = function(o) order[#order + 1] = o.id end}
            for i = 1, 3 do setmetatable({id = i}, mt) end
            collectgarbage()
            print(order[1] .. " " .. order[2] .. " " .. order[3])
            """));

    [Fact]
    public void Gc_runs_once_unless_the_object_is_marked_again() =>
        Assert.Equal("1\t2\n", Run("""
            local n = 0
            local mt = {__gc = function(o) n = n + 1; Saved = o end}
            setmetatable({}, mt)
            collectgarbage()
            local first = n
            collectgarbage()
            local again = n   -- resurrected object finalized only once
            local o = Saved
            Saved = nil
            setmetatable(o, mt)   -- marks it for finalization again
            o = nil
            collectgarbage()
            print(again, n)
            """));

    [Fact]
    public void Gc_error_becomes_a_warning_not_an_error() =>
        Assert.Equal("ok\n", Run("""
            setmetatable({}, {__gc = function() error("boom") end})
            collectgarbage()
            print("ok")
            """));

    [Fact]
    public void Gc_error_message_is_reported_through_the_warning_sink()
    {
        var state = new LuaState { Output = new StringWriter() };
        var warnings = new List<string>();
        state.Gc.Warn = warnings.Add;
        state.DoString("""setmetatable({}, {__gc = function() error("boom") end}); collectgarbage()""");
        Assert.Contains(warnings, w => w.Contains("error in __gc") && w.Contains("boom"));
    }

    [Fact]
    public void Collect_inside_gc_is_not_reentrant() =>
        Assert.Equal("nil\n", Run("""
            local res = true
            setmetatable({}, {__gc = function() res = collectgarbage() end})
            collectgarbage()
            print(res)
            """));

    [Fact]
    public void Gc_runs_for_unreachable_objects_without_an_explicit_collect() =>
        Assert.Equal("true\n", Run("""
            local finish = false
            local u = setmetatable({}, {__gc = function() finish = true end})
            for i = 1, 5000000 do
              u = {}
              if finish then break end
            end
            print(finish)
            """));

    [Fact]
    public void Dispose_runs_remaining_finalizers_in_reverse_marking_order()
    {
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.DoString("""
            local mt = {__gc = function(o) print("gc", o.id) end}
            Keep = {}
            for i = 1, 3 do Keep[i] = setmetatable({id = i}, mt) end
            """);
        Assert.Equal("", output.ToString());
        state.Dispose();
        Assert.Equal("gc\t3\ngc\t2\ngc\t1\n", output.ToString());
    }

    [Fact]
    public void Host_userdata_with_a_gc_metamethod_is_finalized_on_Dispose()
    {
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.DoString("""Mt = {__gc = function(o) print("userdata gc") end}""");
        state.RegisterMetatable<List<int>>((LuaTable)state.GetGlobal("Mt").Ref!);
        state.SetGlobal("U", LuaValue.FromObject(state.CreateUserdata(new List<int>())));
        Assert.Equal("", output.ToString());
        state.Dispose();
        Assert.Equal("userdata gc\n", output.ToString());
    }

    [Fact]
    public void Dispose_swallows_finalizer_errors()
    {
        var state = new LuaState { Output = new StringWriter() };
        state.DoString("""Keep = setmetatable({}, {__gc = function() error("x") end})""");
        state.Dispose();
    }

    [Fact]
    public void Gc_with_a_non_function_field_is_ignored() =>
        Assert.Equal("ok\n", Run("""
            setmetatable({}, {__gc = true})
            collectgarbage()
            print("ok")
            """));
}
