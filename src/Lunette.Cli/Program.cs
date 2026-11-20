using System.Runtime.InteropServices;
using System.Text;

namespace Lunette.Cli;

public static class Program
{
    /// <summary>Nested coroutine resumes cost about 5KB of .NET stack each (200-level limit), so the interpreter gets a big stack.</summary>
    private const int InterpreterStackBytes = 256 * 1024 * 1024;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        // The name scripts and error messages see as argv[0]: the launcher's own path, unless we are running under the 'dotnet' host.
        // A wrapper script that launches the dll can name itself through LUNETTE_PROGNAME.
        string? path = Environment.GetEnvironmentVariable("LUNETTE_PROGNAME") ?? Environment.ProcessPath;
        string progName = path is not null && !Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? path
            : "lunette";
        int code = 1;
        // Raw bytes, not Console.Out: Lua strings are bytes and must reach stdout unchanged. A terminal gets every write at once;
        // a pipe or file is buffered and flushed at exit.
        Stream raw = Console.OpenStandardOutput();
        Stream stdout = Console.IsOutputRedirected ? new BufferedStream(raw, 16 * 1024) : raw;
        var worker = new Thread(() => code = Run(args, stdout, Console.Error, progName), InterpreterStackBytes);
        worker.Start();
        worker.Join();
        return code;
    }

    private const string Usage = "usage: lunette [options] [script [args]]\n" +
        "Available options are:\n" +
        "  -e stat   execute string 'stat'\n" +
        "  -i        enter interactive mode after executing 'script'\n" +
        "  -l mod    require library 'mod' into global 'mod'\n" +
        "  -l g=mod  require library 'mod' into global 'g'\n" +
        "  -v        show version information\n" +
        "  -E        ignore environment variables\n" +
        "  -W        turn warnings on\n" +
        "  --        stop handling options\n" +
        "  -         stop handling options and execute stdin";

    /// <summary>Runs the <c>lua</c>-compatible driver: <c>lunette [options] [script [args]]</c>.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, string progName = "lunette") =>
        Run(args, stdout, null, stderr, progName);

    /// <summary>Like <see cref="Run(string[], TextWriter, TextWriter, string)"/>, but <c>print</c> and <c>io.stdout</c> write their bytes verbatim to <paramref name="stdout"/>.</summary>
    public static int Run(string[] args, Stream stdout, TextWriter stderr, string progName = "lunette") =>
        Run(args, null, stdout, stderr, progName);

    private static int Run(string[] args, TextWriter? stdoutText, Stream? stdoutBytes, TextWriter stderr, string progName)
    {
        // Pass 1: find the script and validate options, like lua.c's collectargs.
        bool hasI = false, hasV = false, hasE = false, noEnv = false;
        int i = 0;
        for (; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Length == 0 || a[0] != '-') break;
            if (a == "--") { i++; break; }
            if (a == "-") break;
            switch (a[1])
            {
                case 'e' or 'l':
                    if (a[1] == 'e') hasE = true;
                    if (a.Length == 2)
                    {
                        if (i + 1 >= args.Length) return Fail(stderr, progName, $"'{a}' needs argument");
                        i++;
                    }
                    break;
                case 'i' when a.Length == 2: hasI = hasV = true; break;
                case 'v' when a.Length == 2: hasV = true; break;
                case 'E' when a.Length == 2: noEnv = true; break;
                case 'W' when a.Length == 2: break;
                default: return Fail(stderr, progName, $"unrecognized option '{a}'");
            }
        }
        int scriptIndex = i;
        bool hasScript = scriptIndex < args.Length;

        var actions = new List<(char, string)>();
        for (int j = 0; j < scriptIndex; j++)
        {
            string a = args[j];
            if (a == "--") break;
            switch (a[1])
            {
                case 'W': actions.Add(('W', "")); break;
                case 'e' or 'l': actions.Add((a[1], a.Length > 2 ? a[2..] : args[++j])); break;
            }
        }

        string? script = hasScript ? args[scriptIndex] : null;
        bool scriptIsStdin = script == "-" && (scriptIndex == 0 || args[scriptIndex - 1] != "--");
        bool stdinTty = !Console.IsInputRedirected;
        var options = new Driver.Options(progName, hasI, hasV, hasE, noEnv, stdinTty, actions, script, scriptIsStdin);

        var state = new LuaState(new CliPlatform(stderr)) { OutputStream = stdoutBytes };
        if (stdoutText is not null) state.Output = stdoutText;
        state.SetCommandLineArgs([progName, .. args], hasScript ? scriptIndex + 1 : 0);
        // Ctrl-C as in lua.c: the first SIGINT raises one "interrupted" error in the script; a second takes the default action.
        int sigints = 0;
        // A shell starts background jobs with SIGINT ignored and .NET honours that; lua.c installs its handler regardless.
        if (!OperatingSystem.IsWindows()) ResetSigint();
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            if (Interlocked.Increment(ref sigints) != 1) return;
            context.Cancel = true;
            state.RequestInterruptOnce();
        });
        bool close = true; // os.exit without its close argument leaves the state open, as C exit() does
        try
        {
            state.DoBytes(Driver.Build(options), "=lunette");
            return 0;
        }
        catch (LuaExitException e)
        {
            close = e.Close;
            return e.Code;
        }
        catch (LuaException e)
        {
            stderr.WriteLine($"{progName}: {e.Describe(state)}");
            return 1;
        }
        finally
        {
            stdoutText?.Flush();
            stdoutBytes?.Flush();
            if (close) state.Dispose();
            stdoutText?.Flush();
            stdoutBytes?.Flush();
        }
    }

    private const int SigInt = 2;

#pragma warning disable SYSLIB1054 // a blittable libc call; the LibraryImport generator would need AllowUnsafeBlocks
    [DllImport("libc", EntryPoint = "signal")]
    private static extern IntPtr CSignal(int signum, IntPtr handler);
#pragma warning restore SYSLIB1054

    private static void ResetSigint()
    {
        try { CSignal(SigInt, IntPtr.Zero); } // SIG_DFL
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private static int Fail(TextWriter stderr, string progName, string message)
    {
        stderr.WriteLine($"{progName}: {message}");
        stderr.WriteLine(Usage);
        return 1;
    }
}
