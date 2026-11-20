namespace Lunette;

/// <summary>Integer/float conversions and the shift and bitwise primitives, with PUC 5.4 semantics.</summary>
internal static class LuaNumber
{
    private const double Two63 = 9223372036854775808.0;

    /// <summary>Converts a float to an integer only when it has an exact integer value in range.</summary>
    public static bool FloatToInteger(double d, out long result)
    {
        if (d >= -Two63 && d < Two63 && Math.Floor(d) == d)
        {
            result = (long)d;
            return true;
        }
        result = 0;
        return false;
    }

    /// <summary>Converts a number value to an integer exactly (integers pass through; floats must be integral).</summary>
    public static bool TryToInteger(LuaValue v, out long result)
    {
        if (v.IsInteger) { result = v.AsInteger; return true; }
        if (v.IsFloat) return FloatToInteger(v.AsFloat, out result);
        result = 0;
        return false;
    }

    /// <summary>
    /// String-to-number conversion per the manual (and PUC's l_str2int/l_str2d): optional surrounding
    /// whitespace and sign, decimal or hexadecimal integers (hex wraps modulo 2^64, decimal overflow becomes a
    /// float), decimal floats and hexadecimal floats. "inf" and "nan" are rejected.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out LuaValue result)
    {
        result = default;
        int start = 0, end = text.Length;
        while (start < end && IsSpace(text[start])) start++;
        while (end > start && IsSpace(text[end - 1])) end--;
        ReadOnlySpan<char> s = text[start..end];
        if (s.IsEmpty) return false;

        if (TryParseInteger(s, out long iv))
        {
            result = LuaValue.FromInteger(iv);
            return true;
        }
        if (TryParseFloat(s, out double d))
        {
            result = LuaValue.FromFloat(d);
            return true;
        }
        return false;
    }

    private static bool IsSpace(char c) => c is ' ' or '\f' or '\n' or '\r' or '\t' or '\v';

    private static bool TryParseInteger(ReadOnlySpan<char> s, out long value)
    {
        value = 0;
        int i = 0;
        bool neg = false;
        if (s[i] == '-') { neg = true; i++; }
        else if (s[i] == '+') i++;
        if (i >= s.Length) return false;
        ulong a = 0;
        if (s[i] == '0' && i + 1 < s.Length && s[i + 1] is 'x' or 'X')
        {
            i += 2;
            int digits = 0;
            for (; i < s.Length; i++, digits++)
            {
                int h = HexValue(s[i]);
                if (h < 0) return false;
                a = unchecked(a * 16 + (ulong)h);
            }
            if (digits == 0) return false;
        }
        else
        {
            int digits = 0;
            for (; i < s.Length; i++, digits++)
            {
                if (!char.IsAsciiDigit(s[i])) return false;
                int d = s[i] - '0';
                if (a > (ulong.MaxValue - 9) / 10 || a * 10 + (ulong)d > (ulong)long.MaxValue + (neg ? 1UL : 0UL))
                    return false; // overflow: not an integer, retry as float
                a = a * 10 + (ulong)d;
            }
            if (digits == 0) return false;
        }
        value = unchecked(neg ? (long)(0 - a) : (long)a);
        return true;
    }

    private static int HexValue(char c) =>
        c is >= '0' and <= '9' ? c - '0'
        : c is >= 'a' and <= 'f' ? c - 'a' + 10
        : c is >= 'A' and <= 'F' ? c - 'A' + 10
        : -1;

    private static bool TryParseFloat(ReadOnlySpan<char> s, out double value)
    {
        value = 0;
        int i = 0;
        bool neg = false;
        if (s[i] == '-') { neg = true; i++; }
        else if (s[i] == '+') i++;
        if (i >= s.Length) return false;
        if (s[i] == '0' && i + 1 < s.Length && s[i + 1] is 'x' or 'X')
        {
            if (!TryParseHexFloat(s[(i + 2)..], out value)) return false;
            if (neg) value = -value;
            return true;
        }
        // Validate the decimal syntax strictly; the framework parser is more permissive than Lua.
        int j = i, mantissaDigits = 0;
        while (j < s.Length && char.IsAsciiDigit(s[j])) { j++; mantissaDigits++; }
        if (j < s.Length && s[j] == '.')
        {
            j++;
            while (j < s.Length && char.IsAsciiDigit(s[j])) { j++; mantissaDigits++; }
        }
        if (mantissaDigits == 0) return false;
        if (j < s.Length && s[j] is 'e' or 'E')
        {
            j++;
            if (j < s.Length && s[j] is '+' or '-') j++;
            int expDigits = 0;
            while (j < s.Length && char.IsAsciiDigit(s[j])) { j++; expDigits++; }
            if (expDigits == 0) return false;
        }
        if (j != s.Length) return false;
        return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    // After the "0x" prefix: hexdigits [. hexdigits] [p [+-] decdigits], at least one hex digit.
    private static bool TryParseHexFloat(ReadOnlySpan<char> s, out double value)
    {
        value = 0;
        double r = 0;
        int sigdig = 0, e = 0, i = 0;
        bool hasDot = false, any = false;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '.')
            {
                if (hasDot) return false;
                hasDot = true;
                continue;
            }
            int h = HexValue(c);
            if (h < 0) break;
            any = true;
            if (sigdig != 0 || h != 0)
            {
                if (++sigdig <= 30) r = r * 16.0 + h;
                else e++; // too many digits: ignore them but keep the magnitude
            }
            if (hasDot) e--;
        }
        if (!any) return false;
        e *= 4;
        if (i < s.Length && s[i] is 'p' or 'P')
        {
            i++;
            bool eneg = false;
            if (i < s.Length && s[i] is '+' or '-') { eneg = s[i] == '-'; i++; }
            if (i >= s.Length || !char.IsAsciiDigit(s[i])) return false;
            int exp1 = 0;
            for (; i < s.Length && char.IsAsciiDigit(s[i]); i++)
                exp1 = exp1 < 100000 ? exp1 * 10 + (s[i] - '0') : exp1;
            e += eneg ? -exp1 : exp1;
        }
        if (i != s.Length) return false;
        value = Math.ScaleB(r, e);
        return true;
    }

    /// <summary>
    /// Formats a float as Lua's tostring does: "%.14g", then ".0" appended when the result looks like an
    /// integer. This is the engine's own formatter, not the framework's "G14" (ADR: number formatting).
    /// </summary>
    public static string FormatFloat(double d)
    {
        if (double.IsNaN(d)) return BitConverter.DoubleToInt64Bits(d) < 0 ? "-nan" : "nan";
        if (double.IsPositiveInfinity(d)) return "inf";
        if (double.IsNegativeInfinity(d)) return "-inf";
        string s = FormatG(d, 14);
        foreach (char c in s)
        {
            if (!(char.IsAsciiDigit(c) || c == '-')) return s;
        }
        return s + ".0";
    }

    /// <summary>C's "%.{precision}g" for a finite double (precision 1 to 99).</summary>
    public static string FormatG(double d, int precision)
    {
        if (precision == 0) precision = 1;
        bool neg = BitConverter.DoubleToInt64Bits(d) < 0;
        double a = Math.Abs(d);
        string sign = neg ? "-" : "";
        if (a == 0) return sign + "0";
        // "E" formatting rounds the shortest-correct digits exactly: d.dddE+xxx with precision-1 decimals.
        string e = a.ToString("E" + (precision - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        int ePos = e.IndexOf('E');
        int exp = int.Parse(e.AsSpan(ePos + 1), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture);
        string digits = e[..ePos].Replace(".", "");
        if (exp < -4 || exp >= precision)
        {
            string frac = digits[1..].TrimEnd('0');
            string mant = frac.Length > 0 ? digits[0] + "." + frac : digits[..1];
            string es = Math.Abs(exp).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (es.Length < 2) es = "0" + es;
            return sign + mant + "e" + (exp < 0 ? "-" : "+") + es;
        }
        string result;
        if (exp >= 0)
        {
            string intPart = digits[..(exp + 1)];
            string frac = digits[(exp + 1)..].TrimEnd('0');
            result = frac.Length > 0 ? intPart + "." + frac : intPart;
        }
        else
        {
            string frac = (new string('0', -exp - 1) + digits).TrimEnd('0');
            result = "0." + frac;
        }
        return sign + result;
    }

    /// <summary>Logical shift left by <paramref name="n"/> bits; a negative count shifts right; counts of 64 or more give 0.</summary>
    public static long ShiftLeft(long x, long n)
    {
        if (n <= -64 || n >= 64) return 0;
        if (n >= 0) return (long)((ulong)x << (int)n);
        return (long)((ulong)x >> (int)-n);
    }
}
