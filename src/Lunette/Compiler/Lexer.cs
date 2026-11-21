using System.Globalization;

namespace Lunette.Compiler;

internal enum Tok
{
    Eof, Name, Integer, Float,
    Local, Nil, True, False,
    If, Then, Else, Elseif, End, While, Do, Repeat, Until, For, Function, Return, Break, And, Or, Not, In,
    Eq, Ne, Lt, Le, Gt, Ge, Ellipsis,
    Plus, Minus, Star, Slash, DSlash, Percent, Caret, Concat,
    Amp, Pipe, Tilde, Shl, Shr,
    LParen, RParen, Comma, Assign, Semi,
    String, Hash, LBrace, RBrace, LBracket, RBracket, Dot, Colon, Goto, DColon,
}

internal struct Token
{
    public Tok Kind;
    public string Text;
    /// <summary>The bytes of a string literal or the interned spelling of a name.</summary>
    public LuaString Str;
    public long Int;
    public double Float;
    public int Line;
    /// <summary>Source range of a string or number token; <see cref="Text"/> stays empty for those and is sliced on demand by <see cref="Lexer.TextOf"/>.</summary>
    public int Start, End;
}

internal sealed class Lexer(byte[] bytes, string chunkName, StringCache cache)
{
    /// <summary>Source viewed one char per byte so the scanning code reads like text.</summary>
    private readonly Src src = new(bytes);
    private byte[] _buf = new byte[64];
    private int _bufLen;
    private int _pos;
    private int _line = 1;

    private readonly struct Src(byte[] b)
    {
        public int Length => b.Length;
        public char this[int i] => (char)b[i];
        public string Slice(int start, int end)
        {
            var chars = new char[end - start];
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)b[start + i];
            return new string(chars);
        }
    }

    private int _tokStart;

    // Per-compilation name table: source bytes -> canonical text, interned LuaString and keyword kind, so a repeated name costs one hash and no allocation.
    // Starts empty and small so tiny chunks (load("return 1 + 2")) pay nothing for it.
    private struct NameEntry { public uint Hash; public int Next; public string Text; public LuaString Str; public Tok Kind; }
    private NameEntry[] _names = [];
    private int[] _nameBuckets = [];
    private int _nameCount;
    private static readonly string[] OneChar = BuildOneChar();
    private static string[] BuildOneChar() { var a = new string[128]; for (int i = 0; i < 128; i++) a[i] = ((char)i).ToString(); return a; }

    private static readonly NameEntry[] Keywords = BuildKeywords(out KeywordBuckets);
    private static readonly int[] KeywordBuckets;

    private static NameEntry[] BuildKeywords(out int[] buckets)
    {
        (string, Tok)[] kw =
        [
            ("local", Tok.Local), ("nil", Tok.Nil), ("true", Tok.True), ("false", Tok.False),
            ("if", Tok.If), ("then", Tok.Then), ("else", Tok.Else), ("elseif", Tok.Elseif), ("end", Tok.End),
            ("while", Tok.While), ("do", Tok.Do), ("repeat", Tok.Repeat), ("until", Tok.Until), ("for", Tok.For),
            ("function", Tok.Function), ("return", Tok.Return), ("break", Tok.Break), ("goto", Tok.Goto),
            ("in", Tok.In), ("and", Tok.And), ("or", Tok.Or), ("not", Tok.Not),
        ];
        var entries = new NameEntry[kw.Length];
        buckets = new int[64];
        for (int i = 0; i < kw.Length; i++)
        {
            uint h = 2166136261;
            foreach (char c in kw[i].Item1) h = (h ^ c) * 16777619;
            int b = (int)(h & 63);
            entries[i] = new NameEntry { Hash = h, Text = kw[i].Item1, Kind = kw[i].Item2, Next = buckets[b] };
            buckets[b] = i + 1;
        }
        return entries;
    }

    private static bool IsText(ReadOnlySpan<byte> span, string text)
    {
        for (int i = 0; i < span.Length; i++) if (span[i] != text[i]) return false;
        return true;
    }

    /// <summary>The raw source text of a token for error messages: names and symbols carry theirs, strings and numbers are sliced on demand.</summary>
    public string TextOf(in Token t) => t.Text.Length > 0 || t.End <= t.Start ? t.Text : src.Slice(t.Start, t.End);

    private ref NameEntry LookupName(int start, int len)
    {
        ReadOnlySpan<byte> span = bytes.AsSpan(start, len);
        uint h = 2166136261;
        for (int i = 0; i < span.Length; i++) h = (h ^ span[i]) * 16777619;
        // Keywords are shared by every compilation and never touch the per-compilation table.
        if (len <= 8)
        {
            for (int e = KeywordBuckets[(int)(h & 63)]; e != 0; e = Keywords[e - 1].Next)
            {
                ref NameEntry ke = ref Keywords[e - 1];
                if (ke.Hash == h && ke.Text.Length == len && IsText(span, ke.Text)) return ref ke;
            }
        }
        if (_nameCount > 0)
        {
            for (int e = _nameBuckets[(int)(h & (uint)(_nameBuckets.Length - 1))]; e != 0; e = _names[e - 1].Next)
            {
                ref NameEntry ne = ref _names[e - 1];
                if (ne.Hash == h && ne.Str.Bytes.SequenceEqual(span)) return ref ne;
            }
        }
        if (_nameCount == _names.Length)
        {
            int cap = _names.Length == 0 ? 8 : _names.Length * 2;
            Array.Resize(ref _names, cap);
            _nameBuckets = new int[cap * 2];
            for (int i = 0; i < _nameCount; i++)
            {
                int nb = (int)(_names[i].Hash & (uint)(_nameBuckets.Length - 1));
                _names[i].Next = _nameBuckets[nb];
                _nameBuckets[nb] = i + 1;
            }
        }
        string text = src.Slice(start, start + len);
        ref NameEntry made = ref _names[_nameCount++];
        made.Hash = h;
        made.Text = text;
        made.Str = cache.Intern(span);
        made.Kind = Tok.Name;
        int b = (int)(h & (uint)(_nameBuckets.Length - 1));
        made.Next = _nameBuckets[b];
        _nameBuckets[b] = _nameCount;
        return ref made;
    }

    public string ChunkName { get; } = chunkName;

    public LuaException Error(string msg, int line) => new($"{DisplayName(ChunkName)}:{line}: {msg}");

    private static string DisplayName(string n) => ChunkId.Display(n);

    public Token Next()
    {
        SkipTrivia();
        var t = new Token { Line = _line, Text = "" };
        _tokStart = _pos;
        if (_pos >= src.Length) { t.Kind = Tok.Eof; t.Text = "<eof>"; return t; }
        char c = src[_pos];
        if (char.IsAsciiLetter(c) || c == '_')
        {
            int s = _pos;
            while (_pos < src.Length && (char.IsAsciiLetterOrDigit(src[_pos]) || src[_pos] == '_')) _pos++;
            ref NameEntry ne = ref LookupName(s, _pos - s);
            t.Text = ne.Text;
            t.Str = ne.Str;
            t.Kind = ne.Kind;
            return t;
        }
        if (char.IsAsciiDigit(c) || (c == '.' && _pos + 1 < src.Length && char.IsAsciiDigit(src[_pos + 1])))
            return Number(t);
        _pos++;
        switch (c)
        {
            case '+': t.Kind = Tok.Plus; break;
            case '-': t.Kind = Tok.Minus; break;
            case '*': t.Kind = Tok.Star; break;
            case '/':
                if (_pos < src.Length && src[_pos] == '/') { _pos++; t.Kind = Tok.DSlash; } else t.Kind = Tok.Slash;
                break;
            case '%': t.Kind = Tok.Percent; break;
            case '^': t.Kind = Tok.Caret; break;
            case '#': t.Kind = Tok.Hash; break;
            case '{': t.Kind = Tok.LBrace; break;
            case '}': t.Kind = Tok.RBrace; break;
            case '[':
            {
                int sep = LongBracketLevel();
                if (sep >= 0) return LongString(t, sep, isComment: false);
                if (sep == -2) throw Error("invalid long string delimiter near '['", _line);
                t.Kind = Tok.LBracket;
                break;
            }
            case ']': t.Kind = Tok.RBracket; break;
            case ':':
                if (_pos < src.Length && src[_pos] == ':') { _pos++; t.Kind = Tok.DColon; t.Text = "::"; return t; }
                t.Kind = Tok.Colon; break;
            case '"':
            case '\'':
                return QuotedString(t, c);
            case '(': t.Kind = Tok.LParen; break;
            case ')': t.Kind = Tok.RParen; break;
            case ',': t.Kind = Tok.Comma; break;
            case '=':
                if (_pos < src.Length && src[_pos] == '=') { _pos++; t.Kind = Tok.Eq; t.Text = "=="; return t; }
                t.Kind = Tok.Assign; break;
            case '~':
                if (_pos < src.Length && src[_pos] == '=') { _pos++; t.Kind = Tok.Ne; t.Text = "~="; return t; }
                t.Kind = Tok.Tilde; break;
            case '&': t.Kind = Tok.Amp; break;
            case '|': t.Kind = Tok.Pipe; break;
            case '<':
                if (_pos < src.Length && src[_pos] == '=') { _pos++; t.Kind = Tok.Le; t.Text = "<="; return t; }
                if (_pos < src.Length && src[_pos] == '<') { _pos++; t.Kind = Tok.Shl; t.Text = "<<"; return t; }
                t.Kind = Tok.Lt; break;
            case '>':
                if (_pos < src.Length && src[_pos] == '=') { _pos++; t.Kind = Tok.Ge; t.Text = ">="; return t; }
                if (_pos < src.Length && src[_pos] == '>') { _pos++; t.Kind = Tok.Shr; t.Text = ">>"; return t; }
                t.Kind = Tok.Gt; break;
            case '.':
                if (_pos + 1 < src.Length && src[_pos] == '.' && src[_pos + 1] == '.') { _pos += 2; t.Kind = Tok.Ellipsis; t.Text = "..."; return t; }
                if (_pos < src.Length && src[_pos] == '.') { _pos++; t.Kind = Tok.Concat; t.Text = ".."; return t; }
                t.Kind = Tok.Dot; break;
            case ';': t.Kind = Tok.Semi; break;
            default: throw Error($"unexpected symbol near '{(c is >= ' ' and <= '~' ? c.ToString() : "<\\" + (int)c + ">")}'", _line);
        }
        t.Text = c < 128 ? OneChar[c] : c.ToString();
        return t;
    }

    private void Put(byte b)
    {
        if (_bufLen == _buf.Length) Array.Resize(ref _buf, _buf.Length * 2);
        _buf[_bufLen++] = b;
    }

    /// <summary>Just after a '[': returns the number of '=' of a long-bracket opener and consumes it, or -1 if this is no opener, or -2 for '[=' not followed by '['.</summary>
    private int LongBracketLevel()
    {
        int save = _pos, level = 0;
        while (_pos < src.Length && src[_pos] == '=') { level++; _pos++; }
        if (_pos < src.Length && src[_pos] == '[') { _pos++; return level; }
        _pos = save;
        return level == 0 ? -1 : -2;
    }

    private Token LongString(Token t, int level, bool isComment)
    {
        int startLine = _line;
        _bufLen = 0;
        if (_pos < src.Length && src[_pos] is '\n' or '\r')
        {
            char first = src[_pos++];
            if (_pos < src.Length && src[_pos] is '\n' or '\r' && src[_pos] != first) _pos++;
            _line++;
        }
        while (true)
        {
            if (_pos >= src.Length)
                throw Error(isComment ? $"unfinished long comment (starting at line {startLine}) near <eof>" : $"unfinished long string (starting at line {startLine}) near <eof>", _line);
            char c = src[_pos++];
            if (c == ']')
            {
                int save = _pos, n = 0;
                while (_pos < src.Length && src[_pos] == '=') { n++; _pos++; }
                if (n == level && _pos < src.Length && src[_pos] == ']') { _pos++; break; }
                _pos = save;
                if (!isComment) Put((byte)']');
            }
            else if (c is '\n' or '\r')
            {
                if (_pos < src.Length && src[_pos] is '\n' or '\r' && src[_pos] != c) _pos++;
                _line++;
                if (!isComment) Put((byte)'\n');
            }
            else if (!isComment) Put((byte)c);
        }
        if (isComment) return t;
        t.Kind = Tok.String;
        t.Start = _tokStart; t.End = _pos; // error messages quote the raw token text (sliced on demand)
        t.Str = InternBuffer();
        return t;
    }

    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private int ReadHexDigit(int line)
    {
        if (_pos >= src.Length || !char.IsAsciiHexDigit(src[_pos])) throw EscError("hexadecimal digit expected", line);
        return HexValue(src[_pos++]);
    }

    private Dictionary<string, LuaString>? _longStrings;

    /// <summary>Interns the scanned string; long strings are shared within one compilation, like PUC's scanner table.</summary>
    private LuaString InternBuffer()
    {
        var span = new ReadOnlySpan<byte>(_buf, 0, _bufLen);
        if (_bufLen <= LuaString.MaxShortLength) return cache.Intern(span);
        var chars = new char[_bufLen];
        for (int i = 0; i < chars.Length; i++) chars[i] = (char)_buf[i];
        string key = new(chars);
        _longStrings ??= new();
        if (_longStrings.TryGetValue(key, out LuaString found)) return found;
        LuaString made = LuaString.Create(span);
        _longStrings[key] = made;
        return made;
    }

    private char _quote;
    private int _escStart;

    /// <summary>An escape-sequence error: like PUC, "near" shows the string read so far, the escape and the offending character.</summary>
    private LuaException EscError(string msg, int line)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_quote);
        for (int i = 0; i < _bufLen; i++) sb.Append((char)_buf[i]);
        sb.Append(src.Slice(_escStart, Math.Min(_pos + 1, src.Length)));
        return Error($"{msg} near '{sb}'", line);
    }

    private void PutUtf8(long x)
    {
        if (x < 0x80) { Put((byte)x); return; }
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
        for (int i = 8 - n; i < 8; i++) Put(tmp[i]);
    }

    private Token QuotedString(Token t, char quote)
    {
        _bufLen = 0;
        _quote = quote;
        while (true)
        {
            if (_pos >= src.Length) throw Error("unfinished string near <eof>", _line);
            char c = src[_pos++];
            if (c == quote) break;
            if (c == '\n' || c == '\r') throw Error("unfinished string", _line);
            if (c != '\\') { Put((byte)c); continue; }
            if (_pos >= src.Length) throw Error("unfinished string near <eof>", _line);
            _escStart = _pos - 1;
            char e = src[_pos++];
            switch (e)
            {
                case 'n': Put((byte)'\n'); break;
                case 't': Put((byte)'\t'); break;
                case 'r': Put((byte)'\r'); break;
                case 'a': Put(7); break;
                case 'b': Put(8); break;
                case 'f': Put(12); break;
                case 'v': Put(11); break;
                case '\\': Put((byte)'\\'); break;
                case '"': Put((byte)'"'); break;
                case '\'': Put((byte)'\''); break;
                case '\n':
                case '\r':
                    if (_pos < src.Length && src[_pos] is '\n' or '\r' && src[_pos] != e) _pos++;
                    _line++;
                    Put((byte)'\n');
                    break;
                case 'x':
                {
                    int v = ReadHexDigit(_line);
                    v = v * 16 + ReadHexDigit(_line);
                    Put((byte)v);
                    break;
                }
                case 'z':
                    while (_pos < src.Length && src[_pos] is ' ' or '\t' or '\n' or '\r' or '\f' or '\v')
                    {
                        char w = src[_pos++];
                        if (w is '\n' or '\r')
                        {
                            if (_pos < src.Length && src[_pos] is '\n' or '\r' && src[_pos] != w) _pos++;
                            _line++;
                        }
                    }
                    break;
                case 'u':
                {
                    if (_pos >= src.Length || src[_pos] != '{') throw EscError("missing '{' in \\u{xxxx}", _line);
                    _pos++;
                    long v = ReadHexDigit(_line);
                    while (_pos < src.Length && char.IsAsciiHexDigit(src[_pos]))
                    {
                        if (v > (0x7FFFFFFF >> 4)) throw EscError("UTF-8 value too large", _line);
                        v = v * 16 + HexValue(src[_pos++]);
                    }
                    if (_pos >= src.Length || src[_pos] != '}') throw EscError("missing '}' in \\u{xxxx}", _line);
                    _pos++;
                    PutUtf8(v);
                    break;
                }
                case >= '0' and <= '9':
                {
                    int v = e - '0';
                    for (int k = 0; k < 2 && _pos < src.Length && char.IsAsciiDigit(src[_pos]); k++) v = v * 10 + (src[_pos++] - '0');
                    if (v > 255) throw EscError("decimal escape too large", _line);
                    Put((byte)v);
                    break;
                }
                default: _pos--; throw EscError("invalid escape sequence", _line);
            }
        }
        t.Kind = Tok.String;
        t.Start = _tokStart; t.End = _pos;
        t.Str = InternBuffer();
        return t;
    }

    private Token Number(Token t)
    {
        // Like PUC read_numeral: take the longest run of digits, dots, exponent marks (with sign) and letters,
        // then let the numeric parser decide whether it is well formed.
        int s = _pos;
        bool hex = src[_pos] == '0' && _pos + 1 < src.Length && src[_pos + 1] is 'x' or 'X';
        if (hex) _pos += 2;
        while (_pos < src.Length)
        {
            char c = src[_pos];
            bool isExp = hex ? c is 'p' or 'P' : c is 'e' or 'E';
            if (isExp && _pos + 1 < src.Length && src[_pos + 1] is '+' or '-') _pos += 2;
            else if (char.IsAsciiHexDigit(c) || c == '.' || char.IsAsciiLetter(c) || c == '_') _pos++;
            else break;
        }
        t.Start = s; t.End = _pos;
        int nlen = _pos - s;
        Span<char> text = nlen <= 64 ? stackalloc char[nlen] : new char[nlen];
        for (int k = 0; k < nlen; k++) text[k] = src[s + k];
        if (!LuaNumber.TryParse(text, out LuaValue v)) throw Error("malformed number near '" + src.Slice(s, _pos) + "'", _line);
        if (v.IsInteger) { t.Kind = Tok.Integer; t.Int = v.AsInteger; }
        else { t.Kind = Tok.Float; t.Float = v.AsFloat; }
        return t;
    }

    private void SkipTrivia()
    {
        while (_pos < src.Length)
        {
            char c = src[_pos];
            if (c is '\n' or '\r')
            {
                _pos++;
                if (_pos < src.Length && src[_pos] is '\n' or '\r' && src[_pos] != c) _pos++;
                _line++;
            }
            else if (c is ' ' or '\t' or '\f' or '\v') _pos++;
            else if (c == '-' && _pos + 1 < src.Length && src[_pos + 1] == '-')
            {
                _pos += 2;
                if (_pos < src.Length && src[_pos] == '[')
                {
                    int save = _pos++;
                    int sep = LongBracketLevel();
                    if (sep >= 0)
                    {
                        LongString(default, sep, isComment: true);
                        continue;
                    }
                    _pos = save;
                }
                while (_pos < src.Length && src[_pos] is not ('\n' or '\r')) _pos++;
            }
            else break;
        }
    }
}
