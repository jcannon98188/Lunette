namespace Lunette;

/// <summary>Process-wide string-engine settings read once from the environment (CI variants).</summary>
internal static class StringConfig
{
    public static readonly bool ConstantHash = Environment.GetEnvironmentVariable("LUNETTE_CONSTANT_HASH") == "1";
}

/// <summary>
/// Per-state best-effort intern cache for short strings (ADR 0005): a bounded, strong, 4-way set-associative
/// table. Interning only saves allocations and speeds up pointer-first equality; correctness never depends
/// on it, because equality falls back to length and bytes.
/// </summary>
[LuaStringPath]
internal sealed class StringCache
{
    public const int DefaultCapacity = 4096;
    private const int Ways = 4;

    private readonly LuaString[] _slots;
    private readonly int _ways;
    private readonly int _setMask;
    private uint _victim;

    public StringCache(int capacity = DefaultCapacity)
    {
        if (capacity < 1) capacity = 1;
        _ways = Math.Min(Ways, capacity);
        int sets = 1;
        while (sets * 2 * _ways <= capacity) sets *= 2;
        _setMask = sets - 1;
        _slots = new LuaString[sets * _ways];
    }

    public int Capacity => _slots.Length;

    /// <summary>Forgets every cached string; safe because interning is not an invariant.</summary>
    public void Clear() => Array.Clear(_slots);

    /// <summary>Returns the shared instance for short strings, or a fresh string for long ones.</summary>
    public LuaString Intern(ReadOnlySpan<byte> data)
    {
        if (data.Length > LuaString.MaxShortLength) return LuaString.Create(data);
        if (data.Length == 0) return LuaString.Empty;
        int set = (LuaString.ComputeHash(data) & _setMask) * _ways;
        if (Find(set, data, out LuaString found)) return found;
        LuaString created = LuaString.Create(data);
        Insert(set, created);
        return created;
    }

    /// <summary>Interns an already built string: returns the cached equal one if present, else caches this one.</summary>
    public LuaString Intern(LuaString s)
    {
        if (s.Length > LuaString.MaxShortLength || s.Length == 0) return s;
        int set = (LuaString.ComputeHash(s.Bytes) & _setMask) * _ways;
        if (Find(set, s.Bytes, out LuaString found)) return found;
        Insert(set, s);
        return s;
    }

    public LuaString FromUtf16(ReadOnlySpan<char> text) => Intern(LuaString.FromUtf16(text));

    private bool Find(int set, ReadOnlySpan<byte> data, out LuaString found)
    {
        for (int w = 0; w < _ways; w++)
        {
            LuaString s = _slots[set + w];
            if (!s.IsDefault && s.Length == data.Length && s.Bytes.SequenceEqual(data))
            {
                found = s;
                return true;
            }
        }
        found = default;
        return false;
    }

    private void Insert(int set, LuaString s) => _slots[set + (int)(_victim++ % (uint)_ways)] = s;
}
