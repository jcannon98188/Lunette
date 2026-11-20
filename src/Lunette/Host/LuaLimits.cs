namespace Lunette;

/// <summary>
/// Best-effort sandbox limits. Exceeding the instruction or memory limit aborts the run with a <see cref="LuaAbortException"/>
/// that no Lua code can catch; the state is then unusable and must be discarded. The stack depth limit is an ordinary,
/// catchable Lua error. Zero means unlimited.
/// </summary>
public sealed class LuaLimits
{
    /// <summary>
    /// Budget per top-level run (<see cref="LuaState.DoString"/> or a <see cref="LuaState.Call(LuaFunction)"/> from the host).
    /// <see cref="LuaState.CallInstructionLimit"/> overrides it.
    /// <para>
    /// The unit is a tick, not a VM instruction. One tick is charged per backward jump (every loop iteration after the first:
    /// <c>while</c>, <c>repeat</c>, numeric and generic <c>for</c>, <c>goto</c> backwards) and per call of a Lua function, tail calls
    /// included. Straight-line code, forward jumps and calls into host or library functions cost nothing, so a run's work is
    /// bounded only by its loops and calls (a library call such as <c>string.rep</c> does its work in one tick-free step; bound
    /// that with <see cref="MemoryLimitBytes"/>). The count is exact: a run is aborted at the tick that reaches the limit, and a
    /// loop of N iterations costs N - 1 ticks.
    /// </para>
    /// </summary>
    public long InstructionLimit { get; init; }

    /// <summary>
    /// Cap on the instructions (same unit) spent across all top-level runs since the state was made or
    /// <see cref="LuaState.ResetCumulativeBudget"/> was last called; for example a per-tick cap.
    /// </summary>
    public long CumulativeInstructionLimit { get; init; }

    /// <summary>
    /// Approximate cap, per top-level run, on bytes allocated by the running thread (garbage included), which covers every
    /// allocation Lunette makes for the state and any made by host functions on that thread: an upper bound. Checked at the
    /// same points as the instruction budget, and additionally before the large single allocations of <c>string.rep</c>,
    /// <c>table.concat</c> and <c>table.unpack</c>, which abort at the cap instead of allocating. Other allocating library calls
    /// (such as <c>string.gsub</c> with a huge replacement) can still overshoot until the next poll point.
    /// </summary>
    public long MemoryLimitBytes { get; init; }

    /// <summary>Maximum number of nested active Lua calls; exceeding it raises "stack overflow".</summary>
    public int StackDepthLimit { get; init; }
}
