namespace Lunette;

/// <summary>
/// One level of the Lua call stack as captured where an uncaught error was raised (<see cref="LuaException.StackFrames"/>).
/// Innermost first. A dev-mode console reads these directly; the <c>debug</c> library does not have to be exposed to scripts.
/// </summary>
public sealed class LuaStackFrame
{
    internal LuaStackFrame(string? functionName, string source, int line, bool isNative)
    {
        FunctionName = functionName;
        Source = source;
        Line = line;
        IsNative = isNative;
    }

    /// <summary>The function's global or called-as name (for example <c>outer</c> or <c>string.rep</c>), or null for a main chunk or anonymous function.</summary>
    public string? FunctionName { get; }

    /// <summary>The chunk's display name (a chunk named <c>=mods/farm.lua</c> shows as <c>mods/farm.lua</c>), or <c>[C]</c> for a host function.</summary>
    public string Source { get; }

    /// <summary>The line being executed in this function; 0 for a host function.</summary>
    public int Line { get; }

    /// <summary>True for a host (non-Lua) function.</summary>
    public bool IsNative { get; }

    public override string ToString() => IsNative ? $"[C]: in {FunctionName ?? "?"}" : $"{Source}:{Line}: in {FunctionName ?? "main chunk or anonymous function"}";
}
