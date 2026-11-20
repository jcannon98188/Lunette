using Lunette.Vm;

namespace Lunette;

/// <summary>The <c>coroutine</c> library. Yield itself is recognised by the interpreter (<see cref="BuiltinKind.Yield"/>).</summary>
internal static class CoroutineLib
{
    public static void Register(LuaState state)
    {
        if (!state.Wants("coroutine")) return;
        var t = new LuaTable(0, 8);
        Add(t, "create", Create);
        Add(t, "resume", Resume);
        t.Set(LuaValue.FromString("yield"), LuaValue.FromObject(new BuiltinFunction(Yield, BuiltinKind.Yield)));
        Add(t, "status", Status);
        Add(t, "wrap", Wrap);
        Add(t, "isyieldable", IsYieldable);
        Add(t, "running", static (st, _) => Rets.Two(LuaValue.FromObject(st.Running), LuaValue.FromBoolean(st.Running.IsMain)));
        Add(t, "close", Close);
        state.SetGlobal("coroutine", LuaValue.FromTable(t));
    }

    private static void Add(LuaTable t, string name, BuiltinBody body) =>
        t.Set(LuaValue.FromString(name), LuaValue.FromObject(new BuiltinFunction(body)));

    private static LuaValue Arg(ReadOnlySpan<LuaValue> args, int i) => i < args.Length ? args[i] : default;

    private static Coroutine CheckCo(ReadOnlySpan<LuaValue> args, string fn)
    {
        if (args.Length > 0 && args[0].Ref is Coroutine co) return co;
        throw LuaErrors.TypeError(args, 0, fn, "coroutine");
    }

    private static Coroutine NewCoroutine(LuaState state, ReadOnlySpan<LuaValue> args, string fn)
    {
        if (args.Length == 0 || !args[0].IsFunction) throw LuaErrors.TypeError(args, 0, fn, "function");
        LuaValue body = args[0];
        if (body.Ref is BuiltinFunction)
        {
            // A host function as the body runs through a Lua trampoline so that it can yield across pcall and friends.
            LuaClosure tramp = state.Load("local f = ...; return function(...) return f(...) end"u8.ToArray(), "=(coroutine)", LuaValue.FromTable(state.Globals));
            body = Interpreter.CallValues(state, LuaValue.FromObject(tramp), [body])[0];
        }
        return new Coroutine { Body = body };
    }

    private static LuaValue[] Create(LuaState state, ReadOnlySpan<LuaValue> args) =>
        [LuaValue.FromObject(NewCoroutine(state, args, "create"))];

    private static LuaValue[] Resume(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        Coroutine co = CheckCo(args, "resume");
        bool ok = Interpreter.Resume(state, co, args[1..], out LuaValue[] res);
        if (res.Length <= 2)
        {
            LuaValue first = res.Length > 0 ? res[0] : default, second = res.Length > 1 ? res[1] : default;
            int n = res.Length;
            Rets.Release(res);
            LuaValue okv = LuaValue.FromBoolean(ok);
            return n == 0 ? Rets.One(okv) : n == 1 ? Rets.Two(okv, first) : Rets.Three(okv, first, second);
        }
        var ret = new LuaValue[res.Length + 1];
        ret[0] = LuaValue.FromBoolean(ok);
        res.CopyTo(ret, 1);
        return ret;
    }

    private static LuaValue[] Yield(LuaState state, ReadOnlySpan<LuaValue> args) =>
        // Only reached through host code (the interpreter handles direct calls), where yielding is never possible.
        throw new LuaException(state.Running.IsMain ? "attempt to yield from outside a coroutine" : "attempt to yield across a C-call boundary");

    private static LuaValue[] Status(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        Coroutine co = CheckCo(args, "status");
        return [LuaValue.FromString(co.Status switch
        {
            CoStatus.Suspended => "suspended",
            CoStatus.Running => "running",
            CoStatus.Normal => "normal",
            _ => "dead",
        })];
    }

    private static LuaValue[] IsYieldable(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        Coroutine co = args.Length > 0 ? CheckCo(args, "isyieldable") : state.Running;
        return Rets.One(LuaValue.FromBoolean(!co.IsMain && co.Nny == 0));
    }

    private static LuaValue[] Wrap(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        Coroutine co = NewCoroutine(state, args, "wrap");
        return [LuaValue.FromObject(new BuiltinFunction((st, a) =>
        {
            if (Interpreter.Resume(st, co, a, out LuaValue[] res)) return res;
            // Like auxwrap, an error in the coroutine closes its pending to-be-closed variables; an error there replaces it.
            if (co.Status == CoStatus.Dead && Interpreter.CloseThread(st, co) is LuaValue closeErr) res = [closeErr];
            var ex = new LuaException(res[0]);
            // Like auxwrap: a string error gets the position of the caller of the wrapper.
            if (res[0].Ref is string) ex.WantsPosition = true;
            throw ex;
        }) { Collectable = true })];
    }

    private static LuaValue[] Close(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        Coroutine co = CheckCo(args, "close");
        switch (co.Status)
        {
            case CoStatus.Running:
                throw LuaErrors.Error("cannot close a running coroutine");
            case CoStatus.Normal:
                throw LuaErrors.Error("cannot close a normal coroutine");
        }
        return Interpreter.CloseThread(state, co) is LuaValue err
            ? [LuaValue.FromBoolean(false), err]
            : [LuaValue.FromBoolean(true)];
    }
}
