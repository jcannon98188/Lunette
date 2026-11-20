using System.Buffers.Binary;
using static Lunette.Lib.LibArgs;

namespace Lunette.Lib;

/// <summary>string.pack, string.unpack and string.packsize (PUC 5.4 lstrlib, little-endian native, 64-bit long).</summary>
internal static class StringPack
{
    private const int MaxIntSize = 16;
    private const int MaxAlign = 8;
    private const int SzInt = 8;

    private enum Kind { Int, Uint, Float, Number, Double, Char, String, Zstr, Padding, PadAlign, Nop }

    private struct Header
    {
        public bool IsLittle;
        public int MaxAlign;
    }

    private static int GetNum(ReadOnlySpan<byte> fmt, ref int pos, int dflt)
    {
        if (pos >= fmt.Length || !char.IsAsciiDigit((char)fmt[pos])) return dflt;
        int a = 0;
        do
        {
            a = a * 10 + (fmt[pos++] - '0');
        } while (pos < fmt.Length && char.IsAsciiDigit((char)fmt[pos]) && a <= (int.MaxValue - 9) / 10);
        return a;
    }

    private static int GetNumLimit(ReadOnlySpan<byte> fmt, ref int pos, int dflt)
    {
        int sz = GetNum(fmt, ref pos, dflt);
        if (sz > MaxIntSize || sz <= 0) throw LuaErrors.Error($"integral size ({sz}) out of limits [1,{MaxIntSize}]");
        return sz;
    }

    private static Kind GetOption(ref Header h, ReadOnlySpan<byte> fmt, ref int pos, out int size)
    {
        char opt = (char)fmt[pos++];
        size = 0;
        switch (opt)
        {
            case 'b': size = 1; return Kind.Int;
            case 'B': size = 1; return Kind.Uint;
            case 'h': size = 2; return Kind.Int;
            case 'H': size = 2; return Kind.Uint;
            case 'l': size = 8; return Kind.Int;
            case 'L': size = 8; return Kind.Uint;
            case 'j': size = 8; return Kind.Int;
            case 'J': size = 8; return Kind.Uint;
            case 'T': size = 8; return Kind.Uint;
            case 'f': size = 4; return Kind.Float;
            case 'n': size = 8; return Kind.Number;
            case 'd': size = 8; return Kind.Double;
            case 'i': size = GetNumLimit(fmt, ref pos, 4); return Kind.Int;
            case 'I': size = GetNumLimit(fmt, ref pos, 4); return Kind.Uint;
            case 's': size = GetNumLimit(fmt, ref pos, 8); return Kind.String;
            case 'c':
                size = GetNum(fmt, ref pos, -1);
                if (size == -1) throw LuaErrors.Error("missing size for format option 'c'");
                return Kind.Char;
            case 'z': return Kind.Zstr;
            case 'x': size = 1; return Kind.Padding;
            case 'X': return Kind.PadAlign;
            case ' ': break;
            case '<': h.IsLittle = true; break;
            case '>': h.IsLittle = false; break;
            case '=': h.IsLittle = true; break;
            case '!': h.MaxAlign = GetNumLimit(fmt, ref pos, MaxAlign); break;
            default: throw LuaErrors.Error($"invalid format option '{opt}'");
        }
        return Kind.Nop;
    }

    private static Kind GetDetails(string fn, ref Header h, long totalSize, ReadOnlySpan<byte> fmt, ref int pos, out int size, out int toAlign)
    {
        Kind opt = GetOption(ref h, fmt, ref pos, out size);
        int align = size;
        if (opt == Kind.PadAlign)
        {
            if (pos >= fmt.Length) throw ArgError(fn, 1, "invalid next option for option 'X'");
            Kind next = GetOption(ref h, fmt, ref pos, out align);
            if (next == Kind.Char || align == 0) throw ArgError(fn, 1, "invalid next option for option 'X'");
        }
        if (align <= 1 || opt == Kind.Char) toAlign = 0;
        else
        {
            if (align > h.MaxAlign) align = h.MaxAlign;
            if ((align & (align - 1)) != 0) throw ArgError(fn, 1, "format asks for alignment not power of 2");
            toAlign = (align - (int)(totalSize & (align - 1))) & (align - 1);
        }
        return opt;
    }

    private static void PackInt(ref ByteBuilder b, ulong n, bool little, int size, bool neg)
    {
        Span<byte> buf = b.Reserve(size);
        buf[little ? 0 : size - 1] = (byte)(n & 0xFF);
        for (int i = 1; i < size; i++)
        {
            n >>= 8;
            buf[little ? i : size - 1 - i] = (byte)(n & 0xFF);
        }
        if (neg && size > SzInt)
        {
            for (int i = SzInt; i < size; i++) buf[little ? i : size - 1 - i] = 0xFF;
        }
    }

    private static long UnpackInt(ReadOnlySpan<byte> str, bool little, int size, bool signed)
    {
        ulong res = 0;
        int limit = size <= SzInt ? size : SzInt;
        for (int i = limit - 1; i >= 0; i--)
        {
            res <<= 8;
            res |= str[little ? i : size - 1 - i];
        }
        if (size < SzInt)
        {
            if (signed)
            {
                ulong mask = 1UL << (size * 8 - 1);
                res = (res ^ mask) - mask;
            }
        }
        else if (size > SzInt)
        {
            int mask = !signed || (long)res >= 0 ? 0 : 0xFF;
            for (int i = limit; i < size; i++)
            {
                if (str[little ? i : size - 1 - i] != mask)
                    throw LuaErrors.Error($"{size}-byte integer does not fit into Lua Integer");
            }
        }
        return (long)res;
    }

    private static void PutFloat(ref ByteBuilder b, double v, bool single, bool little)
    {
        Span<byte> d = b.Reserve(single ? 4 : 8);
        if (single)
        {
            if (little) BinaryPrimitives.WriteSingleLittleEndian(d, (float)v);
            else BinaryPrimitives.WriteSingleBigEndian(d, (float)v);
        }
        else if (little) BinaryPrimitives.WriteDoubleLittleEndian(d, v);
        else BinaryPrimitives.WriteDoubleBigEndian(d, v);
    }

    public static LuaValue[] Pack(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        const string fn = "pack";
        LuaString fmtStr = CheckString(state, fn, args, 0);
        ReadOnlySpan<byte> fmt = fmtStr.Bytes;
        var h = new Header { IsLittle = true, MaxAlign = 1 };
        var b = new ByteBuilder(32);
        try
        {
            int arg = 0, pos = 0;
            long total = 0;
            while (pos < fmt.Length)
            {
                Kind opt = GetDetails(fn, ref h, total, fmt, ref pos, out int size, out int toAlign);
                total += toAlign + size;
                for (int k = 0; k < toAlign; k++) b.Append(0);
                arg++;
                switch (opt)
                {
                    case Kind.Int:
                    {
                        long n = CheckInteger(fn, args, arg);
                        if (size < SzInt)
                        {
                            long lim = 1L << (size * 8 - 1);
                            if (!(-lim <= n && n < lim)) throw ArgError(fn, arg + 1, "integer overflow");
                        }
                        PackInt(ref b, (ulong)n, h.IsLittle, size, n < 0);
                        break;
                    }
                    case Kind.Uint:
                    {
                        long n = CheckInteger(fn, args, arg);
                        if (size < SzInt && (ulong)n >= 1UL << (size * 8)) throw ArgError(fn, arg + 1, "unsigned overflow");
                        PackInt(ref b, (ulong)n, h.IsLittle, size, false);
                        break;
                    }
                    case Kind.Float:
                        PutFloat(ref b, CheckNumber(fn, args, arg), true, h.IsLittle);
                        break;
                    case Kind.Number:
                    case Kind.Double:
                        PutFloat(ref b, CheckNumber(fn, args, arg), false, h.IsLittle);
                        break;
                    case Kind.Char:
                    {
                        LuaString s = CheckString(state, fn, args, arg);
                        if (s.Length > size) throw ArgError(fn, arg + 1, "string longer than given size");
                        b.Append(s.Bytes);
                        for (int k = s.Length; k < size; k++) b.Append(0);
                        break;
                    }
                    case Kind.String:
                    {
                        LuaString s = CheckString(state, fn, args, arg);
                        if (!(size >= 8 || (ulong)s.Length < 1UL << (size * 8)))
                            throw ArgError(fn, arg + 1, "string length does not fit in given size");
                        PackInt(ref b, (ulong)s.Length, h.IsLittle, size, false);
                        b.Append(s.Bytes);
                        total += s.Length;
                        break;
                    }
                    case Kind.Zstr:
                    {
                        LuaString s = CheckString(state, fn, args, arg);
                        if (s.Bytes.IndexOf((byte)0) >= 0) throw ArgError(fn, arg + 1, "string contains zeros");
                        b.Append(s.Bytes);
                        b.Append(0);
                        total += s.Length + 1;
                        break;
                    }
                    case Kind.Padding:
                        b.Append(0);
                        arg--;
                        break;
                    default:
                        arg--;
                        break;
                }
            }
            return [LuaValue.FromString(b.ToLuaString(state.Strings))];
        }
        finally
        {
            b.Dispose();
        }
    }

    public static LuaValue[] PackSize(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        const string fn = "packsize";
        LuaString fmtStr = CheckString(state, fn, args, 0);
        ReadOnlySpan<byte> fmt = fmtStr.Bytes;
        var h = new Header { IsLittle = true, MaxAlign = 1 };
        long total = 0;
        int pos = 0;
        while (pos < fmt.Length)
        {
            Kind opt = GetDetails(fn, ref h, total, fmt, ref pos, out int size, out int toAlign);
            if (opt is Kind.String or Kind.Zstr) throw ArgError(fn, 1, "variable-length format");
            size += toAlign;
            if (total > int.MaxValue - size) throw ArgError(fn, 1, "format result too large");
            total += size;
        }
        return [LuaValue.FromInteger(total)];
    }

    public static LuaValue[] Unpack(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        const string fn = "unpack";
        LuaString fmtStr = CheckString(state, fn, args, 0);
        LuaString dataStr = CheckString(state, fn, args, 1);
        ReadOnlySpan<byte> fmt = fmtStr.Bytes, data = dataStr.Bytes;
        long ld = data.Length;
        long init = OptInteger(fn, args, 2, 1);
        long posL = (init > 0 ? init : init == 0 ? 1 : init < -ld ? 1 : ld + init + 1) - 1;
        if (posL > ld) throw ArgError(fn, 3, "initial position out of string");
        var h = new Header { IsLittle = true, MaxAlign = 1 };
        var results = new List<LuaValue>();
        int fpos = 0;
        long pos = posL;
        while (fpos < fmt.Length)
        {
            Kind opt = GetDetails(fn, ref h, pos, fmt, ref fpos, out int size, out int toAlign);
            if ((long)toAlign + size > ld - pos) throw ArgError(fn, 2, "data string too short");
            pos += toAlign;
            int p = (int)pos;
            switch (opt)
            {
                case Kind.Int:
                case Kind.Uint:
                    results.Add(LuaValue.FromInteger(UnpackInt(data.Slice(p, size), h.IsLittle, size, opt == Kind.Int)));
                    break;
                case Kind.Float:
                    results.Add(LuaValue.FromFloat(h.IsLittle
                        ? BinaryPrimitives.ReadSingleLittleEndian(data.Slice(p, 4))
                        : BinaryPrimitives.ReadSingleBigEndian(data.Slice(p, 4))));
                    break;
                case Kind.Number:
                case Kind.Double:
                    results.Add(LuaValue.FromFloat(h.IsLittle
                        ? BinaryPrimitives.ReadDoubleLittleEndian(data.Slice(p, 8))
                        : BinaryPrimitives.ReadDoubleBigEndian(data.Slice(p, 8))));
                    break;
                case Kind.Char:
                    results.Add(LuaValue.FromString(state.Strings.Intern(data.Slice(p, size))));
                    break;
                case Kind.String:
                {
                    ulong len = (ulong)UnpackInt(data.Slice(p, size), h.IsLittle, size, false);
                    if (len > (ulong)(ld - pos - size)) throw ArgError(fn, 2, "data string too short");
                    results.Add(LuaValue.FromString(state.Strings.Intern(data.Slice(p + size, (int)len))));
                    pos += (long)len;
                    break;
                }
                case Kind.Zstr:
                {
                    int len = data[p..].IndexOf((byte)0);
                    if (len < 0) throw ArgError(fn, 2, "unfinished string for format 'z'");
                    results.Add(LuaValue.FromString(state.Strings.Intern(data.Slice(p, len))));
                    pos += len + 1;
                    break;
                }
            }
            pos += size;
        }
        results.Add(LuaValue.FromInteger(pos + 1));
        return results.ToArray();
    }
}
