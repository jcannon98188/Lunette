using System.Runtime;
using System.Runtime.InteropServices;

namespace Lunette;

/// <summary>One entry taken out of a weak table for the duration of a collection window.</summary>
internal struct WeakSlot
{
    public bool InArray;
    public int Index;
    /// <summary>Weak key (key and value both weak, or weak key with a non-collectable value).</summary>
    public GCHandle Key;
    /// <summary>Weak value.</summary>
    public GCHandle Value;
    /// <summary>Weak key with a collectable value: the value lives only while the key does (ephemeron).</summary>
    public DependentHandle Ephemeron;
    public bool HasEphemeron;
    /// <summary>Weak value with a long string key: the key's text lives only while the value does (primary = value, dependent = key).</summary>
    public DependentHandle KeyHandle;
    public bool HasKeyHandle;
}

/// <summary>The entries detached from one weak table; <see cref="Table"/> is weak so a dead table is not kept alive.</summary>
internal sealed class WeakDetach(WeakReference<LuaTable> table, WeakSlot[] slots)
{
    private static long _liveHandles;
    private int _handles = CountHandles(slots);

    /// <summary>Handles allocated by detached entries and not yet freed, process-wide (a leak check for the tests; zero between windows).</summary>
    internal static long LiveHandles => Volatile.Read(ref _liveHandles);

    public WeakReference<LuaTable> Table { get; } = table;
    public WeakSlot[] Slots { get; } = slots;

    private static int CountHandles(WeakSlot[] slots)
    {
        int n = 0;
        foreach (ref readonly WeakSlot s in slots.AsSpan())
        {
            if (s.Key.IsAllocated) n++;
            if (s.Value.IsAllocated) n++;
            if (s.HasEphemeron) n++;
            if (s.HasKeyHandle) n++;
        }
        Interlocked.Add(ref _liveHandles, n);
        return n;
    }

    /// <summary>Frees every handle of the detached entries; idempotent.</summary>
    public void DisposeHandles()
    {
        if (_handles == 0) return;
        foreach (ref WeakSlot s in Slots.AsSpan())
        {
            if (s.Key.IsAllocated) s.Key.Free();
            if (s.Value.IsAllocated) s.Value.Free();
            if (s.HasEphemeron) { s.Ephemeron.Dispose(); s.HasEphemeron = false; }
            if (s.HasKeyHandle) { s.KeyHandle.Dispose(); s.HasKeyHandle = false; }
        }
        Interlocked.Add(ref _liveHandles, -_handles);
        _handles = 0;
    }

    internal static GCHandle Weak(object? o) => GCHandle.Alloc(o, GCHandleType.Weak);
}

/// <summary>
/// Weak-table support (ticket 12). A table is weakened only inside a collection window: its weak entries are
/// moved into handles, the .NET GC runs, and the survivors are written back in place. Strong tables therefore
/// pay nothing on any access, and the node layout (and so <c>#t</c> and <c>next</c> order) is preserved.
/// </summary>
public sealed partial class LuaTable
{
    /// <summary>Marks a key whose object was collected; the node keeps its place in the collision chain.</summary>
    /// <summary>Strings longer than this are Lua's "long strings" (LUAI_MAXSHORTLEN).</summary>
    private const int LongStringLength = 40;

    private static readonly LuaValue DeadKey =LuaValue.FromObject(new object());

    /// <summary>Weak-key / weak-value flags from this table's metatable <c>__mode</c> string.</summary>
    internal (bool Keys, bool Values) WeakMode()
    {
        if (Metatable is null) return (false, false);
        LuaValue mode = Metatable.GetTagMethod(TagMethod.Mode);
        if (mode.Ref is not byte[] raw) return (false, false);
        bool k = false, v = false;
        foreach (byte b in new LuaString(raw).Bytes)
        {
            if (b == (byte)'k') k = true;
            else if (b == (byte)'v') v = true;
        }
        return (k, v);
    }

    /// <summary>Takes this table's weakly held entries out of it; null when there are none.</summary>
    internal WeakDetach? DetachWeak(bool weakKeys, bool weakValues, WeakReference<LuaTable> self)
    {
        var slots = new List<WeakSlot>();
        if (weakValues)
        {
            for (int i = 0; i < _arr.Length; i++)
            {
                if (!_arr[i].IsCollectable) continue;
                slots.Add(new WeakSlot { InArray = true, Index = i, Value = WeakDetach.Weak(_arr[i].Ref) });
                _arr[i] = default;
            }
        }
        for (int n = 0; n < _nodes.Length; n++)
        {
            ref Node nd = ref _nodes[n];
            if (nd.Value.IsNil)
            {
                // An erased entry keeps its key for 'next'; a long string key must not keep its text alive, so hold it weakly.
                if (nd.Key.Ref is byte[] { Length: > LongStringLength } dead)
                {
                    slots.Add(new WeakSlot { Index = n, Key = WeakDetach.Weak(dead) });
                    nd.Key = DeadKey;
                }
                continue;
            }
            bool wk = weakKeys && nd.Key.IsCollectable;
            bool wv = weakValues && nd.Value.IsCollectable;
            if (!wk && !wv) continue;
            var slot = new WeakSlot { Index = n };
            if (wk)
            {
                if (wv)
                {
                    slot.Key = WeakDetach.Weak(nd.Key.Ref);
                    slot.Value = WeakDetach.Weak(nd.Value.Ref);
                    nd.Value = default;
                }
                else if (nd.Value.IsCollectable)
                {
                    slot.Ephemeron = new DependentHandle(nd.Key.Ref, nd.Value.Ref);
                    slot.HasEphemeron = true;
                    nd.Value = default;
                }
                else
                {
                    // A string or number value stays in the node; it is cleared afterwards if the key died.
                    slot.Key = WeakDetach.Weak(nd.Key.Ref);
                }
                nd.Key = DeadKey;
            }
            else
            {
                slot.Value = WeakDetach.Weak(nd.Value.Ref);
                if (nd.Key.Ref is byte[] { Length: > LongStringLength } text)
                {
                    // A long string key lives only while its weak value does, so a dead entry frees its text in this collection.
                    slot.KeyHandle = new DependentHandle(nd.Value.Ref, text);
                    slot.HasKeyHandle = true;
                    nd.Key = DeadKey;
                }
                nd.Value = default;
            }
            slots.Add(slot);
        }
        return slots.Count == 0 ? null : new WeakDetach(self, slots.ToArray());
    }

    /// <summary>Writes the surviving entries back where they were; entries with a dead part stay removed.</summary>
    internal void RestoreWeak(WeakDetach detached)
    {
        foreach (ref WeakSlot s in detached.Slots.AsSpan())
        {
            if (s.InArray)
            {
                object? v = s.Value.Target;
                if (v is not null) _arr[s.Index] = LuaValue.FromObject(v);
                continue;
            }
            ref Node nd = ref _nodes[s.Index];
            if (s.HasEphemeron)
            {
                (object? key, object? value) = s.Ephemeron.TargetAndDependent;
                if (key is not null && value is not null)
                {
                    nd.Key = LuaValue.FromObject(key);
                    nd.Value = LuaValue.FromObject(value);
                }
                continue;
            }
            if (s.Key.IsAllocated)
            {
                object? key = s.Key.Target;
                object? value = s.Value.IsAllocated ? s.Value.Target : null;
                bool valueAlive = !s.Value.IsAllocated || value is not null;
                if (key is not null && valueAlive)
                {
                    nd.Key = LuaValue.FromObject(key);
                    if (value is not null) nd.Value = LuaValue.FromObject(value);
                }
                else nd.Value = default;
            }
            else
            {
                object? value = s.Value.Target;
                if (value is not null)
                {
                    nd.Value = LuaValue.FromObject(value);
                    if (s.HasKeyHandle && s.KeyHandle.Dependent is { } text) nd.Key = LuaValue.FromObject(text);
                }
            }
        }
    }
}
