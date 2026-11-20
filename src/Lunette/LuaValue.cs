using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lunette;

/// <summary>
/// The single 16-byte Lua value (ADR 0001): the type is derived from <see cref="Ref"/>,
/// number payloads live in <see cref="Bits"/>. A null Ref is nil.
/// </summary>
public readonly struct LuaValue
{
    private sealed class Tag(string name)
    {
        public override string ToString() => name;
    }

    private static readonly object IntegerTag = new Tag("integer");
    private static readonly object FloatTag = new Tag("float");
    private static readonly object TrueTag = new Tag("true");
    private static readonly object FalseTag = new Tag("false");

    internal readonly object? Ref;
    internal readonly long Bits;

    private LuaValue(object? r, long bits)
    {
        Ref = r;
        Bits = bits;
    }

    public static LuaValue Nil => default;
    public static LuaValue FromInteger(long v) => new(IntegerTag, v);
    public static LuaValue FromFloat(double v) => new(FloatTag, BitConverter.DoubleToInt64Bits(v));
    public static LuaValue FromBoolean(bool v) => new(v ? TrueTag : FalseTag, 0);

    public bool IsNil => Ref is null;
    public bool IsInteger => ReferenceEquals(Ref, IntegerTag);
    public bool IsFloat => ReferenceEquals(Ref, FloatTag);
    public bool IsNumber => IsInteger || IsFloat;
    public bool IsBoolean => ReferenceEquals(Ref, TrueTag) || ReferenceEquals(Ref, FalseTag);
    public bool IsFalsy => Ref is null || ReferenceEquals(Ref, FalseTag);

    public long AsInteger => Bits;
    public double AsFloat => BitConverter.Int64BitsToDouble(Bits);

    internal static LuaValue FromObject(object o) => new(o, 0);

    /// <summary>Primitive equality: no metamethods; an integer equals a float of the same mathematical value.</summary>
    internal static bool RawEquals(LuaValue a, LuaValue b)
    {
        if (a.IsInteger)
        {
            if (b.IsInteger) return a.Bits == b.Bits;
            return b.IsFloat && FloatEqualsInteger(b.AsFloat, a.Bits);
        }
        if (a.IsFloat)
        {
            if (b.IsFloat) return a.AsFloat == b.AsFloat;
            return b.IsInteger && FloatEqualsInteger(a.AsFloat, b.Bits);
        }
        return ReferenceEquals(a.Ref, b.Ref);
    }

    private static bool FloatEqualsInteger(double f, long i) =>
        f >= -9223372036854775808.0 && f < 9223372036854775808.0 && Math.Floor(f) == f && (long)f == i;

    internal string TypeName =>
        Ref switch
        {
            null => "nil",
            _ when IsNumber => "number",
            _ when IsBoolean => "boolean",
            LuaFunction => "function",
            _ => "userdata",
        };

    internal bool TryToNumber(out double d)
    {
        if (IsFloat) { d = AsFloat; return true; }
        if (IsInteger) { d = Bits; return true; }
        d = 0;
        return false;
    }

    public override string ToString()
    {
        if (Ref is null) return "nil";
        if (IsInteger) return Bits.ToString(CultureInfo.InvariantCulture);
        if (IsFloat) return FormatFloat(AsFloat);
        if (IsBoolean) return ReferenceEquals(Ref, TrueTag) ? "true" : "false";
        return Ref.ToString() ?? "userdata";
    }

    private static string FormatFloat(double d)
    {
        if (double.IsNaN(d)) return BitConverter.DoubleToInt64Bits(d) < 0 ? "-nan" : "nan";
        if (double.IsPositiveInfinity(d)) return "inf";
        if (double.IsNegativeInfinity(d)) return "-inf";
        // Lua uses "%.14g"; .NET "G14" matches apart from exponent casing.
        string s = d.ToString("G14", CultureInfo.InvariantCulture).Replace('E', 'e');
        bool looksLikeInt = true;
        foreach (char c in s)
        {
            if (!(char.IsDigit(c) || c == '-')) { looksLikeInt = false; break; }
        }
        return looksLikeInt ? s + ".0" : s;
    }
}
