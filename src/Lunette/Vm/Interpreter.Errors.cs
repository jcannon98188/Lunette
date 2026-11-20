using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>
/// Error handling for the VM (ADR 0003). Errors are pending state: the value is found, the Frame chain is walked to the
/// nearest protected Frame (pcall/xpcall) and the loop resumes there. A thrown <see cref="LuaException"/> is only the
/// ergonomic fallback, caught outside the dispatch method and converged onto the same <see cref="Unwind"/>.
/// </summary>
internal static partial class Interpreter
{
    /// <summary>Outcome of <see cref="CallSpecial"/>.</summary>
    private enum Special
    {
        /// <summary>Not handled here; run the builtin's ordinary body.</summary>
        None,
        /// <summary>Handled: results are in place and <c>co.ResumeTop</c> holds the end of a variable list.</summary>
        Done,
        /// <summary>A new Frame is ready in <c>co.ResumeFrame</c>.</summary>
        NewFrame,
        /// <summary>The coroutine yielded: the loop must return to the resumer.</summary>
        Yield,
        /// <summary>Handled, but the hook mask changed: the loop must return so Run re-enters in the other variant.</summary>
        Reenter,
    }

    /// <summary>How many times a failing xpcall handler is retried on its own error before "error in error handling".</summary>
    private const int HandlerRetries = 200;

    private static readonly LuaValue AssertMessage = LuaValue.FromString("assertion failed!");

    internal static bool IsLuaError(Exception e) =>
        e is LuaException or IOException or InvalidCastException or OutOfMemoryException;

    /// <summary>Converts an allow-listed foreign exception to a Lua error; the original stays as InnerException.</summary>
    internal static LuaException AsLuaException(Exception e) => e switch
    {
        LuaException l => l,
        OutOfMemoryException => new LuaException("not enough memory", e),
        _ => new LuaException(e.Message, e),
    };

    /// <summary>Prefixes the calling Lua function's position onto a library error the first time the VM sees it.</summary>
    private static void Locate(Coroutine co, LuaException ex)
    {
        if (!ex.WantsPosition) return;
        if (co.Depth > 0)
        {
            Frame fr = co.Frames[co.Depth - 1]!;
            ex.Prefix(Where(fr.Closure.Proto, fr.Pc - 1));
        }
        else ex.WantsPosition = false;
    }

    /// <summary>
    /// Finds the nearest protected Frame at or above index <paramref name="stop"/>, runs its message handler (frames still intact),
    /// pops to it and stores (false, value). On success the loop must resume at <c>co.ResumeFrame</c> with <c>co.ResumeTop</c>.
    /// </summary>
    private static bool Unwind(LuaState state, Coroutine co, int stop, LuaValue value)
    {
        for (int i = co.Depth - 1; i >= stop; i--)
        {
            Frame fr = co.Frames[i]!;
            if (fr.PcallKind == 0) continue;
            if (fr.PcallKind == 2) value = RunHandler(state, co, fr.Handler, value);
            int dst = fr.PcallBase;
            int wanted = fr.Wanted;
            value = CloseTbcOnError(state, co, fr.FuncIdx, value);
            co.Close(fr.FuncIdx);
            co.Depth = i;
            var r = co.Stack;
            r[dst] = LuaValue.FromBoolean(false);
            r[dst + 1] = value;
            if (wanted < 0) co.ResumeTop = dst + 2;
            else for (int j = 2; j < wanted; j++) r[dst + j] = default;
            co.ResumeFrame = co.Frames[i - 1];
            co.RaisingBuiltin = default;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Calls an xpcall message handler on the faulting stack. An error inside the handler is itself passed to the handler,
    /// until it gives up with "error in error handling".
    /// </summary>
    internal static LuaValue RunHandler(LuaState state, Coroutine co, LuaValue handler, LuaValue value)
    {
        int depth = co.Depth;
        bool prev = co.InHandler;
        co.InHandler = true;
        try
        {
            if (depth > 0)
            {
                Frame top = co.Frames[depth - 1]!;
                co.CallTop = Math.Max(co.CallTop, top.Base + top.Closure.Proto.MaxStack);
            }
            for (int tries = 0; tries < HandlerRetries; tries++)
            {
                try
                {
                    LuaValue[] res = CallValues(state, handler, [value]);
                    return res.Length > 0 ? res[0] : default;
                }
                catch (Exception e) when (IsLuaError(e))
                {
                    value = AsLuaException(e).Value;
                    co.Depth = depth;
                }
            }
            return LuaValue.FromString("error in error handling");
        }
        finally
        {
            co.InHandler = prev;
            // Give back the handler headroom so the next overflow is detected at the normal limit.
            if (!prev && co.Stack.Length > Coroutine.StackLimit) Array.Resize(ref co.Stack, Coroutine.StackLimit);
        }
    }

    /// <summary>Raises <paramref name="value"/>: unwinds in the loop when a protected Frame exists at this level, else throws to the level below.</summary>
    private static Special Raise(LuaState state, Coroutine co, int stop, LuaValue value)
    {
        if (Unwind(state, co, stop, value)) return Special.NewFrame;
        throw new LuaException(value);
    }

    /// <summary>
    /// Runs error, assert, pcall and xpcall inside the loop so the common error path needs no exception.
    /// Returns <see cref="Special.None"/> to fall back to the builtin's own body.
    /// </summary>
    private static Special CallSpecial(LuaState state, Coroutine co, BuiltinKind kind, int fa, int nargs, int wanted, int stop, Proto proto, int pc)
    {
        var st = co.Stack;
        switch (kind)
        {
            case BuiltinKind.Error:
            {
                LuaValue msg = nargs > 0 ? st[fa + 1] : default;
                long level = nargs > 1 ? ErrorLib.LevelArg(st[fa + 2]) : 1;
                co.RaisingBuiltin = st[fa];
                return Raise(state, co, stop, ErrorValue(co, msg, level, false));
            }
            case BuiltinKind.Assert:
            {
                if (nargs == 0) throw LuaErrors.ArgError(1, "assert", "value expected");
                if (!st[fa + 1].IsFalsy)
                {
                    Array.Copy(st, fa + 1, st, fa, nargs);
                    if (wanted < 0) co.ResumeTop = fa + nargs;
                    else for (int j = nargs; j < wanted; j++) st[fa + j] = default;
                    return Special.Done;
                }
                co.RaisingBuiltin = st[fa];
                return Raise(state, co, stop, ErrorValue(co, nargs > 1 ? st[fa + 2] : AssertMessage, 1, false));
            }
            case BuiltinKind.Pcall:
            case BuiltinKind.Xpcall:
            {
                bool x = kind == BuiltinKind.Xpcall;
                int fixedArgs = x ? 2 : 1;
                ErrorLib.CheckProtectedArgs(x, st.AsSpan(fa + 1, nargs));
                LuaValue fn = st[fa + 1];
                if (fn.Ref is BuiltinFunction { Kind: BuiltinKind.Yield })
                    return DoYield(state, co, st.AsSpan(fa + 1 + fixedArgs, nargs - fixedArgs), fa, wanted, true);
                if (fn.Ref is not LuaClosure cl) return Special.None;
                LuaValue handler = x ? st[fa + 2] : default;
                int ci = fa + fixedArgs;
                st[ci] = fn;
                Frame nf = Precall(co, cl, ci, nargs - fixedArgs, wanted, null, proto, pc);
                nf.PcallKind = x ? (byte)2 : (byte)1;
                nf.PcallBase = fa;
                nf.Handler = handler;
                co.ResumeFrame = nf;
                return Special.NewFrame;
            }
            case BuiltinKind.Yield:
                return DoYield(state, co, st.AsSpan(fa + 1, nargs), fa, wanted, false);
            case BuiltinKind.SetHook:
            {
                // Runs the ordinary body, then asks the loop to switch variants if the running coroutine's hook state changed.
                bool was = co.Traced;
                _ = DebugLib.SetHook(state, st.AsSpan(fa + 1, nargs));
                if (wanted < 0) co.ResumeTop = fa;
                else for (int j = 0; j < wanted; j++) st[fa + j] = default;
                if (co.Traced == was) return Special.Done;
                co.Reenter = true;
                co.PendingReturnHook = co.Traced && (co.HookMask & Coroutine.HookReturn) != 0;
                return Special.Reenter;
            }
            case BuiltinKind.Host:
                return CallHost(state, co, (BuiltinFunction)st[fa].Ref!, fa, nargs, wanted, proto, pc);
            default:
                return Special.None;
        }
    }

    /// <summary>
    /// The value <c>error(msg, level)</c> raises: a string message gets the position of the function at <paramref name="level"/>
    /// (1 = the caller of error). <paramref name="fromNative"/> means error was called by a host function such as pcall, which has no position.
    /// </summary>
    internal static LuaValue ErrorValue(Coroutine co, LuaValue msg, long level, bool fromNative)
    {
        if (msg.Ref is byte[] raw && level > 0)
        {
            string where = WhereLevel(co, level, fromNative);
            if (where.Length > 0) return LuaValue.FromString(where + " " + new LuaString(raw).ToString());
        }
        return msg;
    }

    /// <summary>Position "chunk:line:" of the function at call level <paramref name="level"/>, or "" for a host function or an unknown level.</summary>
    private static string WhereLevel(Coroutine co, long level, bool fromNative)
    {
        long lv = 1;
        if (fromNative)
        {
            if (level == 1) return "";
            lv = 2;
        }
        for (int idx = co.Depth - 1; idx >= 0; idx--)
        {
            Frame fr = co.Frames[idx]!;
            if (lv == level) return Where(fr.Closure.Proto, fr.Pc - 1);
            lv++;
            if (fr.PcallKind != 0)
            {
                // The Lua function was called by pcall, a host function level of its own.
                if (lv == level) return "";
                lv++;
            }
        }
        return "";
    }
}
