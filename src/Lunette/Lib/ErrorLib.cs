using Lunette.Vm;

namespace Lunette;

/// <summary>The error-related base functions: error, assert, pcall and xpcall.</summary>
/// <remarks>
/// The interpreter recognises these by <see cref="BuiltinKind"/> and runs them in its own loop when called from Lua. The bodies
/// here are the fallback for calls that come through host code (a metamethod, <c>pcall(pcall, f)</c>, a callback) and use exceptions.
/// </remarks>
internal static class ErrorLib
{
    public static void Register(LuaState state)
    {
        state.SetGlobal("error", LuaValue.FromObject(new BuiltinFunction(Error, BuiltinKind.Error)));
        state.SetGlobal("assert", LuaValue.FromObject(new BuiltinFunction(Assert, BuiltinKind.Assert)));
        state.SetGlobal("pcall", LuaValue.FromObject(new BuiltinFunction((st, args) => Protected(st, args, false), BuiltinKind.Pcall)));
        state.SetGlobal("xpcall", LuaValue.FromObject(new BuiltinFunction((st, args) => Protected(st, args, true), BuiltinKind.Xpcall)));
    }

    /// <summary>The optional level argument of error: nil means 1.</summary>
    public static long LevelArg(LuaValue v)
    {
        if (v.IsNil) return 1;
        if (v.IsInteger) return v.AsInteger;
        if (v.IsFloat && LuaNumber.TryToInteger(v, out long n)) return n;
        throw LuaErrors.ArgError(2, "error", v.IsNumber ? "number has no integer representation" : $"number expected, got {v.TypeName}");
    }

    /// <summary>pcall needs a value; xpcall needs a function as its second argument.</summary>
    public static void CheckProtectedArgs(bool xpcall, ReadOnlySpan<LuaValue> args)
    {
        if (xpcall)
        {
            if (args.Length < 2 || !args[1].IsFunction) throw LuaErrors.TypeError(args, 1, "xpcall", "function");
        }
        else if (args.Length == 0) throw LuaErrors.ArgError(1, "pcall", "value expected");
    }

    private static LuaValue[]? Error(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaValue msg = args.Length > 0 ? args[0] : default;
        long level = args.Length > 1 ? LevelArg(args[1]) : 1;
        throw new LuaException(Interpreter.ErrorValue(state.Running, msg, level, true));
    }

    private static LuaValue[]? Assert(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length == 0) throw LuaErrors.ArgError(1, "assert", "value expected");
        if (!args[0].IsFalsy) return args.ToArray();
        LuaValue msg = args.Length > 1 ? args[1] : LuaValue.FromString("assertion failed!");
        throw new LuaException(Interpreter.ErrorValue(state.Running, msg, 1, true));
    }

    private static LuaValue[] Protected(LuaState state, ReadOnlySpan<LuaValue> args, bool xpcall)
    {
        CheckProtectedArgs(xpcall, args);
        var co = state.Running;
        int fixedArgs = xpcall ? 2 : 1;
        LuaValue handler = xpcall ? args[1] : default;
        int level = co.CallTop;
        int depth = co.Depth;
        try
        {
            LuaValue[] res = Interpreter.CallValues(state, args[0], args[fixedArgs..]);
            var ret = new LuaValue[res.Length + 1];
            ret[0] = LuaValue.FromBoolean(true);
            res.CopyTo(ret, 1);
            return ret;
        }
        catch (Exception e) when (Interpreter.IsLuaError(e))
        {
            LuaValue value = Interpreter.AsLuaException(e).Value;
            if (xpcall) value = Interpreter.RunHandler(state, co, handler, value);
            co.Depth = depth;
            co.Close(level);
            co.CallTop = level;
            return [LuaValue.FromBoolean(false), value];
        }
    }
}
