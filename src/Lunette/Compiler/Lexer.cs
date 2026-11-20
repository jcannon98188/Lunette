using System.Globalization;

namespace Lunette.Compiler;

internal enum Tok
{
    Eof, Name, Integer, Float,
    Local, Nil, True, False,
    If, Then, Else, Elseif, End, While, Do, Repeat, Until, For, Function, Return, Break, And, Or, Not, In,
    Eq, Ne, Lt, Le, Gt, Ge, Ellipsis,
    Plus, Minus, Star, Slash, DSlash, Percent, Caret,
    LParen, RParen, Comma, Assign, Semi,
    String, Hash, LBrace, RBrace, LBracket, RBracket, Dot, Colon,
}

internal struct Token
{
    public Tok Kind;
    public string Text;
    public long Int;
    public double Float;
    public int Line;
}

internal sealed class Lexer(string src, string chunkName)
{
    private int _pos;
    private int _line = 1;

    public string ChunkName { get; } = chunkName;

    public LuaException Error(string msg, int line) => new($"{DisplayName(ChunkName)}:{line}: {msg}");

    private static string DisplayName(string n) => n.Length > 0 && (n[0] is '=' or '@') ? n[1..] : $"[string \"{n}\"]";

    public Token Next()
    {
        SkipTrivia();
        var t = new Token { Line = _line, Text = "" };
        if (_pos >= src.Length) { t.Kind = Tok.Eof; t.Text = "<eof>"; return t; }
        char c = src[_pos];
        if (char.IsAsciiLetter(c) || c == '_')
        {
            int s = _pos;
            while (_pos < src.Length && (char.IsAsciiLetterOrDigit(src[_pos]) || src[_pos] == '_')) _pos++;
            t.Text = src[s.._pos];
            t.Kind = t.Text switch
            {
                "local" => Tok.Local, "nil" => Tok.Nil, "true" => Tok.True, "false" => Tok.False,
                "if" => Tok.If, "then" => Tok.Then, "else" => Tok.Else, "elseif" => Tok.Elseif, "end" => Tok.End,
                "while" => Tok.While, "do" => Tok.Do, "repeat" => Tok.Repeat, "until" => Tok.Until, "for" => Tok.For,
                "function" => Tok.Function, "return" => Tok.Return, "break" => Tok.Break,
                "in" => Tok.In, "and" => Tok.And, "or" => Tok.Or, "not" => Tok.Not,
                _ => Tok.Name,
            };
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
            case '[': t.Kind = Tok.LBracket; break;
            case ']': t.Kind = Tok.RBracket; break;
            case ':': t.Kind = Tok.Colon; break;
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
                throw Error("unexpected symbol near '~'", _line);
            case '<':
                if (_pos < src.Length && src[_pos] == '=') { _pos++; t.Kind = Tok.Le; t.Text = "<="; return t; }
                t.Kind = Tok.Lt; break;
            case '>':
                if (_pos < src.Length && src[_pos] == '=') { _pos++; t.Kind = Tok.Ge; t.Text = ">="; return t; }
                t.Kind = Tok.Gt; break;
            case '.':
                if (_pos + 1 < src.Length && src[_pos] == '.' && src[_pos + 1] == '.') { _pos += 2; t.Kind = Tok.Ellipsis; t.Text = "..."; return t; }
                t.Kind = Tok.Dot; break;
            case ';': t.Kind = Tok.Semi; break;
            default: throw Error($"unexpected symbol near '{c}'", _line);
        }
        t.Text = c.ToString();
        return t;
    }

    private Token QuotedString(Token t, char quote)
    {
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            if (_pos >= src.Length || src[_pos] == '\n') throw Error("unfinished string", _line);
            char c = src[_pos++];
            if (c == quote) break;
            if (c != '\\') { sb.Append(c); continue; }
            if (_pos >= src.Length) throw Error("unfinished string", _line);
            char e = src[_pos++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'v': sb.Append('\v'); break;
                case '\n': _line++; sb.Append('\n'); break;
                case >= '0' and <= '9':
                {
                    int v = e - '0';
                    for (int k = 0; k < 2 && _pos < src.Length && char.IsAsciiDigit(src[_pos]); k++) v = v * 10 + (src[_pos++] - '0');
                    if (v > 255) throw Error("decimal escape too large", _line);
                    sb.Append((char)v);
                    break;
                }
                default: sb.Append(e); break;
            }
        }
        t.Kind = Tok.String;
        t.Text = sb.ToString();
        return t;
    }

    private Token Number(Token t)
    {
        int s = _pos;
        if (src[_pos] == '0' && _pos + 1 < src.Length && src[_pos + 1] is 'x' or 'X')
        {
            _pos += 2;
            int hs = _pos;
            while (_pos < src.Length && char.IsAsciiHexDigit(src[_pos])) _pos++;
            if (hs == _pos) throw Error("malformed number near '" + src[s.._pos] + "'", _line);
            ulong v = 0;
            for (int i = hs; i < _pos; i++) v = unchecked(v * 16 + (ulong)Convert.ToInt32(src[i].ToString(), 16));
            t.Kind = Tok.Integer; t.Int = unchecked((long)v); t.Text = src[s.._pos];
            return t;
        }
        bool isFloat = false;
        while (_pos < src.Length && char.IsAsciiDigit(src[_pos])) _pos++;
        if (_pos < src.Length && src[_pos] == '.')
        {
            isFloat = true; _pos++;
            while (_pos < src.Length && char.IsAsciiDigit(src[_pos])) _pos++;
        }
        if (_pos < src.Length && src[_pos] is 'e' or 'E')
        {
            isFloat = true; _pos++;
            if (_pos < src.Length && src[_pos] is '+' or '-') _pos++;
            int es = _pos;
            while (_pos < src.Length && char.IsAsciiDigit(src[_pos])) _pos++;
            if (es == _pos) throw Error("malformed number near '" + src[s.._pos] + "'", _line);
        }
        string text = src[s.._pos];
        t.Text = text;
        if (!isFloat && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long iv))
        {
            t.Kind = Tok.Integer; t.Int = iv;
        }
        else
        {
            t.Kind = Tok.Float; t.Float = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        return t;
    }

    private void SkipTrivia()
    {
        while (_pos < src.Length)
        {
            char c = src[_pos];
            if (c == '\n') { _line++; _pos++; }
            else if (c is ' ' or '\t' or '\r') _pos++;
            else if (c == '-' && _pos + 1 < src.Length && src[_pos + 1] == '-')
            {
                while (_pos < src.Length && src[_pos] != '\n') _pos++;
            }
            else break;
        }
    }
}
