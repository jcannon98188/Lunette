using System.Globalization;

namespace Lunette;

/// <summary>Number/string conversions on byte strings, and the concatenation primitive.</summary>
internal static class StringConv
{
    /// <summary>Scratch size large enough for any formatted number.</summary>
    public const int NumberScratch = 64;

    /// <summary>Formats a number value as ASCII into <paramref name="dest"/> and returns the count.</summary>
    public static int FormatNumber(in LuaValue v, Span<byte> dest)
    {
        if (v.IsInteger)
        {
            v.AsInteger.TryFormat(dest, out int n, default, CultureInfo.InvariantCulture);
            return n;
        }
        string s = LuaNumber.FormatFloat(v.AsFloat);
        for (int i = 0; i < s.Length; i++) dest[i] = (byte)s[i];
        return s.Length;
    }

    public static LuaString NumberToString(LuaState state, in LuaValue v)
    {
        Span<byte> tmp = stackalloc byte[NumberScratch];
        int n = FormatNumber(v, tmp);
        return state.Strings.Intern(tmp[..n]);
    }

    /// <summary>Views a string or number value as bytes (numbers are formatted into <paramref name="scratch"/>).</summary>
    public static bool TryGetBytes(in LuaValue v, Span<byte> scratch, out ReadOnlySpan<byte> bytes)
    {
        if (v.Ref is byte[] s)
        {
            bytes = new LuaString(s).Bytes;
            return true;
        }
        if (v.IsNumber)
        {
            bytes = scratch[..FormatNumber(v, scratch)];
            return true;
        }
        bytes = default;
        return false;
    }

    /// <summary>Lua string-to-number conversion on bytes (whitespace, signs, hex, floats); embedded non-ASCII bytes fail.</summary>
    public static bool TryParseNumber(ReadOnlySpan<byte> text, out LuaValue result)
    {
        result = default;
        Span<char> chars = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == 0) return false;
            chars[i] = (char)text[i];
        }
        return LuaNumber.TryParse(chars, out result);
    }

    /// <summary>Converts a number or numeric string to a number value (the string-to-number coercion).</summary>
    public static bool TryToNumber(in LuaValue v, out LuaValue number)
    {
        if (v.IsNumber) { number = v; return true; }
        if (v.Ref is byte[] s) return TryParseNumber(new LuaString(s).Bytes, out number);
        number = default;
        return false;
    }

    public static LuaValue Concat(LuaState state, in LuaValue a, in LuaValue b)
    {
        Span<byte> sa = stackalloc byte[NumberScratch];
        Span<byte> sb = stackalloc byte[NumberScratch];
        TryGetBytes(a, sa, out ReadOnlySpan<byte> x);
        TryGetBytes(b, sb, out ReadOnlySpan<byte> y);
        long total = (long)x.Length + y.Length;
        if (total > int.MaxValue - 64) throw new LuaException("string length overflow");
        if (total <= LuaString.MaxShortLength)
        {
            Span<byte> tmp = stackalloc byte[LuaString.MaxShortLength];
            x.CopyTo(tmp);
            y.CopyTo(tmp[x.Length..]);
            return LuaValue.FromString(state.Strings.Intern(tmp[..(int)total]));
        }
        LuaString s = LuaString.Allocate((int)total);
        Span<byte> d = s.WritableBytes;
        x.CopyTo(d);
        y.CopyTo(d[x.Length..]);
        return LuaValue.FromString(s);
    }
}
