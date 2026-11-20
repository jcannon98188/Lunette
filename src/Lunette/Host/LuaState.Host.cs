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
        _budgetLeft = _limits.InstructionLimit > 0 ? _limits.InstructionLimit : long.MaxValue;
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
        string? problem = null;
        if (Volatile.Read(ref _interrupt) != 0) problem = "interrupted";
        else if (_budgetLeft <= 0) problem = "instruction limit exceeded";
        else if (_limits.MemoryLimitBytes > 0 && GC.GetAllocatedBytesForCurrentThread() - _allocBaseline > _limits.MemoryLimitBytes)
            problem = "memory limit exceeded";
        if (problem is not null)
        {
            _sliceSize = 1;
            Ticks = 1;
            throw LuaErrors.Error(problem);
        }
        _sliceSize = (int)Math.Min(TickSlice, _budgetLeft);
        Ticks = _sliceSize;
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
        new LuaClosure(LuaCompiler.Compile(source, chunkName, Strings), []);

    /// <summary>Compiles a text chunk given as bytes.</summary>
    public LuaFunction Load(ReadOnlySpan<byte> source, string chunkName) =>
        new LuaClosure(LuaCompiler.Compile(source.ToArray(), chunkName, Strings), []);

    /// <summary>Calls <paramref name="function"/> and returns all its results.</summary>
    public LuaValue[] Call(LuaFunction function, ReadOnlySpan<LuaValue> args)
    {
        ArgumentNullException.ThrowIfNull(function);
        EnterHostCall();
        return Interpreter.CallValues(this, LuaValue.FromObject(function), args);
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
        EnterHostCall();
        return Interpreter.CallFirst(this, LuaValue.FromObject(function), a, b, c, d, nargs);
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
    public LuaValue GetGlobal(string name) => Globals.GetValueOrDefault(name);

    /// <summary>Sets a global variable.</summary>
    public void SetGlobal(string name, LuaValue value) => Globals[name] = value;

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
        return new LuaUserdata(value, mt);
    }
}
