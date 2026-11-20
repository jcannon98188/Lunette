using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>Order comparisons (<c>&lt;</c>, <c>&lt;=</c>) with exact integer/float semantics.</summary>
internal static class Compare
{
    private const double Two63 = 9223372036854775808.0;

    public static bool LessThan(LuaValue a, LuaValue b, Proto p, int pc)
    {
        if (a.IsInteger)
        {
            if (b.IsInteger) return a.AsInteger < b.AsInteger;
            if (b.IsFloat) return IntLtFloat(a.AsInteger, b.AsFloat);
        }
        else if (a.IsFloat)
        {
            if (b.IsFloat) return a.AsFloat < b.AsFloat;
            if (b.IsInteger) return FloatLtInt(a.AsFloat, b.AsInteger);
        }
        throw Error(a, b, p, pc);
    }

    public static bool LessEqual(LuaValue a, LuaValue b, Proto p, int pc)
    {
        if (a.IsInteger)
        {
            if (b.IsInteger) return a.AsInteger <= b.AsInteger;
            if (b.IsFloat) return IntLeFloat(a.AsInteger, b.AsFloat);
        }
        else if (a.IsFloat)
        {
            if (b.IsFloat) return a.AsFloat <= b.AsFloat;
            if (b.IsInteger) return FloatLeInt(a.AsFloat, b.AsInteger);
        }
        throw Error(a, b, p, pc);
    }

    // i < f  <=>  i < ceil(f)
    private static bool IntLtFloat(long i, double f)
    {
        if (double.IsNaN(f)) return false;
        if (f >= Two63) return true;
        if (f > -Two63) return i < (long)Math.Ceiling(f);
        return false;
    }

    // i <= f  <=>  i <= floor(f)
    private static bool IntLeFloat(long i, double f)
    {
        if (double.IsNaN(f)) return false;
        if (f >= Two63) return true;
        if (f >= -Two63) return i <= (long)Math.Floor(f);
        return false;
    }

    // f < i  <=>  floor(f) < i
    private static bool FloatLtInt(double f, long i)
    {
        if (double.IsNaN(f)) return false;
        if (f >= Two63) return false;
        if (f >= -Two63) return (long)Math.Floor(f) < i;
        return true;
    }

    // f <= i  <=>  ceil(f) <= i
    private static bool FloatLeInt(double f, long i)
    {
        if (double.IsNaN(f)) return false;
        if (f >= Two63) return false;
        if (f > -Two63) return (long)Math.Ceiling(f) <= i;
        return true;
    }

    internal static LuaException Error(LuaValue a, LuaValue b, Proto? p, int pc)
    {
        string t1 = a.TypeName, t2 = b.TypeName;
        string msg = t1 == t2
            ? $"attempt to compare two {t1} values"
            : $"attempt to compare {t1} with {t2}";
        return new LuaException(p is null ? msg : $"{Interpreter.Where(p, pc)} {msg}");
    }
}
