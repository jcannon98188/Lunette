using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>
/// The VM loop. Lua-to-Lua calls push heap Frames and continue in this one loop; the .NET
/// stack never grows with Lua call depth (ADR 0002).
/// </summary>
internal static partial class Interpreter
{
    private const double Two63 = 9223372036854775808.0;

    public static void Execute(LuaState state, Proto proto, ReadOnlySpan<LuaValue> args = default)
    {
        var co = state.Main;
        co.Reset();
        var main = state.NewMainClosure(proto, LuaValue.FromTable(state.Globals));
        co.EnsureStack(args.Length + 2, null, 0);
        co.Stack[0] = LuaValue.FromObject(main);
        args.CopyTo(new Span<LuaValue>(co.Stack, 1, args.Length));
        Frame entry = Precall(co, main, 0, args.Length, 0, null, null, 0);
        Run(state, co, entry, 0);
    }

    /// <summary>Longest chain of nested native-to-Lua calls (metamethods and callbacks), like PUC's C-call limit.</summary>
    private const int MaxNativeDepth = 200;

    /// <summary>
    /// Calls <paramref name="fn"/> with the first <paramref name="nargs"/> of a, b, c and returns its first result.
    /// Used for metamethods: runs a nested interpreter loop above the current frame's registers.
    /// </summary>
    public static LuaValue Call1(LuaState state, LuaValue fn, LuaValue a, LuaValue b, LuaValue c, int nargs)
    {
        var co = state.Running;
        int fa = co.CallTop;
        co.EnsureStack(fa + 5, null, 0);
        var st = co.Stack;
        st[fa] = fn;
        st[fa + 1] = a;
        st[fa + 2] = b;
        st[fa + 3] = c;
        int n = CallAt(state, co, fa, nargs);
        co.CallTop = fa;
        return n > 0 ? co.Stack[fa] : default;
    }

    /// <summary>Calls <paramref name="fn"/> with <paramref name="args"/> and returns all of its results.</summary>
    public static LuaValue[] CallValues(LuaState state, LuaValue fn, ReadOnlySpan<LuaValue> args)
    {
        var co = state.Running;
        int fa = co.CallTop;
        co.EnsureStack(fa + args.Length + 2, null, 0);
        var st = co.Stack;
        st[fa] = fn;
        args.CopyTo(new Span<LuaValue>(st, fa + 1, args.Length));
        int n = CallAt(state, co, fa, args.Length);
        co.CallTop = fa;
        return new ReadOnlySpan<LuaValue>(co.Stack, fa, n).ToArray();
    }

    /// <summary>Runs the function at stack index <paramref name="fa"/> to completion; its results land at <paramref name="fa"/>.</summary>
    private static int CallAt(LuaState state, Coroutine co, int fa, int nargs)
    {
        if (state.NativeDepth >= MaxNativeDepth) throw new LuaException("C stack overflow");
        state.NativeDepth++;
        co.Nny++;
        try
        {
            nargs = ResolveCallable(co, fa, nargs, null, 0);
            if (co.Stack[fa].Ref is LuaClosure cl)
            {
                int stop = co.Depth;
                Frame f = Precall(co, cl, fa, nargs, -1, null, null, 0);
                f.Native = true;
                return Run(state, co, f, stop) - fa;
            }
            return CallBuiltin(state, co, fa, nargs, -1, null, 0) - fa;
        }
        finally
        {
            co.Nny--;
            state.NativeDepth--;
        }
    }

    /// <summary>Makes R[fa] a function by inserting __call handlers in front of it; returns the new argument count.</summary>
    private static int ResolveCallable(Coroutine co, int fa, int nargs, Proto? p, int pc)
    {
        for (int guard = 0; co.Stack[fa].Ref is not (LuaClosure or BuiltinFunction); guard++)
        {
            LuaTable? callMt = co.Stack[fa].Ref switch { LuaTable t => t.Metatable, LuaUserdata u => u.Metatable, _ => null };
            LuaValue tm = callMt is null ? default : callMt.GetTagMethod(TagMethod.Call);
            if (tm.IsNil || guard > 100)
                throw new LuaException($"{(p is null ? "" : Where(p, pc) + " ")}attempt to call a {Meta.ObjTypeName(co.Stack[fa])} value{VarInfo.DescribeCallee(p, pc)}");
            co.EnsureStack(fa + nargs + 2, p, pc);
            var st = co.Stack;
            Array.Copy(st, fa, st, fa + 1, nargs + 1);
            st[fa] = tm;
            nargs++;
        }
        return nargs;
    }

    /// <summary>
    /// Runs the dispatch loop. The only <c>try</c> lives here, outside the loop method: a thrown LuaException
    /// is converted to pending-error state, unwound to the nearest protected Frame and the loop re-entered (ADR 0003).
    /// </summary>
    private static int Run(LuaState state, Coroutine co, Frame f, int stop, int top = 0) =>
        Run(state, co, f, stop, top, default, false);

    /// <summary>
    /// <paramref name="resuming"/> first gives the resume values to the continuations of the host function that yielded (inside the
    /// <c>try</c>, so an error they raise reaches a Lua pcall of the coroutine) and then continues the top Frame with its results.
    /// </summary>
    private static int Run(LuaState state, Coroutine co, Frame f, int stop, int top, ReadOnlySpan<LuaValue> resumeArgs, bool resuming)
    {
        while (true)
        {
            try
            {
                if (resuming)
                {
                    resuming = false;
                    if (!FinishYield(state, co, resumeArgs, out top)) return 0;
                    f = co.Frames[co.Depth - 1]!;
                }
                int result = co.Traced ? RunLoop<TracedLoop>(state, co, f, stop, top) : RunLoop<FastLoop>(state, co, f, stop, top);
                if (co.CollectPending)
                {
                    top = RunCollect(state, co);
                    f = co.Frames[co.Depth - 1]!;
                    continue;
                }
                if (!co.Reenter) return result;
                // sethook switched this coroutine between the fast and the traced loop: continue where it stopped.
                co.Reenter = false;
                f = co.ResumeFrame!;
                top = co.ResumeTop;
                continue;
            }
            catch (Exception e) when (IsLuaError(e))
            {
                LuaException ex = AsLuaException(e);
                Locate(co, ex);
                if (!Unwind(state, co, stop, ex.Value))
                {
                    DebugLib.NoteFault(co, ex);
                    // The structured trace is captured at the first point the error leaves every protected frame, host-initiated calls included.
                    ex.StackFrames ??= DebugLib.CaptureFrames(state, co);
                    if (state.NativeDepth == 0)
                    {
                        ex.Traceback ??= DebugLib.CaptureTraceback(state, co);
                        co.RaisingBuiltin = default;
                    }
                    if (ReferenceEquals(ex, e)) throw;
                    throw ex;
                }
            }
            f = co.ResumeFrame!;
            top = co.ResumeTop;
        }
    }

    [LuaInterpreterLoop]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static int RunLoop<TMode>(LuaState state, Coroutine co, Frame f, int stop, int top)
        where TMode : struct, ILoopMode
    {
        LuaClosure cl;
        Proto proto;
        Instruction[] code;
        LuaValue[] k;
        LuaValue[] r;
        int bs, pc;

    newframe:
        cl = f.Closure;
        proto = cl.Proto;
        code = proto.Code;
        k = proto.Constants;
        r = co.Stack;
        bs = f.Base;
        pc = f.Pc;
        co.CallTop = bs + proto.MaxStack;
        if (TMode.Traced)
        {
            TraceEnter(state, co, f, pc, top);
            r = co.Stack;
        }

        while (true)
        {
            if (TMode.Traced)
            {
                TraceInstruction(state, co, f, pc, top);
                r = co.Stack;
            }
            Instruction i = code[pc++];
            switch (i.Op)
            {
                case OpCode.Move:
                    r[bs + i.A] = r[bs + i.B];
                    break;
                case OpCode.LoadK:
                    r[bs + i.A] = k[i.Bx];
                    break;
                case OpCode.LoadKX:
                    r[bs + i.A] = k[code[pc++].Ax];
                    break;
                case OpCode.LoadNil:
                    for (int j = 0; j <= i.B; j++) r[bs + i.A + j] = default;
                    break;
                case OpCode.LoadBool:
                    r[bs + i.A] = LuaValue.FromBoolean(i.B != 0);
                    break;
                case OpCode.GetGlobal:
                {
                    LuaValue obj = cl.Upvals[i.B].Get(), key = k[i.C];
                    if (obj.Ref is LuaTable t)
                    {
                        LuaValue v = t.Get(key);
                        if (!v.IsNil || t.Metatable is null)
                        {
                            r[bs + i.A] = v;
                            break;
                        }
                    }
                    if (Meta.PrepareIndex(state, obj, key, proto, pc - 1, out LuaValue res, out MetaCall mc))
                    {
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                    }
                    r[bs + i.A] = res;
                    break;
                }
                case OpCode.SetGlobal:
                {
                    LuaValue obj = cl.Upvals[i.B].Get();
                    if (obj.Ref is LuaTable t && t.Metatable is null) Meta.RawSet(t, k[i.C], r[bs + i.A], proto, pc - 1);
                    else if (Meta.PrepareSetIndex(state, obj, k[i.C], r[bs + i.A], proto, pc - 1, out MetaCall mc))
                    {
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Discard, 0, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                    }
                    break;
                }
                case OpCode.GetUpval:
                    r[bs + i.A] = cl.Upvals[i.B].Get();
                    break;
                case OpCode.SetUpval:
                    cl.Upvals[i.B].Set(r[bs + i.A]);
                    break;
                case OpCode.Add:
                case OpCode.Sub:
                case OpCode.Mul:
                case OpCode.Div:
                case OpCode.IDiv:
                case OpCode.Mod:
                case OpCode.Pow:
                {
                    LuaValue lhs = r[bs + i.B], rhs = r[bs + i.C];
                    if (lhs.IsNumber && rhs.IsNumber) r[bs + i.A] = Arith.Binary(i.Op, lhs, rhs, proto, pc - 1);
                    else
                    {
                        MetaCall mc = Meta.PrepareArith(state, Meta.ArithEvent(i.Op), lhs, rhs, proto, pc - 1);
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        r[bs + i.A] = res;
                    }
                    break;
                }
                case OpCode.BAnd:
                case OpCode.BOr:
                case OpCode.BXor:
                case OpCode.Shl:
                case OpCode.Shr:
                {
                    LuaValue lhs = r[bs + i.B], rhs = r[bs + i.C];
                    if (lhs.IsNumber && rhs.IsNumber) r[bs + i.A] = Arith.Bitwise(i.Op, lhs, rhs, proto, pc - 1);
                    else
                    {
                        MetaCall mc = Meta.PrepareArith(state, Meta.ArithEvent(i.Op), lhs, rhs, proto, pc - 1);
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        r[bs + i.A] = res;
                    }
                    break;
                }
                case OpCode.BNot:
                {
                    LuaValue v = r[bs + i.B];
                    if (v.IsNumber) r[bs + i.A] = Arith.BitwiseNot(v, proto, pc - 1);
                    else
                    {
                        MetaCall mc = Meta.PrepareUnary(state, TagMethod.BNot, v, proto, pc - 1);
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        r[bs + i.A] = res;
                    }
                    break;
                }
                case OpCode.Unm:
                {
                    LuaValue v = r[bs + i.B];
                    if (v.IsNumber) r[bs + i.A] = Arith.Negate(v, proto, pc - 1);
                    else
                    {
                        MetaCall mc = Meta.PrepareUnary(state, TagMethod.Unm, v, proto, pc - 1);
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        r[bs + i.A] = res;
                    }
                    break;
                }
                case OpCode.Not:
                    r[bs + i.A] = LuaValue.FromBoolean(r[bs + i.B].IsFalsy);
                    break;
                case OpCode.Eq:
                {
                    if (Meta.PrepareEqual(r[bs + i.B], r[bs + i.C], out bool eq, out MetaCall mc))
                    {
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Bool, bs + i.A, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        eq = !res.IsFalsy;
                    }
                    r[bs + i.A] = LuaValue.FromBoolean(eq);
                    break;
                }
                case OpCode.Lt:
                {
                    LuaValue lhs = r[bs + i.B], rhs = r[bs + i.C];
                    if (lhs.IsNumber && rhs.IsNumber) r[bs + i.A] = LuaValue.FromBoolean(Compare.LessThan(lhs, rhs, proto, pc - 1));
                    else
                    {
                        if (!Meta.PrepareOrder(state, TagMethod.Lt, lhs, rhs, proto, pc - 1, out bool ordRes, out MetaCall mc))
                        {
                            r[bs + i.A] = LuaValue.FromBoolean(ordRes);
                            break;
                        }
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Bool, bs + i.A, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        r[bs + i.A] = LuaValue.FromBoolean(!res.IsFalsy);
                    }
                    break;
                }
                case OpCode.Le:
                {
                    LuaValue lhs = r[bs + i.B], rhs = r[bs + i.C];
                    if (lhs.IsNumber && rhs.IsNumber) r[bs + i.A] = LuaValue.FromBoolean(Compare.LessEqual(lhs, rhs, proto, pc - 1));
                    else
                    {
                        if (!Meta.PrepareOrder(state, TagMethod.Le, lhs, rhs, proto, pc - 1, out bool ordRes, out MetaCall mc))
                        {
                            r[bs + i.A] = LuaValue.FromBoolean(ordRes);
                            break;
                        }
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Bool, bs + i.A, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        r[bs + i.A] = LuaValue.FromBoolean(!res.IsFalsy);
                    }
                    break;
                }
                case OpCode.Test:
                    if (!r[bs + i.A].IsFalsy != (i.C != 0)) pc++;
                    break;
                case OpCode.Jmp:
                    if (i.Ax < pc)
                    {
                        // Backward jumps are a poll point for the sandbox limits (budget, memory, interrupt) and the collector.
                        if (--state.Ticks <= 0)
                        {
                            f.Pc = pc;
                            state.Poll();
                        }
                        if (--state.GcTick < 0)
                        {
                            f.Pc = pc;
                            state.Gc.Safepoint();
                            r = co.Stack;
                        }
                    }
                    pc = i.Ax;
                    break;
                case OpCode.Call:
                {
                    int fa = bs + i.A;
                    int nargs = i.B != 0 ? i.B - 1 : top - fa - 1;
                    int wanted = i.C - 1;
                    f.Pc = pc;
                    if (r[fa].Ref is not (LuaClosure or BuiltinFunction))
                    {
                        nargs = ResolveCallable(co, fa, nargs, proto, pc - 1);
                        r = co.Stack;
                    }
                    if (r[fa].Ref is LuaClosure callee)
                    {
                        if (co.Depth >= state.DepthLimit) throw LuaErrors.Error("stack overflow");
                        if (--state.Ticks <= 0) state.Poll();
                        f = Precall(co, callee, fa, nargs, wanted, null, proto, pc - 1);
                        goto newframe;
                    }
                    var bf = (BuiltinFunction)r[fa].Ref!;
                    if (TMode.Traced) TraceBuiltin(state, co, bf, Math.Max(top, fa + nargs + 1), true);
                    if (bf.Kind != BuiltinKind.None)
                    {
                        Special sp = CallSpecial(state, co, bf.Kind, fa, nargs, wanted, stop, proto, pc - 1);
                        if (sp == Special.NewFrame)
                        {
                            f = co.ResumeFrame!;
                            top = co.ResumeTop;
                            goto newframe;
                        }
                        if (sp is Special.Yield or Special.Collect) return top;
                        if (sp == Special.Reenter)
                        {
                            if (wanted < 0) top = co.ResumeTop;
                            co.ResumeFrame = f;
                            co.ResumeTop = top;
                            return top;
                        }
                        if (sp == Special.Done)
                        {
                            r = co.Stack;
                            if (wanted < 0) top = co.ResumeTop;
                            if (TMode.Traced) TraceBuiltin(state, co, bf, top, false);
                            r = co.Stack;
                            break;
                        }
                    }
                    int end = CallBuiltin(state, co, fa, nargs, wanted, proto, pc - 1);
                    r = co.Stack;
                    if (wanted < 0) top = end;
                    if (TMode.Traced)
                    {
                        TraceBuiltin(state, co, bf, Math.Max(top, end), false);
                        r = co.Stack;
                    }
                    break;
                }
                case OpCode.TailCall:
                {
                    int fa = bs + i.A;
                    int nargs = i.B != 0 ? i.B - 1 : top - fa - 1;
                    if (r[fa].Ref is not (LuaClosure or BuiltinFunction))
                    {
                        nargs = ResolveCallable(co, fa, nargs, proto, pc - 1);
                        r = co.Stack;
                    }
                    if (r[fa].Ref is LuaClosure callee)
                    {
                        f.Pc = pc;
                        if (--state.Ticks <= 0) state.Poll();
                        if (co.OpenUpvals.Count > 0) co.Close(bs);
                        Array.Copy(r, fa, r, f.FuncIdx, nargs + 1);
                        f = Precall(co, callee, f.FuncIdx, nargs, f.Wanted, f, proto, pc - 1);
                        f.IsTail = true;
                        goto newframe;
                    }
                    // Builtins cannot be tail-called in place: run it and let the Return that follows pass on its results.
                    f.Pc = pc;
                    var tbf = (BuiltinFunction)r[fa].Ref!;
                    if (TMode.Traced) TraceBuiltin(state, co, tbf, Math.Max(top, fa + nargs + 1), true);
                    if (tbf.Kind != BuiltinKind.None)
                    {
                        Special sp = CallSpecial(state, co, tbf.Kind, fa, nargs, -1, stop, proto, pc - 1);
                        if (sp == Special.NewFrame)
                        {
                            f = co.ResumeFrame!;
                            top = co.ResumeTop;
                            goto newframe;
                        }
                        if (sp is Special.Yield or Special.Collect) return top;
                        if (sp == Special.Reenter)
                        {
                            co.ResumeFrame = f;
                            return top;
                        }
                        if (sp == Special.Done)
                        {
                            r = co.Stack;
                            top = co.ResumeTop;
                            break;
                        }
                    }
                    top = CallBuiltin(state, co, fa, nargs, -1, proto, pc - 1);
                    if (TMode.Traced) TraceBuiltin(state, co, tbf, top, false);
                    r = co.Stack;
                    break;
                }
                case OpCode.Return:
                {
                    int ra = bs + i.A;
                    int n = i.B != 0 ? i.B - 1 : top - ra;
                    if (co.OpenUpvals.Count > 0) co.Close(bs);
                    if (co.Tbc.Count > 0 && co.Tbc[^1] >= bs)
                    {
                        // Each __close handler runs as a Frame so it can yield; this instruction runs again when it returns.
                        f.Pc = pc;
                        Frame? hf = StartClose(state, co, f, bs, Math.Max(bs + proto.MaxStack, ra + n), top);
                        r = co.Stack;
                        if (hf is not null)
                        {
                            f = hf;
                            goto newframe;
                        }
                    }
                    if (TMode.Traced)
                    {
                        TraceReturn(state, co, f, pc, Math.Max(top, ra + n));
                        r = co.Stack;
                    }
                    if (f.Cont != Continuation.None)
                    {
                        Continuation cont = f.Cont;
                        FinishMeta(f, n > 0 ? r[ra] : default, r);
                        f = ResumeAfterMeta(state, co, cont, ref top);
                        goto newframe;
                    }
                    int dst = f.FuncIdx;
                    int wanted = f.Wanted;
                    if (f.PcallKind != 0)
                    {
                        // A protected call's results are (true, ...) at its own base; wanted counts the status too.
                        dst = f.PcallBase;
                        r[dst++] = LuaValue.FromBoolean(true);
                        if (wanted > 0) wanted--;
                    }
                    if (wanted < 0)
                    {
                        Array.Copy(r, ra, r, dst, n);
                        top = dst + n;
                    }
                    else
                    {
                        int c = Math.Min(n, wanted);
                        Array.Copy(r, ra, r, dst, c);
                        for (int j = c; j < wanted; j++) r[dst + j] = default;
                    }
                    if (--co.Depth == stop) return top;
                    f = co.Frames[co.Depth - 1]!;
                    goto newframe;
                }
                case OpCode.ForPrep:
                    if (ForPrep(r, bs + i.A, proto, pc - 1)) pc++;
                    break;
                case OpCode.ForLoop:
                    if (!ForLoop(r, bs + i.A)) pc++;
                    break;
                case OpCode.Closure:
                {
                    var np = proto.Protos[i.Bx];
                    var ups = new UpVal[np.Upvalues.Length];
                    for (int j = 0; j < ups.Length; j++)
                    {
                        var d = np.Upvalues[j];
                        ups[j] = d.InStack ? co.FindUpval(bs + d.Index) : cl.Upvals[d.Index];
                    }
                    r[bs + i.A] = LuaValue.FromObject(new LuaClosure(np, ups));
                    break;
                }
                case OpCode.Close:
                    if (co.OpenUpvals.Count > 0) co.Close(bs + i.A);
                    if (co.Tbc.Count > 0 && co.Tbc[^1] >= bs + i.A)
                    {
                        f.Pc = pc;
                        Frame? hf = StartClose(state, co, f, bs + i.A, bs + proto.MaxStack, top);
                        r = co.Stack;
                        if (hf is not null)
                        {
                            f = hf;
                            goto newframe;
                        }
                    }
                    break;
                case OpCode.Tbc:
                    MarkToBeClosed(state, co,bs + i.A, proto, pc - 1, proto.Names[i.Bx]);
                    break;
                case OpCode.Vararg:
                {
                    int ra = bs + i.A;
                    int n = f.VarargCount;
                    int wanted = i.C - 1;
                    if (wanted < 0)
                    {
                        co.EnsureStack(ra + n, proto, pc - 1);
                        r = co.Stack;
                        Array.Copy(r, f.VarargBase, r, ra, n);
                        top = ra + n;
                    }
                    else
                    {
                        int c = Math.Min(n, wanted);
                        Array.Copy(r, f.VarargBase, r, ra, c);
                        for (int j = c; j < wanted; j++) r[ra + j] = default;
                    }
                    break;
                }
                case OpCode.NewTable:
                {
                    int arraySize = code[pc++].Ax;
                    r[bs + i.A] = LuaValue.FromTable(new LuaTable(arraySize, i.B));
                    break;
                }
                case OpCode.GetTable:
                {
                    LuaValue obj = r[bs + i.B], key = r[bs + i.C];
                    if (obj.Ref is LuaTable t)
                    {
                        LuaValue v = t.Get(key);
                        if (!v.IsNil || t.Metatable is null)
                        {
                            r[bs + i.A] = v;
                            break;
                        }
                    }
                    if (Meta.PrepareIndex(state, obj, key, proto, pc - 1, out LuaValue res, out MetaCall mc))
                    {
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                    }
                    r[bs + i.A] = res;
                    break;
                }
                case OpCode.SetTable:
                {
                    LuaValue obj = r[bs + i.A];
                    if (obj.Ref is LuaTable t && t.Metatable is null) Meta.RawSet(t, r[bs + i.B], r[bs + i.C], proto, pc - 1);
                    else
                    {
                        if (Meta.PrepareSetIndex(state, obj, r[bs + i.B], r[bs + i.C], proto, pc - 1, out MetaCall mc))
                        {
                            Frame? nf = StartMeta(state, co, f, in mc, Continuation.Discard, 0, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        }
                        r = co.Stack;
                    }
                    break;
                }
                case OpCode.SetList:
                {
                    int offset = code[pc++].Ax;
                    int ra = bs + i.A;
                    int n = i.B != 0 ? i.B : top - ra - 1;
                    var t = (LuaTable)r[ra].Ref!;
                    t.EnsureArray(offset + n);
                    for (int j = 1; j <= n; j++) t.SetInt(offset + j, r[ra + j]);
                    break;
                }
                case OpCode.Self:
                {
                    LuaValue obj = r[bs + i.B], key = r[bs + i.C];
                    r[bs + i.A + 1] = obj;
                    LuaValue v = default;
                    if (obj.Ref is LuaTable t) v = t.Get(key);
                    if (v.IsNil)
                    {
                        if (Meta.PrepareIndex(state, obj, key, proto, pc - 1, out v, out MetaCall mc))
                        {
                            Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                            v = res;
                        }
                    }
                    r[bs + i.A] = v;
                    break;
                }
                case OpCode.Len:
                {
                    LuaValue v = r[bs + i.B];
                    if (v.Ref is LuaTable t && t.Metatable is null) r[bs + i.A] = LuaValue.FromInteger(t.Length());
                    else
                    {
                        if (Meta.PrepareLen(state, v, proto, pc - 1, out LuaValue res, out MetaCall mc))
                        {
                            Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        }
                        r[bs + i.A] = res;
                    }
                    break;
                }
                case OpCode.Concat:
                {
                    LuaValue lhs = r[bs + i.B], rhs = r[bs + i.C];
                    if ((lhs.IsString || lhs.IsNumber) && (rhs.IsString || rhs.IsNumber)) r[bs + i.A] = StringConv.Concat(state, lhs, rhs);
                    else
                    {
                        MetaCall mc = Meta.PrepareConcat(state, lhs, rhs, proto, pc - 1);
                        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out LuaValue res);
                        if (nf is not null)
                        {
                            f = nf;
                            goto newframe;
                        }
                        r = co.Stack;
                        r[bs + i.A] = res;
                    }
                    break;
                }
                case OpCode.TForLoop:
                    if (!r[bs + i.A + 4].IsNil) r[bs + i.A + 2] = r[bs + i.A + 4];
                    else pc++;
                    break;
                default:
                    throw new InvalidOperationException($"bad opcode {i.Op}");
            }
        }
    }

    /// <summary>Sets up a Frame for a Lua call whose function sits at <paramref name="fa"/> with <paramref name="nargs"/> arguments above it.</summary>
    private static Frame Precall(Coroutine co, LuaClosure cl, int fa, int nargs, int wanted, Frame? reuse, Proto? whereProto, int wherePc)
    {
        Proto p = cl.Proto;
        int baseIdx, varargBase = 0, varargCount = 0;
        if (!p.IsVararg)
        {
            baseIdx = fa + 1;
            co.EnsureStack(baseIdx + p.MaxStack, whereProto, wherePc);
            var st = co.Stack;
            for (int j = nargs; j < p.NumParams; j++) st[baseIdx + j] = default;
        }
        else
        {
            // PUC layout: the extra arguments stay where they are and the function plus fixed
            // parameters are copied above them, so the callee's registers start past the varargs.
            int newFunc = fa + 1 + nargs;
            baseIdx = newFunc + 1;
            co.EnsureStack(baseIdx + p.MaxStack, whereProto, wherePc);
            var st = co.Stack;
            st[newFunc] = st[fa];
            for (int j = 0; j < p.NumParams; j++) st[baseIdx + j] = j < nargs ? st[fa + 1 + j] : default;
            varargBase = fa + 1 + p.NumParams;
            varargCount = Math.Max(nargs - p.NumParams, 0);
        }
        Frame f = reuse ?? co.PushFrame();
        f.Closure = cl;
        f.Base = baseIdx;
        f.Pc = 0;
        f.FuncIdx = fa;
        f.Wanted = wanted;
        f.VarargBase = varargBase;
        f.VarargCount = varargCount;
        return f;
    }

    /// <summary>Calls a builtin and places its results at <paramref name="fa"/>; returns the index after the last result.</summary>
    private static int CallBuiltin(LuaState state, Coroutine co, int fa, int nargs, int wanted, Proto? proto, int pc)
    {
        if (co.Stack[fa].Ref is not BuiltinFunction fn)
            throw new LuaException($"{(proto is null ? "" : Where(proto, pc) + " ")}attempt to call a {Meta.ObjTypeName(co.Stack[fa])} value");
        int savedTop = co.CallTop;
        co.CallTop = fa + 1 + nargs;
        var res = fn.Invoke(state, new ReadOnlySpan<LuaValue>(co.Stack, fa + 1, nargs));
        co.CallTop = savedTop;
        int n = res?.Length ?? 0;
        if (wanted < 0)
        {
            co.EnsureStack(fa + n, proto, pc);
            res?.CopyTo(co.Stack, fa);
            Rets.Release(res);
            return fa + n;
        }
        var r = co.Stack;
        int c = Math.Min(n, wanted);
        for (int j = 0; j < c; j++) r[fa + j] = res![j];
        for (int j = c; j < wanted; j++) r[fa + j] = default;
        Rets.Release(res);
        return fa + wanted;
    }

    private static bool ForPrep(LuaValue[] r, int ra, Proto p, int pc)
    {
        LuaValue init = r[ra], limit = r[ra + 1], step = r[ra + 2];
        if (init.IsInteger && step.IsInteger)
        {
            long i0 = init.AsInteger, st = step.AsInteger;
            if (st == 0) throw new LuaException($"{Where(p, pc)} 'for' step is zero");
            if (ForLimit(limit, st, p, pc, out long lim)) return false;
            if (st > 0 ? i0 > lim : i0 < lim) return false;
            ulong count = st > 0
                ? (ulong)lim - (ulong)i0
                : (ulong)i0 - (ulong)lim;
            count = st > 0 ? count / (ulong)st : count / ((ulong)(-(st + 1)) + 1UL);
            r[ra + 1] = LuaValue.FromInteger((long)count);
            r[ra + 3] = init;
            return true;
        }
        if (!ForNumber(limit, out double fl)) throw new LuaException($"{Where(p, pc)} bad 'for' limit (number expected, got {Meta.ObjTypeName(limit)})");
        if (!ForNumber(step, out double fs)) throw new LuaException($"{Where(p, pc)} bad 'for' step (number expected, got {Meta.ObjTypeName(step)})");
        if (!ForNumber(init, out double fi)) throw new LuaException($"{Where(p, pc)} bad 'for' initial value (number expected, got {Meta.ObjTypeName(init)})");
        if (fs == 0) throw new LuaException($"{Where(p, pc)} 'for' step is zero");
        if (fs > 0 ? fl < fi : fi < fl) return false;
        r[ra] = LuaValue.FromFloat(fi);
        r[ra + 1] = LuaValue.FromFloat(fl);
        r[ra + 2] = LuaValue.FromFloat(fs);
        r[ra + 3] = r[ra];
        return true;
    }

    private static bool ForNumber(in LuaValue v, out double d)
    {
        if (v.TryToNumber(out d)) return true;
        if (StringConv.TryToNumber(v, out LuaValue n))
        {
            d = n.IsInteger ? n.AsInteger : n.AsFloat;
            return true;
        }
        return false;
    }

    /// <summary>Converts the loop limit to an integer; returns true when the loop must be skipped.</summary>
    private static bool ForLimit(LuaValue limit, long step, Proto p, int pc, out long lim)
    {
        lim = 0;
        if (limit.IsInteger)
        {
            lim = limit.AsInteger;
            return false;
        }
        if (!limit.IsFloat)
        {
            if (!StringConv.TryToNumber(limit, out LuaValue conv)) throw new LuaException($"{Where(p, pc)} bad 'for' limit (number expected, got {Meta.ObjTypeName(limit)})");
            if (conv.IsInteger)
            {
                lim = conv.AsInteger;
                return false;
            }
            limit = conv;
        }
        double f = limit.AsFloat;
        if (double.IsNaN(f)) return true;
        f = step < 0 ? Math.Ceiling(f) : Math.Floor(f);
        if (f >= Two63)
        {
            if (step < 0) return true;
            lim = long.MaxValue;
        }
        else if (f < -Two63)
        {
            if (step > 0) return true;
            lim = long.MinValue;
        }
        else lim = (long)f;
        return false;
    }

    /// <summary>Advances the loop; returns true when another iteration should run.</summary>
    private static bool ForLoop(LuaValue[] r, int ra)
    {
        if (r[ra + 2].IsInteger)
        {
            ulong count = (ulong)r[ra + 1].AsInteger;
            if (count == 0) return false;
            r[ra + 1] = LuaValue.FromInteger((long)(count - 1));
            long idx = unchecked(r[ra].AsInteger + r[ra + 2].AsInteger);
            r[ra] = LuaValue.FromInteger(idx);
            r[ra + 3] = r[ra];
            return true;
        }
        double step = r[ra + 2].AsFloat, limit = r[ra + 1].AsFloat;
        double next = r[ra].AsFloat + step;
        if (step > 0 ? next <= limit : limit <= next)
        {
            r[ra] = LuaValue.FromFloat(next);
            r[ra + 3] = r[ra];
            return true;
        }
        return false;
    }

    internal static string Where(Proto p, int pc)
    {
        return $"{DebugLib.ChunkId(p.ChunkName)}:{p.Lines[pc]}:";
    }
}
