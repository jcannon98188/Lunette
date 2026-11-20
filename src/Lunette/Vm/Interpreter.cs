using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>
/// The VM loop. Lua-to-Lua calls push heap Frames and continue in this one loop; the .NET
/// stack never grows with Lua call depth (ADR 0002).
/// </summary>
internal static class Interpreter
{
    private const double Two63 = 9223372036854775808.0;

    public static void Execute(LuaState state, Proto proto)
    {
        var co = state.Main;
        co.Reset();
        var main = new LuaClosure(proto, []);
        co.Stack[0] = LuaValue.FromObject(main);
        Frame entry = Precall(co, main, 0, 0, 0, null, null, 0);
        Run(state, co, entry);
    }

    private static void Run(LuaState state, Coroutine co, Frame f)
    {
        LuaClosure cl;
        Proto proto;
        Instruction[] code;
        LuaValue[] k;
        LuaValue[] r;
        int bs, pc;
        int top = 0;

    newframe:
        cl = f.Closure;
        proto = cl.Proto;
        code = proto.Code;
        k = proto.Constants;
        r = co.Stack;
        bs = f.Base;
        pc = f.Pc;

        while (true)
        {
            Instruction i = code[pc++];
            switch (i.Op)
            {
                case OpCode.Move:
                    r[bs + i.A] = r[bs + i.B];
                    break;
                case OpCode.LoadK:
                    r[bs + i.A] = k[i.Bx];
                    break;
                case OpCode.LoadNil:
                    for (int j = 0; j <= i.B; j++) r[bs + i.A + j] = default;
                    break;
                case OpCode.LoadBool:
                    r[bs + i.A] = LuaValue.FromBoolean(i.B != 0);
                    break;
                case OpCode.GetGlobal:
                    r[bs + i.A] = state.Globals.GetValueOrDefault(proto.Names[i.Bx]);
                    break;
                case OpCode.SetGlobal:
                    state.Globals[proto.Names[i.Bx]] = r[bs + i.A];
                    break;
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
                    r[bs + i.A] = Arith.Binary(i.Op, r[bs + i.B], r[bs + i.C], proto, pc - 1);
                    break;
                case OpCode.BAnd:
                case OpCode.BOr:
                case OpCode.BXor:
                case OpCode.Shl:
                case OpCode.Shr:
                    r[bs + i.A] = Arith.Bitwise(i.Op, r[bs + i.B], r[bs + i.C], proto, pc - 1);
                    break;
                case OpCode.BNot:
                    r[bs + i.A] = Arith.BitwiseNot(r[bs + i.B], proto, pc - 1);
                    break;
                case OpCode.Unm:
                    r[bs + i.A] = Arith.Negate(r[bs + i.B], proto, pc - 1);
                    break;
                case OpCode.Not:
                    r[bs + i.A] = LuaValue.FromBoolean(r[bs + i.B].IsFalsy);
                    break;
                case OpCode.Eq:
                    r[bs + i.A] = LuaValue.FromBoolean(LuaValue.RawEquals(r[bs + i.B], r[bs + i.C]));
                    break;
                case OpCode.Lt:
                    r[bs + i.A] = LuaValue.FromBoolean(Compare.LessThan(r[bs + i.B], r[bs + i.C], proto, pc - 1));
                    break;
                case OpCode.Le:
                    r[bs + i.A] = LuaValue.FromBoolean(Compare.LessEqual(r[bs + i.B], r[bs + i.C], proto, pc - 1));
                    break;
                case OpCode.Test:
                    if (!r[bs + i.A].IsFalsy != (i.C != 0)) pc++;
                    break;
                case OpCode.Jmp:
                    pc = i.Ax;
                    break;
                case OpCode.Call:
                {
                    int fa = bs + i.A;
                    int nargs = i.B != 0 ? i.B - 1 : top - fa - 1;
                    int wanted = i.C - 1;
                    f.Pc = pc;
                    if (r[fa].Ref is LuaClosure callee)
                    {
                        f = Precall(co, callee, fa, nargs, wanted, null, proto, pc - 1);
                        goto newframe;
                    }
                    int end = CallBuiltin(state, co, fa, nargs, wanted, proto, pc - 1);
                    r = co.Stack;
                    if (wanted < 0) top = end;
                    break;
                }
                case OpCode.TailCall:
                {
                    int fa = bs + i.A;
                    int nargs = i.B != 0 ? i.B - 1 : top - fa - 1;
                    if (r[fa].Ref is LuaClosure callee)
                    {
                        if (co.OpenUpvals.Count > 0) co.Close(bs);
                        Array.Copy(r, fa, r, f.FuncIdx, nargs + 1);
                        f = Precall(co, callee, f.FuncIdx, nargs, f.Wanted, f, proto, pc - 1);
                        goto newframe;
                    }
                    // Builtins cannot be tail-called in place: run it and let the Return that follows pass on its results.
                    top = CallBuiltin(state, co, fa, nargs, -1, proto, pc - 1);
                    r = co.Stack;
                    break;
                }
                case OpCode.Return:
                {
                    int ra = bs + i.A;
                    int n = i.B != 0 ? i.B - 1 : top - ra;
                    if (co.OpenUpvals.Count > 0) co.Close(bs);
                    int dst = f.FuncIdx;
                    int wanted = f.Wanted;
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
                    if (--co.Depth == 0) return;
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
    private static int CallBuiltin(LuaState state, Coroutine co, int fa, int nargs, int wanted, Proto proto, int pc)
    {
        if (co.Stack[fa].Ref is not BuiltinFunction fn)
            throw new LuaException($"{Where(proto, pc)} attempt to call a {co.Stack[fa].TypeName} value");
        var res = fn.Invoke(state, new ReadOnlySpan<LuaValue>(co.Stack, fa + 1, nargs));
        int n = res?.Length ?? 0;
        if (wanted < 0)
        {
            co.EnsureStack(fa + n, proto, pc);
            res?.CopyTo(co.Stack, fa);
            return fa + n;
        }
        var r = co.Stack;
        int c = Math.Min(n, wanted);
        for (int j = 0; j < c; j++) r[fa + j] = res![j];
        for (int j = c; j < wanted; j++) r[fa + j] = default;
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
        if (!limit.TryToNumber(out double fl)) throw new LuaException($"{Where(p, pc)} 'for' limit must be a number");
        if (!step.TryToNumber(out double fs)) throw new LuaException($"{Where(p, pc)} 'for' step must be a number");
        if (!init.TryToNumber(out double fi)) throw new LuaException($"{Where(p, pc)} 'for' initial value must be a number");
        if (fs == 0) throw new LuaException($"{Where(p, pc)} 'for' step is zero");
        if (fs > 0 ? fl < fi : fi < fl) return false;
        r[ra] = LuaValue.FromFloat(fi);
        r[ra + 1] = LuaValue.FromFloat(fl);
        r[ra + 2] = LuaValue.FromFloat(fs);
        r[ra + 3] = r[ra];
        return true;
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
        if (!limit.IsFloat) throw new LuaException($"{Where(p, pc)} 'for' limit must be a number");
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
        string n = p.ChunkName;
        string name = n.Length > 0 && (n[0] is '=' or '@') ? n[1..] : $"[string \"{n}\"]";
        return $"{name}:{p.Lines[pc]}:";
    }
}
