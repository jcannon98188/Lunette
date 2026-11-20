using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>Names the variable behind a register for error messages, after ldebug.c's getobjname/varinfo.</summary>
internal static class VarInfo
{
    /// <summary>Returns " (kind 'name')" for the value in <paramref name="reg"/> at <paramref name="pc"/>, or "" when unknown.</summary>
    public static string Describe(Proto? p, int pc, int reg)
    {
        if (p is null) return "";
        string? kind = ObjName(p, pc, reg, out string name);
        return kind is null ? "" : $" ({kind} '{name}')";
    }

    /// <summary>Describes the called value of a Call or TailCall instruction at <paramref name="pc"/>.</summary>
    public static string DescribeCallee(Proto? p, int pc)
    {
        if (p is null) return "";
        var i = p.Code[pc];
        if (i.Op == OpCode.Call && pc + 1 < p.Code.Length && p.Code[pc + 1].Op == OpCode.TForLoop)
            return " (for iterator 'for iterator')";
        return Describe(p, pc, i.A);
    }

    /// <summary>The register operand a binary operator reports as faulty: B for the left, C for the right.</summary>
    public static string DescribeOperand(Proto? p, int pc, bool left)
    {
        if (p is null) return "";
        var i = p.Code[pc];
        if (!left && Meta.IsArithK(i.Op)) return ""; // the right operand is a numeric constant, never a variable
        return Describe(p, pc, left ? i.B : i.C);
    }

    public static string? ObjName(Proto p, int lastpc, int reg, out string name)
    {
        name = "";
        foreach (var lv in p.LocVars)
        {
            if (lv.Reg == reg && lv.StartPc <= lastpc && lastpc < lv.EndPc)
            {
                name = lv.Name;
                return "local";
            }
        }
        int setreg = FindSetReg(p, lastpc, reg);
        if (setreg < 0) return null;
        var i = p.Code[setreg];
        switch (i.Op)
        {
            case OpCode.Move:
                return i.B == reg ? null : ObjName(p, setreg, i.B, out name);
            case OpCode.GetGlobal:
                name = KeyConstName(p, i.C);
                return "global";
            case OpCode.GetUpval:
                name = p.UpvalNames[i.B];
                return "upvalue";
            case OpCode.GetTable:
                name = KeyName(p, setreg, i.C);
                return IsEnvLocal(p, setreg, i.B) ? "global" : "field";
            case OpCode.Self:
                name = KeyName(p, setreg, i.C);
                return "method";
            case OpCode.LoadK:
                if (p.Constants[i.Bx].Ref is byte[] s)
                {
                    name = new LuaString(s).ToString();
                    return "constant";
                }
                return null;
            default:
                return null;
        }
    }

    private static bool IsEnvLocal(Proto p, int pc, int reg)
    {
        foreach (var lv in p.LocVars)
            if (lv.Reg == reg && lv.StartPc <= pc && pc < lv.EndPc) return lv.Name == "_ENV";
        int set = FindSetReg(p, pc, reg);
        return set >= 0 && p.Code[set].Op == OpCode.GetUpval && p.UpvalNames[p.Code[set].B] == "_ENV";
    }

    private static string KeyConstName(Proto p, int k) =>
        p.Constants[k].Ref is byte[] s ? new LuaString(s).ToString() : "?";

    private static string KeyName(Proto p, int pc, int reg)
    {
        int set = FindSetReg(p, pc, reg);
        if (set >= 0 && p.Code[set].Op == OpCode.LoadK && p.Constants[p.Code[set].Bx].Ref is byte[] s) return new LuaString(s).ToString();
        if (set >= 0 && p.Code[set].Op == OpCode.LoadKX && p.Constants[p.Code[set + 1].Ax].Ref is byte[] sx) return new LuaString(sx).ToString();
        return "?";
    }

    private static bool Sets(Instruction i, int reg)
    {
        switch (i.Op)
        {
            case OpCode.LoadNil:
                return reg >= i.A && reg <= i.A + i.B;
            case OpCode.Call:
                return reg >= i.A;
            case OpCode.Vararg:
                return reg >= i.A && (i.C == 0 || reg < i.A + i.C - 1);
            case OpCode.Self:
                return reg == i.A || reg == i.A + 1;
            case OpCode.ForPrep:
            case OpCode.ForLoop:
                return reg >= i.A && reg <= i.A + 3;
            case OpCode.Move:
            case OpCode.LoadK:
            case OpCode.LoadKX:
            case OpCode.LoadBool:
            case OpCode.GetGlobal:
            case OpCode.GetUpval:
            case >= OpCode.Add and <= OpCode.Shr:
            case >= OpCode.AddK and <= OpCode.PowK:
            case OpCode.BNot:
            case OpCode.Unm:
            case OpCode.Not:
            case OpCode.Eq:
            case OpCode.Lt:
            case OpCode.Le:
            case OpCode.Closure:
            case OpCode.NewTable:
            case OpCode.GetTable:
            case OpCode.Len:
                return reg == i.A;
            default:
                return false;
        }
    }

    /// <summary>The last instruction before <paramref name="lastpc"/> that certainly wrote <paramref name="reg"/>, or -1.</summary>
    private static int FindSetReg(Proto p, int lastpc, int reg)
    {
        int setreg = -1;
        int jmptarget = 0;
        for (int pc = 0; pc < lastpc; pc++)
        {
            var i = p.Code[pc];
            if (i.Op == OpCode.Jmp)
            {
                int dest = i.Ax;
                if (pc < dest && dest <= lastpc && dest > jmptarget) jmptarget = dest;
            }
            else if (Sets(i, reg)) setreg = pc < jmptarget ? -1 : pc;
        }
        return setreg;
    }
}
