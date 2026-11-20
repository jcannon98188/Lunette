using System.Globalization;

namespace Lunette.Compiler;

internal enum Tok
{
    Eof, Name, Integer, Float,
    Local, Nil, True, False,
    Plus, Minus, Star, Slash, DSlash, Percent, Caret,
    LParen, RParen, Comma, Assign, Semi,
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
            case '(': t.Kind = Tok.LParen; break;
            case ')': t.Kind = Tok.RParen; break;
            case ',': t.Kind = Tok.Comma; break;
            case '=': t.Kind = Tok.Assign; break;
            case ';': t.Kind = Tok.Semi; break;
            default: throw Error($"unexpected symbol near '{c}'", _line);
        }
        t.Text = c.ToString();
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
