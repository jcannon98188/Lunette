namespace Lunette;

/// <summary>
/// Deny-by-default construction options for an embedder that gives each script pack its own state (see
/// <see cref="LuaState(LuaStateOptions)"/>). Only the names passed to <see cref="Allow"/> (base functions such as
/// <c>print</c> or <c>load</c>, and libraries such as <c>math</c>, <c>io</c> or <c>os</c>) exist in the new state; the
/// <c>package</c> table never does, and <c>require</c> resolves only <see cref="AddModule"/> registrations.
/// </summary>
public sealed class LuaStateOptions
{
    private readonly HashSet<string> _allowed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HostFunction> _replacements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<LuaState, LuaValue>> _modules = new(StringComparer.Ordinal);
    private long? _seed1;
    private long _seed2;

    /// <summary>Sandbox limits applied to the new state; null leaves it unlimited.</summary>
    public LuaLimits? Limits { get; init; }

    /// <summary>Makes the named base functions and libraries present. Names are those Lua code sees (<c>"dofile"</c>, <c>"coroutine"</c>, <c>"io"</c>).</summary>
    public LuaStateOptions Allow(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            if (name == "package")
                throw new ArgumentException("'package' cannot be allowlisted: require is host-seeded and never touches package.", nameof(names));
            _allowed.Add(name);
        }
        return this;
    }

    /// <summary>
    /// Installs a host function in place of a global (<c>"dofile"</c>) or a member of an allowlisted library
    /// (<c>"math.random"</c>). A replaced global is present even if it is not allowlisted.
    /// </summary>
    public LuaStateOptions Replace(string name, HostFunction function)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(function);
        _replacements[name] = function;
        return this;
    }

    /// <summary>Registers a module <c>require(name)</c> may resolve. The loader runs on first use and its result is cached per state.</summary>
    public LuaStateOptions AddModule(string name, Func<LuaState, LuaValue> loader)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(loader);
        _modules[name] = loader;
        return this;
    }

    /// <summary>
    /// Seeds <c>math.random</c> deterministically. With a seed set, <c>math.randomseed()</c> without arguments returns to this seed
    /// instead of reading the clock. Without one the seed is (0, 0): a state made from options is never clock-seeded.
    /// </summary>
    public LuaStateOptions SeedRandom(long seed, long seed2 = 0)
    {
        _seed1 = seed;
        _seed2 = seed2;
        return this;
    }

    internal bool IsAllowed(string name) => _allowed.Contains(name);
    internal IReadOnlyDictionary<string, HostFunction> Replacements => _replacements;
    internal IReadOnlyDictionary<string, Func<LuaState, LuaValue>> Modules => _modules;
    internal (ulong, ulong) Seed => ((ulong)(_seed1 ?? 0), (ulong)_seed2);
}
