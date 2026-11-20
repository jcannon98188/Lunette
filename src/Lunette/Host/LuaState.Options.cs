using Lunette.Lib;
using Lunette.Vm;

namespace Lunette;

// Deny-by-default construction (LuaStateOptions). Libraries are matched by the global name Lua code sees, so a library added
// later (io, os) is gated without any change here.
public sealed partial class LuaState
{
    /// <summary>
    /// A state whose globals are exactly <paramref name="options"/>' allowlist plus its replacements, whose <c>require</c> sees
    /// only host-registered modules, and whose <c>math.random</c> is seeded by the host rather than the clock.
    /// </summary>
    public LuaState(LuaStateOptions options) : this()
    {
        ArgumentNullException.ThrowIfNull(options);
        MathLib.Register(this, options.Seed);
        Prune(options);
        if (!options.IsAllowed("string")) StringMetatable = null;
        if (options.IsAllowed("require")) SetGlobal("require", LuaValue.FromObject(new BuiltinFunction(HostRequire(options))));
        foreach (var (name, function) in options.Replacements) Install(name, LuaValue.FromFunction(CreateFunction(function)));
        if (options.Limits is not null) SetLimits(options.Limits);
    }

    /// <summary>Removes every global that is not allowlisted.</summary>
    private void Prune(LuaStateOptions options)
    {
        var doomed = new List<LuaValue>();
        LuaValue key = default;
        while (Globals.Next(key, out LuaValue next, out _))
        {
            key = next;
            if (!(key.Ref is byte[] raw && options.IsAllowed(new LuaString(raw).ToString()))) doomed.Add(key);
        }
        foreach (LuaValue k in doomed) Globals.Set(k, default);
    }

    private void Install(string name, LuaValue value)
    {
        int dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            SetGlobal(name, value);
            return;
        }
        string libName = name.Substring(0, dot);
        if (GetGlobal(libName).Ref is not LuaTable lib)
            throw new ArgumentException($"Cannot replace '{name}': library '{libName}' is not allowlisted.", nameof(name));
        lib.Set(LuaValue.FromString(Strings.FromUtf16(name.Substring(dot + 1))), value);
    }

    /// <summary><c>require</c> over the host's module registrations only: no <c>package</c>, no searchers, no filesystem.</summary>
    private static BuiltinBody HostRequire(LuaStateOptions options)
    {
        var loaders = new Dictionary<string, Func<LuaState, LuaValue>>(options.Modules, StringComparer.Ordinal);
        var loaded = new Dictionary<string, LuaValue>(StringComparer.Ordinal);
        return (state, args) =>
        {
            string name = LibArgs.CheckString(state, "require", args, 0).ToString();
            if (loaded.TryGetValue(name, out LuaValue cached)) return [cached];
            if (!loaders.TryGetValue(name, out var loader)) throw LuaErrors.Error($"module '{name}' not found");
            LuaValue result = loader(state);
            if (result.IsNil) result = LuaValue.FromBoolean(true);
            loaded[name] = result;
            return [result];
        };
    }
}
