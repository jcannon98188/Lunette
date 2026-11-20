using Xunit;

namespace Lunette.Tests;

/// <summary>
/// A state built from <see cref="LuaStateOptions"/> never constructs a withheld library: nothing reachable from its globals,
/// registry, <c>package.loaded</c> or per-type metatables is a function the allowlist did not name.
/// </summary>
public class SandboxReachabilityTests
{
    /// <summary>Every function reachable from the state's roots, as the dotted path first found (functions reached only as values keep the path of their holder).</summary>
    private static SortedSet<string> ReachableFunctions(LuaState state)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(string path, LuaValue v)
        {
            if (v.Ref is LuaFunction) { found.Add(path); return; }
            if (v.Ref is not LuaTable t || !seen.Add(t)) return;
            if (t.Metatable is not null) Walk(path + "<mt>", LuaValue.FromTable(t.Metatable));
            LuaValue key = default;
            while (t.Next(key, out LuaValue k, out LuaValue val))
            {
                key = k;
                string name = k.Ref is byte[] raw ? new LuaString(raw).ToString() : "?";
                Walk(path.Length == 0 ? name : path + "." + name, val);
            }
        }
        Walk("", LuaValue.FromTable(state.Globals));
        if (state.Registry is { } reg) Walk("<registry>", LuaValue.FromTable(reg));
        if (state.Package is { } pkg) Walk("<package>", LuaValue.FromTable(pkg));
        if (state.StringMetatable is { } sm) Walk("<string-mt>", LuaValue.FromTable(sm));
        foreach (LuaValue sample in new[] { default, LuaValue.FromBoolean(true), LuaValue.FromInteger(1) })
            if (state.TypeMetatable(sample) is { } mt) Walk("<type-mt>", LuaValue.FromTable(mt));
        return found;
    }

    private static LuaState Build(params string[] allow) => new(new LuaStateOptions().Allow(allow));

    private static string Run(LuaState state, string source) =>
        new LuaString((byte[])state.Call(state.Load(source)).Ref!).ToString();

    [Fact]
    public void A_minimal_state_reaches_only_the_allowlisted_functions()
    {
        using var state = Build("print", "pairs");
        Assert.Equal(["pairs", "print"], ReachableFunctions(state));
        Assert.Null(state.Package);
        Assert.Null(state.Registry);
        Assert.Null(state.StringMetatable);
    }

    [Fact]
    public void A_library_allowlist_reaches_that_library_and_nothing_else()
    {
        using var state = Build("math");
        SortedSet<string> reachable = ReachableFunctions(state);
        Assert.NotEmpty(reachable);
        Assert.All(reachable, p => Assert.StartsWith("math.", p, StringComparison.Ordinal));
        Assert.Null(state.Package);
    }

    [Fact]
    public void The_string_metatable_exists_only_with_the_string_library()
    {
        using var withString = Build("string");
        Assert.NotNull(withString.StringMetatable);
        Assert.All(ReachableFunctions(withString), p => Assert.StartsWith("string.", p, StringComparison.Ordinal));
        using var without = Build("print");
        Assert.Null(without.StringMetatable);
    }

    [Fact]
    public void Require_never_reaches_package_loaded()
    {
        using var state = new LuaState(new LuaStateOptions().Allow("require", "type").AddModule("m", _ => LuaValue.FromInteger(7)));
        Assert.Null(state.Package);
        Assert.Equal(["require", "type"], ReachableFunctions(state));
    }

    [Theory]
    [InlineData("bt")]
    [InlineData("t")]
    [InlineData("b")]
    public void Load_rejects_a_binary_chunk_in_a_sandboxed_state(string mode)
    {
        using var state = Build("load", "type");
        string error = Run(state, $"local f, e = load('\\27Lua\\84\\0', 'x', '{mode}') return e");
        Assert.Contains(mode == "t" ? "binary chunk" : "precompiled chunks are not supported", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_a_binary_chunk_in_the_full_state()
    {
        using var state = new LuaState();
        Assert.Contains("precompiled chunks are not supported", Run(state, "local f, e = load('\\27Lua\\84\\0') return e"), StringComparison.Ordinal);
    }

    [Fact]
    public void Dofile_and_loadfile_are_confined_by_the_platform_the_host_supplies()
    {
        string path = Path.Combine(Path.GetTempPath(), "lunette-sandbox-" + Guid.NewGuid().ToString("N") + ".lua");
        File.WriteAllText(path, "return 42");
        try
        {
            string lua = path.Replace("\\", "/");
            using var denied = new LuaState(new LuaStateOptions { Platform = new DefaultLuaPlatform(PlatformCapabilities.None) }.Allow("dofile", "loadfile"));
            Assert.Contains("cannot open", Run(denied, $"local f, e = loadfile('{lua}') return e"), StringComparison.Ordinal);
            Assert.Throws<LuaException>(() => denied.Call(denied.Load($"return dofile('{lua}')")));
            using var allowed = new LuaState(new LuaStateOptions { Platform = new DefaultLuaPlatform() }.Allow("dofile"));
            Assert.Equal(42, allowed.Call(allowed.Load($"return dofile('{lua}')")).AsInteger);
        }
        finally { File.Delete(path); }
    }
}
