using Lunette.Compiler;
using Lunette.Vm;

namespace Lunette;

/// <summary>The single-threaded embedding root: owns globals and the output sink.</summary>
public sealed class LuaState
{
    internal readonly Dictionary<string, LuaValue> Globals = new();
    internal readonly Coroutine Main = new();
    /// <summary>The coroutine currently executing.</summary>
    internal Coroutine Running => Main;
    /// <summary>Depth of nested native-to-Lua calls in progress.</summary>
    internal int NativeDepth;

    public LuaState()
    {
        Globals["print"] = LuaValue.FromObject(new BuiltinFunction(static (state, args) =>
        {
            var o = state.Output;
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) o.Write('\t');
                o.Write(Meta.ToStringValue(state, args[i]));
            }
            o.Write(o.NewLine);
            return null;
        }));
        BaseLib.Register(this);
        ErrorLib.Register(this);
    }

    public TextWriter Output { get; set; } = Console.Out;

    /// <summary>Compiles and runs a chunk of Lua source.</summary>
    public void DoString(string source, string chunkName = "=(load)")
    {
        Proto proto = LuaCompiler.Compile(source, chunkName);
        Interpreter.Execute(this, proto);
    }
}
