namespace Lunette;

/// <summary>A callable Lua function value.</summary>
public abstract class LuaFunction
{
    public override string ToString() => "function";
}

internal sealed class BuiltinFunction(Action<LuaState, LuaValue[]> body) : LuaFunction
{
    public void Invoke(LuaState state, LuaValue[] args) => body(state, args);
}
