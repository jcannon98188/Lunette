using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Lunette.Compiler;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Ticket 56: the compiler's constant and name lookup uses a lazily built hash index once a function has many entries.
/// The index must reproduce the linear scan exactly (same indexes, same first-match rule), so a golden digest of the
/// compiled constants, names and code of a corpus is pinned, and big chunks must compile in bounded time.
/// </summary>
public class CompilerConstantIndexTests
{
    private static Proto Compile(string source) => LuaCompiler.Compile(source, "=test");

    private static void Dump(Proto p, StringBuilder sb)
    {
        sb.Append("P ").Append(p.Code.Length).Append('\n');
        foreach (var k in p.Constants)
        {
            if (k.IsNil) sb.Append("nil");
            else if (k.IsInteger) sb.Append('i').Append(k.Bits);
            else if (k.IsFloat) sb.Append('f').Append(k.Bits);
            else if (k.IsBoolean) sb.Append('b').Append(k.AsBooleanBit);
            else if (k.Ref is byte[] s) sb.Append('s').Append(Convert.ToHexString(new LuaString(s).Bytes));
            else sb.Append('?');
            sb.Append(',');
        }
        sb.Append('\n').Append(string.Join('|', p.Names)).Append('\n');
        foreach (var i in p.Code) sb.Append(i.Raw).Append(' ');
        sb.Append('\n');
        foreach (var c in p.Protos) Dump(c, sb);
    }

    private static string Digest(string source)
    {
        var sb = new StringBuilder();
        Dump(Compile(source), sb);        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static IEnumerable<string> Corpus()
    {
        yield return "return 1, 1.0, -0.0, 0.0, 0/0, 2^53, 1e300, 'a', 'a', \"a\" .. 'b', nil, true, false";
        yield return "local t = {1, 1.0, 2, 2.0, -0.0, 0.0, 'x', 'x', 'y', [1]=1, [1.0]=2, x=1, x=2, y=3} return t.x, t.y, t.z";
        yield return "for k, v in pairs({}) do local x <close> = nil end for i = 1, 10 do end for i = 1.0, 10 do end";
        yield return "local a <const> = 'k' local b <const> = 3 return a, b, a .. b, #a";
        var sb = new StringBuilder("local t = {");
        for (int i = 0; i < 700; i++) sb.Append(i % 3 == 0 ? $"{i}," : i % 3 == 1 ? $"{i}.5," : $"'s{i % 90}',");
        for (int i = 0; i < 300; i++) sb.Append($"k{i % 120} = {i % 50}, ");
        sb.Append("} return t");
        yield return sb.ToString();
        sb.Clear();
        for (int i = 0; i < 400; i++) sb.Append($"g{i % 150} = {i % 77} + 'q{i % 60}' x{i % 200} = x{(i + 1) % 200} ");
        yield return sb.ToString();
        sb.Clear();
        for (int i = 0; i < 90; i++) sb.Append($"do local c{i} <close> = nil end for k{i}, v in pairs(t) do end ");
        sb.Append("return 0/0, 1, 1.0, -0.0, 0.0, 70000, 70000.0, 1, 2, 3");
        yield return sb.ToString();
        sb.Clear();
        for (int i = 0; i < 70000; i++) sb.Append($"x = {i}.25 ");
        yield return sb.ToString();
        sb.Clear();
        for (int i = 0; i < 300; i++) sb.Append($"function f{i}() return {i}, 'a{i}', t.a{i % 10}, t['b{i % 7}'] end ");
        yield return sb.ToString();
    }

    // Pinned from the linear-scan compiler (before ticket 56).
    private const string GoldenDigest = "C103911303913C5109B4D7BBA5EFA7EAAE2406807574542FDA6612119A6D9EF3";

    [Fact]
    public void Constants_names_and_code_match_the_linear_scan_compiler()
    {
        var all = new StringBuilder();
        foreach (string src in Corpus()) all.Append(Digest(src)).Append('\n');
        if (Environment.GetEnvironmentVariable("LW56_OUT") is { } outPath) File.WriteAllText(outPath, all.ToString());
        Assert.Equal(GoldenDigest, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString()))));
    }

    [Fact]
    public void Number_constants_stay_distinct_by_type_and_bits()
    {
        var p = Compile("return 1, 1.0, -0.0, 0.0, 0/0, 'a', 'a', 1, 1.0, -0.0, 0.0");
        var k = p.Constants;
        Assert.Contains(k, v => v.IsInteger && v.Bits == 1);
        Assert.Contains(k, v => v.IsFloat && v.AsFloat == 1.0);
        Assert.Equal(1, k.Count(v => v.IsInteger && v.Bits == 1));
        Assert.Equal(1, k.Count(v => v.IsFloat && v.AsFloat == 1.0));
        Assert.Equal(1, k.Count(v => v.IsFloat && v.AsFloat == 0.0 && double.IsNegative(v.AsFloat)));
        Assert.Equal(1, k.Count(v => v.IsFloat && v.AsFloat == 0.0 && !double.IsNegative(v.AsFloat)));
    }

    [Fact]
    public void Dedup_rules_hold_past_the_index_threshold()
    {
        // 100 filler numbers push the function well past the lazy-index threshold before the cases under test.
        var sb = new StringBuilder("local t = {");
        for (int i = 0; i < 100; i++) sb.Append($"{i}.5,");
        sb.Append("1, 1.0, -0.0, 0.0, 1, 1.0, -0.0, 0.0} return gname, gname, gname");
        var k = Compile(sb.ToString()).Constants;
        Assert.Equal(1, k.Count(v => v.IsInteger && v.Bits == 1));
        Assert.Equal(1, k.Count(v => v.IsFloat && v.AsFloat == 1.0));
        Assert.Equal(1, k.Count(v => v.IsFloat && v.AsFloat == 0.0 && double.IsNegative(v.AsFloat)));
        Assert.Equal(1, k.Count(v => v.IsFloat && v.AsFloat == 0.0 && !double.IsNegative(v.AsFloat)));
        Assert.Equal(1, k.Count(v => v.Ref is byte[] s && new LuaString(s).ToString() == "gname"));
    }

    [Fact]
    public void A_chunk_with_tens_of_thousands_of_distinct_constants_compiles_in_bounded_time()
    {
        var sb = new StringBuilder("return {");
        for (int i = 0; i < 40000; i++) sb.Append($"{{{i}.5, 'name{i}', k{i} = {i}}},");
        sb.Append('}');
        var sw = Stopwatch.StartNew();
        var p = Compile(sb.ToString());
        sw.Stop();
        Assert.True(p.Constants.Length > 80000);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"compile took {sw.Elapsed}");
    }

    [Fact]
    public void Loaded_data_chunk_returns_its_values()
    {
        var state = new LuaState();
        state.DoString("""
            local parts = {"return {"}
            for i = 1, 3000 do parts[#parts + 1] = string.format("{%d, %d.5, 's%d', k%d = %d},", i, i, i, i, i) end
            parts[#parts + 1] = "}"
            local t = assert(load(table.concat(parts)))()
            assert(#t == 3000 and t[3000][1] == 3000 and t[17][2] == 17.5 and t[17][3] == "s17" and t[17].k17 == 17)
            """);
    }

    [Fact]
    public void A_long_load_is_stopped_by_the_instruction_budget()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 2_000 });
        var e = Assert.Throws<LuaAbortException>(() => state.DoString("""
            local s = string.rep("x = 1 ", 400000)
            load(s)
            """));
        Assert.Equal(LuaAbortKind.InstructionBudget, e.Kind);
    }

    [Fact]
    public void A_long_load_sees_an_interrupt_request()
    {
        var state = new LuaState();
        state.DoString("src = string.rep('x = 1 ', 2000000)");
        state.RequestInterrupt();
        state.DoString("res = select(2, load(src))");
        Assert.Contains("interrupted", state.GetGlobal("res").ToString());
    }

    [Fact]
    public void Small_loads_still_work_under_a_tight_budget()
    {
        var state = new LuaState(new LuaLimits { InstructionLimit = 2_000 });
        state.DoString("for i = 1, 100 do assert(load('return 1 + 2'))() end");
    }
}
