using Lunette.Compiler;
using Lunette.Lib;
using Lunette.Vm;

namespace Lunette;

/// <summary>Which events a host hook (<see cref="LuaState.SetHook"/>) receives; a count hook is requested with the count argument instead.</summary>
[Flags]
public enum LuaHookMask
{
    None = 0,
    /// <summary>A Lua or host function is called.</summary>
    Call = 1,
    /// <summary>A function is about to return.</summary>
    Return = 2,
    /// <summary>The interpreter is about to run code on a new line (or jumps back to a line).</summary>
    Line = 4,
}

/// <summary>The event a host hook is told about.</summary>
public enum LuaHookEvent
{
    Call,
    TailCall,
    Return,
    Line,
    Count,
}

/// <summary>
/// A host debug hook. <paramref name="line"/> is the new line for <see cref="LuaHookEvent.Line"/> and -1 otherwise. While the hook runs,
/// level 0 of <see cref="LuaState.TryGetInfo"/> is the function the event is about.
/// </summary>
public delegate void LuaHookCallback(LuaState state, LuaHookEvent hookEvent, int line);

/// <summary>A snapshot of one call level, after the fields of <c>debug.getinfo</c>.</summary>
public readonly struct LuaDebugInfo
{
    internal LuaDebugInfo(LuaValue function, string source, string shortSource, int lineDefined, int lastLineDefined, string what,
        int currentLine, string? name, string nameWhat, bool isTailCall, int upvalueCount, int parameterCount, bool isVararg)
    {
        Function = function;
        Source = source;
        ShortSource = shortSource;
        LineDefined = lineDefined;
        LastLineDefined = lastLineDefined;
        What = what;
        CurrentLine = currentLine;
        Name = name;
        NameWhat = nameWhat;
        IsTailCall = isTailCall;
        UpvalueCount = upvalueCount;
        ParameterCount = parameterCount;
        IsVararg = isVararg;
    }

    /// <summary>The function running at this level.</summary>
    public LuaValue Function { get; }

    /// <summary>The chunk name as given to <c>Load</c> (<c>=[C]</c> for a host function).</summary>
    public string Source { get; }

    /// <summary>The printable form of <see cref="Source"/> (<c>[C]</c> for a host function).</summary>
    public string ShortSource { get; }

    /// <summary>The line where the function starts (0 for a main chunk, -1 for a host function).</summary>
    public int LineDefined { get; }

    /// <summary>The line where the function ends (-1 for a host function).</summary>
    public int LastLineDefined { get; }

    /// <summary><c>Lua</c>, <c>main</c> or <c>C</c>.</summary>
    public string What { get; }

    /// <summary>The line being executed, or -1 for a host function.</summary>
    public int CurrentLine { get; }

    /// <summary>How the caller named the function (a global, local, method, field), or null when unknown.</summary>
    public string? Name { get; }

    /// <summary><c>global</c>, <c>local</c>, <c>method</c>, <c>field</c>, <c>upvalue</c>, <c>metamethod</c>, <c>hook</c>, <c>for iterator</c> or empty.</summary>
    public string NameWhat { get; }

    /// <summary>True when the function was entered by a tail call (its caller's frame is gone).</summary>
    public bool IsTailCall { get; }

    /// <summary>Number of upvalues of the function.</summary>
    public int UpvalueCount { get; }

    /// <summary>Number of fixed parameters.</summary>
    public int ParameterCount { get; }

    /// <summary>True when the function takes <c>...</c> (always true for a host function).</summary>
    public bool IsVararg { get; }
}

/// <summary>
/// The host debug API, for debug adapters, profilers and coverage tools that do not want a Lua shim over <c>debug.sethook</c>.
/// It drives the same machinery as the debug library, so semantics match it exactly. The state is single-threaded: every member
/// here must be called from the thread that runs the state (only <c>RequestInterrupt</c> may come from elsewhere).
/// </summary>
public sealed partial class LuaState
{
    /// <summary>The coroutine that is running now, as a thread value (the main coroutine when no Lua code is running).</summary>
    public LuaValue RunningThread => LuaValue.FromObject(Running);

    private Coroutine ResolveThread(LuaValue thread)
    {
        if (thread.IsNil) return Running;
        if (thread.Ref is Coroutine co) return co;
        throw new ArgumentException("The value is not a thread.", nameof(thread));
    }

    /// <summary>
    /// Installs <paramref name="hook"/> for the events in <paramref name="mask"/> and, when <paramref name="count"/> is above zero, every
    /// <paramref name="count"/> instructions, on one coroutine (<paramref name="thread"/>, or the running one when nil). A hook is per
    /// coroutine: coroutines created later start without one. A coroutine has one hook at a time, shared with <c>debug.sethook</c>: the
    /// last of the two to be set wins, and <c>debug.gethook</c> reports a host hook as an opaque function. A null
    /// <paramref name="hook"/>, or an empty mask with no count, clears it and the interpreter returns to its untraced loop.
    /// </summary>
    /// <remarks>
    /// The hook does not re-enter itself: Lua code and host calls it makes run without hooks. If it throws a <see cref="LuaException"/>
    /// the error is raised at the hooked point exactly like an error thrown by a Lua hook (a script may <c>pcall</c> it); budget and
    /// memory aborts pass through, and any other exception leaves the state in an unspecified condition, so abandon the run. When it
    /// is set from a host function the hook starts after that function returns.
    /// </remarks>
    public void SetHook(LuaHookCallback? hook, LuaHookMask mask, int count = 0, LuaValue thread = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Coroutine co = ResolveThread(thread);
        byte bits = (byte)((int)mask & (Coroutine.HookCall | Coroutine.HookReturn | Coroutine.HookLine));
        if (count > 0) bits |= Coroutine.HookCount;
        if (hook is null || bits == 0)
        {
            DebugLib.ClearHook(co);
            return;
        }
        co.HookFn = LuaValue.FromObject(new BuiltinFunction((state, args) =>
        {
            string ev = args[0].ToString();
            LuaHookEvent kind = ev switch
            {
                "call" => LuaHookEvent.Call,
                "tail call" => LuaHookEvent.TailCall,
                "return" => LuaHookEvent.Return,
                "line" => LuaHookEvent.Line,
                _ => LuaHookEvent.Count,
            };
            hook(state, kind, args.Length > 1 && args[1].IsInteger ? (int)args[1].AsInteger : -1);
            return null;
        }));
        co.HookMask = bits;
        co.HookPeriod = co.HookCounter = count;
    }

    /// <summary>
    /// Call levels follow the stack seen from where the host asks: from a hook, level 0 is the function the event is about; from a host
    /// function called by Lua, level 0 is its Lua caller; on a suspended coroutine, level 0 is the <c>yield</c> that suspended it.
    /// </summary>
    private List<DebugLib.Level> HostLevels(Coroutine co)
    {
        List<DebugLib.Level> levels = DebugLib.Levels(this, co, default, false);
        if (!co.AllowHook && co.HookFrameIndex == co.Depth && !co.HookBuiltin.IsNil)
            levels.Insert(0, new DebugLib.Level(-1, co.HookBuiltin)); // a host function's call or return event: the function has no Frame
        return levels;
    }

    /// <summary>Describes call level <paramref name="level"/> (0 is the innermost) of a coroutine; false when the stack has no such level.</summary>
    public bool TryGetInfo(int level, out LuaDebugInfo info, LuaValue thread = default)
    {
        info = default;
        Coroutine co = ResolveThread(thread);
        List<DebugLib.Level> levels = HostLevels(co);
        if (level < 0 || level >= levels.Count) return false;
        LuaValue fn = levels[level].Fn;
        Proto? p = (fn.Ref as LuaClosure)?.Proto;
        Frame? fr = levels[level].Frame >= 0 ? co.Frames[levels[level].Frame] : null;
        (string? nameWhat, string? name) = DebugLib.NameOf(co, levels, level);
        int nups = fn.Ref is LuaClosure uc ? uc.Upvals.Length : (fn.Ref as BuiltinFunction)?.NUpvalues ?? 0;
        info = p is null
            ? new LuaDebugInfo(fn, "=[C]", "[C]", -1, -1, "C", -1, name, nameWhat ?? "", false, nups, 0, true)
            : new LuaDebugInfo(fn, p.ChunkName, DebugLib.ChunkId(p.ChunkName), p.LineDefined, p.LastLineDefined,
                p.LineDefined == 0 ? "main" : "Lua", fr is null ? -1 : DebugLib.CurrentLine(co, fr, levels[level].Frame),
                name, nameWhat ?? "", fr is { IsTail: true }, nups, p.NumParams, p.IsVararg);
        return true;
    }

    /// <summary>
    /// Reads local <paramref name="index"/> (1-based, in declaration order among those active at the current line; temporaries follow; a
    /// negative index reads a vararg) of a call level. False when the level or index does not exist.
    /// </summary>
    public bool TryGetLocal(int level, int index, out string? name, out LuaValue value, LuaValue thread = default)
    {
        name = null;
        value = default;
        Coroutine co = ResolveThread(thread);
        List<DebugLib.Level> levels = HostLevels(co);
        if (level < 0 || level >= levels.Count) return false;
        name = DebugLib.LocalAt(co, levels[level], index, out int slot);
        if (name is null) return false;
        value = co.Stack[slot];
        return true;
    }

    /// <summary>Assigns local <paramref name="index"/> of a call level (see <see cref="TryGetLocal"/>); false when it does not exist.</summary>
    public bool TrySetLocal(int level, int index, LuaValue value, out string? name, LuaValue thread = default)
    {
        Coroutine co = ResolveThread(thread);
        List<DebugLib.Level> levels = HostLevels(co);
        name = null;
        if (level < 0 || level >= levels.Count) return false;
        name = DebugLib.LocalAt(co, levels[level], index, out int slot);
        if (name is null) return false;
        co.Stack[slot] = value;
        return true;
    }

    /// <summary>Reads upvalue <paramref name="index"/> (1-based) of a Lua function; false for a host function or an index out of range.</summary>
    public bool TryGetUpvalue(LuaFunction function, int index, out string? name, out LuaValue value)
    {
        ArgumentNullException.ThrowIfNull(function);
        name = null;
        value = default;
        if (function is not LuaClosure cl || index < 1 || index > cl.Upvals.Length) return false;
        name = DebugLib.UpvalName(cl, index);
        value = cl.Upvals[index - 1].Get();
        return true;
    }

    /// <summary>Assigns upvalue <paramref name="index"/> of a Lua function (shared with every closure that captured it).</summary>
    public bool TrySetUpvalue(LuaFunction function, int index, LuaValue value, out string? name)
    {
        ArgumentNullException.ThrowIfNull(function);
        name = null;
        if (function is not LuaClosure cl || index < 1 || index > cl.Upvals.Length) return false;
        name = DebugLib.UpvalName(cl, index);
        cl.Upvals[index - 1].Set(value);
        return true;
    }
}
