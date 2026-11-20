namespace Lunette.Compiler;

/// <summary>One-pass compiler: parses and emits register bytecode directly, with no AST.</summary>
internal sealed class LuaCompiler
{
    private const int MaxRegisters = 250;
    private const int UnaryPriority = 12;

    private readonly Lexer _lex;
    private Token _tok;
    private Token _ahead;
    private bool _hasAhead;

    private readonly List<Instruction> _code = new();
    private readonly List<int> _lines = new();
    private readonly List<LuaValue> _consts = new();
    private readonly List<string> _names = new();
    private readonly List<(string Name, int Reg)> _locals = new();
    private int _free;
    private int _max;

    private LuaCompiler(string source, string chunkName)
    {
        _lex = new Lexer(source, chunkName);
        _tok = _lex.Next();
    }

    public static Proto Compile(string source, string chunkName)
    {
        var c = new LuaCompiler(source, chunkName);
        c.Block();
        c.Emit(Instruction.Abc(OpCode.Return, 0, 0, 0), c._tok.Line);
        return new Proto
        {
            Code = c._code.ToArray(),
            Constants = c._consts.ToArray(),
            Names = c._names.ToArray(),
            MaxStack = c._max + 1,
            Lines = c._lines.ToArray(),
            ChunkName = chunkName,
        };
    }

    // ---- token helpers ----

    private void Advance()
    {
        if (_hasAhead) { _tok = _ahead; _hasAhead = false; }
        else _tok = _lex.Next();
    }

    private Token Peek()
    {
        if (!_hasAhead) { _ahead = _lex.Next(); _hasAhead = true; }
        return _ahead;
    }

    private LuaException Err(string msg) => _lex.Error($"{msg} near '{_tok.Text}'", _tok.Line);

    private void Expect(Tok kind, string what)
    {
        if (_tok.Kind != kind) throw Err($"'{what}' expected");
        Advance();
    }

    // ---- emission ----

    private int Emit(Instruction i, int line)
    {
        _code.Add(i);
        _lines.Add(line);
        return _code.Count - 1;
    }

    private int Alloc()
    {
        if (_free >= MaxRegisters) throw Err("function or expression needs too many registers");
        int r = _free++;
        if (r > _max) _max = r;
        return r;
    }

    private int AddConst(LuaValue v)
    {
        _consts.Add(v);
        return _consts.Count - 1;
    }

    private int AddName(string n)
    {
        int i = _names.IndexOf(n);
        if (i >= 0) return i;
        _names.Add(n);
        return _names.Count - 1;
    }

    private int FindLocal(string name)
    {
        for (int i = _locals.Count - 1; i >= 0; i--)
            if (_locals[i].Name == name) return _locals[i].Reg;
        return -1;
    }

    // ---- statements ----

    private void Block()
    {
        while (_tok.Kind != Tok.Eof)
        {
            Statement();
        }
    }

    private void Statement()
    {
        switch (_tok.Kind)
        {
            case Tok.Semi:
                Advance();
                return;
            case Tok.Local:
                LocalStatement();
                return;
            case Tok.Name:
                if (Peek().Kind == Tok.LParen) CallStatement();
                else AssignStatement();
                return;
            default:
                throw Err("unexpected symbol");
        }
    }

    private void LocalStatement()
    {
        Advance();
        if (_tok.Kind != Tok.Name) throw Err("<name> expected");
        string name = _tok.Text;
        int line = _tok.Line;
        Advance();
        int reg = Alloc();
        if (_tok.Kind == Tok.Assign)
        {
            Advance();
            Expr(reg, 0);
        }
        else Emit(Instruction.Abc(OpCode.LoadNil, reg, 0, 0), line);
        _locals.Add((name, reg));
    }

    private void AssignStatement()
    {
        string name = _tok.Text;
        int line = _tok.Line;
        Advance();
        Expect(Tok.Assign, "=");
        int local = FindLocal(name);
        if (local >= 0)
        {
            Expr(local, 0);
            return;
        }
        int save = _free;
        int tmp = Alloc();
        Expr(tmp, 0);
        Emit(Instruction.ABx(OpCode.SetGlobal, tmp, AddName(name)), line);
        _free = save;
    }

    private void CallStatement()
    {
        string name = _tok.Text;
        int line = _tok.Line;
        Advance();
        Advance(); // (
        int save = _free;
        int fn = Alloc();
        LoadVariable(name, fn, line);
        int nargs = 0;
        if (_tok.Kind != Tok.RParen)
        {
            do
            {
                if (nargs > 0) Advance(); // comma
                int arg = Alloc();
                Expr(arg, 0);
                nargs++;
            } while (_tok.Kind == Tok.Comma);
        }
        Expect(Tok.RParen, ")");
        Emit(Instruction.Abc(OpCode.Call, fn, nargs, 0), line);
        _free = save;
    }

    private void LoadVariable(string name, int target, int line)
    {
        int local = FindLocal(name);
        if (local >= 0) Emit(Instruction.Abc(OpCode.Move, target, local, 0), line);
        else Emit(Instruction.ABx(OpCode.GetGlobal, target, AddName(name)), line);
    }

    // ---- expressions ----

    private static bool BinaryOp(Tok t, out OpCode op, out int left, out int right)
    {
        switch (t)
        {
            case Tok.Plus: op = OpCode.Add; left = right = 10; return true;
            case Tok.Minus: op = OpCode.Sub; left = right = 10; return true;
            case Tok.Star: op = OpCode.Mul; left = right = 11; return true;
            case Tok.Slash: op = OpCode.Div; left = right = 11; return true;
            case Tok.DSlash: op = OpCode.IDiv; left = right = 11; return true;
            case Tok.Percent: op = OpCode.Mod; left = right = 11; return true;
            case Tok.Caret: op = OpCode.Pow; left = 14; right = 13; return true;
            default: op = default; left = right = 0; return false;
        }
    }

    /// <summary>Compiles an expression binding tighter than <paramref name="limit"/> into register <paramref name="target"/>.</summary>
    private void Expr(int target, int limit)
    {
        int line = _tok.Line;
        if (_tok.Kind == Tok.Minus)
        {
            Advance();
            Expr(target, UnaryPriority);
            Emit(Instruction.Abc(OpCode.Unm, target, target, 0), line);
        }
        else Simple(target);

        while (BinaryOp(_tok.Kind, out var op, out int left, out int right) && left > limit)
        {
            int opLine = _tok.Line;
            Advance();
            int save = _free;
            int rhs = Alloc();
            Expr(rhs, right);
            Emit(Instruction.Abc(op, target, target, rhs), opLine);
            _free = save;
        }
    }

    private void Simple(int target)
    {
        int line = _tok.Line;
        switch (_tok.Kind)
        {
            case Tok.Integer:
                Emit(Instruction.ABx(OpCode.LoadK, target, AddConst(LuaValue.FromInteger(_tok.Int))), line);
                Advance();
                break;
            case Tok.Float:
                Emit(Instruction.ABx(OpCode.LoadK, target, AddConst(LuaValue.FromFloat(_tok.Float))), line);
                Advance();
                break;
            case Tok.Nil:
                Emit(Instruction.Abc(OpCode.LoadNil, target, 0, 0), line);
                Advance();
                break;
            case Tok.True:
                Emit(Instruction.Abc(OpCode.LoadBool, target, 1, 0), line);
                Advance();
                break;
            case Tok.False:
                Emit(Instruction.Abc(OpCode.LoadBool, target, 0, 0), line);
                Advance();
                break;
            case Tok.Name:
                LoadVariable(_tok.Text, target, line);
                Advance();
                break;
            case Tok.LParen:
                Advance();
                Expr(target, 0);
                Expect(Tok.RParen, ")");
                break;
            default:
                throw Err("unexpected symbol");
        }
    }
}
