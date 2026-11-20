using Lunette.Compiler;
using Lunette.Lib;
using Lunette.Vm;

namespace Lunette;

/// <summary>The single-threaded embedding root: owns globals and the output sink.</summary>
public sealed class LuaState
{
    /// <summary>The global environment table (_G): the initial value of every main chunk's _ENV upvalue.</summary>
    internal readonly LuaTable Globals = new();
    /// <summary>Best-effort intern cache for short strings (ADR 0005).</summary>
    internal readonly StringCache Strings;
    /// <summary>The metatable shared by all string values; its __index is the string library table.</summary>
    internal LuaTable? StringMetatable;
    internal readonly Coroutine Main = new();
    /// <summary>The coroutine currently executing.</summary>
    internal Coroutine Running;
    /// <summary>Depth of nested native-to-Lua calls in progress.</summary>
    internal int NativeDepth;
    /// <summary>The <c>package</c> table (require reads it from here, not from the global, as PUC reads its registry).</summary>
    internal LuaTable? Package;
    /// <summary>Whether warn() output is enabled (toggled by warn("@on") / warn("@off")).</summary>
    internal bool WarningsOn;
    /// <summary>Host sink for warnings; null writes to standard error.</summary>
    internal Action<string>? Warn;

    public LuaState() : this(DefaultInternCapacity())
    {
    }

    /// <summary>CI variant hook: LUNETTE_INTERN_CAPACITY=1 proves the engine is correct without interning.</summary>
    private static int DefaultInternCapacity() =>
        int.TryParse(Environment.GetEnvironmentVariable("LUNETTE_INTERN_CAPACITY"), out int n) && n > 0 ? n : StringCache.DefaultCapacity;

    internal LuaState(int internCapacity)
    {
        Strings = new StringCache(internCapacity);
        Main.IsMain = true;
        Main.Status = CoStatus.Running;
        Running = Main;
        SetGlobal("_G", LuaValue.FromTable(Globals));
        SetGlobal("_VERSION", LuaValue.FromString("Lua 5.4"));
        SetGlobal("print", LuaValue.FromObject(new BuiltinFunction(static (state, args) =>
        {
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
        MathLib.Register(this);
        ErrorLib.Register(this);
        CoroutineLib.Register(this);
        TableLib.Register(this);
        LoadLib.Register(this);
    }

    internal void SetGlobal(string name, LuaValue value) =>
        Globals.Set(LuaValue.FromString(Strings.FromUtf16(name)), value);

    /// <summary>A closure for a compiled main chunk whose single upvalue (_ENV) holds <paramref name="env"/>.</summary>
    internal LuaClosure NewMainClosure(Proto proto, LuaValue env) => new(proto, [new UpVal { Value = env }]);

    /// <summary>Compiles a text chunk and returns it as a function value with the given environment.</summary>
    internal LuaClosure Load(byte[] source, string chunkName, LuaValue env) =>
        NewMainClosure(LuaCompiler.Compile(source, chunkName, Strings), env);

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
        byte[] src = name == "pcall" ? "return pcall(...)"u8.ToArray() : "return xpcall(...)"u8.ToArray();
        cl = Load(src, "=(" + name + ")", LuaValue.FromTable(env));
        _trampolines[fn] = cl;
        return cl;
    }

    public TextWriter Output { get; set; } = Console.Out;

    /// <summary>Compiles and runs a chunk of Lua source (encoded as UTF-8).</summary>
    public void DoString(string source, string chunkName = "=(load)")
    {
        Proto proto = LuaCompiler.Compile(source, chunkName, Strings);
        Interpreter.Execute(this, proto);
    }

    /// <summary>Runs a chunk given as raw bytes with <paramref name="args"/> as its <c>...</c> (the command-line driver's script arguments).</summary>
    public void DoBytes(byte[] source, string chunkName, IReadOnlyList<string> args)
    {
        Proto proto = LuaCompiler.Compile(source, chunkName, Strings);
        var values = new LuaValue[args.Count];
        for (int i = 0; i < values.Length; i++) values[i] = LuaValue.FromString(Strings.FromUtf16(args[i]));
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
        Interpreter.Execute(this, proto);
    }
}
