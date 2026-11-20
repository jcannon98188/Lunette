using Xunit;

namespace Lunette.Tests;

/// <summary>Seam: the LuaState host API. A budget or memory abort unwinds straight to the host call and the state is then unusable (ticket 23).</summary>
public class BudgetAbortTests
{
    private static LuaAbortException Aborts(LuaState state, string source) =>
        Assert.Throws<LuaAbortException>(() => state.Call(state.Load(source)));

    private static LuaState Budgeted(long instructions = 5_000) => new(new LuaLimits { InstructionLimit = instructions });

    [Fact]
    public void Pcall_and_xpcall_cannot_catch_an_instruction_abort()
    {
        var state = Budgeted();
        var e = Aborts(state, """
            local ok = pcall(function() while true do end end)
            xpcall(function() while true do end end, function(m) return m end)
            escaped = true
            """);
        Assert.Equal(LuaAbortKind.InstructionBudget, e.Kind);
        Assert.True(state.GetGlobal("escaped").IsNil);
    }

    [Fact]
    public void Pcall_cannot_catch_a_memory_abort()
    {
        var state = new LuaState(new LuaLimits { MemoryLimitBytes = 1_000_000 });
        var e = Aborts(state, "pcall(function() local t = {} for i = 1, 100000000 do t[i] = i end end) escaped = true");
        Assert.Equal(LuaAbortKind.MemoryLimit, e.Kind);
        Assert.True(state.GetGlobal("escaped").IsNil);
    }

    [Fact]
    public void An_abort_inside_a_coroutine_resume_or_wrap_reaches_the_host()
    {
        var state = Budgeted();
        Aborts(state, "local co = coroutine.create(function() while true do end end) coroutine.resume(co) escaped = true");
        Assert.True(state.GetGlobal("escaped").IsNil);
        state = Budgeted();
        Aborts(state, "local f = coroutine.wrap(function() while true do end end) pcall(f) escaped = true");
        Assert.True(state.GetGlobal("escaped").IsNil);
    }

    [Fact]
    public void An_abort_inside_a_close_handler_is_not_swallowed()
    {
        var state = Budgeted();
        Aborts(state, """
            do
              local x <close> = setmetatable({}, {__close = function() while true do end end})
            end
            escaped = true
            """);
        Assert.True(state.GetGlobal("escaped").IsNil);
        state = Budgeted();
        Aborts(state, """
            pcall(function()
              local x <close> = setmetatable({}, {__close = function() while true do end end})
              error('boom')
            end)
            escaped = true
            """);
        Assert.True(state.GetGlobal("escaped").IsNil);
    }

    [Fact]
    public void An_abort_inside_a_gc_finalizer_is_not_swallowed()
    {
        var state = Budgeted(20_000);
        Aborts(state, """
            setmetatable({}, {__gc = function() while true do end end})
            collectgarbage()
            collectgarbage()
            escaped = true
            """);
        Assert.True(state.GetGlobal("escaped").IsNil);
    }

    [Fact]
    public void A_state_is_unusable_after_an_abort_and_can_be_disposed()
    {
        var state = Budgeted();
        Aborts(state, "while true do end");
        Assert.True(state.IsAborted);
        Assert.Equal(LuaAbortKind.InstructionBudget, state.AbortKind);
        Assert.Throws<InvalidOperationException>(() => state.Call(state.Load("return 1")));
        Assert.Throws<InvalidOperationException>(() => state.DoString("return 1"));
        state.Dispose();
        state.Dispose();
    }

    [Fact]
    public void Disposing_an_aborted_state_does_not_run_finalizers()
    {
        var state = Budgeted();
        state.DoString("ran = false setmetatable({}, {__gc = function() ran = true end})");
        state.SetLimits(new LuaLimits { InstructionLimit = 5_000 });
        Aborts(state, "while true do end");
        state.Dispose();
        Assert.True(state.GetGlobal("ran").IsFalsy);
    }

    [Fact]
    public void The_per_call_budget_can_be_set_for_each_host_call()
    {
        var state = new LuaState();
        LuaFunction work = state.Load("local n = 0 for i = 1, 5000 do n = n + 1 end return n");
        state.CallInstructionLimit = 100_000;
        Assert.Equal(5000, state.Call(work).AsInteger);
        state.CallInstructionLimit = 1_000;
        Assert.Equal(LuaAbortKind.InstructionBudget, Aborts(state, "while true do end").Kind);
    }

    [Fact]
    public void The_cumulative_budget_spans_several_calls_until_reset()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 10_000, CumulativeInstructionLimit = 12_000 });
        LuaFunction work = state.Load("for i = 1, 5000 do end");
        state.Call(work);
        state.Call(work);
        Assert.InRange(state.CumulativeInstructionsUsed, 9_000, 11_000);
        state.ResetCumulativeBudget();
        Assert.Equal(0, state.CumulativeInstructionsUsed);
        state.Call(work);
        state.Call(work);
        var e = Assert.Throws<LuaAbortException>(() => state.Call(work));
        Assert.Equal(LuaAbortKind.CumulativeInstructionBudget, e.Kind);
        Assert.True(state.IsAborted);
    }

    [Fact]
    public void Memory_counts_closures_and_coroutines_not_only_tables_and_strings()
    {
        var state = new LuaState(new LuaLimits { MemoryLimitBytes = 2_000_000 });
        var e = Aborts(state, "local keep = {} for i = 1, 1e9 do keep[i % 8] = coroutine.create(function() return i end) end");
        Assert.Equal(LuaAbortKind.MemoryLimit, e.Kind);
    }

    // ---- ticket 23 item 3: the memory count covers frames, stacks and userdata ----

    [Fact]
    public void Memory_counts_call_frames_and_the_stack_they_grow()
    {
        var state = new LuaState(new LuaLimits { MemoryLimitBytes = 300_000 });
        var e = Aborts(state, "local function f(n) local a, b, c = n, n, n return 1 + f(n + 1) end pcall(f, 1) escaped = true");
        Assert.Equal(LuaAbortKind.MemoryLimit, e.Kind);
        Assert.True(state.GetGlobal("escaped").IsNil);
    }

    [Fact]
    public void Memory_counts_a_stack_grown_by_a_vararg_call_and_by_coroutine_stacks()
    {
        var state = new LuaState(new LuaLimits { MemoryLimitBytes = 1_000_000 });
        Assert.Equal(LuaAbortKind.MemoryLimit, Aborts(state, "local t = {} for i = 1, 20000 do t[i] = i end local function f(...) return select('#', ...) end for i = 1, 1e9 do f(table.unpack(t)) end").Kind);
        state = new LuaState(new LuaLimits { MemoryLimitBytes = 1_000_000 });
        Assert.Equal(LuaAbortKind.MemoryLimit, Aborts(state, "local keep = {} for i = 1, 1e9 do local co = coroutine.wrap(function(...) coroutine.yield(...) end) co(1, 2, 3) keep[i % 8] = co end").Kind);
    }

    [Fact]
    public void Memory_counts_userdata_the_host_creates()
    {
        var state = new LuaState(new LuaLimits { MemoryLimitBytes = 1_000_000 });
        state.Register("mk", (ref CallContext ctx) =>
        {
            ctx.Push(LuaValue.FromUserdata(ctx.State.CreateUserdata(new object())));
            return 1;
        });
        var e = Aborts(state, "local keep = {} for i = 1, 1e9 do keep[i % 8] = mk() end escaped = true");
        Assert.Equal(LuaAbortKind.MemoryLimit, e.Kind);
        Assert.True(state.GetGlobal("escaped").IsNil);
    }

    // ---- an abort inside an xpcall message handler and inside coroutine.close ----

    [Fact]
    public void An_abort_inside_an_xpcall_message_handler_reaches_the_host()
    {
        var state = Budgeted();
        var e = Aborts(state, "xpcall(function() error('boom') end, function(m) while true do end end) escaped = true");
        Assert.Equal(LuaAbortKind.InstructionBudget, e.Kind);
        Assert.True(state.GetGlobal("escaped").IsNil);
        state = new LuaState(new LuaLimits { MemoryLimitBytes = 1_000_000 });
        e = Aborts(state, "xpcall(error, function(m) local t = {} for i = 1, 1e9 do t[i] = i end end, 'boom') escaped = true");
        Assert.Equal(LuaAbortKind.MemoryLimit, e.Kind);
        Assert.True(state.GetGlobal("escaped").IsNil);
    }

    [Fact]
    public void An_abort_inside_coroutine_close_is_not_swallowed()
    {
        const string suspended = "local co = coroutine.create(function() local x <close> = setmetatable({}, {__close = function() while true do end end}) coroutine.yield() end) coroutine.resume(co) ";
        var state = Budgeted();
        Aborts(state, suspended + "coroutine.close(co) escaped = true");
        Assert.True(state.GetGlobal("escaped").IsNil);
        state = Budgeted();
        Aborts(state, suspended + "pcall(coroutine.close, co) escaped = true");
        Assert.True(state.GetGlobal("escaped").IsNil);
    }
}
