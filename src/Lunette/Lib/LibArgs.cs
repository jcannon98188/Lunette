namespace Lunette.Lib;

/// <summary>
/// Argument checking for library functions, with luaL_argerror / luaL_typeerror message shapes.
/// Index convention: <c>i</c> is a zero-based index into <c>args</c>; <c>n</c> in <see cref="ArgError"/> is the one-based
/// argument number printed in the message.
/// </summary>
internal static class LibArgs
{
    public static LuaValue Arg(ReadOnlySpan<LuaValue> args, int i) => i < args.Length ? args[i] : default;

    /// <summary>"bad argument #n to 'fn' (msg)" with a one-based argument number.</summary>
    public static LuaException ArgError(string fn, int n, string msg)
    {
        LuaException e = LuaErrors.Error($"bad argument #{n} to '{fn}' ({msg})");
        e.ArgN = n;
        e.ArgMsg = msg;
        return e;
    }

    /// <summary>"bad argument #n to 'fn' (expected expected, got ...)" for the zero-based argument index <paramref name="i"/>.</summary>

    public static LuaException TypeError(string fn, ReadOnlySpan<LuaValue> args, int i, string expected) =>
        ArgError(fn, i + 1, $"{expected} expected, got {(i < args.Length ? Vm.Meta.ArgTypeName(args[i]) : "no value")}");

    /// <summary>A string argument; numbers are converted (and interned) as Lua's string coercion does.</summary>
    public static LuaString CheckString(LuaState state, string fn, ReadOnlySpan<LuaValue> args, int i)
    {
        if (i < args.Length)
        {
            LuaValue v = args[i];
            if (v.Ref is byte[] s) return new LuaString(s);
            if (v.IsNumber) return StringConv.NumberToString(state, v);
        }
        throw TypeError(fn, args, i, "string");
    }

    public static long CheckInteger(string fn, ReadOnlySpan<LuaValue> args, int i)
    {
        if (i < args.Length)
        {
            LuaValue v = args[i];
            if (v.IsInteger) return v.AsInteger;
            if (v.IsFloat)
            {
                if (LuaNumber.FloatToInteger(v.AsFloat, out long l)) return l;
                throw ArgError(fn, i + 1, "number has no integer representation");
            }
            if (v.Ref is byte[] s && StringConv.TryParseNumber(new LuaString(s).Bytes, out LuaValue n))
            {
                if (LuaNumber.TryToInteger(n, out long l)) return l;
                throw ArgError(fn, i + 1, "number has no integer representation");
            }
        }
        throw TypeError(fn, args, i, "number");
    }

    public static long OptInteger(string fn, ReadOnlySpan<LuaValue> args, int i, long dflt) =>
        i >= args.Length || args[i].IsNil ? dflt : CheckInteger(fn, args, i);

    public static double CheckNumber(string fn, ReadOnlySpan<LuaValue> args, int i)
    {
        if (i < args.Length && StringConv.TryToNumber(args[i], out LuaValue n))
            return n.IsInteger ? n.AsInteger : n.AsFloat;
        throw TypeError(fn, args, i, "number");
    }

    public static LuaValue CheckNumberValue(string fn, ReadOnlySpan<LuaValue> args, int i)
    {
        if (i < args.Length && StringConv.TryToNumber(args[i], out LuaValue n)) return n;
        throw TypeError(fn, args, i, "number");
    }

    public static LuaTable CheckTable(string fn, ReadOnlySpan<LuaValue> args, int i)
    {
        if (i < args.Length && args[i].Ref is LuaTable t) return t;
        throw TypeError(fn, args, i, "table");
    }

    public static void CheckAny(string fn, ReadOnlySpan<LuaValue> args, int i)
    {
        if (i >= args.Length) throw ArgError(fn, i + 1, "value expected");
    }
}
