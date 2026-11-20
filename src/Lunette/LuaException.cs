namespace Lunette;

/// <summary>A Lua runtime or compile error.</summary>
public sealed class LuaException(string message) : Exception(message);
