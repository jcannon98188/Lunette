using Lunette.Compiler;
using Lunette.Vm;

namespace Lunette;

/// <summary>The single-threaded embedding root: owns globals and the output sink.</summary>
public sealed class LuaState
{
    internal readonly Dictionary<string, LuaValue> Globals = new();
    internal readonly Coroutine Main = new();

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
            return null;
        }));
        Globals["assert"] = LuaValue.FromObject(new BuiltinFunction(static (_, args) =>
        {
            if (args.Length == 0) throw new LuaException("bad argument #1 to 'assert' (value expected)");
            if (args[0].IsFalsy)
                throw new LuaException(args.Length > 1 ? args[1].ToString() : "assertion failed!");
            return args.ToArray();
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
