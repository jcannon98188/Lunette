namespace Lunette;

// Markers for the custom analyzers (Lunette.Analyzers, rules LD001-LD006). The analyzers match them by simple name.

/// <summary>Code on the interpreter's hot path: no allocation (LD001), no throw (LD002).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property)]
internal sealed class LuaHotPathAttribute : Attribute
{
}

/// <summary>
/// A VM dispatch-loop method. CI holds it to 70% of the JIT's MinOpts limits (jit-budgets.json) and, with
/// <c>AggressiveOptimization</c> on loop methods only, checks it is never jitted as MinOpts.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class LuaInterpreterLoopAttribute : Attribute
{
}

/// <summary>A method that exists to throw on behalf of hot-path code; exempt from LD002.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class LuaColdThrowAttribute : Attribute
{
}

/// <summary>String-library code that works on bytes only (LD004).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method)]
internal sealed class LuaStringPathAttribute : Attribute
{
}
