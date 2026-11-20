using Lunette.Vm;

namespace Lunette;

/// <summary>The table-related part of the base library: type, next, pairs, ipairs, raw access and metatables.</summary>
internal static class BaseLib
{
    private static readonly LuaValue ProtectedKey = LuaValue.FromString("__metatable");
    private static readonly LuaValue PairsKey = LuaValue.FromString("__pairs");

    public static void Register(LuaState state)
    {
        var next = new BuiltinFunction(Next);
        var ipairsAux = new BuiltinFunction(IpairsAux);
        Add(state, "type", Type);
        state.Globals["next"] = LuaValue.FromObject(next);
        Add(state, "pairs", (st, args) => Pairs(st, args, next));
        Add(state, "ipairs", (_, args) =>
        {
            if (args.Length == 0) throw ArgError(1, "ipairs", "table expected, got no value");
            return [LuaValue.FromObject(ipairsAux), args[0], LuaValue.FromInteger(0)];
        });
        Add(state, "rawget", (_, args) => [CheckTable(args, 1, "rawget").Get(Arg(args, 1))]);
        Add(state, "rawset", (_, args) =>
        {
            var t = CheckTable(args, 1, "rawset");
            Meta.RawSet(t, Arg(args, 1), Arg(args, 2), null, 0);
            return [args[0]];
        });
        Add(state, "rawequal", (_, args) => [LuaValue.FromBoolean(LuaValue.RawEquals(Arg(args, 0), Arg(args, 1)))]);
        Add(state, "rawlen", (_, args) =>
        {
            LuaValue v = Arg(args, 0);
            if (v.Ref is LuaTable t) return [LuaValue.FromInteger(t.Length())];
            if (v.Ref is string s) return [LuaValue.FromInteger(s.Length)];
            throw new LuaException("table or string expected");
        });
        Add(state, "setmetatable", SetMetatable);
        Add(state, "getmetatable", (_, args) =>
        {
            var mt = Meta.GetMetatable(Arg(args, 0));
            if (mt is null) return [default];
            LuaValue protectedValue = mt.Get(ProtectedKey);
            return [protectedValue.IsNil ? LuaValue.FromTable(mt) : protectedValue];
        });
        Add(state, "tostring", (st, args) => [LuaValue.FromString(Meta.ToStringValue(st, Arg(args, 0)))]);
    }

    private static void Add(LuaState state, string name, BuiltinBody body) =>
        state.Globals[name] = LuaValue.FromObject(new BuiltinFunction(body));

    private static LuaValue Arg(ReadOnlySpan<LuaValue> args, int i) => i < args.Length ? args[i] : default;

    private static LuaException ArgError(int n, string fn, string msg) => new($"bad argument #{n} to '{fn}' ({msg})");

    private static LuaTable CheckTable(ReadOnlySpan<LuaValue> args, int n, string fn)
    {
        if (n <= args.Length && args[n - 1].Ref is LuaTable t) return t;
        string got = n <= args.Length ? args[n - 1].TypeName : "no value";
        throw ArgError(n, fn, $"table expected, got {got}");
    }

    private static LuaValue[] Type(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length == 0) throw ArgError(1, "type", "value expected");
        return [LuaValue.FromString(args[0].TypeName)];
    }

    private static LuaValue[] Next(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        var t = CheckTable(args, 1, "next");
        if (t.Next(Arg(args, 1), out var k, out var v)) return [k, v];
        return [default];
    }

    private static LuaValue[] Pairs(LuaState state, ReadOnlySpan<LuaValue> args, BuiltinFunction next)
    {
        if (args.Length == 0) throw ArgError(1, "pairs", "table expected, got no value");
        var mt = Meta.GetMetatable(args[0]);
        LuaValue tm = mt is null ? default : mt.Get(PairsKey);
        if (tm.IsNil) return [LuaValue.FromObject(next), args[0], default];
        var res = Interpreter.CallValues(state, tm, args[..1]);
        return [res.Length > 0 ? res[0] : default, res.Length > 1 ? res[1] : default, res.Length > 2 ? res[2] : default];
    }

    private static LuaValue[] IpairsAux(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        long i = unchecked(Arg(args, 1).AsInteger + 1);
        LuaValue v = Meta.Index(state, Arg(args, 0), LuaValue.FromInteger(i), null, 0);
        return v.IsNil ? [default] : [LuaValue.FromInteger(i), v];
    }

    private static LuaValue[] SetMetatable(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        var t = CheckTable(args, 1, "setmetatable");
        LuaValue m = Arg(args, 1);
        if (!(m.IsNil || m.Ref is LuaTable)) throw ArgError(2, "setmetatable", "nil or table expected");
        if (t.Metatable is not null && !t.Metatable.Get(ProtectedKey).IsNil)
            throw new LuaException("cannot change a protected metatable");
        t.Metatable = m.Ref as LuaTable;
        return [args[0]];
    }
}
