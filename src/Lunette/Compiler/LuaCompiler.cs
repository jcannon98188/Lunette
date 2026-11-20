namespace Lunette.Compiler;

/// <summary>One-pass compiler: parses and emits register bytecode directly, with no AST.</summary>
internal sealed class LuaCompiler
{
    private const int MaxRegisters = 250;
    private const int UnaryPriority = 12;

    private enum VarKind : byte { Regular, Const, Close }

    private sealed class LocalVar(string name, int reg, VarKind kind)
    {
        public string Name { get; } = name;
        public int Reg { get; } = reg;
        public VarKind Kind { get; } = kind;
        public bool Captured { get; set; }
        /// <summary>Set for a <c>&lt;const&gt;</c> local whose initializer is a literal: it has no register and every use loads <see cref="Folded"/>.</summary>
        public bool IsFolded { get; set; }
        public LuaValue Folded { get; set; }
        public int StartPc { get; set; }
        public int EndPc { get; set; } = -1;
    }

    private sealed class LoopInfo(int baseReg)
    {
        public int BaseReg { get; } = baseReg;
        public List<int> Breaks { get; } = new();
    }

    private sealed class LabelInfo(string name, int pc, int line, int nactvar)
    {
        public string Name { get; } = name;
        public int Pc { get; } = pc;
        public int Line { get; } = line;
        public int NActVar { get; } = nactvar;
    }

    private sealed class GotoInfo(string name, int jmpPc, int closePc, int line, int nactvar)
    {
        public string Name { get; } = name;
        public int JmpPc { get; } = jmpPc;
        public int ClosePc { get; } = closePc;
        public int Line { get; } = line;
        /// <summary>Active locals at the goto, lowered to the enclosing block's count as blocks close.</summary>
        public int NActVar { get; set; } = nactvar;
    }

    private sealed class BlockInfo(int nactvar, int firstLabel, int firstGoto, bool insideTbc)
    {
        public int NActVar { get; } = nactvar;
        public int FirstLabel { get; } = firstLabel;
        public int FirstGoto { get; } = firstGoto;
        /// <summary>True when a to-be-closed variable is in scope: a return of a call there cannot be a tail call.</summary>
        public bool InsideTbc { get; set; } = insideTbc;
    }

    private sealed class FuncState(FuncState? parent)
    {
        public FuncState? Parent { get; } = parent;
        public int LineDefined { get; set; }
        public int LastLineDefined { get; set; }
        public List<Instruction> Code { get; } = new();
        public List<int> Lines { get; } = new();
        public List<LuaValue> Consts { get; } = new();
        public List<string> Names { get; } = new();
        public List<LocalVar> Locals { get; } = new();
        /// <summary>Every local ever declared, for debug info (variable names in error messages).</summary>
        public List<LocalVar> AllLocals { get; } = new();

        public List<LabelInfo> Labels { get; } = new();
        /// <summary>Forward gotos still waiting for their label.</summary>
        public List<GotoInfo> Gotos { get; } = new();
        public List<BlockInfo> Blocks { get; } = new();

        /// <summary>Raises the "too many ..." compile error for this function (set by the compiler).</summary>
        public Action<FuncState, string, int>? OnLimit { get; set; }

        public void AddLocal(string name, int reg, VarKind kind = VarKind.Regular)
        {
            if (Locals.Count >= MaxLocals) OnLimit?.Invoke(this, "local variables", MaxLocals);
            var v = new LocalVar(name, reg, kind) { StartPc = Code.Count };
            Locals.Add(v);
            AllLocals.Add(v);
        }

        public void DropLocals(int keep)
        {
            for (int i = keep; i < Locals.Count; i++) Locals[i].EndPc = Code.Count;
            Locals.RemoveRange(keep, Locals.Count - keep);
        }

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

    private readonly StringCache _cache;

    private LuaCompiler(byte[] source, string chunkName, StringCache cache)
    {
        _cache = cache;
        _lex = new Lexer(source, chunkName, cache);
        _chunkName = chunkName;
        _tok = _lex.Next();
    }

    public static Proto Compile(string source, string chunkName, StringCache? cache = null) =>
        Compile(LuaString.FromUtf16(source).Bytes.ToArray(), chunkName, cache);

    public static Proto Compile(byte[] source, string chunkName, StringCache? cache = null)
    {
        var c = new LuaCompiler(source, chunkName, cache ?? new StringCache());
        c._fs.IsVararg = true;
        c._fs.OnLimit = (fs, what, limit) => throw c.LimitError(fs, what, limit);
        c._fs.Upvals.Add(("_ENV", new UpvalDesc(true, 0)));
        c._fs.LastLineDefined = 0;
        c.EnterBlock();
        c.Block();
        if (c._tok.Kind != Tok.Eof) throw c.Err("<eof> expected");
        c.LeaveBlock();
        c.Emit(Instruction.Abc(OpCode.Return, 0, 1, 0), c._lastLine);
        return c.Finish(c._fs);
    }

    private Proto Finish(FuncState fs)
    {
        var upvals = new UpvalDesc[fs.Upvals.Count];
        var upNames = new string[upvals.Length];
        for (int i = 0; i < upvals.Length; i++)
        {
            upvals[i] = fs.Upvals[i].Desc;
            upNames[i] = fs.Upvals[i].Name;
        }
        var locVars = new LocVar[fs.AllLocals.Count];
        for (int i = 0; i < locVars.Length; i++)
        {
            var v = fs.AllLocals[i];
            locVars[i] = new LocVar(v.Name, v.Reg, v.StartPc, v.EndPc < 0 ? fs.Code.Count : v.EndPc);
        }
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
        UpvalNames = upNames,
        LocVars = locVars,
        Protos = fs.Protos.ToArray(),
        LineDefined = fs.LineDefined,
        LastLineDefined = fs.LastLineDefined,
        };
    }

    // ---- token helpers ----

    private int _lastLine = 1;

    private void Advance()
    {
        _lastLine = _tok.Line;
        if (_hasAhead) { _tok = _ahead; _hasAhead = false; }
        else _tok = _lex.Next();
    }

    private Token Peek()
    {
        if (!_hasAhead) { _ahead = _lex.Next(); _hasAhead = true; }
        return _ahead;
    }

    private const int MaxLocals = 200, MaxUpvalues = 255, MaxLevels = 200;
    private int _level;

    /// <summary>luaY errorlimit: "too many upvalues (limit is 255) in function at line N near 'x'".</summary>
    private LuaException LimitError(FuncState fs, string what, int limit) =>
        Err($"too many {what} (limit is {limit}) in {(fs.LineDefined == 0 ? "main function" : $"function at line {fs.LineDefined}")}");

    private void EnterLevel()
    {
        if (++_level > MaxLevels) throw LimitError(_fs, "C levels", MaxLevels);
    }

    private LuaException Err(string msg) => _lex.Error(_tok.Kind == Tok.Eof ? $"{msg} near <eof>" : $"{msg} near '{_tok.Text}'", _tok.Line);

    private void Expect(Tok kind, string what)
    {
        if (_tok.Kind != kind) throw Err($"'{what}' expected");
        Advance();
    }

    /// <summary>lparser.c check_match: closes a construct opened at <paramref name="line"/>; names the opener when it is on another line.</summary>
    private void ExpectMatch(Tok kind, string what, string who, int line)
    {
        if (_tok.Kind != kind)
        {
            if (line == _tok.Line) throw Err($"'{what}' expected");
            throw Err($"'{what}' expected (to close '{who}' at line {line})");
        }
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

    private void PatchJmp(int at, int target)
    {
        _fs.Code[at] = Instruction.JmpTo(target);
        if (target == Here) _lastTarget = target; // a jump lands after the last instruction: it can no longer be rewritten on its own
    }

    /// <summary>Highest pc a jump was patched to while it was the next instruction to emit.</summary>
    private int _lastTarget = -1;

    /// <summary>Set by <see cref="Condition"/> for the next <see cref="Expr"/> only: a lone comparison may become a fused compare-and-jump.</summary>
    private bool _wantCond;

    /// <summary>The Jmp after a fused compare-and-jump emitted for the condition just compiled, or -1.</summary>
    private int _condJump = -1;

    private enum OperandKind : byte { None, Reg, Const }

    /// <summary>The register of <paramref name="name"/> when it is a plain local of the function being compiled (not a folded constant), else -1.</summary>
    private int LocalReg(string name)
    {
        var l = FindLocal(_fs, name);
        return l is null || l.IsFolded ? -1 : l.Reg;
    }

    private static bool SameConst(LuaValue a, LuaValue b) =>
        (ReferenceEquals(a.Ref, b.Ref) && a.Bits == b.Bits) || (a.Ref is byte[] && b.Ref is byte[] && LuaValue.RawEquals(a, b));

    /// <summary>Index of <paramref name="v"/> in the first 256 constants (the ones a K operand reaches), adding it when there is room; -1 when there is none.</summary>
    private int ConstOperand(LuaValue v)
    {
        var consts = _fs.Consts;
        int n = Math.Min(consts.Count, 256);
        for (int i = 0; i < n; i++)
            if (SameConst(consts[i], v)) return i;
        return consts.Count < 256 ? AddConst(v) : -1;
    }

    /// <summary>A number constant's index, reusing an equal one (same type and bits), for the 16-bit LoadK operand.</summary>
    private int NumberConst(LuaValue v)
    {
        var consts = _fs.Consts;
        for (int i = 0; i < consts.Count; i++)
            if (SameConst(consts[i], v)) return i;
        return AddConst(v);
    }

    /// <summary>
    /// When the current token starts a complete operand at binding <paramref name="limit"/> that needs no code (a local variable's register,
    /// or a number literal or folded constant; with <paramref name="anyConst"/> also nil, booleans and strings), consumes it and says which.
    /// </summary>
    private bool TryOperand(int limit, bool constOk, bool anyConst, out OperandKind kind, out int index)
    {
        kind = OperandKind.None;
        index = 0;
        LuaValue lit;
        switch (_tok.Kind)
        {
            case Tok.Name:
            {
                string name = _tok.Text;
                if (FoldedValue(name) is { } folded)
                {
                    if (!constOk || !(folded.IsNumber || anyConst)) return false;
                    lit = folded;
                    break;
                }
                int reg = LocalReg(name);
                if (reg < 0 || !OperandEnds(limit)) return false;
                Advance();
                kind = OperandKind.Reg;
                index = reg;
                return true;
            }
            case Tok.Integer when constOk: lit = LuaValue.FromInteger(_tok.Int); break;
            case Tok.Float when constOk: lit = LuaValue.FromFloat(_tok.Float); break;
            case Tok.String when constOk && anyConst: lit = LuaValue.FromString(_tok.Str); break;
            case Tok.Nil when constOk && anyConst: lit = default; break;
            case Tok.True when constOk && anyConst: lit = LuaValue.FromBoolean(true); break;
            case Tok.False when constOk && anyConst: lit = LuaValue.FromBoolean(false); break;
            default: return false;
        }
        if (!OperandEnds(limit)) return false;
        int k = ConstOperand(lit);
        if (k < 0) return false;
        Advance();
        kind = OperandKind.Const;
        index = k;
        return true;
    }

    /// <summary>True when the token after the current one neither starts a suffix of it nor a binary operator that binds tighter than <paramref name="limit"/>.</summary>
    private bool OperandEnds(int limit)
    {
        Tok next = Peek().Kind;
        return !IsSuffixStart(next) && !(BinaryPriority(next, out int l, out _) && l > limit);
    }

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

    private LuaValue NameConst(string n) => LuaValue.FromString(_cache.FromUtf16(n));

    private int AddConst(LuaValue v)
    {
        _fs.Consts.Add(v);
        return _fs.Consts.Count - 1;
    }

    /// <summary>Loads a constant into a register: LoadK reaches 65536 constants, LoadKX (with an Extra word) any number.</summary>
    private void EmitLoadK(int reg, int index, int line)
    {
        if (index <= 0xFFFF)
        {
            Emit(Instruction.ABx(OpCode.LoadK, reg, index), line);
            return;
        }
        Emit(Instruction.Abc(OpCode.LoadKX, reg, 0, 0), line);
        Emit(Instruction.Extra(index), line);
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

    private int FindUpval(FuncState fs, string name)
    {
        for (int i = 0; i < fs.Upvals.Count; i++)
            if (fs.Upvals[i].Name == name) return i;
        if (fs.Parent is null) return -1;
        var local = FindLocal(fs.Parent, name);
        if (local is not null)
        {
            local.Captured = true;
            fs.Upvals.Add((name, new UpvalDesc(true, local.Reg)));
            if (fs.Upvals.Count > MaxUpvalues) throw LimitError(fs, "upvalues", MaxUpvalues);
            return fs.Upvals.Count - 1;
        }
        int outer = FindUpval(fs.Parent, name);
        if (outer < 0) return -1;
        fs.Upvals.Add((name, new UpvalDesc(false, outer)));
        if (fs.Upvals.Count > MaxUpvalues) throw LimitError(fs, "upvalues", MaxUpvalues);
        return fs.Upvals.Count - 1;
    }

    /// <summary>The literal a folded <c>&lt;const&gt;</c> local stands for, found through enclosing functions; null when the name is not one.</summary>
    private LuaValue? FoldedValue(string name)
    {
        for (var fs = _fs; fs is not null; fs = fs.Parent)
        {
            if (FindLocal(fs, name) is not { } v) continue;
            if (v.IsFolded) return v.Folded;
            return default(LuaValue?);
        }
        return default(LuaValue?);
    }

    private void EmitFolded(LuaValue v, int target, int line)
    {
        if (v.IsNil) Emit(Instruction.Abc(OpCode.LoadNil, target, 0, 0), line);
        else if (v.IsBoolean) Emit(Instruction.Abc(OpCode.LoadBool, target, v.IsFalsy ? 0 : 1, 0), line);
        else EmitLoadK(target, AddConst(v), line);
    }

    private void LoadVariable(string name, int target, int line)
    {
        if (FoldedValue(name) is { } folded) { EmitFolded(folded, target, line); return; }
        var local = FindLocal(_fs, name);
        if (local is not null) { Emit(Instruction.Abc(OpCode.Move, target, local.Reg, 0), line); return; }
        int up = FindUpval(_fs, name);
        if (up >= 0) Emit(Instruction.Abc(OpCode.GetUpval, target, up, 0), line);
        else LoadGlobal(name, target, line);
    }

    private int StringConst(string name)
    {
        LuaValue v = NameConst(name);
        for (int i = 0; i < _fs.Consts.Count; i++)
            if (LuaValue.RawEquals(_fs.Consts[i], v)) return i;
        return AddConst(v);
    }

    /// <summary>A global name is a field of the <c>_ENV</c> variable in scope (a local or an upvalue).</summary>
    private void LoadGlobal(string name, int target, int line)
    {
        int k = StringConst(name);
        var env = FindLocal(_fs, "_ENV");
        if (env is null)
        {
            int up = FindUpval(_fs, "_ENV");
            if (k <= 255)
            {
                Emit(Instruction.Abc(OpCode.GetGlobal, target, up, k), line);
                return;
            }
            int save = _fs.Free;
            int envReg = Alloc();
            int keyReg = Alloc();
            Emit(Instruction.Abc(OpCode.GetUpval, envReg, up, 0), line);
            EmitLoadK(keyReg, k, line);
            Emit(Instruction.Abc(OpCode.GetTable, target, envReg, keyReg), line);
            _fs.Free = save;
            return;
        }
        int save2 = _fs.Free;
        int envR = env.Reg;
        if (env.IsFolded) { envR = Alloc(); EmitFolded(env.Folded, envR, line); }
        if (k <= 255)
        {
            Emit(Instruction.Abc(OpCode.GetField, target, envR, k), line);
            _fs.Free = save2;
            return;
        }
        int key = Alloc();
        EmitLoadK(key, k, line);
        Emit(Instruction.Abc(OpCode.GetTable, target, envR, key), line);
        _fs.Free = save2;
    }

    private void StoreGlobal(string name, int source, int line)
    {
        int k = StringConst(name);
        var env = FindLocal(_fs, "_ENV");
        if (env is null)
        {
            int up = FindUpval(_fs, "_ENV");
            if (k <= 255)
            {
                Emit(Instruction.Abc(OpCode.SetGlobal, source, up, k), line);
                return;
            }
            int save = _fs.Free;
            int envReg = Alloc();
            int keyReg = Alloc();
            Emit(Instruction.Abc(OpCode.GetUpval, envReg, up, 0), line);
            EmitLoadK(keyReg, k, line);
            Emit(Instruction.Abc(OpCode.SetTable, envReg, keyReg, source), line);
            _fs.Free = save;
            return;
        }
        int save2 = _fs.Free;
        int envR = env.Reg;
        if (env.IsFolded) { envR = Alloc(); EmitFolded(env.Folded, envR, line); }
        if (k <= 255)
        {
            Emit(Instruction.Abc(OpCode.SetField, envR, k, source), line);
            _fs.Free = save2;
            return;
        }
        int key = Alloc();
        EmitLoadK(key, k, line);
        Emit(Instruction.Abc(OpCode.SetTable, envR, key, source), line);
        _fs.Free = save2;
    }

    /// <summary>Kind of the variable <paramref name="name"/> resolves to from the current function, or Regular for a global.</summary>
    private VarKind KindOf(string name)
    {
        for (var fs = _fs; fs is not null; fs = fs.Parent)
            if (FindLocal(fs, name) is { } v) return v.Kind;
        return VarKind.Regular;
    }

    private void StoreVariable(string name, int source, int line)
    {
        if (KindOf(name) != VarKind.Regular)
            throw _lex.Error($"attempt to assign to const variable '{name}'", line);
        var local = FindLocal(_fs, name);
        if (local is not null) { Emit(Instruction.Abc(OpCode.Move, local.Reg, source, 0), line); return; }
        int up = FindUpval(_fs, name);
        if (up >= 0) Emit(Instruction.Abc(OpCode.SetUpval, source, up, 0), line);
        else StoreGlobal(name, source, line);
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
        EnterBlock();
        Block();
        LeaveBlock();
        LeaveScope(nlocals, free);
    }

    // ---- blocks, labels and gotos ----

    private void EnterBlock()
    {
        bool tbc = _fs.Blocks.Count > 0 && _fs.Blocks[^1].InsideTbc;
        _fs.Blocks.Add(new BlockInfo(_fs.Locals.Count, _fs.Labels.Count, _fs.Gotos.Count, tbc));
    }

    /// <summary>Ends the innermost block: its labels go out of scope and its pending gotos move out to the enclosing block.</summary>
    private void LeaveBlock()
    {
        var bl = _fs.Blocks[^1];
        _fs.Blocks.RemoveAt(_fs.Blocks.Count - 1);
        _fs.Labels.RemoveRange(bl.FirstLabel, _fs.Labels.Count - bl.FirstLabel);
        for (int i = bl.FirstGoto; i < _fs.Gotos.Count; i++)
            if (_fs.Gotos[i].NActVar > bl.NActVar) _fs.Gotos[i].NActVar = bl.NActVar;
        if (_fs.Blocks.Count == 0 && _fs.Gotos.Count > 0)
        {
            var g = _fs.Gotos[0];
            throw _lex.Error($"no visible label '{g.Name}' for <goto> at line {g.Line}", _tok.Line);
        }
    }

    /// <summary>First register above the first <paramref name="nactvar"/> active locals.</summary>
    private int RegLevel(int nactvar)
    {
        for (int i = nactvar - 1; i >= 0; i--)
            if (!_fs.Locals[i].IsFolded) return _fs.Locals[i].Reg + 1;
        return 0;
    }

    private void GotoStatement()
    {
        int line = _tok.Line;
        Advance();
        string name = ExpectName();
        foreach (var lb in _fs.Labels)
        {
            if (lb.Name != name) continue;
            // A backward jump leaves the scope of every local declared since the label.
            if (_fs.Locals.Count > lb.NActVar) Emit(Instruction.Abc(OpCode.Close, RegLevel(lb.NActVar), 0, 0), line);
            PatchJmp(EmitJmp(line), lb.Pc);
            return;
        }
        int closePc = Emit(Instruction.Abc(OpCode.Close, 0, 0, 0), line);
        int jmp = EmitJmp(line);
        _fs.Gotos.Add(new GotoInfo(name, jmp, closePc, line, _fs.Locals.Count));
    }

    private void LabelStatement()
    {
        int line = _tok.Line;
        Advance();
        string name = ExpectName();
        Expect(Tok.DColon, "::");
        foreach (var lb in _fs.Labels)
            if (lb.Name == name)
                throw _lex.Error($"label '{name}' already defined on line {lb.Line}", line);
        // Void statements after a label at the end of a block leave the label outside the scope of the block's locals.
        while (_tok.Kind is Tok.Semi or Tok.DColon) Statement();
        foreach (var lb in _fs.Labels)
            if (lb.Name == name)
                throw _lex.Error($"label '{name}' already defined on line {lb.Line}", line);
        var bl = _fs.Blocks[^1];
        bool last = _tok.Kind is Tok.Eof or Tok.End or Tok.Else or Tok.Elseif;
        var label = new LabelInfo(name, Here, line, last ? bl.NActVar : _fs.Locals.Count);
        _fs.Labels.Add(label);
        for (int i = bl.FirstGoto; i < _fs.Gotos.Count;)
        {
            var g = _fs.Gotos[i];
            if (g.Name != name) { i++; continue; }
            if (g.NActVar < label.NActVar)
                throw _lex.Error($"<goto {name}> at line {g.Line} jumps into the scope of local '{_fs.Locals[g.NActVar].Name}'", g.Line);
            PatchJmp(g.JmpPc, label.Pc);
            _fs.Code[g.ClosePc] = Instruction.Abc(OpCode.Close, RegLevel(label.NActVar), 0, 0);
            _fs.Gotos.RemoveAt(i);
        }
    }

    /// <summary>Drops the locals declared since <paramref name="nlocals"/>, closing their upvalues if any were captured.</summary>
    private void LeaveScope(int nlocals, int free)
    {
        bool captured = false;
        for (int i = nlocals; i < _fs.Locals.Count; i++) captured |= _fs.Locals[i].Captured || _fs.Locals[i].Kind == VarKind.Close;
        _fs.DropLocals(nlocals);
        if (captured) Emit(Instruction.Abc(OpCode.Close, free, 0, 0), _tok.Line);
        _fs.Free = free;
    }

    private void Statement()
    {
        EnterLevel();
        StatementCore();
        _level--;
    }

    private void StatementCore()
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
                int doLine = _tok.Line;
                Advance();
                ScopedBlock();
                ExpectMatch(Tok.End, "end", "do", doLine);
                return;
            }
            case Tok.Break:
                BreakStatement();
                return;
            case Tok.Goto:
                GotoStatement();
                return;
            case Tok.DColon:
                LabelStatement();
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
        if (_fs.Loops.Count == 0) throw _lex.Error($"break outside loop at line {line}", line);
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
            if (n == 1 && open >= 0 && _fs.Code[open].Op == OpCode.Call && !_fs.Blocks[^1].InsideTbc)
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
        _fs.Free = first;
    }

    private void LocalFunction()
    {
        int line = _tok.Line;
        Advance();
        string name = ExpectName();
        int reg = Alloc();
        _fs.AddLocal(name, reg);
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
                    int fk = ConstOperand(NameConst(field));
                    if (fk >= 0) Emit(Instruction.Abc(OpCode.GetField, obj, obj, fk), line);
                    else
                    {
                        int kk = Alloc();
                        EmitLoadK(kk, StringConst(field), line);
                        Emit(Instruction.Abc(OpCode.GetTable, obj, obj, kk), line);
                    }
                    _fs.Free = obj + 1;
                }
                isMethod = _tok.Kind == Tok.Colon;
                Advance();
                field = ExpectName();
                if (isMethod) break;
            }
            int sk = ConstOperand(NameConst(field));
            int key = -1;
            if (sk < 0)
            {
                key = Alloc();
                EmitLoadK(key, StringConst(field), line);
            }
            int fnReg = Alloc();
            FunctionBody(fnReg, line, isMethod);
            if (sk >= 0) Emit(Instruction.Abc(OpCode.SetField, obj, sk, fnReg), line);
            else Emit(Instruction.Abc(OpCode.SetTable, obj, key, fnReg), line);
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
        int line = _tok.Line;
        var names = new List<string>();
        var kinds = new List<VarKind>();
        bool hasClose = false;
        do
        {
            if (names.Count > 0) Advance();
            names.Add(ExpectName());
            if (_fs.Locals.Count + names.Count > MaxLocals) throw LimitError(_fs, "local variables", MaxLocals);
            var kind = VarKind.Regular;
            if (_tok.Kind == Tok.Lt)
            {
                Advance();
                string attr = ExpectName();
                Expect(Tok.Gt, ">");
                if (attr == "const") kind = VarKind.Const;
                else if (attr == "close")
                {
                    if (hasClose) throw _lex.Error("multiple to-be-closed variables in local list", line);
                    hasClose = true;
                    kind = VarKind.Close;
                }
                else throw _lex.Error($"unknown attribute '{attr}'", line);
            }
            kinds.Add(kind);
        } while (_tok.Kind == Tok.Comma);
        int baseReg = _fs.Free;
        int startPc = Here;
        int n = 0, open = -1;
        if (_tok.Kind == Tok.Assign)
        {
            Advance();
            (n, open) = ExprList();
        }
        if (names.Count == 1 && n == 1 && kinds[0] == VarKind.Const && TryTakeLiteral(startPc, baseReg) is { } literal)
        {
            _fs.Free = baseReg;
            _fs.Locals.Add(new LocalVar(names[0], baseReg, VarKind.Const) { IsFolded = true, Folded = literal, StartPc = Here });
            return;
        }
        AdjustList(names.Count, n, open, baseReg);
        for (int i = 0; i < names.Count; i++)
        {
            _fs.AddLocal(names[i], baseReg + i, kinds[i]);
            if (kinds[i] != VarKind.Close) continue;
            Emit(Instruction.ABx(OpCode.Tbc, baseReg + i, AddName(names[i])), line);
            _fs.Blocks[^1].InsideTbc = true;
        }
    }

    /// <summary>
    /// When the code emitted since <paramref name="startPc"/> only loads one literal (nil, a boolean, a number, a string, or a negated number)
    /// into <paramref name="reg"/>, removes that code and returns the literal; otherwise leaves the code alone and returns null.
    /// </summary>
    private LuaValue? TryTakeLiteral(int startPc, int reg)
    {
        var code = _fs.Code;
        int count = code.Count - startPc;
        if (count is < 1 or > 2 || code[startPc].A != reg) return null;
        var first = code[startPc];
        LuaValue value;
        switch (first.Op)
        {
            case OpCode.LoadNil when first.B == 0: value = default; break;
            case OpCode.LoadBool: value = LuaValue.FromBoolean(first.B != 0); break;
            case OpCode.LoadK: value = _fs.Consts[first.Bx]; break;
            default: return null;
        }
        if (count == 2)
        {
            var second = code[startPc + 1];
            if (second.Op != OpCode.Unm || second.A != reg || second.B != reg || !value.IsNumber) return null;
            value = value.IsInteger ? LuaValue.FromInteger(unchecked(-value.AsInteger)) : LuaValue.FromFloat(-value.AsFloat);
        }
        code.RemoveRange(startPc, count);
        _fs.Lines.RemoveRange(startPc, count);
        return value;
    }

    /// <summary>A suffixed expression seen as a statement: an assignable place or a call.</summary>
    private struct Place
    {
        public int Kind; // 0 variable, 1 indexed (Table[Key]), 2 call
        public string Name;
        public int Table;
        public int Key;
        public bool KeyIsK; // Key is a constant index (SetField), not a register
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
            int local = LocalReg(name);
            if (local >= 0 && _tok.Kind is Tok.Dot or Tok.LBracket or Tok.Colon) return Suffixes(reg, true, line, local);
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
            if (places.Count + _level > MaxLevels) throw LimitError(_fs, "C levels", MaxLevels);
        }
        if (_tok.Kind != Tok.Assign) throw Err("syntax error");
        Advance();
        if (places.Count > 1) ResolveConflicts(places);
        int baseReg = _fs.Free;
        int exprStart = Here;
        var (n, open) = ExprList();
        AdjustList(places.Count, n, open, baseReg);
        int storeLine = _lastLine; // PUC stores after reading the whole right-hand side
        for (int i = places.Count - 1; i >= 0; i--)
        {
            var p = places[i];
            if (p.Kind == 0 && places.Count == 1 && n == 1 && open < 0 && TryRetargetLocal(p.Name, exprStart, baseReg)) continue;
            if (p.Kind == 0) StoreVariable(p.Name, baseReg + i, storeLine);
            else Emit(Instruction.Abc(p.KeyIsK ? OpCode.SetField : OpCode.SetTable, p.Table, p.Key, baseReg + i), storeLine);
        }
        _fs.Free = save;
    }

    /// <summary>
    /// In a multiple assignment a table or key that is a local read in place must not see another target's store (PUC's check_conflict):
    /// when a target assigns that local, the indexed target uses a copy made before the right-hand side runs.
    /// </summary>
    private void ResolveConflicts(List<Place> places)
    {
        for (int v = 0; v < places.Count; v++)
        {
            if (places[v].Kind != 0 || FindLocal(_fs, places[v].Name) is not { IsFolded: false } local) continue;
            for (int i = 0; i < places.Count; i++)
            {
                var p = places[i];
                if (p.Kind != 1 || (p.Table != local.Reg && (p.KeyIsK || p.Key != local.Reg))) continue;
                int tmp = Alloc();
                Emit(Instruction.Abc(OpCode.Move, tmp, local.Reg, 0), _lastLine);
                if (p.Table == local.Reg) p.Table = tmp;
                if (!p.KeyIsK && p.Key == local.Reg) p.Key = tmp;
                places[i] = p;
            }
        }
    }

    private static bool Retargetable(OpCode op) =>
        op is OpCode.Move or OpCode.LoadK or OpCode.LoadBool or OpCode.GetGlobal or OpCode.GetUpval or OpCode.Unm or OpCode.Not
            or OpCode.BNot or OpCode.Len or OpCode.Concat or OpCode.GetTable or OpCode.GetField or OpCode.Eq or OpCode.Lt or OpCode.Le
        || Lunette.Vm.Meta.IsBinaryArith(op);

    /// <summary>
    /// For <c>x = expr</c> with a local <c>x</c>: when the last instruction of <c>expr</c> (emitted at or after <paramref name="startPc"/>) leaves its value
    /// in temporary <paramref name="source"/> and no jump lands after it, makes it write <c>x</c> directly instead of emitting a Move.
    /// </summary>
    private bool TryRetargetLocal(string name, int startPc, int source)
    {
        if (KindOf(name) != VarKind.Regular || FindLocal(_fs, name) is not { IsFolded: false } local) return false;
        int last = Here - 1;
        if (last < startPc || _lastTarget == Here) return false;
        var ins = _fs.Code[last];
        if (ins.A != source || !Retargetable(ins.Op)) return false;
        _fs.Code[last] = Instruction.Abc(ins.Op, local.Reg, ins.B, ins.C);
        return true;
    }

    /// <summary>
    /// Applies field, index, method-call and call suffixes to the value in register <paramref name="reg"/>.
    /// With <paramref name="place"/> the final index suffix is left unloaded so it can be assigned to.
    /// </summary>
    private Place Suffixes(int reg, bool place, int line, int src = -1)
    {
        while (true)
        {
            int l = _tok.Line;
            switch (_tok.Kind)
            {
                case Tok.Dot:
                case Tok.LBracket:
                {
                    int tbl = src >= 0 ? src : reg; // src: the table is a local, read where it lives
                    src = -1;
                    int key = _fs.Free;
                    int keyReg = key;
                    int kc = -1; // constant index of a string key reachable by a K operand
                    if (_tok.Kind == Tok.Dot)
                    {
                        Advance();
                        string name = ExpectName();
                        kc = ConstOperand(NameConst(name));
                        if (kc < 0) EmitLoadK(Alloc(), StringConst(name), l);
                    }
                    else
                    {
                        Advance();
                        if (_tok.Kind == Tok.String && Peek().Kind == Tok.RBracket && (kc = ConstOperand(LuaValue.FromString(_tok.Str))) >= 0)
                        {
                            Advance();
                        }
                        else
                        {
                            kc = -1;
                            if (TryOperand(0, false, false, out var kind, out int idx) && kind == OperandKind.Reg) keyReg = idx;
                            else Expr(Alloc(), 0);
                        }
                        Expect(Tok.RBracket, "]");
                    }
                    if (place && !IsSuffixStart(_tok.Kind))
                        return new Place { Kind = 1, Table = tbl, Key = kc >= 0 ? kc : keyReg, KeyIsK = kc >= 0, Line = l };
                    Emit(kc >= 0 ? Instruction.Abc(OpCode.GetField, reg, tbl, kc) : Instruction.Abc(OpCode.GetTable, reg, tbl, keyReg), l);
                    _fs.Free = Math.Max(key, reg + 1);
                    _openIdx = -1;
                    break;
                }
                case Tok.Colon:
                {
                    Advance();
                    string name = ExpectName();
                    CallSuffix(reg, name, src);
                    src = -1;
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
        int ifLine = _tok.Line;
        while (true)
        {
            Advance(); // if / elseif
            int skip = Condition(Tok.Then, "then");
            ScopedBlock();
            if (_tok.Kind is Tok.Elseif or Tok.Else) endJumps.Add(EmitJmp(_lastLine));
            PatchJmp(skip, Here);
            if (_tok.Kind == Tok.Elseif) continue;
            if (_tok.Kind == Tok.Else)
            {
                Advance();
                ScopedBlock();
            }
            break;
        }
        ExpectMatch(Tok.End, "end", "if", ifLine);
        foreach (int j in endJumps) PatchJmp(j, Here);
    }

    /// <summary>Compiles a condition and emits a jump taken when it is falsy; returns the jump to patch.</summary>
    private int Condition(Tok follow = Tok.Eof, string followText = "")
    {
        int save = _fs.Free;
        int t = Alloc();
        _wantCond = true;
        Expr(t, 0);
        int fused = _condJump;
        _condJump = -1;
        if (follow != Tok.Eof) Expect(follow, followText);
        if (fused >= 0)
        {
            _fs.Free = save;
            return fused;
        }
        int line = _lastLine; // PUC emits the test after the last token it consumed
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
        int exit = Condition();
        Expect(Tok.Do, "do");
        var loop = new LoopInfo(_fs.Free);
        _fs.Loops.Add(loop);
        ScopedBlock();
        PatchJmp(EmitJmp(_lastLine), top);
        ExpectMatch(Tok.End, "end", "while", line);
        _fs.Loops.RemoveAt(_fs.Loops.Count - 1);
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
        EnterBlock();
        Block();
        ExpectMatch(Tok.Until, "until", "repeat", line);
        int t = Alloc();
        _wantCond = true;
        Expr(t, 0);
        int fused = _condJump;
        _condJump = -1;
        int condLine = _lastLine;
        bool captured = false;
        for (int i = nlocals; i < _fs.Locals.Count; i++) captured |= _fs.Locals[i].Captured || _fs.Locals[i].Kind == VarKind.Close;
        if (fused >= 0)
        {
            // the compare and its jump go after the Close that releases the body's captured locals
            if (captured)
            {
                var cmp = _fs.Code[fused - 1];
                int cmpLine = _fs.Lines[fused - 1];
                _fs.Code.RemoveRange(fused - 1, 2);
                _fs.Lines.RemoveRange(fused - 1, 2);
                Emit(Instruction.Abc(OpCode.Close, free, 0, 0), condLine);
                Emit(cmp, cmpLine);
                fused = EmitJmp(cmpLine);
            }
            PatchJmp(fused, top);
        }
        else
        {
            if (captured) Emit(Instruction.Abc(OpCode.Close, free, 0, 0), condLine);
            Emit(Instruction.Abc(OpCode.Test, t, 0, 0), condLine);
            PatchJmp(EmitJmp(condLine), top);
        }
        LeaveBlock();
        _fs.DropLocals(nlocals);
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
        else EmitLoadK(stepReg, NumberConst(LuaValue.FromInteger(1)), line);
        Expect(Tok.Do, "do");
        Emit(Instruction.Abc(OpCode.ForPrep, a, 0, 0), line);
        int exit = EmitJmp(line);

        int nlocals = _fs.Locals.Count;
        for (int h = 0; h < 3; h++) _fs.AddLocal("(for state)", a + h); // visible to debug.getlocal like PUC
        int free = _fs.Free; // a + 3
        _fs.AddLocal(name, Alloc());
        var loop = new LoopInfo(free);
        _fs.Loops.Add(loop);
        int body = Here;
        EnterBlock();
        Block();
        LeaveBlock();
        ExpectMatch(Tok.End, "end", "for", line);
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
        int inLine = _tok.Line; // the iterator call is attributed to the line where the explist starts, as in PUC
        int a = _fs.Free;
        var (n, open) = ExprList();
        AdjustList(4, n, open, a); // iterator, state, control, closing value
        Expect(Tok.Do, "do");
        Reserve(a + 7);
        // The fourth value is a to-be-closed variable: closed on normal exit, break, goto, return and error.
        EnterBlock();
        _fs.Blocks[^1].InsideTbc = true;
        Emit(Instruction.ABx(OpCode.Tbc, a + 3, AddName("(for state)")), line);
        int toCall = EmitJmp(line);
        int nlocals = _fs.Locals.Count - 0;
        for (int h = 0; h < 4; h++) _fs.AddLocal("(for state)", a + h);
        int free = _fs.Free; // a + 4
        foreach (string name in names) _fs.AddLocal(name, Alloc());
        var loop = new LoopInfo(a + 3);
        _fs.Loops.Add(loop);
        int body = Here;
        EnterBlock();
        Block();
        LeaveBlock();
        ExpectMatch(Tok.End, "end", "for", line);
        LeaveBlock();
        _fs.Loops.RemoveAt(_fs.Loops.Count - 1);
        LeaveScope(nlocals, free);
        PatchJmp(toCall, Here);
        Emit(Instruction.Abc(OpCode.TForMove, a + 4, a, 0), inLine);
        Emit(Instruction.Abc(OpCode.Move, a + 5, a + 1, 0), inLine);
        Emit(Instruction.Abc(OpCode.Move, a + 6, a + 2, 0), inLine);
        Emit(Instruction.Abc(OpCode.Call, a + 4, 3, names.Count + 1), inLine);
        Emit(Instruction.Abc(OpCode.TForLoop, a, 0, 0), inLine);
        PatchJmp(EmitJmp(inLine), body);
        Emit(Instruction.Abc(OpCode.Close, a + 3, 0, 0), line);
        foreach (int b in loop.Breaks) PatchJmp(b, Here);
        _fs.Free = a;
    }

    // ---- functions ----

    private void FunctionBody(int target, int line, bool isMethod)
    {
        var child = new FuncState(_fs) { LineDefined = line };
        child.OnLimit = (fs, what, limit) => throw LimitError(fs, what, limit);
        _fs = child;
        if (isMethod)
        {
            child.AddLocal("self", Alloc());
            child.NumParams++;
        }
        Expect(Tok.LParen, "(");
        if (_tok.Kind != Tok.RParen)
        {
            while (true)
            {
                if (_tok.Kind == Tok.Ellipsis)
                {
                    Advance();
                    child.IsVararg = true;
                    break;
                }
                string p = ExpectName();
                child.AddLocal(p, Alloc());
                child.NumParams++;
                if (_tok.Kind != Tok.Comma) break;
                Advance();
            }
        }
        Expect(Tok.RParen, ")");
        EnterBlock();
        Block();
        int endLine = _tok.Line;
        child.LastLineDefined = endLine;
        ExpectMatch(Tok.End, "end", "function", line);
        LeaveBlock();
        Emit(Instruction.Abc(OpCode.Return, 0, 1, 0), endLine);
        _fs = child.Parent!;
        _fs.Protos.Add(Finish(child));
        Emit(Instruction.ABx(OpCode.Closure, target, _fs.Protos.Count - 1), endLine);
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
        else if (extra > 0) Emit(Instruction.Abc(OpCode.LoadNil, baseReg + n, extra - 1, 0), _lastLine);
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
            case Tok.Concat: left = 9; right = 8; return true;
            case Tok.Plus or Tok.Minus: left = right = 10; return true;
            case Tok.Star or Tok.Slash or Tok.DSlash or Tok.Percent: left = right = 11; return true;
            case Tok.Caret: left = 14; right = 13; return true;
            default: left = right = 0; return false;
        }
    }

    /// <summary>Compiles an expression binding tighter than <paramref name="limit"/> into register <paramref name="target"/>.</summary>
    private void Expr(int target, int limit)
    {
        EnterLevel();
        ExprCore(target, limit);
        _level--;
    }

    private void ExprCore(int target, int limit)
    {
        bool wantCond = _wantCond;
        _wantCond = false;
        _condJump = -1;
        _openIdx = -1;
        int line = _tok.Line;
        int leftStart = Here;
        int leftReg = -1; // a local variable used as the left operand in place, with no code emitted for it
        if (_tok.Kind is Tok.Minus or Tok.Not or Tok.Tilde or Tok.Hash)
        {
            OpCode unop = _tok.Kind switch { Tok.Not => OpCode.Not, Tok.Tilde => OpCode.BNot, Tok.Hash => OpCode.Len, _ => OpCode.Unm };
            Advance();
            if (unop == OpCode.Unm && _tok.Kind is Tok.Integer or Tok.Float && Peek().Kind != Tok.Caret)
            {
                // a negated number literal is one constant
                LuaValue neg = _tok.Kind == Tok.Integer ? LuaValue.FromInteger(unchecked(-_tok.Int)) : LuaValue.FromFloat(-_tok.Float);
                EmitLoadK(target, NumberConst(neg), line);
                Advance();
            }
            else
            {
                Expr(target, UnaryPriority);
                Emit(Instruction.Abc(unop, target, target, 0), line);
            }
            _openIdx = -1;
        }
        else if (_tok.Kind == Tok.Name && InPlaceLeft(limit, out leftReg)) Advance();
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
                bool compare = op is Tok.Eq or Tok.Ne or Tok.Lt or Tok.Le or Tok.Gt or Tok.Ge;
                bool arith = op is Tok.Plus or Tok.Minus or Tok.Star or Tok.Slash or Tok.DSlash or Tok.Percent or Tok.Caret;
                int lsrc = leftReg >= 0 ? leftReg : target;
                if (leftReg < 0) RelabelPendingLeft(leftStart, opLine);
                leftReg = -1;
                int save = _fs.Free;
                var kind = OperandKind.None;
                int idx = 0;
                if (op != Tok.Concat)
                {
                    // a constant right operand needs an instruction form that takes one: arithmetic, or a compare fused with its jump
                    bool constOk = arith || (compare && wantCond && !(BinaryPriority(Peek().Kind, out int pl, out _) && pl > limit));
                    TryOperand(right, constOk, op is Tok.Eq or Tok.Ne, out kind, out idx);
                }
                if (kind == OperandKind.None)
                {
                    idx = Alloc();
                    Expr(idx, right);
                    kind = OperandKind.Reg;
                }
                bool last = !(BinaryPriority(_tok.Kind, out int nextLeft, out _) && nextLeft > limit);
                if (compare && wantCond && last) EmitFusedCompare(op, lsrc, kind, idx, _lastLine);
                else
                {
                    if (kind == OperandKind.Const && !arith) throw new InvalidOperationException("constant operand without a K form");
                    EmitBinary(op, target, lsrc, kind, idx, opLine);
                }
                _fs.Free = save;
            }
            _openIdx = -1;
        }
    }

    /// <summary>True when the current token names a local variable that is the left operand of a binary operator with a register form that binds tighter than <paramref name="limit"/>.</summary>
    private bool InPlaceLeft(int limit, out int reg)
    {
        reg = -1;
        int local = LocalReg(_tok.Text);
        if (local < 0) return false;
        Tok next = Peek().Kind;
        if (next is Tok.And or Tok.Or or Tok.Concat || !BinaryPriority(next, out int l, out _) || l <= limit) return false;
        reg = local;
        return true;
    }

    /// <summary>Emits a comparison fused with the jump taken when it is false (a condition's skip jump); <see cref="_condJump"/> is that jump.</summary>
    private void EmitFusedCompare(Tok op, int lsrc, OperandKind kind, int idx, int line)
    {
        int a = op == Tok.Ne ? 1 : 0;
        Instruction ins;
        if (kind == OperandKind.Const)
        {
            OpCode k = op switch { Tok.Eq or Tok.Ne => OpCode.JEqK, Tok.Lt => OpCode.JLtK, Tok.Le => OpCode.JLeK, Tok.Gt => OpCode.JGtK, _ => OpCode.JGeK };
            ins = Instruction.Abc(k, a, lsrc, idx);
        }
        else
        {
            ins = op switch
            {
                Tok.Eq or Tok.Ne => Instruction.Abc(OpCode.JEq, a, lsrc, idx),
                Tok.Lt => Instruction.Abc(OpCode.JLt, a, lsrc, idx),
                Tok.Le => Instruction.Abc(OpCode.JLe, a, lsrc, idx),
                Tok.Gt => Instruction.Abc(OpCode.JLt, a, idx, lsrc),
                _ => Instruction.Abc(OpCode.JLe, a, idx, lsrc),
            };
        }
        Emit(ins, line);
        _condJump = EmitJmp(line);
    }

    /// <summary>
    /// PUC discharges a pending variable read (global, upvalue, index) of a left operand only once the operator is read,
    /// so its loads carry the operator line (what line hooks and active lines show).
    /// </summary>
    private void RelabelPendingLeft(int start, int opLine)
    {
        if (Here <= start || _fs.Code[Here - 1].Op is not (OpCode.GetGlobal or OpCode.GetUpval or OpCode.GetTable or OpCode.GetField)) return;
        for (int pc = start; pc < Here; pc++)
            if (_fs.Code[pc].Op is not (OpCode.Move or OpCode.LoadK or OpCode.GetGlobal or OpCode.GetUpval or OpCode.GetTable or OpCode.GetField)) return;
        for (int pc = start; pc < Here; pc++) _fs.Lines[pc] = opLine;
    }

    /// <summary>Emits <c>target = lsrc op rhs</c>, where the right operand is register <paramref name="rhs"/> or, for arithmetic with <paramref name="kind"/> Const, constant <paramref name="rhs"/>.</summary>
    private void EmitBinary(Tok op, int target, int lsrc, OperandKind kind, int rhs, int line)
    {
        bool k = kind == OperandKind.Const;
        switch (op)
        {
            case Tok.Plus: Emit(Instruction.Abc(k ? OpCode.AddK : OpCode.Add, target, lsrc, rhs), line); break;
            case Tok.Minus: Emit(Instruction.Abc(k ? OpCode.SubK : OpCode.Sub, target, lsrc, rhs), line); break;
            case Tok.Star: Emit(Instruction.Abc(k ? OpCode.MulK : OpCode.Mul, target, lsrc, rhs), line); break;
            case Tok.Slash: Emit(Instruction.Abc(k ? OpCode.DivK : OpCode.Div, target, lsrc, rhs), line); break;
            case Tok.DSlash: Emit(Instruction.Abc(k ? OpCode.IDivK : OpCode.IDiv, target, lsrc, rhs), line); break;
            case Tok.Percent: Emit(Instruction.Abc(k ? OpCode.ModK : OpCode.Mod, target, lsrc, rhs), line); break;
            case Tok.Caret: Emit(Instruction.Abc(k ? OpCode.PowK : OpCode.Pow, target, lsrc, rhs), line); break;
            case Tok.Concat: Emit(Instruction.Abc(OpCode.Concat, target, lsrc, rhs), line); break;
            case Tok.Amp: Emit(Instruction.Abc(OpCode.BAnd, target, lsrc, rhs), line); break;
            case Tok.Pipe: Emit(Instruction.Abc(OpCode.BOr, target, lsrc, rhs), line); break;
            case Tok.Tilde: Emit(Instruction.Abc(OpCode.BXor, target, lsrc, rhs), line); break;
            case Tok.Shl: Emit(Instruction.Abc(OpCode.Shl, target, lsrc, rhs), line); break;
            case Tok.Shr: Emit(Instruction.Abc(OpCode.Shr, target, lsrc, rhs), line); break;
            case Tok.Eq: Emit(Instruction.Abc(OpCode.Eq, target, lsrc, rhs), line); break;
            case Tok.Ne:
                Emit(Instruction.Abc(OpCode.Eq, target, lsrc, rhs), line);
                Emit(Instruction.Abc(OpCode.Not, target, target, 0), line);
                break;
            case Tok.Lt: Emit(Instruction.Abc(OpCode.Lt, target, lsrc, rhs), line); break;
            case Tok.Le: Emit(Instruction.Abc(OpCode.Le, target, lsrc, rhs), line); break;
            case Tok.Gt: Emit(Instruction.Abc(OpCode.Lt, target, rhs, lsrc), line); break;
            case Tok.Ge: Emit(Instruction.Abc(OpCode.Le, target, rhs, lsrc), line); break;
            default: throw new InvalidOperationException(op.ToString());
        }
    }

    private void Simple(int target)
    {
        int line = _tok.Line;
        switch (_tok.Kind)
        {
            case Tok.Integer:
                EmitLoadK(target, NumberConst(LuaValue.FromInteger(_tok.Int)), line);
                Advance();
                return;
            case Tok.Float:
                EmitLoadK(target, NumberConst(LuaValue.FromFloat(_tok.Float)), line);
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
                _openIdx = -1; // the body's last call is not an open call of this expression
                return;
            case Tok.String:
                EmitLoadK(target, AddConst(LuaValue.FromString(_tok.Str)), line);
                Advance();
                return;
            case Tok.LBrace:
                TableConstructor(target);
                return;
            case Tok.Name:
            {
                int local = LocalReg(_tok.Text);
                if (local >= 0 && Peek().Kind is Tok.Dot or Tok.LBracket or Tok.Colon)
                {
                    // a local used as the table of an index or method call is read in place
                    Advance();
                    Suffixes(target, false, line, local);
                    return;
                }
                LoadVariable(_tok.Text, target, line);
                Advance();
                break;
            }
            case Tok.LParen:
                Advance();
                Expr(target, 0);
                ExpectMatch(Tok.RParen, ")", "(", line);
                _openIdx = -1;
                break;
            default:
                throw Err("unexpected symbol");
        }
        Suffixes(target, false, line);
    }

    /// <summary>Compiles a call (or method call, when <paramref name="method"/> is set) whose function value is in <paramref name="target"/>.</summary>
    private void CallSuffix(int target, string? method, int src = -1)
    {
        int line = _tok.Line;
        int save = _fs.Free;
        int fn = target;
        bool fromSrc = method is not null && src >= 0; // the receiver is a local: Self reads it where it lives
        if (target != _fs.Free - 1)
        {
            fn = Alloc();
            if (!fromSrc) Emit(Instruction.Abc(OpCode.Move, fn, target, 0), line);
        }
        int extra = 0;
        if (method is not null)
        {
            Alloc(); // fn + 1 receives the receiver
            int mk = ConstOperand(NameConst(method));
            if (mk >= 0) Emit(Instruction.Abc(OpCode.SelfK, fn, fromSrc ? src : fn, mk), line);
            else
            {
                int k = Alloc();
                EmitLoadK(k, StringConst(method), line);
                Emit(Instruction.Abc(OpCode.Self, fn, fromSrc ? src : fn, k), line);
            }
            _fs.Free = fn + 2;
            extra = 1;
        }
        int b = 1 + extra;
        switch (_tok.Kind)
        {
            case Tok.String:
                EmitLoadK(Alloc(), AddConst(LuaValue.FromString(_tok.Str)), line);
                Advance();
                b++;
                break;
            case Tok.LBrace:
                TableConstructor(Alloc());
                b++;
                break;
            case Tok.LParen:
            {
                int parenLine = _tok.Line;
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
                ExpectMatch(Tok.RParen, ")", "(", parenLine);
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
                int fk = ConstOperand(LuaValue.FromString(_tok.Str));
                if (fk >= 0)
                {
                    Advance();
                    Advance();
                    int fv = Alloc();
                    Expr(fv, 0);
                    Emit(Instruction.Abc(OpCode.SetField, t, fk, fv), line);
                }
                else
                {
                    int k = Alloc();
                    EmitLoadK(k, AddConst(LuaValue.FromString(_tok.Str)), _tok.Line);
                    Advance();
                    Advance();
                    int v = Alloc();
                    Expr(v, 0);
                    Emit(Instruction.Abc(OpCode.SetTable, t, k, v), line);
                }
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
        ExpectMatch(Tok.RBrace, "}", "{", line);
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
