namespace Lunette.Compiler;

/// <summary>One-pass compiler: parses and emits register bytecode directly, with no AST.</summary>
internal sealed class LuaCompiler
{
    private const int MaxRegisters = 250;
    private const int UnaryPriority = 12;

    private sealed class LocalVar(string name, int reg)
    {
        public string Name { get; } = name;
        public int Reg { get; } = reg;
        public bool Captured { get; set; }
    }

    private sealed class LoopInfo(int baseReg)
    {
        public int BaseReg { get; } = baseReg;
        public List<int> Breaks { get; } = new();
    }

    private sealed class FuncState(FuncState? parent)
    {
        public FuncState? Parent { get; } = parent;
        public List<Instruction> Code { get; } = new();
        public List<int> Lines { get; } = new();
        public List<LuaValue> Consts { get; } = new();
        public List<string> Names { get; } = new();
        public List<LocalVar> Locals { get; } = new();
        public List<(string Name, UpvalDesc Desc)> Upvals { get; } = new();
        public List<Proto> Protos { get; } = new();
        public List<LoopInfo> Loops { get; } = new();
        public int Free;
        public int Max;
        public int NumParams;
        public bool IsVararg;
    }

    private readonly Lexer _lex;
    private readonly string _chunkName;
    private Token _tok;
    private Token _ahead;
    private bool _hasAhead;
    private FuncState _fs = new(null);
    private int _openIdx = -1;

    private LuaCompiler(string source, string chunkName)
    {
        _lex = new Lexer(source, chunkName);
        _chunkName = chunkName;
        _tok = _lex.Next();
    }

    public static Proto Compile(string source, string chunkName)
    {
        var c = new LuaCompiler(source, chunkName);
        c._fs.IsVararg = true;
        c.Block();
        if (c._tok.Kind != Tok.Eof) throw c.Err("'<eof>' expected");
        c.Emit(Instruction.Abc(OpCode.Return, 0, 1, 0), c._tok.Line);
        return c.Finish(c._fs);
    }

    private Proto Finish(FuncState fs)
    {
        var upvals = new UpvalDesc[fs.Upvals.Count];
        for (int i = 0; i < upvals.Length; i++) upvals[i] = fs.Upvals[i].Desc;
        return new Proto
        {
        Code = fs.Code.ToArray(),
        Constants = fs.Consts.ToArray(),
        Names = fs.Names.ToArray(),
        MaxStack = fs.Max + 1,
        Lines = fs.Lines.ToArray(),
        ChunkName = _chunkName,
        NumParams = fs.NumParams,
        IsVararg = fs.IsVararg,
        Upvalues = upvals,
        Protos = fs.Protos.ToArray(),
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

    private string ExpectName()
    {
        if (_tok.Kind != Tok.Name) throw Err("<name> expected");
        string n = _tok.Text;
        Advance();
        return n;
    }

    // ---- emission ----

    private int Emit(Instruction i, int line)
    {
        _fs.Code.Add(i);
        _fs.Lines.Add(line);
        return _fs.Code.Count - 1;
    }

    private int Here => _fs.Code.Count;

    private int EmitJmp(int line) => Emit(Instruction.JmpTo(0), line);

    private void PatchJmp(int at, int target) => _fs.Code[at] = Instruction.JmpTo(target);

    private int Alloc()
    {
        int r = _fs.Free;
        Reserve(r + 1);
        _fs.Free = r + 1;
        return r;
    }

    /// <summary>Makes registers below <paramref name="upTo"/> part of the frame.</summary>
    private void Reserve(int upTo)
    {
        if (upTo > MaxRegisters) throw Err("function or expression needs too many registers");
        if (upTo - 1 > _fs.Max) _fs.Max = upTo - 1;
    }

    private int AddConst(LuaValue v)
    {
        _fs.Consts.Add(v);
        return _fs.Consts.Count - 1;
    }

    private int AddName(string n)
    {
        int i = _fs.Names.IndexOf(n);
        if (i >= 0) return i;
        _fs.Names.Add(n);
        return _fs.Names.Count - 1;
    }

    // ---- variable resolution ----

    private static LocalVar? FindLocal(FuncState fs, string name)
    {
        for (int i = fs.Locals.Count - 1; i >= 0; i--)
            if (fs.Locals[i].Name == name) return fs.Locals[i];
        return null;
    }

    private static int FindUpval(FuncState fs, string name)
    {
        for (int i = 0; i < fs.Upvals.Count; i++)
            if (fs.Upvals[i].Name == name) return i;
        if (fs.Parent is null) return -1;
        var local = FindLocal(fs.Parent, name);
        if (local is not null)
        {
            local.Captured = true;
            fs.Upvals.Add((name, new UpvalDesc(true, local.Reg)));
            return fs.Upvals.Count - 1;
        }
        int outer = FindUpval(fs.Parent, name);
        if (outer < 0) return -1;
        fs.Upvals.Add((name, new UpvalDesc(false, outer)));
        return fs.Upvals.Count - 1;
    }

    private void LoadVariable(string name, int target, int line)
    {
        var local = FindLocal(_fs, name);
        if (local is not null) { Emit(Instruction.Abc(OpCode.Move, target, local.Reg, 0), line); return; }
        int up = FindUpval(_fs, name);
        if (up >= 0) Emit(Instruction.Abc(OpCode.GetUpval, target, up, 0), line);
        else Emit(Instruction.ABx(OpCode.GetGlobal, target, AddName(name)), line);
    }

    private void StoreVariable(string name, int source, int line)
    {
        var local = FindLocal(_fs, name);
        if (local is not null) { Emit(Instruction.Abc(OpCode.Move, local.Reg, source, 0), line); return; }
        int up = FindUpval(_fs, name);
        if (up >= 0) Emit(Instruction.Abc(OpCode.SetUpval, source, up, 0), line);
        else Emit(Instruction.ABx(OpCode.SetGlobal, source, AddName(name)), line);
    }

    // ---- statements ----

    private bool BlockFollow() =>
        _tok.Kind is Tok.Eof or Tok.End or Tok.Else or Tok.Elseif or Tok.Until;

    private void Block()
    {
        while (!BlockFollow())
        {
            if (_tok.Kind == Tok.Return)
            {
                ReturnStatement();
                return;
            }
            Statement();
        }
    }

    private void ScopedBlock()
    {
        int nlocals = _fs.Locals.Count;
        int free = _fs.Free;
        Block();
        LeaveScope(nlocals, free);
    }

    /// <summary>Drops the locals declared since <paramref name="nlocals"/>, closing their upvalues if any were captured.</summary>
    private void LeaveScope(int nlocals, int free)
    {
        bool captured = false;
        for (int i = nlocals; i < _fs.Locals.Count; i++) captured |= _fs.Locals[i].Captured;
        _fs.Locals.RemoveRange(nlocals, _fs.Locals.Count - nlocals);
        if (captured) Emit(Instruction.Abc(OpCode.Close, free, 0, 0), _tok.Line);
        _fs.Free = free;
    }

    private void Statement()
    {
        switch (_tok.Kind)
        {
            case Tok.Semi:
                Advance();
                return;
            case Tok.Local:
                Advance();
                if (_tok.Kind == Tok.Function) LocalFunction();
                else LocalStatement();
                return;
            case Tok.Function:
                FunctionStatement();
                return;
            case Tok.If:
                IfStatement();
                return;
            case Tok.While:
                WhileStatement();
                return;
            case Tok.Repeat:
                RepeatStatement();
                return;
            case Tok.For:
                ForStatement();
                return;
            case Tok.Do:
            {
                Advance();
                ScopedBlock();
                Expect(Tok.End, "end");
                return;
            }
            case Tok.Break:
                BreakStatement();
                return;
            case Tok.Name:
                if (Peek().Kind is Tok.Assign or Tok.Comma) AssignStatement();
                else CallStatement();
                return;
            default:
                throw Err("unexpected symbol");
        }
    }

    private void BreakStatement()
    {
        int line = _tok.Line;
        Advance();
        if (_fs.Loops.Count == 0) throw _lex.Error($"break outside a loop at line {line}", line);
        var loop = _fs.Loops[^1];
        Emit(Instruction.Abc(OpCode.Close, loop.BaseReg, 0, 0), line);
        loop.Breaks.Add(EmitJmp(line));
    }

    private void ReturnStatement()
    {
        int line = _tok.Line;
        Advance();
        int first = _fs.Free;
        if (BlockFollow() || _tok.Kind == Tok.Semi)
        {
            Emit(Instruction.Abc(OpCode.Return, first, 1, 0), line);
        }
        else
        {
            var (n, open) = ExprList();
            if (n == 1 && open >= 0 && _fs.Code[open].Op == OpCode.Call)
            {
                var call = _fs.Code[open];
                _fs.Code[open] = Instruction.Abc(OpCode.TailCall, call.A, call.B, 0);
                Emit(Instruction.Abc(OpCode.Return, first, 0, 0), line);
            }
            else if (open >= 0)
            {
                SetOpen(open, -1);
                Emit(Instruction.Abc(OpCode.Return, first, 0, 0), line);
            }
            else Emit(Instruction.Abc(OpCode.Return, first, n + 1, 0), line);
        }
        if (_tok.Kind == Tok.Semi) Advance();
        if (!BlockFollow()) throw Err("'<eof>' expected");
        _fs.Free = first;
    }

    private void LocalFunction()
    {
        int line = _tok.Line;
        Advance();
        string name = ExpectName();
        int reg = Alloc();
        _fs.Locals.Add(new LocalVar(name, reg));
        FunctionBody(reg, line);
    }

    private void FunctionStatement()
    {
        int line = _tok.Line;
        Advance();
        string name = ExpectName();
        int save = _fs.Free;
        int tmp = Alloc();
        FunctionBody(tmp, line);
        StoreVariable(name, tmp, line);
        _fs.Free = save;
    }

    private void LocalStatement()
    {
        var names = new List<string> { ExpectName() };
        while (_tok.Kind == Tok.Comma)
        {
            Advance();
            names.Add(ExpectName());
        }
        int baseReg = _fs.Free;
        int n = 0, open = -1;
        if (_tok.Kind == Tok.Assign)
        {
            Advance();
            (n, open) = ExprList();
        }
        AdjustList(names.Count, n, open, baseReg);
        for (int i = 0; i < names.Count; i++) _fs.Locals.Add(new LocalVar(names[i], baseReg + i));
    }

    private void AssignStatement()
    {
        var names = new List<(string Name, int Line)>();
        while (true)
        {
            names.Add((ExpectName(), _tok.Line));
            if (_tok.Kind != Tok.Comma) break;
            Advance();
        }
        Expect(Tok.Assign, "=");
        int baseReg = _fs.Free;
        var (n, open) = ExprList();
        AdjustList(names.Count, n, open, baseReg);
        for (int i = names.Count - 1; i >= 0; i--) StoreVariable(names[i].Name, baseReg + i, names[i].Line);
        _fs.Free = baseReg;
    }

    private void CallStatement()
    {
        int save = _fs.Free;
        int tmp = Alloc();
        Expr(tmp, 0);
        if (_openIdx < 0 || _fs.Code[_openIdx].Op != OpCode.Call) throw Err("syntax error");
        SetOpen(_openIdx, 0);
        _fs.Free = save;
    }

    private void IfStatement()
    {
        var endJumps = new List<int>();
        while (true)
        {
            int line = _tok.Line;
            Advance(); // if / elseif
            int skip = Condition(line);
            Expect(Tok.Then, "then");
            ScopedBlock();
            if (_tok.Kind is Tok.Elseif or Tok.Else) endJumps.Add(EmitJmp(_tok.Line));
            PatchJmp(skip, Here);
            if (_tok.Kind == Tok.Elseif) continue;
            if (_tok.Kind == Tok.Else)
            {
                Advance();
                ScopedBlock();
            }
            break;
        }
        Expect(Tok.End, "end");
        foreach (int j in endJumps) PatchJmp(j, Here);
    }

    /// <summary>Compiles a condition and emits a jump taken when it is falsy; returns the jump to patch.</summary>
    private int Condition(int line)
    {
        int save = _fs.Free;
        int t = Alloc();
        Expr(t, 0);
        Emit(Instruction.Abc(OpCode.Test, t, 0, 0), line);
        int j = EmitJmp(line);
        _fs.Free = save;
        return j;
    }

    private void WhileStatement()
    {
        int line = _tok.Line;
        Advance();
        int top = Here;
        int exit = Condition(line);
        Expect(Tok.Do, "do");
        var loop = new LoopInfo(_fs.Free);
        _fs.Loops.Add(loop);
        ScopedBlock();
        Expect(Tok.End, "end");
        _fs.Loops.RemoveAt(_fs.Loops.Count - 1);
        PatchJmp(EmitJmp(line), top);
        PatchJmp(exit, Here);
        foreach (int b in loop.Breaks) PatchJmp(b, Here);
    }

    private void RepeatStatement()
    {
        int line = _tok.Line;
        Advance();
        int top = Here;
        var loop = new LoopInfo(_fs.Free);
        _fs.Loops.Add(loop);
        int nlocals = _fs.Locals.Count;
        int free = _fs.Free;
        Block();
        Expect(Tok.Until, "until");
        int t = Alloc();
        Expr(t, 0);
        bool captured = false;
        for (int i = nlocals; i < _fs.Locals.Count; i++) captured |= _fs.Locals[i].Captured;
        if (captured) Emit(Instruction.Abc(OpCode.Close, free, 0, 0), line);
        Emit(Instruction.Abc(OpCode.Test, t, 0, 0), line);
        PatchJmp(EmitJmp(line), top);
        _fs.Locals.RemoveRange(nlocals, _fs.Locals.Count - nlocals);
        _fs.Free = free;
        _fs.Loops.RemoveAt(_fs.Loops.Count - 1);
        foreach (int b in loop.Breaks) PatchJmp(b, Here);
    }

    private void ForStatement()
    {
        int line = _tok.Line;
        Advance();
        string name = ExpectName();
        if (_tok.Kind != Tok.Assign) throw Err("'=' expected");
        Advance();
        int a = Alloc();
        Expr(a, 0);
        Expect(Tok.Comma, ",");
        Expr(Alloc(), 0);
        int stepReg = Alloc();
        if (_tok.Kind == Tok.Comma)
        {
            Advance();
            Expr(stepReg, 0);
        }
        else Emit(Instruction.ABx(OpCode.LoadK, stepReg, AddConst(LuaValue.FromInteger(1))), line);
        Expect(Tok.Do, "do");
        Emit(Instruction.Abc(OpCode.ForPrep, a, 0, 0), line);
        int exit = EmitJmp(line);

        int nlocals = _fs.Locals.Count;
        int free = _fs.Free; // a + 3
        _fs.Locals.Add(new LocalVar(name, Alloc()));
        var loop = new LoopInfo(free);
        _fs.Loops.Add(loop);
        int body = Here;
        Block();
        Expect(Tok.End, "end");
        _fs.Loops.RemoveAt(_fs.Loops.Count - 1);
        LeaveScope(nlocals, free);
        Emit(Instruction.Abc(OpCode.ForLoop, a, 0, 0), line);
        PatchJmp(EmitJmp(line), body);
        PatchJmp(exit, Here);
        foreach (int b in loop.Breaks) PatchJmp(b, Here);
        _fs.Free = a;
    }

    // ---- functions ----

    private void FunctionBody(int target, int line)
    {
        var child = new FuncState(_fs);
        _fs = child;
        Expect(Tok.LParen, "(");
        if (_tok.Kind != Tok.RParen)
        {
            do
            {
                if (_tok.Kind == Tok.Comma) Advance();
                if (_tok.Kind == Tok.Ellipsis)
                {
                    Advance();
                    child.IsVararg = true;
                    break;
                }
                string p = ExpectName();
                child.Locals.Add(new LocalVar(p, Alloc()));
                child.NumParams++;
            } while (_tok.Kind == Tok.Comma);
        }
        Expect(Tok.RParen, ")");
        Block();
        int endLine = _tok.Line;
        Expect(Tok.End, "end");
        Emit(Instruction.Abc(OpCode.Return, 0, 1, 0), endLine);
        _fs = child.Parent!;
        _fs.Protos.Add(Finish(child));
        Emit(Instruction.ABx(OpCode.Closure, target, _fs.Protos.Count - 1), line);
    }

    // ---- expression lists ----

    /// <summary>
    /// Compiles comma-separated expressions into consecutive fresh registers.
    /// Returns the count and the index of a trailing open Call/Vararg instruction (or -1).
    /// </summary>
    private (int Count, int Open) ExprList()
    {
        int n = 0;
        while (true)
        {
            Expr(Alloc(), 0);
            n++;
            if (_tok.Kind != Tok.Comma) return (n, _openIdx);
            Advance();
        }
    }

    /// <summary>Sets how many values an open Call/Vararg yields (-1: all, up to top).</summary>
    private void SetOpen(int idx, int wanted)
    {
        var ins = _fs.Code[idx];
        _fs.Code[idx] = Instruction.Abc(ins.Op, ins.A, ins.B, wanted + 1);
    }

    /// <summary>Adjusts an expression list of <paramref name="n"/> values to <paramref name="nvars"/> registers from <paramref name="baseReg"/>.</summary>
    private void AdjustList(int nvars, int n, int open, int baseReg)
    {
        int extra = nvars - n;
        if (open >= 0) SetOpen(open, Math.Max(extra + 1, 0));
        else if (extra > 0) Emit(Instruction.Abc(OpCode.LoadNil, baseReg + n, extra - 1, 0), _tok.Line);
        Reserve(baseReg + Math.Max(nvars, n));
        _fs.Free = baseReg + nvars;
    }

    // ---- expressions ----

    private static bool BinaryPriority(Tok t, out int left, out int right)
    {
        switch (t)
        {
            case Tok.Or: left = right = 1; return true;
            case Tok.And: left = right = 2; return true;
            case Tok.Eq or Tok.Ne or Tok.Lt or Tok.Le or Tok.Gt or Tok.Ge: left = right = 3; return true;
            case Tok.Plus or Tok.Minus: left = right = 10; return true;
            case Tok.Star or Tok.Slash or Tok.DSlash or Tok.Percent: left = right = 11; return true;
            case Tok.Caret: left = 14; right = 13; return true;
            default: left = right = 0; return false;
        }
    }

    /// <summary>Compiles an expression binding tighter than <paramref name="limit"/> into register <paramref name="target"/>.</summary>
    private void Expr(int target, int limit)
    {
        _openIdx = -1;
        int line = _tok.Line;
        if (_tok.Kind is Tok.Minus or Tok.Not)
        {
            bool isNot = _tok.Kind == Tok.Not;
            Advance();
            Expr(target, UnaryPriority);
            Emit(Instruction.Abc(isNot ? OpCode.Not : OpCode.Unm, target, target, 0), line);
            _openIdx = -1;
        }
        else Simple(target);

        while (BinaryPriority(_tok.Kind, out int left, out int right) && left > limit)
        {
            Tok op = _tok.Kind;
            int opLine = _tok.Line;
            Advance();
            if (op is Tok.And or Tok.Or)
            {
                Emit(Instruction.Abc(OpCode.Test, target, 0, op == Tok.Or ? 1 : 0), opLine);
                int j = EmitJmp(opLine);
                Expr(target, right);
                PatchJmp(j, Here);
            }
            else
            {
                int save = _fs.Free;
                int rhs = Alloc();
                Expr(rhs, right);
                EmitBinary(op, target, rhs, opLine);
                _fs.Free = save;
            }
            _openIdx = -1;
        }
    }

    private void EmitBinary(Tok op, int target, int rhs, int line)
    {
        switch (op)
        {
            case Tok.Plus: Emit(Instruction.Abc(OpCode.Add, target, target, rhs), line); break;
            case Tok.Minus: Emit(Instruction.Abc(OpCode.Sub, target, target, rhs), line); break;
            case Tok.Star: Emit(Instruction.Abc(OpCode.Mul, target, target, rhs), line); break;
            case Tok.Slash: Emit(Instruction.Abc(OpCode.Div, target, target, rhs), line); break;
            case Tok.DSlash: Emit(Instruction.Abc(OpCode.IDiv, target, target, rhs), line); break;
            case Tok.Percent: Emit(Instruction.Abc(OpCode.Mod, target, target, rhs), line); break;
            case Tok.Caret: Emit(Instruction.Abc(OpCode.Pow, target, target, rhs), line); break;
            case Tok.Eq: Emit(Instruction.Abc(OpCode.Eq, target, target, rhs), line); break;
            case Tok.Ne:
                Emit(Instruction.Abc(OpCode.Eq, target, target, rhs), line);
                Emit(Instruction.Abc(OpCode.Not, target, target, 0), line);
                break;
            case Tok.Lt: Emit(Instruction.Abc(OpCode.Lt, target, target, rhs), line); break;
            case Tok.Le: Emit(Instruction.Abc(OpCode.Le, target, target, rhs), line); break;
            case Tok.Gt: Emit(Instruction.Abc(OpCode.Lt, target, rhs, target), line); break;
            case Tok.Ge: Emit(Instruction.Abc(OpCode.Le, target, rhs, target), line); break;
            default: throw new InvalidOperationException(op.ToString());
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
                return;
            case Tok.Float:
                Emit(Instruction.ABx(OpCode.LoadK, target, AddConst(LuaValue.FromFloat(_tok.Float))), line);
                Advance();
                return;
            case Tok.Nil:
                Emit(Instruction.Abc(OpCode.LoadNil, target, 0, 0), line);
                Advance();
                return;
            case Tok.True:
                Emit(Instruction.Abc(OpCode.LoadBool, target, 1, 0), line);
                Advance();
                return;
            case Tok.False:
                Emit(Instruction.Abc(OpCode.LoadBool, target, 0, 0), line);
                Advance();
                return;
            case Tok.Ellipsis:
                if (!_fs.IsVararg) throw Err("cannot use '...' outside a vararg function");
                Advance();
                _openIdx = Emit(Instruction.Abc(OpCode.Vararg, target, 0, 2), line);
                return;
            case Tok.Function:
                Advance();
                FunctionBody(target, line);
                return;
            case Tok.Name:
                LoadVariable(_tok.Text, target, line);
                Advance();
                break;
            case Tok.LParen:
                Advance();
                Expr(target, 0);
                Expect(Tok.RParen, ")");
                _openIdx = -1;
                break;
            default:
                throw Err("unexpected symbol");
        }
        while (_tok.Kind == Tok.LParen) CallSuffix(target);
    }

    private void CallSuffix(int target)
    {
        int line = _tok.Line;
        Advance(); // (
        int save = _fs.Free;
        int fn = target;
        if (target != _fs.Free - 1)
        {
            fn = Alloc();
            Emit(Instruction.Abc(OpCode.Move, fn, target, 0), line);
        }
        int b = 1;
        if (_tok.Kind != Tok.RParen)
        {
            var (n, open) = ExprList();
            if (open >= 0)
            {
                SetOpen(open, -1);
                b = 0;
            }
            else b = n + 1;
        }
        Expect(Tok.RParen, ")");
        int call = Emit(Instruction.Abc(OpCode.Call, fn, b, 2), line);
        if (fn != target)
        {
            Emit(Instruction.Abc(OpCode.Move, target, fn, 0), line);
            _fs.Free = save;
            _openIdx = -1;
        }
        else
        {
            _fs.Free = fn + 1;
            _openIdx = call;
        }
    }
}
