namespace Lunette.Cli;

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs the `lua`-compatible driver: <c>lunette script.lua</c>.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("usage: lunette script.lua");
            return 1;
        }
        string path = args[0];
        byte[] source;
        try
        {
            source = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"lunette: cannot open {path}");
            return 1;
        }
        var state = new LuaState { Output = stdout };
        try
        {
            state.DoBytes(source, "@" + path);
        }
        catch (LuaException e)
        {
            stderr.WriteLine($"lunette: {e.Message}");
            return 1;
        }
        return 0;
    }
}
