namespace Lunette;

/// <summary>Why a state was aborted.</summary>
public enum LuaAbortKind
{
    /// <summary>The per-call instruction budget ran out.</summary>
    InstructionBudget,
    /// <summary>The cumulative instruction budget across host calls ran out.</summary>
    CumulativeInstructionBudget,
    /// <summary>The memory cap was exceeded.</summary>
    MemoryLimit,
}

/// <summary>
/// Thrown to the host when a budget or memory limit aborts a run. It is deliberately not a <see cref="LuaException"/>:
/// no Lua construct (<c>pcall</c>, <c>xpcall</c>, coroutines, <c>__close</c>, <c>__gc</c>) can catch it. The state is unusable
/// afterwards (<see cref="LuaState.IsAborted"/>) and must be discarded with <see cref="LuaState.Dispose"/>.
/// </summary>
public sealed class LuaAbortException : Exception
{
    internal LuaAbortException(LuaAbortKind kind, string message) : base(message) => Kind = kind;

    /// <summary>Which limit was hit.</summary>
    public LuaAbortKind Kind { get; }
}
