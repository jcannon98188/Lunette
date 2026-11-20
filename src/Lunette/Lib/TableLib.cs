using Lunette.Lib;
using Lunette.Vm;

namespace Lunette;

/// <summary>The <c>table</c> library, after ltablib.c. Every access honours __index, __newindex and __len.</summary>
internal static class TableLib
{
    private const int CanRead = 1, CanWrite = 2, CanLen = 4;

    public static void Register(LuaState state)
    {
        if (!state.Wants("table")) return;
        var t = new LuaTable(0, 8);
        Add(t, "insert", Insert);
        Add(t, "remove", Remove);
        Add(t, "concat", Concat);
        Add(t, "unpack", Unpack);
        Add(t, "pack", Pack);
        Add(t, "move", Move);
        Add(t, "sort", Sort);
        state.SetGlobal("table", LuaValue.FromTable(t));
    }

    private static void Add(LuaTable t, string name, BuiltinBody body) =>
        t.Set(LuaValue.FromString(name), LuaValue.FromObject(new BuiltinFunction(body)));

    private static LuaValue Geti(LuaState state, LuaValue t, long i) =>
        t.Ref is LuaTable { Metatable: null } tb ? tb.GetInt(i) : Meta.Index(state, t, LuaValue.FromInteger(i), null, 0);

    private static void Seti(LuaState state, LuaValue t, long i, LuaValue v)
    {
        if (t.Ref is LuaTable { Metatable: null } tb) tb.SetInt(i, v);
        else Meta.SetIndex(state, t, LuaValue.FromInteger(i), v, null, 0);
    }

    /// <summary>luaL_len: the length with __len honoured, which must be an integer.</summary>
    private static long Len(LuaState state, LuaValue t)
    {
        if (t.Ref is LuaTable { Metatable: null } tb) return tb.Length();
        LuaValue n = Meta.Len(state, t, null, 0);
        if (n.IsInteger) return n.AsInteger;
        if (n.IsFloat && LuaNumber.FloatToInteger(n.AsFloat, out long l)) return l;
        throw LuaErrors.Error("object length is not an integer");
    }

    /// <summary>A table, or any value whose metatable provides the metamethods the operation needs.</summary>
    private static LuaValue CheckTab(LuaState state, ReadOnlySpan<LuaValue> args, int i, string fn, int what)
    {
        if (i < args.Length)
        {
            LuaValue v = args[i];
            if (v.Ref is LuaTable) return v;
            var mt = Meta.GetMetatable(state, v);
            if (mt is not null
                && ((what & CanRead) == 0 || !mt.GetTagMethod(TagMethod.Index).IsNil)
                && ((what & CanWrite) == 0 || !mt.GetTagMethod(TagMethod.NewIndex).IsNil)
                && ((what & CanLen) == 0 || !mt.GetTagMethod(TagMethod.Len).IsNil))
                return v;
        }
        throw LuaErrors.TypeError(args, i, fn, "table");
    }

    private static long CheckInt(ReadOnlySpan<LuaValue> args, int i, string fn)
    {
        try { return LibArgs.CheckInteger(fn, args, i); }
        catch (LuaException e) { throw LuaErrors.Error(e.Message); }
    }

    private static LuaValue[]? Insert(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaValue t = CheckTab(state, args, 0, "insert", CanRead | CanWrite | CanLen);
        long e = unchecked(Len(state, t) + 1);
        long pos;
        switch (args.Length)
        {
            case 2:
                pos = e;
                break;
            case 3:
                pos = CheckInt(args, 1, "insert");
                if ((ulong)pos - 1UL >= (ulong)e) throw LuaErrors.ArgError(2, "insert", "position out of bounds");
                for (long i = e; i > pos; i--) Seti(state, t, i, Geti(state, t, i - 1));
                break;
            default:
                throw LuaErrors.Error("wrong number of arguments to 'insert'");
        }
        Seti(state, t, pos, args[^1]);
        return null;
    }

    private static LuaValue[] Remove(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaValue t = CheckTab(state, args, 0, "remove", CanRead | CanWrite | CanLen);
        long size = Len(state, t);
        long pos = args.Length > 1 && !args[1].IsNil ? CheckInt(args, 1, "remove") : size;
        if (pos != size && (ulong)pos - 1UL > (ulong)size)
            throw LuaErrors.ArgError(2, "remove", "position out of bounds");
        LuaValue removed = Geti(state, t, pos);
        for (; pos < size; pos++) Seti(state, t, pos, Geti(state, t, pos + 1));
        Seti(state, t, pos, default);
        return Rets.One(removed);
    }

    private static LuaValue[] Concat(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaValue t = CheckTab(state, args, 0, "concat", CanRead | CanLen);
        LuaString sep = args.Length > 1 && !args[1].IsNil ? LibArgs.CheckString(state, "concat", args, 1) : LuaString.Empty;
        long i = args.Length > 2 && !args[2].IsNil ? CheckInt(args, 2, "concat") : 1;
        long last = args.Length > 3 && !args[3].IsNil ? CheckInt(args, 3, "concat") : Len(state, t);
        using var b = new ByteBuilder(64);
        for (; i <= last; i++)
        {
            LuaValue v = Geti(state, t, i);
            if (v.Ref is byte[] s)
            {
                state.CheckAlloc((long)b.Length + s.Length);
                b.Append(new LuaString(s).Bytes);
            }
            else if (v.IsNumber) b.Append(StringConv.NumberToString(state, v).Bytes);
            else throw LuaErrors.Error($"invalid value ({v.TypeName}) at index {i} in table for 'concat'");
            if (i != last) b.Append(sep.Bytes);
            if (i == long.MaxValue) break;
        }
        return Rets.One(LuaValue.FromString(b.ToLuaString(state.Strings)));
    }

    private static LuaValue[] Unpack(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaValue t = args.Length > 0 ? args[0] : default;
        long i = args.Length > 1 && !args[1].IsNil ? CheckInt(args, 1, "unpack") : 1;
        long e = args.Length > 2 && !args[2].IsNil ? CheckInt(args, 2, "unpack") : Len(state, t);
        if (i > e) return [];
        ulong n = (ulong)e - (ulong)i;
        if (n >= 1_000_000UL) throw LuaErrors.Error("too many results to unpack");
        state.CheckAlloc(((long)n + 1) * 16);
        var res = new LuaValue[(int)n + 1];
        for (int k = 0; k < res.Length; k++) res[k] = Geti(state, t, i + k);
        return res;
    }

    private static LuaValue[] Pack(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        var t = new LuaTable(args.Length, 1);
        for (int i = 0; i < args.Length; i++) t.SetInt(i + 1, args[i]);
        t.Set(LuaValue.FromString(state.Strings.FromUtf16("n")), LuaValue.FromInteger(args.Length));
        return Rets.One(LuaValue.FromTable(t));
    }

    private static LuaValue[] Move(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaValue a1 = CheckTab(state, args, 0, "move", CanRead);
        long f = CheckInt(args, 1, "move");
        long e = CheckInt(args, 2, "move");
        long t = CheckInt(args, 3, "move");
        int tt = args.Length > 4 && !args[4].IsNil ? 4 : 0;
        LuaValue a2 = CheckTab(state, args, tt, "move", CanWrite);
        if (e >= f)
        {
            if (!(f > 0 || e < long.MaxValue + f)) throw LuaErrors.ArgError(3, "move", "too many elements to move");
            long n = e - f + 1;
            if (t > long.MaxValue - n + 1) throw LuaErrors.ArgError(4, "move", "destination wrap around");
            if (t > e || t <= f || (tt != 0 && !Meta.Equal(state, a1, a2)))
                for (long i = 0; i < n; i++) Seti(state, a2, t + i, Geti(state, a1, f + i));
            else
                for (long i = n - 1; i >= 0; i--) Seti(state, a2, t + i, Geti(state, a1, f + i));
        }
        return Rets.One(a2);
    }

    // ---- sort: the quicksort of ltablib.c, so "invalid order function" is detected the same way ----

    private const int RanLimit = 100;

    private ref struct Sorter
    {
        public LuaState State;
        public LuaValue Table;
        public LuaValue Cmp;

        public bool Lt(LuaValue a, LuaValue b)
        {
            if (Cmp.IsNil)
            {
                if (a.IsNumber && b.IsNumber) return Compare.NumberLessThan(a, b);
                return Meta.Order(State, TagMethod.Lt, a, b, null, 0);
            }
            return !Interpreter.Call1(State, Cmp, a, b, default, 2).IsFalsy;
        }

        public LuaValue Get(long i) => Geti(State, Table, i);
        public void Set(long i, LuaValue v) => Seti(State, Table, i, v);

        public void AuxSort(long lo, long up, uint rnd)
        {
            while (lo < up)
            {
                LuaValue a = Get(lo), b = Get(up);
                if (Lt(b, a)) { Set(lo, b); Set(up, a); }
                if (up - lo == 1) break;
                long p = up - lo < RanLimit || rnd == 0 ? (lo + up) / 2 : ChoosePivot(lo, up, rnd);
                LuaValue vp = Get(p), vlo = Get(lo);
                if (Lt(vp, vlo)) { Set(p, vlo); Set(lo, vp); }
                else
                {
                    LuaValue vup = Get(up);
                    if (Lt(vup, vp)) { Set(p, vup); Set(up, vp); }
                }
                if (up - lo == 2) break;
                LuaValue pivot = Get(p);
                LuaValue beforeUp = Get(up - 1);
                Set(p, beforeUp);
                Set(up - 1, pivot);
                p = Partition(lo, up, pivot);
                long n;
                if (p - lo < up - p)
                {
                    AuxSort(lo, p - 1, rnd);
                    n = p - lo;
                    lo = p + 1;
                }
                else
                {
                    AuxSort(p + 1, up, rnd);
                    n = up - p;
                    up = p - 1;
                }
                if ((up - lo) / 128 > n) rnd = (uint)Random.Shared.Next();
            }
        }

        private static long ChoosePivot(long lo, long up, uint rnd)
        {
            long r4 = (up - lo) / 4;
            return (long)(rnd % (ulong)(r4 * 2)) + lo + r4;
        }

        private long Partition(long lo, long up, LuaValue pivot)
        {
            long i = lo, j = up - 1;
            while (true)
            {
                LuaValue ai;
                while (Lt(ai = Get(++i), pivot))
                    if (i == up - 1) throw LuaErrors.Error("invalid order function for sorting");
                LuaValue aj;
                while (Lt(pivot, aj = Get(--j)))
                    if (j < i) throw LuaErrors.Error("invalid order function for sorting");
                if (j < i)
                {
                    Set(up - 1, ai);
                    Set(i, pivot);
                    return i;
                }
                Set(i, aj);
                Set(j, ai);
            }
        }
    }

    private static LuaValue[]? Sort(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaValue t = CheckTab(state, args, 0, "sort", CanRead | CanWrite | CanLen);
        long n = Len(state, t);
        if (n > 1)
        {
            if (n >= int.MaxValue) throw LuaErrors.ArgError(1, "sort", "array too big");
            LuaValue cmp = args.Length > 1 ? args[1] : default;
            if (!cmp.IsNil && !cmp.IsFunction) throw LuaErrors.TypeError(args, 1, "sort", "function");
            new Sorter { State = state, Table = t, Cmp = cmp }.AuxSort(1, n, 0);
        }
        return null;
    }
}
