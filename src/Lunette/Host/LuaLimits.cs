namespace Lunette;

/// <summary>
/// Best-effort sandbox limits. A script that exceeds one fails with an ordinary Lua error (which, for the budget
/// limits, stays raised: <c>pcall</c> cannot be used to swallow it and carry on). Zero means unlimited.
/// </summary>
public sealed class LuaLimits
{
    /// <summary>
    /// Budget per top-level run (<see cref="LuaState.DoString"/> or a <see cref="LuaState.Call(LuaFunction)"/> from the host), counted
    /// in backward jumps and Lua calls rather than individual VM instructions.
    /// </summary>
    public long InstructionLimit { get; init; }

    /// <summary>
    /// Approximate cap, per top-level run, on bytes allocated by the running thread (garbage included). Checked at the same
    /// points as the instruction budget, so a single huge library call can overshoot it.
    /// </summary>
    public long MemoryLimitBytes { get; init; }

    /// <summary>Maximum number of nested active Lua calls; exceeding it raises "stack overflow".</summary>
    public int StackDepthLimit { get; init; }
}
