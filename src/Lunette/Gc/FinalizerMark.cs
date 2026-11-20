namespace Lunette.Gc;

/// <summary>
/// The finalizable sentinel of an object marked for <c>__gc</c>. It holds a strong reference back to the object,
/// so when both become unreachable the .NET GC resurrects them together and runs <see cref="Finalize"/> on its
/// finalizer thread. That thread only enqueues the mark: Lua code never runs there (ticket 12).
/// </summary>
internal sealed class FinalizerMark
{
    private readonly GcState _owner;

    public FinalizerMark(object target, long seq, GcState owner)
    {
        Target = target;
        Seq = seq;
        _owner = owner;
    }

    /// <summary>The object whose <c>__gc</c> is pending.</summary>
    public object Target { get; }

    /// <summary>Marking order; finalizers run in descending order within a batch.</summary>
    public long Seq { get; }

    /// <summary>Set once <c>__gc</c> has been run (or abandoned), so it runs at most once per marking.</summary>
    public volatile bool Done;

    ~FinalizerMark() => _owner.Enqueue(this);
}
