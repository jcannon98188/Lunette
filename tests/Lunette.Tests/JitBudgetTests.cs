using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Static half of the interpreter-loop JIT budget (spec ticket 17, decision 4): every method marked
/// [LuaInterpreterLoop] must stay within 70% of the retail JIT's MinOpts limits, so it keeps getting full
/// optimisation. Limits live in <c>jit-budgets.json</c>; each marked loop variant is budgeted separately.
/// The dynamic half (no MinOptJitted, no inlining cliff) runs in a child process: see JitProbeTests.
/// </summary>
public class JitBudgetTests
{
    internal static IReadOnlyList<MethodInfo> LoopMethods() =>
        typeof(LuaState).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<LuaInterpreterLoopAttribute>() is not null)
            .ToList();

    internal static Dictionary<string, int> Limits()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "jit-budgets.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, int>();
        foreach (var p in doc.RootElement.GetProperty("limits").EnumerateObject()) map[p.Name] = p.Value.GetInt32();
        return map;
    }

    [Fact]
    public void At_least_one_method_is_marked_as_an_interpreter_loop()
    {
        Assert.NotEmpty(LoopMethods());
        Assert.Contains(LoopMethods(), m => m.Name == "RunLoop");
    }

    [Fact]
    public void Interpreter_loop_methods_use_aggressive_optimization()
    {
        foreach (MethodInfo m in LoopMethods())
            Assert.True(m.MethodImplementationFlags.HasFlag(MethodImplAttributes.AggressiveOptimization), $"{m.Name} must be AggressiveOptimization");
    }

    [Fact]
    public void Only_interpreter_loop_methods_use_aggressive_optimization()
    {
        var offenders = typeof(LuaState).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.MethodImplementationFlags.HasFlag(MethodImplAttributes.AggressiveOptimization)
                        && m.GetCustomAttribute<LuaInterpreterLoopAttribute>() is null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}");
        Assert.Empty(offenders);
    }

    [Fact]
    public void Interpreter_loop_methods_stay_within_the_il_budget()
    {
        AllocationTests.RequireOptimized();
        if (Optimizer.Disabled) return; // Debug IL is larger; the budget is a Release-build contract.
        var limits = Limits();
        var failures = new List<string>();
        foreach (MethodInfo m in LoopMethods())
        {
            IlMetrics x = IlWalker.Measure(m);
            void Check(string what, int actual, string key)
            {
                if (actual > limits[key]) failures.Add($"{m.Name}: {what} {actual} exceeds budget {limits[key]}");
            }
            Check("IL bytes", x.Bytes, "ilBytes");
            Check("IL instructions", x.Instructions, "ilInstructions");
            Check("basic blocks", x.BasicBlocks, "basicBlocks");
            Check("local refs", x.LocalRefs, "localRefs");
            Check("IL locals", x.Locals, "ilLocals");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    [Fact]
    public void The_il_walker_counts_a_known_method()
    {
        if (Optimizer.Disabled) return; // Debug IL has extra locals; the expected shape is a Release-build one.
        // Independent expectation: a hand-checked tiny method (see IlWalkerSubject) has a known shape.
        IlMetrics m = IlWalker.Measure(typeof(IlWalkerSubject).GetMethod(nameof(IlWalkerSubject.Sum))!);
        Assert.True(m.Instructions > 5 && m.Instructions < 60);
        Assert.True(m.BasicBlocks >= 3);
        Assert.True(m.LocalRefs >= 4);
        Assert.Equal(2, m.Locals);
    }

    [Fact]
    public void The_il_walker_sees_the_real_loop_as_large()
    {
        IlMetrics m = IlWalker.Measure(typeof(LuaState).Assembly.GetType("Lunette.Vm.Interpreter")!
            .GetMethod("RunLoop", BindingFlags.NonPublic | BindingFlags.Static)!);
        Assert.True(m.Instructions > 100, "walker should see a large loop");
    }
}

internal static class Optimizer
{
    public static bool Disabled =>
        typeof(LuaState).Assembly.GetCustomAttribute<System.Diagnostics.DebuggableAttribute>()?.IsJITOptimizerDisabled == true;
}

internal static class IlWalkerSubject
{
    public static int Sum(int n)
    {
        int s = 0;
        for (int i = 0; i < n; i++) s += i;
        return s;
    }
}
