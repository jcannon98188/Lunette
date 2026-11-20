namespace Lunette.Vm;

internal static partial class Interpreter
{
    /// <summary>
    /// <c>collectgarbage()</c> / <c>collectgarbage("collect")</c> called from Lua: queue the cycle and make the
    /// dispatch loop return, so the collector runs in <see cref="Run"/> where no dispatch-loop temporaries
    /// (whose last-used values stay GC-reported) are on the stack. Anything else uses the builtin body.
    /// </summary>
    private static Special RequestCollect(LuaState state, Coroutine co, ReadOnlySpan<LuaValue> args, int fa, int nargs, int wanted)
    {
        if (state.Gc.InCollector) return Special.None;
        long stepKb = -1;
        if (args.Length > 0 && !args[0].IsNil)
        {
            string option = args[0].Ref is byte[] raw ? new LuaString(raw).ToString() : "";
            if (option == "step")
            {
                // The size is a plain integer; anything else goes to the builtin body, which raises the argument error.
                if (args.Length > 1 && !args[1].IsNil && !args[1].IsInteger) return Special.None;
                stepKb = args.Length > 1 && !args[1].IsNil ? Math.Max(0, args[1].AsInteger) : 0;
            }
            else if (option != "collect") return Special.None;
        }
        co.CollectPending = true;
        co.CollectStepKb = stepKb;
        co.CollectFa = fa;
        co.CollectNargs = nargs;
        co.CollectWanted = wanted;
        return Special.Collect;
    }

    /// <summary>The loop reached a safepoint with a cycle due: run it after the loop returns, then resume at the jump target.</summary>
    private static void RequestAutoCollect(Coroutine co, int liveTop, int top)
    {
        co.CollectPending = true;
        co.CollectStepKb = -2;
        co.CollectFa = liveTop;
        co.CollectNargs = -1;
        co.CollectTop = top;
    }

    /// <summary>Runs the requested cycle, stores its result where the call's results go (a full collection answers 0, a step whether a cycle completed), and returns the new <c>top</c>.</summary>
    private static int RunCollect(LuaState state, Coroutine co)
    {
        co.CollectPending = false;
        int fa = co.CollectFa, wanted = co.CollectWanted;
        long stepKb = co.CollectStepKb;
        int savedTop = co.CallTop;
        co.CallTop = fa + 1 + co.CollectNargs;
        LuaValue result;
        if (stepKb >= 0) result = LuaValue.FromBoolean(state.Gc.StepBy(stepKb));
        else
        {
            state.Gc.FullCollect();
            result = LuaValue.FromInteger(0);
        }
        co.CallTop = savedTop;
        if (stepKb == -2) return co.CollectTop; // automatic: the loop resumes as if nothing happened
        var st = co.Stack;
        if (wanted < 0)
        {
            st[fa] = result;
            return fa + 1;
        }
        for (int j = 0; j < wanted; j++) st[fa + j] = j == 0 ? result : default;
        return 0;
    }

    /// <summary>
    /// Calls <paramref name="fn"/> with one argument as a non-yieldable native call and swallows a Lua error,
    /// returning it instead. Used for <c>__gc</c>, whose errors become warnings.
    /// </summary>
    public static bool TryCall1(LuaState state, LuaValue fn, LuaValue arg, out LuaValue error)
    {
        var co = state.Running;
        int depth = co.Depth;
        int callTop = co.CallTop;
        int nativeDepth = state.NativeDepth;
        try
        {
            CallValues(state, fn, [arg]);
            error = default;
            return true;
        }
        catch (Exception e) when (IsLuaError(e))
        {
            error = AsLuaException(e).Value;
            co.Depth = depth;
            co.CallTop = callTop;
            state.NativeDepth = nativeDepth;
            return false;
        }
    }
}
