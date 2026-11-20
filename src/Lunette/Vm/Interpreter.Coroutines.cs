using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>
/// Coroutines and yieldable metamethods. A coroutine owns its stack and Frames; resume runs a nested loop on the .NET stack
/// and yield returns out of it, which is safe because everything yieldable is a heap Frame (ADR 0002).
/// </summary>
internal static partial class Interpreter
{
    /// <summary>
    /// Pushes a Frame for a prepared metamethod call so the instruction at <paramref name="pc"/> finishes when it returns
    /// (see <see cref="FinishMeta"/>). A host function is called at once on a nested loop and returns null with its result.
    /// </summary>
    private static Frame? StartMeta(LuaState state, Coroutine co, Frame f, in MetaCall mc, Continuation cont, int dest, int pc, Proto proto, out LuaValue result)
    {
        result = default;
        if (mc.IsDirect)
        {
            result = mc.Direct;
            return null;
        }
        if (mc.Fn.Ref is not LuaClosure cl)
        {
            f.Pc = pc;
            result = Meta.CallMeta(state, in mc);
            return null;
        }
        f.Pc = pc;
        int fa = co.CallTop;
        co.EnsureStack(fa + 5, proto, pc - 1);
        var st = co.Stack;
        st[fa] = mc.Fn;
        st[fa + 1] = mc.A;
        st[fa + 2] = mc.B;
        st[fa + 3] = mc.C;
        Frame nf = Precall(co, cl, fa, mc.N, 1, null, proto, pc - 1);
        nf.Cont = cont;
        nf.ContDest = dest;
        return nf;
    }

    /// <summary>Completes the instruction that called a metamethod Frame, given the metamethod's first result.</summary>
    private static void FinishMeta(Frame f, LuaValue result, LuaValue[] r)
    {
        switch (f.Cont)
        {
            case Continuation.Value:
                r[f.ContDest] = result;
                break;
            case Continuation.Bool:
                r[f.ContDest] = LuaValue.FromBoolean(!result.IsFalsy);
                break;
        }
    }

    /// <summary>Records a yield and tells the loop to return to the resumer.</summary>
    private static Special DoYield(LuaState state, Coroutine co, ReadOnlySpan<LuaValue> args, int fa, int wanted, bool viaPcall)
    {
        if (co.IsMain || co.Nny > 0)
        {
            // pcall(coroutine.yield) falls back to the ordinary body, which raises the same error inside the pcall.
            if (viaPcall) return Special.None;
            throw new LuaException(co.IsMain ? "attempt to yield from outside a coroutine" : "attempt to yield across a C-call boundary");
        }
        co.YieldValues = Rets.Of(args); // shared for up to three values; Resume hands it straight to its caller, who consumes it at once
        co.YieldFa = fa;
        co.YieldWanted = wanted;
        co.YieldPcall = viaPcall;
        co.YieldFn = co.Stack[fa].Ref as BuiltinFunction;
        co.Yielded = true;
        return Special.Yield;
    }

    /// <summary>
    /// Resumes <paramref name="co"/> with <paramref name="args"/>. Returns true with the yielded or returned values, or false
    /// with the error value as the only result.
    /// </summary>
    internal static bool Resume(LuaState state, Coroutine co, ReadOnlySpan<LuaValue> args, out LuaValue[] results)
    {
        if (co.Status != CoStatus.Suspended)
        {
            results = [LuaValue.FromString(co.Status == CoStatus.Dead ? "cannot resume dead coroutine" : "cannot resume non-suspended coroutine")];
            return false;
        }
        if (state.NativeDepth >= MaxNativeDepth)
        {
            results = [LuaValue.FromString("C stack overflow")];
            return false;
        }
        Coroutine prev = state.Running;
        prev.Status = CoStatus.Normal;
        co.Status = CoStatus.Running;
        state.Running = co;
        state.NativeDepth++;
        try
        {
            int end;
            if (!co.Started)
            {
                co.Started = true;
                if (co.Body.Ref is not LuaClosure cl)
                {
                    LuaValue[]? res = ((BuiltinFunction)co.Body.Ref!).Invoke(state, args);
                    co.Status = CoStatus.Dead;
                    results = res is null ? [] : Rets.Own(res);
                    return true;
                }
                co.EnsureStack(args.Length + 2, null, 0);
                co.Stack[0] = co.Body;
                args.CopyTo(new Span<LuaValue>(co.Stack, 1, args.Length));
                Frame f = Precall(co, cl, 0, args.Length, -1, null, null, 0);
                end = Run(state, co, f, 0);
            }
            else
            {
                end = Run(state, co, co.Frames[co.Depth - 1]!, 0, 0, args, true);
            }
            if (co.Yielded)
            {
                co.Yielded = false;
                co.Status = CoStatus.Suspended;
                results = co.YieldValues;
                co.YieldValues = [];
                return true;
            }
            co.Status = CoStatus.Dead;
            results = new ReadOnlySpan<LuaValue>(co.Stack, 0, end).ToArray();
            return true;
        }
        catch (Exception e) when (IsLuaError(e))
        {
            co.Status = CoStatus.Dead;
            co.Yielded = false;
            co.ErrorValue = AsLuaException(e).Value;
            results = [co.ErrorValue.Value];
            return false;
        }
        finally
        {
            state.NativeDepth--;
            state.Running = prev;
            prev.Status = CoStatus.Running;
        }
    }

    /// <summary>
    /// Kills a suspended or dead coroutine: runs its pending __close handlers (on the coroutine, so they see its stack), closes its
    /// upvalues and marks it dead. Returns the error that killed it or that a handler raised, or null for a clean close.
    /// </summary>
    internal static LuaValue? CloseThread(LuaState state, Coroutine co)
    {
        LuaValue err = co.ErrorValue ?? default;
        co.ErrorValue = null;
        if (co.Tbc.Count > 0)
        {
            Coroutine prev = state.Running;
            CoStatus before = co.Status;
            state.Running = co;
            prev.Status = CoStatus.Normal;
            co.Status = CoStatus.Running;
            try
            {
                err = CloseTbcOnError(state, co, 0, err);
            }
            finally
            {
                state.Running = prev;
                prev.Status = CoStatus.Running;
                co.Status = before;
            }
        }
        co.Close(0);
        co.Depth = 0;
        co.Status = CoStatus.Dead;
        return err.IsNil ? (LuaValue?)null : err;
    }

    /// <summary>
    /// Completes the yield call that suspended <paramref name="co"/>: runs host continuations, stores the results and reports the
    /// host function's return to a hook. Returns false when a continuation yielded again.
    /// </summary>
    private static bool FinishYield(LuaState state, Coroutine co, ReadOnlySpan<LuaValue> args, out int top)
    {
        top = 0;
        if (!RunContinuations(state, co, args, out LuaValue[]? contValues)) return false;
        top = PlaceResumeValues(co, contValues ?? args);
        if (co.YieldFn is { } yf)
        {
            co.YieldFn = null;
            if (co.Traced) TraceBuiltin(state, co, yf, top, false);
        }
        return true;
    }

    /// <summary>Stores the resume arguments as the results of the yield call that suspended <paramref name="co"/>; returns the new top.</summary>
    private static int PlaceResumeValues(Coroutine co, ReadOnlySpan<LuaValue> args)
    {
        int fa = co.YieldFa;
        int lead = co.YieldPcall ? 1 : 0;
        int total = args.Length + lead;
        int wanted = co.YieldWanted;
        co.EnsureStack(fa + Math.Max(total, wanted) + 1, null, 0);
        var st = co.Stack;
        if (lead == 1) st[fa] = LuaValue.FromBoolean(true);
        args.CopyTo(new Span<LuaValue>(st, fa + lead, args.Length));
        if (wanted < 0) return fa + total;
        for (int j = total; j < wanted; j++) st[fa + j] = default;
        return 0;
    }
}
