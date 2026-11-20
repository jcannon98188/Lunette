namespace Lunette;

/// <summary>
/// A Lua error carrying an arbitrary Lua error value. Throwing is the ergonomic fallback (ADR 0003):
/// the VM catches it outside its dispatch loop and converts it to pending state.
/// </summary>
public sealed class LuaException : Exception
{
    private string _message;

    public LuaException(string message)
    {
        _message = message;
        Value = LuaValue.FromString(message);
    }

    internal LuaException(LuaValue value)
    {
        Value = value;
        _message = value.Ref is byte[] s ? new LuaString(s).ToString() : value.IsNumber ? value.ToString() : $"(error object is a {value.TypeName} value)";
    }

    internal LuaException(string message, Exception inner) : base(message, inner)
    {
        _message = message;
        Value = LuaValue.FromString(message);
    }

    /// <summary>The Lua error value (any Lua value, not only a string).</summary>
    internal LuaValue Value { get; private set; }

    /// <summary>True for library errors that, like luaL_error, get the calling Lua function's position prefixed when first caught.</summary>
    internal bool WantsPosition { get; set; }

    public override string Message => _message;

    /// <summary>The Lua stack traceback captured where an uncaught error was raised, or null.</summary>
    public string? Traceback { get; internal set; }

    /// <summary>
    /// The structured Lua stack captured where an uncaught error was raised, innermost first (null when the error was caught and
    /// rethrown by the host before reaching the VM, empty for a compile error).
    /// </summary>
    public IReadOnlyList<LuaStackFrame>? StackFrames { get; internal set; }

    /// <summary>The chunk (display name) where the error was raised, or null when it has no source position.</summary>
    public string? Chunk => Locate().Chunk;

    /// <summary>The 1-based source line where the error was raised, or 0 when unknown.</summary>
    public int Line => Locate().Line;

    private (string? Chunk, int Line) Locate()
    {
        if (StackFrames is not null)
            foreach (LuaStackFrame f in StackFrames)
                if (!f.IsNative) return (f.Source, f.Line);
        // Compile errors (and errors without frames) carry their position as the message prefix "chunk:line:".
        string m = _message;
        int colon = 0;
        while ((colon = m.IndexOf(':', colon + 1)) > 0)
        {
            int j = colon + 1;
            while (j < m.Length && char.IsAsciiDigit(m[j])) j++;
            if (j > colon + 1 && j < m.Length && m[j] == ':' && int.TryParse(m.AsSpan(colon + 1, j - colon - 1), out int line))
                return (m.Substring(0, colon), line);
        }
        return (null, 0);
    }

    /// <summary>The error value as the standalone interpreter shows it: strings and numbers as they are, other values through <c>__tostring</c> when they have one.</summary>
    public string Describe(LuaState state)
    {
        if (Value.IsString || Value.IsNumber) return Message;
        LuaValue tostring = Vm.Meta.GetMetatable(state, Value)?.Get(LuaValue.FromString("__tostring")) ?? default;
        return tostring.IsNil ? Message : Vm.Meta.ToStringValue(state, Value).ToString();
    }

    internal void Prefix(string where)
    {
        _message = where + " " + _message;
        Value = LuaValue.FromString(_message);
        WantsPosition = false;
    }
}
