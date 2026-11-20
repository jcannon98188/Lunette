using static Lunette.Lib.LibArgs;
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
        state.Define("next", LuaValue.FromObject(next));
        state.Define("pairs", LuaValue.FromObject(new BuiltinFunction((st, args) => Pairs(st, args, next), BuiltinKind.Pairs)));
        Add(state, "ipairs", (_, args) =>
        {
            if (args.Length == 0) throw ArgError("ipairs", 1, "value expected");
            return Rets.Three(LuaValue.FromObject(ipairsAux), args[0], LuaValue.FromInteger(0));
        });
        Add(state, "rawget", (_, args) => Rets.One(CheckTable("rawget", args, 0).Get(Arg(args, 1))));
        Add(state, "rawset", (_, args) =>
        {
            var t = CheckTable("rawset", args, 0);
            Meta.RawSet(t, Arg(args, 1), Arg(args, 2), null, 0);
            return Rets.One(args[0]);
        });
        Add(state, "rawequal", (_, args) => Rets.One(LuaValue.FromBoolean(LuaValue.RawEquals(Arg(args, 0), Arg(args, 1)))));
        Add(state, "rawlen", (_, args) =>
        {
            LuaValue v = Arg(args, 0);
            if (v.Ref is LuaTable t) return Rets.One(LuaValue.FromInteger(t.Length()));
            if (v.Ref is byte[] s) return Rets.One(LuaValue.FromInteger(new LuaString(s).Length));
            throw ArgError("rawlen", 1, $"table or string expected, got {(args.Length > 0 ? v.TypeName : "no value")}");
        });
        Add(state, "setmetatable", SetMetatable);
        Add(state, "getmetatable", (st, args) =>
        {
            var mt = Meta.GetMetatable(st, Arg(args, 0));
            if (mt is null) return Rets.One(default);
            LuaValue protectedValue = mt.Get(ProtectedKey);
            return Rets.One(protectedValue.IsNil ? LuaValue.FromTable(mt) : protectedValue);
        });
        Add(state, "tostring", (st, args) =>
        {
            if (args.Length == 0) throw ArgError("tostring", 1, "value expected");
            return Rets.One(LuaValue.FromString(Meta.ToStringValue(st, args[0])));
        });
        Add(state, "tonumber", ToNumber);
        Add(state, "select", Select);
    }

    private static LuaValue[] Select(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length > 0 && args[0].Ref is byte[] raw && new LuaString(raw).Length == 1 && new LuaString(raw)[0] == (byte)'#')
            return Rets.One(LuaValue.FromInteger(args.Length - 1));
        long n = CheckInteger("select", args, 0);
        int count = args.Length - 1;
        if (n < 0) n = count + n;
        else if (n > count) n = count + 1;
        else n--;
        if (n < 0) throw ArgError("select", 1, "index out of range");
        if (n < 0 || n >= count) return [];
        var rest = args[(int)(n + 1)..];
        return rest.Length switch
        {
            1 => Rets.One(rest[0]),
            2 => Rets.Two(rest[0], rest[1]),
            3 => Rets.Three(rest[0], rest[1], rest[2]),
            _ => rest.ToArray(),
        };
    }

    private static void Add(LuaState state, string name, BuiltinBody body) =>
        state.Define(name, LuaValue.FromObject(new BuiltinFunction(body)));

    private static LuaValue[] ToNumber(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length < 2 || args[1].IsNil)
        {
            if (args.Length == 0) throw ArgError("tonumber", 1, "value expected");
            return Rets.One(StringConv.TryToNumber(args[0], out LuaValue n) ? n : default);
        }
        long b = CheckInteger("tonumber", args, 1);
        if (args[0].Ref is not byte[] raw) throw TypeError("tonumber", args, 0, "string");
        if (b < 2 || b > 36) throw ArgError("tonumber", 2, "base out of range");
        ReadOnlySpan<byte> s = new LuaString(raw).Bytes;
        int i = 0;
        while (i < s.Length && (s[i] == (byte)' ' || (uint)(s[i] - 9) < 5)) i++;
        bool neg = false;
        if (i < s.Length && s[i] == (byte)'-') { neg = true; i++; }
        else if (i < s.Length && s[i] == (byte)'+') i++;
        if (i >= s.Length || !char.IsAsciiLetterOrDigit((char)s[i])) return Rets.One(default);
        ulong acc = 0;
        while (i < s.Length && char.IsAsciiLetterOrDigit((char)s[i]))
        {
            int d = char.IsAsciiDigit((char)s[i]) ? s[i] - '0' : (s[i] | 0x20) - 'a' + 10;
            if (d >= b) return Rets.One(default);
            acc = unchecked(acc * (ulong)b + (ulong)d);
            i++;
        }
        while (i < s.Length && (s[i] == (byte)' ' || (uint)(s[i] - 9) < 5)) i++;
        if (i != s.Length) return Rets.One(default);
        return Rets.One(LuaValue.FromInteger(unchecked((long)(neg ? 0UL - acc : acc))));
    }

    private static LuaValue[] Type(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length == 0) throw ArgError("type", 1, "value expected");
        return Rets.One(LuaValue.FromString(state.Strings.FromUtf16(args[0].TypeName)));
    }

    private static LuaValue[] Next(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        var t = CheckTable("next", args, 0);
        if (t.Next(Arg(args, 1), out var k, out var v)) return Rets.Two(k, v);
        return Rets.One(default);
    }

    private static LuaValue[] Pairs(LuaState state, ReadOnlySpan<LuaValue> args, BuiltinFunction next)
    {
        if (args.Length == 0) throw ArgError("pairs", 1, "value expected");
        var mt = Meta.GetMetatable(state, args[0]);
        LuaValue tm = mt is null ? default : mt.Get(PairsKey);
        if (tm.IsNil) return Rets.Three(LuaValue.FromObject(next), args[0], default);
        var res = Interpreter.CallValues(state, tm, args[..1]);
        return Rets.Three(res.Length > 0 ? res[0] : default, res.Length > 1 ? res[1] : default, res.Length > 2 ? res[2] : default);
    }

    private static LuaValue[] IpairsAux(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        long i = unchecked(Arg(args, 1).AsInteger + 1);
        LuaValue v = Meta.Index(state, Arg(args, 0), LuaValue.FromInteger(i), null, 0);
        return v.IsNil ? Rets.One(default) : Rets.Two(LuaValue.FromInteger(i), v);
    }

    private static LuaValue[] SetMetatable(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        var t = CheckTable("setmetatable", args, 0);
        LuaValue m = Arg(args, 1);
        if (!(m.IsNil || m.Ref is LuaTable)) throw ArgError("setmetatable", 2, "nil or table expected");
        if (t.Metatable is not null && !t.Metatable.Get(ProtectedKey).IsNil)
            throw LuaErrors.Error("cannot change a protected metatable");
        t.Metatable = m.Ref as LuaTable;
        state.Gc.OnSetMetatable(t, t.Metatable);
        return Rets.One(args[0]);
    }
}
