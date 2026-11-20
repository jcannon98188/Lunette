using System.Globalization;
using Lunette.Vm;
using static Lunette.Lib.LibArgs;

namespace Lunette.Lib;

/// <summary>The Lua <c>string</c> library (PUC 5.4 semantics, byte strings, "C" locale).</summary>
internal static class StringLib
{
    public static void Register(LuaState state)
    {
        if (!state.Wants("string")) return;
        var lib = new LuaTable();
        void Add(string name, BuiltinBody body) =>
            lib.Set(LuaValue.FromString(state.Strings.FromUtf16(name)), LuaValue.FromObject(new BuiltinFunction(body)));

        Add("len", Len);
        Add("sub", Sub);
        Add("upper", static (st, a) => Case(st, a, "upper", upper: true));
        Add("lower", static (st, a) => Case(st, a, "lower", upper: false));
        Add("rep", Rep);
        Add("reverse", Reverse);
        Add("byte", Byte);
        Add("char", Char);
        Add("format", Format);
        Add("find", static (st, a) => FindAux(st, a, find: true));
        Add("match", static (st, a) => FindAux(st, a, find: false));
        Add("gmatch", GMatch);
        Add("gsub", GSub);
        Add("pack", StringPack.Pack);
        Add("unpack", StringPack.Unpack);
        Add("packsize", StringPack.PackSize);
        Add("dump", static (_, _) => throw LuaErrors.Error("unable to dump given function"));

        state.SetGlobal("string", LuaValue.FromTable(lib));
        var meta = new LuaTable();
        meta.Set(TagMethods.Names[(int)TagMethod.Index], LuaValue.FromTable(lib));
        state.StringMetatable = meta;
    }

    private static LuaValue[] One(LuaValue v) => Rets.One(v);

    private static LuaValue Str(LuaState state, ReadOnlySpan<byte> bytes) => LuaValue.FromString(state.Strings.Intern(bytes));

    // ---- positions ----

    private static long PosRelatI(long pos, long len) =>
        pos > 0 ? pos : pos == 0 ? 1 : pos < -len ? 1 : len + pos + 1;

    private static long GetEndPos(long pos, long len) =>
        pos > len ? len : pos >= 0 ? pos : pos < -len ? 0 : len + pos + 1;

    // ---- simple functions ----

    private static LuaValue[] Len(LuaState state, ReadOnlySpan<LuaValue> args) =>
        One(LuaValue.FromInteger(CheckString(state, "len", args, 0).Length));

    private static LuaValue[] Sub(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString s = CheckString(state, "sub", args, 0);
        long l = s.Length;
        long start = PosRelatI(CheckInteger("sub", args, 1), l);
        long end = GetEndPos(OptInteger("sub", args, 2, -1), l);
        if (start > end) return One(LuaValue.FromString(LuaString.Empty));
        if (start == 1 && end == l) return One(LuaValue.FromString(s));
        return One(Str(state, s.Bytes.Slice((int)start - 1, (int)(end - start + 1))));
    }

    private static LuaValue[] Case(LuaState state, ReadOnlySpan<LuaValue> args, string fn, bool upper)
    {
        LuaString s = CheckString(state, fn, args, 0);
        LuaString r = LuaString.Allocate(s.Length);
        ReadOnlySpan<byte> src = s.Bytes;
        Span<byte> dst = r.WritableBytes;
        for (int i = 0; i < src.Length; i++)
        {
            byte c = src[i];
            if (upper) dst[i] = (uint)(c - 'a') < 26 ? (byte)(c - 32) : c;
            else dst[i] = (uint)(c - 'A') < 26 ? (byte)(c + 32) : c;
        }
        return One(LuaValue.FromString(state.Strings.Intern(r)));
    }

    private static LuaValue[] Rep(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString s = CheckString(state, "rep", args, 0);
        long n = CheckInteger("rep", args, 1);
        LuaString sep = args.Length > 2 && !args[2].IsNil ? CheckString(state, "rep", args, 2) : LuaString.Empty;
        if (n <= 0) return One(LuaValue.FromString(LuaString.Empty));
        long total = (long)s.Length * n + (long)sep.Length * (n - 1);
        if (total > int.MaxValue - 64 || ((long)s.Length + sep.Length) > 0 && n > (int.MaxValue - 64) / ((long)s.Length + sep.Length) + 1)
            throw LuaErrors.Error("resulting string too large");
        state.CheckAlloc(total);
        LuaString r = LuaString.Allocate((int)total);
        Span<byte> d = r.WritableBytes;
        int pos = 0;
        for (long i = 0; i < n; i++)
        {
            s.Bytes.CopyTo(d[pos..]);
            pos += s.Length;
            if (i < n - 1 && sep.Length > 0)
            {
                sep.Bytes.CopyTo(d[pos..]);
                pos += sep.Length;
            }
        }
        return One(LuaValue.FromString(state.Strings.Intern(r)));
    }

    private static LuaValue[] Reverse(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString s = CheckString(state, "reverse", args, 0);
        LuaString r = LuaString.Allocate(s.Length);
        s.Bytes.CopyTo(r.WritableBytes);
        r.WritableBytes.Reverse();
        return One(LuaValue.FromString(state.Strings.Intern(r)));
    }

    private static LuaValue[] Byte(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString s = CheckString(state, "byte", args, 0);
        long l = s.Length;
        long pi = OptInteger("byte", args, 1, 1);
        long start = PosRelatI(pi, l);
        long end = GetEndPos(OptInteger("byte", args, 2, pi), l);
        if (start > end) return [];
        if (start == end) return One(LuaValue.FromInteger(s[(int)start - 1]));
        if (end - start >= int.MaxValue) throw LuaErrors.Error("string slice too long");
        var res = new LuaValue[end - start + 1];
        for (long i = start; i <= end; i++) res[i - start] = LuaValue.FromInteger(s[(int)i - 1]);
        return res;
    }

    private static LuaValue[] Char(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString r = LuaString.Allocate(args.Length);
        Span<byte> d = r.WritableBytes;
        for (int i = 0; i < args.Length; i++)
        {
            long c = CheckInteger("char", args, i);
            if ((ulong)c > 255) throw ArgError("char", i + 1, "value out of range");
            d[i] = (byte)c;
        }
        return One(LuaValue.FromString(state.Strings.Intern(r)));
    }

    // ---- format ----

    private static void AppendAscii(ref ByteBuilder b, string s)
    {
        for (int i = 0; i < s.Length; i++) b.Append((byte)s[i]);
    }

    private static LuaException InvalidSpecification(ReadOnlySpan<byte> f, int specStart, int specEnd)
    {
        var chars = new char[specEnd - specStart];
        for (int i = 0; i < chars.Length; i++) chars[i] = (char)f[specStart + i];
        return LuaErrors.Error($"invalid conversion specification: '%{new string(chars)}'");
    }

    private static LuaException InvalidConversion(ReadOnlySpan<byte> f, int specStart, int specEnd)
    {
        var chars = new char[specEnd - specStart];
        for (int i = 0; i < chars.Length; i++) chars[i] = (char)f[specStart + i];
        return LuaErrors.Error($"invalid conversion '%{new string(chars)}' to 'format'");
    }

    /// <summary>The conversion-spec check of lstrlib's checkformat: allowed flags, then up to two width digits, then an optional two-digit precision.</summary>
    private static bool ValidSpec(ReadOnlySpan<byte> spec, string flags, bool allowPrec)
    {
        int p = 0;
        while (p < spec.Length && flags.Contains((char)spec[p], StringComparison.Ordinal)) p++;
        if (p < spec.Length && spec[p] != '0')
        {
            int n = 0;
            while (p < spec.Length && n < 2 && char.IsAsciiDigit((char)spec[p])) { p++; n++; }
        }
        if (p < spec.Length && spec[p] == '.' && allowPrec)
        {
            p++;
            int n = 0;
            while (p < spec.Length && n < 2 && char.IsAsciiDigit((char)spec[p])) { p++; n++; }
        }
        return p == spec.Length;
    }

    private static LuaValue[] Format(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString fmt = CheckString(state, "format", args, 0);
        ReadOnlySpan<byte> f = fmt.Bytes;
        var b = new ByteBuilder(f.Length + 16);
        try
        {
            int arg = 0;
            int i = 0;
            while (i < f.Length)
            {
                byte c = f[i++];
                if (c != '%')
                {
                    b.Append(c);
                    continue;
                }
                if (i < f.Length && f[i] == '%')
                {
                    b.Append((byte)'%');
                    i++;
                    continue;
                }
                int specStart = i;
                // The reference checks that an argument exists before it parses the conversion.
                if (++arg >= args.Length) throw ArgError("format", arg + 1, "no value");
                int span = 0;
                while (specStart + span < f.Length && f[specStart + span] is (byte)'-' or (byte)'+' or (byte)' ' or (byte)'#' or (byte)'.' or (>= (byte)'0' and <= (byte)'9')) span++;
                if (span + 1 >= 22) throw LuaErrors.Error("invalid format string to 'format' (too long)");
                var spec = new FormatSpec { Prec = -1 };
                while (i < f.Length && f[i] is (byte)'-' or (byte)'+' or (byte)' ' or (byte)'#' or (byte)'0')
                {
                    switch ((char)f[i])
                    {
                        case '-': spec.Left = true; break;
                        case '+': spec.Plus = true; break;
                        case ' ': spec.Space = true; break;
                        case '#': spec.Alt = true; break;
                        default: spec.Zero = true; break;
                    }
                    i++;
                }
                int digits = 0;
                bool badWidth = false;
                while (i < f.Length && char.IsAsciiDigit((char)f[i]))
                {
                    spec.Width = spec.Width * 10 + (f[i++] - '0');
                    digits++;
                }
                if (digits > 2) badWidth = true;
                if (i < f.Length && f[i] == '.')
                {
                    i++;
                    spec.Prec = 0;
                    digits = 0;
                    while (i < f.Length && char.IsAsciiDigit((char)f[i]))
                    {
                        spec.Prec = spec.Prec * 10 + (f[i++] - '0');
                        digits++;
                    }
                    if (digits > 2) badWidth = true;
                }
                if (i >= f.Length) throw InvalidConversion(f, specStart, i);
                char conv = (char)f[i++];
                bool hasMods = specStart != i - 1;
                (string flagSet, bool allowPrec)? rules = conv switch
                {
                    'c' or 'p' => ("-", false),
                    'd' or 'i' => ("-+0 ", true),
                    'u' => ("-0", true),
                    'o' or 'x' or 'X' => ("-#0", true),
                    'a' or 'A' or 'e' or 'E' or 'f' or 'g' or 'G' => ("-+ #0", true),
                    's' => ("-", true),
                    _ => null,
                };
                if (rules is { } rl && (badWidth || !ValidSpec(f.Slice(specStart, i - 1 - specStart), rl.flagSet, rl.allowPrec)))
                    throw InvalidSpecification(f, specStart, i);
                switch (conv)
                {
                    case 'c':
                    {
                        byte ch = (byte)CheckInteger("format", args, arg);
                        int pad = Math.Max(spec.Width - 1, 0);
                        if (!spec.Left) for (int k = 0; k < pad; k++) b.Append((byte)' ');
                        b.Append(ch);
                        if (spec.Left) for (int k = 0; k < pad; k++) b.Append((byte)' ');
                        break;
                    }
                    case 'd' or 'i' when !hasMods:
                    {
                        long v = CheckInteger("format", args, arg);
                        int at = b.Length;
                        v.TryFormat(b.Reserve(20), out int written, default, CultureInfo.InvariantCulture);
                        b.Truncate(at + written);
                        break;
                    }
                    case 'd':
                    case 'i':
                    case 'u':
                    case 'o':
                    case 'x':
                    case 'X':
                        AppendAscii(ref b, CFormat.Integer(CheckInteger("format", args, arg), conv, spec));
                        break;
                    case 'a':
                    case 'A':
                    case 'e':
                    case 'E':
                    case 'f':
                    case 'g':
                    case 'G':
                        AppendAscii(ref b, CFormat.Float(CheckNumber("format", args, arg), conv, spec));
                        break;
                    case 'p':
                    {
                        object? o = args[arg].Ref;
                        string text = o is null || args[arg].IsNumber || args[arg].IsBoolean
                            ? "(null)"
                            : "0x" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o).ToString("x8", CultureInfo.InvariantCulture);
                        AppendAscii(ref b, text.PadLeft(spec.Left ? 0 : spec.Width).PadRight(spec.Left ? spec.Width : 0));
                        break;
                    }
                    case 'q':
                        if (hasMods) throw LuaErrors.Error("specifier '%q' cannot have modifiers");
                        AddQuoted(state, ref b, args[arg], arg + 1);
                        break;
                    case 's':
                    {
                        LuaString s = Meta.ToStringValue(state, args[arg]);
                        ReadOnlySpan<byte> sb = s.Bytes;
                        if (sb.Length >= BigAppend) state.CheckAlloc((long)b.Length + sb.Length);
                        if (!hasMods)
                        {
                            b.Append(sb);
                            break;
                        }
                        if (sb.IndexOf((byte)0) >= 0) throw ArgError("format", arg + 1, "string contains zeros");
                        if (spec.Prec < 0 && sb.Length >= 100)
                        {
                            b.Append(sb);
                            break;
                        }
                        if (spec.Prec >= 0 && spec.Prec < sb.Length) sb = sb[..spec.Prec];
                        int padding = Math.Max(spec.Width - sb.Length, 0);
                        if (!spec.Left) for (int k = 0; k < padding; k++) b.Append((byte)' ');
                        b.Append(sb);
                        if (spec.Left) for (int k = 0; k < padding; k++) b.Append((byte)' ');
                        break;
                    }
                    default:
                        throw InvalidConversion(f, specStart, i);
                }
            }
            state.CheckAlloc(b.Length);
            return One(LuaValue.FromString(b.ToLuaString(state.Strings)));
        }
        finally
        {
            b.Dispose();
        }
    }

    /// <summary>An appended piece at least this long gets a memory-cap pre-check (<see cref="LuaState.CheckAlloc"/>) before it is copied.</summary>
    private const int BigAppend = 4096;

    private static void AddQuoted(LuaState state, ref ByteBuilder b, LuaValue v, int argNumber)
    {
        if (v.Ref is byte[] raw)
        {
            ReadOnlySpan<byte> s = new LuaString(raw).Bytes;
            if (s.Length >= BigAppend) state.CheckAlloc((long)b.Length + s.Length * 2L);
            b.Append((byte)'"');
            for (int i = 0; i < s.Length; i++)
            {
                byte c = s[i];
                if (c is (byte)'"' or (byte)'\\' or (byte)'\n')
                {
                    b.Append((byte)'\\');
                    b.Append(c);
                }
                else if (c == 0 || c < 32 || c == 127)
                {
                    bool nextDigit = i + 1 < s.Length && char.IsAsciiDigit((char)s[i + 1]);
                    AppendAscii(ref b, "\\" + (nextDigit ? ((int)c).ToString("000", CultureInfo.InvariantCulture) : ((int)c).ToString(CultureInfo.InvariantCulture)));
                }
                else b.Append(c);
            }
            b.Append((byte)'"');
        }
        else if (v.IsInteger)
        {
            long n = v.AsInteger;
            AppendAscii(ref b, n == long.MinValue ? "0x8000000000000000" : n.ToString(CultureInfo.InvariantCulture));
        }
        else if (v.IsFloat)
        {
            double d = v.AsFloat;
            string text = d == double.PositiveInfinity ? "1e9999"
                : d == double.NegativeInfinity ? "-1e9999"
                : double.IsNaN(d) ? "(0/0)"
                : CFormat.HexFloatFull(d);
            AppendAscii(ref b, text);
        }
        else if (v.IsNil || v.IsBoolean)
        {
            AppendAscii(ref b, v.ToString());
        }
        else
        {
            throw ArgError("format", argNumber, "value has no literal form");
        }
    }

    // ---- pattern functions ----

    private static LuaValue GetCapture(LuaState state, in MatchState ms, int i, int s, int e)
    {
        if (i >= ms.Level)
        {
            if (i != 0) throw LuaErrors.Error($"invalid capture index %{i + 1}");
            return Str(state, ms.Src[s..e]);
        }
        int l = ms.CapLen[i];
        if (l == MatchState.CapUnfinished) throw LuaErrors.Error("unfinished capture");
        if (l == MatchState.CapPosition) return LuaValue.FromInteger(ms.CapStart[i] + 1);
        return Str(state, ms.Src.Slice(ms.CapStart[i], l));
    }

    private static int CaptureCount(in MatchState ms, bool wholeIfNone) => ms.Level == 0 && wholeIfNone ? 1 : ms.Level;

    private static bool NoSpecials(ReadOnlySpan<byte> p) => p.IndexOfAny("^$*+?.([%-"u8) < 0;

    private static LuaValue[] FindAux(LuaState state, ReadOnlySpan<LuaValue> args, bool find)
    {
        string fn = find ? "find" : "match";
        LuaString s = CheckString(state, fn, args, 0);
        LuaString p = CheckString(state, fn, args, 1);
        long ls = s.Length;
        long init = PosRelatI(OptInteger(fn, args, 2, 1), ls) - 1;
        if (init > ls) return Rets.One(default);
        ReadOnlySpan<byte> src = s.Bytes, pat = p.Bytes;
        if (find && ((args.Length > 3 && !args[3].IsFalsy) || NoSpecials(pat)))
        {
            int idx = src[(int)init..].IndexOf(pat);
            if (idx < 0) return Rets.One(default);
            return Rets.Two(LuaValue.FromInteger(init + idx + 1), LuaValue.FromInteger(init + idx + pat.Length));
        }
        Span<int> capStart = stackalloc int[MatchState.MaxCaptures];
        Span<int> capLen = stackalloc int[MatchState.MaxCaptures];
        var ms = new MatchState(src, pat, capStart, capLen);
        bool anchor = pat.Length > 0 && pat[0] == '^';
        int pstart = anchor ? 1 : 0;
        int s1 = (int)init;
        do
        {
            ms.Reprep();
            int e = ms.Match(s1, pstart);
            if (e != -1)
            {
                int n = CaptureCount(ms, !find);
                if (find)
                {
                    if (ms.Level == 0) return Rets.Two(LuaValue.FromInteger(s1 + 1), LuaValue.FromInteger(e));
                    var res = new LuaValue[2 + ms.Level];
                    res[0] = LuaValue.FromInteger(s1 + 1);
                    res[1] = LuaValue.FromInteger(e);
                    for (int i = 0; i < ms.Level; i++) res[2 + i] = GetCapture(state, ms, i, s1, e);
                    return res;
                }
                if (n == 1) return Rets.One(GetCapture(state, ms, 0, s1, e));
                var caps = new LuaValue[n];
                for (int i = 0; i < n; i++) caps[i] = GetCapture(state, ms, i, s1, e);
                return caps;
            }
        } while (s1++ < src.Length && !anchor);
        return Rets.One(default);
    }

    private sealed class GMatchState(LuaString s, LuaString p, int pos)
    {
        public readonly LuaString Src = s, Pat = p;
        public int Pos = pos;
        public int LastMatch = -1;
    }

    private static LuaValue[] GMatch(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString s = CheckString(state, "gmatch", args, 0);
        LuaString p = CheckString(state, "gmatch", args, 1);
        long ls = s.Length;
        long init = PosRelatI(OptInteger("gmatch", args, 2, 1), ls) - 1;
        if (init > ls) init = ls + 1;
        var gm = new GMatchState(s, p, (int)Math.Min(init, int.MaxValue));
        return One(LuaValue.FromObject(new BuiltinFunction((st, _) =>
        {
            ReadOnlySpan<byte> src = gm.Src.Bytes;
            Span<int> capStart = stackalloc int[MatchState.MaxCaptures];
            Span<int> capLen = stackalloc int[MatchState.MaxCaptures];
            var ms = new MatchState(src, gm.Pat.Bytes, capStart, capLen);
            for (int pos = gm.Pos; pos <= src.Length; pos++)
            {
                ms.Reprep();
                int e = ms.Match(pos, 0);
                if (e != -1 && e != gm.LastMatch)
                {
                    gm.Pos = gm.LastMatch = e;
                    int n = CaptureCount(ms, true);
                    if (n == 1) return Rets.One(GetCapture(st, ms, 0, pos, e));
                    var caps = new LuaValue[n];
                    for (int i = 0; i < n; i++) caps[i] = GetCapture(st, ms, i, pos, e);
                    return caps;
                }
            }
            gm.Pos = src.Length + 1;
            return Rets.One(default);
        }) { NUpvalues = 3 })); // the subject, the pattern and the match state
    }

    private static LuaValue[] GSub(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString s = CheckString(state, "gsub", args, 0);
        LuaString p = CheckString(state, "gsub", args, 1);
        LuaValue repl = Arg(args, 2);
        bool isString = repl.IsString || repl.IsNumber;
        if (!isString && repl.Ref is not (LuaTable or LuaFunction))
            throw TypeError("gsub", args, 2, "string/function/table");
        LuaString replStr = isString ? CheckString(state, "gsub", args, 2) : default;
        ReadOnlySpan<byte> src = s.Bytes, pat = p.Bytes;
        long maxS = OptInteger("gsub", args, 3, (long)src.Length + 1);
        bool anchor = pat.Length > 0 && pat[0] == '^';
        int pstart = anchor ? 1 : 0;
        Span<int> capStart = stackalloc int[MatchState.MaxCaptures];
        Span<int> capLen = stackalloc int[MatchState.MaxCaptures];
        var ms = new MatchState(src, pat, capStart, capLen);
        var b = new ByteBuilder(src.Length + 16);
        try
        {
            int pos = 0, lastMatch = -1;
            long n = 0;
            bool changed = false;
            bool literal = !anchor && pat.Length > 0 && NoSpecials(pat);
            while (n < maxS)
            {
                ms.Reprep();
                int e;
                if (literal)
                {
                    // A pattern without magic characters: jump straight to the next occurrence instead of trying every position.
                    int idx = src[pos..].IndexOf(pat);
                    if (idx < 0) break;
                    if (idx > 0) b.Append(src.Slice(pos, idx));
                    pos += idx;
                    e = pos + pat.Length;
                }
                else e = ms.Match(pos, pstart);
                if (e != -1 && e != lastMatch)
                {
                    n++;
                    changed |= AddValue(state, ref b, ms, pos, e, repl, isString ? replStr.Bytes : default, isString);
                    pos = lastMatch = e;
                    if (b.Length >= BigAppend) state.CheckAlloc(b.Length);
                }
                else if (pos < src.Length) b.Append(src[pos++]);
                else break;
                if (anchor) break;
            }
            if (pos < src.Length) b.Append(src[pos..]);
            if (changed) state.CheckAlloc(b.Length);
            return Rets.Two(!changed ? LuaValue.FromString(s) : LuaValue.FromString(b.ToLuaString(state.Strings)), LuaValue.FromInteger(n));
        }
        finally
        {
            b.Dispose();
        }
    }

    private static bool AddValue(LuaState state, ref ByteBuilder b, in MatchState ms, int s, int e, LuaValue repl, ReadOnlySpan<byte> replBytes, bool isString)
    {
        LuaValue result;
        if (isString)
        {
            Span<byte> scratch = stackalloc byte[StringConv.NumberScratch];
            for (int i = 0; i < replBytes.Length; i++)
            {
                byte c = replBytes[i];
                if (c != '%')
                {
                    b.Append(c);
                    continue;
                }
                i++;
                if (i >= replBytes.Length || (replBytes[i] != '%' && !char.IsAsciiDigit((char)replBytes[i])))
                    throw LuaErrors.Error("invalid use of '%' in replacement string");
                c = replBytes[i];
                if (c == '%') b.Append((byte)'%');
                else if (c == '0') b.Append(ms.Src[s..e]);
                else
                {
                    LuaValue cap = GetCapture(state, ms, c - '1', s, e);
                    StringConv.TryGetBytes(cap, scratch, out ReadOnlySpan<byte> bytes);
                    b.Append(bytes);
                }
            }
            return true;
        }
        if (repl.Ref is LuaTable)
        {
            result = Meta.Index(state, repl, GetCapture(state, ms, 0, s, e), null, 0);
        }
        else
        {
            int n = CaptureCount(ms, true);
            LuaValue[] res;
            if (n == 1)
            {
                ReadOnlySpan<LuaValue> one = [GetCapture(state, ms, 0, s, e)]; // inline array on the stack, no arguments allocation
                res = Interpreter.CallValues(state, repl, one);
            }
            else
            {
                var capsArr = new LuaValue[n];
                for (int i = 0; i < n; i++) capsArr[i] = GetCapture(state, ms, i, s, e);
                res = Interpreter.CallValues(state, repl, capsArr);
            }
            result = res.Length > 0 ? res[0] : default;
        }
        if (result.IsFalsy)
        {
            b.Append(ms.Src[s..e]);
            return false;
        }
        Span<byte> tmp = stackalloc byte[StringConv.NumberScratch];
        if (!StringConv.TryGetBytes(result, tmp, out ReadOnlySpan<byte> text))
            throw LuaErrors.Error($"invalid replacement value (a {result.TypeName})");
        b.Append(text);
        return true;
    }
}
