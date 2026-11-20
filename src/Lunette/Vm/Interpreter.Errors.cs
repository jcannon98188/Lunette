using Lunette.Lib;
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
        /// <summary>A full collection was requested: the loop must return so it runs with no dispatch-loop temporaries on the stack.</summary>
        Collect,
        /// <summary>Handled, but the hook mask changed: the loop must return so Run re-enters in the other variant.</summary>
        Reenter,
    }

    /// <summary>How many times a failing xpcall handler is retried on its own error before "error in error handling".</summary>
    private const int HandlerRetries = 200;

    /// <summary>Nested pcall/xpcall levels allowed per coroutine (LUAI_MAXCCALLS).</summary>
    private const int MaxProtectedDepth = 200;

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
            // The trampoline that lets pcall protect a host function is not a script position (the host function is the caller).
            if (fr.Closure.Proto.ChunkName == "=(host)")
            {
                ex.WantsPosition = false;
                return;
            }
            if (ex.ArgN > 0) NameArgError(fr, ex);
            ex.Prefix(Where(fr.Closure.Proto, fr.Pc - 1));
        }
        else ex.WantsPosition = false;
    }

    /// <summary>True when the first Frame of a pcall target would exceed the stack or depth limit.</summary>
    private static bool TargetOverflows(LuaState state, Coroutine co, LuaClosure cl, int calleeIdx, int nargs)
    {
        if (co.InHandler) return false;
        if (co.Depth >= state.DepthLimit) return true;
        int baseIdx = cl.Proto.IsVararg ? calleeIdx + nargs + 2 : calleeIdx + 1;
        return baseIdx + cl.Proto.MaxStack > Coroutine.StackLimit;
    }

    /// <summary>
    /// luaL_argerror: names the faulting builtin from how the calling instruction reached it (a call-site name, or a metamethod
    /// event), counting a method call's self out of the argument number; "calling 'f' on bad self" when self is the culprit.
    /// </summary>
    private static void NameArgError(Frame fr, LuaException ex)
    {
        (string? what, string? name) = DebugLib.CallSiteName(fr);
        if (name is null) return;
        int arg = ex.ArgN;
        if (what == "method")
        {
            arg--;
            if (arg == 0)
            {
                ex.Reword($"calling '{name}' on bad self ({ex.ArgMsg})");
                return;
            }
        }
        ex.Reword($"bad argument #{arg} to '{name}' ({ex.ArgMsg})");
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
            // A failing __close handler of this very call replaces the error; the message handler already ran for the first one.
            if (fr.PcallKind == 2 && !fr.Closing) value = RunHandler(state, co, fr.Handler, value);
            ContinueUnwind(state, co, i, value);
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
        int prevRaising = co.RaisingDepth;
        if (!prev) co.RaisingDepth = depth;
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
                    co.FaultLine = 0;
                }
            }
            return LuaValue.FromString("error in error handling");
        }
        finally
        {
            co.InHandler = prev;
            co.RaisingDepth = prevRaising;
            // Give back the handler headroom so the next overflow is detected at the normal limit.
            if (!prev && co.Stack.Length > Coroutine.StackLimit) Array.Resize(ref co.Stack, Coroutine.StackLimit);
        }
    }

    /// <summary>The error a failing <c>ResolveCallable</c> left in <c>co.PendingError</c>: unwinds to the Frame to resume (the loop then takes <c>co.ResumeTop</c> itself).</summary>
    private static Frame RaiseDeferred(LuaState state, Coroutine co, int stop)
    {
        LuaException ex = co.PendingError!;
        co.PendingError = null;
        Frame f = RaiseInLoop(state, co, stop, ex);
        co.Unwound = false;
        return f;
    }

    /// <summary>
    /// A VM-detected error handed back by a slow-path helper: does what <see cref="Run"/> does with a thrown one (note the fault line, unwind to the
    /// nearest protected Frame) but without a .NET exception. Returns the Frame to resume (<c>co.Unwound</c> set, so the loop also takes
    /// <c>co.ResumeTop</c>), or throws <paramref name="ex"/> when no protected Frame at this level can take it.
    /// </summary>
    private static Frame RaiseInLoop(LuaState state, Coroutine co, int stop, LuaException ex)
    {
        DebugLib.NoteFault(co, ex);
        if (!Unwind(state, co, stop, ex.Value)) throw ex;
        co.FaultLine = 0;
        co.Unwound = true;
        return co.ResumeFrame!;
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
                if (nargs == 0) throw LibArgs.ArgError("assert", 1, "value expected");
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
                LuaClosure? cl = fn.Ref as LuaClosure;
                if (cl is null && fn.Ref is BuiltinFunction { Kind: BuiltinKind.Pcall or BuiltinKind.Xpcall } inner
                    && nargs - fixedArgs >= (inner.Kind == BuiltinKind.Xpcall ? 2 : 1))
                {
                    cl = state.Trampoline(inner, inner.Kind == BuiltinKind.Pcall ? "pcall" : "xpcall");
                    fn = LuaValue.FromObject(cl);
                }
                if (cl is null && fn.Ref is BuiltinFunction { Kind: BuiltinKind.Host } hostFn)
                {
                    // A host function may yield, so it runs under a Lua Frame (like a C function called by lua_pcallk).
                    cl = state.Trampoline(hostFn, "host");
                    fn = LuaValue.FromObject(cl);
                }
                if (cl is null) return Special.None;
                LuaValue handler = x ? st[fa + 2] : default;
                int level = co.Depth > 0 ? co.Frames[co.Depth - 1]!.PcallLevel : 0;
                string? refusal = level >= MaxProtectedDepth ? "C stack overflow" : TargetOverflows(state, co, cl, fa + fixedArgs, nargs - fixedArgs) ? "stack overflow" : null;
                if (refusal is not null)
                {
                    // PUC runs the protected function through a C call, so nesting is limited to LUAI_MAXCCALLS levels;
                    // a target whose very first frame does not fit is reported by this pcall, not by an outer one.
                    st[fa] = LuaValue.FromBoolean(false);
                    st[fa + 1] = LuaValue.FromString(refusal);
                    if (wanted < 0) co.ResumeTop = fa + 2;
                    else for (int j = 2; j < wanted; j++) st[fa + j] = default;
                    return Special.Done;
                }
                int ci = fa + fixedArgs;
                st[ci] = fn;
                Frame nf = Precall(co, cl, ci, nargs - fixedArgs, wanted, null, proto, pc);
                nf.PcallLevel = level + 1;
                nf.PcallKind = x ? (byte)2 : (byte)1;
                nf.PcallBase = fa;
                nf.Handler = handler;
                co.ResumeFrame = nf;
                return Special.NewFrame;
            }
            case BuiltinKind.Yield:
                return DoYield(state, co, st.AsSpan(fa + 1, nargs), fa, wanted, false);
            case BuiltinKind.Pairs:
            {
                // A Lua __pairs metamethod runs as an ordinary Frame so that it can yield; its first three results are pairs' results.
                if (nargs == 0 || wanted < 0) return Special.None;
                if (st[fa + 1].Ref is LuaTable { Metatable: null } plain && state.NextFunction is { } nextFn)
                {
                    // No __pairs is possible: hand back next, the table and nil in place.
                    st[fa] = LuaValue.FromObject(nextFn);
                    st[fa + 1] = LuaValue.FromTable(plain);
                    for (int j = 2; j < wanted; j++) st[fa + j] = default;
                    return Special.Done;
                }
                LuaTable? mt = Meta.GetMetatable(state, st[fa + 1]);
                LuaValue tm = mt is null ? default : mt.Get(LuaValue.FromString(state.Strings.FromUtf16("__pairs")));
                if (tm.Ref is not LuaClosure cl) return Special.None;
                st[fa] = tm;
                Frame nf = Precall(co, cl, fa, 1, wanted, null, proto, pc);
                co.ResumeFrame = nf;
                return Special.NewFrame;
            }
            case BuiltinKind.Next:
            {
                if (nargs == 0 || st[fa + 1].Ref is not LuaTable nt) return Special.None;
                bool more = nt.Next(nargs > 1 ? st[fa + 2] : default, out LuaValue nk, out LuaValue nv);
                if (!more) { nk = default; nv = default; }
                return Deliver(st, co, fa, wanted, nk, nv, more ? 2 : 1);
            }
            case BuiltinKind.IpairsAux:
            {
                if (nargs < 2 || st[fa + 1].Ref is not LuaTable it || !st[fa + 2].IsInteger) return Special.None;
                long idx = unchecked(st[fa + 2].AsInteger + 1);
                ref readonly LuaValue iv = ref it.GetIntRef(idx);
                if (!iv.IsNil) return Deliver(st, co, fa, wanted, LuaValue.FromInteger(idx), iv, 2);
                if (!it.NoIndex) return Special.None;
                return Deliver(st, co, fa, wanted, default, default, 1);
            }
            case BuiltinKind.DoFile:
            {
                LuaClosure chunk = LoadLib.LoadForDoFile(state, st.AsSpan(fa + 1, nargs));
                st[fa] = LuaValue.FromObject(chunk);
                Frame nf = Precall(co, chunk, fa, 0, wanted, null, proto, pc);
                co.ResumeFrame = nf;
                return Special.NewFrame;
            }
            case BuiltinKind.Collect:
                return RequestCollect(state, co, st.AsSpan(fa + 1, nargs), fa, nargs, wanted);
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
                if (co.PendingReturnHook)
                {
                    co.HookBuiltin = st[fa]; // the return event is reported against sethook itself
                    co.HookBuiltinNested = false;
                }
                return Special.Reenter;
            }
            case BuiltinKind.Host:
                return CallHost(state, co, (BuiltinFunction)st[fa].Ref!, fa, nargs, wanted, proto, pc);
            default:
                return Special.None;
        }
    }

    /// <summary>Places a builtin's one or two results at <paramref name="fa"/> the way CallBuiltin would: padded or trimmed to <paramref name="wanted"/>, or all of them (setting the top) when it is negative.</summary>
    private static Special Deliver(LuaValue[] st, Coroutine co, int fa, int wanted, in LuaValue a, in LuaValue b, int n)
    {
        st[fa] = a;
        if (wanted < 0)
        {
            if (n > 1) st[fa + 1] = b;
            co.ResumeTop = fa + n;
            return Special.Done;
        }
        if (wanted > 1) st[fa + 1] = n > 1 ? b : default;
        for (int j = 2; j < wanted; j++) st[fa + j] = default;
        return Special.Done;
    }

    /// <summary>
    /// The value <c>error(msg, level)</c> raises: a string message gets the position of the function at <paramref name="level"/>
    /// (1 = the caller of error). <paramref name="fromNative"/> means error was called by a host function such as pcall, which has no position.
    /// </summary>
    internal static LuaValue ErrorValue(Coroutine co, LuaValue msg, long level, bool fromNative)
    {
        if (msg.Ref is byte[] raw && level > 0 && FrameAtLevel(co, level, fromNative) is { } fr)
        {
            Proto p = fr.Closure.Proto;
            int pc = fr.Pc - 1;
            // Valid UTF-8 (the usual case) is copied as bytes behind a cached "chunk:" head; anything else keeps the old
            // decode-and-re-encode text, which replaces invalid sequences.
            if (System.Text.Unicode.Utf8.IsValid(new LuaString(raw).Bytes))
                return LuaValue.FromString(Positioned(p.WhereBytes, p.Lines[pc], new LuaString(raw).Bytes));
            return LuaValue.FromString(Where(p, pc) + " " + new LuaString(raw).ToString());
        }
        return msg;
    }

    /// <summary>The string "head" + line + ": " + message, assembled in one allocation.</summary>
    private static LuaString Positioned(byte[] head, int line, ReadOnlySpan<byte> message)
    {
        Span<byte> digits = stackalloc byte[11];
        line.TryFormat(digits, out int nd, default, System.Globalization.CultureInfo.InvariantCulture);
        LuaString s = LuaString.Allocate(head.Length + nd + 2 + message.Length);
        Span<byte> d = s.WritableBytes;
        head.CopyTo(d);
        digits[..nd].CopyTo(d[head.Length..]);
        d[head.Length + nd] = (byte)':';
        d[head.Length + nd + 1] = (byte)' ';
        message.CopyTo(d[(head.Length + nd + 2)..]);
        return s;
    }

    /// <summary>The Lua Frame at call level <paramref name="level"/>, or null for a host function or an unknown level.</summary>
    private static Frame? FrameAtLevel(Coroutine co, long level, bool fromNative)
    {
        long lv = 1;
        if (fromNative)
        {
            if (level == 1) return null;
            lv = 2;
        }
        for (int idx = co.Depth - 1; idx >= 0; idx--)
        {
            Frame fr = co.Frames[idx]!;
            if (lv == level) return fr;
            lv++;
            if (fr.PcallKind != 0)
            {
                // The Lua function was called by pcall, a host function level of its own.
                if (lv == level) return null;
                lv++;
            }
        }
        return null;
    }
}
