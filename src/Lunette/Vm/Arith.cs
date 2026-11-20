using Lunette.Compiler;

namespace Lunette.Vm;

internal static class Arith
{
    public static LuaValue Binary(OpCode op, LuaValue a, LuaValue b, Proto p, int pc)
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
        if (!a.TryToNumber(out double fa)) throw Fail(a, p, pc);
        if (!b.TryToNumber(out double fb)) throw Fail(b, p, pc);
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

    public static LuaValue Negate(LuaValue a, Proto p, int pc)
    {
        if (a.IsInteger) return LuaValue.FromInteger(unchecked(-a.AsInteger));
        if (a.IsFloat) return LuaValue.FromFloat(-a.AsFloat);
        throw Fail(a, p, pc);
    }

    private static long IntFloorDiv(long x, long y, Proto p, int pc)
    {
        if (y == 0) throw new LuaException($"{Interpreter.Where(p, pc)} attempt to perform 'n//0'");
        if (y == -1) return unchecked(-x);
        long q = x / y;
        if ((x ^ y) < 0 && x % y != 0) q--;
        return q;
    }

    private static long IntMod(long x, long y, Proto p, int pc)
    {
        if (y == 0) throw new LuaException($"{Interpreter.Where(p, pc)} attempt to perform 'n%%0'");
        if (y == -1) return 0;
        long m = x % y;
        if (m != 0 && (m ^ y) < 0) m += y;
        return m;
    }

    private static double FloatMod(double a, double b)
    {
        double m = a % b;
        if (m != 0 && (m < 0) != (b < 0)) m += b;
        return m;
    }

    private static LuaException Fail(LuaValue v, Proto p, int pc) =>
        new($"{Interpreter.Where(p, pc)} attempt to perform arithmetic on a {v.TypeName} value");
}
