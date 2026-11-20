using Lunette.Compiler;
using Lunette.Vm;

namespace Lunette;

/// <summary>A callable Lua function value.</summary>
public abstract class LuaFunction
{
    public override string ToString() => "function";
}

internal delegate LuaValue[]? BuiltinBody(LuaState state, ReadOnlySpan<LuaValue> args);

internal sealed class BuiltinFunction(BuiltinBody body) : LuaFunction
{
    public LuaValue[]? Invoke(LuaState state, ReadOnlySpan<LuaValue> args) => body(state, args);
}

/// <summary>A Lua function value: a Proto plus its captured upvalues.</summary>
internal sealed class LuaClosure(Proto proto, UpVal[] upvals) : LuaFunction
{
    public Proto Proto { get; } = proto;
    public UpVal[] Upvals { get; } = upvals;
}
