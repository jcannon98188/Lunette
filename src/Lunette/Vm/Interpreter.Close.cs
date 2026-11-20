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
    /// Starts the next <c>__close</c> handler for the Return or Close instruction that ended at <paramref name="pc"/> as a Frame, so it
    /// can yield. The instruction runs again when the handler returns (<see cref="ResumeAfterMeta"/>), once per variable. A handler that
    /// is not a Lua function runs at once and cannot yield. Returns null when no variable at or above <paramref name="level"/> is left.
    /// </summary>
    private static Frame? StartClose(LuaState state, Coroutine co, Frame f, int level, int protectTo, int top)
    {
        while (co.Tbc.Count > 0 && co.Tbc[^1] >= level)
        {
            int idx = co.Tbc[^1];
            co.Tbc.RemoveAt(co.Tbc.Count - 1);
            LuaValue v = co.Stack[idx];
            LuaValue tm = Meta.GetTagMethod(state, v, TagMethod.Close);
            if (tm.Ref is not LuaClosure cl)
            {
                int saved = co.CallTop;
                co.CallTop = Math.Max(saved, protectTo);
                try
                {
                    CallClose(state, co, idx, NoError);
                }
                finally
                {
                    co.CallTop = saved;
                }
                continue;
            }
            f.SavedTop = top;
            co.EnsureStack(protectTo + 4, null, 0);
            var st = co.Stack;
            st[protectTo] = tm;
            st[protectTo + 1] = v;
            st[protectTo + 2] = default;
            Frame nf = Precall(co, cl, protectTo, 2, 0, null, null, 0);
            nf.Cont = Continuation.Close;
            return nf;
        }
        return null;
    }

    /// <summary>
    /// Finishes the Frame that ran a <c>__close</c> handler: pops it and returns the Frame to continue in. After a handler for a Return
    /// or Close instruction that instruction runs again; after one for an unwinding protected call the unwinding goes on.
    /// </summary>
    private static Frame ResumeAfterMeta(LuaState state, Coroutine co, Continuation cont, ref int top)
    {
        Frame f = co.Frames[--co.Depth - 1]!;
        if (cont == Continuation.Close)
        {
            top = f.SavedTop;
            f.Pc--;
        }
        else if (cont == Continuation.CloseError)
        {
            ContinueUnwind(state, co, co.Depth - 1, f.ErrValue);
            f = co.ResumeFrame!;
            top = co.ResumeTop;
        }
        return f;
    }

    /// <summary>
    /// Closes the variables of the failed protected Frame <paramref name="i"/> with <paramref name="error"/>, newest first. A Lua handler
    /// runs as a Frame (so it can yield) and the unwinding continues when it returns; an error in a handler replaces the error. When
    /// every variable is closed the protected call stores (false, error) and the loop resumes in its caller. Either way
    /// <c>co.ResumeFrame</c> and <c>co.ResumeTop</c> say where the loop goes next.
    /// </summary>
    private static void ContinueUnwind(LuaState state, Coroutine co, int i, LuaValue error)
    {
        Frame fr = co.Frames[i]!;
        co.Depth = i + 1;
        if (co.Tbc.Count > 0 && co.Tbc[^1] >= fr.FuncIdx)
        {
            co.Close(fr.FuncIdx);
            while (co.Tbc.Count > 0 && co.Tbc[^1] >= fr.FuncIdx)
            {
                int idx = co.Tbc[^1];
                co.Tbc.RemoveAt(co.Tbc.Count - 1);
                LuaValue v = co.Stack[idx];
                LuaValue tm = Meta.GetTagMethod(state, v, TagMethod.Close);
                int fa = idx + 2;
                if (tm.Ref is LuaClosure cl)
                {
                    co.EnsureStack(fa + 4, null, 0);
                    var st = co.Stack;
                    st[fa] = tm;
                    st[fa + 1] = v;
                    st[fa + 2] = error;
                    fr.Closing = true;
                    fr.ErrValue = error;
                    Frame nf = Precall(co, cl, fa, 2, 0, null, null, 0);
                    nf.Cont = Continuation.CloseError;
                    co.ResumeFrame = nf;
                    co.ResumeTop = 0;
                    return;
                }
                int saved = co.CallTop;
                co.CallTop = fa;
                try
                {
                    CallClose(state, co, idx, error);
                }
                catch (Exception e) when (IsLuaError(e))
                {
                    error = AsLuaException(e).Value;
                    co.Depth = i + 1;
                }
                finally
                {
                    co.CallTop = saved;
                }
            }
        }
        fr.Closing = false;
        fr.ErrValue = default;
        co.Close(fr.FuncIdx);
        int dst = fr.PcallBase;
        int wanted = fr.Wanted;
        co.Depth = i;
        var r = co.Stack;
        r[dst] = LuaValue.FromBoolean(false);
        r[dst + 1] = error;
        if (wanted < 0) co.ResumeTop = dst + 2;
        else for (int j = 2; j < wanted; j++) r[dst + j] = default;
        co.ResumeFrame = co.Frames[i - 1];
        co.RaisingBuiltin = default;
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

    /// <summary>
    /// <c>lua_close</c>: closes every to-be-closed variable still registered on the main thread (newest first, no error), as
    /// when the host or <c>os.exit(_, true)</c> ends the state while chunks are still active. Handler errors are ignored.
    /// </summary>
    internal static void CloseMainThread(LuaState state)
    {
        Coroutine co = state.Main;
        if (co.Tbc.Count == 0) return;
        state.Running = co;
        int depth = co.Depth;
        if (depth > 0)
        {
            Frame top = co.Frames[depth - 1]!;
            co.CallTop = Math.Max(co.CallTop, top.Base + top.Closure.Proto.MaxStack);
        }
        while (co.Tbc.Count > 0)
        {
            int idx = co.Tbc[^1];
            co.Tbc.RemoveAt(co.Tbc.Count - 1);
            try { CallClose(state, co, idx, NoError); }
            catch (Exception e) when (IsLuaError(e) || e is LuaExitException) { co.Depth = depth; }
        }
    }

    private static void CallClose(LuaState state, Coroutine co, int index, LuaValue error)
    {
        LuaValue v = co.Stack[index];
        LuaValue tm = Meta.GetTagMethod(state, v, TagMethod.Close);
        if (tm.IsNil) throw new LuaException("attempt to call a nil value (metamethod 'close')");
        CallValues(state, tm, [v, error]);
    }
}
