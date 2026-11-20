namespace Lunette.Compiler;

internal enum OpCode : byte
{
    Move,      // R[A] = R[B]
    LoadK,     // R[A] = K[Bx]
    LoadNil,   // R[A] = nil
    LoadBool,  // R[A] = (B != 0)
    GetGlobal, // R[A] = G[Names[Bx]]
    SetGlobal, // G[Names[Bx]] = R[A]
    Add,       // R[A] = R[B] + R[C]
    Sub,
    Mul,
    Div,
    IDiv,
    Mod,
    Pow,
    Unm,       // R[A] = -R[B]
    Call,      // R[A](R[A+1 .. A+B]) ; no results kept
    Return,
}

/// <summary>One 32-bit instruction: op(8) A(8) B(8) C(8); Bx overlays B and C as 16 bits.</summary>
internal readonly record struct Instruction(uint Raw)
{
    public OpCode Op => (OpCode)(Raw & 0xFF);
    public int A => (int)((Raw >> 8) & 0xFF);
    public int B => (int)((Raw >> 16) & 0xFF);
    public int C => (int)((Raw >> 24) & 0xFF);
    public int Bx => (int)(Raw >> 16);

    public static Instruction Abc(OpCode op, int a, int b, int c) =>
        new((uint)op | ((uint)a << 8) | ((uint)b << 16) | ((uint)c << 24));

    public static Instruction ABx(OpCode op, int a, int bx) =>
        new((uint)op | ((uint)a << 8) | ((uint)bx << 16));
}

/// <summary>The compiled bytecode form of one Lua function body.</summary>
internal sealed class Proto
{
    public required Instruction[] Code { get; init; }
    public required LuaValue[] Constants { get; init; }
    public required string[] Names { get; init; }
    public required int MaxStack { get; init; }
    public required int[] Lines { get; init; }
    public required string ChunkName { get; init; }
}
