using Lunette.Compiler;

namespace Lunette.Vm;

internal static class Arith
{
    /// <summary>With a null <paramref name="p"/> (string operands, which PUC converts inside the string metamethod, a C function) errors carry no position.</summary>
    public static LuaValue Binary(OpCode op, LuaValue a, LuaValue b, Proto? p, int pc)
    {
        if (a.IsInteger && b.IsInteger)
        {
            long x = a.AsInteger, y = b.AsInteger;
            switch (op)
            {
                case OpCode.Add: return LuaValue.FromInteger(unchecked(x + y));
                case OpCode.Sub: return LuaValue.FromInteger(unchecked(x - y));
                case OpCode.Mul: return LuaValue.FromInteger(unchecked(x * y));
                case OpCode.IDiv: return LuaValue.FromInteger(IntFloorDiv(x, y, p, pc));
                case OpCode.Mod: return LuaValue.FromInteger(IntMod(x, y, p, pc));
            }
        }
        if (!a.TryToNumber(out double fa)) throw Fail(a, true, p!, pc);
        if (!b.TryToNumber(out double fb)) throw Fail(b, false, p!, pc);
        return LuaValue.FromFloat(op switch
        {
            OpCode.Add => fa + fb,
            OpCode.Sub => fa - fb,
            OpCode.Mul => fa * fb,
            OpCode.Div => fa / fb,
            OpCode.IDiv => Math.Floor(fa / fb),
            OpCode.Mod => FloatMod(fa, fb),
            OpCode.Pow => Math.Pow(fa, fb),
            _ => throw new InvalidOperationException(op.ToString()),
        });
    }

    public static LuaValue Bitwise(OpCode op, LuaValue a, LuaValue b, Proto p, int pc)
    {
        long x = ToBitwiseOperand(a, b, a, p, pc);
        long y = ToBitwiseOperand(a, b, b, p, pc);
        return LuaValue.FromInteger(op switch
        {
            OpCode.BAnd => x & y,
            OpCode.BOr => x | y,
            OpCode.BXor => x ^ y,
            OpCode.Shl => LuaNumber.ShiftLeft(x, y),
            OpCode.Shr => LuaNumber.ShiftLeft(x, unchecked(-y)),
            _ => throw new InvalidOperationException(op.ToString()),
        });
    }

    public static LuaValue BitwiseNot(LuaValue a, Proto p, int pc) =>
        LuaValue.FromInteger(~ToBitwiseOperand(a, a, a, p, pc));

    // Mirrors luaG_opinterror/luaG_tointerror: a non-number is reported first; otherwise the float that has no integer form.
    private static long ToBitwiseOperand(LuaValue a, LuaValue b, LuaValue v, Proto p, int pc)
    {
        if (LuaNumber.TryToInteger(v, out long n)) return n;
        if (!a.IsNumber || !b.IsNumber)
        {
            bool left = !a.IsNumber;
            LuaValue bad = left ? a : b;
            throw new LuaException($"{Interpreter.Where(p, pc)} attempt to perform bitwise operation on a {Meta.ObjTypeName(bad)} value{VarInfo.DescribeOperand(p, pc, left)}");
        }
        bool leftBad = !LuaNumber.TryToInteger(a, out _);
        throw new LuaException($"{Interpreter.Where(p, pc)} number{VarInfo.DescribeOperand(p, pc, leftBad)} has no integer representation");
    }

    public static LuaValue Negate(LuaValue a, Proto p, int pc)
    {
        if (a.IsInteger) return LuaValue.FromInteger(unchecked(-a.AsInteger));
        if (a.IsFloat) return LuaValue.FromFloat(-a.AsFloat);
        throw Fail(a, true, p, pc);
    }

    /// <summary>
    /// The number-only cases of binary arithmetic that the dispatch loop's inline paths leave out (mixed integer/float operands, IDiv, Mod, Pow and the bitwise
    /// operators on integers): stores the result in <paramref name="dst"/> and returns true. Returns false for anything that can fail or needs a metamethod
    /// (non-numbers, a zero or negative integer divisor, floats in a bitwise operator), which takes the general path.
    /// </summary>
    public static bool TryNumeric(OpCode op, ref LuaValue dst, in LuaValue x, in LuaValue y)
    {
        bool xi = x.IsInteger, yi = y.IsInteger;
        if (!(xi || x.IsFloat) || !(yi || y.IsFloat)) return false;
        OpCode b = Meta.BaseArith(op);
        if (b >= OpCode.BAnd)
        {
            if (!(xi && yi)) return false;
            long a = x.AsInteger, c = y.AsInteger;
            long res = b switch
            {
                OpCode.BAnd => a & c,
                OpCode.BOr => a | c,
                OpCode.BXor => a ^ c,
                OpCode.Shl => LuaNumber.ShiftLeft(a, c),
                _ => LuaNumber.ShiftLeft(a, unchecked(-c)),
            };
            Lunette.Unsafe.Regs.SetInteger(ref dst, res);
            return true;
        }
        if (xi && yi)
        {
            long a = x.AsInteger, c = y.AsInteger;
            switch (b)
            {
                case OpCode.Add: Lunette.Unsafe.Regs.SetInteger(ref dst, unchecked(a + c)); return true;
                case OpCode.Sub: Lunette.Unsafe.Regs.SetInteger(ref dst, unchecked(a - c)); return true;
                case OpCode.Mul: Lunette.Unsafe.Regs.SetInteger(ref dst, unchecked(a * c)); return true;
                case OpCode.IDiv:
                case OpCode.Mod:
                    if (c <= 0) return false;
                    long q = a / c;
                    bool adjust = a < 0 && q * c != a;
                    Lunette.Unsafe.Regs.SetInteger(ref dst, b == OpCode.IDiv ? (adjust ? q - 1 : q) : a - q * c + (adjust ? c : 0));
                    return true;
            }
        }
        double fa = xi ? x.AsInteger : x.AsFloat, fb = yi ? y.AsInteger : y.AsFloat;
        double fr;
        switch (b)
        {
            case OpCode.Add: fr = fa + fb; break;
            case OpCode.Sub: fr = fa - fb; break;
            case OpCode.Mul: fr = fa * fb; break;
            case OpCode.Div: fr = fa / fb; break;
            case OpCode.IDiv: fr = Math.Floor(fa / fb); break;
            case OpCode.Mod: fr = FloatMod(fa, fb); break;
            default: fr = Math.Pow(fa, fb); break;
        }
        Lunette.Unsafe.Regs.SetFloat(ref dst, fr);
        return true;
    }

    private static string Pos(Proto? p, int pc) => p is null ? "" : Interpreter.Where(p, pc) + " ";

    private static long IntFloorDiv(long x, long y, Proto? p, int pc)
    {
        if (y == 0) throw new LuaException($"{Pos(p, pc)}attempt to divide by zero");
        if (y == -1) return unchecked(-x);
        long q = x / y;
        if ((x ^ y) < 0 && x % y != 0) q--;
        return q;
    }

    private static long IntMod(long x, long y, Proto? p, int pc)
    {
        if (y == 0) throw new LuaException($"{Pos(p, pc)}attempt to perform 'n%0'");
        if (y == -1) return 0;
        long m = x % y;
        if (m != 0 && (m ^ y) < 0) m += y;
        return m;
    }

    private static double FloatMod(double a, double b)
    {
        double m = LuaNumber.FMod(a, b);
        if (m != 0 && (m < 0) != (b < 0)) m += b;
        return m;
    }

    private static LuaException Fail(LuaValue v, bool left, Proto p, int pc) =>
        new($"{Interpreter.Where(p, pc)} attempt to perform arithmetic on a {Meta.ObjTypeName(v)} value{VarInfo.DescribeOperand(p, pc, left)}");
}
