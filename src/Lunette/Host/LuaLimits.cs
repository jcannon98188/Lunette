namespace Lunette;

/// <summary>
/// Best-effort sandbox limits. Exceeding the instruction or memory limit aborts the run with a <see cref="LuaAbortException"/>
/// that no Lua code can catch; the state is then unusable and must be discarded. The stack depth limit is an ordinary,
/// catchable Lua error. Zero means unlimited.
/// </summary>
public sealed class LuaLimits
{
    /// <summary>
    /// Budget per top-level run (<see cref="LuaState.DoString"/> or a <see cref="LuaState.Call(LuaFunction)"/> from the host), counted
    /// in backward jumps and Lua calls rather than individual VM instructions. <see cref="LuaState.CallInstructionLimit"/> overrides it.
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
    /// same points as the instruction budget, so a single huge library call can overshoot it.
    /// </summary>
    public long MemoryLimitBytes { get; init; }

    /// <summary>Maximum number of nested active Lua calls; exceeding it raises "stack overflow".</summary>
    public int StackDepthLimit { get; init; }
}
