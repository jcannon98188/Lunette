using System.Buffers;
using System.Text.Unicode;

namespace Lunette.Lib;

/// <summary>stdio buffering modes (<c>file:setvbuf</c>).</summary>
internal enum BufferMode { None, Full, Line }

/// <summary>
/// A byte-exact buffered file over a <see cref="Stream"/> (ADR: binary always, no newline translation on any OS).
/// Implements the stdio behaviour the <c>io</c> library needs: <c>setvbuf</c> modes, interleaved reads and writes
/// (reads flush pending output, writes drop read-ahead), seeking and the PUC read formats.
/// </summary>
internal sealed class LuaFile
{
    public const int DefaultBufferSize = 8192;
    private const int MaxNumberLength = 200;

    private readonly Stream _stream;
    private readonly bool _append;
    private byte[]? _read; // allocated on the first read: write-only files and stdout/stderr never pay for it
    private int _readPos, _readLen;
    // True for the default state of a host-backed standard output (stdout over LuaState.OutputStream/Output): writes go straight to the host
    // stream without flushing it, so a redirected stdout is block-buffered by the host (C stdio's choice) and a terminal, whose stream is
    // unbuffered, still sees every write. An explicit setvbuf, io.flush, close and exit flush it; print keeps its own fflush.
    private bool _hostBuffered;
    private byte[]? _write;
    private int _writeLen;
    private int _bufferSize = DefaultBufferSize;

    public LuaFile(Stream stream, bool append = false, bool standard = false, IPlatformProcess? process = null, bool hostBuffered = false)
    {
        _hostBuffered = hostBuffered;
        _stream = stream;
        _append = append;
        IsStandard = standard;
        Process = process;
        Mode = standard ? BufferMode.None : BufferMode.Full;
    }

    public bool IsClosed { get; private set; }

    /// <summary>Called once when the file closes (the library uses it to drop its strong reference).</summary>
    public Action<LuaFile>? OnClose { get; set; }
    public bool IsStandard { get; }
    public IPlatformProcess? Process { get; }
    public BufferMode Mode { get; private set; }

    // ---- buffering ----

    public void SetBuffering(BufferMode mode, int size)
    {
        Flush();
        _hostBuffered = false; // an explicit setvbuf takes over: "no" flushes per write, "line"/"full" use our own buffer
        Mode = mode;
        _bufferSize = size > 0 ? size : DefaultBufferSize;
        if (_write is not null && _write.Length < _bufferSize) _write = null;
    }

    public void Flush()
    {
        if (_writeLen > 0)
        {
            int n = _writeLen;
            _writeLen = 0;
            WriteThrough(_write.AsSpan(0, n), flushHost: true);
        }
        else if (_stream.CanWrite) _stream.Flush();
    }

    private void WriteThrough(ReadOnlySpan<byte> data, bool flushHost = false)
    {
        if (!_stream.CanWrite) throw new ErrnoException(Errno.EBADF);
        if (_append && _stream.CanSeek) _stream.Seek(0, SeekOrigin.End);
        _stream.Write(data);
        if (flushHost || !_hostBuffered) _stream.Flush();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (!_stream.CanWrite) throw new ErrnoException(Errno.EBADF);
        DropReadAhead();
        if (Mode == BufferMode.None)
        {
            WriteThrough(data);
            return;
        }
        _write ??= new byte[_bufferSize];
        if (_writeLen + data.Length > _write.Length)
        {
            Flush();
            if (data.Length >= _write.Length)
            {
                WriteThrough(data, flushHost: true);
                return;
            }
        }
        data.CopyTo(_write.AsSpan(_writeLen));
        _writeLen += data.Length;
        if (Mode == BufferMode.Line && data.Contains((byte)'\n')) Flush();
    }

    /// <summary>Gives unread read-ahead back to the stream so a following write or seek sees the logical position.</summary>
    private void DropReadAhead()
    {
        int unread = _readLen - _readPos;
        _readPos = _readLen = 0;
        if (unread > 0 && _stream.CanSeek) _stream.Seek(-unread, SeekOrigin.Current);
    }

    // ---- reading ----

    private bool Fill()
    {
        if (_readPos < _readLen) return true;
        if (!_stream.CanRead) throw new ErrnoException(Errno.EBADF);
        if (_writeLen > 0) Flush();
        _readPos = 0;
        byte[] buf = _read ??= new byte[DefaultBufferSize];
        _readLen = _stream.Read(buf, 0, buf.Length);
        return _readLen > 0;
    }

    /// <summary>The next byte without consuming it, or -1 at end of file.</summary>
    public int Peek() => Fill() ? _read![_readPos] : -1;

    private int Next() => Fill() ? _read![_readPos++] : -1;

    public bool AtEof() => !Fill();

    /// <summary>Reads a line; returns false at end of file with nothing read.</summary>
    public bool ReadLine(ref ByteBuilder buffer, bool chop)
    {
        bool any = false;
        while (Fill())
        {
            ReadOnlySpan<byte> avail = _read!.AsSpan(_readPos, _readLen - _readPos);
            int nl = avail.IndexOf((byte)'\n');
            any = true;
            if (nl < 0)
            {
                buffer.Append(avail);
                _readPos = _readLen;
                continue;
            }
            buffer.Append(chop ? avail[..nl] : avail[..(nl + 1)]);
            _readPos += nl + 1;
            return true;
        }
        return any;
    }

    public void ReadAll(ref ByteBuilder buffer)
    {
        while (Fill())
        {
            buffer.Append(_read!.AsSpan(_readPos, _readLen - _readPos));
            _readPos = _readLen;
        }
    }

    /// <summary>Reads up to <paramref name="count"/> bytes; returns false when none were available.</summary>
    public bool ReadCount(ref ByteBuilder buffer, long count)
    {
        long left = count;
        while (left > 0 && Fill())
        {
            int take = (int)Math.Min(left, _readLen - _readPos);
            buffer.Append(_read!.AsSpan(_readPos, take));
            _readPos += take;
            left -= take;
        }
        return left != count;
    }

    private static bool IsSpace(int c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';
    private static bool IsDigit(int c) => c is >= '0' and <= '9';
    private static bool IsHex(int c) => IsDigit(c) || c is >= 'a' and <= 'f' || c is >= 'A' and <= 'F';

    /// <summary>
    /// PUC's <c>l_getn</c>: reads the longest prefix that looks like a numeral (at most 200 characters) and leaves the rest.
    /// Returns the candidate text, or null when it was too long; the caller converts it and fails if it is not a number.
    /// </summary>
    public string? ReadNumeral()
    {
        byte[] buf = new byte[MaxNumberLength];
        int n = 0;
        int c;
        do { c = Next(); } while (IsSpace(c));
        if (c >= 0) _readPos--; // un-consume the look-ahead; Peek/Next below mirror getc/ungetc
        bool tooLong = false;

        bool Take(ReadOnlySpan<char> set)
        {
            int p = Peek();
            if (p < 0 || (p != set[0] && p != set[1])) return false;
            if (n >= MaxNumberLength) { tooLong = true; return false; }
            buf[n++] = (byte)p;
            _readPos++;
            return true;
        }

        int Digits(bool hex)
        {
            int count = 0;
            while (true)
            {
                int p = Peek();
                if (!(hex ? IsHex(p) : IsDigit(p))) break;
                if (n >= MaxNumberLength) { tooLong = true; break; }
                buf[n++] = (byte)p;
                _readPos++;
                count++;
            }
            return count;
        }

        int count = 0;
        bool isHex = false;
        Take("-+");
        if (Take("00"))
        {
            if (Take("xX")) isHex = true;
            else count = 1;
        }
        count += Digits(isHex);
        if (Take("..")) count += Digits(isHex);
        if (count > 0 && Take(isHex ? "pP" : "eE"))
        {
            Take("-+");
            Digits(false);
        }
        if (tooLong) return null;
        var chars = new char[n];
        for (int i = 0; i < n; i++) chars[i] = (char)buf[i];
        return new string(chars);
    }

    // ---- seeking ----

    public long Seek(int whence, long offset)
    {
        if (!_stream.CanSeek) throw new ErrnoException(Errno.ESPIPE);
        Flush();
        int unread = _readLen - _readPos;
        _readPos = _readLen = 0;
        if (whence == 1) offset -= unread;
        else if (whence == 0 && offset < 0) throw new ErrnoException(Errno.EINVAL);
        return _stream.Seek(offset, whence switch { 0 => SeekOrigin.Begin, 1 => SeekOrigin.Current, _ => SeekOrigin.End });
    }

    // ---- closing ----

    /// <summary>Flushes and releases the file; for a pipe also waits for the child and returns its status.</summary>
    public ProcessStatus? Close()
    {
        if (IsClosed) return null;
        ProcessStatus? status = null;
        try { Flush(); }
        finally
        {
            IsClosed = true;
            OnClose?.Invoke(this);
            if (Process is { } p)
            {
                try { status = p.Wait(); }
                finally { p.Dispose(); }
            }
            else _stream.Dispose();
        }
        return status;
    }
}

/// <summary>A write-only stream that forwards UTF-8 bytes into a <see cref="TextWriter"/> (the state's current <c>Output</c>, shared with <c>print</c>).</summary>
internal sealed class TextWriterStream(LuaState state) : Stream
{
    private readonly byte[] _carry = new byte[4];
    private int _carryLen;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush()
    {
        if (state.OutputStream is { } raw) raw.Flush();
        else state.Output.Flush();
    }

    public override void Write(ReadOnlySpan<byte> data)
    {
        if (state.OutputStream is { } raw)
        {
            raw.Write(data);
            return;
        }
        byte[] input = data.ToArray();
        int start = 0;
        if (_carryLen > 0)
        {
            byte[] joined = new byte[_carryLen + data.Length];
            _carry.AsSpan(0, _carryLen).CopyTo(joined);
            data.CopyTo(joined.AsSpan(_carryLen));
            input = joined;
            _carryLen = 0;
        }
        char[] chars = ArrayPool<char>.Shared.Rent(input.Length);
        try
        {
            OperationStatus status = Utf8.ToUtf16(input.AsSpan(start), chars, out int read, out int written, replaceInvalidSequences: true, isFinalBlock: false);
            state.Output.Write(chars, 0, written);
            if (status == OperationStatus.NeedMoreData)
            {
                _carryLen = input.Length - start - read;
                input.AsSpan(start + read).CopyTo(_carry);
            }
        }
        finally { ArrayPool<char>.Shared.Return(chars); }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
