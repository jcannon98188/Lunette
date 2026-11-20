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
}
