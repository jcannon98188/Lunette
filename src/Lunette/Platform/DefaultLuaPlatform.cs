using System.Diagnostics;

namespace Lunette;

/// <summary>
/// The default <see cref="ILuaPlatform"/> over <c>System.IO</c> and <c>System.Diagnostics.Process</c>. Pass a reduced
/// <see cref="PlatformCapabilities"/> to sandbox a state: the removed capabilities' functions are absent or fail cleanly.
/// </summary>
public sealed class DefaultLuaPlatform : ILuaPlatform
{
    /// <summary>A platform with every capability.</summary>
    public DefaultLuaPlatform() : this(PlatformCapabilities.Files | PlatformCapabilities.Processes | PlatformCapabilities.Environment | PlatformCapabilities.Clock)
    {
    }

    /// <summary>A platform with only the given capabilities.</summary>
    public DefaultLuaPlatform(PlatformCapabilities capabilities) => Capabilities = capabilities;

    public PlatformCapabilities Capabilities { get; }

    public Stream OpenFile(string path, FileMode mode, FileAccess access) =>
        new FileStream(path, mode, access, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);

    public void Remove(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: false);
        else if (File.Exists(path)) File.Delete(path);
        else throw new FileNotFoundException("no such file", path);
    }

    public void Rename(string from, string to)
    {
        if (Directory.Exists(from) && !File.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to, overwrite: true);
    }

    public string CreateTempFile() => Path.GetTempFileName();

    public Stream OpenStandardInput() => Console.OpenStandardInput();

    public Stream OpenStandardError() => Console.OpenStandardError();

    public bool HasShell => OperatingSystem.IsWindows() || File.Exists("/bin/sh");

    private static ProcessStartInfo ShellInfo(string command)
    {
        var info = new ProcessStartInfo { UseShellExecute = false };
        if (OperatingSystem.IsWindows())
        {
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.Arguments = "/s /c \"" + command + "\"";
        }
        else
        {
            info.FileName = "/bin/sh";
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(command);
        }
        return info;
    }

    /// <summary>On Unix .NET reports a signal death as 128 + signal; Windows has no signals.</summary>
    private static ProcessStatus StatusOf(int exitCode) =>
        !OperatingSystem.IsWindows() && exitCode is > 128 and <= 128 + 64
            ? new ProcessStatus(true, exitCode - 128)
            : new ProcessStatus(false, exitCode);

    public IPlatformProcess StartProcess(string command, bool forReading)
    {
        ProcessStartInfo info = ShellInfo(command);
        if (forReading) info.RedirectStandardOutput = true;
        else info.RedirectStandardInput = true;
        Process p = Process.Start(info) ?? throw new IOException("cannot start process");
        return new PipeProcess(p, forReading ? p.StandardOutput.BaseStream : p.StandardInput.BaseStream);
    }

    public ProcessStatus RunShell(string command)
    {
        using Process p = Process.Start(ShellInfo(command)) ?? throw new IOException("cannot start process");
        p.WaitForExit();
        return StatusOf(p.ExitCode);
    }

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public double CpuTimeSeconds() => Environment.CpuUsage.TotalTime.TotalSeconds;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeZoneInfo TimeZone => TimeZoneInfo.Local;

    private sealed class PipeProcess(Process process, Stream stream) : IPlatformProcess
    {
        public Stream Stream { get; } = stream;

        public ProcessStatus Wait()
        {
            try { Stream.Dispose(); }
            catch (IOException) { /* a child that already exited may have closed its end */ }
            process.WaitForExit();
            return StatusOf(process.ExitCode);
        }

        public void Dispose() => process.Dispose();
    }
}
