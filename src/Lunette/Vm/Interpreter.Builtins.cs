using Lunette.Lib;

namespace Lunette.Vm;

/// <summary>
/// In-place bodies for the hottest library functions (ticket 47): the arguments are read from the stack and the result is written to it,
/// with no argument span and no result array. Anything unusual (wrong types, missing or extra arguments, out-of-range positions) returns
/// <see cref="Special.None"/> so the ordinary builtin body runs and reports the error exactly as before.
/// </summary>
internal static partial class Interpreter
{
    /// <summary>The first kind <see cref="CallFast"/> handles (the enum lists them last).</summary>
    private const BuiltinKind FastFirst = BuiltinKind.MathAbs;

    private static Special CallFast(LuaState state, Coroutine co, BuiltinKind kind, int fa, int nargs, int wanted)
    {
        var st = co.Stack;
        if (nargs == 0) return Special.None;
        ref LuaValue a = ref st[fa + 1];
        switch (kind)
        {
            case BuiltinKind.MathAbs:
                if (a.IsInteger) return DeliverOne(st, co, fa, wanted, LuaValue.FromInteger(a.AsInteger < 0 ? unchecked(0 - a.AsInteger) : a.AsInteger));
                if (a.IsFloat) return DeliverOne(st, co, fa, wanted, LuaValue.FromFloat(Math.Abs(a.AsFloat)));
                return Special.None;
            case BuiltinKind.MathSqrt:
                if (!a.IsNumber) return Special.None;
                return DeliverOne(st, co, fa, wanted, LuaValue.FromFloat(Math.Sqrt(a.IsInteger ? a.AsInteger : a.AsFloat)));
            case BuiltinKind.MathSin:
                if (!a.IsNumber) return Special.None;
                return DeliverOne(st, co, fa, wanted, LuaValue.FromFloat(Math.Sin(a.IsInteger ? a.AsInteger : a.AsFloat)));
            case BuiltinKind.MathCos:
                if (!a.IsNumber) return Special.None;
                return DeliverOne(st, co, fa, wanted, LuaValue.FromFloat(Math.Cos(a.IsInteger ? a.AsInteger : a.AsFloat)));
            case BuiltinKind.MathFloor:
            {
                if (a.IsInteger) return DeliverOne(st, co, fa, wanted, a);
                if (!a.IsFloat) return Special.None;
                double fl = Math.Floor(a.AsFloat);
                return DeliverOne(st, co, fa, wanted, LuaNumber.FloatToInteger(fl, out long n) ? LuaValue.FromInteger(n) : LuaValue.FromFloat(fl));
            }
            case BuiltinKind.StringLen:
                if (a.Ref is not byte[] ls) return Special.None;
                return DeliverOne(st, co, fa, wanted, LuaValue.FromInteger(new LuaString(ls).Length));
            case BuiltinKind.StringByte:
            {
                if (a.Ref is not byte[] bs || nargs > 2) return Special.None;
                long pos = 1;
                if (nargs == 2)
                {
                    if (!st[fa + 2].IsInteger) return Special.None;
                    pos = st[fa + 2].AsInteger;
                }
                var s = new LuaString(bs);
                if (pos < 1 || pos > s.Length) return Special.None; // negative positions and misses: the builtin
                return DeliverOne(st, co, fa, wanted, LuaValue.FromInteger(s.Bytes[(int)pos - 1]));
            }
            case BuiltinKind.Type:
                return DeliverOne(st, co, fa, wanted, state.TypeNameValue(a));
            case BuiltinKind.Select:
            {
                if (a.IsInteger)
                {
                    long n = a.AsInteger;
                    if (n < 1) return Special.None;
                    int cnt = n >= nargs ? 0 : nargs - (int)n;
                    int from = fa + 1 + (int)Math.Min(n, nargs);
                    int copy = wanted < 0 ? cnt : Math.Min(cnt, wanted);
                    for (int j = 0; j < copy; j++) st[fa + j] = st[from + j];
                    if (wanted < 0) co.ResumeTop = fa + cnt;
                    else for (int j = copy; j < wanted; j++) st[fa + j] = default;
                    return Special.Done;
                }
                if (a.Ref is byte[] hs && hs.Length == 9 && new LuaString(hs).Bytes[0] == (byte)'#')
                    return DeliverOne(st, co, fa, wanted, LuaValue.FromInteger(nargs - 1));
                return Special.None;
            }
            case BuiltinKind.SetMetatable:
            {
                // Giving a fresh table (no metatable yet, so nothing protected) its metatable: the object-construction idiom.
                if (nargs != 2 || a.Ref is not LuaTable smt || smt.Metatable is not null) return Special.None;
                LuaValue mv = st[fa + 2];
                if (mv.Ref is not LuaTable && !mv.IsNil) return Special.None;
                smt.Metatable = mv.Ref as LuaTable;
                state.Gc.OnSetMetatable(smt, smt.Metatable);
                return DeliverOne(st, co, fa, wanted, a);
            }
            case BuiltinKind.Tonumber:
                if (nargs != 1) return Special.None;
                return DeliverOne(st, co, fa, wanted, StringConv.TryToNumber(a, out LuaValue tn) ? tn : default);
            case BuiltinKind.MathMax:
            case BuiltinKind.MathMin:
            {
                if (nargs != 2 || !a.IsNumber || !st[fa + 2].IsNumber) return Special.None;
                LuaValue b2 = st[fa + 2];
                bool pickB = kind == BuiltinKind.MathMax ? Compare.NumberLessThan(a, b2) : Compare.NumberLessThan(b2, a);
                return DeliverOne(st, co, fa, wanted, pickB ? b2 : a);
            }
            case BuiltinKind.TableUnpack:
            {
                // unpack(t [, i [, j]]) on a plain table with a short range: the elements go straight to the stack
                if (a.Ref is not LuaTable { Metatable: null } ut || nargs > 3) return Special.None;
                long lo = 1, hi;
                if (nargs >= 2)
                {
                    if (!st[fa + 2].IsInteger) return Special.None;
                    lo = st[fa + 2].AsInteger;
                }
                if (nargs == 3)
                {
                    if (!st[fa + 3].IsInteger) return Special.None;
                    hi = st[fa + 3].AsInteger;
                }
                else hi = ut.Length();
                int cnt = 0;
                if (lo <= hi)
                {
                    ulong span = (ulong)hi - (ulong)lo;
                    if (span >= 64) return Special.None;
                    cnt = (int)span + 1;
                }
                if (wanted < 0 && fa + cnt >= st.Length) return Special.None;
                int copy = wanted < 0 ? cnt : Math.Min(cnt, wanted);
                for (int j = 0; j < copy; j++) st[fa + j] = ut.GetInt(lo + j);
                if (wanted < 0) co.ResumeTop = fa + cnt;
                else for (int j = copy; j < wanted; j++) st[fa + j] = default;
                return Special.Done;
            }
            default:
                return Special.None;
        }
    }

    /// <summary>Places a builtin's single result at <paramref name="fa"/> the way CallBuiltin would: padded to <paramref name="wanted"/> with nils, or setting the top when it is negative.</summary>
    private static Special DeliverOne(LuaValue[] st, Coroutine co, int fa, int wanted, in LuaValue v)
    {
        st[fa] = v;
        if (wanted < 0)
        {
            co.ResumeTop = fa + 1;
            return Special.Done;
        }
        for (int j = 1; j < wanted; j++) st[fa + j] = default;
        return Special.Done;
    }

    /// <summary>
    /// The generic-for step for <c>next</c> and the <c>ipairs</c> iterator (ticket 48): the loop state is at <paramref name="ra"/>..ra+2 and the loop variables
    /// start at ra+4. Writes the variables in place and returns true, or returns false having changed nothing (wrong types, a missing key, an
    /// <c>__index</c> to consult) so the ordinary instruction sequence runs and reports exactly what it always did. Only the untraced loop calls it.
    /// </summary>
    private static bool TForFused(Coroutine co, BuiltinKind kind, int ra, int nvars)
    {
        var st = co.Stack;
        if (st[ra + 1].Ref is not LuaTable t) return false;
        if (kind == BuiltinKind.Next)
        {
            int step = t.Step(st[ra + 2], out LuaValue nk, out LuaValue nv);
            if (step < 0) return false;
            if (step == 0)
            {
                st[ra + 4] = default;
                return true;
            }
            st[ra + 4] = nk;
            if (nvars > 1)
            {
                st[ra + 5] = nv;
                for (int j = 2; j < nvars; j++) st[ra + 4 + j] = default;
            }
            return true;
        }
        if (!st[ra + 2].IsInteger) return false;
        long idx = unchecked(st[ra + 2].AsInteger + 1);
        ref readonly LuaValue iv = ref t.GetIntRef(idx);
        if (iv.IsNil)
        {
            if (!t.NoIndex) return false;
            st[ra + 4] = default;
            return true;
        }
        st[ra + 4] = LuaValue.FromInteger(idx);
        if (nvars > 1)
        {
            st[ra + 5] = iv;
            for (int j = 2; j < nvars; j++) st[ra + 4 + j] = default;
        }
        return true;
    }
}
