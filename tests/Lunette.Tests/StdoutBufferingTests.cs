using Lunette.Cli;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Seams: io.stdout write buffering over <c>LuaState.OutputStream</c> / <c>Output</c> and the CLI driver (ticket 59). A redirected stdout is
/// host-buffered: io.write does not flush the host stream per call (C stdio's block buffering), print keeps its fflush, and
/// setvbuf, io.flush, close, exit and error exit still deliver every byte.
/// </summary>
public class StdoutBufferingTests
{
    private sealed class CountingStream : MemoryStream
    {
        public int Flushes;
        public override void Flush() { Flushes++; base.Flush(); }
    }

    private static (LuaState State, CountingStream Sink) NewState()
    {
        var sink = new CountingStream();
        return (new LuaState { OutputStream = sink }, sink);
    }

    [Fact]
    public void Io_write_does_not_flush_the_host_stream_per_call()
    {
        var (state, sink) = NewState();
        state.DoString("for i = 1, 100 do io.write('line ', i, '\\n') end");
        Assert.True(sink.Flushes <= 2, $"flushed {sink.Flushes} times");
        Assert.Equal(100, sink.ToArray().Count(b => b == (byte)'\n'));
    }

    [Fact]
    public void Print_still_flushes_and_orders_after_io_write()
    {
        var (state, sink) = NewState();
        state.DoString("io.write('a') print('b') io.write('c')");
        Assert.Equal("ab\nc", System.Text.Encoding.ASCII.GetString(sink.ToArray()));
        state.DoString("print('x')");
        Assert.True(sink.Flushes >= 1);
    }

    [Fact]
    public void Io_flush_and_file_flush_flush_the_host_stream()
    {
        var (state, sink) = NewState();
        state.DoString("io.write('a')");
        int before = sink.Flushes;
        state.DoString("io.flush()");
        Assert.Equal(before + 1, sink.Flushes);
        state.DoString("io.stdout:flush()");
        Assert.Equal(before + 2, sink.Flushes);
    }

    [Fact]
    public void Setvbuf_no_flushes_after_every_write()
    {
        var (state, sink) = NewState();
        state.DoString("io.stdout:setvbuf('no')");
        int before = sink.Flushes;
        state.DoString("io.write('a') io.write('b') io.write('c')");
        Assert.True(sink.Flushes - before >= 3, $"flushed {sink.Flushes - before} times");
    }

    [Fact]
    public void Setvbuf_line_flushes_only_when_a_newline_was_written()
    {
        var (state, sink) = NewState();
        state.DoString("io.stdout:setvbuf('line') io.write('abc')");
        Assert.Empty(sink.ToArray());
        state.DoString("io.write('def\\n')");
        Assert.Equal("abcdef\n", System.Text.Encoding.ASCII.GetString(sink.ToArray()));
    }

    [Fact]
    public void Setvbuf_full_holds_bytes_until_flush_or_dispose()
    {
        var (state, sink) = NewState();
        state.DoString("io.stdout:setvbuf('full', 4096) io.write('held')");
        Assert.Empty(sink.ToArray());
        state.DoString("io.flush()");
        Assert.Equal("held", System.Text.Encoding.ASCII.GetString(sink.ToArray()));
        state.DoString("io.write('more')");
        Assert.Equal(4, sink.ToArray().Length);
        state.Dispose();
        Assert.Equal("heldmore", System.Text.Encoding.ASCII.GetString(sink.ToArray()));
    }

    [Fact]
    public void Io_write_returns_the_file_and_reports_argument_errors()
    {
        var (state, _) = NewState();
        state.DoString("assert(io.write() == io.stdout) assert(io.write('a', 1, 2.5) == io.stdout) assert(io.stdout:write('x') == io.stdout)");
        var e = Assert.Throws<LuaException>(() => state.DoString("io.write('ok', {})"));
        Assert.Contains("bad argument #2 to 'write' (string expected, got table)", e.Message);
    }

    [Fact]
    public void Io_write_to_a_failing_stream_returns_nil_message_errno()
    {
        var state = new LuaState { OutputStream = new ThrowingStream() };
        state.DoString("local ok, msg, code = io.write('x') assert(ok == nil and type(msg) == 'string' and math.type(code) == 'integer')");
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("disk full");
    }

    [Fact]
    public void Text_writer_output_receives_io_write_bytes_without_a_flush_per_write()
    {
        var sw = new FlushCountingWriter();
        var state = new LuaState { Output = sw };
        state.DoString("for i = 1, 50 do io.write('é', i) end");
        Assert.True(sw.Flushes <= 2, $"flushed {sw.Flushes} times");
        Assert.StartsWith("é1é2", sw.ToString());
    }

    private sealed class FlushCountingWriter : StringWriter
    {
        public int Flushes;
        public override void Flush() { Flushes++; base.Flush(); }
    }

    [Fact]
    public void The_cli_flushes_buffered_output_on_normal_exit_os_exit_and_error_exit()
    {
        foreach ((string code, int expectedExit) in new[]
        {
            ("io.write('done')", 0),
            ("io.write('done') os.exit(3)", 3),
            ("io.write('done') os.exit(4, true)", 4),
            ("io.write('done') error('boom')", 1),
        })
        {
            var sink = new MemoryStream();
            int exit = Program.Run(["-e", code], new BufferedOnlyStream(sink), new StringWriter());
            Assert.Equal(expectedExit, exit);
            Assert.Equal("done", System.Text.Encoding.ASCII.GetString(sink.ToArray()));
        }
    }

    /// <summary>Like a BufferedStream: bytes reach the sink only on Flush.</summary>
    private sealed class BufferedOnlyStream(MemoryStream sink) : Stream
    {
        private readonly MemoryStream _pending = new();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { _pending.WriteTo(sink); _pending.SetLength(0); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _pending.Write(buffer, offset, count);
    }
}
