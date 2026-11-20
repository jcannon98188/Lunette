using System.Reflection;
using System.Reflection.Emit;

namespace Lunette.Tests;

/// <summary>Static IL metrics for one method, in the units the JIT's MinOpts thresholds use.</summary>
internal sealed record IlMetrics(int Bytes, int Instructions, int BasicBlocks, int LocalRefs, int Locals);

/// <summary>
/// Walks a method's IL (Release build) and counts instructions, local references (locals and arguments),
/// basic blocks (distinct leaders: entry, branch targets, instructions after a branch, and EH region starts)
/// and declared locals. Block count is a close static approximation of the JIT's flow graph, which also splits
/// blocks for calls inside try regions and for inlinees; the 70% ceiling leaves room for that.
/// </summary>
internal static class IlWalker
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = BuildTable();

    private static Dictionary<short, OpCode> BuildTable()
    {
        var map = new Dictionary<short, OpCode>();
        foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (f.GetValue(null) is OpCode op) map[op.Value] = op;
        return map;
    }

    public static IlMetrics Measure(MethodBase method)
    {
        MethodBody body = method.GetMethodBody() ?? throw new InvalidOperationException($"{method.Name} has no IL body");
        byte[] il = body.GetILAsByteArray()!;
        int instructions = 0, localRefs = 0;
        var leaders = new HashSet<int> { 0 };
        foreach (ExceptionHandlingClause c in body.ExceptionHandlingClauses)
        {
            leaders.Add(c.TryOffset);
            leaders.Add(c.HandlerOffset);
            if (c.Flags == ExceptionHandlingClauseOptions.Filter) leaders.Add(c.FilterOffset);
        }

        int pos = 0;
        while (pos < il.Length)
        {
            short code = il[pos++];
            if (code == 0xFE) code = (short)(0xFE00 | il[pos++]);
            OpCode op = OpCodesByValue[code];
            instructions++;
            string name = op.Name!;
            if (name.StartsWith("ldloc", StringComparison.Ordinal) || name.StartsWith("stloc", StringComparison.Ordinal)
                || name.StartsWith("ldarg", StringComparison.Ordinal) || name.StartsWith("starg", StringComparison.Ordinal))
                localRefs++;

            int operandStart = pos;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget:
                    leaders.Add(operandStart + 1 + (sbyte)il[operandStart]);
                    leaders.Add(operandStart + 1);
                    pos += 1;
                    break;
                case OperandType.InlineBrTarget:
                    leaders.Add(operandStart + 4 + BitConverter.ToInt32(il, operandStart));
                    leaders.Add(operandStart + 4);
                    pos += 4;
                    break;
                case OperandType.InlineSwitch:
                {
                    int n = BitConverter.ToInt32(il, operandStart);
                    int end = operandStart + 4 + 4 * n;
                    for (int i = 0; i < n; i++) leaders.Add(end + BitConverter.ToInt32(il, operandStart + 4 + 4 * i));
                    leaders.Add(end);
                    pos = end;
                    break;
                }
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    pos += 8;
                    break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    pos += 1;
                    break;
                case OperandType.InlineVar:
                    pos += 2;
                    break;
                default:
                    pos += 4;
                    break;
            }

            if (op.FlowControl is FlowControl.Return or FlowControl.Throw && pos < il.Length) leaders.Add(pos);
        }

        leaders.RemoveWhere(l => l >= il.Length);
        return new IlMetrics(il.Length, instructions, leaders.Count, localRefs, body.LocalVariables.Count);
    }
}
