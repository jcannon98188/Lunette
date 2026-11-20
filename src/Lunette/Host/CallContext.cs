namespace Lunette;

/// <summary>
/// A host function: reads <see cref="CallContext.Args"/>, pushes results with <see cref="CallContext.Push"/> and returns
/// how many results it produced (the last <c>n</c> pushed values). Returning <see cref="CallContext.YieldRequest"/>
/// (via <see cref="CallContext.Yield"/>) asks the VM to yield the pushed values instead.
/// </summary>
public delegate int HostFunction(ref CallContext ctx);

/// <summary>
/// Runs when a coroutine suspended by a host function is resumed. <see cref="CallContext.Args"/> are the resume
/// values; the result protocol is the same as for <see cref="HostFunction"/> and it may yield again.
/// </summary>
public delegate int HostContinuation(ref CallContext ctx);

/// <summary>The arguments, result sink and yield request of one host-function call. Valid only during the call.</summary>
public ref struct CallContext
{
    /// <summary>The result count a host function returns to request a yield; use <see cref="Yield"/>.</summary>
    public const int YieldRequest = -1;

    private LuaValue[]? _pushed;
    private int _count;
    private HostContinuation? _continuation;

    internal CallContext(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        State = state;
        Args = args;
    }

    /// <summary>The state the call runs in.</summary>
    public LuaState State { get; }

    /// <summary>The arguments, as a span over the VM's stack window. Do not keep it past the call.</summary>
    public ReadOnlySpan<LuaValue> Args { get; }

    /// <summary>Appends one result (or, when yielding, one value to yield).</summary>
    public void Push(LuaValue value)
    {
        _pushed ??= new LuaValue[4];
        if (_count == _pushed.Length) Array.Resize(ref _pushed, _count * 2);
        _pushed[_count++] = value;
    }

    /// <summary>A Lua error for <c>throw</c>; like <c>luaL_error</c> it gets the calling script position prefixed.</summary>
    public readonly LuaException Error(string message) => LuaErrors.Error(message);

    /// <summary>
    /// Asks the VM to suspend the running coroutine, yielding the pushed values. When resumed, <paramref name="continuation"/>
    /// runs with the resume values; without one, the resume values become the host function's results.
    /// Return the result of this call from the host function.
    /// </summary>
    public int Yield(HostContinuation? continuation = null)
    {
        _continuation = continuation;
        return YieldRequest;
    }

    internal readonly HostContinuation? Continuation => _continuation;

    internal readonly ReadOnlySpan<LuaValue> Pushed => new(_pushed, 0, _count);

    /// <summary>The last <paramref name="n"/> pushed values as the call's result array.</summary>
    internal readonly LuaValue[] TakeResults(int n)
    {
        if (n < 0 || n > _count) throw new InvalidOperationException($"host function returned {n} results but pushed {_count}");
        if (n == 0) return [];
        if (_pushed!.Length == n) return _pushed;
        return new ReadOnlySpan<LuaValue>(_pushed, _count - n, n).ToArray();
    }
}
