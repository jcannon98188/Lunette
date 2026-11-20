using System.Globalization;

namespace Lunette.Lib;

/// <summary>One parsed printf conversion specification.</summary>
internal struct FormatSpec
{
    public bool Left, Plus, Space, Alt, Zero;
    public int Width;
    /// <summary>-1 when absent.</summary>
    public int Prec;
}

/// <summary>C printf formatting of numbers, in the "C" locale (the engine's own, not the framework's culture-sensitive formatters).</summary>
internal static class CFormat
{
    private static string Digits(ulong v, int radix, bool upper)
    {
        if (v == 0) return "0";
        Span<char> buf = stackalloc char[64];
        int n = buf.Length;
        string alphabet = upper ? "0123456789ABCDEF" : "0123456789abcdef";
        while (v != 0)
        {
            buf[--n] = alphabet[(int)(v % (ulong)radix)];
            v /= (ulong)radix;
        }
        return new string(buf[n..]);
    }

    /// <summary>Applies sign, prefix, zero padding and width: <paramref name="zeroPadOk"/> controls the '0' flag.</summary>
    private static string Assemble(string sign, string prefix, string body, in FormatSpec f, bool zeroPadOk)
    {
        int len = sign.Length + prefix.Length + body.Length;
        if (len >= f.Width) return sign + prefix + body;
        int pad = f.Width - len;
        if (f.Left) return sign + prefix + body + new string(' ', pad);
        if (f.Zero && zeroPadOk) return sign + prefix + new string('0', pad) + body;
        return new string(' ', pad) + sign + prefix + body;
    }

    public static string Integer(long v, char conv, in FormatSpec f)
    {
        string sign = "", prefix = "", digits;
        if (conv is 'd' or 'i')
        {
            ulong mag = v < 0 ? unchecked((ulong)-v) : (ulong)v;
            digits = Digits(mag, 10, false);
            sign = v < 0 ? "-" : f.Plus ? "+" : f.Space ? " " : "";
        }
        else
        {
            ulong u = unchecked((ulong)v);
            int radix = conv == 'o' ? 8 : conv is 'x' or 'X' ? 16 : 10;
            digits = Digits(u, radix, conv == 'X');
            if (f.Alt && u != 0 && conv == 'x') prefix = "0x";
            if (f.Alt && u != 0 && conv == 'X') prefix = "0X";
        }
        if (f.Prec == 0 && v == 0) digits = "";
        if (f.Prec > digits.Length) digits = new string('0', f.Prec - digits.Length) + digits;
        if (f.Alt && conv == 'o' && (digits.Length == 0 || digits[0] != '0')) digits = "0" + digits;
        return Assemble(sign, prefix, digits, f, f.Prec < 0);
    }

    public static string Float(double d, char conv, in FormatSpec f)
    {
        bool upper = char.IsAsciiLetterUpper(conv);
        bool neg = BitConverter.DoubleToInt64Bits(d) < 0;
        string sign = neg ? "-" : f.Plus ? "+" : f.Space ? " " : "";
        double a = Math.Abs(d);
        if (!double.IsFinite(d))
        {
            string text = double.IsNaN(d) ? "nan" : "inf";
            return Assemble(sign, "", upper ? text.ToUpperInvariant() : text, f, false);
        }
        string prefix = "", body;
        switch (char.ToLowerInvariant(conv))
        {
            case 'f':
                body = Fixed(a, f.Prec < 0 ? 6 : f.Prec, f.Alt);
                break;
            case 'e':
                body = Exponent(a, f.Prec < 0 ? 6 : f.Prec, f.Alt, upper);
                break;
            case 'g':
                body = General(a, f.Prec, f.Alt, upper);
                break;
            default:
                prefix = upper ? "0X" : "0x";
                body = HexFloat(a, f.Prec, f.Alt, upper);
                break;
        }
        return Assemble(sign, prefix, body, f, true);
    }

    private static string Fixed(double a, int prec, bool alt)
    {
        string s = a.ToString("F" + prec.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return alt && prec == 0 ? s + "." : s;
    }

    private static string Exponent(double a, int prec, bool alt, bool upper)
    {
        string s = a.ToString("E" + prec.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        int e = s.IndexOf('E');
        int exp = int.Parse(s.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string mant = s[..e];
        if (alt && prec == 0) mant += ".";
        return mant + (upper ? "E" : "e") + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
    }

    private static string General(double a, int prec, bool alt, bool upper)
    {
        int p = prec < 0 ? 6 : prec == 0 ? 1 : prec;
        int x = 0;
        if (a != 0)
        {
            string s = a.ToString("E" + (p - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            x = int.Parse(s.AsSpan(s.IndexOf('E') + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        string body;
        if (p > x && x >= -4)
        {
            body = Fixed(a, p - 1 - x, alt);
            if (!alt) body = TrimZeros(body);
        }
        else
        {
            body = Exponent(a, p - 1, alt, upper);
            if (!alt)
            {
                int e = body.IndexOf(upper ? 'E' : 'e');
                body = TrimZeros(body[..e]) + body[e..];
            }
        }
        return body;
    }

    private static string TrimZeros(string s)
    {
        if (s.IndexOf('.') < 0) return s;
        s = s.TrimEnd('0');
        return s.EndsWith('.') ? s[..^1] : s;
    }

    private static string HexFloat(double a, int prec, bool alt, bool upper)
    {
        long bits = BitConverter.DoubleToInt64Bits(a);
        int expField = (int)((bits >> 52) & 0x7FF);
        ulong m = (ulong)bits & 0xFFFFFFFFFFFFFUL;
        int lead = expField == 0 ? 0 : 1;
        int exp = expField == 0 ? (m == 0 ? 0 : -1022) : expField - 1023;
        string frac;
        if (prec < 0)
        {
            frac = m.ToString("x13", CultureInfo.InvariantCulture).TrimEnd('0');
        }
        else if (prec >= 13)
        {
            frac = m.ToString("x13", CultureInfo.InvariantCulture) + new string('0', prec - 13);
        }
        else
        {
            int shift = 4 * (13 - prec);
            ulong rem = m & ((1UL << shift) - 1);
            ulong kept = m >> shift;
            ulong half = 1UL << (shift - 1);
            if (rem > half || (rem == half && (kept & 1) == 1)) kept++;
            if (prec == 0)
            {
                lead += (int)kept;
                frac = "";
            }
            else
            {
                if (kept >> (4 * prec) != 0) { lead++; kept = 0; }
                frac = kept.ToString("x" + prec.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
        }
        string s = lead.ToString(CultureInfo.InvariantCulture);
        if (frac.Length > 0 || alt) s += "." + frac;
        s += "p" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
        return upper ? s.ToUpperInvariant() : s;
    }

    /// <summary>The exact "%a" text used by %q: lowercase, no flags.</summary>
    public static string HexFloatFull(double d)
    {
        var f = new FormatSpec { Prec = -1 };
        return Float(d, 'a', f);
    }
}
