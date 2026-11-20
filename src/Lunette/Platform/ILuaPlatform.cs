namespace Lunette;

/// <summary>The independently removable capabilities of an <see cref="ILuaPlatform"/> (sandboxing switches).</summary>
[Flags]
public enum PlatformCapabilities
{
    None = 0,
    /// <summary>File system access: <c>io.open</c>, <c>io.tmpfile</c>, <c>os.remove</c>, <c>os.rename</c>, <c>os.tmpname</c>, <c>loadfile</c>, <c>dofile</c>, <c>require</c> from files.</summary>
    Files = 1,
    /// <summary>Process spawning: <c>io.popen</c> and <c>os.execute</c>.</summary>
    Processes = 2,
    /// <summary>Environment variables: <c>os.getenv</c>.</summary>
    Environment = 4,
    /// <summary>Wall and CPU clocks: <c>os.time</c>, <c>os.date</c>, <c>os.clock</c>.</summary>
    Clock = 8,
}

/// <summary>How a child process ended, in the shape of Lua's <c>"exit"</c> / <c>"signal"</c> status.</summary>
public readonly struct ProcessStatus(bool signaled, int code)
{
    /// <summary>True when the process was killed by a signal rather than exiting.</summary>
    public bool Signaled { get; } = signaled;

    /// <summary>The exit code, or the signal number when <see cref="Signaled"/>.</summary>
    public int Code { get; } = code;
}

/// <summary>A running <c>io.popen</c> child: the pipe end Lua reads or writes, and its exit status.</summary>
public interface IPlatformProcess : IDisposable
{
    /// <summary>The pipe: the child's stdout when opened for reading, its stdin when opened for writing.</summary>
    Stream Stream { get; }

    /// <summary>Closes the pipe, waits for the child and reports how it ended.</summary>
    ProcessStatus Wait();
}

/// <summary>
/// The host-replaceable seam under the <c>io</c> and <c>os</c> libraries (and file loading). Every operation reports failure
/// by throwing <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/> or <see cref="NotSupportedException"/>;
/// the libraries translate those into PUC's <c>nil, message, errno</c>. File paths are .NET strings (decoded from strict UTF-8).
/// </summary>
public interface ILuaPlatform
{
    /// <summary>The capabilities present. Functions of a missing capability are absent from the library table or fail cleanly.</summary>
    PlatformCapabilities Capabilities { get; }

    /// <summary>Opens a file; the library layers its own byte-exact buffering on top, so the stream should be unbuffered.</summary>
    Stream OpenFile(string path, FileMode mode, FileAccess access);

    /// <summary>Removes a file or an empty directory (C <c>remove</c>).</summary>
    void Remove(string path);

    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/>, replacing an existing destination.</summary>
    void Rename(string from, string to);

    /// <summary>Creates a new, unique, empty temporary file and returns its path.</summary>
    string CreateTempFile();

    /// <summary>The raw standard input stream.</summary>
    Stream OpenStandardInput();

    /// <summary>The raw standard error stream.</summary>
    Stream OpenStandardError();

    /// <summary>Starts <paramref name="command"/> in the system shell with a pipe for reading its output or writing its input.</summary>
    IPlatformProcess StartProcess(string command, bool forReading);

    /// <summary>Runs <paramref name="command"/> in the system shell and waits for it.</summary>
    ProcessStatus RunShell(string command);

    /// <summary>Whether a system shell is available (<c>os.execute()</c> with no argument).</summary>
    bool HasShell { get; }

    /// <summary>An environment variable, or null when unset.</summary>
    string? GetEnvironmentVariable(string name);

    /// <summary>Process CPU time in seconds (<c>os.clock</c>).</summary>
    double CpuTimeSeconds();

    /// <summary>The current time (<c>os.time()</c>, <c>os.date</c>).</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>The local time zone used by <c>os.date</c> and <c>os.time</c>.</summary>
    TimeZoneInfo TimeZone { get; }
}

/// <summary>
/// Thrown to the host by <c>os.exit</c> instead of ending the process. Like <see cref="LuaAbortException"/> it is not a
/// <see cref="LuaException"/>: no Lua construct can catch it. The command-line driver honours it by exiting with <see cref="Code"/>.
/// </summary>
public sealed class LuaExitException : Exception
{
    internal LuaExitException(int code, bool close) : base($"exit requested with code {code}")
    {
        Code = code;
        Close = close;
    }

    /// <summary>The requested process exit code.</summary>
    public int Code { get; }

    /// <summary>True when the script asked for the state to be closed first (<c>os.exit(code, true)</c>).</summary>
    public bool Close { get; }
}
