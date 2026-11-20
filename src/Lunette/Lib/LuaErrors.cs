namespace Lunette;

/// <summary>Shared error constructors for library code, mirroring lauxlib.c (luaL_error, luaL_argerror, luaL_typeerror).</summary>
internal static class LuaErrors
{
    /// <summary>A library error; the calling Lua function's position is prefixed when the VM catches it.</summary>
    public static LuaException Error(string message) => new(message) { WantsPosition = true };
}
