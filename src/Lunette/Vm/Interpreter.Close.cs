using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>To-be-closed variables: registration, normal-exit closing and closing while an error unwinds.</summary>
internal static partial class Interpreter
{
    private static readonly LuaValue NoError = default;

    /// <summary>Registers R[<paramref name="index"/>] to be closed; nil and false are ignored, anything without __close is an error.</summary>
    private static void MarkToBeClosed(LuaState state, Coroutine co, int index, Proto proto, int pc, string name)
    {
        LuaValue v = co.Stack[index];
        if (v.IsFalsy) return;
        if (Meta.GetTagMethod(state, v, TagMethod.Close).IsNil)
            throw new LuaException($"{Where(proto, pc)} variable '{name}' got a non-closable value");
        co.Tbc.Add(index);
    }

    /// <summary>
    /// Runs the __close handlers of every to-be-closed variable at or above stack index <paramref name="level"/>, newest first.
    /// <paramref name="protectTo"/> is the first stack slot the handlers may use, so live results below it survive.
    /// A handler error propagates; the variables not yet closed stay registered for the unwinder.
    /// </summary>
    private static void CloseTbc(LuaState state, Coroutine co, int level, int protectTo)
    {
        int saved = co.CallTop;
        co.CallTop = Math.Max(saved, protectTo);
        try
        {
            while (co.Tbc.Count > 0 && co.Tbc[^1] >= level)
            {
                int idx = co.Tbc[^1];
                co.Tbc.RemoveAt(co.Tbc.Count - 1);
                CallClose(state, co, idx, NoError);
            }
        }
        finally
        {
            co.CallTop = saved;
        }
    }

    /// <summary>
    /// Closes the to-be-closed variables at or above <paramref name="level"/> while an error unwinds, passing each the error.
    /// An error raised by a handler replaces the current error. Returns the final error value.
    /// </summary>
    private static LuaValue CloseTbcOnError(LuaState state, Coroutine co, int level, LuaValue error)
    {
        if (co.Tbc.Count == 0 || co.Tbc[^1] < level) return error;
        int depth = co.Depth;
        int saved = co.CallTop;
        if (depth > 0)
        {
            Frame top = co.Frames[depth - 1]!;
            co.CallTop = Math.Max(co.CallTop, top.Base + top.Closure.Proto.MaxStack);
        }
        try
        {
            while (co.Tbc.Count > 0 && co.Tbc[^1] >= level)
            {
                int idx = co.Tbc[^1];
                co.Tbc.RemoveAt(co.Tbc.Count - 1);
                try
                {
                    CallClose(state, co, idx, error);
                }
                catch (Exception e) when (IsLuaError(e))
                {
                    error = AsLuaException(e).Value;
                    co.Depth = depth;
                }
            }
        }
        finally
        {
            co.CallTop = saved;
        }
        return error;
    }

    private static void CallClose(LuaState state, Coroutine co, int index, LuaValue error)
    {
        LuaValue v = co.Stack[index];
        LuaValue tm = Meta.GetTagMethod(state, v, TagMethod.Close);
        if (tm.IsNil) throw new LuaException("attempt to call a nil value (metamethod 'close')");
        CallValues(state, tm, [v, error]);
    }
}
