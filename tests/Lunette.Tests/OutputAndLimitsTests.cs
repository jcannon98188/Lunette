using Lunette.Cli;
using Xunit;

namespace Lunette.Tests;

/// <summary>Seams: raw byte output (LuaState.OutputStream, the CLI), single-allocation memory pre-checks, the budget unit and Dispose (ticket 28).</summary>
public class OutputAndLimitsTests
{
    // ---- raw byte output ----

    [Fact]
    public void Print_and_io_write_pass_arbitrary_bytes_to_the_output_stream()
    {
        var sink = new MemoryStream();
        var state = new LuaState { OutputStream = sink };
        state.DoString("print('a\\255\\254b', 7) io.write('\\128\\0\\200', 1.5, '\\n') io.stdout:write('\\xC3') io.stdout:write('\\xA4')");
        byte[] expected = [(byte)'a', 255, 254, (byte)'b', (byte)'\t', (byte)'7', (byte)'\n', 128, 0, 200, (byte)'1', (byte)'.', (byte)'5', (byte)'\n', 0xC3, 0xA4];
        Assert.Equal(expected, sink.ToArray());
    }

    [Fact]
    public void The_cli_round_trips_non_utf8_bytes_through_print_and_io_write()
    {
        var sink = new MemoryStream();
        var err = new StringWriter();
        int code = Program.Run(["-e", "print('x\\255\\254\\0y') io.write('\\200\\201')"], sink, err);
        Assert.Equal(0, code);
        Assert.Equal([(byte)'x', 255, 254, 0, (byte)'y', (byte)'\n', 200, 201], sink.ToArray());
    }

    // ---- single huge library allocations ----

    [Theory]
    [InlineData("return string.rep('x', 1e9)")]
    [InlineData("return string.rep('abc', 3e8, ',')")]
    [InlineData("return #table.concat(setmetatable({}, {__len = function() return 1e9 end, __index = function() return string.rep('x', 1000) end}))")]
    [InlineData("return table.unpack({}, 1, 900000)")]
    public void A_huge_single_allocation_aborts_at_the_cap_before_allocating(string source)
    {
        var state = new LuaState(new LuaLimits { MemoryLimitBytes = 4_000_000, InstructionLimit = 10_000_000 });
        LuaFunction f = state.Load(source);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var e = Assert.Throws<LuaAbortException>(() => state.Call(f));
        long used = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(LuaAbortKind.MemoryLimit, e.Kind);
        Assert.True(used < 40_000_000, $"allocated {used} bytes before aborting");
    }

    [Fact]
    public void An_allocation_inside_the_cap_still_succeeds()
    {
        var state = new LuaState(new LuaLimits { MemoryLimitBytes = 4_000_000 });
        Assert.Equal(1000, state.Call(state.Load("return #string.rep('x', 1000)")).AsInteger);
    }

    // ---- the budget unit ----

    /// <summary>The unit is one tick: a backward jump (every loop iteration) or a Lua call (tail calls included). Nothing else is charged.</summary>
    [Fact]
    public void The_budget_unit_is_one_per_loop_iteration_and_one_per_lua_call()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 1_000_000 });
        state.Call(state.Load("for i = 1, 5000 do end"));
        Assert.Equal(4999, state.CumulativeInstructionsUsed); // N iterations cost N - 1: the first pass is entered, not jumped to
        state.ResetCumulativeBudget();
        state.Call(state.Load("local function f() end for i = 1, 1000 do f() end"));
        Assert.Equal(1999, state.CumulativeInstructionsUsed); // 999 backward jumps + 1000 calls
    }

    [Fact]
    public void Straight_line_code_and_builtin_calls_are_free()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 1 });
        state.Call(state.Load("local t = {} t[1] = 1 t[2] = #t + 2 local s = string.rep('a', 10) return math.max(1, 2), s:upper()"));
        Assert.Equal(0, state.CumulativeInstructionsUsed);
    }

    [Fact]
    public void A_run_aborts_exactly_when_the_limit_is_reached()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 3000 });
        Assert.Throws<LuaAbortException>(() => state.Call(state.Load("n = 0 while true do n = n + 1 end")));
        Assert.Equal(3000, state.GetGlobal("n").AsInteger);
    }

    // ---- Dispose ----

    [Fact]
    public void Dispose_closes_pending_tbc_variables_left_by_an_exit_and_is_idempotent()
    {
        var state = new LuaState();
        state.DoString("log = 0");
        try
        {
            state.DoString("local x <close> = setmetatable({}, {__close = function() log = log + 1 end}) os.exit(0)");
        }
        catch (LuaExitException) { }
        Assert.Equal(0, state.GetGlobal("log").AsInteger);
        state.Dispose();
        state.Dispose();
        Assert.Equal(1, state.GetGlobal("log").AsInteger);
    }

    [Fact]
    public void Dispose_of_a_cleanly_finished_state_runs_no_extra_close_handlers_and_leaves_suspended_coroutines_alone()
    {
        var state = new LuaState();
        state.DoString("""
            log = 0
            do local x <close> = setmetatable({}, {__close = function() log = log + 1 end}) end
            co = coroutine.wrap(function() local y <close> = setmetatable({}, {__close = function() log = log + 10 end}) coroutine.yield() end)
            co()
            """);
        Assert.Equal(1, state.GetGlobal("log").AsInteger);
        state.Dispose();
        state.Dispose();
        Assert.Equal(1, state.GetGlobal("log").AsInteger);
    }
}
