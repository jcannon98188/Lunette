namespace Lunette;

/// <summary>Shared error constructors for library code, mirroring lauxlib.c (luaL_error, luaL_argerror, luaL_typeerror).</summary>
internal static class LuaErrors
{
    /// <summary>A library error; the calling Lua function's position is prefixed when the VM catches it.</summary>
    public static LuaException Error(string message) => new(message) { WantsPosition = true };

    /// <summary>"bad argument #n to 'fn' (msg)" with a one-based argument number.</summary>
    public static LuaException ArgError(int n, string fn, string msg)
    {
        LuaException e = Error($"bad argument #{n} to '{fn}' ({msg})");
        e.ArgN = n;
        e.ArgMsg = msg;
        return e;
    }

    /// <summary>"bad argument #n to 'fn' (expected expected, got ...)" for a zero-based argument index.</summary>
    public static LuaException TypeError(ReadOnlySpan<LuaValue> args, int index, string fn, string expected) =>
        ArgError(index + 1, fn, $"{expected} expected, got {(index >= args.Length ? "no value" : Vm.Meta.ArgTypeName(args[index]))}");
}
