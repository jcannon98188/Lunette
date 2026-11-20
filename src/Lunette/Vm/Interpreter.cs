using Lunette.Compiler;

namespace Lunette.Vm;

internal static class Interpreter
{
    public static void Execute(LuaState state, Proto proto)
    {
        var code = proto.Code;
        var k = proto.Constants;
        var r = new LuaValue[proto.MaxStack];
        int pc = 0;

        while (true)
        {
            Instruction i = code[pc];
            switch (i.Op)
            {
                case OpCode.Move:
                    r[i.A] = r[i.B];
                    break;
                case OpCode.LoadK:
                    r[i.A] = k[i.Bx];
                    break;
                case OpCode.LoadNil:
                    r[i.A] = default;
                    break;
                case OpCode.LoadBool:
                    r[i.A] = LuaValue.FromBoolean(i.B != 0);
                    break;
                case OpCode.GetGlobal:
                    r[i.A] = state.Globals.GetValueOrDefault(proto.Names[i.Bx]);
                    break;
                case OpCode.SetGlobal:
                    state.Globals[proto.Names[i.Bx]] = r[i.A];
                    break;
                case OpCode.Add:
                case OpCode.Sub:
                case OpCode.Mul:
                case OpCode.Div:
                case OpCode.IDiv:
                case OpCode.Mod:
                case OpCode.Pow:
                    r[i.A] = Arith.Binary(i.Op, r[i.B], r[i.C], proto, pc);
                    break;
                case OpCode.Unm:
                    r[i.A] = Arith.Negate(r[i.B], proto, pc);
                    break;
                case OpCode.Call:
                {
                    if (r[i.A].Ref is not BuiltinFunction fn)
                        throw new LuaException($"{Where(proto, pc)} attempt to call a {r[i.A].TypeName} value");
                    var args = new LuaValue[i.B];
                    Array.Copy(r, i.A + 1, args, 0, i.B);
                    fn.Invoke(state, args);
                    break;
                }
                case OpCode.Return:
                    return;
                default:
                    throw new InvalidOperationException($"bad opcode {i.Op}");
            }
            pc++;
        }
    }

    internal static string Where(Proto p, int pc)
    {
        string n = p.ChunkName;
        string name = n.Length > 0 && (n[0] is '=' or '@') ? n[1..] : $"[string \"{n}\"]";
        return $"{name}:{p.Lines[pc]}:";
    }
}
