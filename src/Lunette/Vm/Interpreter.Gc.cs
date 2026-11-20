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
        if (args.Length > 0 && !args[0].IsNil && !(args[0].Ref is byte[] raw && new LuaString(raw).ToString() == "collect"))
            return Special.None;
        co.CollectPending = true;
        co.CollectFa = fa;
        co.CollectNargs = nargs;
        co.CollectWanted = wanted;
        return Special.Collect;
    }

    /// <summary>Runs the requested cycle, stores its result (0) where the call's results go, and returns the new <c>top</c>.</summary>
    private static int RunCollect(LuaState state, Coroutine co)
    {
        co.CollectPending = false;
        int fa = co.CollectFa, wanted = co.CollectWanted;
        int savedTop = co.CallTop;
        co.CallTop = fa + 1 + co.CollectNargs;
        state.Gc.FullCollect();
        co.CallTop = savedTop;
        var st = co.Stack;
        if (wanted < 0)
        {
            st[fa] = LuaValue.FromInteger(0);
            return fa + 1;
        }
        for (int j = 0; j < wanted; j++) st[fa + j] = j == 0 ? LuaValue.FromInteger(0) : default;
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
            co.FaultLine = 0;
            co.CallTop = callTop;
            state.NativeDepth = nativeDepth;
            return false;
        }
    }
}
