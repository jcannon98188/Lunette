using Lunette.Vm;

namespace Lunette.Lib;

/// <summary>
/// The Lua <c>math</c> library, PUC 5.4 semantics. <see cref="Members"/> maps each field name to its value
/// (functions as <see cref="BuiltinFunction"/>, plus <c>pi</c>, <c>huge</c>, <c>maxinteger</c>, <c>mininteger</c>);
/// <see cref="Register"/> installs it as the global <c>math</c>.
/// </summary>
internal sealed class MathLib
{
    private readonly ulong[] _rng = new ulong[4];
    /// <summary>The host-chosen seed; when set, <c>math.randomseed()</c> without arguments returns to it instead of reading the clock.</summary>
    private readonly (ulong, ulong)? _hostSeed;

    public Dictionary<string, LuaValue> Members { get; } = new();

    public MathLib((ulong, ulong)? hostSeed = null)
    {
        _hostSeed = hostSeed;
        Members["pi"] = LuaValue.FromFloat(Math.PI);
        Members["huge"] = LuaValue.FromFloat(double.PositiveInfinity);
        Members["maxinteger"] = LuaValue.FromInteger(long.MaxValue);
        Members["mininteger"] = LuaValue.FromInteger(long.MinValue);

        Add("abs", Abs);
        Add("ceil", static (_, a) => Round("ceil", a, Math.Ceiling));
        Add("floor", static (_, a) => Round("floor", a, Math.Floor));
        Add("fmod", Fmod);
        Add("modf", Modf);
        Add("sqrt", static (_, a) => One(Math.Sqrt(Num("sqrt", a, 0))));
        Add("exp", static (_, a) => One(Math.Exp(Num("exp", a, 0))));
        Add("sin", static (_, a) => One(Math.Sin(Num("sin", a, 0))));
        Add("cos", static (_, a) => One(Math.Cos(Num("cos", a, 0))));
        Add("tan", static (_, a) => One(Math.Tan(Num("tan", a, 0))));
        Add("asin", static (_, a) => One(Math.Asin(Num("asin", a, 0))));
        Add("acos", static (_, a) => One(Math.Acos(Num("acos", a, 0))));
        Add("atan", static (_, a) => One(Math.Atan2(Num("atan", a, 0), a.Length > 1 && !a[1].IsNil ? Num("atan", a, 1) : 1.0)));
        Add("log", Log);
        Add("deg", static (_, a) => One(Num("deg", a, 0) * (180.0 / Math.PI)));
        Add("rad", static (_, a) => One(Num("rad", a, 0) * (Math.PI / 180.0)));
        Add("tointeger", ToInteger);
        Add("ult", static (_, a) => One(LuaValue.FromBoolean((ulong)Int("ult", a, 0) < (ulong)Int("ult", a, 1))));
        Add("max", static (_, a) => MinMax("max", a, true));
        Add("min", static (_, a) => MinMax("min", a, false));
        Add("random", (_, a) => Random(a));
        Add("randomseed", (_, a) => RandomSeed(a));
        if (hostSeed is var (h1, h2)) SetSeed(h1, h2);
        else SetSeed((ulong)DateTime.UtcNow.Ticks, (ulong)Environment.TickCount64);
    }

    private void Add(string name, BuiltinBody body) => Members[name] = LuaValue.FromObject(new BuiltinFunction(body));

    private static LuaValue[] One(double d) => Rets.One(LuaValue.FromFloat(d));
    private static LuaValue[] One(LuaValue v) => Rets.One(v);

    // ---- argument checking (messages follow luaL_argerror / luaL_typeerror) ----

    private static LuaException ArgError(string fn, int index, string msg) =>
        LuaErrors.ArgError(index + 1, fn, msg);

    private static LuaException TypeError(string fn, LuaValue[] args, int index, string expected) =>
        ArgError(fn, index, $"{expected} expected, got {(index < args.Length ? args[index].TypeName : "no value")}");

    private static LuaValue Number(string fn, ReadOnlySpan<LuaValue> args, int index)
    {
        if (index < args.Length && StringConv.TryToNumber(args[index], out LuaValue n)) return n;
        throw ArgError(fn, index, $"number expected, got {(index < args.Length ? args[index].TypeName : "no value")}");
    }

    private static double Num(string fn, ReadOnlySpan<LuaValue> args, int index)
    {
        LuaValue v = Number(fn, args, index);
        return v.IsInteger ? v.AsInteger : v.AsFloat;
    }

    private static long Int(string fn, ReadOnlySpan<LuaValue> args, int index)
    {
        LuaValue v = Number(fn, args, index);
        if (LuaNumber.TryToInteger(v, out long n)) return n;
        throw ArgError(fn, index, "number has no integer representation");
    }

    // ---- functions ----

    private static LuaValue[] Abs(LuaState _, ReadOnlySpan<LuaValue> a)
    {
        LuaValue v = Number("abs", a, 0);
        if (v.IsInteger)
        {
            long n = v.AsInteger;
            return One(LuaValue.FromInteger(n < 0 ? unchecked(0 - n) : n));
        }
        return One(Math.Abs(v.AsFloat));
    }

    private static LuaValue[] Round(string fn, ReadOnlySpan<LuaValue> a, Func<double, double> op)
    {
        LuaValue v = Number(fn, a, 0);
        if (v.IsInteger) return One(v);
        double r = op(v.AsFloat);
        return One(LuaNumber.FloatToInteger(r, out long n) ? LuaValue.FromInteger(n) : LuaValue.FromFloat(r));
    }

    private static LuaValue[] Fmod(LuaState _, ReadOnlySpan<LuaValue> a)
    {
        LuaValue x = Number("fmod", a, 0), y = Number("fmod", a, 1);
        if (x.IsInteger && y.IsInteger)
        {
            long d = y.AsInteger;
            if ((ulong)d + 1UL <= 1UL)
            {
                if (d == 0) throw ArgError("fmod", 1, "zero");
                return One(LuaValue.FromInteger(0)); // d == -1: avoid overflow of minint % -1
            }
            return One(LuaValue.FromInteger(x.AsInteger % d));
        }
        return One(LuaNumber.FMod(Num("fmod", a, 0), Num("fmod", a, 1)));
    }

    private static LuaValue[] Modf(LuaState _, ReadOnlySpan<LuaValue> a)
    {
        LuaValue v = Number("modf", a, 0);
        if (v.IsInteger) return Rets.Two(v, LuaValue.FromFloat(0.0));
        double n = v.AsFloat;
        double ip = n < 0 ? Math.Ceiling(n) : Math.Floor(n);
        double frac = double.IsInfinity(n) ? 0.0 : n - ip;
        return Rets.Two(LuaValue.FromFloat(ip), LuaValue.FromFloat(frac));
    }

    private static LuaValue[] Log(LuaState _, ReadOnlySpan<LuaValue> a)
    {
        double x = Num("log", a, 0);
        if (a.Length < 2 || a[1].IsNil) return One(Math.Log(x));
        double b = Num("log", a, 1);
        if (b == 2.0) return One(Math.Log2(x));
        if (b == 10.0) return One(Math.Log10(x));
        return One(Math.Log(x) / Math.Log(b));
    }

    private static LuaValue[] ToInteger(LuaState _, ReadOnlySpan<LuaValue> a)
    {
        if (a.Length == 0) throw ArgError("tointeger", 0, "value expected");
        return One(StringConv.TryToNumber(a[0], out LuaValue v) && LuaNumber.TryToInteger(v, out long n)
            ? LuaValue.FromInteger(n) : LuaValue.Nil);
    }

    private static LuaValue[] MathType(LuaState state, ReadOnlySpan<LuaValue> a)
    {
        if (a.Length == 0) throw ArgError("type", 0, "value expected");
        if (a[0].IsInteger) return One(LuaValue.FromString(state.Strings.FromUtf16("integer")));
        if (a[0].IsFloat) return One(LuaValue.FromString(state.Strings.FromUtf16("float")));
        return One(LuaValue.Nil);
    }

    /// <summary>Installs a fresh <c>math</c> table (with its own random state) as the global <c>math</c>.</summary>
    public static void Register(LuaState state, (ulong, ulong)? hostSeed = null)
    {
        if (!state.Wants("math")) return;
        var lib = new MathLib(hostSeed);
        var table = new LuaTable();
        foreach (var (name, value) in lib.Members)
            table.Set(LuaValue.FromString(state.Strings.FromUtf16(name)), value);
        table.Set(LuaValue.FromString(state.Strings.FromUtf16("type")),
            LuaValue.FromObject(new BuiltinFunction(MathType)));
        state.SetGlobal("math", LuaValue.FromTable(table));
    }

    private static LuaValue[] MinMax(string fn, ReadOnlySpan<LuaValue> a, bool max)
    {
        if (a.Length == 0) throw ArgError(fn, 0, "value expected");
        LuaValue best = Number(fn, a, 0);
        for (int i = 1; i < a.Length; i++)
        {
            LuaValue v = Number(fn, a, i);
            if (max ? Compare.NumberLessThan(best, v) : Compare.NumberLessThan(v, best)) best = v;
        }
        return One(best);
    }

    // ---- random: xoshiro256** exactly as PUC 5.4 ----

    private static ulong Rotl(ulong x, int n) => (x << n) | (x >> (64 - n));

    private ulong NextRand()
    {
        ulong s0 = _rng[0], s1 = _rng[1], s2 = _rng[2] ^ s0, s3 = _rng[3] ^ s1;
        ulong res = Rotl(s1 * 5, 7) * 9;
        s1 <<= 17;
        _rng[0] = s0 ^ s3;
        _rng[1] = _rng[1] ^ s2;
        _rng[2] = s2 ^ s1;
        _rng[3] = Rotl(s3, 45);
        return res;
    }

    private void SetSeed(ulong n1, ulong n2)
    {
        _rng[0] = n1;
        _rng[1] = 0xff;
        _rng[2] = n2;
        _rng[3] = 0;
        for (int i = 0; i < 16; i++) NextRand();
    }

    // Projects a random 64-bit value into [0, n] without bias, as PUC's project().
    private ulong Project(ulong ran, ulong n)
    {
        if ((n & (n + 1)) == 0) return ran & n;
        ulong lim = n;
        lim |= lim >> 1;
        lim |= lim >> 2;
        lim |= lim >> 4;
        lim |= lim >> 8;
        lim |= lim >> 16;
        lim |= lim >> 32;
        while ((ran &= lim) > n) ran = NextRand();
        return ran;
    }

    private LuaValue[] Random(ReadOnlySpan<LuaValue> a)
    {
        ulong rv = NextRand();
        long low, up;
        switch (a.Length)
        {
            case 0:
                return One((rv >> 11) * (0.5 / (1UL << 52)));
            case 1:
                low = 1;
                up = Int("random", a, 0);
                if (up == 0) return One(LuaValue.FromInteger(unchecked((long)rv)));
                break;
            case 2:
                low = Int("random", a, 0);
                up = Int("random", a, 1);
                break;
            default:
                throw LuaErrors.Error("wrong number of arguments");
        }
        if (low > up) throw ArgError("random", 0, "interval is empty");
        return One(LuaValue.FromInteger(unchecked((long)(Project(rv, (ulong)up - (ulong)low) + (ulong)low))));
    }

    private LuaValue[] RandomSeed(ReadOnlySpan<LuaValue> a)
    {
        long n1, n2;
        if (a.Length == 0)
        {
            if (_hostSeed is var (h1, h2)) { n1 = (long)h1; n2 = (long)h2; }
            else
            {
                n1 = DateTime.UtcNow.Ticks;
                n2 = Environment.TickCount64;
            }
        }
        else
        {
            n1 = Int("randomseed", a, 0);
            n2 = a.Length > 1 && !a[1].IsNil ? Int("randomseed", a, 1) : 0;
        }
        SetSeed((ulong)n1, (ulong)n2);
        return Rets.Two(LuaValue.FromInteger(n1), LuaValue.FromInteger(n2));
    }
}
