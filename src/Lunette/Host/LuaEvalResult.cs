namespace Lunette;

/// <summary>The outcome of <see cref="LuaState.Evaluate"/>: the values the snippet produced, or the structured error it raised.</summary>
public sealed class LuaEvalResult
{
    internal LuaEvalResult(LuaValue[] values, LuaException? error)
    {
        Values = values;
        Error = error;
    }

    /// <summary>The values returned by the snippet (empty for a statement and on failure).</summary>
    public LuaValue[] Values { get; }

    /// <summary>The compile or runtime error, with chunk, line and <see cref="LuaException.TraceEntries"/>; null on success.</summary>
    public LuaException? Error { get; }

    public bool Succeeded => Error is null;
}
