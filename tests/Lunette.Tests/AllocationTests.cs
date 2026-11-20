using System.Text.Json;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Exact-byte allocation budgets for hot paths (spec ticket 17, decision 3). Allocation only, never time.
/// Each scenario defines a global function <c>work(n)</c> that does <c>n</c> iterations of the path under test; the
/// per-iteration cost is (bytes for 2N iterations - bytes for N iterations) / N, measured on a warmed state, so
/// one-off costs (compiling the driver chunk, stack growth) cancel. Budgets live in <c>alloc-budgets.json</c> and
/// may only be loosened in a PR that cites before/after numbers.
/// </summary>
public class AllocationTests
{
    private const int N = 20000;

    private static readonly Dictionary<string, long> Budgets = LoadBudgets();

    private static Dictionary<string, long> LoadBudgets()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "alloc-budgets.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, long>();
        foreach (var p in doc.RootElement.GetProperty("budgets").EnumerateObject())
        {
            if (p.Value.TryGetProperty("bytesPerIteration", out var exact)) { map[p.Name] = exact.GetInt64(); continue; }
            // One result string: byte[] object header + length word (24) plus the 8-byte Lua header and the bytes, 8-aligned.
            long len = p.Value.GetProperty("resultLength").GetInt64();
            map[p.Name] = 24 + ((8 + len + 7) & ~7L) + p.Value.GetProperty("excessBytes").GetInt64();
        }
        return map;
    }

    internal static long MeasurePerIteration(string setup, int n = N)
    {
        var state = new LuaState { Output = TextWriter.Null };
        state.DoString(setup);
        string small = $"work({n})", large = $"work({2 * n})";
        // Warm: JIT, caches, stack growth.
        for (int i = 0; i < 3; i++) { state.DoString(large); }
        long a = Bytes(state, small), b = Bytes(state, large);
        long a2 = Bytes(state, small), b2 = Bytes(state, large);
        // Take the minimum of two samples to discard stray runtime allocations (tiering, event sources).
        // Allocation is accounted in allocation-context quanta (~8 KB), so large n keeps the rounding error under a byte.
        return (Math.Min(b - a, b2 - a2) + n / 2) / n;
    }

    private static long Bytes(LuaState state, string chunk)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        state.DoString(chunk);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    internal static void RequireOptimized()
    {
        bool optimized = !typeof(LuaState).Assembly.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false)
            .Cast<System.Diagnostics.DebuggableAttribute>().Any(d => d.IsJITOptimizerDisabled);
        if (!optimized && Environment.GetEnvironmentVariable("LUNETTE_REQUIRE_RELEASE") == "1")
            throw new InvalidOperationException("allocation and JIT-budget tests must run against a Release build");
    }

    private static bool Skip()
    {
        RequireOptimized();
        // Debug builds allocate differently (no inlining, extra locals); budgets are Release-only.
        return typeof(LuaState).Assembly.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false)
            .Cast<System.Diagnostics.DebuggableAttribute>().Any(d => d.IsJITOptimizerDisabled);
    }

    public static TheoryData<string, string> Scenarios => new()
    {
        { "arithmetic-loop", "function work(n) local s = 0 for i = 1, n do s = s + i * 2 - (i // 3) + i % 7 end return s end" },
        { "lua-call-return", "local function id(x) return x end function work(n) local s = 0 for i = 1, n do s = s + id(i) end return s end" },
        { "table-array-get-set", "local t = {1, 2, 3, 4, 5, 6, 7, 8} function work(n) for i = 1, n do local k = i % 8 + 1 t[k] = t[k] + 1 end end" },
        { "table-hash-get-set", "local t = {a = 1, b = 2, c = 3} function work(n) for i = 1, n do t.a = t.b + t.c t.c = i end end" },
        { "pcall-ok", "local function f(x) return x end function work(n) for i = 1, n do pcall(f, i) end end" },
        { "pcall-error", "local e = {} local function f() error(e) end function work(n) for i = 1, n do pcall(f) end end" },
        { "string-sub", "local s = string.rep('abcdef', 20) function work(n) for i = 1, n do local x = s:sub(10, 60) end end" },
        { "string-format-d", "local fmt = string.format function work(n) for i = 1, n do local x = fmt('%d', 123456) end end" },
        { "string-rep", "local rep = string.rep function work(n) for i = 1, n do local x = rep('ab', 30) end end" },
        { "string-gsub", "local s = string.rep('abc', 20) function work(n) for i = 1, n do local x = s:gsub('b', 'x') end end" },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Hot_path_allocates_exactly_its_budget(string name, string setup)
    {
        if (Skip()) return;
        long expected = Budgets[name];
        long actual = MeasurePerIteration(setup);
        Assert.True(actual == expected, $"{name}: allocated {actual} bytes/iteration, budget is {expected}. Loosen alloc-budgets.json only in a PR citing before/after numbers.");
    }

    [Fact]
    public void Every_scenario_has_a_budget_and_every_budget_a_scenario()
    {
        var names = Scenarios.Select(r => (string)r[0]).OrderBy(x => x).ToArray();
        Assert.Equal(names, Budgets.Keys.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Harness_detects_an_allocation_regression()
    {
        if (Skip()) return;
        // A loop that allocates a table per iteration must register as non-zero, proving the meter can fail CI.
        long actual = MeasurePerIteration("function work(n) for i = 1, n do local t = {} end end");
        Assert.True(actual > 0, "allocation meter did not see a per-iteration table allocation");
    }
}
