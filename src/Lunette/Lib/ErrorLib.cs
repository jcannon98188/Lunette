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

    /// <summary>
    /// A library function called straight from pcall has no call-site name, so luaL_argerror falls back to the qualified
    /// global name: "bad argument #1 to 'string.rep'" rather than 'rep'.
    /// </summary>
    internal static void QualifyArgError(LuaState state, LuaValue fn, LuaException ex)
    {
        const string Head = "bad argument #";
        if (fn.Ref is not BuiltinFunction || !ex.WantsPosition || !ex.Value.IsString) return;
        string m = ex.Message;
        if (!m.StartsWith(Head, StringComparison.Ordinal)) return;
        int q = m.IndexOf(" to '", StringComparison.Ordinal);
        int end = q < 0 ? -1 : m.IndexOf('\'', q + 5);
        if (end < 0) return;
        string bare = m.Substring(q + 5, end - q - 5);
        string? full = DebugLib.GlobalFuncName(state, fn);
        if (full is null || full == bare || !full.EndsWith("." + bare, StringComparison.Ordinal)) return;
        ex.Reword(m.Substring(0, q + 5) + full + m.Substring(end));
    }

    private static LuaValue[] Protected(LuaState state, ReadOnlySpan<LuaValue> args, bool xpcall)
    {
        CheckProtectedArgs(xpcall, args);
        var co = state.Running;
        int fixedArgs = xpcall ? 2 : 1;
        LuaValue handler = xpcall ? args[1] : default;
        int level = co.CallTop;
        int depth = co.Depth;
        int savedHost = co.HostProtectDepth;
        bool savedHostX = co.HostProtectX;
        try
        {
            if (args[0].Ref is BuiltinFunction)
            {
                co.HostProtectDepth = depth;
                co.HostProtectX = xpcall;
            }
            LuaValue[] res = Interpreter.CallValues(state, args[0], args[fixedArgs..]);
            var ret = new LuaValue[res.Length + 1];
            ret[0] = LuaValue.FromBoolean(true);
            res.CopyTo(ret, 1);
            return ret;
        }
        catch (Exception e) when (Interpreter.IsLuaError(e))
        {
            LuaException ex = Interpreter.AsLuaException(e);
            QualifyArgError(state, args[0], ex);
            LuaValue value = ex.Value;
            if (xpcall) value = Interpreter.RunHandler(state, co, handler, value);
            co.Depth = depth;
            co.FaultLine = 0; // the fault line belongs to frames that are gone now
            co.Close(level);
            co.CallTop = level;
            return Rets.Two(LuaValue.FromBoolean(false), value);
        }
        finally
        {
            co.HostProtectDepth = savedHost;
            co.HostProtectX = savedHostX;
        }
    }
}
