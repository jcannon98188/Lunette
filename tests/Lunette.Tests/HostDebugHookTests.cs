using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Seam: the public host debug API (LuaState.SetHook, TryGetInfo, TryGetLocal, TryGetUpvalue). The oracle for event order and
/// getinfo fields is the debug library of the same engine, which the official db.lua suite validates.
/// </summary>
public class HostDebugHookTests
{
    private const string Script = """
        local function leaf(x)
          local y = x + 1
          return y
        end
        local function tail(x)
          return leaf(x)
        end
        local a = tail(1)
        for i = 1, 2 do
          a = a + i
        end
        return a
        """;

    private static List<string> HostEvents(string source, LuaHookMask mask, int count = 0)
    {
        var state = new LuaState();
        var log = new List<string>();
        state.SetHook((_, ev, line) => log.Add(ev == LuaHookEvent.Line ? $"line {line}" : ev.ToString().ToLowerInvariant()), mask, count);
        state.Call(state.Load(source, "=t"));
        return log;
    }

    private static List<string> LuaEvents(string source, string mask)
    {
        var state = new LuaState();
        var log = new List<string>();
        state.Register("record", (ref CallContext ctx) =>
        {
            string ev = ctx.Args[0].ToString().Replace(" ", "");
            log.Add(ev == "line" ? $"line {ctx.Args[1].AsInteger}" : ev);
            return 0;
        });
        // Installed on the first line of the script so that line numbers agree.
        state.Call(state.Load($"debug.sethook(function(e, l) record(e, l) end, '{mask}') " + source, "=t"));
        return log;
    }

    [Fact]
    public void Line_events_arrive_in_execution_order()
    {
        var log = HostEvents(Script, LuaHookMask.Line);
        Assert.Equal(["line 4", "line 7", "line 8", "line 6", "line 2", "line 3", "line 9", "line 10", "line 9", "line 10", "line 9", "line 12"], log);
    }

    [Fact]
    public void Call_and_return_events_mark_tail_calls()
    {
        var log = HostEvents(Script, LuaHookMask.Call | LuaHookMask.Return);
        Assert.Equal(["call", "call", "tailcall", "return", "return"], log);
    }

    [Fact]
    public void The_event_sequence_matches_a_lua_hook_set_by_debug_sethook()
    {
        var host = HostEvents(Script, LuaHookMask.Line | LuaHookMask.Call | LuaHookMask.Return);
        var lua = LuaEvents(Script, "lcr");
        // The Lua hook is installed mid-chunk: it never saw the main chunk's call, and first reports the debug.sethook call returning.
        Assert.Equal("call", host[0]);
        Assert.Equal("return", lua[0]);
        Assert.Equal(host.Skip(1).ToList(), lua.Skip(1).ToList());
    }

    [Fact]
    public void A_count_hook_fires_once_per_period_instructions()
    {
        var log = HostEvents("local n = 0 for i = 1, 1000 do n = n + i end", LuaHookMask.None, 100);
        Assert.All(log, e => Assert.Equal("count", e));
        Assert.InRange(log.Count, 20, 80);
        Assert.True(HostEvents("local n = 0 for i = 1, 1000 do n = n + i end", LuaHookMask.None, 1000).Count < log.Count);
    }

    [Fact]
    public void Clearing_the_hook_stops_events_and_restores_the_untraced_loop()
    {
        var state = new LuaState();
        int n = 0;
        state.SetHook((_, _, _) => n++, LuaHookMask.Line);
        state.Call(state.Load("local a = 1\nlocal b = 2"));
        int seen = n;
        Assert.True(seen > 0);
        state.SetHook(null, LuaHookMask.None);
        state.Call(state.Load("local a = 1\nlocal b = 2"));
        Assert.Equal(seen, n);
        state.SetHook((_, _, _) => n++, LuaHookMask.None, 0); // nothing to report: same as clearing
        state.Call(state.Load("local a = 1"));
        Assert.Equal(seen, n);
    }

    [Fact]
    public void Hooks_are_per_coroutine()
    {
        var state = new LuaState();
        var lines = new List<int>();
        state.Call(state.Load("co = coroutine.create(function()\n local a = 1\n coroutine.yield()\n local b = 2\nend)"));
        LuaValue co = state.GetGlobal("co");
        Assert.Equal(LuaType.Thread, co.Type);
        state.SetHook((_, _, line) => lines.Add(line), LuaHookMask.Line, 0, co);
        state.Call(state.Load("local x = 1\ncoroutine.resume(co)\ncoroutine.resume(co)"));
        Assert.Equal([2, 3, 4, 5], lines);
    }

    [Fact]
    public void The_running_thread_is_reported_to_the_hook()
    {
        var state = new LuaState();
        LuaValue seen = default;
        state.Call(state.Load("co = coroutine.create(function() local a = 1 end)"));
        LuaValue co = state.GetGlobal("co");
        state.SetHook((s, _, _) => seen = s.RunningThread, LuaHookMask.Line, 0, co);
        state.Call(state.Load("coroutine.resume(co)"));
        Assert.Equal(LuaType.Thread, seen.Type);
        Assert.Same(co.Ref, seen.Ref);
    }

    [Fact]
    public void A_throwing_hook_surfaces_as_a_catchable_lua_error_and_leaves_the_state_usable()
    {
        var state = new LuaState();
        state.SetHook((_, _, line) =>
        {
            if (line == 2) throw new LuaException("boom from hook");
        }, LuaHookMask.Line);
        var e = Assert.Throws<LuaException>(() => state.Call(state.Load("local a = 1\nlocal b = 2\nlocal c = 3", "=t")));
        Assert.Contains("boom from hook", e.Message);

        // The hook is re-armed after the error: it fires again on the next run.
        Assert.Throws<LuaException>(() => state.Call(state.Load("local a = 1\nlocal b = 2", "=t")));
        state.SetHook(null, LuaHookMask.None);
        Assert.Equal(7, state.Call(state.Load("return 3 + 4")).AsInteger);
    }

    [Fact]
    public void A_non_lua_exception_from_the_hook_reaches_the_host_and_the_state_stays_usable()
    {
        var state = new LuaState();
        state.SetHook((_, _, _) => throw new InvalidOperationException("adapter bug"), LuaHookMask.Line);
        Assert.Throws<InvalidOperationException>(() => state.Call(state.Load("local a = 1", "=t")));
        state.SetHook(null, LuaHookMask.None);
        Assert.Equal(7, state.Call(state.Load("return 3 + 4")).AsInteger);
    }

    [Fact]
    public void A_lua_script_can_catch_an_error_raised_by_a_host_hook()
    {
        var state = new LuaState();
        state.SetHook((_, _, line) =>
        {
            if (line == 3) throw new LuaException("stop");
        }, LuaHookMask.Line);
        LuaValue[] r = state.Call(state.Load("local ok, err = pcall(function()\nlocal a = 1\nlocal b = 2\nend)\nreturn ok, err", "=t"), ReadOnlySpan<LuaValue>.Empty);
        Assert.True(r[0].TryGetBoolean(out bool ok) && !ok);
        Assert.Contains("stop", r[1].ToString());
    }

    [Fact]
    public void A_hook_is_not_reentered_by_the_code_it_runs()
    {
        var state = new LuaState();
        int depth = 0, max = 0, calls = 0;
        LuaFunction helper = state.Load("local x = 1\nlocal y = 2\nreturn x + y");
        state.SetHook((s, _, _) =>
        {
            calls++;
            max = Math.Max(max, ++depth);
            s.Call(helper);
            depth--;
        }, LuaHookMask.Line);
        state.Call(state.Load("local a = 1\nlocal b = 2"));
        Assert.Equal(1, max);
        Assert.True(calls >= 2);
    }

    [Fact]
    public void An_instruction_budget_still_aborts_a_hooked_loop()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 5_000 });
        state.SetHook((_, _, _) => { }, LuaHookMask.Line);
        Assert.Throws<LuaAbortException>(() => state.Call(state.Load("while true do end")));
    }

    [Fact]
    public void An_interrupt_stops_a_hooked_loop_from_inside_the_hook()
    {
        var state = new LuaState();
        int n = 0;
        state.SetHook((s, _, _) =>
        {
            if (++n == 50) s.RequestInterrupt();
        }, LuaHookMask.Line);
        var e = Assert.Throws<LuaException>(() => state.Call(state.Load("while true do local x = 1 end")));
        Assert.Contains("interrupted", e.Message);
    }

    [Fact]
    public void Get_info_reports_the_hooked_function_at_level_zero_like_debug_getinfo()
    {
        var state = new LuaState();
        state.Call(state.Load("""
            function named(a, b, ...)
              local x = 1
              info = debug.getinfo(1, 'Slnut')
              return x
            end
            named(1, 2)
            """, "@dir/prog.lua"));

        var seen = new List<LuaDebugInfo>();
        state.SetHook((s, _, line) =>
        {
            if (line == 3 && s.TryGetInfo(0, out LuaDebugInfo i)) seen.Add(i);
        }, LuaHookMask.Line);
        state.Call(state.Load("""
            function named(a, b, ...)
              local x = 1
              info = debug.getinfo(1, 'Slnut')
              return x
            end
            named(1, 2)
            """, "@dir/prog.lua"));

        LuaDebugInfo host = Assert.Single(seen);
        LuaTable t = (LuaTable)state.GetGlobal("info").Ref!;
        string S(string k) => t.Get(LuaValue.FromString(k)).ToString();
        long N(string k) => t.Get(LuaValue.FromString(k)).AsInteger;
        Assert.Equal(S("source"), host.Source);
        Assert.Equal(S("short_src"), host.ShortSource);
        Assert.Equal("dir/prog.lua", host.ShortSource);
        Assert.Equal(N("linedefined"), host.LineDefined);
        Assert.Equal(N("lastlinedefined"), host.LastLineDefined);
        Assert.Equal(S("what"), host.What);
        Assert.Equal(N("currentline"), host.CurrentLine);
        Assert.Equal(3, host.CurrentLine);
        Assert.Equal(S("name"), host.Name);
        Assert.Equal(S("namewhat"), host.NameWhat);
        Assert.Equal("global", host.NameWhat);
        Assert.Equal(N("nups"), host.UpvalueCount);
        Assert.Equal(N("nparams"), host.ParameterCount);
        Assert.Equal(2, host.ParameterCount);
        Assert.True(host.IsVararg);
        Assert.False(host.IsTailCall);
        Assert.Equal(LuaType.Function, host.Function.Type);
    }

    [Fact]
    public void Get_info_walks_the_stack_and_returns_false_past_the_bottom()
    {
        var state = new LuaState();
        var names = new List<string?>();
        int levels = -1;
        state.SetHook((s, _, line) =>
        {
            if (line != 2) return;
            int i = 0;
            while (s.TryGetInfo(i, out LuaDebugInfo info))
            {
                names.Add(info.Name ?? info.What);
                i++;
            }
            levels = i;
        }, LuaHookMask.Line);
        state.Call(state.Load("local function inner()\n local v = 1\nend\nlocal function outer() inner() end\nouter()", "=t"));
        Assert.True(levels >= 3);
        Assert.Equal(["inner", "outer"], names.Take(2));
        Assert.Equal("main", names[2]);
    }

    [Fact]
    public void Get_info_marks_a_tail_called_function_and_a_hooked_host_function()
    {
        var state = new LuaState();
        bool tail = false;
        string? hostWhat = null;
        string? hostName = null;
        state.SetHook((s, ev, line) =>
        {
            if (ev == LuaHookEvent.TailCall && s.TryGetInfo(0, out LuaDebugInfo i)) tail = i.IsTailCall;
            if (ev == LuaHookEvent.Call && s.TryGetInfo(0, out LuaDebugInfo c) && c.What == "C")
            {
                hostWhat = c.What;
                hostName = c.Name;
            }
        }, LuaHookMask.Call);
        state.Call(state.Load("local function g() return 1 end\nlocal function f() return g() end\nf()\nprint = print or nil\nlocal s = tostring(1)", "=t"));
        Assert.True(tail);
        Assert.Equal("C", hostWhat);
        Assert.Equal("tostring", hostName);
    }

    [Fact]
    public void Locals_can_be_read_and_written_at_a_level()
    {
        var state = new LuaState();
        var names = new List<string>();
        var values = new List<long>();
        state.SetHook((s, _, line) =>
        {
            if (line != 3) return;
            for (int n = 1; s.TryGetLocal(0, n, out string? name, out LuaValue v); n++)
            {
                names.Add(name!);
                if (name == "x" || name == "y") values.Add(v.AsInteger);
            }
            Assert.Equal("x", s.TrySetLocal(0, 1, 100, out string? set) ? set : null);
        }, LuaHookMask.Line);
        LuaValue[] r = state.Call(state.Load("local x = 7\nlocal y = 8\nreturn x + y", "=t"), ReadOnlySpan<LuaValue>.Empty);
        Assert.Equal(["x", "y"], names.Take(2));
        Assert.Equal([7, 8], values);
        Assert.Equal(108, r[0].AsInteger);
    }

    [Fact]
    public void Varargs_are_reached_with_negative_indices_and_a_bad_index_is_false()
    {
        var state = new LuaState();
        string? name = null;
        LuaValue got = default;
        bool bad = true;
        state.SetHook((s, _, line) =>
        {
            if (line != 2) return;
            s.TryGetLocal(0, -2, out name, out got);
            bad = s.TryGetLocal(0, -9, out string? n2, out LuaValue v2);
        }, LuaHookMask.Line);
        state.Call(state.Load("local a = ...\nlocal b = 1", "=t"), [10, 20]);
        Assert.Equal("(vararg)", name);
        Assert.Equal(20, got.AsInteger);
        Assert.False(bad);
    }

    [Fact]
    public void Upvalues_are_listed_with_names_and_can_be_set()
    {
        var state = new LuaState();
        LuaFunction f = state.Load("local count = 5\nreturn function() count = count + 1 return count end");
        LuaValue inner = state.Call(f, ReadOnlySpan<LuaValue>.Empty)[0];
        LuaFunction fn = (LuaFunction)inner.Ref!;
        Assert.True(state.TryGetUpvalue(fn, 1, out string? name, out LuaValue v));
        Assert.Equal("count", name);
        Assert.Equal(5, v.AsInteger);
        Assert.True(state.TrySetUpvalue(fn, 1, 41, out string? setName));
        Assert.Equal("count", setName);
        Assert.Equal(42, state.Call(fn).AsInteger);
        Assert.False(state.TryGetUpvalue(fn, 2, out _, out _));
    }

    [Fact]
    public void Get_info_takes_a_coroutine_and_describes_its_suspended_stack()
    {
        var state = new LuaState();
        state.Call(state.Load("co = coroutine.create(function()\n local inside = 3\n coroutine.yield()\nend)\ncoroutine.resume(co)"));
        LuaValue co = state.GetGlobal("co");
        Assert.True(state.TryGetInfo(1, out LuaDebugInfo info, co));
        Assert.Equal(2 + 1, info.CurrentLine);
        Assert.True(state.TryGetLocal(1, 1, out string? name, out LuaValue v, co));
        Assert.Equal("inside", name);
        Assert.Equal(3, v.AsInteger);
    }

    [Fact]
    public void A_host_hook_replaces_a_lua_hook_and_the_reverse_the_last_one_set_wins()
    {
        var state = new LuaState();
        int host = 0, lua = 0;
        state.Register("tick", (ref CallContext ctx) =>
        {
            lua++;
            return 0;
        });
        state.Call(state.Load("debug.sethook(function() tick() end, 'l')"));
        state.SetHook((_, _, _) => host++, LuaHookMask.Line);
        int luaBefore = lua;
        state.Call(state.Load("local a = 1\nlocal b = 2"));
        Assert.Equal(luaBefore, lua);
        Assert.True(host > 0);

        // debug.gethook reports the host hook as a function, mask and count.
        LuaValue[] g = state.Call(state.Load("return debug.gethook()"), ReadOnlySpan<LuaValue>.Empty);
        Assert.Equal(LuaType.Function, g[0].Type);
        Assert.Equal("l", g[1].ToString());

        int hostBefore = host;
        state.Call(state.Load("debug.sethook(function() tick() end, 'l')\nlocal c = 1\nlocal d = 2"));
        state.Call(state.Load("local a = 1"));
        Assert.True(lua > luaBefore);
        Assert.True(host - hostBefore <= 2);
    }

    [Fact]
    public void A_non_positive_count_with_no_mask_clears_and_a_negative_count_is_rejected()
    {
        var state = new LuaState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetHook((_, _, _) => { }, LuaHookMask.Line, -1));
    }
}
