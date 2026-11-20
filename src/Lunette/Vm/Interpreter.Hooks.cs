namespace Lunette.Vm;

/// <summary>
/// Selects one of the two dispatch loops generated from <c>RunLoop&lt;TMode&gt;</c>. The mode is a struct type argument with a
/// constant <see cref="Traced"/>, so the JIT specialises the loop per mode and removes every <c>if (TMode.Traced)</c> branch from the
/// fast instantiation: with no hook set the loop contains no hook-check code at all (ticket 08, Q7).
/// </summary>
internal interface ILoopMode
{
    static abstract bool Traced { get; }
}

/// <summary>The loop that runs while no hook is set.</summary>
internal struct FastLoop : ILoopMode
{
    public static bool Traced => false;
}

/// <summary>The loop that runs while a call, return, line or count hook is set on the coroutine.</summary>
internal struct TracedLoop : ILoopMode
{
    public static bool Traced => true;
}

/// <summary>
/// Hook events for the traced loop (debug.sethook). Only <c>RunLoop&lt;TracedLoop&gt;</c> reaches these methods. Hook functions run on
/// a nested loop with hooks disabled, so they do not nest; they see the hooked function at level 2 of debug.getinfo.
/// </summary>
internal static partial class Interpreter
{
    /// <summary>Called when the loop (re)enters a Frame: a fresh call fires the call event, a return or resume just resyncs the line tracker.</summary>
    private static void TraceEnter(LuaState state, Coroutine co, Frame f, int pc, int top)
    {
        if (co.HookMask == 0 || !co.AllowHook) return;
        int scratch = Math.Max(co.CallTop, top);
        if (pc == 0)
        {
            co.OldPc = 0;
            if ((co.HookMask & Coroutine.HookCall) != 0) FireHook(state, co, f, 1, scratch, f.IsTail ? "tail call" : "call", -1);
            return;
        }
        co.OldPc = pc - 1;
        if (co.PendingReturnHook)
        {
            // The sethook call that installed this hook returns first.
            co.PendingReturnHook = false;
            if ((co.HookMask & Coroutine.HookReturn) != 0) FireHook(state, co, f, pc, scratch, "return", -1);
        }
    }

    /// <summary>Called before the instruction at <paramref name="pc"/> runs: count and line events.</summary>
    private static void TraceInstruction(LuaState state, Coroutine co, Frame f, int pc, int top)
    {
        byte mask = co.HookMask;
        if (mask == 0 || !co.AllowHook) return;
        int scratch = Math.Max(co.CallTop, top);
        if ((mask & Coroutine.HookCount) != 0 && --co.HookCounter <= 0)
        {
            co.HookCounter = co.HookPeriod;
            FireHook(state, co, f, pc + 1, scratch, "count", -1);
        }
        if ((mask & Coroutine.HookLine) != 0)
        {
            int[] lines = f.Closure.Proto.Lines;
            int old = co.OldPc;
            if (pc <= old || old >= lines.Length || lines[pc] != lines[old])
            {
                FireHook(state, co, f, pc + 1, scratch, "line", lines[pc]);
            }
            co.OldPc = pc;
        }
    }

    /// <summary>Called before a Lua function returns, while its Frame is still on the stack.</summary>
    private static void TraceReturn(LuaState state, Coroutine co, Frame f, int pc, int scratch)
    {
        if ((co.HookMask & Coroutine.HookReturn) == 0 || !co.AllowHook) return;
        FireHook(state, co, f, pc, Math.Max(co.CallTop, scratch), "return", -1);
    }

    /// <summary>Reports a call to, or return from, a host function made by the running Lua function.</summary>
    private static void TraceBuiltin(LuaState state, Coroutine co, BuiltinFunction bf, int scratch, bool call, bool nested = false)
    {
        byte want = call ? Coroutine.HookCall : Coroutine.HookReturn;
        if ((co.HookMask & want) == 0 || !co.AllowHook || co.Depth == 0) return;
        Frame f = co.Frames[co.Depth - 1]!;
        co.HookBuiltin = LuaValue.FromObject(bf);
        co.HookBuiltinNested = nested;
        FireHook(state, co, f, f.Pc, Math.Max(co.CallTop, scratch), call ? "call" : "return", -1);
    }

    /// <summary>Calls the hook function with (event, line). <paramref name="framePc"/> is the Frame's saved pc while the hook runs.</summary>
    private static void FireHook(LuaState state, Coroutine co, Frame f, int framePc, int scratch, string ev, int line)
    {
        LuaValue hook = co.HookFn;
        if (hook.IsNil) return;
        int savedPc = f.Pc;
        int savedTop = co.CallTop;
        int oldPc = co.OldPc;
        int savedHookFrame = co.HookFrameIndex;
        f.Pc = framePc;
        co.CallTop = scratch;
        co.AllowHook = false;
        co.HookFrameIndex = co.Depth;
        try
        {
            CallValues(state, hook, [LuaValue.FromString(ev), line >= 0 ? LuaValue.FromInteger(line) : default]);
        }
        finally
        {
            co.AllowHook = true;
            co.HookFrameIndex = savedHookFrame;
            co.HookBuiltin = default;
            co.OldPc = oldPc;
            co.CallTop = savedTop;
            f.Pc = savedPc;
        }
    }
}
