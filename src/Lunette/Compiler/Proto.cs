namespace Lunette.Compiler;

internal enum OpCode : byte
{
    Move,      // R[A] = R[B]
    LoadK,     // R[A] = K[Bx]
    LoadNil,   // R[A .. A+B] = nil
    LoadBool,  // R[A] = (B != 0)
    GetGlobal, // R[A] = Upvalues[B][Constants[C]] (B: the _ENV upvalue; C <= 255)
    SetGlobal, // Upvalues[B][Constants[C]] = R[A]
    GetUpval,  // R[A] = Upval[B]
    SetUpval,  // Upval[B] = R[A]
    Add,       // R[A] = R[B] + R[C]
    Sub,
    Mul,
    Div,
    IDiv,
    Mod,
    Pow,
    BAnd,      // R[A] = R[B] & R[C]
    BOr,
    BXor,
    Shl,
    Shr,
    BNot,      // R[A] = ~R[B]
    Unm,      // R[A] = -R[B]
    Not,       // R[A] = not R[B]
    Eq,        // R[A] = R[B] == R[C]
    Lt,        // R[A] = R[B] < R[C]
    Le,        // R[A] = R[B] <= R[C]
    Test,      // if truthy(R[A]) == (C != 0) then run the next instruction (a Jmp) else skip it
    Jmp,       // pc = Ax (absolute)
    Call,      // R[A .. A+C-2] = R[A](R[A+1 .. A+B-1]) ; B == 0: args up to top ; C == 0: results set top
    TailCall,  // return R[A](R[A+1 .. A+B-1])
    Return,    // return R[A .. A+B-2] ; B == 0: up to top
    ForPrep,   // prepare numeric loop at R[A..A+3]; if it runs skip the next instruction (a Jmp to the exit)
    ForLoop,   // step numeric loop at R[A..A+3]; if it continues run the next instruction (a Jmp back), else skip it
    Closure,   // R[A] = closure(Protos[Bx])
    Close,     // close upvalues and run pending to-be-closed variables >= R[A]
    Vararg,    // R[A .. A+C-2] = ... ; C == 0: all, sets top
    NewTable,  // R[A] = {} sized for B hash entries and, from the Extra that follows, Ax array slots
    GetTable,  // R[A] = R[B][R[C]]
    SetTable,  // R[A][R[B]] = R[C]
    SetList,   // R[A][Ax+1 .. Ax+B] = R[A+1 .. A+B] (Ax from the Extra that follows); B == 0: up to top
    Self,      // R[A+1] = R[B]; R[A] = R[B][R[C]]
    Len,       // R[A] = #R[B]
    Concat,    // R[A] = R[B] .. R[C]
    TForLoop,  // generic for at R[A..A+3], loop variables from R[A+4]: if R[A+4] is not nil, R[A+2] = R[A+4] and run the next instruction (a Jmp back), else skip it
    Tbc,       // mark R[A] to be closed (Names[Bx] is the variable's name for the error message); nil and false are ignored
    Extra,     // operand word of the preceding instruction (Ax)
    LoadKX,    // R[A] = Constants[Ax of the Extra that follows] (constant indexes beyond what LoadK's Bx reaches)
    AddK,      // R[A] = R[B] + K[C] (K[C] is a number; C <= 255). SubK..PowK likewise, in the order of Add..Pow
    SubK,
    MulK,
    DivK,
    IDivK,
    ModK,
    PowK,
    JEq,       // if (R[B] == R[C]) == (A != 0) then run the next instruction (a Jmp) else skip it
    JLt,       // ... R[B] < R[C]
    JLe,       // ... R[B] <= R[C]
    JEqK,      // ... R[B] == K[C]
    JLtK,      // ... R[B] < K[C] (K[C] is a number)
    JLeK,      // ... R[B] <= K[C]
    JGtK,      // ... K[C] < R[B]  (the source said R[B] > K[C])
    JGeK,      // ... K[C] <= R[B]
}

/// <summary>One 32-bit instruction: op(8) A(8) B(8) C(8); Bx overlays B and C as 16 bits; Ax is the upper 24 bits.</summary>
internal readonly record struct Instruction(uint Raw)
{
    public OpCode Op
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        get => (OpCode)(Raw & 0xFF);
    }
    public int A => (int)((Raw >> 8) & 0xFF);
    public int B => (int)((Raw >> 16) & 0xFF);
    public int C => (int)((Raw >> 24) & 0xFF);
    public int Bx => (int)(Raw >> 16);
    public int Ax => (int)(Raw >> 8);

    public static Instruction Abc(OpCode op, int a, int b, int c) =>
        new((uint)op | ((uint)a << 8) | ((uint)b << 16) | ((uint)c << 24));

    public static Instruction ABx(OpCode op, int a, int bx) =>
        new((uint)op | ((uint)a << 8) | ((uint)bx << 16));

    public static Instruction Extra(int ax) => new((uint)OpCode.Extra | ((uint)ax << 8));

    public static Instruction JmpTo(int target) => new((uint)OpCode.Jmp | ((uint)target << 8));
}

/// <summary>Where a closure finds one captured variable: a register of the enclosing function, or one of its upvalues.</summary>
internal readonly record struct UpvalDesc(bool InStack, int Index);

/// <summary>Debug info for one local variable: the register it lives in and the pc range [StartPc, EndPc) where it is in scope.</summary>
internal readonly record struct LocVar(string Name, int Reg, int StartPc, int EndPc);

/// <summary>The compiled bytecode form of one Lua function body.</summary>
internal sealed class Proto
{
    public required Instruction[] Code { get; init; }
    public required LuaValue[] Constants { get; init; }
    public required string[] Names { get; init; }
    public required int MaxStack { get; init; }
    public required int[] Lines { get; init; }
    public required string ChunkName { get; init; }
    public required int NumParams { get; init; }
    public required bool IsVararg { get; init; }
    public required UpvalDesc[] Upvalues { get; init; }
    public required string[] UpvalNames { get; init; }
    public required LocVar[] LocVars { get; init; }
    public required Proto[] Protos { get; init; }
    /// <summary>Source line of the function keyword (0 for a main chunk) and of its closing end; debug.getinfo reports them.</summary>
    public int LineDefined { get; init; }
    public int LastLineDefined { get; init; }

    private System.Collections.Concurrent.ConcurrentDictionary<int, int>? _liveAt;

    /// <summary>
    /// Registers that can hold a live value at <paramref name="pc"/>, the target of a backward jump (a statement boundary): the
    /// named locals in scope plus the hidden control registers of every <c>for</c> loop whose body contains <paramref name="pc"/>.
    /// Every register above is a dead temporary. The bounds are inclusive, so the answer errs towards keeping a register.
    /// </summary>
    public int LiveRegisters(int pc)
    {
        _liveAt ??= new();
        if (_liveAt.TryGetValue(pc, out int cached)) return cached;
        int top = NumParams;
        foreach (LocVar lv in LocVars)
            if (lv.StartPc <= pc && pc <= lv.EndPc && lv.Reg + 1 > top) top = lv.Reg + 1;
        for (int i = 0; i + 1 < Code.Length; i++)
        {
            Instruction ins = Code[i];
            if (ins.Op is OpCode.ForLoop or OpCode.TForLoop && Code[i + 1].Ax <= pc && pc <= i && ins.A + 4 > top) top = ins.A + 4;
        }
        _liveAt[pc] = top;
        return top;
    }
}
