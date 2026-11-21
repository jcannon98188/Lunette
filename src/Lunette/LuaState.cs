using Lunette.Compiler;
using Lunette.Lib;
using Lunette.Vm;

namespace Lunette;

/// <summary>The single-threaded embedding root: owns globals and the output sink.</summary>
public sealed partial class LuaState : IDisposable
{
    /// <summary>Collector bookkeeping: <c>__gc</c>, weak tables and the <c>collectgarbage</c> options.</summary>
    internal Gc.GcState Gc { get; }
    /// <summary>Back-edges left before the interpreter's next collector safepoint.</summary>
    internal int GcTick = Lunette.Gc.GcState.SafepointInterval;
    /// <summary>The global environment table (_G): the initial value of every main chunk's _ENV upvalue.</summary>
    internal readonly LuaTable Globals = new();
    /// <summary>Best-effort intern cache for short strings (ADR 0005).</summary>
    internal readonly StringCache Strings;
    /// <summary>The metatable shared by all string values; its __index is the string library table.</summary>
    internal LuaTable? StringMetatable;
    /// <summary>The <c>next</c> builtin that <c>pairs</c> returns, kept so the interpreter can hand it back without calling pairs' body.</summary>
    internal BuiltinFunction? NextFunction;
    /// <summary>The registry table (debug.getregistry).</summary>
    internal LuaTable? Registry;
    /// <summary>Metatables shared by all values of nil, boolean, number, function, thread and userdata (debug.setmetatable).</summary>
    private LuaTable?[]? _primitiveMetatables;

    internal LuaTable? TypeMetatable(LuaValue v) => _primitiveMetatables?[TypeSlot(v)];

    internal void SetTypeMetatable(LuaValue v, LuaTable? mt)
    {
        _primitiveMetatables ??= new LuaTable?[6];
        _primitiveMetatables[TypeSlot(v)] = mt;
    }

    private static int TypeSlot(LuaValue v) => v.TypeName switch
    {
        "nil" => 0,
        "boolean" => 1,
        "number" => 2,
        "function" => 3,
        "thread" => 4,
        _ => 5,
    };
    internal readonly Coroutine Main = new();
    /// <summary>The coroutine currently executing.</summary>
    internal Coroutine Running;
    /// <summary>Depth of nested native-to-Lua calls in progress.</summary>
    internal int NativeDepth;
    /// <summary>The <c>package</c> table (require reads it from here, not from the global, as PUC reads its registry).</summary>
    internal LuaTable? Package;
    /// <summary>Whether warn() output is enabled (toggled by warn("@on") / warn("@off")).</summary>
    internal bool WarningsOn;

    /// <summary>A collector warning (a failed <c>__gc</c>): shown like <c>warn</c> output, only while warnings are on.</summary>
    private void EmitGcWarning(string message)
    {
        if (!WarningsOn) return;
        string text = "Lua warning: " + message;
        Console.Error.WriteLine(text);
    }

    public LuaState() : this(DefaultInternCapacity())
    {
    }

    /// <summary>A state whose <c>io</c>, <c>os</c> and file loading run over <paramref name="platform"/> (a sandbox removes capabilities).</summary>
    public LuaState(ILuaPlatform platform) : this(DefaultInternCapacity(), platform)
    {
    }

    /// <summary>The host platform under <c>io</c>, <c>os</c>, <c>loadfile</c>, <c>dofile</c> and <c>require</c>.</summary>
    public ILuaPlatform Platform { get; }

    /// <summary>CI variant hook: LUNETTE_INTERN_CAPACITY=1 proves the engine is correct without interning.</summary>
    private static int DefaultInternCapacity() =>
        int.TryParse(Environment.GetEnvironmentVariable("LUNETTE_INTERN_CAPACITY"), out int n) && n > 0 ? n : StringCache.DefaultCapacity;

    private readonly LuaStateOptions? _construction;

    private LuaValue[]? _typeNames;

    /// <summary>The string <c>type(v)</c> returns for <paramref name="v"/>, interned once per state (the in-place <c>type</c> fast path).</summary>
    internal LuaValue TypeNameValue(in LuaValue v)
    {
        var names = _typeNames ??= new LuaValue[8];
        int ix = v.Ref switch
        {
            null => 0,
            _ when v.IsNumber => 1,
            _ when v.IsBoolean => 2,
            LuaFunction => 3,
            LuaTable => 4,
            Vm.Coroutine => 5,
            byte[] => 6,
            _ => 7,
        };
        if (names[ix].IsNil) names[ix] = LuaValue.FromString(Strings.FromUtf16(v.TypeName));
        return names[ix];
    }

    /// <summary>Whether construction registers the global <paramref name="name"/>: always for a full state, only when allowlisted for a sandboxed one.</summary>
    internal bool Wants(string name) => _construction is null || _construction.IsAllowed(name);

    /// <summary>Registers a library or base function while the state is being built; a withheld name is dropped without ever being reachable.</summary>
    internal void Define(string name, LuaValue value)
    {
        if (Wants(name)) SetGlobal(name, value);
    }

    internal LuaState(int internCapacity, ILuaPlatform? platform = null, LuaStateOptions? options = null)
    {
        _construction = options;
        Platform = platform ?? new DefaultLuaPlatform();
        Strings = new StringCache(internCapacity);
        Gc = new Gc.GcState(this);
        Gc.WarnFallback = EmitGcWarning;
        Main.IsMain = true;
        Main.Status = CoStatus.Running;
        Running = Main;
        Define("_G", LuaValue.FromTable(Globals));
        Define("_VERSION", LuaValue.FromString("Lua 5.4"));
        if (Wants("print")) SetGlobal("print", LuaValue.FromObject(new BuiltinFunction(static (state, args) =>
        {
            if (state.OutputStream is { } raw)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) raw.WriteByte((byte)'\t');
                    raw.Write(Meta.ToStringValue(state, args[i]).Bytes);
                }
                raw.WriteByte((byte)'\n');
                raw.Flush(); // as lua_writeline's fflush: a pipe reader sees each print while the script keeps running
                return null;
            }
            var o = state.Output;
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) o.Write('\t');
                o.Write(Meta.ToStringValue(state, args[i]).ToString());
            }
            o.Write(o.NewLine);
            return null;
        })));
        BaseLib.Register(this);
        StringLib.Register(this);
        Utf8Lib.Register(this);
        MathLib.Register(this, options?.Seed);
        ErrorLib.Register(this);
        CoroutineLib.Register(this);
        TableLib.Register(this);
        GcLib.Register(this);
        DebugLib.Register(this);
        IoLib.Register(this);
        OsLib.Register(this);
        LoadLib.Register(this);
    }

    /// <summary>A closure for a compiled main chunk whose single upvalue (_ENV) holds <paramref name="env"/>.</summary>
    internal LuaClosure NewMainClosure(Proto proto, LuaValue env) => new(proto, [new UpVal { Value = env }]);

    /// <summary>Compiles a text chunk and returns it as a function value with the given environment.</summary>
    internal LuaClosure Load(byte[] source, string chunkName, LuaValue env) =>
        NewMainClosure(LuaCompiler.Compile(source, chunkName, Strings, this), env);

    private readonly Dictionary<BuiltinFunction, LuaClosure> _trampolines = new();

    /// <summary>
    /// A Lua closure that forwards its arguments to the host function <paramref name="fn"/>. Used when pcall/xpcall protect another
    /// pcall/xpcall, so the inner one runs as an ordinary yieldable Lua call.
    /// </summary>
    internal LuaClosure Trampoline(BuiltinFunction fn, string name)
    {
        if (_trampolines.TryGetValue(fn, out LuaClosure? cl)) return cl;
        var env = new LuaTable();
        env.Set(LuaValue.FromString(Strings.FromUtf16(name)), LuaValue.FromObject(fn));
        byte[] src = name switch
        {
            "pcall" => "return pcall(...)"u8.ToArray(),
            "xpcall" => "return xpcall(...)"u8.ToArray(),
            _ => "return host(...)"u8.ToArray(),
        };
        cl = Load(src, "=(" + name + ")", LuaValue.FromTable(env));
        _trampolines[fn] = cl;
        return cl;
    }

    /// <summary>
    /// Closes the state like <c>lua_close</c>: first closes the to-be-closed variables still pending on the main thread (only
    /// left over when a run ended with <c>os.exit</c>; a cleanly finished state has none, and suspended coroutines are not touched),
    /// then runs every remaining <c>__gc</c> in reverse marking order (errors are swallowed). An aborted state
    /// (<see cref="IsAborted"/>) runs no Lua code; it just drops every reference it holds. Safe to call repeatedly.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsAborted) DiscardAborted();
        else
        {
            BeginRun();
            Vm.Interpreter.CloseMainThread(this);
            Gc.Close();
        }
    }

    /// <summary>Text sink for <c>print</c> and <c>io.stdout</c>. Lua strings are bytes, so bytes that are not valid UTF-8 become U+FFFD here; set <see cref="OutputStream"/> to keep them. <c>io.write</c> and <c>io.stdout:write</c> do not flush this writer per call (like C stdio on a redirected stdout): they are flushed by <c>io.flush</c>, <c>setvbuf</c>, <c>io.stdout:close</c>, <see cref="Dispose"/> and the host; <c>print</c> on the <see cref="OutputStream"/> path flushes after each line. A host that needs bytes immediately should hand in an unbuffered writer or stream.</summary>
    public TextWriter Output { get; set; } = Console.Out;

    /// <summary>When set, <c>print</c> and <c>io.stdout</c> write their bytes here verbatim (no decoding) and <see cref="Output"/> is not used. The stream is flushed after each <c>print</c> and by <c>io.flush</c>/<c>setvbuf</c>/close/<see cref="Dispose"/>, but not after each <c>io.write</c>; the command-line driver passes a buffered stream when stdout is redirected and the raw stream on a terminal.</summary>
    public Stream? OutputStream { get; set; }

    /// <summary>Compiles and runs a chunk of Lua source (encoded as UTF-8).</summary>
    public void DoString(string source, string chunkName = "=(load)")
    {
        Proto proto = LuaCompiler.Compile(source, chunkName, Strings);
        RunTopLevel(proto);
    }

    /// <summary>Runs a chunk given as raw bytes with <paramref name="args"/> as its <c>...</c> (the command-line driver's script arguments).</summary>
    public void RunScript(byte[] source, string chunkName, IReadOnlyList<string> args)
    {
        Proto proto = LuaCompiler.Compile(source, chunkName, Strings);
        var values = new LuaValue[args.Count];
        for (int i = 0; i < values.Length; i++) values[i] = LuaValue.FromString(Strings.FromUtf16(args[i]));
        Main.CBase = true;
        Interpreter.Execute(this, proto, values);
    }

    /// <summary>Sets the global <c>arg</c> table the way the standalone interpreter does: arg[0] is the script, negatives are the interpreter and options.</summary>
    public void SetCommandLineArgs(IReadOnlyList<string> all, int scriptIndex)
    {
        var t = new LuaTable();
        for (int i = 0; i < all.Count; i++)
            t.Set(LuaValue.FromInteger(i - scriptIndex), LuaValue.FromString(Strings.FromUtf16(all[i])));
        SetGlobal("arg", LuaValue.FromTable(t));
    }

    /// <summary>Compiles and runs a chunk of Lua source given as raw bytes.</summary>
    public void DoBytes(byte[] source, string chunkName = "=(load)")
    {
        Proto proto = LuaCompiler.Compile(source, chunkName, Strings);
        RunTopLevel(proto);
    }

    private void RunTopLevel(Proto proto)
    {
        ThrowIfAborted();
        // Called from a host function that is itself running under Lua: Execute resets the main stack, which would destroy the
        // caller's live frames (and it ignores the running coroutine), so run the chunk as an ordinary nested call.
        if (_runDepth > 0)
        {
            Call(NewMainClosure(proto, LuaValue.FromTable(Globals)));
            return;
        }
        BeginHostRun();
        try { Interpreter.Execute(this, proto); }
        finally { EndRun(); }
    }
}
