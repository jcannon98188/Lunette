using Lunette.Compiler;
using Lunette.Vm;

namespace Lunette;

/// <summary>A callable Lua function value.</summary>
public abstract class LuaFunction
{
    public override string ToString() => $"function: 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x8}";
}

internal delegate LuaValue[]? BuiltinBody(LuaState state, ReadOnlySpan<LuaValue> args);

/// <summary>Builtins the interpreter recognises and runs itself, so common error paths need no exception (ADR 0003).</summary>
internal enum BuiltinKind : byte
{
    None,
    Error,
    Assert,
    Pcall,
    Xpcall,
    Yield,
    Pairs,
}

internal sealed class BuiltinFunction(BuiltinBody body, BuiltinKind kind = BuiltinKind.None) : LuaFunction
{
    public BuiltinKind Kind { get; } = kind;

    public LuaValue[]? Invoke(LuaState state, ReadOnlySpan<LuaValue> args) => body(state, args);
}

/// <summary>A Lua function value: a Proto plus its captured upvalues.</summary>
internal sealed class LuaClosure(Proto proto, UpVal[] upvals) : LuaFunction
{
    public Proto Proto { get; } = proto;
    public UpVal[] Upvals { get; } = upvals;
}
