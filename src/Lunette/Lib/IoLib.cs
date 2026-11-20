using System.Runtime.CompilerServices;
using static Lunette.Lib.LibArgs;

namespace Lunette.Lib;

/// <summary>
/// The Lua <c>io</c> library (PUC 5.4 liolib semantics) over <see cref="LuaFile"/> and the state's <see cref="ILuaPlatform"/>.
/// Files are always binary: <c>"r"</c> and <c>"rb"</c> are the same on every OS.
/// </summary>
internal sealed class IoLib
{
    private const int MaxLinesArgs = 250;

    private readonly LuaState _state;
    private readonly ILuaPlatform _platform;
    private readonly LuaTable _meta = new();
    private LuaUserdata _input = null!;
    private LuaUserdata _output = null!;

    private IoLib(LuaState state)
    {
        _state = state;
        _platform = state.Platform;
    }

    public static void Register(LuaState state)
    {
        var io = new IoLib(state);
        io.Install();
    }

    private LuaValue Str(string s) => LuaValue.FromString(_state.Strings.FromUtf16(s));

    private void Put(LuaTable t, string name, BuiltinBody body) => t.Set(Str(name), LuaValue.FromObject(new BuiltinFunction(body)));

    private void Install()
    {
        PlatformCapabilities caps = _platform.Capabilities;
        var lib = new LuaTable();
        var methods = new LuaTable();

        Put(lib, "close", IoClose);
        Put(lib, "flush", (_, _) => FlushResult(_output));
        Put(lib, "input", (_, a) => IoFile(a, input: true));
        Put(lib, "output", (_, a) => IoFile(a, input: false));
        Put(lib, "lines", IoLines);
        Put(lib, "read", (_, a) => ReadFormats(DefaultFile(_input, "input"), a, 0, "read"));
        Put(lib, "write", (_, a) => WriteValues(_output, DefaultFile(_output, "output"), a, 0, "write"));
        Put(lib, "type", IoType);
        Put(lib, "popen", IoPopen);
        if ((caps & PlatformCapabilities.Files) != 0)
        {
            Put(lib, "open", IoOpen);
            Put(lib, "tmpfile", IoTmpFile);
        }

        Put(methods, "close", FileClose);
        Put(methods, "flush", (_, a) => FlushResult(CheckUserdata(a, "flush")));
        Put(methods, "lines", (_, a) => LinesIterator(ToFile(CheckUserdata(a, "lines")), a, 1, toClose: false));
        Put(methods, "read", (_, a) => ReadFormats(ToFile(CheckUserdata(a, "read")), a, 1, "read"));
        Put(methods, "seek", FileSeek);
        Put(methods, "setvbuf", FileSetVBuf);
        Put(methods, "write", (_, a) =>
        {
            LuaUserdata u = CheckUserdata(a, "write");
            return WriteValues(u, ToFile(u), a, 1, "write");
        });

        _meta.Set(Str("__index"), LuaValue.FromTable(methods));
        _meta.Set(Str("__name"), Str("FILE*"));
        Put(_meta, "__tostring", FileToString);
        Put(_meta, "__gc", FileGc);
        Put(_meta, "__close", FileGc);
        _state.RegisterMetatable<LuaFile>(_meta);

        LuaUserdata stdin = _state.CreateUserdata(new LuaFile(_platform.OpenStandardInput(), standard: true));
        LuaUserdata stdout = _state.CreateUserdata(new LuaFile(new TextWriterStream(_state), standard: true));
        LuaUserdata stderr = _state.CreateUserdata(new LuaFile(_platform.OpenStandardError(), standard: true));
        _input = stdin;
        _output = stdout;
        lib.Set(Str("stdin"), LuaValue.FromObject(stdin));
        lib.Set(Str("stdout"), LuaValue.FromObject(stdout));
        lib.Set(Str("stderr"), LuaValue.FromObject(stderr));
        _state.SetGlobal("io", LuaValue.FromTable(lib));
    }

    // ---- results ----

    private LuaValue[] FileResult(Exception e, string? name)
    {
        (int code, string message) = Errno.From(e);
        return [default, Str(name is null ? message : name + ": " + message), LuaValue.FromInteger(code)];
    }

    private LuaValue[] ExecResult(ProcessStatus s) =>
        [!s.Signaled && s.Code == 0 ? LuaValue.FromBoolean(true) : default, Str(s.Signaled ? "signal" : "exit"), LuaValue.FromInteger(s.Code)];

    private static bool IsFileError(Exception e) =>
        e is IOException or UnauthorizedAccessException or ErrnoException or NotSupportedException or ArgumentException or ObjectDisposedException;

    // ---- argument helpers ----

    private static LuaUserdata CheckUserdata(ReadOnlySpan<LuaValue> args, string fn)
    {
        if (args.Length > 0 && args[0].Ref is LuaUserdata { Value: LuaFile } u) return u;
        throw LuaErrors.TypeError(args, 0, fn, "FILE*");
    }

    private static LuaFile ToFile(LuaUserdata u)
    {
        var f = (LuaFile)u.Value;
        if (f.IsClosed) throw LuaErrors.Error("attempt to use a closed file");
        return f;
    }

    private static LuaFile DefaultFile(LuaUserdata u, string which)
    {
        var f = (LuaFile)u.Value;
        if (f.IsClosed) throw LuaErrors.Error($"default {which} file is closed");
        return f;
    }

    private string CheckName(string fn, ReadOnlySpan<LuaValue> args, int i, out LuaValue[]? failure)
    {
        LuaString s = CheckString(_state, fn, args, i);
        failure = null;
        if (!PathNames.TryDecode(s.Bytes, out string path))
        {
            failure = [default, Str(s.ToString() + ": " + Errno.Text(Errno.EILSEQ)), LuaValue.FromInteger(Errno.EILSEQ)];
            return "";
        }
        return path;
    }

    private static bool ValidMode(string mode)
    {
        int i = 0;
        if (mode.Length == 0 || mode[i] is not ('r' or 'w' or 'a')) return false;
        i++;
        if (i < mode.Length && mode[i] == '+') i++;
        for (; i < mode.Length; i++) if (mode[i] != 'b') return false;
        return true;
    }

    private static (FileMode Mode, FileAccess Access, bool Append) ModeOf(string mode)
    {
        bool plus = mode.Length > 1 && mode[1] == '+';
        return mode[0] switch
        {
            'r' => (FileMode.Open, plus ? FileAccess.ReadWrite : FileAccess.Read, false),
            'w' => (FileMode.Create, plus ? FileAccess.ReadWrite : FileAccess.Write, false),
            _ => (FileMode.OpenOrCreate, plus ? FileAccess.ReadWrite : FileAccess.Write, true),
        };
    }

    /// <summary>Opens a file with a C fopen mode string.</summary>
    private LuaFile OpenFile(string path, string mode)
    {
        if ((_platform.Capabilities & PlatformCapabilities.Files) == 0) throw new ErrnoException(Errno.ENOSYS);
        (FileMode fm, FileAccess fa, bool append) = ModeOf(mode);
        Stream s = _platform.OpenFile(path, fm, fa);
        if (append && s.CanSeek) s.Seek(0, SeekOrigin.End);
        return new LuaFile(s, append);
    }

    // Open files are rooted here until closed: when a script drops its last reference, .NET would otherwise finalize the
    // FileStream's handle before the Lua-side __gc runs, losing buffered output. __gc (or state close) then closes them in order.
    private readonly HashSet<LuaFile> _open = [];

    private LuaUserdata Wrap(LuaFile f)
    {
        _open.Add(f);
        f.OnClose = file => _open.Remove(file);
        return _state.CreateUserdata(f);
    }

    // ---- io.open / tmpfile / popen ----

    private LuaValue[] IoOpen(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        string name = CheckName("open", args, 0, out LuaValue[]? bad);
        string mode = args.Length > 1 && !args[1].IsNil ? CheckString(state, "open", args, 1).ToString() : "r";
        if (!ValidMode(mode)) throw LuaErrors.ArgError(2, "open", "invalid mode");
        if (bad is not null) return bad;
        try { return [LuaValue.FromObject(Wrap(OpenFile(name, mode)))]; }
        catch (Exception e) when (IsFileError(e)) { return FileResult(e, LibArgs.CheckString(state, "open", args, 0).ToString()); }
    }

    private LuaValue[] IoTmpFile(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        try
        {
            string path = _platform.CreateTempFile();
            LuaFile f = OpenFile(path, "w+");
            try { _platform.Remove(path); }
            catch (Exception e) when (IsFileError(e)) { /* best effort: the name may linger until the OS cleans the temp directory */ }
            return [LuaValue.FromObject(Wrap(f))];
        }
        catch (Exception e) when (IsFileError(e)) { return FileResult(e, null); }
    }

    private LuaValue[] IoPopen(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        string command = CheckString(state, "popen", args, 0).ToString();
        string mode = args.Length > 1 && !args[1].IsNil ? CheckString(state, "popen", args, 1).ToString() : "r";
        if (mode is not ("r" or "w")) throw LuaErrors.ArgError(2, "popen", "invalid mode");
        if ((_platform.Capabilities & PlatformCapabilities.Processes) == 0) throw LuaErrors.Error("'popen' not supported");
        try
        {
            IPlatformProcess p = _platform.StartProcess(command, mode == "r");
            return [LuaValue.FromObject(Wrap(new LuaFile(p.Stream, process: p)))];
        }
        catch (Exception e) when (IsFileError(e) || e is System.Runtime.InteropServices.ExternalException)
        {
            return FileResult(e, command);
        }
    }

    // ---- io.type / close / input / output / lines ----

    private LuaValue[] IoType(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        CheckAny("type", args, 0);
        if (args[0].Ref is LuaUserdata { Value: LuaFile f }) return [Str(f.IsClosed ? "closed file" : "file")];
        return [default];
    }

    private LuaValue[] IoClose(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length == 0) return CloseFile(_output);
        return CloseFile(CheckUserdata(args, "close"));
    }

    private LuaValue[] FileClose(LuaState state, ReadOnlySpan<LuaValue> args) => CloseFile(CheckUserdata(args, "close"));

    private LuaValue[] CloseFile(LuaUserdata u)
    {
        LuaFile f = ToFile(u);
        if (f.IsStandard) return [default, Str("cannot close standard file")];
        try
        {
            ProcessStatus? status = f.Close();
            return status is { } s ? ExecResult(s) : [LuaValue.FromBoolean(true)];
        }
        catch (Exception e) when (IsFileError(e)) { return FileResult(e, null); }
    }

    private LuaValue[] FileGc(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length == 0 || args[0].Ref is not LuaUserdata { Value: LuaFile }) throw LuaErrors.TypeError(args, 0, "__gc", "FILE*");
        if (args[0].Ref is LuaUserdata { Value: LuaFile f } && !f.IsClosed)
        {
            try
            {
                if (f.IsStandard) f.Flush();
                else f.Close();
            }
            catch (Exception e) when (IsFileError(e)) { /* nothing to report to from a finalizer */ }
        }
        return [];
    }

    private LuaValue[] FileToString(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaUserdata u = CheckUserdata(args, "tostring");
        return [Str(((LuaFile)u.Value).IsClosed ? "file (closed)" : $"file (0x{RuntimeHelpers.GetHashCode(u):x8})")];
    }

    private LuaValue[] IoFile(ReadOnlySpan<LuaValue> args, bool input)
    {
        string which = input ? "input" : "output";
        if (args.Length > 0 && !args[0].IsNil)
        {
            LuaValue[] open;
            if (args[0].IsString || args[0].IsNumber)
            {
                string name = CheckName(which, args, 0, out LuaValue[]? bad);
                string label = LibArgs.CheckString(_state, which, args, 0).ToString();
                LuaUserdata u;
                try
                {
                    if (bad is not null) throw new ErrnoException(Errno.EILSEQ);
                    u = Wrap(OpenFile(name, input ? "r" : "w"));
                }
                catch (Exception e) when (IsFileError(e))
                {
                    throw LuaErrors.Error($"cannot open file '{label}' ({Errno.From(e).Message})");
                }
                open = [LuaValue.FromObject(u)];
            }
            else
            {
                ToFile(CheckUserdata(args, which));
                open = [args[0]];
            }
            if (input) _input = (LuaUserdata)open[0].Ref!;
            else _output = (LuaUserdata)open[0].Ref!;
        }
        return [LuaValue.FromObject(input ? _input : _output)];
    }

    private LuaValue[] IoLines(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length == 0 || args[0].IsNil)
        {
            return LinesIterator(DefaultFile(_input, "input"), args, 1, toClose: false);
        }
        string name = CheckName("lines", args, 0, out LuaValue[]? bad);
        string label = LibArgs.CheckString(state, "lines", args, 0).ToString();
        LuaFile f;
        try
        {
            if (bad is not null) throw new ErrnoException(Errno.EILSEQ);
            f = OpenFile(name, "r");
        }
        catch (Exception e) when (IsFileError(e))
        {
            throw LuaErrors.Error($"{label}: {Errno.From(e).Message}");
        }
        LuaUserdata u = Wrap(f);
        LuaValue[] iter = LinesIterator(f, args, 1, toClose: true);
        return [iter[0], default, default, LuaValue.FromObject(u)];
    }

    private LuaValue[] LinesIterator(LuaFile file, ReadOnlySpan<LuaValue> args, int first, bool toClose)
    {
        int n = Math.Max(args.Length - first, 0);
        if (n > MaxLinesArgs) throw LuaErrors.ArgError(MaxLinesArgs + 2, "lines", "too many arguments");
        var formats = new LuaValue[n];
        for (int i = 0; i < n; i++) formats[i] = args[first + i];
        var iterator = new BuiltinFunction((st, _) =>
        {
            if (file.IsClosed) throw LuaErrors.Error("file is already closed");
            LuaValue[] r = ReadFormats(file, formats, 0, "lines");
            if (r.Length > 0 && !r[0].IsFalsy) return r;
            if (r.Length > 1 && !r[1].IsNil) throw LuaErrors.Error(new LuaString((byte[])r[1].Ref!).ToString());
            if (toClose) file.Close();
            return [];
        });
        return [LuaValue.FromObject(iterator)];
    }

    // ---- reading ----

    private LuaValue ReadLineValue(LuaFile f, bool chop, out bool ok)
    {
        var b = new ByteBuilder(128);
        try
        {
            ok = f.ReadLine(ref b, chop);
            return ok ? LuaValue.FromString(b.ToLuaString(_state.Strings)) : default;
        }
        finally { b.Dispose(); }
    }

    private LuaValue ReadCountValue(LuaFile f, long count, out bool ok)
    {
        var b = new ByteBuilder(count > 0 && count < 8192 ? (int)count : 256);
        try
        {
            ok = f.ReadCount(ref b, count < 0 ? long.MaxValue : count);
            return ok ? LuaValue.FromString(b.ToLuaString(_state.Strings)) : default;
        }
        finally { b.Dispose(); }
    }

    private LuaValue ReadAllValue(LuaFile f)
    {
        var b = new ByteBuilder(1024);
        try
        {
            f.ReadAll(ref b);
            return LuaValue.FromString(b.ToLuaString(_state.Strings));
        }
        finally { b.Dispose(); }
    }

    private LuaValue ReadNumberValue(LuaFile f, out bool ok)
    {
        ok = false;
        string? numeral = f.ReadNumeral();
        if (numeral is null) return default;
        byte[] bytes = new byte[numeral.Length];
        for (int k = 0; k < bytes.Length; k++) bytes[k] = (byte)numeral[k];
        ok = StringConv.TryParseNumber(bytes, out LuaValue num);
        return ok ? num : default;
    }

    private LuaValue[] ReadFormats(LuaFile f, ReadOnlySpan<LuaValue> args, int first, string fn)
    {
        int nargs = args.Length - first;
        try
        {
            if (nargs <= 0) return [ReadLineValue(f, chop: true, out _)];
            var results = new LuaValue[nargs];
            int count = 0;
            bool success = true;
            for (int i = first; i < args.Length && success; i++)
            {
                if (args[i].IsNumber)
                {
                    long l = CheckInteger(fn, args, i);
                    if (l == 0)
                    {
                        success = !f.AtEof();
                        results[count++] = success ? LuaValue.FromString(LuaString.Empty) : default;
                    }
                    else results[count++] = ReadCountValue(f, l, out success);
                    continue;
                }
                string p = CheckString(_state, fn, args, i).ToString();
                if (p.StartsWith('*')) p = p[1..];
                switch (p.Length > 0 ? p[0] : '\0')
                {
                    case 'n': results[count++] = ReadNumberValue(f, out success); break;
                    case 'l': results[count++] = ReadLineValue(f, chop: true, out success); break;
                    case 'L': results[count++] = ReadLineValue(f, chop: false, out success); break;
                    case 'a': results[count++] = ReadAllValue(f); break;
                    default: throw LuaErrors.ArgError(i + 1, fn, "invalid format");
                }
            }
            return count == results.Length ? results : results[..count];
        }
        catch (Exception e) when (IsFileError(e))
        {
            return FileResult(e, null);
        }
    }

    // ---- writing / flushing / seeking ----

    private LuaValue[] WriteValues(LuaUserdata u, LuaFile f, ReadOnlySpan<LuaValue> args, int first, string fn)
    {
        try
        {
            for (int i = first; i < args.Length; i++)
            {
                LuaValue a = args[i];
                if (a.IsNumber) f.Write(StringConv.NumberToString(_state, a).Bytes);
                else f.Write(CheckString(_state, fn, args, i).Bytes);
            }
            return [LuaValue.FromObject(u)];
        }
        catch (Exception e) when (IsFileError(e)) { return FileResult(e, null); }
    }

    private LuaValue[] FlushResult(LuaUserdata u)
    {
        try
        {
            ToFile(u).Flush();
            return [LuaValue.FromBoolean(true)];
        }
        catch (Exception e) when (IsFileError(e)) { return FileResult(e, null); }
    }

    private static int CheckOption(string fn, ReadOnlySpan<LuaValue> args, int i, string dflt, string[] options, LuaState state)
    {
        string v = i < args.Length && !args[i].IsNil ? CheckString(state, fn, args, i).ToString() : dflt;
        int idx = Array.IndexOf(options, v);
        if (idx < 0) throw LuaErrors.ArgError(i + 1, fn, $"invalid option '{v}'");
        return idx;
    }

    private LuaValue[] FileSeek(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaFile f = ToFile(CheckUserdata(args, "seek"));
        int whence = CheckOption("seek", args, 1, "cur", ["set", "cur", "end"], state);
        long offset = OptInteger("seek", args, 2, 0);
        try { return [LuaValue.FromInteger(f.Seek(whence, offset))]; }
        catch (Exception e) when (IsFileError(e)) { return FileResult(e, null); }
    }

    private LuaValue[] FileSetVBuf(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaFile f = ToFile(CheckUserdata(args, "setvbuf"));
        int mode = CheckOption("setvbuf", args, 1, "", ["no", "full", "line"], state);
        long size = OptInteger("setvbuf", args, 2, LuaFile.DefaultBufferSize);
        try
        {
            f.SetBuffering((BufferMode)mode, (int)Math.Clamp(size, 1, 1 << 24));
            return [LuaValue.FromBoolean(true)];
        }
        catch (Exception e) when (IsFileError(e)) { return FileResult(e, null); }
    }
}
