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
    /// <summary><c>dofile</c>: the loaded chunk runs as an ordinary frame so that it can yield.</summary>
    DoFile,
    /// <summary>A host function made with LuaState.CreateFunction: it may request a yield.</summary>
    Host,
    /// <summary><c>next</c>: the interpreter steps a plain table without going through the builtin's argument span and result array.</summary>
    Next,
    /// <summary>The iterator <c>ipairs</c> returns: a hit in a table's array part is delivered in place.</summary>
    IpairsAux,
    // The kinds below are the in-place fast paths of Interpreter.CallFast; keep them last (the loop compares against the first).
    MathAbs,
    MathSqrt,
    MathSin,
    MathCos,
    MathFloor,
    StringLen,
    StringByte,
    Type,
    Select,
    SetMetatable,
    TableUnpack,
    Tonumber,
    MathMax,
    MathMin,
}

internal sealed class BuiltinFunction(BuiltinBody body, BuiltinKind kind = BuiltinKind.None) : LuaFunction
{
    public BuiltinKind Kind { get; } = kind;

    /// <summary>The host delegate behind a <see cref="BuiltinKind.Host"/> function.</summary>
    public HostFunction? Host { get; init; }

    /// <summary>True for a host closure that owns state (such as a <c>coroutine.wrap</c> function), which a weak table may drop; library functions are never collected.</summary>
    public bool Collectable { get; init; }

    /// <summary>How many (unnamed, opaque) upvalues PUC gives the equivalent C closure; debug.getupvalue reports them with the empty name.</summary>
    public byte NUpvalues { get; init; }

    public LuaValue[]? Invoke(LuaState state, ReadOnlySpan<LuaValue> args) => body(state, args);
}

/// <summary>A Lua function value: a Proto plus its captured upvalues.</summary>
internal sealed class LuaClosure(Proto proto, UpVal[] upvals) : LuaFunction
{
    public Proto Proto { get; } = proto;
    public UpVal[] Upvals { get; } = upvals;
}
