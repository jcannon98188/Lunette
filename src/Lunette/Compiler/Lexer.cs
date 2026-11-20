using System.Globalization;

namespace Lunette.Compiler;

internal enum Tok
{
    Eof, Name, Integer, Float,
    Local, Nil, True, False,
    If, Then, Else, Elseif, End, While, Do, Repeat, Until, For, Function, Return, Break, And, Or, Not,
    Eq, Ne, Lt, Le, Gt, Ge, Ellipsis,
    Plus, Minus, Star, Slash, DSlash, Percent, Caret,
    Amp, Pipe, Tilde, Shl, Shr,
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
                "if" => Tok.If, "then" => Tok.Then, "else" => Tok.Else, "elseif" => Tok.Elseif, "end" => Tok.End,
                "while" => Tok.While, "do" => Tok.Do, "repeat" => Tok.Repeat, "until" => Tok.Until, "for" => Tok.For,
                "function" => Tok.Function, "return" => Tok.Return, "break" => Tok.Break,
                "and" => Tok.And, "or" => Tok.Or, "not" => Tok.Not,
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
                throw Error("unexpected symbol near '.'", _line);
            case ';': t.Kind = Tok.Semi; break;
            default: throw Error($"unexpected symbol near '{c}'", _line);
        }
        t.Text = c.ToString();
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
        string text = src[s.._pos];
        t.Text = text;
        if (!LuaNumber.TryParse(text, out LuaValue v)) throw Error("malformed number near '" + text + "'", _line);
        if (v.IsInteger) { t.Kind = Tok.Integer; t.Int = v.AsInteger; }
        else { t.Kind = Tok.Float; t.Float = v.AsFloat; }
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
