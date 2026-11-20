using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Lunette.Vm;

namespace Lunette.Gc;

/// <summary>
/// Per-state collector bookkeeping on top of the .NET GC (ticket 12, best-effort policy):
/// <c>__gc</c> through <see cref="FinalizerMark"/> sentinels drained on the Lua thread, weak tables weakened
/// during collection windows, and the <c>collectgarbage</c> options.
/// </summary>
internal sealed class GcState
{
    /// <summary>Back-edges between safepoint checks.</summary>
    internal const int SafepointInterval = 1 << 14;

    private const long MinCycleBytes = 16L << 20;

    private readonly LuaState _state;
    private readonly ConditionalWeakTable<object, FinalizerMark> _marks = new();
    private readonly ConcurrentQueue<FinalizerMark> _pending = new();
    private readonly List<WeakReference<FinalizerMark>> _registry = [];
    private readonly List<WeakReference<LuaTable>> _weakTables = [];
    private int _pendingCount;
    private int _registryLimit = 64;
    private int _weakTablesLimit = 64;
    private long _seq;
    private bool _inCollector;
    private bool _closed;
    private long _allocatedAtLastCycle;

    internal GcState(LuaState state) => _state = state;

    /// <summary>Receives warnings such as a failing <c>__gc</c>; the default discards them (Lua's warnings start off).</summary>
    public Action<string>? Warn { get; set; }

    internal string Mode { get; set; } = "incremental";
    internal int Pause { get; set; } = 200;
    internal int StepMul { get; set; } = 100;
    /// <summary>False after <c>collectgarbage("stop")</c>: the automatic safepoint work is off.</summary>
    internal bool Running { get; set; } = true;
    internal bool InCollector => _inCollector;

    // ---- marking ----

    /// <summary>Called whenever a metatable is installed on a table or userdata (<c>setmetatable</c>, userdata creation).</summary>
    internal void OnSetMetatable(object target, LuaTable? mt)
    {
        if (mt is null) return;
        if (!mt.GetTagMethod(TagMethod.Gc).IsNil) Mark(target);
        if (target is LuaTable t && !mt.GetTagMethod(TagMethod.Mode).IsNil) RegisterWeak(t);
    }

    private void Mark(object t)
    {
        if (_closed) return;
        if (_marks.TryGetValue(t, out FinalizerMark? old))
        {
            if (!old.Done) return;
            _marks.Remove(t);
        }
        var mark = new FinalizerMark(t, ++_seq, this);
        _marks.Add(t, mark);
        _registry.Add(new WeakReference<FinalizerMark>(mark, trackResurrection: true));
        if (_registry.Count > _registryLimit)
        {
            _registry.RemoveAll(static w => !w.TryGetTarget(out FinalizerMark? m) || m.Done);
            _registryLimit = Math.Max(64, _registry.Count * 2);
        }
    }

    private void RegisterWeak(LuaTable t)
    {
        _weakTables.Add(new WeakReference<LuaTable>(t));
        if (_weakTables.Count > _weakTablesLimit)
        {
            Prune(_weakTables);
            _weakTablesLimit = Math.Max(64, _weakTables.Count * 2);
        }
    }

    /// <summary>Drops dead and duplicate registrations.</summary>
    private static void Prune(List<WeakReference<LuaTable>> list)
    {
        var seen = new HashSet<LuaTable>(ReferenceEqualityComparer.Instance);
        list.RemoveAll(w => !w.TryGetTarget(out LuaTable? t) || !seen.Add(t));
    }

    /// <summary>Called on the .NET finalizer thread: only enqueues.</summary>
    internal void Enqueue(FinalizerMark mark)
    {
        if (_closed) return;
        _pending.Enqueue(mark);
        Interlocked.Increment(ref _pendingCount);
    }

    internal bool HasPending => Volatile.Read(ref _pendingCount) != 0;

    // ---- finalizer drain (Lua thread) ----

    /// <summary>Runs the pending <c>__gc</c> finalizers, newest marking first; returns how many ran.</summary>
    private int Drain()
    {
        if (_pending.IsEmpty) return 0;
        var batch = new List<FinalizerMark>();
        while (_pending.TryDequeue(out FinalizerMark? m))
        {
            Interlocked.Decrement(ref _pendingCount);
            batch.Add(m);
        }
        return RunFinalizers(batch);
    }

    private int RunFinalizers(List<FinalizerMark> batch)
    {
        batch.Sort(static (a, b) => b.Seq.CompareTo(a.Seq));
        int ran = 0;
        foreach (FinalizerMark m in batch)
        {
            if (m.Done) continue;
            m.Done = true;
            GC.SuppressFinalize(m);
            object target = m.Target;
            if (_marks.TryGetValue(target, out FinalizerMark? current) && ReferenceEquals(current, m)) _marks.Remove(target);
            LuaTable? mt = target is LuaTable tt ? tt.Metatable : ((LuaUserdata)target).Metatable;
            LuaValue tm = mt is null ? default : mt.GetTagMethod(TagMethod.Gc);
            if (tm.IsNil) continue;
            ran++;
            if (!Interpreter.TryCall1(_state, tm, LuaValue.FromObject(target), out LuaValue error))
                Warning("error in __gc (" + (error.Ref is byte[] s ? new LuaString(s).ToString() : "error object is not a string") + ")");
        }
        return ran;
    }

    private void Warning(string message)
    {
        try { Warn?.Invoke(message); }
        catch (Exception) { /* a faulty sink must not break the collector */ }
    }

    // ---- collection ----

    /// <summary>
    /// A full cycle: weaken weak tables, collect, restore the survivors, then run the finalizers; repeated once
    /// when finalizers ran, so objects they released are collected too. False when called from inside a finalizer.
    /// </summary>
    internal bool FullCollect()
    {
        if (_inCollector) return false;
        _inCollector = true;
        try
        {
            for (int pass = 0; pass < 2; pass++)
            {
                ClearDeadStack(full: true);
                CollectWindow(GC.MaxGeneration, compacting: true);
                _state.Strings.Clear();
                if (Drain() == 0) break;
            }
            _allocatedAtLastCycle = GC.GetAllocatedBytesForCurrentThread();
            return true;
        }
        finally { _inCollector = false; }
    }

    /// <summary>A cheap cycle: young generations only, then the finalizers. False when called from inside a finalizer.</summary>
    internal bool Step()
    {
        if (_inCollector) return false;
        _inCollector = true;
        try
        {
            ClearDeadStack(full: true);
            GC.Collect(1, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            Drain();
            return true;
        }
        finally { _inCollector = false; }
    }

    private void CollectWindow(int generation, bool compacting)
    {
        List<WeakDetach>? detached = Weaken();
        GC.Collect(generation, GCCollectionMode.Forced, blocking: true, compacting);
        GC.WaitForPendingFinalizers();
        if (detached is not null) Restore(detached);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private List<WeakDetach>? Weaken()
    {
        if (_weakTables.Count == 0) return null;
        Prune(_weakTables);
        List<WeakDetach>? result = null;
        foreach (WeakReference<LuaTable> wr in _weakTables)
        {
            if (!wr.TryGetTarget(out LuaTable? t)) continue;
            (bool wk, bool wv) = t.WeakMode();
            if (!wk && !wv) continue;
            WeakDetach? d = t.DetachWeak(wk, wv, wr);
            if (d is not null) (result ??= []).Add(d);
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Restore(List<WeakDetach> detached)
    {
        foreach (WeakDetach d in detached)
        {
            if (d.Table.TryGetTarget(out LuaTable? t)) t.RestoreWeak(d);
            d.DisposeHandles();
        }
    }

    /// <summary>Forgets stale references the running coroutine left above its live registers.</summary>
    private void ClearDeadStack(bool full)
    {
        var co = _state.Running;
        int from = Math.Min(co.CallTop, co.Stack.Length);
        int count = co.Stack.Length - from;
        if (!full) count = Math.Min(count, 1024);
        if (count > 0) Array.Clear(co.Stack, from, count);
    }

    /// <summary>
    /// Called by the interpreter every <see cref="SafepointInterval"/> loop back-edges: drains finalizers the .NET GC
    /// has queued, and starts a full cycle when weak tables exist and enough has been allocated since the last one.
    /// </summary>
    internal void Safepoint()
    {
        _state.GcTick = SafepointInterval;
        if (!Running || _inCollector || _closed) return;
        ClearDeadStack(full: false);
        if (_weakTables.Count > 0)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            if (allocated - _allocatedAtLastCycle > Math.Max(MinCycleBytes, GC.GetTotalMemory(false) * Pause / 100))
            {
                FullCollect();
                return;
            }
        }
        if (!HasPending) return;
        _inCollector = true;
        try { Drain(); }
        finally { _inCollector = false; }
    }

    // ---- close ----

    /// <summary>Forgets every registered finalizer and weak table without running any Lua code (used for an aborted state).</summary>
    internal void Discard()
    {
        _closed = true;
        while (_pending.TryDequeue(out _)) { }
        Volatile.Write(ref _pendingCount, 0);
        _registry.Clear();
        _weakTables.Clear();
    }

    /// <summary>Runs every remaining <c>__gc</c> in reverse marking order; errors are swallowed.</summary>
    internal void Close()
    {
        if (_closed) return;
        _inCollector = true;
        try
        {
            var batch = new List<FinalizerMark>();
            while (_pending.TryDequeue(out FinalizerMark? p)) batch.Add(p);
            Volatile.Write(ref _pendingCount, 0);
            foreach (WeakReference<FinalizerMark> w in _registry)
                if (w.TryGetTarget(out FinalizerMark? m) && !m.Done && !batch.Contains(m)) batch.Add(m);
            try { RunFinalizers(batch); }
            catch (Exception) { /* errors while closing are swallowed */ }
        }
        finally
        {
            _closed = true;
            _inCollector = false;
            _registry.Clear();
        }
    }
}
