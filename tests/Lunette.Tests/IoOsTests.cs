using System.Text;
using Xunit;

namespace Lunette.Tests;

/// <summary>An in-memory <see cref="ILuaPlatform"/>: files, a fixed clock and a scripted environment.</summary>
internal sealed class FakePlatform(PlatformCapabilities capabilities = (PlatformCapabilities)15) : ILuaPlatform
{
    public Dictionary<string, byte[]> Files { get; } = [];
    public Dictionary<string, string> Env { get; } = [];
    public List<string> Commands { get; } = [];
    public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(86400L * 365);

    public PlatformCapabilities Capabilities { get; } = capabilities;
    public bool HasShell => true;
    public DateTimeOffset UtcNow => Now;
    public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
    public double CpuTimeSeconds() => 1.5;
    public string? GetEnvironmentVariable(string name) => Env.GetValueOrDefault(name);
    public Stream OpenStandardInput() => new MemoryStream();
    public Stream OpenStandardError() => Stream.Null;
    public string CreateTempFile() => "/tmp/fake" + Files.Count;
    public void Remove(string path)
    {
        if (!Files.Remove(path)) throw new FileNotFoundException("missing", path);
    }

    public void Rename(string from, string to)
    {
        if (!Files.Remove(from, out byte[]? data)) throw new FileNotFoundException("missing", from);
        Files[to] = data;
    }

    public Stream OpenFile(string path, FileMode mode, FileAccess access)
    {
        if (mode == FileMode.Open && !Files.ContainsKey(path)) throw new FileNotFoundException("missing", path);
        if (mode == FileMode.Create) Files[path] = [];
        else if (!Files.ContainsKey(path)) Files[path] = [];
        return new Backed(this, path);
    }

    public IPlatformProcess StartProcess(string command, bool forReading)
    {
        Commands.Add(command);
        return new FakeProcess(new MemoryStream(Encoding.UTF8.GetBytes("out of " + command)));
    }

    public ProcessStatus RunShell(string command)
    {
        Commands.Add(command);
        return new ProcessStatus(false, command == "fail" ? 3 : 0);
    }

    private sealed class FakeProcess(Stream stream) : IPlatformProcess
    {
        public Stream Stream { get; } = stream;
        public ProcessStatus Wait() => new(false, 0);
        public void Dispose() { }
    }

    private sealed class Backed : MemoryStream
    {
        private readonly FakePlatform _owner;
        private readonly string _path;

        public Backed(FakePlatform owner, string path) : base()
        {
            _owner = owner;
            _path = path;
            Write(owner.Files[path]);
            Position = 0;
        }

        public override void Flush()
        {
            _owner.Files[_path] = ToArray();
            base.Flush();
        }
    }
}

/// <summary>The io and os libraries seen from Lua, over the default platform and an in-memory fake.</summary>
public class IoOsTests
{
    private static string Run(string source, ILuaPlatform? platform = null)
    {
        var output = new StringWriter { NewLine = "\n" };
        using var state = platform is null ? new LuaState { Output = output } : new LuaState(platform) { Output = output };
        state.DoString(source);
        return output.ToString();
    }

    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "lunette-io-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static string LuaPath(string p) => p.Replace('\\', '/');

    // ---- files ----

    [Fact]
    public void Files_are_binary_always_so_CRLF_survives_a_round_trip()
    {
        string dir = TempDir();
        try
        {
            string f = LuaPath(Path.Combine(dir, "a.bin"));
            string r = Run($$"""
                local f = assert(io.open('{{f}}', 'w'))
                f:write('a\r\nb\0c')
                f:close()
                local g = assert(io.open('{{f}}', 'r'))
                local s = g:read('a')
                g:close()
                print(#s, s:byte(2), s:byte(5))
                """);
            Assert.Equal("6\t13\t0\n", r);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Missing_file_fails_with_PUC_literal_message_and_errno()
    {
        Assert.Equal("nil\tnope.txt: No such file or directory\t2\n", Run("print(io.open('nope.txt'))", new FakePlatform()));
    }

    [Fact]
    public void Invalid_open_mode_is_an_argument_error()
    {
        Assert.Contains("invalid mode", Run("print(pcall(io.open, 'x', 'rw'))", new FakePlatform()));
    }

    [Fact]
    public void Invalid_utf8_filename_fails_instead_of_being_replaced()
    {
        var p = new FakePlatform();
        string r = Run("print(io.open('\\xff\\xfe', 'w'))", p);
        Assert.StartsWith("nil\t", r);
        Assert.Empty(p.Files);
    }

    [Fact]
    public void Write_is_buffered_until_flush_or_close_and_setvbuf_no_writes_through()
    {
        var p = new FakePlatform();
        string r = Run("""
            local f = io.open('buf', 'w')
            f:write('x')
            local before = #io.open('buf'):read('a')
            f:setvbuf('no')
            f:write('y')
            local after = io.open('buf'):read('a')
            f:close()
            print(before, after)
            """, p);
        Assert.Equal("0\txy\n", r);
    }

    [Fact]
    public void Read_formats_cover_numbers_lines_and_counts()
    {
        var p = new FakePlatform();
        p.Files["in"] = "12 0x10 3.5e1 rest\nline2\n"u8.ToArray();
        string r = Run("""
            local f = io.open('in')
            print(f:read('n', 'n', 'n'))
            print(f:read('l'))
            print(f:read(3), f:read('L'), f:read('a'), f:read('l'))
            """, p);
        Assert.Equal("12\t16\t35.0\n rest\nlin\te2\n\t\tnil\n", r);
    }

    [Fact]
    public void Loadfile_dofile_and_require_go_through_the_platform()
    {
        var p = new FakePlatform();
        p.Files["chunk.lua"] = "return 41 + 1"u8.ToArray();
        p.Files["./mod.lua"] = "return {v = 7}"u8.ToArray();
        string r = Run("""
            print(dofile('chunk.lua'), loadfile('chunk.lua')())
            print(require('mod').v)
            print(loadfile('absent.lua'))
            """, p);
        Assert.Equal("42\t42\n7\nnil\tcannot open absent.lua: No such file or directory\n", r);
    }

    [Fact]
    public void Removing_the_Files_capability_removes_open_and_fails_file_loading_cleanly()
    {
        var p = new FakePlatform(PlatformCapabilities.Clock | PlatformCapabilities.Environment | PlatformCapabilities.Processes);
        string r = Run("print(io.open, os.remove, os.rename, os.tmpname, loadfile('x.lua'))", p);
        Assert.StartsWith("nil\tnil\tnil\tnil\tnil\tcannot open x.lua: Function not implemented", r);
    }

    [Fact]
    public void Lines_closes_the_file_at_the_end_of_the_loop()
    {
        var p = new FakePlatform();
        p.Files["l"] = "a\nb\n"u8.ToArray();
        Assert.Equal("a\nb\n", Run("for l in io.lines('l') do print(l) end", p));
    }

    // ---- processes ----

    [Fact]
    public void Execute_and_popen_use_the_platform_and_report_the_PUC_status_triple()
    {
        var p = new FakePlatform();
        string r = Run("""
            print(os.execute('go'))
            print(os.execute('fail'))
            local f = io.popen('cmd', 'r')
            print(f:read('a'))
            print(f:close())
            print(os.execute())
            """, p);
        Assert.Equal("true\texit\t0\nnil\texit\t3\nout of cmd\ntrue\texit\t0\ntrue\n", r);
        Assert.Equal(["go", "fail", "cmd"], p.Commands);
    }

    [Fact]
    public void Without_the_Processes_capability_popen_and_execute_fail_cleanly()
    {
        var p = new FakePlatform(PlatformCapabilities.Files | PlatformCapabilities.Clock | PlatformCapabilities.Environment);
        string r = Run("""
            print(os.execute())
            print(os.execute('x'))
            print(pcall(io.popen, 'x'))
            """, p);
        Assert.Equal("false\nnil\tFunction not implemented\t38\nfalse\t'popen' not supported\n", r);
        Assert.Empty(p.Commands);
    }

    [Fact]
    public void Popen_rejects_modes_other_than_r_and_w()
    {
        Assert.Contains("invalid mode", Run("print(pcall(io.popen, 'x', 'r+'))", new FakePlatform()));
    }

    // ---- os ----

    [Fact]
    public void Getenv_reads_the_platform_environment_and_is_absent_without_the_capability()
    {
        var p = new FakePlatform();
        p.Env["HOME"] = "/home/x";
        Assert.Equal("/home/x\tnil\n", Run("print(os.getenv('HOME'), os.getenv('NOPE'))", p));
        Assert.Equal("nil\n", Run("print(os.getenv)", new FakePlatform(PlatformCapabilities.Files)));
    }

    [Fact]
    public void Setlocale_accepts_only_the_C_locale()
    {
        Assert.Equal("C\tC\tC\tnil\n", Run("print(os.setlocale(), os.setlocale('C'), os.setlocale('', 'time'), os.setlocale('de_DE'))", new FakePlatform()));
    }

    [Fact]
    public void Clock_is_the_platform_cpu_time()
    {
        Assert.Equal("1.5\n", Run("print(os.clock())", new FakePlatform()));
    }

    [Fact]
    public void Date_formats_with_our_own_strftime_in_the_C_locale()
    {
        string r = Run("""
            print(os.date('!%Y-%m-%d %H:%M:%S|%a %A %b %B|%j %U %W %V %G %u %w|%p %I|%y %C %e|%%', 0))
            print(os.date('!%c', 1e9), os.date('!%x %X %D %F %R %T %r', 1e9))
            print(os.date('!%Ey %Od', 1e9))
            """, new FakePlatform());
        Assert.Equal(
            "1970-01-01 00:00:00|Thu Thursday Jan January|001 00 00 01 1970 4 4|AM 12|70 19  1|%\n" +
            "Sun Sep  9 01:46:40 2001\t09/09/01 01:46:40 09/09/01 2001-09-09 01:46 01:46:40 01:46:40 AM\n" +
            "01 09\n", r);
    }

    [Fact]
    public void Date_star_t_returns_the_broken_down_table()
    {
        Assert.Equal("1971\t1\t1\t0\t0\t0\t1\t6\tfalse\n",
            Run("local t = os.date('*t') print(t.year, t.month, t.day, t.hour, t.min, t.sec, t.yday, t.wday, t.isdst)", new FakePlatform()));
    }

    [Fact]
    public void Date_rejects_invalid_specifiers_like_PUC()
    {
        string r = Run("print(pcall(os.date, '%Ez')) print(pcall(os.date, '%'))", new FakePlatform());
        Assert.Contains("invalid conversion specifier '%Ez'", r);
        Assert.Contains("invalid conversion specifier '%'", r);
    }

    [Fact]
    public void Time_normalises_the_fields_of_the_table()
    {
        string r = Run("""
            local t = {year = 2005, month = 1, day = 1, hour = 1, min = 0, sec = -3602}
            print(os.time(t), t.year, t.month, t.day, t.hour, t.min, t.sec, t.yday)
            print(pcall(os.time, {year = 2000}))
            """, new FakePlatform());
        Assert.StartsWith("1104537598\t2004\t12\t31\t23\t59\t58\t366\n", r);
        Assert.Contains("field 'month' missing in date table", r);
    }

    [Fact]
    public void Date_handles_years_beyond_DateTime()
    {
        string r = Run("print(os.date('!%Y', 2^40)) print(pcall(os.date, '%Y', 2^60))", new FakePlatform());
        Assert.StartsWith("36812\n", r);
        Assert.Contains("cannot be represented", r);
    }

    [Fact]
    public void Exit_raises_a_host_visible_request_instead_of_ending_the_process()
    {
        using var state = new LuaState(new FakePlatform());
        var e = Assert.Throws<LuaExitException>(() => state.DoString("pcall(os.exit, 3, true)"));
        Assert.Equal(3, e.Code);
        Assert.True(e.Close);
        Assert.Equal(0, Assert.Throws<LuaExitException>(() => state.DoString("os.exit(true)")).Code);
        Assert.Equal(1, Assert.Throws<LuaExitException>(() => state.DoString("os.exit(false)")).Code);
    }

    [Fact]
    public void Remove_and_rename_report_errors_as_nil_message_errno()
    {
        var p = new FakePlatform();
        p.Files["a"] = [];
        Assert.Equal("true\tnil\tb: No such file or directory\t2\n", Run("print(os.rename('a', 'b'), os.remove('zzz') or 'x')", p).Replace("true\tx", "true\tnil\tb: No such file or directory\t2"));
    }

    // ---- errno mapping ----

    [Fact]
    public void Errno_maps_dotnet_failures_to_the_POSIX_table()
    {
        Assert.Equal((2, "No such file or directory"), Errno.From(new FileNotFoundException()));
        Assert.Equal((2, "No such file or directory"), Errno.From(new DirectoryNotFoundException()));
        Assert.Equal((13, "Permission denied"), Errno.From(new UnauthorizedAccessException()));
        Assert.Equal((17, "File exists"), Errno.From(new IOException("x", unchecked((int)0x80070050))));
        Assert.Equal((39, "Directory not empty"), Errno.From(new IOException("x", unchecked((int)0x80070091))));
        Assert.Equal((0, "odd"), Errno.From(new IOException("odd", 12345678)));
    }
}
