using Xunit;

namespace Lunette.Tests;

/// <summary>
/// SimGame embedding: deny-by-default globals, gated functions, host-seeded require and deterministic randomness.
/// The seam is the public host API (LuaStateOptions + LuaState) observed through Lua code.
/// </summary>
public class SimGameHostTests
{
    private static string Run(LuaState state, string source)
    {
        var sw = new StringWriter { NewLine = "\n" };
        state.Output = sw;
        state.Call(state.Load(source));
        return sw.ToString();
    }

    private static string Globals(LuaState state) =>
        Run(state, "local t = {} for k in pairs(_G) do t[#t+1] = k end table.sort(t) print(table.concat(t, ','))").TrimEnd();

    // ---- 1. deny by default ----

    [Fact]
    public void A_state_created_from_an_allowlist_exposes_nothing_else()
    {
        using var state = new LuaState(new LuaStateOptions().Allow("_G", "print", "pairs", "table", "string", "pcall"));
        Assert.Equal("_G,pairs,pcall,print,string,table", Globals(state));
    }

    [Fact]
    public void An_empty_allowlist_leaves_no_globals_at_all()
    {
        using var state = new LuaState(new LuaStateOptions());
        Assert.True(state.GetGlobal("print").IsNil);
        Assert.True(state.GetGlobal("string").IsNil);
        Assert.True(state.GetGlobal("require").IsNil);
        Assert.True(state.GetGlobal("package").IsNil);
        Assert.True(state.GetGlobal("math").IsNil);
    }

    [Fact]
    public void Withholding_the_string_library_also_removes_string_methods()
    {
        using var state = new LuaState(new LuaStateOptions().Allow("print", "pcall"));
        Assert.Equal("false\n", Run(state, "print((pcall(function() return ('x'):upper() end)))"));
    }

    [Fact]
    public void Allowed_libraries_work_normally()
    {
        using var state = new LuaState(new LuaStateOptions().Allow("print", "string", "math"));
        Assert.Equal("ABC\t3\n", Run(state, "print(('abc'):upper(), math.floor(3.7))"));
    }

    [Fact]
    public void The_package_table_cannot_be_allowlisted()
    {
        Assert.Throws<ArgumentException>(() => new LuaStateOptions().Allow("package"));
    }

    // ---- 2. gated functions, independently ----

    [Theory]
    [InlineData("dofile")]
    [InlineData("loadfile")]
    [InlineData("load")]
    [InlineData("collectgarbage")]
    [InlineData("coroutine")]
    public void Each_gated_name_can_be_withheld_independently(string withheld)
    {
        string[] gated = ["dofile", "loadfile", "load", "collectgarbage", "coroutine"];
        using var state = new LuaState(new LuaStateOptions().Allow(["print", .. gated.Where(g => g != withheld)]));
        foreach (string g in gated)
            Assert.Equal(g == withheld, state.GetGlobal(g).IsNil);
    }

    [Fact]
    public void A_gated_function_can_be_replaced_by_the_host()
    {
        var options = new LuaStateOptions().Allow("print", "load").Replace("dofile", (ref CallContext ctx) =>
        {
            ctx.Push(LuaValue.FromString("blocked"));
            return 1;
        });
        using var state = new LuaState(options);
        Assert.Equal("blocked\n", Run(state, "print(dofile('/etc/passwd'))"));
        Assert.False(state.GetGlobal("load").IsNil);
        Assert.True(state.GetGlobal("loadfile").IsNil);
    }

    [Fact]
    public void Replacing_load_leaves_the_other_gated_functions_untouched()
    {
        var options = new LuaStateOptions().Allow("print", "collectgarbage", "coroutine", "type")
            .Replace("load", (ref CallContext ctx) =>
            {
                ctx.Push(LuaValue.Nil);
                ctx.Push(LuaValue.FromString("load is disabled"));
                return 2;
            });
        using var state = new LuaState(options);
        Assert.Equal("nil\tload is disabled\nnumber\tfunction\n",
            Run(state, "print(load('return 1')) print(type(collectgarbage('count')), type(coroutine.wrap))"));
    }

    // ---- 3. host-seeded require ----

    [Fact]
    public void Require_resolves_only_host_registered_modules()
    {
        var options = new LuaStateOptions().Allow("print", "require", "pcall", "select")
            .AddModule("simgame.economy", _ =>
            {
                var t = new LuaTable();
                t.Set(LuaValue.FromString("version"), LuaValue.FromInteger(7));
                return LuaValue.FromTable(t);
            });
        using var state = new LuaState(options);
        Assert.Equal("7\ttrue\n", Run(state, "local e = require('simgame.economy') print(e.version, require('simgame.economy') == e)"));
        string err = Run(state, "print(select(2, pcall(require, 'simgame.combat')))");
        Assert.Contains("module 'simgame.combat' not found", err);
    }

    [Fact]
    public void Require_never_touches_the_filesystem_or_package()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lunette-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "evil.lua"), "return 'from disk'");
            string cwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir);
            try
            {
                using var state = new LuaState(new LuaStateOptions().Allow("print", "require", "pcall", "select"));
                Assert.Contains("module 'evil' not found", Run(state, "print(select(2, pcall(require, 'evil')))"));
                Assert.True(state.GetGlobal("package").IsNil);
            }
            finally { Directory.SetCurrentDirectory(cwd); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_module_loader_runs_once_and_each_state_has_its_own_modules()
    {
        int loads = 0;
        LuaStateOptions Make(int n) => new LuaStateOptions().Allow("print", "require")
            .AddModule("simgame.log", _ =>
            {
                loads++;
                var t = new LuaTable();
                t.Set(LuaValue.FromString("id"), LuaValue.FromInteger(n));
                return LuaValue.FromTable(t);
            });
        using var a = new LuaState(Make(1));
        using var b = new LuaState(Make(2));
        Assert.Equal("1\t1\n", Run(a, "print(require('simgame.log').id, require('simgame.log').id)"));
        Assert.Equal("2\n", Run(b, "print(require('simgame.log').id)"));
        Assert.Equal(2, loads);
    }

    [Fact]
    public void Require_is_absent_unless_allowlisted()
    {
        using var state = new LuaState(new LuaStateOptions().Allow("print").AddModule("m", _ => LuaValue.FromInteger(1)));
        Assert.True(state.GetGlobal("require").IsNil);
    }

    // ---- 4. randomness ----

    private static string Rolls(long seed)
    {
        using var state = new LuaState(new LuaStateOptions().Allow("print", "math").SeedRandom(seed));
        return Run(state, "print(math.random(1, 1000000), math.random(1, 1000000), math.random())");
    }

    [Fact]
    public void Math_random_is_deterministic_for_a_host_seed()
    {
        Assert.Equal(Rolls(12345), Rolls(12345));
        Assert.NotEqual(Rolls(12345), Rolls(54321));
    }

    [Fact]
    public void Randomseed_without_arguments_never_reads_the_clock()
    {
        using var state = new LuaState(new LuaStateOptions().Allow("print", "math").SeedRandom(99));
        Assert.Equal("true\n", Run(state, """
            local a = math.random(1, 1000000)
            math.randomseed()
            print(a == math.random(1, 1000000))
            """));
    }

    [Fact]
    public void Math_random_and_randomseed_can_be_replaced()
    {
        var options = new LuaStateOptions().Allow("print", "math", "type")
            .Replace("math.random", (ref CallContext ctx) => { ctx.Push(LuaValue.FromInteger(4)); return 1; })
            .Replace("math.randomseed", (ref CallContext ctx) => 0);
        using var state = new LuaState(options);
        Assert.Equal("4\t4\n", Run(state, "math.randomseed(1) print(math.random(1, 6), math.random())"));
        Assert.Equal("true\n", Run(state, "print(type(math.floor) == 'function')"));
    }

    [Fact]
    public void Replacing_a_member_of_a_withheld_library_is_an_error()
    {
        var options = new LuaStateOptions().Allow("print").Replace("math.random", (ref CallContext ctx) => 0);
        Assert.Throws<ArgumentException>(() => new LuaState(options));
    }

    [Fact]
    public void Limits_set_on_the_options_apply_to_the_state()
    {
        using var state = new LuaState(new LuaStateOptions { Limits = new LuaLimits { InstructionLimit = 10_000 } }.Allow("print"));
        Assert.Throws<LuaAbortException>(() => Run(state, "while true do end"));
    }
}
