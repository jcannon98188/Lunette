using static Lunette.Lib.LibArgs;

namespace Lunette.Lib;

/// <summary>The Lua <c>os</c> library (PUC 5.4 loslib semantics) over the state's <see cref="ILuaPlatform"/>; always the "C" locale.</summary>
internal sealed class OsLib
{
    private readonly LuaState _state;
    private readonly ILuaPlatform _platform;

    private OsLib(LuaState state)
    {
        _state = state;
        _platform = state.Platform;
    }

    public static void Register(LuaState state) => new OsLib(state).Install();

    private LuaValue Str(string s) => LuaValue.FromString(_state.Strings.FromUtf16(s));

    private void Put(LuaTable t, string name, BuiltinBody body) => t.Set(Str(name), LuaValue.FromObject(new BuiltinFunction(body)));

    private void Install()
    {
        PlatformCapabilities caps = _platform.Capabilities;
        var lib = new LuaTable();
        Put(lib, "difftime", Difftime);
        Put(lib, "execute", Execute);
        Put(lib, "exit", Exit);
        Put(lib, "setlocale", SetLocale);
        if ((caps & PlatformCapabilities.Environment) != 0) Put(lib, "getenv", GetEnv);
        if ((caps & PlatformCapabilities.Clock) != 0)
        {
            Put(lib, "clock", (_, _) => [LuaValue.FromFloat(_platform.CpuTimeSeconds())]);
            Put(lib, "date", Date);
            Put(lib, "time", Time);
        }
        if ((caps & PlatformCapabilities.Files) != 0)
        {
            Put(lib, "remove", Remove);
            Put(lib, "rename", Rename);
            Put(lib, "tmpname", TmpName);
        }
        _state.SetGlobal("os", LuaValue.FromTable(lib));
    }

    private LuaValue[] Failure(Exception e, string? name)
    {
        (int code, string message) = Errno.From(e);
        return [default, Str(name is null ? message : name + ": " + message), LuaValue.FromInteger(code)];
    }

    private static bool IsFileError(Exception e) =>
        e is IOException or UnauthorizedAccessException or ErrnoException or NotSupportedException or ArgumentException;

    // ---- environment, locale, processes ----

    private LuaValue[] GetEnv(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        string name = CheckString(state, "getenv", args, 0).ToString();
        string? v = _platform.GetEnvironmentVariable(name);
        return [v is null ? default : Str(v)];
    }

    private LuaValue[] SetLocale(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        string[] categories = ["all", "collate", "ctype", "monetary", "numeric", "time"];
        string? locale = args.Length > 0 && !args[0].IsNil ? CheckString(state, "setlocale", args, 0).ToString() : null;
        string cat = args.Length > 1 && !args[1].IsNil ? CheckString(state, "setlocale", args, 1).ToString() : "all";
        if (Array.IndexOf(categories, cat) < 0) throw LuaErrors.ArgError(2, "setlocale", $"invalid option '{cat}'");
        return locale is null or "" or "C" or "POSIX" ? [Str("C")] : [default];
    }

    private LuaValue[] Execute(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        bool available = (_platform.Capabilities & PlatformCapabilities.Processes) != 0 && _platform.HasShell;
        if (args.Length == 0 || args[0].IsNil) return [LuaValue.FromBoolean(available)];
        string command = CheckString(state, "execute", args, 0).ToString();
        if (!available) return [default, Str(Errno.Text(Errno.ENOSYS)!), LuaValue.FromInteger(Errno.ENOSYS)];
        try
        {
            ProcessStatus s = _platform.RunShell(command);
            return [LuaValue.FromBoolean(!s.Signaled && s.Code == 0), Str(s.Signaled ? "signal" : "exit"), LuaValue.FromInteger(s.Code)];
        }
        catch (Exception e) when (IsFileError(e) || e is System.ComponentModel.Win32Exception) { return Failure(e, null); }
    }

    private LuaValue[] Exit(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int code;
        if (args.Length == 0 || args[0].IsNil || (args[0].IsBoolean && !args[0].IsFalsy)) code = 0;
        else if (args[0].IsBoolean) code = 1;
        else code = (int)OptInteger("exit", args, 0, 0);
        bool close = args.Length > 1 && !args[1].IsFalsy;
        throw new LuaExitException(code, close);
    }

    // ---- files ----

    private LuaValue[] Remove(LuaState state, ReadOnlySpan<LuaValue> args) => PathOp(args, "remove", rename: false);

    private LuaValue[] Rename(LuaState state, ReadOnlySpan<LuaValue> args) => PathOp(args, "rename", rename: true);

    private LuaValue[] PathOp(ReadOnlySpan<LuaValue> args, string fn, bool rename)
    {
        LuaString first = CheckString(_state, fn, args, 0);
        LuaString second = rename ? CheckString(_state, fn, args, 1) : default;
        if (!PathNames.TryDecode(first.Bytes, out string from) || (rename && !PathNames.TryDecode(second.Bytes, out _)))
            return [default, Str(first.ToString() + ": " + Errno.Text(Errno.EILSEQ)), LuaValue.FromInteger(Errno.EILSEQ)];
        try
        {
            if (rename)
            {
                PathNames.TryDecode(second.Bytes, out string to);
                _platform.Rename(from, to);
            }
            else _platform.Remove(from);
            return [LuaValue.FromBoolean(true)];
        }
        catch (Exception e) when (IsFileError(e)) { return Failure(e, rename ? null : first.ToString()); }
    }

    private LuaValue[] TmpName(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        try { return [Str(_platform.CreateTempFile())]; }
        catch (Exception e) when (IsFileError(e)) { throw LuaErrors.Error("unable to generate a unique filename"); }
    }

    // ---- time ----

    private static long CheckTime(string fn, ReadOnlySpan<LuaValue> args, int i) => CheckInteger(fn, args, i);

    private LuaValue[] Difftime(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        long t1 = CheckTime("difftime", args, 0);
        long t2 = CheckTime("difftime", args, 1);
        return [LuaValue.FromFloat((double)t1 - t2)];
    }

    private static bool TryGetInteger(LuaValue v, out long l)
    {
        l = 0;
        return StringConv.TryToNumber(v, out LuaValue n) && LuaNumber.TryToInteger(n, out l);
    }

    private int GetField(LuaTable t, string key, int dflt, int delta)
    {
        LuaValue v = t.Get(Str(key));
        if (!TryGetInteger(v, out long res))
        {
            if (!v.IsNil) throw LuaErrors.Error($"field '{key}' is not an integer");
            if (dflt < 0) throw LuaErrors.Error($"field '{key}' missing in date table");
            return dflt;
        }
        if (!(res >= 0 ? res - delta <= int.MaxValue : int.MinValue + delta <= res))
            throw LuaErrors.Error($"field '{key}' is out-of-bound");
        return (int)(res - delta);
    }

    private LuaValue[] Time(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length == 0 || args[0].IsNil) return [LuaValue.FromInteger(_platform.UtcNow.ToUnixTimeSeconds())];
        if (args[0].Ref is not LuaTable t) throw LuaErrors.TypeError(args, 0, "time", "table");
        int year = GetField(t, "year", -1, 1900);
        int month = GetField(t, "month", -1, 1);
        int day = GetField(t, "day", -1, 0);
        int hour = GetField(t, "hour", 12, 0);
        int min = GetField(t, "min", 0, 0);
        int sec = GetField(t, "sec", 0, 0);
        TimeZoneInfo zone = _platform.TimeZone;
        long? result = LuaTime.MakeTime(year, month, day, hour, min, sec, zone);
        if (result is not { } secs || !LuaTime.TryBreakDown(secs, utc: false, zone, out BrokenTime bt))
            throw LuaErrors.Error("time result cannot be represented in this installation");
        SetAllFields(t, bt);
        return [LuaValue.FromInteger(secs)];
    }

    private void SetAllFields(LuaTable t, in BrokenTime bt)
    {
        void Set(string k, long v) => t.Set(Str(k), LuaValue.FromInteger(v));
        Set("year", bt.Year);
        Set("month", bt.Month);
        Set("day", bt.Day);
        Set("hour", bt.Hour);
        Set("min", bt.Minute);
        Set("sec", bt.Second);
        Set("yday", bt.YearDay + 1);
        Set("wday", bt.WeekDay + 1);
        t.Set(Str("isdst"), LuaValue.FromBoolean(bt.IsDst));
    }

    private LuaValue[] Date(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString format = args.Length > 0 && !args[0].IsNil ? CheckString(state, "date", args, 0) : LuaString.FromUtf16("%c");
        long time = args.Length > 1 && !args[1].IsNil ? CheckTime("date", args, 1) : _platform.UtcNow.ToUnixTimeSeconds();
        ReadOnlySpan<byte> f = format.Bytes;
        bool utc = false;
        if (f.Length > 0 && f[0] == (byte)'!')
        {
            utc = true;
            f = f[1..];
        }
        if (!LuaTime.TryBreakDown(time, utc, _platform.TimeZone, out BrokenTime bt))
            throw LuaErrors.Error("date result cannot be represented in this installation");
        if (f.StartsWith("*t"u8))
        {
            var table = new LuaTable(0, 9);
            SetAllFields(table, bt);
            return [LuaValue.FromTable(table)];
        }
        var b = new ByteBuilder(f.Length + 32);
        try
        {
        for (int i = 0; i < f.Length; i++)
        {
            if (f[i] != (byte)'%')
            {
                b.Append(f[i]);
                continue;
            }
            i++;
            int conv = ParseConversion(f, i, out int length);
            if (conv < 0)
            {
                string rest = LuaString.Create(f[i..]).ToString();
                throw LuaErrors.ArgError(1, "date", $"invalid conversion specifier '%{rest}'");
            }
            LuaTime.AppendConversion(ref b, (char)conv, bt);
            i += length - 1;
        }
        return [LuaValue.FromString(b.ToLuaString(_state.Strings))];
        }
        finally { b.Dispose(); }
    }

    /// <summary>Validates the conversion at <paramref name="i"/> (after '%'); returns the conversion character or -1.</summary>
    private static int ParseConversion(ReadOnlySpan<byte> f, int i, out int length)
    {
        length = 0;
        if (i >= f.Length) return -1;
        char c = (char)f[i];
        if (c is 'E' or 'O')
        {
            if (i + 1 >= f.Length) return -1;
            char d = (char)f[i + 1];
            string valid = c == 'E' ? LuaTime.ValidEConversions : LuaTime.ValidOConversions;
            if (valid.Contains(d)) { length = 2; return d; }
            return -1;
        }
        if (LuaTime.ValidConversions.Contains(c)) { length = 1; return c; }
        return -1;
    }
}
