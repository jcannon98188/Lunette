using Xunit;

namespace Lunette.Tests;

/// <summary>
/// SimGame dev mode: structured error locations and traces, console evaluation inside a live state, and cheap
/// discard-and-recreate. The seam is the public host API (LuaState, LuaException, LuaEvalResult) with the debug library withheld.
/// </summary>
public class DevDiagnosticsTests
{
    private static LuaState NewState() => new(new LuaStateOptions().Allow("_G", "print", "pcall", "error", "string", "table", "require", "setmetatable")
        .AddModule("util", s => { var t = new LuaTable(); t.Set(LuaValue.FromString("triple"), LuaValue.FromFunction(s.CreateFunction((ref CallContext c) => { c.Push(c.Args[0].AsInteger * 3); return 1; }))); return LuaValue.FromTable(t); }));

    private static LuaException Catch(LuaState state, string source, string chunk)
    {
        LuaFunction f = state.Load(source, chunk);
        return Assert.Throws<LuaException>(() => state.Call(f));
    }

    // ---- 1. error locations and structured traces ----

    [Fact]
    public void A_runtime_error_carries_chunk_line_and_function_frames_without_the_debug_library()
    {
        using var state = NewState();
        Assert.True(state.GetGlobal("debug").IsNil);
        const string src = "local function inner()\n  error('boom')\nend\nfunction outer()\n  inner()\nend\nouter()";
        LuaException e = Catch(state, src, "=mods/farm.lua");

        Assert.Equal("mods/farm.lua", e.Chunk);
        Assert.Equal(2, e.Line);
        Assert.Contains("boom", e.Message);

        var frames = e.StackFrames;
        Assert.NotNull(frames);
        Assert.Equal("error", frames![0].FunctionName);
        Assert.True(frames[0].IsNative);
        Assert.Equal("inner", frames[1].FunctionName);
        Assert.Equal("mods/farm.lua", frames[1].Source);
        Assert.Equal(2, frames[1].Line);
        Assert.Equal("outer", frames[2].FunctionName);
        Assert.Equal(5, frames[2].Line);
        Assert.Null(frames[^1].FunctionName);
        Assert.Equal(7, frames[^1].Line);
        Assert.False(frames[^1].IsNative);
    }

    [Fact]
    public void A_vm_detected_error_reports_the_faulting_line()
    {
        using var state = NewState();
        LuaException e = Catch(state, "local t\n\nlocal x = t.field", "=mods/a.lua");
        Assert.Equal("mods/a.lua", e.Chunk);
        Assert.Equal(3, e.Line);
        Assert.Equal(3, e.StackFrames![0].Line);
    }

    [Fact]
    public void A_syntax_error_carries_chunk_and_line_but_no_frames()
    {
        using var state = NewState();
        LuaException e = Assert.Throws<LuaException>(() => state.Load("local x = 1\nlocal = 2", "=mods/bad.lua"));
        Assert.Equal("mods/bad.lua", e.Chunk);
        Assert.Equal(2, e.Line);
        Assert.Empty(e.StackFrames ?? []);
    }

    [Fact]
    public void An_error_raised_by_a_host_function_still_locates_the_calling_lua_line()
    {
        using var state = NewState();
        state.Register("fail", (ref CallContext _) => throw new LuaException("host says no"));
        LuaException e = Catch(state, "local a = 1\nfail()", "=mods/h.lua");
        Assert.Equal(2, e.Line);
        Assert.Equal("mods/h.lua", e.Chunk);
    }

    // ---- 2. console evaluation ----

    [Fact]
    public void Evaluate_returns_the_values_of_an_expression_using_live_globals()
    {
        using var state = NewState();
        state.DoString("gold = 40");
        LuaEvalResult r = state.Evaluate("gold + 2, 'ok'");
        Assert.True(r.Succeeded);
        Assert.Null(r.Error);
        Assert.Equal(2, r.Values.Length);
        Assert.Equal(42, r.Values[0].AsInteger);
        Assert.True(r.Values[1].TryGetString(out string? s));
        Assert.Equal("ok", s);
    }

    [Fact]
    public void Evaluate_runs_statements_that_mutate_the_live_state()
    {
        using var state = NewState();
        LuaEvalResult r = state.Evaluate("gold = 7");
        Assert.True(r.Succeeded);
        Assert.Empty(r.Values);
        Assert.Equal(7, state.GetGlobal("gold").AsInteger);
    }

    [Fact]
    public void Evaluate_sees_modules_the_pack_can_require()
    {
        using var state = NewState();
        LuaEvalResult r = state.Evaluate("require('util').triple(14)");
        Assert.True(r.Succeeded);
        Assert.Equal(42, r.Values[0].AsInteger);
    }

    [Fact]
    public void A_module_loader_can_run_lua_through_DoString_and_Evaluate_while_require_is_running()
    {
        using var state = new LuaState(new LuaStateOptions().Allow("_G", "print", "pcall", "error", "require")
            .AddModule("boot", s =>
            {
                s.DoString("booted = 20 + 1");
                LuaEvalResult inner = s.Evaluate("booted * 2");
                Assert.True(inner.Succeeded);
                var t = new LuaTable();
                t.Set(LuaValue.FromString("answer"), inner.Values[0]);
                return LuaValue.FromTable(t);
            }));
        LuaEvalResult r = state.Evaluate("return require('boot').answer, booted");
        Assert.True(r.Succeeded, r.Error?.Message);
        Assert.Equal(42, r.Values[0].AsInteger);
        Assert.Equal(21, r.Values[1].AsInteger);
    }

    [Fact]
    public void A_host_function_called_from_a_required_chunk_can_call_DoString()
    {
        LuaState? holder = null;
        var options = new LuaStateOptions().Allow("_G", "require")
            .AddModule("mod", s =>
            {
                // A Lua chunk running under require calls a host function that re-enters the state.
                LuaFunction chunk = s.Load("hostrun() hostrun() return viaHost", "=mods/nested.lua");
                return s.Call(chunk);
            });
        using var state = new LuaState(options);
        holder = state;
        state.Register("hostrun", (ref CallContext _) => { holder.DoString("viaHost = (viaHost or 0) + 1"); return 0; });
        LuaEvalResult r = state.Evaluate("return require('mod')");
        Assert.True(r.Succeeded, r.Error?.Message);
        Assert.Equal(2, r.Values[0].AsInteger);
    }

    [Fact]
    public void Evaluate_returns_a_structured_error_instead_of_throwing_and_the_state_stays_usable()
    {
        using var state = NewState();
        LuaEvalResult r = state.Evaluate("nothing.here");
        Assert.False(r.Succeeded);
        Assert.NotNull(r.Error);
        Assert.Equal("=(console)", "=" + r.Error!.Chunk);
        Assert.Equal(1, r.Error.Line);
        Assert.Contains("nothing", r.Error.Message);
        Assert.NotEmpty(r.Error.StackFrames!);
        Assert.Equal(3, state.Evaluate("1 + 2").Values[0].AsInteger);
    }

    [Fact]
    public void Evaluate_reports_a_syntax_error_structurally()
    {
        using var state = NewState();
        LuaEvalResult r = state.Evaluate("local = ", "=console#4");
        Assert.False(r.Succeeded);
        Assert.Equal("console#4", r.Error!.Chunk);
    }

    [Fact]
    public void Evaluate_honours_the_instruction_budget()
    {
        using var state = new LuaState(new LuaStateOptions { Limits = new LuaLimits { InstructionLimit = 10_000 } }.Allow("_G"));
        Assert.Throws<LuaAbortException>(() => state.Evaluate("while true do end"));
        Assert.True(state.IsAborted);
    }

    // ---- 3. discard and recreate ----

    [Fact]
    public void Dispose_runs_pending_gc_finalizers_so_pack_resources_are_released()
    {
        int released = 0;
        var state = NewState();
        state.Register("note", (ref CallContext _) => { released++; return 0; });
        state.DoString("setmetatable({}, {__gc = function() note() end}) keep = setmetatable({}, {__gc = function() note() end})");
        state.Dispose();
        Assert.Equal(2, released);
        state.Dispose();
        Assert.Equal(2, released);
    }

    [Fact]
    public void Repeated_create_and_dispose_cycles_leak_nothing()
    {
        const int Cycles = 300;
        int finalized = 0;
        var refs = new List<WeakReference>();
        for (int i = 0; i < Cycles; i++) refs.Add(Cycle(ref finalized));
        Assert.Equal(Cycles, finalized);
        Collect();
        int alive = 0;
        foreach (WeakReference w in refs) if (w.IsAlive) alive++;
        Assert.Equal(0, alive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Cycle(ref int finalized)
    {
        int count = 0;
        var state = NewState();
        state.Register("note", (ref CallContext _) => { count++; return 0; });
        state.DoString("local w = setmetatable({}, {__mode = 'k'}) w[{}] = 1 big = {} for i = 1, 100 do big[i] = {i} end G = setmetatable({}, {__gc = function() note() end})");
        state.Evaluate("big[1]");
        state.Dispose();
        finalized += count;
        return new WeakReference(state);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
