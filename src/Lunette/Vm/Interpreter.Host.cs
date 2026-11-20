using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>Entry points for the public host API: allocation-free calls and host functions that may yield.</summary>
internal static partial class Interpreter
{
    /// <summary>Calls <paramref name="fn"/> with the first <paramref name="nargs"/> of a..d and returns its first result.</summary>
    public static LuaValue CallFirst(LuaState state, LuaValue fn, LuaValue a, LuaValue b, LuaValue c, LuaValue d, int nargs)
    {
        var co = state.Running;
        int fa = co.CallTop;
        co.EnsureStack(fa + 6, null, 0);
        var st = co.Stack;
        st[fa] = fn;
        st[fa + 1] = a;
        st[fa + 2] = b;
        st[fa + 3] = c;
        st[fa + 4] = d;
        int n = CallAt(state, co, fa, nargs);
        co.CallTop = fa;
        return n > 0 ? co.Stack[fa] : default;
    }

    /// <summary>
    /// Runs a host function made by <see cref="LuaState.CreateFunction"/> directly from the dispatch loop so that it can yield.
    /// Results are placed like a builtin's; a yield request suspends the coroutine and remembers the continuation.
    /// </summary>
    private static Special CallHost(LuaState state, Coroutine co, BuiltinFunction bf, int fa, int nargs, int wanted, Proto proto, int pc)
    {
        int savedTop = co.CallTop;
        co.CallTop = fa + 1 + nargs;
        var ctx = new CallContext(state, new ReadOnlySpan<LuaValue>(co.Stack, fa + 1, nargs));
        int n = bf.Host!(ref ctx);
        co.CallTop = savedTop;
        if (n == CallContext.YieldRequest)
        {
            Special sp = DoYield(state, co, ctx.Pushed, fa, wanted, false);
            co.YieldCont = ctx.Continuation;
            return sp;
        }
        LuaValue[] res = ctx.TakeResults(n);
        if (wanted < 0)
        {
            co.EnsureStack(fa + res.Length, proto, pc);
            res.CopyTo(co.Stack, fa);
            co.ResumeTop = fa + res.Length;
            return Special.Done;
        }
        var r = co.Stack;
        int c = Math.Min(res.Length, wanted);
        for (int j = 0; j < c; j++) r[fa + j] = res[j];
        for (int j = c; j < wanted; j++) r[fa + j] = default;
        return Special.Done;
    }

    /// <summary>
    /// Runs the continuations of host functions that yielded, feeding each the resume values. Returns false when one yields again
    /// (the new yield is recorded on <paramref name="co"/>); otherwise true with <paramref name="values"/> the values the yield call returns.
    /// </summary>
    private static bool RunContinuations(LuaState state, Coroutine co, ReadOnlySpan<LuaValue> args, out LuaValue[]? values)
    {
        values = null;
        while (co.YieldCont is { } k)
        {
            co.YieldCont = null;
            var ctx = new CallContext(state, values ?? args);
            int n = k(ref ctx);
            if (n == CallContext.YieldRequest)
            {
                co.YieldValues = ctx.Pushed.ToArray();
                co.YieldCont = ctx.Continuation;
                co.Yielded = true;
                return false;
            }
            values = ctx.TakeResults(n);
        }
        return true;
    }
}
