using System.Numerics;
using System.Runtime.CompilerServices;

namespace Lunette;

/// <summary>Metamethod events, in PUC order: the first six keep their absence cached in <see cref="LuaTable.Flags"/>.</summary>
internal enum TagMethod
{
    Index, NewIndex, Gc, Mode, Len, Eq,
    Add, Sub, Mul, Mod, Pow, Div, IDiv,
    BAnd, BOr, BXor, Shl, Shr, Unm, BNot,
    Lt, Le, Concat, Call, Close,
}

internal static class TagMethods
{
    /// <summary>Number of events whose absence is cached in the metatable's flags.</summary>
    public const int FastCount = 6;

    private static readonly string[] Spelling =
    [
        "__index", "__newindex", "__gc", "__mode", "__len", "__eq",
        "__add", "__sub", "__mul", "__mod", "__pow", "__div", "__idiv",
        "__band", "__bor", "__bxor", "__shl", "__shr", "__unm", "__bnot",
        "__lt", "__le", "__concat", "__call", "__close",
    ];

    public static readonly LuaValue[] Names = BuildNames();

    private static LuaValue[] BuildNames()
    {
        var names = new LuaValue[Spelling.Length];
        for (int i = 0; i < names.Length; i++) names[i] = LuaValue.FromString(Spelling[i]);
        return names;
    }

    public static string Name(TagMethod e) => Spelling[(int)e];
}

/// <summary>
/// PUC-faithful table: an array part sized by the "more than half used" rule plus a node hash part using
/// chained scatter with Brent's variation. Matching the sizing rule makes <c>#t</c> on tables with holes
/// identical to the reference binary. Hash-part iteration order is not specified.
/// </summary>
public sealed class LuaTable
{
    private const int MaxABits = 31;

    private struct Node
    {
        public LuaValue Key;
        public LuaValue Value;
        /// <summary>Offset to the next node in this collision chain; 0 ends it.</summary>
        public int Next;
    }

    private LuaValue[] _arr = [];
    /// <summary>PUC's 'alimit': a hint for <c>#t</c> that is at most the real array size.</summary>
    private int _limit;
    /// <summary>Empty means PUC's shared dummy node.</summary>
    private Node[] _nodes = [];
    private int _lastFree;

    public LuaTable? Metatable;
    /// <summary>Bit e set means metamethod e is known to be absent from this table (used when it is a metatable).</summary>
    internal byte Flags;

    public LuaTable()
    {
    }

    public LuaTable(int arraySize, int hashSize)
    {
        if (arraySize != 0 || hashSize != 0) Resize(arraySize, hashSize);
    }

    // ---- metamethod cache ----

    /// <summary>Looks the metamethod up in this table (as a metatable), caching absence of the common events.</summary>
    internal LuaValue GetTagMethod(TagMethod e)
    {
        if ((int)e < TagMethods.FastCount)
        {
            byte bit = (byte)(1 << (int)e);
            if ((Flags & bit) != 0) return default;
            LuaValue v = Get(TagMethods.Names[(int)e]);
            if (v.IsNil) Flags |= bit;
            return v;
        }
        return Get(TagMethods.Names[(int)e]);
    }

    internal bool AbsentTagMethod(TagMethod e) => GetTagMethod(e).IsNil;

    // ---- hashing ----

    private static int CeilLog2(uint x) => x <= 1 ? 0 : BitOperations.Log2(x - 1) + 1;

    private int MainPosition(in LuaValue key)
    {
        int size = _nodes.Length;
        if (key.IsInteger)
        {
            ulong ui = (ulong)key.Bits;
            return (int)(ui % (ulong)((size - 1) | 1));
        }
        if (key.IsFloat) return HashMod(HashFloat(key.AsFloat), size);
        if (key.IsBoolean) return (key.AsBooleanBit & (size - 1));
        if (key.Ref is byte[] str) return HashMod((uint)new LuaString(str).Hash, size);
        return HashMod((uint)key.Ref!.GetHashCode(), size);
    }

    private static int HashMod(uint h, int size) => (int)(h % (uint)((size - 1) | 1));

    private static uint HashFloat(double n)
    {
        if (!double.IsFinite(n)) return 0;
        if (n == 0) return 0;
        int e = Math.ILogB(n) + 1;
        double m = Math.ScaleB(n, -e) * 2147483648.0;
        if (!(m >= -9223372036854775808.0 && m < 9223372036854775808.0)) return 0;
        long ni = (long)m;
        uint u = unchecked((uint)e + (uint)ni);
        return u <= int.MaxValue ? u : ~u;
    }

    private static bool KeysEqual(in LuaValue a, in LuaValue b) =>
        a.Bits == b.Bits && (ReferenceEquals(a.Ref, b.Ref)
            || (a.Ref is byte[] x && b.Ref is byte[] y && new LuaString(x).Equals(new LuaString(y))));

    /// <summary>Turns a float key with an integer value into an integer key.</summary>
    private static LuaValue Normalize(LuaValue key)
    {
        if (key.IsFloat)
        {
            double d = key.AsFloat;
            if (d >= -9223372036854775808.0 && d < 9223372036854775808.0 && Math.Floor(d) == d)
                return LuaValue.FromInteger((long)d);
        }
        return key;
    }

    // ---- lookup ----

    /// <summary>Array index of integer key <paramref name="k"/> or -1; may move the length hint like PUC's luaH_getint.</summary>
    [LuaHotPath]
    private int ArrayIndex(long k)
    {
        ulong idx = (ulong)k - 1;
        if (idx < (ulong)_limit) return (int)idx;
        if (_limit != _arr.Length && idx < (ulong)_arr.Length)
        {
            _limit = (int)k;
            return (int)idx;
        }
        return -1;
    }

    [LuaHotPath]
    private int FindNodeInt(long k)
    {
        Node[] nodes = _nodes;
        if (nodes.Length == 0) return -1;
        int n = (int)((ulong)k % (ulong)((nodes.Length - 1) | 1));
        while (true)
        {
            ref Node nd = ref nodes[n];
            if (nd.Key.IsInteger && nd.Key.Bits == k) return n;
            if (nd.Next == 0) return -1;
            n += nd.Next;
        }
    }

    private int FindNode(in LuaValue key)
    {
        if (_nodes.Length == 0) return -1;
        int n = MainPosition(key);
        while (true)
        {
            ref Node nd = ref _nodes[n];
            if (KeysEqual(nd.Key, key)) return n;
            if (nd.Next == 0) return -1;
            n += nd.Next;
        }
    }

    [LuaHotPath]
    public LuaValue GetInt(long k)
    {
        int i = ArrayIndex(k);
        if (i >= 0) return _arr[i];
        int n = FindNodeInt(k);
        return n >= 0 ? _nodes[n].Value : default;
    }

    [LuaHotPath]
    public LuaValue Get(LuaValue key)
    {
        if (key.IsInteger) return GetInt(key.Bits);
        if (key.Ref is null) return default;
        key = Normalize(key);
        if (key.IsInteger) return GetInt(key.Bits);
        if (key.IsFloat && double.IsNaN(key.AsFloat)) return default;
        int n = FindNode(key);
        return n >= 0 ? _nodes[n].Value : default;
    }

    // ---- store ----

    public void SetInt(long k, LuaValue value)
    {
        Flags = 0;
        int i = ArrayIndex(k);
        if (i >= 0) { _arr[i] = value; return; }
        int n = FindNodeInt(k);
        if (n >= 0) { _nodes[n].Value = value; return; }
        NewKey(LuaValue.FromInteger(k), value);
    }

    /// <summary>Raw set; throws for nil and NaN keys.</summary>
    public void Set(LuaValue key, LuaValue value)
    {
        if (key.IsInteger) { SetInt(key.Bits, value); return; }
        key = Normalize(key);
        if (key.IsInteger) { SetInt(key.Bits, value); return; }
        if (key.Ref is null) ThrowBadKey("table index is nil");
        if (key.IsFloat && double.IsNaN(key.AsFloat)) ThrowBadKey("table index is NaN");
        Flags = 0;
        int n = FindNode(key);
        if (n >= 0) { _nodes[n].Value = value; return; }
        NewKey(key, value);
    }

    [LuaColdThrow]
    private static void ThrowBadKey(string message) => throw new LuaException(message);

    private int GetFreePos()
    {
        while (_lastFree > 0)
        {
            _lastFree--;
            if (_nodes[_lastFree].Key.IsNil) return _lastFree;
        }
        return -1;
    }

    /// <summary>Inserts a key known to be absent (PUC's luaH_newkey, Brent's variation).</summary>
    private void NewKey(LuaValue key, LuaValue value)
    {
        if (value.IsNil) return;
        if (_nodes.Length == 0)
        {
            Rehash(key);
            Set(key, value);
            return;
        }
        int mp = MainPosition(key);
        if (!_nodes[mp].Value.IsNil)
        {
            int f = GetFreePos();
            if (f < 0)
            {
                Rehash(key);
                Set(key, value);
                return;
            }
            int other = MainPosition(_nodes[mp].Key);
            if (other != mp)
            {
                // Colliding node is out of its main position: move it to the free slot.
                while (other + _nodes[other].Next != mp) other += _nodes[other].Next;
                _nodes[other].Next = f - other;
                _nodes[f] = _nodes[mp];
                if (_nodes[mp].Next != 0)
                {
                    _nodes[f].Next += mp - f;
                    _nodes[mp].Next = 0;
                }
                _nodes[mp].Value = default;
            }
            else
            {
                // Colliding node is in its main position: the new key goes to the free slot.
                _nodes[f].Next = _nodes[mp].Next != 0 ? mp + _nodes[mp].Next - f : 0;
                _nodes[mp].Next = f - mp;
                mp = f;
            }
        }
        _nodes[mp].Key = key;
        _nodes[mp].Value = value;
    }

    // ---- rehash ----

    private static int CountInt(long key, Span<int> nums)
    {
        if ((ulong)key - 1 < (1UL << MaxABits))
        {
            nums[CeilLog2((uint)key)]++;
            return 1;
        }
        return 0;
    }

    private int NumUseArray(Span<int> nums)
    {
        int ause = 0;
        int i = 1;
        int asize = _arr.Length;
        long ttlg = 1;
        for (int lg = 0; lg <= MaxABits; lg++, ttlg *= 2)
        {
            int lc = 0;
            long lim = ttlg;
            if (lim > asize)
            {
                lim = asize;
                if (i > lim) break;
            }
            for (; i <= lim; i++)
                if (!_arr[i - 1].IsNil) lc++;
            nums[lg] += lc;
            ause += lc;
        }
        return ause;
    }

    private int NumUseHash(Span<int> nums, ref int na)
    {
        int total = 0, ause = 0;
        for (int i = _nodes.Length - 1; i >= 0; i--)
        {
            ref Node n = ref _nodes[i];
            if (n.Value.IsNil) continue;
            if (n.Key.IsInteger) ause += CountInt(n.Key.Bits, nums);
            total++;
        }
        na += ause;
        return total;
    }

    private static int ComputeSizes(Span<int> nums, ref int pna)
    {
        int a = 0, na = 0, optimal = 0;
        long twotoi = 1;
        for (int i = 0; twotoi > 0 && twotoi <= int.MaxValue && pna > twotoi / 2; i++, twotoi *= 2)
        {
            a += nums[i];
            if (a > twotoi / 2)
            {
                optimal = (int)twotoi;
                na = a;
            }
        }
        pna = na;
        return optimal;
    }

    private void Rehash(LuaValue extraKey)
    {
        Span<int> nums = stackalloc int[MaxABits + 1];
        _limit = _arr.Length;
        int na = NumUseArray(nums);
        int total = na;
        total += NumUseHash(nums, ref na);
        if (extraKey.IsInteger) na += CountInt(extraKey.Bits, nums);
        total++;
        int asize = ComputeSizes(nums, ref na);
        Resize(asize, total - na);
    }

    /// <summary>Resizes the array part to <paramref name="newASize"/> slots and the hash part to hold <paramref name="nhSize"/> entries.</summary>
    internal void Resize(int newASize, int nhSize)
    {
        _limit = _arr.Length;
        LuaValue[] oldArr = _arr;
        Node[] oldNodes = _nodes;
        int oldASize = oldArr.Length;

        if (nhSize == 0)
        {
            _nodes = [];
            _lastFree = 0;
        }
        else
        {
            int size = 1 << CeilLog2((uint)nhSize);
            _nodes = new Node[size];
            _lastFree = size;
        }

        var newArr = newASize == 0 ? [] : new LuaValue[newASize];
        Array.Copy(oldArr, newArr, Math.Min(oldASize, newASize));
        _arr = newArr;
        _limit = newASize;

        for (int i = newASize; i < oldASize; i++)
            if (!oldArr[i].IsNil) InsertRaw(LuaValue.FromInteger(i + 1), oldArr[i]);
        for (int j = 0; j < oldNodes.Length; j++)
            if (!oldNodes[j].Value.IsNil) InsertRaw(oldNodes[j].Key, oldNodes[j].Value);
    }

    private void InsertRaw(LuaValue key, LuaValue value)
    {
        if (key.IsInteger)
        {
            int i = ArrayIndex(key.Bits);
            if (i >= 0) { _arr[i] = value; return; }
        }
        int n = FindNode(key);
        if (n >= 0) { _nodes[n].Value = value; return; }
        NewKey(key, value);
    }

    /// <summary>Grows the array part to at least <paramref name="n"/> slots, keeping the hash part (PUC's luaH_resizearray).</summary>
    internal void EnsureArray(int n)
    {
        if (n > _arr.Length) Resize(n, _nodes.Length);
    }

    // ---- length (border) ----

    private static bool IsPow2(int x) => (x & (x - 1)) == 0;

    private static int BinSearch(LuaValue[] array, int i, int j)
    {
        while (j - i > 1)
        {
            int m = (int)(((uint)i + (uint)j) / 2);
            if (array[m - 1].IsNil) j = m;
            else i = m;
        }
        return i;
    }

    private long HashSearch(long j)
    {
        long i;
        if (j == 0) j++;
        do
        {
            i = j;
            if (j <= long.MaxValue / 2) j *= 2;
            else
            {
                j = long.MaxValue;
                if (GetInt(j).IsNil) break;
                return j;
            }
        } while (!GetInt(j).IsNil);
        while (j - i > 1)
        {
            long m = (long)(((ulong)i + (ulong)j) / 2);
            if (GetInt(m).IsNil) j = m;
            else i = m;
        }
        return i;
    }

    /// <summary>A border of the table (PUC's luaH_getn).</summary>
    public long Length()
    {
        int limit = _limit;
        if (limit > 0 && _arr[limit - 1].IsNil)
        {
            // There must be a border before 'limit'.
            if (limit >= 2 && !_arr[limit - 2].IsNil)
            {
                if (IsPow2(_arr.Length) && !IsPow2(limit - 1)) _limit = limit - 1;
                return limit - 1;
            }
            int boundary = BinSearch(_arr, 0, limit);
            if (IsPow2(_arr.Length) && boundary > _arr.Length / 2) _limit = boundary;
            return boundary;
        }
        if (limit != _arr.Length)
        {
            // 'limit' > 0 and is not the real size.
            if (_arr[limit].IsNil) return limit;
            limit = _arr.Length;
            if (_arr[limit - 1].IsNil)
            {
                int boundary = BinSearch(_arr, _limit, limit);
                _limit = boundary;
                return boundary;
            }
        }
        if (_nodes.Length == 0 || GetInt((long)limit + 1).IsNil) return limit;
        return HashSearch(limit);
    }

    // ---- traversal ----

    /// <summary>Advances the traversal after <paramref name="key"/> (nil starts it); false when it is exhausted.</summary>
    public bool Next(LuaValue key, out LuaValue nextKey, out LuaValue nextValue)
    {
        int asize = _arr.Length;
        int i;
        if (key.IsNil) i = 0;
        else
        {
            key = Normalize(key);
            if (key.IsInteger && (ulong)key.Bits - 1 < (ulong)asize) i = (int)key.Bits;
            else
            {
                int n = FindNode(key);
                if (n < 0) ThrowBadKey("invalid key to 'next'");
                i = n + 1 + asize;
            }
        }
        for (; i < asize; i++)
        {
            if (!_arr[i].IsNil)
            {
                nextKey = LuaValue.FromInteger(i + 1);
                nextValue = _arr[i];
                return true;
            }
        }
        for (i -= asize; i < _nodes.Length; i++)
        {
            if (!_nodes[i].Value.IsNil)
            {
                nextKey = _nodes[i].Key;
                nextValue = _nodes[i].Value;
                return true;
            }
        }
        nextKey = default;
        nextValue = default;
        return false;
    }

    public override string ToString() => $"table: 0x{RuntimeHelpers.GetHashCode(this):x8}";
}
