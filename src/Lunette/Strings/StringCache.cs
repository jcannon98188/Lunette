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

    /// <summary>A fresh state starts with this many sets (256 slots, 2 KB) and doubles on demand up to the configured capacity.</summary>
    private const int InitialSets = 64;

    private LuaString[] _slots;
    private readonly int _ways;
    private readonly int _maxSets;
    private int _setMask;
    private uint _victim;

    public StringCache(int capacity = DefaultCapacity)
    {
        if (capacity < 1) capacity = 1;
        _ways = Math.Min(Ways, capacity);
        int sets = 1;
        while (sets * 2 * _ways <= capacity) sets *= 2;
        _maxSets = sets;
        int initial = Math.Min(sets, InitialSets);
        _setMask = initial - 1;
        _slots = new LuaString[initial * _ways];
    }

    /// <summary>The slots currently allocated (grows towards the configured capacity).</summary>
    public int Capacity => _slots.Length;

    /// <summary>Forgets every cached string; safe because interning is not an invariant.</summary>
    public void Clear() => Array.Clear(_slots);

    /// <summary>Returns the shared instance for short strings, or a fresh string for long ones.</summary>
    public LuaString Intern(ReadOnlySpan<byte> data)
    {
        if (data.Length > LuaString.MaxShortLength) return LuaString.Create(data);
        if (data.Length == 0) return LuaString.Empty;
        if (data.Length == 1) return LuaString.SingleByte(data[0]);
        int hash = LuaString.ComputeHash(data);
        int set = (hash & _setMask) * _ways;
        if (Find(set, hash, data, out LuaString found)) return found;
        LuaString created = LuaString.CreateHashed(data, hash);
        Insert(hash, created);
        return created;
    }

    /// <summary>Interns an already built string: returns the cached equal one if present, else caches this one.</summary>
    public LuaString Intern(LuaString s)
    {
        if (s.Length > LuaString.MaxShortLength || s.Length == 0) return s;
        int hash = s.Hash;
        int set = (hash & _setMask) * _ways;
        if (Find(set, hash, s.Bytes, out LuaString found)) return found;
        Insert(hash, s);
        return s;
    }

    public LuaString FromUtf16(ReadOnlySpan<char> text) => Intern(LuaString.FromUtf16(text));

    private bool Find(int set, int hash, ReadOnlySpan<byte> data, out LuaString found)
    {
        for (int w = 0; w < _ways; w++)
        {
            LuaString s = _slots[set + w];
            // Cached strings always carry their hash (set at creation or by Intern(LuaString)), so it is a cheap reject.
            if (!s.IsDefault && s.Length == data.Length && s.CachedHash == hash && s.Bytes.SequenceEqual(data))
            {
                found = s;
                return true;
            }
        }
        found = default;
        return false;
    }

    private void Insert(int hash, LuaString s)
    {
        while (true)
        {
            int set = (hash & _setMask) * _ways;
            for (int w = 0; w < _ways; w++)
            {
                if (_slots[set + w].IsDefault)
                {
                    _slots[set + w] = s;
                    return;
                }
            }
            if (_setMask + 1 >= _maxSets)
            {
                _slots[set + (int)(_victim++ % (uint)_ways)] = s;
                return;
            }
            Grow(); // a full set while the table is below its capacity: double it instead of evicting
        }
    }

    private void Grow()
    {
        LuaString[] old = _slots;
        int newMask = (_setMask + 1) * 2 - 1;
        var next = new LuaString[(newMask + 1) * _ways];
        for (int i = 0; i < old.Length; i++)
        {
            LuaString s = old[i];
            if (s.IsDefault) continue;
            int set = (s.CachedHash & newMask) * _ways;
            for (int w = 0; w < _ways; w++)
            {
                if (next[set + w].IsDefault)
                {
                    next[set + w] = s;
                    break;
                }
            }
        }
        _slots = next;
        _setMask = newMask;
    }
}
