using static Lunette.Lib.LibArgs;

namespace Lunette.Lib;

/// <summary>The Lua <c>utf8</c> library (PUC 5.4 lutf8lib semantics), working on raw bytes.</summary>
internal static class Utf8Lib
{
    private const uint MaxUnicode = 0x10FFFF;
    private const uint MaxUtf = 0x7FFFFFFF;

    public static void Register(LuaState state)
    {
        var lib = new LuaTable();
        void Add(string name, BuiltinBody body) =>
            lib.Set(LuaValue.FromString(state.Strings.FromUtf16(name)), LuaValue.FromObject(new BuiltinFunction(body)));

        Add("char", Char);
        Add("codepoint", CodePoint);
        Add("len", Len);
        Add("offset", Offset);
        var strictIter = new BuiltinFunction((st, a) => IterAux(st, a, strict: true));
        var laxIter = new BuiltinFunction((st, a) => IterAux(st, a, strict: false));
        Add("codes", (st, a) =>
        {
            LuaString s = CheckString(st, "codes", a, 0);
            bool lax = a.Length > 1 && !a[1].IsFalsy;
            if (s.Length > 0 && IsCont(s[0])) throw LuaErrors.Error("bad argument #1 to 'codes' (invalid UTF-8 code)");
            return Rets.Three(LuaValue.FromObject(lax ? laxIter : strictIter), LuaValue.FromString(s), LuaValue.FromInteger(0));
        });
        ReadOnlySpan<byte> pattern = [(byte)'[', 0, (byte)'-', 0x7F, 0xC2, (byte)'-', 0xFD, (byte)']', (byte)'[', 0x80, (byte)'-', 0xBF, (byte)']', (byte)'*'];
        lib.Set(LuaValue.FromString(state.Strings.FromUtf16("charpattern")), LuaValue.FromString(state.Strings.Intern(pattern)));
        state.SetGlobal("utf8", LuaValue.FromTable(lib));
    }

    private static bool IsCont(int c) => (c & 0xC0) == 0x80;

    private static bool Cont(ReadOnlySpan<byte> s, long p) => p < s.Length && IsCont(s[(int)p]);

    private static long PosRelat(long pos, long len) => pos >= 0 ? pos : -pos > len ? 0 : len + pos + 1;

    /// <summary>Decodes one sequence at <paramref name="at"/>; returns the index after it, or -1 if invalid.</summary>
    private static int Decode(ReadOnlySpan<byte> s, int at, out uint val, bool strict)
    {
        ReadOnlySpan<uint> limits = [~0u, 0x80, 0x800, 0x10000u, 0x200000u, 0x4000000u];
        uint c = s[at];
        uint res = 0;
        val = 0;
        if (c < 0x80) res = c;
        else
        {
            int count = 0;
            for (; (c & 0x40) != 0; c <<= 1)
            {
                int idx = at + ++count;
                uint cc = idx < s.Length ? s[idx] : 0u;
                if ((cc & 0xC0) != 0x80) return -1;
                res = (res << 6) | (cc & 0x3F);
            }
            res |= (c & 0x7F) << (count * 5);
            if (count > 5 || res > MaxUtf || res < limits[count]) return -1;
            at += count;
        }
        if (strict && (res > MaxUnicode || (0xD800u <= res && res <= 0xDFFFu))) return -1;
        val = res;
        return at + 1;
    }

    private static LuaValue[] Len(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString str = CheckString(state, "len", args, 0);
        ReadOnlySpan<byte> s = str.Bytes;
        long len = s.Length;
        long posi = PosRelat(OptInteger("len", args, 1, 1), len);
        long posj = PosRelat(OptInteger("len", args, 2, -1), len);
        bool lax = args.Length > 3 && !args[3].IsFalsy;
        if (!(1 <= posi && --posi <= len)) throw ArgError("len", 2, "initial position out of bounds");
        if (!(--posj < len)) throw ArgError("len", 3, "final position out of bounds");
        long n = 0;
        while (posi <= posj)
        {
            int next = Decode(s, (int)posi, out _, !lax);
            if (next < 0) return Rets.Two(default, LuaValue.FromInteger(posi + 1));
            posi = next;
            n++;
        }
        return Rets.One(LuaValue.FromInteger(n));
    }

    private static LuaValue[] CodePoint(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString str = CheckString(state, "codepoint", args, 0);
        ReadOnlySpan<byte> s = str.Bytes;
        long len = s.Length;
        long posi = PosRelat(OptInteger("codepoint", args, 1, 1), len);
        long pose = PosRelat(OptInteger("codepoint", args, 2, posi), len);
        bool lax = args.Length > 3 && !args[3].IsFalsy;
        if (posi < 1) throw ArgError("codepoint", 2, "out of bounds");
        if (pose > len) throw ArgError("codepoint", 3, "out of bounds");
        if (posi > pose) return [];
        if (pose - posi >= int.MaxValue) throw LuaErrors.Error("string slice too long");
        var res = new List<LuaValue>();
        int at = (int)posi - 1;
        while (at < pose)
        {
            int next = Decode(s, at, out uint code, !lax);
            if (next < 0) throw LuaErrors.Error("invalid UTF-8 code");
            res.Add(LuaValue.FromInteger(code));
            at = next;
        }
        return res.ToArray();
    }

    private static void PutUtf8(ref ByteBuilder b, uint x)
    {
        if (x < 0x80)
        {
            b.Append((byte)x);
            return;
        }
        Span<byte> tmp = stackalloc byte[8];
        int n = 1;
        uint mfb = 0x3f;
        do
        {
            tmp[8 - n++] = (byte)(0x80 | (x & 0x3f));
            x >>= 6;
            mfb >>= 1;
        } while (x > mfb);
        tmp[8 - n] = (byte)(((~mfb << 1) & 0xFF) | x);
        b.Append(tmp[(8 - n)..]);
    }

    private static LuaValue[] Char(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        var b = new ByteBuilder(args.Length * 2);
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                long code = CheckInteger("char", args, i);
                if ((ulong)code > MaxUtf) throw ArgError("char", i + 1, "value out of range");
                PutUtf8(ref b, (uint)code);
            }
            return Rets.One(LuaValue.FromString(b.ToLuaString(state.Strings)));
        }
        finally
        {
            b.Dispose();
        }
    }

    private static LuaValue[] Offset(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString str = CheckString(state, "offset", args, 0);
        ReadOnlySpan<byte> s = str.Bytes;
        long len = s.Length;
        long n = CheckInteger("offset", args, 1);
        long posi = n >= 0 ? 1 : len + 1;
        posi = PosRelat(OptInteger("offset", args, 2, posi), len);
        if (!(1 <= posi && --posi <= len)) throw ArgError("offset", 3, "position out of bounds");
        if (n == 0)
        {
            while (posi > 0 && Cont(s, posi)) posi--;
        }
        else
        {
            if (Cont(s, posi)) throw LuaErrors.Error("initial position is a continuation byte");
            if (n < 0)
            {
                while (n < 0 && posi > 0)
                {
                    do { posi--; } while (posi > 0 && Cont(s, posi));
                    n++;
                }
            }
            else
            {
                n--;
                while (n > 0 && posi < len)
                {
                    do { posi++; } while (Cont(s, posi));
                    n--;
                }
            }
        }
        return n == 0 ? [LuaValue.FromInteger(posi + 1)] : [default];
    }

    private static LuaValue[] IterAux(LuaState state, ReadOnlySpan<LuaValue> args, bool strict)
    {
        LuaString str = CheckString(state, "codes", args, 0);
        ReadOnlySpan<byte> s = str.Bytes;
        long len = s.Length;
        ulong n = args.Length > 1 && LuaNumber.TryToInteger(args[1], out long iv) ? (ulong)iv : 0;
        if (n < (ulong)len)
        {
            while (n < (ulong)len && IsCont(s[(int)n])) n++;
        }
        if (n >= (ulong)len) return Rets.One(default);
        int next = Decode(s, (int)n, out uint code, strict);
        if (next < 0 || (next < s.Length && IsCont(s[next]))) throw LuaErrors.Error("invalid UTF-8 code");
        return Rets.Two(LuaValue.FromInteger((long)n + 1), LuaValue.FromInteger(code));
    }
}
