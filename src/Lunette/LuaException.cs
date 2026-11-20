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
        _message = value.Ref is string s ? s : value.IsNumber ? value.ToString() : $"(error object is a {value.TypeName} value)";
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

    internal void Prefix(string where)
    {
        _message = where + " " + _message;
        Value = LuaValue.FromString(_message);
        WantsPosition = false;
    }
}
