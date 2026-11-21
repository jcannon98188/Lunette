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

    /// <summary>Used when no <see cref="Warn"/> sink is set (the standalone interpreter's stderr format).</summary>
    internal Action<string>? WarnFallback { get; set; }

    internal string Mode { get; set; } = "incremental";
    internal int Pause { get; set; } = 200;
    internal int StepMul { get; set; } = 100;
    /// <summary>False after <c>collectgarbage("stop")</c>: the automatic safepoint work is off.</summary>
    internal bool Running
    {
        get => _running;
        set
        {
            if (_running && !value) NoteHeap();
            _running = value;
        }
    }

    private bool _running = true;
    private long _stoppedHeap;
    private long _stoppedAllocated;

    /// <summary>
    /// <c>collectgarbage("count")</c>. Running, it is the managed heap size. Stopped, Lua frees nothing, so it is the heap at the
    /// stop (or last explicit collection) plus what this state's thread has allocated since: a per-state measure that the
    /// process-wide .NET collector, driven by whatever else is running, cannot shrink.
    /// </summary>
    internal long HeapBytes =>
        _running ? GC.GetTotalMemory(false) : _stoppedHeap + (GC.GetAllocatedBytesForCurrentThread() - _stoppedAllocated);

    private void NoteHeap()
    {
        _stoppedHeap = GC.GetTotalMemory(false);
        _stoppedAllocated = GC.GetAllocatedBytesForCurrentThread();
    }
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
        // Long weak reference: a weak table reachable only through an object being finalized (its metatable's field, say)
        // must still be restored, or its detached entries are lost and the table keeps dead-key placeholders.
        _weakTables.Add(new WeakReference<LuaTable>(t, trackResurrection: true));
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

    private bool _closing;

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
            bool ok;
            LuaValue error;
            Coroutine running = _state.Running;
            int savedFrame = running.FinalizerFrame;
            running.FinalizerFrame = running.Depth; // debug.getinfo names this call "__gc"
            try { ok = Interpreter.TryCall1(_state, tm, LuaValue.FromObject(target), out error); }
            catch (LuaExitException) when (_closing) { continue; } // os.exit(_, true) while closing: the rest still run, as lua_close would
            finally { running.FinalizerFrame = savedFrame; }
            if (!ok)
                Warning("error in __gc (" + (error.Ref is byte[] s ? new LuaString(s).ToString() : "error object is not a string") + ")");
        }
        return ran;
    }

    private void Warning(string message)
    {
        try { if (Warn is { } sink) sink(message); else WarnFallback?.Invoke(message); }
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
                _state.Strings.Clear(); // before the collection, so the intern table does not keep a dead string alive through it
                CollectWindow(GC.MaxGeneration, compacting: true);
                if (Drain() == 0) break;
            }
            _allocatedAtLastCycle = GC.GetAllocatedBytesForCurrentThread();
            NoteHeap();
            return true;
        }
        finally { _inCollector = false; }
    }

    /// <summary>The work, in KB of step size, that completes one cycle of <c>collectgarbage("step", n)</c>.</summary>
    internal const long CycleWorkKb = 100;

    private long _stepWork;

    /// <summary>
    /// <c>collectgarbage("step", n)</c>: <paramref name="kb"/> of zero completes a cycle at once; otherwise each call adds that
    /// much work, so larger steps finish a cycle in fewer calls, and the call that reaches <see cref="CycleWorkKb"/> runs a
    /// full collection. True when a cycle completed.
    /// </summary>
    internal bool StepBy(long kb)
    {
        if (kb > 0 && (_stepWork += kb) < CycleWorkKb)
        {
            Step();
            return false;
        }
        _stepWork = 0;
        FullCollect();
        return true;
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
            NoteHeap();
            return true;
        }
        finally { _inCollector = false; }
    }

    private void CollectWindow(int generation, bool compacting)
    {
        List<WeakDetach>? detached = Weaken();
        try
        {
            GC.Collect(generation, GCCollectionMode.Forced, blocking: true, compacting);
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            if (detached is not null) Restore(detached); // also frees every handle, even when the collection threw
        }
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
            try { if (d.Table.TryGetTarget(out LuaTable? t)) t.RestoreWeak(d); }
            finally { d.DisposeHandles(); }
        }
    }

    /// <summary>Forgets stale references the running coroutine left above its live registers.</summary>
    private void ClearDeadStack(bool full, int liveTop = -1)
    {
        var co = _state.Running;
        int from = Math.Min(liveTop >= 0 ? liveTop : co.CallTop, co.Stack.Length);
        int count = co.Stack.Length - from;
        if (!full) count = Math.Min(count, 1024);
        if (count > 0) Array.Clear(co.Stack, from, count);
    }

    /// <summary>
    /// Called by the interpreter every <see cref="SafepointInterval"/> loop back-edges: drains finalizers the .NET GC
    /// has queued. Returns true when a full cycle is due (weak tables exist and enough has been allocated since the last one):
    /// the loop then returns so the cycle runs outside it, where its frame holds no stale references. <paramref name="liveTop"/>
    /// is the first stack slot above the running frame's named locals; everything above it is a dead temporary.
    /// </summary>
    internal bool Safepoint(int liveTop)
    {
        _state.GcTick = SafepointInterval;
        if (!Running || _inCollector || _closed) return false;
        ClearDeadStack(full: false, liveTop);
        if (_weakTables.Count > 0)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            if (allocated - _allocatedAtLastCycle > Math.Max(MinCycleBytes, GC.GetTotalMemory(false) * Pause / 100))
                return true;
        }
        if (!HasPending) return false;
        _inCollector = true;
        try { Drain(); }
        finally { _inCollector = false; }
        return false;
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
        _closing = true;
        try
        {
            var batch = new List<FinalizerMark>();
            var seen = new HashSet<FinalizerMark>(ReferenceEqualityComparer.Instance);
            while (_pending.TryDequeue(out FinalizerMark? p)) { batch.Add(p); seen.Add(p); }
            Volatile.Write(ref _pendingCount, 0);
            foreach (WeakReference<FinalizerMark> w in _registry)
                if (w.TryGetTarget(out FinalizerMark? m) && !m.Done && seen.Add(m)) batch.Add(m);
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
