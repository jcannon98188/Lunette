using Xunit;

namespace Lunette.Tests;

/// <summary>Best-effort sandbox limits: a script over a limit fails with a Lua error.</summary>
public class SandboxLimitTests
{
    private static LuaException Fails(LuaState state, string source) =>
        Assert.Throws<LuaException>(() => state.Call(state.Load(source)));

    [Fact]
    public void An_endless_loop_exceeds_the_instruction_limit()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 10_000 });
        Assert.Contains("instruction limit exceeded", Fails(state, "while true do end").Message);
    }

    [Fact]
    public void A_script_within_the_budget_runs_and_each_top_level_call_gets_a_fresh_budget()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 10_000 });
        LuaFunction work = state.Load("local n = 0 for i = 1, 5000 do n = n + i end return n");
        for (int run = 0; run < 5; run++) Assert.Equal(12_502_500, state.Call(work).AsInteger);
    }

    [Fact]
    public void A_for_loop_and_a_tail_call_loop_are_both_counted()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 10_000 });
        Assert.Contains("instruction limit", Fails(state, "for i = 1, math.maxinteger do end").Message);
        Assert.Contains("instruction limit", Fails(state, "local function f() return f() end return f()").Message);
    }

    [Fact]
    public void Pcall_cannot_swallow_an_exhausted_budget_and_carry_on()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 5_000 });
        Fails(state, """
            pcall(function() while true do end end)
            for i = 1, 10 do end
            return 'escaped'
            """);
    }

    [Fact]
    public void Deep_recursion_exceeds_the_stack_depth_limit_but_shallow_recursion_does_not()
    {
        var state = new LuaState(new LuaLimits { StackDepthLimit = 50 });
        LuaFunction depth = state.Load("local function f(n) if n == 0 then return 0 end return 1 + f(n - 1) end return f(...)");
        Assert.Equal(20, state.Call(depth, 20).AsInteger);
        Assert.Contains("stack overflow", Assert.Throws<LuaException>(() => state.Call(depth, 1000)).Message);
    }

    [Fact]
    public void Allocating_past_the_memory_limit_fails()
    {
        var state = new LuaState(new LuaLimits { MemoryLimitBytes = 1_000_000 });
        Assert.Contains("memory limit exceeded", Fails(state, "local t = {} for i = 1, 100000000 do t[i] = i end").Message);
    }

    [Fact]
    public void Another_thread_can_interrupt_a_running_script()
    {
        var state = new LuaState();
        var worker = new Thread(() =>
        {
            Thread.Sleep(50);
            state.RequestInterrupt();
        });
        worker.Start();
        Assert.Contains("interrupted", Fails(state, "while true do end").Message);
        worker.Join();
        state.ClearInterrupt();
        Assert.Equal(1, state.Call(state.Load("return 1")).AsInteger);
    }

    [Fact]
    public void An_unlimited_state_is_unaffected()
    {
        var state = new LuaState();
        Assert.Equal(3_000_000, state.Call(state.Load("local n = 0 for i = 1, 3000000 do n = n + 1 end return n")).AsInteger);
    }
}
