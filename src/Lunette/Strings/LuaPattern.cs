using System.Diagnostics.CodeAnalysis;

namespace Lunette;

/// <summary>Raises pattern/format errors on behalf of byte-only string paths (which may not build .NET strings).</summary>
internal static class StringErrors
{
    [DoesNotReturn, LuaColdThrow]
    public static void Fail(string message) => throw new LuaException(message);

    [DoesNotReturn, LuaColdThrow]
    public static void InvalidCapture(int index) => throw new LuaException($"invalid capture index %{index + 1}");

    [DoesNotReturn, LuaColdThrow]
    public static void InvalidCaptureIndex(int index, string where) => throw new LuaException($"invalid capture index %{index}{where}");
}

/// <summary>
/// The Lua pattern matcher (PUC 5.4 lstrlib semantics, "C" locale) over byte spans, with zero heap
/// allocation: capture bookkeeping lives in caller-supplied spans.
/// </summary>
[LuaStringPath]
internal ref struct MatchState
{
    public const int MaxCaptures = 32;
    public const int CapUnfinished = -1;
    public const int CapPosition = -2;
    private const int MaxCCalls = 200;
    private const byte Esc = (byte)'%';

    public readonly ReadOnlySpan<byte> Src;
    public readonly ReadOnlySpan<byte> Pat;
    public readonly Span<int> CapStart;
    public readonly Span<int> CapLen;
    public int Level;
    private int _matchDepth;

    public MatchState(ReadOnlySpan<byte> src, ReadOnlySpan<byte> pat, Span<int> capStart, Span<int> capLen)
    {
        Src = src;
        Pat = pat;
        CapStart = capStart;
        CapLen = capLen;
        Level = 0;
        _matchDepth = MaxCCalls;
    }

    public void Reprep()
    {
        Level = 0;
        _matchDepth = MaxCCalls;
    }

    private readonly int P(int i) => i < Pat.Length ? Pat[i] : 0;

    private readonly int S(int i) => i < Src.Length ? Src[i] : 0;

    // ---- character classes ----

    private static bool IsAlpha(int c) => (uint)((c | 0x20) - 'a') < 26;
    private static bool IsDigit(int c) => (uint)(c - '0') < 10;
    private static bool IsLower(int c) => (uint)(c - 'a') < 26;
    private static bool IsUpper(int c) => (uint)(c - 'A') < 26;
    private static bool IsAlnum(int c) => IsAlpha(c) || IsDigit(c);
    private static bool IsSpace(int c) => c == ' ' || (uint)(c - 9) < 5;
    private static bool IsCntrl(int c) => c < 32 || c == 127;
    private static bool IsGraph(int c) => (uint)(c - 33) < 94;
    private static bool IsPunct(int c) => IsGraph(c) && !IsAlnum(c);
    private static bool IsXDigit(int c) => IsDigit(c) || (uint)((c | 0x20) - 'a') < 6;

    public static bool ClassMatches(int c, int cl)
    {
        bool res;
        switch (cl | 0x20)
        {
            case 'a': res = IsAlpha(c); break;
            case 'c': res = IsCntrl(c); break;
            case 'd': res = IsDigit(c); break;
            case 'g': res = IsGraph(c); break;
            case 'l': res = IsLower(c); break;
            case 'p': res = IsPunct(c); break;
            case 's': res = IsSpace(c); break;
            case 'u': res = IsUpper(c); break;
            case 'w': res = IsAlnum(c); break;
            case 'x': res = IsXDigit(c); break;
            default: return cl == c;
        }
        return IsUpper(cl) ? !res : res;
    }

    private readonly int ClassEnd(int p)
    {
        if (p >= Pat.Length) StringErrors.Fail("malformed pattern (ends with '%')");
        int c = Pat[p++];
        if (c == Esc)
        {
            if (p >= Pat.Length) StringErrors.Fail("malformed pattern (ends with '%')");
            return p + 1;
        }
        if (c == '[')
        {
            if (P(p) == '^') p++;
            do
            {
                if (p >= Pat.Length) StringErrors.Fail("malformed pattern (missing ']')");
                int cc = Pat[p++];
                if (cc == Esc)
                {
                    if (p >= Pat.Length) StringErrors.Fail("malformed pattern (missing ']')");
                    p++;
                }
            } while (p >= Pat.Length || Pat[p] != ']');
            return p + 1;
        }
        return p;
    }

    private readonly bool MatchBracketClass(int c, int p, int ec)
    {
        bool sig = true;
        if (Pat[p + 1] == '^')
        {
            sig = false;
            p++;
        }
        while (++p < ec)
        {
            if (Pat[p] == Esc)
            {
                p++;
                if (ClassMatches(c, Pat[p])) return sig;
            }
            else if (p + 2 < ec && Pat[p + 1] == '-')
            {
                p += 2;
                if (Pat[p - 2] <= c && c <= Pat[p]) return sig;
            }
            else if (Pat[p] == c) return sig;
        }
        return !sig;
    }

    private readonly bool SingleMatch(int s, int p, int ep)
    {
        if (s >= Src.Length) return false;
        int c = Src[s];
        switch (Pat[p])
        {
            case (byte)'.': return true;
            case Esc: return ClassMatches(c, Pat[p + 1]);
            case (byte)'[': return MatchBracketClass(c, p, ep - 1);
            default: return Pat[p] == c;
        }
    }

    // ---- matching ----

    private readonly int MatchBalance(int s, int p)
    {
        if (p + 1 >= Pat.Length) StringErrors.Fail("malformed pattern (missing arguments to '%b')");
        if (s >= Src.Length || Src[s] != Pat[p]) return -1;
        int b = Pat[p], e = Pat[p + 1], cont = 1;
        while (++s < Src.Length)
        {
            if (Src[s] == e)
            {
                if (--cont == 0) return s + 1;
            }
            else if (Src[s] == b) cont++;
        }
        return -1;
    }

    private int MaxExpand(int s, int p, int ep)
    {
        int i = 0;
        while (SingleMatch(s + i, p, ep)) i++;
        while (i >= 0)
        {
            int res = Match(s + i, ep + 1);
            if (res != -1) return res;
            i--;
        }
        return -1;
    }

    private int MinExpand(int s, int p, int ep)
    {
        for (; ; )
        {
            int res = Match(s, ep + 1);
            if (res != -1) return res;
            if (SingleMatch(s, p, ep)) s++;
            else return -1;
        }
    }

    private int StartCapture(int s, int p, int what)
    {
        if (Level >= MaxCaptures) StringErrors.Fail("too many captures");
        CapStart[Level] = s;
        CapLen[Level] = what;
        Level++;
        int res = Match(s, p);
        if (res == -1) Level--;
        return res;
    }

    private int EndCapture(int s, int p)
    {
        int l = CaptureToClose();
        CapLen[l] = s - CapStart[l];
        int res = Match(s, p);
        if (res == -1) CapLen[l] = CapUnfinished;
        return res;
    }

    private readonly int CaptureToClose()
    {
        int level = Level;
        for (level--; level >= 0; level--)
        {
            if (CapLen[level] == CapUnfinished) return level;
        }
        StringErrors.Fail("invalid pattern capture");
        return 0;
    }

    private readonly int CheckCapture(int l)
    {
        l -= '1';
        if (l < 0 || l >= Level || CapLen[l] == CapUnfinished) StringErrors.InvalidCaptureIndex(l + 1, "");
        return l;
    }

    private readonly int MatchCapture(int s, int l)
    {
        l = CheckCapture(l);
        int len = CapLen[l];
        if (Src.Length - s >= len && Src.Slice(CapStart[l], len).SequenceEqual(Src.Slice(s, len))) return s + len;
        return -1;
    }

    /// <summary>Matches the pattern from <paramref name="p"/> at subject index <paramref name="s"/>; returns the end index or -1.</summary>
    public int Match(int s, int p)
    {
        if (_matchDepth-- == 0) StringErrors.Fail("pattern too complex");
        while (true)
        {
            if (p >= Pat.Length) break;
            bool dflt = false;
            switch (Pat[p])
            {
                case (byte)'(':
                    s = P(p + 1) == ')' ? StartCapture(s, p + 2, CapPosition) : StartCapture(s, p + 1, CapUnfinished);
                    goto done;
                case (byte)')':
                    s = EndCapture(s, p + 1);
                    goto done;
                case (byte)'$':
                    if (p + 1 != Pat.Length) { dflt = true; break; }
                    s = s == Src.Length ? s : -1;
                    goto done;
                case Esc:
                    switch (P(p + 1))
                    {
                        case 'b':
                            s = MatchBalance(s, p + 2);
                            if (s != -1)
                            {
                                p += 4;
                                continue;
                            }
                            goto done;
                        case 'f':
                        {
                            p += 2;
                            if (P(p) != '[') StringErrors.Fail("missing '[' after '%f' in pattern");
                            int ep = ClassEnd(p);
                            int previous = s == 0 ? 0 : Src[s - 1];
                            if (!MatchBracketClass(previous, p, ep - 1) && MatchBracketClass(S(s), p, ep - 1))
                            {
                                p = ep;
                                continue;
                            }
                            s = -1;
                            goto done;
                        }
                        case >= '0' and <= '9':
                            s = MatchCapture(s, P(p + 1));
                            if (s != -1)
                            {
                                p += 2;
                                continue;
                            }
                            goto done;
                        default:
                            dflt = true;
                            break;
                    }
                    break;
                default:
                    dflt = true;
                    break;
            }
            if (!dflt) break;
            {
                int ep = ClassEnd(p);
                int epc = P(ep);
                if (!SingleMatch(s, p, ep))
                {
                    if (epc == '*' || epc == '?' || epc == '-')
                    {
                        p = ep + 1;
                        continue;
                    }
                    s = -1;
                    goto done;
                }
                switch (epc)
                {
                    case '?':
                    {
                        int res = Match(s + 1, ep + 1);
                        if (res != -1)
                        {
                            s = res;
                            goto done;
                        }
                        p = ep + 1;
                        continue;
                    }
                    case '+':
                        s = MaxExpand(s + 1, p, ep);
                        goto done;
                    case '*':
                        s = MaxExpand(s, p, ep);
                        goto done;
                    case '-':
                        s = MinExpand(s, p, ep);
                        goto done;
                    default:
                        s++;
                        p = ep;
                        continue;
                }
            }
        }
    done:
        _matchDepth++;
        return s;
    }
}
