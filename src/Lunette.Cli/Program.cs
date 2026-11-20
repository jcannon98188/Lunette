using System.Text;

namespace Lunette.Cli;

public static class Program
{
    /// <summary>Nested coroutine resumes cost about 5KB of .NET stack each (200-level limit), so the interpreter gets a big stack.</summary>
    private const int InterpreterStackBytes = 256 * 1024 * 1024;

    public static int Main(string[] args)
    {
        int code = 1;
        var worker = new Thread(() => code = Run(args, Console.Out, Console.Error), InterpreterStackBytes);
        worker.Start();
        worker.Join();
        return code;
    }

    private const string Usage = "usage: lunette [options] [script [args]]\n" +
        "Available options are:\n" +
        "  -e stat   execute string 'stat'\n" +
        "  -l mod    require library 'mod' into global 'mod'\n" +
        "  -v        show version information\n" +
        "  -E        ignore environment variables\n" +
        "  -W        turn warnings on\n" +
        "  --        stop handling options\n" +
        "  -         stop handling options and execute stdin";

    /// <summary>Runs the `lua`-compatible driver: <c>lunette [-e stat] [-l mod] [-v] [-W] [--] [script [args]]</c>.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        // Pass 1: find the script and validate options, like lua.c's collectargs.
        int i = 0;
        for (; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Length == 0 || a[0] != '-') break;
            if (a == "--") { i++; break; }
            if (a == "-") break;
            if (a is "-e" or "-l")
            {
                if (i + 1 >= args.Length) return Fail(stderr, $"'{a}' needs argument");
                i++;
            }
            else if (a.StartsWith("-e") || a.StartsWith("-l")) { }
            else if (a is not ("-v" or "-E" or "-W" or "-i")) return Fail(stderr, $"unrecognized option '{a}'");
        }
        int scriptIndex = i;
        bool hasScript = scriptIndex < args.Length;

        var state = new LuaState { Output = stdout };
        state.SetCommandLineArgs(["lunette", .. args], hasScript ? scriptIndex + 1 : 0);

        bool ranSomething = false;
        for (int j = 0; j < scriptIndex; j++)
        {
            string a = args[j];
            if (a == "--") break;
            string? value = null;
            if (a is "-e" or "-l") value = args[++j];
            else if (a.StartsWith("-e") || a.StartsWith("-l")) value = a[2..];
            if (a is "-v") { stdout.WriteLine("Lunette (Lua 5.4 compatible)"); ranSomething = true; }
            else if (a is "-W") state.DoString("warn('@on')");
            else if (a[1] == 'e')
            {
                ranSomething = true;
                if (!Protected(state, () => state.DoBytes(Encoding.UTF8.GetBytes(value!), "=(command line)"), stderr)) return 1;
            }
            else if (a[1] == 'l')
            {
                string mod = value!;
                string global = mod;
                int eq = mod.IndexOf('=');
                if (eq >= 0) { global = mod[..eq]; mod = mod[(eq + 1)..]; }
                string code = $"{global} = require('{mod.Replace("'", "\\'")}')";
                if (!Protected(state, () => state.DoString(code, "=(command line)"), stderr)) return 1;
            }
        }

        if (!hasScript)
        {
            if (ranSomething) return 0;
            stderr.WriteLine(Usage);
            return 1;
        }

        string path = args[scriptIndex];
        byte[] source;
        try
        {
            source = path == "-" ? ReadAll(Console.OpenStandardInput()) : File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"lunette: cannot open {path}");
            return 1;
        }
        source = SkipShebang(source);
        var scriptArgs = args[(scriptIndex + 1)..];
        string chunk = path == "-" ? "=stdin" : "@" + path;
        return Protected(state, () => state.RunScript(source, chunk, scriptArgs), stderr) ? 0 : 1;
    }

    private static int Fail(TextWriter stderr, string message)
    {
        stderr.WriteLine($"lunette: {message}");
        stderr.WriteLine(Usage);
        return 1;
    }

    private static bool Protected(LuaState state, Action run, TextWriter stderr)
    {
        try
        {
            run();
            return true;
        }
        catch (LuaException e)
        {
            stderr.WriteLine($"lunette: {e.Describe(state)}");
            if (e.Traceback is not null) stderr.WriteLine(e.Traceback);
            return false;
        }
    }

    private static byte[] ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A leading '#' line is dropped but its newline kept, so line numbers stay right.</summary>
    private static byte[] SkipShebang(byte[] source)
    {
        if (source.Length == 0 || source[0] != (byte)'#') return source;
        int n = Array.IndexOf(source, (byte)'\n');
        return n < 0 ? [] : source[n..];
    }
}
