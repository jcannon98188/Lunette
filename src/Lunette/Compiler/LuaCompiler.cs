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
            case Tok.LParen:
                ExprStatement();
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
        FunctionBody(reg, line, false);
    }

    private void FunctionStatement()
    {
        int line = _tok.Line;
        Advance();
        string name = ExpectName();
        int save = _fs.Free;
        if (_tok.Kind is Tok.Dot or Tok.Colon)
        {
            int obj = Alloc();
            LoadVariable(name, obj, line);
            string field = "";
            bool isMethod = false;
            while (_tok.Kind is Tok.Dot or Tok.Colon)
            {
                if (field.Length > 0)
                {
                    int kk = Alloc();
                    Emit(Instruction.ABx(OpCode.LoadK, kk, AddConst(LuaValue.FromString(field))), line);
                    Emit(Instruction.Abc(OpCode.GetTable, obj, obj, kk), line);
                    _fs.Free = obj + 1;
                }
                isMethod = _tok.Kind == Tok.Colon;
                Advance();
                field = ExpectName();
                if (isMethod) break;
            }
            int key = Alloc();
            Emit(Instruction.ABx(OpCode.LoadK, key, AddConst(LuaValue.FromString(field))), line);
            int fnReg = Alloc();
            FunctionBody(fnReg, line, isMethod);
            Emit(Instruction.Abc(OpCode.SetTable, obj, key, fnReg), line);
        }
        else
        {
            int tmp = Alloc();
            FunctionBody(tmp, line, false);
            StoreVariable(name, tmp, line);
        }
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

    /// <summary>A suffixed expression seen as a statement: an assignable place or a call.</summary>
    private struct Place
    {
        public int Kind; // 0 variable, 1 indexed (Table[Key]), 2 call
        public string Name;
        public int Table;
        public int Key;
        public int CallIdx;
        public int Line;
    }

    private static bool IsSuffixStart(Tok t) => t is Tok.Dot or Tok.LBracket or Tok.Colon or Tok.LParen or Tok.String or Tok.LBrace;

    private Place ParsePlace()
    {
        int line = _tok.Line;
        if (_tok.Kind == Tok.Name)
        {
            string name = _tok.Text;
            Advance();
            if (!IsSuffixStart(_tok.Kind)) return new Place { Kind = 0, Name = name, Line = line };
            int reg = Alloc();
            LoadVariable(name, reg, line);
            return Suffixes(reg, true, line);
        }
        int r = Alloc();
        Advance(); // (
        Expr(r, 0);
        Expect(Tok.RParen, ")");
        _openIdx = -1;
        if (!IsSuffixStart(_tok.Kind)) throw Err("syntax error");
        return Suffixes(r, true, line);
    }

    private void ExprStatement()
    {
        int save = _fs.Free;
        var places = new List<Place> { ParsePlace() };
        if (places[0].Kind == 2)
        {
            if (_tok.Kind is Tok.Assign or Tok.Comma) throw Err("syntax error");
            SetOpen(places[0].CallIdx, 0);
            _fs.Free = save;
            return;
        }
        while (_tok.Kind == Tok.Comma)
        {
            Advance();
            var p = ParsePlace();
            if (p.Kind == 2) throw Err("syntax error");
            places.Add(p);
        }
        if (_tok.Kind != Tok.Assign) throw Err("syntax error");
        Advance();
        int baseReg = _fs.Free;
        var (n, open) = ExprList();
        AdjustList(places.Count, n, open, baseReg);
        for (int i = places.Count - 1; i >= 0; i--)
        {
            var p = places[i];
            if (p.Kind == 0) StoreVariable(p.Name, baseReg + i, p.Line);
            else Emit(Instruction.Abc(OpCode.SetTable, p.Table, p.Key, baseReg + i), p.Line);
        }
        _fs.Free = save;
    }

    /// <summary>
    /// Applies field, index, method-call and call suffixes to the value in register <paramref name="reg"/>.
    /// With <paramref name="place"/> the final index suffix is left unloaded so it can be assigned to.
    /// </summary>
    private Place Suffixes(int reg, bool place, int line)
    {
        while (true)
        {
            int l = _tok.Line;
            switch (_tok.Kind)
            {
                case Tok.Dot:
                case Tok.LBracket:
                {
                    int key = _fs.Free;
                    if (_tok.Kind == Tok.Dot)
                    {
                        Advance();
                        string name = ExpectName();
                        int k = Alloc();
                        Emit(Instruction.ABx(OpCode.LoadK, k, AddConst(LuaValue.FromString(name))), l);
                    }
                    else
                    {
                        Advance();
                        Expr(Alloc(), 0);
                        Expect(Tok.RBracket, "]");
                    }
                    if (place && !IsSuffixStart(_tok.Kind))
                        return new Place { Kind = 1, Table = reg, Key = key, Line = l };
                    Emit(Instruction.Abc(OpCode.GetTable, reg, reg, key), l);
                    _fs.Free = Math.Max(key, reg + 1);
                    _openIdx = -1;
                    break;
                }
                case Tok.Colon:
                {
                    Advance();
                    string name = ExpectName();
                    CallSuffix(reg, name);
                    if (place && !IsSuffixStart(_tok.Kind)) return new Place { Kind = 2, CallIdx = _openIdx, Line = l };
                    break;
                }
                case Tok.LParen:
                case Tok.String:
                case Tok.LBrace:
                    CallSuffix(reg, null);
                    if (place && !IsSuffixStart(_tok.Kind)) return new Place { Kind = 2, CallIdx = _openIdx, Line = l };
                    break;
                default:
                    return default;
            }
        }
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
        if (_tok.Kind is Tok.Comma or Tok.In)
        {
            GenericFor(name, line);
            return;
        }
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

    private void GenericFor(string first, int line)
    {
        var names = new List<string> { first };
        while (_tok.Kind == Tok.Comma)
        {
            Advance();
            names.Add(ExpectName());
        }
        Expect(Tok.In, "in");
        int a = _fs.Free;
        var (n, open) = ExprList();
        AdjustList(4, n, open, a); // iterator, state, control, closing value
        Expect(Tok.Do, "do");
        Reserve(a + 7);
        int toCall = EmitJmp(line);
        int nlocals = _fs.Locals.Count;
        int free = _fs.Free; // a + 4
        foreach (string name in names) _fs.Locals.Add(new LocalVar(name, Alloc()));
        var loop = new LoopInfo(free);
        _fs.Loops.Add(loop);
        int body = Here;
        Block();
        Expect(Tok.End, "end");
        _fs.Loops.RemoveAt(_fs.Loops.Count - 1);
        LeaveScope(nlocals, free);
        PatchJmp(toCall, Here);
        Emit(Instruction.Abc(OpCode.Move, a + 4, a, 0), line);
        Emit(Instruction.Abc(OpCode.Move, a + 5, a + 1, 0), line);
        Emit(Instruction.Abc(OpCode.Move, a + 6, a + 2, 0), line);
        Emit(Instruction.Abc(OpCode.Call, a + 4, 3, names.Count + 1), line);
        Emit(Instruction.Abc(OpCode.TForLoop, a, 0, 0), line);
        PatchJmp(EmitJmp(line), body);
        foreach (int b in loop.Breaks) PatchJmp(b, Here);
        _fs.Free = a;
    }

    // ---- functions ----

    private void FunctionBody(int target, int line, bool isMethod)
    {
        var child = new FuncState(_fs);
        _fs = child;
        if (isMethod)
        {
            child.Locals.Add(new LocalVar("self", Alloc()));
            child.NumParams++;
        }
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
            case Tok.Pipe: left = right = 4; return true;
            case Tok.Tilde: left = right = 5; return true;
            case Tok.Amp: left = right = 6; return true;
            case Tok.Shl or Tok.Shr: left = right = 7; return true;
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
        if (_tok.Kind is Tok.Minus or Tok.Not or Tok.Tilde or Tok.Hash)
        {
            OpCode unop = _tok.Kind switch { Tok.Not => OpCode.Not, Tok.Tilde => OpCode.BNot, Tok.Hash => OpCode.Len, _ => OpCode.Unm };
            Advance();
            Expr(target, UnaryPriority);
            Emit(Instruction.Abc(unop, target, target, 0), line);
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
            case Tok.Amp: Emit(Instruction.Abc(OpCode.BAnd, target, target, rhs), line); break;
            case Tok.Pipe: Emit(Instruction.Abc(OpCode.BOr, target, target, rhs), line); break;
            case Tok.Tilde: Emit(Instruction.Abc(OpCode.BXor, target, target, rhs), line); break;
            case Tok.Shl: Emit(Instruction.Abc(OpCode.Shl, target, target, rhs), line); break;
            case Tok.Shr: Emit(Instruction.Abc(OpCode.Shr, target, target, rhs), line); break;
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
                FunctionBody(target, line, false);
                return;
            case Tok.String:
                Emit(Instruction.ABx(OpCode.LoadK, target, AddConst(LuaValue.FromString(_tok.Text))), line);
                Advance();
                return;
            case Tok.LBrace:
                TableConstructor(target);
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
        Suffixes(target, false, line);
    }

    /// <summary>Compiles a call (or method call, when <paramref name="method"/> is set) whose function value is in <paramref name="target"/>.</summary>
    private void CallSuffix(int target, string? method)
    {
        int line = _tok.Line;
        int save = _fs.Free;
        int fn = target;
        if (target != _fs.Free - 1)
        {
            fn = Alloc();
            Emit(Instruction.Abc(OpCode.Move, fn, target, 0), line);
        }
        int extra = 0;
        if (method is not null)
        {
            Alloc(); // fn + 1 receives the receiver
            int k = Alloc();
            Emit(Instruction.ABx(OpCode.LoadK, k, AddConst(LuaValue.FromString(method))), line);
            Emit(Instruction.Abc(OpCode.Self, fn, fn, k), line);
            _fs.Free = fn + 2;
            extra = 1;
        }
        int b = 1 + extra;
        switch (_tok.Kind)
        {
            case Tok.String:
                Emit(Instruction.ABx(OpCode.LoadK, Alloc(), AddConst(LuaValue.FromString(_tok.Text))), line);
                Advance();
                b++;
                break;
            case Tok.LBrace:
                TableConstructor(Alloc());
                b++;
                break;
            case Tok.LParen:
            {
                Advance();
                if (_tok.Kind != Tok.RParen)
                {
                    var (n, open) = ExprList();
                    if (open >= 0)
                    {
                        SetOpen(open, -1);
                        b = 0;
                    }
                    else b += n;
                }
                Expect(Tok.RParen, ")");
                break;
            }
            default:
                throw Err("function arguments expected");
        }
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

    /// <summary>Compiles a table constructor into <paramref name="target"/>; the current token is the opening brace.</summary>
    private void TableConstructor(int target)
    {
        int line = _tok.Line;
        int save = _fs.Free;
        Advance(); // {
        int t = target;
        if (target != _fs.Free - 1) t = Alloc();
        int newIdx = Emit(Instruction.Abc(OpCode.NewTable, t, 0, 0), line);
        Emit(Instruction.Extra(0), line);
        int arraySize = 0, hashSize = 0, pending = 0, flushed = 0;
        bool openList = false;
        while (_tok.Kind != Tok.RBrace)
        {
            if (pending == 50)
            {
                FlushList(t, pending, flushed, line);
                flushed += pending;
                pending = 0;
                _fs.Free = t + 1;
            }
            int itemSave = _fs.Free;
            if (_tok.Kind == Tok.Name && Peek().Kind == Tok.Assign)
            {
                int k = Alloc();
                Emit(Instruction.ABx(OpCode.LoadK, k, AddConst(LuaValue.FromString(_tok.Text))), _tok.Line);
                Advance();
                Advance();
                int v = Alloc();
                Expr(v, 0);
                Emit(Instruction.Abc(OpCode.SetTable, t, k, v), line);
                _fs.Free = itemSave;
                hashSize++;
            }
            else if (_tok.Kind == Tok.LBracket)
            {
                Advance();
                int k = Alloc();
                Expr(k, 0);
                Expect(Tok.RBracket, "]");
                Expect(Tok.Assign, "=");
                int v = Alloc();
                Expr(v, 0);
                Emit(Instruction.Abc(OpCode.SetTable, t, k, v), line);
                _fs.Free = itemSave;
                hashSize++;
            }
            else
            {
                Expr(Alloc(), 0);
                int open = _openIdx;
                pending++;
                arraySize++;
                if (_tok.Kind is Tok.Comma or Tok.Semi)
                {
                    Advance();
                    if (_tok.Kind != Tok.RBrace) continue;
                }
                if (_tok.Kind == Tok.RBrace && open >= 0)
                {
                    SetOpen(open, -1);
                    openList = true;
                    arraySize--;
                }
                break;
            }
            if (_tok.Kind is Tok.Comma or Tok.Semi) Advance();
            else break;
        }
        Expect(Tok.RBrace, "}");
        if (pending > 0) FlushList(t, openList ? 0 : pending, flushed, line);
        _fs.Code[newIdx] = Instruction.Abc(OpCode.NewTable, t, Math.Min(hashSize, 255), 0);
        _fs.Code[newIdx + 1] = Instruction.Extra(arraySize);
        if (t != target)
        {
            Emit(Instruction.Abc(OpCode.Move, target, t, 0), line);
            _fs.Free = save;
        }
        else _fs.Free = t + 1;
        _openIdx = -1;
    }

    private void FlushList(int t, int count, int flushed, int line)
    {
        Emit(Instruction.Abc(OpCode.SetList, t, count, 0), line);
        Emit(Instruction.Extra(flushed), line);
    }
}
