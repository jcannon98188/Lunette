using Lunette.Compiler;
using Lunette.Unsafe;

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
    public static LuaValue Call1(LuaState state, LuaValue fn, LuaValue a, LuaValue b, LuaValue c, int nargs, bool fromHost = false)
    {
        var co = state.Running;
        int fa = co.CallTop;
        co.EnsureStack(fa + 5, null, 0);
        var st = co.Stack;
        st[fa] = fn;
        st[fa + 1] = a;
        st[fa + 2] = b;
        st[fa + 3] = c;
        int n = CallAt(state, co, fa, nargs, fromHost);
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
        int n = CallAt(state, co, fa, args.Length, true);
        co.CallTop = fa;
        return new ReadOnlySpan<LuaValue>(co.Stack, fa, n).ToArray();
    }

    /// <summary>Runs the function at stack index <paramref name="fa"/> to completion; its results land at <paramref name="fa"/>.</summary>
    private static int CallAt(LuaState state, Coroutine co, int fa, int nargs, bool fromHost = false)
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
            var bf = (BuiltinFunction)co.Stack[fa].Ref!;
            LuaValue callee = co.Stack[fa];
            try
            {
                if (!co.Traced) return CallBuiltin(state, co, fa, nargs, -1, null, 0) - fa;
                TraceBuiltin(state, co, bf, fa + 1 + nargs, true, fa + 1, nargs, true);
                int end = CallBuiltin(state, co, fa, nargs, -1, null, 0);
                TraceBuiltin(state, co, bf, end, false, fa, end - fa, true);
                return end - fa;
            }
            catch (LuaException ex) when (fromHost && ex.ArgN > 0 && ex.WantsPosition)
            {
                // Called from host code there is no call-site name and no Lua position: luaL_argerror uses the global name.
                ErrorLib.QualifyArgError(state, callee, ex);
                ex.ArgN = 0;
                ex.WantsPosition = false;
                throw;
            }
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
                DebugLib.NoteFault(co, ex);
                if (!Unwind(state, co, stop, ex.Value))
                {
                    // The structured trace is captured at the first point the error leaves every protected frame, host-initiated calls included.
                    ex.TraceEntries ??= DebugLib.CaptureFrames(state, co);
                    if (state.NativeDepth == 0)
                    {
                        ex.Traceback ??= DebugLib.CaptureTraceback(state, co);
                        co.RaisingBuiltin = default;
                    }
                    if (ReferenceEquals(ex, e)) throw;
                    throw ex;
                }
            }
            co.FaultLine = 0;
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
                    Regs.Copy(ref Regs.At(r, bs + i.A), in Regs.At(r, bs + i.B));
                    break;
                case OpCode.LoadK:
                    Regs.Copy(ref Regs.At(r, bs + i.A), in Regs.At(k, i.Bx));
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
                {
                    UpVal uv = cl.Upvals[i.B];
                    Coroutine? owner = uv.Owner;
                    // Open upvalue: read the owner's stack slot in place; the copy skips the write barrier when the register holds the same tag object
                    if (owner is null) Regs.Copy(ref Regs.At(r, bs + i.A), in uv.Value);
                    else Regs.Copy(ref Regs.At(r, bs + i.A), in owner.Stack[uv.Index]);
                    break;
                }
                case OpCode.SetUpval:
                    cl.Upvals[i.B].Set(r[bs + i.A]);
                    break;
                case OpCode.Add:
                case OpCode.AddK:
                    if (ArithFast<AddOp>(ref Regs.At(r, bs + i.A), ref Regs.At(r, bs + i.B), ref (i.Op == OpCode.AddK ? ref Regs.At(k, i.C) : ref Regs.At(r, bs + i.C)))) break;
                    goto arithslow;
                case OpCode.Sub:
                case OpCode.SubK:
                    if (ArithFast<SubOp>(ref Regs.At(r, bs + i.A), ref Regs.At(r, bs + i.B), ref (i.Op == OpCode.SubK ? ref Regs.At(k, i.C) : ref Regs.At(r, bs + i.C)))) break;
                    goto arithslow;
                case OpCode.Mul:
                case OpCode.MulK:
                    if (ArithFast<MulOp>(ref Regs.At(r, bs + i.A), ref Regs.At(r, bs + i.B), ref (i.Op == OpCode.MulK ? ref Regs.At(k, i.C) : ref Regs.At(r, bs + i.C)))) break;
                    goto arithslow;
                case OpCode.Div:
                case OpCode.DivK:
                    if (ArithFast<DivOp>(ref Regs.At(r, bs + i.A), ref Regs.At(r, bs + i.B), ref (i.Op == OpCode.DivK ? ref Regs.At(k, i.C) : ref Regs.At(r, bs + i.C)))) break;
                    goto arithslow;
                case OpCode.IDiv:
                case OpCode.Mod:
                case OpCode.Pow:
                case OpCode.IDivK:
                case OpCode.ModK:
                case OpCode.PowK:
                    goto arithslow;
                case OpCode.BAnd:
                case OpCode.BOr:
                case OpCode.BXor:
                case OpCode.Shl:
                case OpCode.Shr:
                    goto arithslow;
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
                        eq = Truthy(res);
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
                        r[bs + i.A] = LuaValue.FromBoolean(Truthy(res));
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
                        r[bs + i.A] = LuaValue.FromBoolean(Truthy(res));
                    }
                    break;
                }
                case OpCode.Test:
                    if (!Regs.IsFalsy(ref Regs.At(r, bs + i.A)) != (i.C != 0)) pc++;
                    else
                    {
                        i = code[pc++]; // the Jmp that follows runs now, without another dispatch
                        goto case OpCode.Jmp;
                    }
                    break;
                case OpCode.JLt:
                case OpCode.JLtK:
                case OpCode.JGtK:
                {
                    // R[B] < R[C], R[B] < K[C], or K[C] < R[B]
                    OpCode op = i.Op;
                    ref LuaValue x = ref Regs.At(r, bs + i.B);
                    ref LuaValue y = ref (op == OpCode.JLt ? ref Regs.At(r, bs + i.C) : ref Regs.At(k, i.C));
                    ref LuaValue lhs = ref (op == OpCode.JGtK ? ref y : ref x);
                    ref LuaValue rhs = ref (op == OpCode.JGtK ? ref x : ref y);
                    bool res;
                    if (lhs.IsInteger && rhs.IsInteger) res = lhs.AsInteger < rhs.AsInteger;
                    else if (lhs.IsFloat && rhs.IsFloat) res = lhs.AsFloat < rhs.AsFloat;
                    else goto condslow;
                    if (res == (i.A != 0))
                    {
                        i = code[pc++];
                        goto case OpCode.Jmp;
                    }
                    pc++;
                    break;
                }
                case OpCode.JLe:
                case OpCode.JLeK:
                case OpCode.JGeK:
                {
                    OpCode op = i.Op;
                    ref LuaValue x = ref Regs.At(r, bs + i.B);
                    ref LuaValue y = ref (op == OpCode.JLe ? ref Regs.At(r, bs + i.C) : ref Regs.At(k, i.C));
                    ref LuaValue lhs = ref (op == OpCode.JGeK ? ref y : ref x);
                    ref LuaValue rhs = ref (op == OpCode.JGeK ? ref x : ref y);
                    bool res;
                    if (lhs.IsInteger && rhs.IsInteger) res = lhs.AsInteger <= rhs.AsInteger;
                    else if (lhs.IsFloat && rhs.IsFloat) res = lhs.AsFloat <= rhs.AsFloat;
                    else goto condslow;
                    if (res == (i.A != 0))
                    {
                        i = code[pc++];
                        goto case OpCode.Jmp;
                    }
                    pc++;
                    break;
                }
                case OpCode.JEq:
                case OpCode.JEqK:
                {
                    ref LuaValue lhs = ref Regs.At(r, bs + i.B);
                    ref LuaValue rhs = ref (i.Op == OpCode.JEq ? ref Regs.At(r, bs + i.C) : ref Regs.At(k, i.C));
                    bool res;
                    if (lhs.IsInteger && rhs.IsInteger) res = lhs.AsInteger == rhs.AsInteger;
                    else if (lhs.IsFloat && rhs.IsFloat) res = lhs.AsFloat == rhs.AsFloat;
                    else if (ReferenceEquals(lhs.Ref, rhs.Ref)) res = true;
                    else goto condslow;
                    if (res == (i.A != 0))
                    {
                        i = code[pc++];
                        goto case OpCode.Jmp;
                    }
                    pc++;
                    break;
                }
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
                            int live = bs + proto.LiveRegisters(i.Ax);
                            if (state.Gc.Safepoint(live))
                            {
                                // A cycle is due: leave the loop (resuming at the jump target) so it runs where no dispatch temporary is live.
                                f.Pc = i.Ax;
                                RequestAutoCollect(co, live, top);
                                return top;
                            }
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
                    object? fn = Regs.At(r, fa).Ref;
                    if (fn is not (LuaClosure or BuiltinFunction))
                    {
                        nargs = ResolveCallable(co, fa, nargs, proto, pc - 1);
                        r = co.Stack;
                        fn = Regs.At(r, fa).Ref;
                    }
                    if (fn is LuaClosure callee)
                    {
                        if (co.Depth >= state.DepthLimit) throw LuaErrors.Error("stack overflow");
                        if (--state.Ticks <= 0) state.Poll();
                        f = PrecallLua(co, callee, fa, nargs, wanted, proto, pc - 1);
                        goto newframe;
                    }
                    var bf = (BuiltinFunction)fn!; // ResolveCallable guarantees a closure or a builtin
                    if (TMode.Traced) TraceBuiltin(state, co, bf, Math.Max(top, fa + nargs + 1), true, fa + 1, nargs);
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
                            if (TMode.Traced) TraceBuiltin(state, co, bf, top, false, fa, wanted < 0 ? top - fa : wanted);
                            r = co.Stack;
                            break;
                        }
                    }
                    // A traced call keeps every result so the return hook can see them (debug.getinfo "r"); they are trimmed afterwards.
                    int end = CallBuiltin(state, co, bf, fa, nargs, TMode.Traced ? -1 : wanted, proto, pc - 1);
                    r = co.Stack;
                    if (wanted < 0) top = end;
                    if (TMode.Traced)
                    {
                        TraceBuiltin(state, co, bf, Math.Max(top, end), false, fa, end - fa);
                        r = co.Stack;
                        if (wanted >= 0)
                        {
                            for (int j = end - fa; j < wanted; j++) r[fa + j] = default;
                        }
                    }
                    else if (co.Traced)
                    {
                        // The builtin ran a nested loop in which a hook was set: carry on in the traced loop.
                        co.Reenter = true;
                        co.ResumeFrame = f;
                        co.ResumeTop = top;
                        return top;
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
                        int tdst = f.FuncIdx;
                        if (nargs <= 3) for (int j = 0; j <= nargs; j++) Regs.Copy(ref Regs.At(r, tdst + j), in Regs.At(r, fa + j)); // tdst < fa: forward copy is safe
                        else Array.Copy(r, fa, r, tdst, nargs + 1);
                        f = Precall(co, callee, f.FuncIdx, nargs, f.Wanted, f, proto, pc - 1);
                        f.IsTail = true;
                        goto newframe;
                    }
                    // Builtins cannot be tail-called in place: run it and let the Return that follows pass on its results.
                    f.Pc = pc;
                    var tbf = (BuiltinFunction)r[fa].Ref!;
                    if (TMode.Traced) TraceBuiltin(state, co, tbf, Math.Max(top, fa + nargs + 1), true, fa + 1, nargs);
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
                    if (TMode.Traced) TraceBuiltin(state, co, tbf, top, false, fa, top - fa);
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
                        TraceReturn(state, co, f, pc, Math.Max(top, ra + n), i.A + 1, n);
                        r = co.Stack;
                    }
                    if (f.Cont != Continuation.None)
                    {
                        Continuation cont = f.Cont;
                        int expect = f.ContDest;
                        LuaValue mres = n > 0 ? r[ra] : default;
                        FinishMeta(f, mres, r);
                        f = ResumeAfterMeta(state, co, cont, ref top);
                        if (cont == Continuation.Cond && Truthy(mres) != (expect != 0)) f.Pc++; // the compare's Jmp is not taken
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
                        if (n <= 3) for (int j = 0; j < n; j++) Regs.Copy(ref Regs.At(r, dst + j), in Regs.At(r, ra + j));
                        else Array.Copy(r, ra, r, dst, n);
                        top = dst + n;
                    }
                    else if (wanted == 1)
                    {
                        // The common case: one result, a scalar copy instead of Array.Copy (dst < ra, so no overlap hazard)
                        if (n > 0) Regs.Copy(ref Regs.At(r, dst), in Regs.At(r, ra));
                        else Regs.At(r, dst) = default;
                    }
                    else
                    {
                        int c = Math.Min(n, wanted);
                        if (c <= 3) for (int j = 0; j < c; j++) Regs.Copy(ref Regs.At(r, dst + j), in Regs.At(r, ra + j));
                        else Array.Copy(r, ra, r, dst, c);
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
                {
                    int ra = bs + i.A;
                    ref LuaValue idx = ref Regs.At(r, ra);
                    ref LuaValue cnt = ref Regs.At(r, ra + 1);
                    ref LuaValue stp = ref Regs.At(r, ra + 2);
                    if (stp.IsInteger)
                    {
                        ulong count = (ulong)cnt.AsInteger;
                        if (count == 0)
                        {
                            pc++;
                            break;
                        }
                        long next = unchecked(idx.AsInteger + stp.AsInteger);
                        Regs.SetBits(ref cnt, (long)(count - 1)); // the hidden loop state always holds integers here
                        Regs.SetBits(ref idx, next);
                        Regs.SetInteger(ref Regs.At(r, ra + 3), next);
                    }
                    else if (!ForLoopFloat(r, ra))
                    {
                        pc++;
                        break;
                    }
                    i = code[pc++]; // the Jmp back to the loop body: taken here (it is also the loop's poll point)
                    goto case OpCode.Jmp;
                }
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
                    if (Regs.At(r, bs + i.B).Ref is LuaTable t)
                    {
                        ref readonly LuaValue v = ref t.GetRef(in Regs.At(r, bs + i.C));
                        if (!v.IsNil || t.NoIndex)
                        {
                            Regs.Copy(ref Regs.At(r, bs + i.A), in v);
                            break;
                        }
                    }
                    goto indexslow;
                }
                case OpCode.GetField:
                {
                    if (Regs.At(r, bs + i.B).Ref is LuaTable t)
                    {
                        ref readonly LuaValue v = ref t.GetStrRef((byte[])Regs.At(k, i.C).Ref!);
                        if (!v.IsNil || t.NoIndex)
                        {
                            Regs.Copy(ref Regs.At(r, bs + i.A), in v);
                            break;
                        }
                    }
                    goto indexslow;
                }
                case OpCode.SetTable:
                {
                    if (Regs.At(r, bs + i.A).Ref is LuaTable t && t.NoNewIndex)
                    {
                        ref LuaValue key = ref Regs.At(r, bs + i.B);
                        if (ReferenceEquals(key.Ref, LuaValue.IntegerTag))
                        {
                            t.SetInt(key.Bits, in Regs.At(r, bs + i.C));
                            break;
                        }
                        if (key.Ref is byte[] s)
                        {
                            t.SetStr(s, in Regs.At(r, bs + i.C));
                            break;
                        }
                    }
                    goto setslow;
                }
                case OpCode.SetField:
                {
                    if (Regs.At(r, bs + i.A).Ref is LuaTable t && t.NoNewIndex)
                    {
                        t.SetStr((byte[])Regs.At(k, i.B).Ref!, in Regs.At(r, bs + i.C));
                        break;
                    }
                    goto setslow;
                }
                case OpCode.SetList:
                {
                    int offset = code[pc++].Ax;
                    int ra = bs + i.A;
                    int n = i.B != 0 ? i.B : top - ra - 1;
                    ((LuaTable)r[ra].Ref!).SetRange(offset, r, ra + 1, n);
                    break;
                }
                case OpCode.SelfK:
                {
                    ref LuaValue obj = ref Regs.At(r, bs + i.B);
                    byte[] key = (byte[])Regs.At(k, i.C).Ref!;
                    ref readonly LuaValue v = ref s_nilValue;
                    if (obj.Ref is LuaTable t) v = ref t.GetStrRef(key);
                    else if (obj.Ref is byte[]) v = ref StringMethod(state, key);
                    if (!v.IsNil)
                    {
                        Regs.Copy(ref Regs.At(r, bs + i.A + 1), in obj);
                        Regs.Copy(ref Regs.At(r, bs + i.A), in v);
                        break;
                    }
                    goto indexslow;
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
                    Frame? nf = ConcatOp(state, co, f, i, bs, pc, proto);
                    if (nf is not null)
                    {
                        f = nf;
                        goto newframe;
                    }
                    r = co.Stack;
                    break;
                }
                case OpCode.TForLoop:
                    if (!r[bs + i.A + 4].IsNil) r[bs + i.A + 2] = r[bs + i.A + 4];
                    else pc++;
                    break;
                default:
                    throw new InvalidOperationException($"bad opcode {i.Op}");
                arithslow:
                {
                    Frame? nf = ArithSlow(state, co, f, i, bs, pc, proto, k);
                    if (nf is not null)
                    {
                        f = nf;
                        goto newframe;
                    }
                    r = co.Stack;
                    break;
                }
                indexslow:
                {
                    Frame? nf = IndexSlow(state, co, f, i, bs, pc, proto, k);
                    if (nf is not null)
                    {
                        f = nf;
                        goto newframe;
                    }
                    r = co.Stack;
                    break;
                }
                setslow:
                {
                    Frame? nf = SetSlow(state, co, f, i, bs, pc, proto, k);
                    if (nf is not null)
                    {
                        f = nf;
                        goto newframe;
                    }
                    r = co.Stack;
                    break;
                }
                condslow:
                {
                    Frame? nf = CondSlow(state, co, f, i, bs, pc, proto, k, out bool take);
                    if (nf is not null)
                    {
                        f = nf;
                        goto newframe;
                    }
                    r = co.Stack;
                    if (take)
                    {
                        i = code[pc++];
                        goto case OpCode.Jmp;
                    }
                    pc++;
                    break;
                }
            }
        }
    }

    private static readonly LuaValue s_nilValue = default;

    /// <summary>The entry for <paramref name="key"/> in the table the string metatable's __index names (the <c>string</c> library), or nil when there is none.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ref readonly LuaValue StringMethod(LuaState state, byte[] key)
    {
        LuaTable? smt = state.StringMetatable;
        if (smt is not null && smt.GetTagMethod(TagMethod.Index).Ref is LuaTable idx) return ref idx.GetStrRef(key);
        return ref s_nilValue;
    }

    /// <summary>
    /// GetTable, GetField, Self and SelfK when the inline lookup did not settle it: a miss on a table with a metatable, a non-table receiver, or an error.
    /// Returns the Frame of an __index function that finishes the instruction, or null when the result is stored.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Frame? IndexSlow(LuaState state, Coroutine co, Frame f, Instruction i, int bs, int pc, Proto proto, LuaValue[] k)
    {
        LuaValue[] r = co.Stack;
        OpCode op = i.Op;
        LuaValue obj = r[bs + i.B];
        LuaValue key = op is OpCode.GetField or OpCode.SelfK ? k[i.C] : r[bs + i.C];
        if (op is OpCode.Self or OpCode.SelfK) r[bs + i.A + 1] = obj;
        if (Meta.PrepareIndex(state, obj, key, proto, pc - 1, out LuaValue res, out MetaCall mc))
        {
            Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out res);
            if (nf is not null) return nf;
        }
        co.Stack[bs + i.A] = res;
        return null;
    }

    /// <summary>SetTable and SetField when the inline store did not apply: a metatable with __newindex, a non-table, an unusual key, or an error.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Frame? SetSlow(LuaState state, Coroutine co, Frame f, Instruction i, int bs, int pc, Proto proto, LuaValue[] k)
    {
        LuaValue[] r = co.Stack;
        LuaValue key = i.Op == OpCode.SetField ? k[i.B] : r[bs + i.B];
        if (!Meta.PrepareSetIndex(state, r[bs + i.A], key, r[bs + i.C], proto, pc - 1, out MetaCall mc)) return null;
        return StartMeta(state, co, f, in mc, Continuation.Discard, 0, pc, proto, out _);
    }

    /// <summary>
    /// Arithmetic the inline paths did not take: mixed or unusual number types, IDiv/Mod/Pow, strings, metamethods, errors.
    /// Returns the Frame of a metamethod call that finishes the instruction, or null when the result is stored.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Frame? ArithSlow(LuaState state, Coroutine co, Frame f, Instruction i, int bs, int pc, Proto proto, LuaValue[] k)
    {
        LuaValue[] r = co.Stack;
        OpCode op = i.Op;
        LuaValue lhs = r[bs + i.B];
        LuaValue rhs = Meta.IsArithK(op) ? k[i.C] : r[bs + i.C];
        if (lhs.IsNumber && rhs.IsNumber)
        {
            r[bs + i.A] = op is OpCode.BAnd or OpCode.BOr or OpCode.BXor or OpCode.Shl or OpCode.Shr
                ? Arith.Bitwise(op, lhs, rhs, proto, pc - 1)
                : Arith.Binary(Meta.BaseArith(op), lhs, rhs, proto, pc - 1);
            return null;
        }
        MetaCall mc = Meta.PrepareArith(state, Meta.ArithEvent(op), lhs, rhs, proto, pc - 1);
        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out LuaValue res);
        if (nf is not null) return nf;
        co.Stack[bs + i.A] = res;
        return null;
    }

    /// <summary>
    /// A fused compare-and-jump whose operands are not two integers or two floats: mixed numbers, strings, metamethods, errors.
    /// Returns the Frame of a metamethod call that finishes the instruction, or null with <paramref name="take"/> saying whether the jump is taken.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Frame? CondSlow(LuaState state, Coroutine co, Frame f, Instruction i, int bs, int pc, Proto proto, LuaValue[] k, out bool take)
    {
        LuaValue[] r = co.Stack;
        OpCode op = i.Op;
        LuaValue cb = r[bs + i.B];
        LuaValue cc = op >= OpCode.JEqK ? k[i.C] : r[bs + i.C];
        bool swap = op is OpCode.JGtK or OpCode.JGeK; // K[C] < R[B]
        LuaValue lhs = swap ? cc : cb, rhs = swap ? cb : cc;
        bool res;
        bool needMeta;
        MetaCall mc;
        take = false;
        if (op is OpCode.JEq or OpCode.JEqK) needMeta = Meta.PrepareEqual(lhs, rhs, out res, out mc);
        else
        {
            bool lt = op is OpCode.JLt or OpCode.JLtK or OpCode.JGtK;
            if (lhs.IsNumber && rhs.IsNumber)
            {
                res = lt ? Compare.LessThan(lhs, rhs, proto, pc - 1) : Compare.LessEqual(lhs, rhs, proto, pc - 1);
                needMeta = false;
                mc = default;
            }
            else needMeta = Meta.PrepareOrder(state, lt ? TagMethod.Lt : TagMethod.Le, lhs, rhs, proto, pc - 1, out res, out mc);
        }
        if (needMeta)
        {
            Frame? nf = StartMeta(state, co, f, in mc, Continuation.Cond, i.A, pc, proto, out LuaValue mres);
            if (nf is not null) return nf;
            res = !mres.IsFalsy;
        }
        take = res == (i.A != 0);
        return null;
    }

    /// <summary>Concat out of line: the dispatch loop has a limited inlining budget. Returns a metamethod Frame, or null when the result is stored.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Frame? ConcatOp(LuaState state, Coroutine co, Frame f, Instruction i, int bs, int pc, Proto proto)
    {
        LuaValue[] r = co.Stack;
        LuaValue lhs = r[bs + i.B], rhs = r[bs + i.C];
        if ((lhs.IsString || lhs.IsNumber) && (rhs.IsString || rhs.IsNumber))
        {
            r[bs + i.A] = StringConv.Concat(state, lhs, rhs);
            return null;
        }
        MetaCall mc = Meta.PrepareConcat(state, lhs, rhs, proto, pc - 1);
        Frame? nf = StartMeta(state, co, f, in mc, Continuation.Value, bs + i.A, pc, proto, out LuaValue res);
        if (nf is not null) return nf;
        co.Stack[bs + i.A] = res;
        return null;
    }

    /// <summary>The truthiness of a metamethod's result; out of line so the dispatch loop's inlining budget goes to the hot paths.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool Truthy(LuaValue v) => !v.IsFalsy;

    private interface IArith
    {
        static abstract long Int(long a, long b);
        static abstract double Flt(double a, double b);
        /// <summary>False for an operator whose result on two integers is a float (Div).</summary>
        static abstract bool IntResult { get; }
    }

    private struct AddOp : IArith
    {
        public static long Int(long a, long b) => unchecked(a + b);
        public static double Flt(double a, double b) => a + b;
        public static bool IntResult => true;
    }

    private struct SubOp : IArith
    {
        public static long Int(long a, long b) => unchecked(a - b);
        public static double Flt(double a, double b) => a - b;
        public static bool IntResult => true;
    }

    private struct MulOp : IArith
    {
        public static long Int(long a, long b) => unchecked(a * b);
        public static double Flt(double a, double b) => a * b;
        public static bool IntResult => true;
    }

    private struct DivOp : IArith
    {
        public static long Int(long a, long b) => 0;
        public static double Flt(double a, double b) => a / b;
        public static bool IntResult => false;
    }

    /// <summary>The number-only fast path of Add/Sub/Mul/Div: stores the result and returns true, or returns false for anything else.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool ArithFast<TOp>(ref LuaValue ra, ref LuaValue rb, ref LuaValue rc) where TOp : struct, IArith
    {
        // Two floats or two integers; mixed operands take the out-of-line path (this is inlined at every arithmetic opcode)
        if (rb.IsFloat)
        {
            if (!rc.IsFloat) return false;
            Regs.SetFloat(ref ra, TOp.Flt(rb.AsFloat, rc.AsFloat));
            return true;
        }
        if (!rb.IsInteger || !rc.IsInteger) return false;
        if (TOp.IntResult) Regs.SetInteger(ref ra, TOp.Int(rb.AsInteger, rc.AsInteger));
        else Regs.SetFloat(ref ra, TOp.Flt(rb.AsInteger, rc.AsInteger));
        return true;
    }

    /// <summary>The Call instruction's Lua-to-Lua path: a fixed-arity callee is set up inline, anything else goes through <see cref="Precall"/>.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static Frame PrecallLua(Coroutine co, LuaClosure cl, int fa, int nargs, int wanted, Proto whereProto, int wherePc)
    {
        Proto p = cl.Proto;
        if (p.IsVararg) return Precall(co, cl, fa, nargs, wanted, null, whereProto, wherePc);
        int baseIdx = fa + 1;
        var st = co.Stack;
        if (baseIdx + p.MaxStack > st.Length)
        {
            co.EnsureStack(baseIdx + p.MaxStack, whereProto, wherePc);
            st = co.Stack;
        }
        for (int j = nargs; j < p.NumParams; j++) Regs.At(st, baseIdx + j) = default;
        Frame f = co.PushFrame();
        if (!ReferenceEquals(f.Closure, cl)) f.Closure = cl; // recursion and loops reuse the same Frame and closure: skip the write barrier
        f.Base = baseIdx;
        f.Pc = 0;
        f.FuncIdx = fa;
        f.Wanted = wanted;
        f.VarargBase = 0;
        f.VarargCount = 0;
        return f;
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
        return CallBuiltin(state, co, fn, fa, nargs, wanted, proto, pc);
    }

    /// <summary>As above, for a caller that already knows the builtin (the Call instruction).</summary>
    private static int CallBuiltin(LuaState state, Coroutine co, BuiltinFunction fn, int fa, int nargs, int wanted, Proto? proto, int pc)
    {
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
        if (wanted == 1)
        {
            r[fa] = n > 0 ? res![0] : default;
            Rets.Release(res);
            return fa + 1;
        }
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

    /// <summary>Advances a float loop (the integer loop is inline in the dispatch); returns true when another iteration should run.</summary>
    private static bool ForLoopFloat(LuaValue[] r, int ra)
    {
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
