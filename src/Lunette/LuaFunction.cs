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

    /// <summary>True for a host closure that owns state (such as a <c>coroutine.wrap</c> function), which a weak table may drop; library functions are never collected.</summary>
    public bool Collectable { get; init; }

    public LuaValue[]? Invoke(LuaState state, ReadOnlySpan<LuaValue> args) => body(state, args);
}

/// <summary>A Lua function value: a Proto plus its captured upvalues.</summary>
internal sealed class LuaClosure(Proto proto, UpVal[] upvals) : LuaFunction
{
    public Proto Proto { get; } = proto;
    public UpVal[] Upvals { get; } = upvals;
}
