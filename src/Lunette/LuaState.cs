using Lunette.Compiler;
using Lunette.Lib;
using Lunette.Vm;

namespace Lunette;

/// <summary>The single-threaded embedding root: owns globals and the output sink.</summary>
public sealed class LuaState
{
    internal readonly Dictionary<string, LuaValue> Globals = new();
    /// <summary>Best-effort intern cache for short strings (ADR 0005).</summary>
    internal readonly StringCache Strings;
    /// <summary>The metatable shared by all string values; its __index is the string library table.</summary>
    internal LuaTable? StringMetatable;
    /// <summary>The registry table (debug.getregistry).</summary>
    internal LuaTable? Registry;
    /// <summary>Metatables shared by all values of nil, boolean, number, function, thread and userdata (debug.setmetatable).</summary>
    private LuaTable?[]? _typeMetatables;

    internal LuaTable? TypeMetatable(LuaValue v) => _typeMetatables?[TypeSlot(v)];

    internal void SetTypeMetatable(LuaValue v, LuaTable? mt)
    {
        _typeMetatables ??= new LuaTable?[6];
        _typeMetatables[TypeSlot(v)] = mt;
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
        Globals["print"] = LuaValue.FromObject(new BuiltinFunction(static (state, args) =>
        {
            var o = state.Output;
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) o.Write('\t');
                o.Write(Meta.ToStringValue(state, args[i]).ToString());
            }
            o.Write(o.NewLine);
            return null;
        }));
        BaseLib.Register(this);
        StringLib.Register(this);
        Utf8Lib.Register(this);
        MathLib.Register(this);
        ErrorLib.Register(this);
        CoroutineLib.Register(this);
        DebugLib.Register(this);
    }

    public TextWriter Output { get; set; } = Console.Out;

    /// <summary>Compiles and runs a chunk of Lua source (encoded as UTF-8).</summary>
    public void DoString(string source, string chunkName = "=(load)")
    {
        Proto proto = LuaCompiler.Compile(source, chunkName, Strings);
        Interpreter.Execute(this, proto);
    }

    /// <summary>Compiles and runs a chunk of Lua source given as raw bytes.</summary>
    public void DoBytes(byte[] source, string chunkName = "=(load)")
    {
        Proto proto = LuaCompiler.Compile(source, chunkName, Strings);
        Interpreter.Execute(this, proto);
    }
}
