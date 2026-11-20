using Lunette.Compiler;
using Lunette.Vm;

namespace Lunette;

/// <summary>The single-threaded embedding root: owns globals and the output sink.</summary>
public sealed class LuaState
{
    internal readonly Dictionary<string, LuaValue> Globals = new();

    public LuaState()
    {
        Globals["print"] = LuaValue.FromObject(new BuiltinFunction(static (state, args) =>
        {
            var o = state.Output;
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) o.Write('\t');
                o.Write(args[i].ToString());
            }
            o.Write(o.NewLine);
        }));
    }

    public TextWriter Output { get; set; } = Console.Out;

    /// <summary>Compiles and runs a chunk of Lua source.</summary>
    public void DoString(string source, string chunkName = "=(load)")
    {
        Proto proto = LuaCompiler.Compile(source, chunkName);
        Interpreter.Execute(this, proto);
    }
}
