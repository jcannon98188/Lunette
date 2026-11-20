using System.Text;

namespace Lunette.Cli;

/// <summary>The default platform, except that <c>io.stderr</c> writes to the driver's error writer (so embedders and tests can capture it).</summary>
internal sealed class CliPlatform(TextWriter stderr) : ILuaPlatform
{
    private readonly DefaultLuaPlatform _inner = new();

    public PlatformCapabilities Capabilities => _inner.Capabilities;
    public Stream OpenFile(string path, FileMode mode, FileAccess access) => _inner.OpenFile(path, mode, access);
    public void Remove(string path) => _inner.Remove(path);
    public void Rename(string from, string to) => _inner.Rename(from, to);
    public string CreateTempFile() => _inner.CreateTempFile();
    public Stream OpenStandardInput() => _inner.OpenStandardInput();
    public Stream OpenStandardError() => ReferenceEquals(stderr, Console.Error) ? _inner.OpenStandardError() : new WriterStream(stderr);
    public IPlatformProcess StartProcess(string command, bool forReading) => _inner.StartProcess(command, forReading);
    public ProcessStatus RunShell(string command) => _inner.RunShell(command);
    public bool HasShell => _inner.HasShell;
    public string? GetEnvironmentVariable(string name) => _inner.GetEnvironmentVariable(name);
    public double CpuTimeSeconds() => _inner.CpuTimeSeconds();
    public DateTimeOffset UtcNow => _inner.UtcNow;
    public TimeZoneInfo TimeZone => _inner.TimeZone;

    private sealed class WriterStream(TextWriter writer) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => writer.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => writer.Write(Encoding.UTF8.GetString(buffer, offset, count));
    }
}
