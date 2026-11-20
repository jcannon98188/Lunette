using Lunette.Compiler;
using Lunette.Vm;

namespace Lunette;

/// <summary>A callable Lua function value.</summary>
public abstract class LuaFunction
{
    /// <summary>Only the engine creates function values.</summary>
    internal LuaFunction()
    {
    }

    public override string ToString() => "function";
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
    Collect,
    SetHook,
    /// <summary>A host function made with LuaState.CreateFunction: it may request a yield.</summary>
    Host,
}

internal sealed class BuiltinFunction(BuiltinBody body, BuiltinKind kind = BuiltinKind.None) : LuaFunction
{
    public BuiltinKind Kind { get; } = kind;

    /// <summary>The host delegate behind a <see cref="BuiltinKind.Host"/> function.</summary>
    public HostFunction? Host { get; init; }

    public LuaValue[]? Invoke(LuaState state, ReadOnlySpan<LuaValue> args) => body(state, args);
}

/// <summary>A Lua function value: a Proto plus its captured upvalues.</summary>
internal sealed class LuaClosure(Proto proto, UpVal[] upvals) : LuaFunction
{
    public Proto Proto { get; } = proto;
    public UpVal[] Upvals { get; } = upvals;
}
