using Lunette.Vm;

namespace Lunette;

/// <summary>The <c>coroutine</c> library. Yield itself is recognised by the interpreter (<see cref="BuiltinKind.Yield"/>).</summary>
internal static class CoroutineLib
{
    public static void Register(LuaState state)
    {
        var t = new LuaTable(0, 8);
        Add(t, "create", Create);
        Add(t, "resume", Resume);
        t.Set(LuaValue.FromString("yield"), LuaValue.FromObject(new BuiltinFunction(Yield, BuiltinKind.Yield)));
        Add(t, "status", Status);
        Add(t, "wrap", Wrap);
        Add(t, "isyieldable", IsYieldable);
        Add(t, "running", static (st, _) => [LuaValue.FromObject(st.Running), LuaValue.FromBoolean(st.Running.IsMain)]);
        Add(t, "close", Close);
        state.Globals["coroutine"] = LuaValue.FromTable(t);
    }

    private static void Add(LuaTable t, string name, BuiltinBody body) =>
        t.Set(LuaValue.FromString(name), LuaValue.FromObject(new BuiltinFunction(body)));

    private static LuaValue Arg(ReadOnlySpan<LuaValue> args, int i) => i < args.Length ? args[i] : default;

    private static Coroutine CheckCo(ReadOnlySpan<LuaValue> args, string fn)
    {
        if (args.Length > 0 && args[0].Ref is Coroutine co) return co;
        throw LuaErrors.TypeError(args, 0, fn, "coroutine");
    }

    private static Coroutine NewCoroutine(ReadOnlySpan<LuaValue> args, string fn)
    {
        if (args.Length == 0 || !args[0].IsFunction) throw LuaErrors.TypeError(args, 0, fn, "function");
        return new Coroutine { Body = args[0] };
    }

    private static LuaValue[] Create(LuaState state, ReadOnlySpan<LuaValue> args) =>
        [LuaValue.FromObject(NewCoroutine(args, "create"))];

    private static LuaValue[] Resume(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        Coroutine co = CheckCo(args, "resume");
        bool ok = Interpreter.Resume(state, co, args[1..], out LuaValue[] res);
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
        return [LuaValue.FromBoolean(!co.IsMain && co.Nny == 0)];
    }

    private static LuaValue[] Wrap(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        Coroutine co = NewCoroutine(args, "wrap");
        return [LuaValue.FromObject(new BuiltinFunction((st, a) =>
        {
            if (Interpreter.Resume(st, co, a, out LuaValue[] res)) return res;
            var ex = new LuaException(res[0]);
            // Like auxwrap: a string error gets the position of the caller of the wrapper.
            if (res[0].Ref is string) ex.WantsPosition = true;
            throw ex;
        }))];
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
        co.Close(0);
        co.Status = CoStatus.Dead;
        if (co.ErrorValue is LuaValue err)
        {
            co.ErrorValue = null;
            return [LuaValue.FromBoolean(false), err];
        }
        return [LuaValue.FromBoolean(true)];
    }
}
