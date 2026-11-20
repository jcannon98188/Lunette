using Lunette.Compiler;
using Lunette.Vm;

namespace Lunette;

// The host-facing surface of LuaState: loading, calling, host functions, globals, userdata and sandbox limits.
public sealed partial class LuaState
{
    private const int TickSlice = 1024;

    private LuaLimits _limits = new();
    private int _interrupt;
    private int _sliceSize = TickSlice;
    private long _budgetLeft = long.MaxValue;
    private long _allocBaseline;
    private long _runBudget = long.MaxValue;
    private bool _budgetIsCumulative;
    private long _cumulativeUsed;
    private int _runDepth;
    private bool _disposed;
    private LuaAbortKind _abortKind;
    private long _callInstructionLimit;
    private readonly Dictionary<Type, LuaTable> _typeMetatables = new();

    /// <summary>Countdown to the next limit poll; decremented on backward jumps and Lua calls.</summary>
    internal int Ticks = TickSlice;

    /// <summary>Largest active Lua call depth (frames); int.MaxValue when unlimited.</summary>
    internal int DepthLimit = int.MaxValue;

    /// <summary>A state with sandbox limits applied (see <see cref="LuaLimits"/>).</summary>
    public LuaState(LuaLimits limits) : this()
    {
        SetLimits(limits);
    }

    /// <summary>The limits in force.</summary>
    public LuaLimits Limits => _limits;

    /// <summary>
    /// Overrides <see cref="LuaLimits.InstructionLimit"/> for each following host call (one hook or event invocation); zero
    /// falls back to the limit. A run that exhausts it aborts the state (<see cref="LuaAbortException"/>).
    /// </summary>
    public long CallInstructionLimit
    {
        get => _callInstructionLimit;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _callInstructionLimit = value;
        }
    }

    /// <summary>Instructions (backward jumps and Lua calls) spent by completed host calls since the last <see cref="ResetCumulativeBudget"/>.</summary>
    public long CumulativeInstructionsUsed => _cumulativeUsed;

    /// <summary>Starts a new <see cref="LuaLimits.CumulativeInstructionLimit"/> window (for example at each tick).</summary>
    public void ResetCumulativeBudget() => _cumulativeUsed = 0;

    /// <summary>
    /// True once a budget or memory limit aborted a run. The state is then unusable: every later call throws
    /// <see cref="InvalidOperationException"/>; discard it with <see cref="Dispose"/>.
    /// </summary>
    public bool IsAborted { get; private set; }

    /// <summary>Why the state was aborted; meaningful only when <see cref="IsAborted"/>.</summary>
    public LuaAbortKind AbortKind => _abortKind;

    private void ThrowIfAborted()
    {
        if (IsAborted) throw new InvalidOperationException("The Lua state was aborted by a budget or memory limit and must be discarded.");
    }

    /// <summary>Replaces the limits; the budgets restart at the next top-level run.</summary>
    public void SetLimits(LuaLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits;
        DepthLimit = limits.StackDepthLimit > 0 ? limits.StackDepthLimit : int.MaxValue;
        BeginRun();
    }

    /// <summary>
    /// Asks the running script to stop with a Lua error "interrupted" at its next poll point (a backward jump or call).
    /// Safe to call from any thread; stays in effect until <see cref="ClearInterrupt"/>.
    /// </summary>
    public void RequestInterrupt() => Volatile.Write(ref _interrupt, 1);

    /// <summary>Clears a pending <see cref="RequestInterrupt"/>.</summary>
    public void ClearInterrupt() => Volatile.Write(ref _interrupt, 0);

    /// <summary>Restarts the budgets for a new top-level run.</summary>
    internal void BeginRun()
    {
        long perCall = _callInstructionLimit > 0 ? _callInstructionLimit : _limits.InstructionLimit > 0 ? _limits.InstructionLimit : long.MaxValue;
        long cumulativeLeft = _limits.CumulativeInstructionLimit > 0 ? Math.Max(0, _limits.CumulativeInstructionLimit - _cumulativeUsed) : long.MaxValue;
        _budgetIsCumulative = cumulativeLeft < perCall;
        _budgetLeft = _runBudget = Math.Min(perCall, cumulativeLeft);
        _sliceSize = (int)Math.Min(TickSlice, _budgetLeft);
        Ticks = _sliceSize;
        if (_limits.MemoryLimitBytes > 0) _allocBaseline = GC.GetAllocatedBytesForCurrentThread();
    }

    /// <summary>
    /// Called when <see cref="Ticks"/> reaches zero. Charges the slice to the budget, then checks the interrupt flag and the memory cap.
    /// A failed check leaves the slice at one tick so every later poll fails again.
    /// </summary>
    internal void Poll()
    {
        _budgetLeft -= _sliceSize;
        if (_budgetLeft <= 0) Abort(_budgetIsCumulative ? LuaAbortKind.CumulativeInstructionBudget : LuaAbortKind.InstructionBudget);
        if (_limits.MemoryLimitBytes > 0 && GC.GetAllocatedBytesForCurrentThread() - _allocBaseline > _limits.MemoryLimitBytes)
            Abort(LuaAbortKind.MemoryLimit);
        if (Volatile.Read(ref _interrupt) != 0)
        {
            _sliceSize = 1;
            Ticks = 1;
            throw LuaErrors.Error("interrupted");
        }
        _sliceSize = (int)Math.Min(TickSlice, _budgetLeft);
        Ticks = _sliceSize;
    }

    /// <summary>Marks the state aborted and throws the uncatchable exception; later polls fail again so nothing can run on.</summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private void Abort(LuaAbortKind kind)
    {
        IsAborted = true;
        _abortKind = kind;
        _sliceSize = 1;
        Ticks = 1;
        throw new LuaAbortException(kind, kind switch
        {
            LuaAbortKind.InstructionBudget => "instruction budget exceeded",
            LuaAbortKind.CumulativeInstructionBudget => "cumulative instruction budget exceeded",
            _ => "memory limit exceeded",
        });
    }

    /// <summary>Drops everything an aborted state holds; no Lua code runs.</summary>
    private void DiscardAborted()
    {
        Gc.Discard();
        Globals.Clear();
        _typeMetatables.Clear();
        Main.Reset();
        Main.CallTop = 0;
        Array.Clear(Main.Stack);
        Output = TextWriter.Null;
    }

    /// <summary>Opens a host-initiated run; only the outermost one resets the stack and restarts the budgets.</summary>
    private void BeginHostRun()
    {
        if (_runDepth++ == 0) EnterHostCall();
    }

    /// <summary>Closes a host-initiated run and charges what it spent to the cumulative budget.</summary>
    private void EndRun()
    {
        if (--_runDepth != 0) return;
        long used = _runBudget - _budgetLeft + (_sliceSize - Ticks);
        if (used > 0) _cumulativeUsed += used;
    }

    /// <summary>When the host calls in with nothing running, resets the stack and restarts the budgets.</summary>
    private void EnterHostCall()
    {
        if (!ReferenceEquals(Running, Main) || Main.Depth != 0) return;
        Main.Reset();
        Main.CallTop = 0;
        BeginRun();
    }

    /// <summary>Compiles a text chunk (UTF-8) to a function. Syntax errors throw <see cref="LuaException"/>.</summary>
    public LuaFunction Load(string source) => Load(source, "=(load)");

    /// <inheritdoc cref="Load(string)"/>
    public LuaFunction Load(string source, string chunkName) =>
        NewMainClosure(LuaCompiler.Compile(source, chunkName, Strings), LuaValue.FromTable(Globals));

    /// <summary>Compiles a text chunk given as bytes.</summary>
    public LuaFunction Load(ReadOnlySpan<byte> source, string chunkName) =>
        NewMainClosure(LuaCompiler.Compile(source.ToArray(), chunkName, Strings), LuaValue.FromTable(Globals));

    /// <summary>Calls <paramref name="function"/> and returns all its results.</summary>
    public LuaValue[] Call(LuaFunction function, ReadOnlySpan<LuaValue> args)
    {
        ArgumentNullException.ThrowIfNull(function);
        ThrowIfAborted();
        BeginHostRun();
        try { return Interpreter.CallValues(this, LuaValue.FromObject(function), args); }
        finally { EndRun(); }
    }

    /// <summary>Allocation-free call returning the first result (nil when none).</summary>
    public LuaValue Call(LuaFunction function) => CallFirst(function, default, default, default, default, 0);

    /// <inheritdoc cref="Call(LuaFunction)"/>
    public LuaValue Call(LuaFunction function, LuaValue a) => CallFirst(function, a, default, default, default, 1);

    /// <inheritdoc cref="Call(LuaFunction)"/>
    public LuaValue Call(LuaFunction function, LuaValue a, LuaValue b) => CallFirst(function, a, b, default, default, 2);

    /// <inheritdoc cref="Call(LuaFunction)"/>
    public LuaValue Call(LuaFunction function, LuaValue a, LuaValue b, LuaValue c) => CallFirst(function, a, b, c, default, 3);

    /// <inheritdoc cref="Call(LuaFunction)"/>
    public LuaValue Call(LuaFunction function, LuaValue a, LuaValue b, LuaValue c, LuaValue d) => CallFirst(function, a, b, c, d, 4);

    private LuaValue CallFirst(LuaFunction function, LuaValue a, LuaValue b, LuaValue c, LuaValue d, int nargs)
    {
        ArgumentNullException.ThrowIfNull(function);
        ThrowIfAborted();
        BeginHostRun();
        try { return Interpreter.CallFirst(this, LuaValue.FromObject(function), a, b, c, d, nargs); }
        finally { EndRun(); }
    }

    /// <summary>Wraps a host function as a Lua function value.</summary>
    public LuaFunction CreateFunction(HostFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new BuiltinFunction(
            (state, args) =>
            {
                var ctx = new CallContext(state, args);
                int n = function(ref ctx);
                if (n == CallContext.YieldRequest) throw new LuaException("attempt to yield across a C-call boundary");
                return ctx.TakeResults(n);
            },
            BuiltinKind.Host) { Host = function };
    }

    /// <summary>Defines a global host function.</summary>
    public void Register(string name, HostFunction function) => SetGlobal(name, LuaValue.FromFunction(CreateFunction(function)));

    /// <summary>Defines a global function from a typed delegate (conversion cost per call; use <see cref="HostFunction"/> on hot paths).</summary>
    public void Register(string name, Func<double, double> function) =>
        Register(name, (ref CallContext ctx) =>
        {
            ctx.Push(function(NumberArg(ctx.Args, 0, name)));
            return 1;
        });

    /// <inheritdoc cref="Register(string, Func{double, double})"/>
    public void Register(string name, Func<double, double, double> function) =>
        Register(name, (ref CallContext ctx) =>
        {
            ctx.Push(function(NumberArg(ctx.Args, 0, name), NumberArg(ctx.Args, 1, name)));
            return 1;
        });

    /// <inheritdoc cref="Register(string, Func{double, double})"/>
    public void Register(string name, Func<string, string> function) =>
        Register(name, (ref CallContext ctx) =>
        {
            if (ctx.Args.Length == 0 || !ctx.Args[0].TryGetString(out string? s)) throw LuaErrors.TypeError(ctx.Args, 0, name, "string");
            ctx.Push(function(s));
            return 1;
        });

    private static double NumberArg(ReadOnlySpan<LuaValue> args, int i, string name)
    {
        if (i < args.Length && args[i].TryGetNumber(out double d)) return d;
        throw LuaErrors.TypeError(args, i, name, "number");
    }

    /// <summary>Reads a global variable (nil when unset).</summary>
    public LuaValue GetGlobal(string name) => Globals.Get(LuaValue.FromString(Strings.FromUtf16(name)));

    /// <summary>Sets a global variable.</summary>
    public void SetGlobal(string name, LuaValue value) => Globals.Set(LuaValue.FromString(Strings.FromUtf16(name)), value);

    /// <summary>
    /// Associates a metatable with a .NET type: <see cref="CreateUserdata"/> uses it for objects of that type
    /// (or, failing an exact match, of a derived type). The core never reflects over the type.
    /// </summary>
    public void RegisterMetatable<T>(LuaTable metatable) where T : class
    {
        ArgumentNullException.ThrowIfNull(metatable);
        _typeMetatables[typeof(T)] = metatable;
    }

    /// <summary>Wraps <paramref name="value"/> as userdata, using the metatable registered for its type if any.</summary>
    public LuaUserdata CreateUserdata(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        LuaTable? mt = null;
        for (Type? t = value.GetType(); t is not null && mt is null; t = t.BaseType)
            _typeMetatables.TryGetValue(t, out mt);
        var userdata = new LuaUserdata(value, mt);
        Gc.OnSetMetatable(userdata, mt);
        return userdata;
    }
}
