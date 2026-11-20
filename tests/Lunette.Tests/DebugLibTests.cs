using Lunette.Cli;
using Xunit;

namespace Lunette.Tests;

/// <summary>The debug library and hooks, observed through Lua source (expected values follow the PUC 5.4 reference behaviour).</summary>
public class DebugLibTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Getinfo_reports_source_position_and_kind()
    {
        Assert.Equal("(load)\t1\tmain\t0\n", Run("local t = debug.getinfo(1, 'Sl')\nprint(t.short_src, t.currentline, t.what, t.linedefined)"));
        Assert.Equal("2\t4\tLua\n", Run("local x\nlocal function f()\n  return 1\nend\nlocal t = debug.getinfo(f, 'S')\nprint(t.linedefined, t.lastlinedefined, t.what)"));
    }

    [Fact]
    public void Getinfo_on_a_function_gives_defined_lines_and_parameter_counts()
    {
        Assert.Equal("1\t3\tLua\t2\ttrue\t0\n", Run("local function f(a, b, ...)\n  return a\nend\nlocal t = debug.getinfo(f, 'Su')\nprint(t.linedefined, t.lastlinedefined, t.what, t.nparams, t.isvararg, t.nups)"));
    }

    [Fact]
    public void Getinfo_names_the_function_from_its_call_site()
    {
        Assert.Equal("foo\tlocal\n", Run("local function foo() local i = debug.getinfo(1, 'n') return i end\nlocal i = foo()\nprint(i.name, i.namewhat)"));
        Assert.Equal("bar\tglobal\n", Run("function bar() local i = debug.getinfo(1, 'n') return i end\nlocal i = bar()\nprint(i.name, i.namewhat)"));
        Assert.Equal("m\tmethod\n", Run("local o = {} function o:m() local i = debug.getinfo(1, 'n') return i end\nlocal i = o:m()\nprint(i.name, i.namewhat)"));
    }

    [Fact]
    public void Getinfo_marks_tail_calls_and_host_functions()
    {
        Assert.Equal("true\n", Run("local function g() return debug.getinfo(1, 't').istailcall end\nlocal function f() return g() end\nprint(f())"));
        Assert.Equal("C\t[C]\n", Run("local t = debug.getinfo(print, 'S')\nprint(t.what, t.short_src)"));
    }

    [Fact]
    public void Getinfo_returns_nil_for_a_level_out_of_range_and_rejects_bad_options()
    {
        Assert.Equal("nil\n", Run("print(debug.getinfo(100))"));
        Assert.Equal("false\tbad argument #2 to 'getinfo' (invalid option)\n", Run("print(pcall(debug.getinfo, 1, 'X'))"));
    }

    [Fact]
    public void Getinfo_activelines_lists_lines_with_code()
    {
        Assert.Equal("true\ttrue\tnil\n", Run("local function f()\n  local a = 1\nend\nlocal t = debug.getinfo(f, 'L').activelines\nprint(t[2], t[3], t[1])"));
    }

    [Fact]
    public void Traceback_names_each_level()
    {
        Assert.Equal("msg\nstack traceback:\n\t(load):1: in local 'lvl'\n\t(load):2: in main chunk\n", Run("local function lvl() local s = debug.traceback('msg', 1) return s end\nprint(lvl())"));
        Assert.Equal("stack traceback:\n\t[C]: in function 'debug.traceback'\n\t(load):1: in main chunk\n", Run("print(debug.traceback(nil, 0))"));
    }

    [Fact]
    public void Traceback_passes_non_string_messages_through()
    {
        Assert.Equal("table\n", Run("local t = {} print(type(debug.traceback(t)))"));
    }

    [Fact]
    public void Traceback_of_a_coroutine_shows_its_own_stack()
    {
        Assert.Equal("stack traceback:\n\t[C]: in function 'coroutine.yield'\n\t(load):1: in function <(load):1>\n", Run("local co = coroutine.create(function() coroutine.yield() end)\ncoroutine.resume(co)\nprint(debug.traceback(co))"));
    }

    [Fact]
    public void Traceback_skips_the_middle_of_a_deep_stack()
    {
        string o = Run("local function r(n) if n == 0 then return debug.traceback() end local s = r(n - 1) return s end\nprint(r(30))");
        Assert.Contains("\n\t...\t(skipping ", o);
        Assert.Contains("in main chunk", o);
    }

    [Fact]
    public void Getlocal_and_setlocal_reach_locals_parameters_and_varargs()
    {
        Assert.Equal("a\t10\tb\t20\n", Run("local function f(a, b) return debug.getlocal(1, 1), debug.getlocal(1, 2) end\nlocal n1, v1, n2, v2 = (function(a, b) local x, y = debug.getlocal(1, 1) local p, q = debug.getlocal(1, 2) return x, y, p, q end)(10, 20)\nprint(n1, v1, n2, v2)"));
        Assert.Equal("x\t99\n", Run("local x = 1\nprint(debug.setlocal(1, 1, 99), x)"));
        Assert.Equal("(vararg)\t7\n", Run("local function f(...) return debug.getlocal(1, -1) end\nprint(f(7, 8))"));
        Assert.Equal("a\n", Run("local function f(a, b) end\nprint(debug.getlocal(f, 1))"));
    }

    [Fact]
    public void Getlocal_rejects_a_level_out_of_range()
    {
        Assert.Equal("false\tbad argument #1 to 'getlocal' (level out of range)\n", Run("print(pcall(debug.getlocal, 50, 1))"));
    }

    [Fact]
    public void Upvalue_functions_read_write_identify_and_join()
    {
        Assert.Equal("a\t1\ta\t5\t5\n", Run("local a = 1\nlocal function f() return a end\nlocal n, v = debug.getupvalue(f, 1)\nprint(n, v, debug.setupvalue(f, 1, 5), a, f())"));
        Assert.Equal("true\tfalse\n", Run("local a, b = 1, 2\nlocal function f() return a end\nlocal function g() return a, b end\nprint(debug.upvalueid(f, 1) == debug.upvalueid(g, 1), debug.upvalueid(f, 1) == debug.upvalueid(g, 2))"));
        Assert.Equal("2\n", Run("local a, b = 1, 2\nlocal function f() return a end\nlocal function g() return b end\ndebug.upvaluejoin(f, 1, g, 1)\nprint(f())"));
        Assert.Equal("nil\n", Run("local function f() end\nprint(debug.upvalueid(f, 1))"));
    }

    [Fact]
    public void Metatables_are_set_and_read_raw_for_every_type()
    {
        Assert.Equal("true\ttrue\n", Run("local t, mt = {}, {__metatable = 'locked'}\ndebug.setmetatable(t, mt)\nprint(debug.getmetatable(t) == mt, getmetatable(t) == 'locked')"));
        Assert.Equal("42\n", Run("debug.setmetatable(10, {__index = function(n, k) return 42 end})\nprint((5).x)"));
        Assert.Equal("true\n", Run("local mt = {}\ndebug.setmetatable(print, mt)\nprint(debug.getmetatable(print) == mt and debug.getmetatable(type) == mt)"));
    }

    [Fact]
    public void Registry_is_one_table_per_state()
    {
        Assert.Equal("table\ttrue\n", Run("local r = debug.getregistry()\nprint(type(r), r == debug.getregistry())"));
    }

    [Fact]
    public void Line_hook_reports_each_new_line()
    {
        Assert.Equal("3,4,5,\n", Run("local s = ''\ndebug.sethook(function(e, l) s = s .. l .. ',' end, 'l')\nlocal a = 1\nlocal b = 2\ndebug.sethook()\nprint(s)"));
    }

    [Fact]
    public void Line_hook_reports_loop_back_edges()
    {
        Assert.Equal("3,4,3,4,3,4,3,6,\n", Run("local s = ''\ndebug.sethook(function(e, l) s = s .. l .. ',' end, 'l')\nfor i = 1, 3 do\n  local x = i\nend\ndebug.sethook()\nprint(s)"));
    }

    [Fact]
    public void Call_and_return_hooks_bracket_functions_including_host_ones()
    {
        Assert.Equal("return call return call \n", Run("local ev = ''\nlocal function f() end\ndebug.sethook(function(e) ev = ev .. e .. ' ' end, 'cr')\nf()\ndebug.sethook()\nprint(ev)"));
    }

    [Fact]
    public void Tail_call_event_is_reported()
    {
        Assert.Equal("true\n", Run("local ev = ''\nlocal function g() end\nlocal function f() return g() end\ndebug.sethook(function(e) ev = ev .. e .. ' ' end, 'c')\nf()\ndebug.sethook()\nprint(ev:find('tail call', 1, true) ~= nil)"));
    }

    [Fact]
    public void Count_hook_fires_every_n_instructions()
    {
        Assert.Equal("true\n", Run("local n = 0\ndebug.sethook(function() n = n + 1 end, '', 1)\nlocal a = 1 local b = 2 local c = 3\ndebug.sethook()\nprint(n >= 3)"));
    }

    [Fact]
    public void Gethook_returns_function_mask_and_count()
    {
        Assert.Equal("true\tcrl\t5\n", Run("local f = function() end\ndebug.sethook(f, 'crl', 5)\nlocal g, m, c = debug.gethook()\ndebug.sethook()\nprint(g == f, m, c)"));
        Assert.Equal("nil\n", Run("print(debug.gethook())"));
    }

    [Fact]
    public void Hook_function_sees_the_hooked_function_at_level_two()
    {
        Assert.Equal("3\n", Run("local l\ndebug.sethook(function() l = debug.getinfo(2, 'l').currentline end, 'l')\nlocal a = 1\ndebug.sethook()\nprint(l - 1)"));
    }

    [Fact]
    public void Hooks_are_per_coroutine()
    {
        Assert.Equal("nil\ttrue\n", Run("local co = coroutine.create(function() end)\ndebug.sethook(co, print, 'l')\nprint(debug.gethook(), debug.gethook(co) == print)"));
    }

    [Fact]
    public void Error_in_a_hook_propagates_to_the_hooked_code()
    {
        Assert.Equal("false\tstop\n", Run("local ok, e = pcall(function() debug.sethook(function() debug.sethook() error('stop', 0) end, 'l')\n  local a = 1\nend)\nprint(ok, e)"));
    }

    [Fact]
    public void Setcstacklimit_is_accepted()
    {
        Assert.Equal("number\n", Run("print(type(debug.setcstacklimit(200)))"));
    }

    // ---- CLI: uncaught errors carry a traceback ----

    private static (int Code, string Err) RunScript(string source)
    {
        string path = Path.Combine(Path.GetTempPath(), "script-" + Guid.NewGuid().ToString("N") + ".lua"); // never cwd-relative: the suite runner changes the cwd
        File.WriteAllText(path, source);
        try
        {
            var o = new StringWriter { NewLine = "\n" };
            var e = new StringWriter { NewLine = "\n" };
            int code = Program.Run([path], o, e);
            return (code, e.ToString().Replace(DebugLib.ChunkId("@" + path), "script.lua"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Cli_prints_message_and_traceback_for_an_uncaught_error()
    {
        var (code, err) = RunScript("local function f()\n  local x = nil\n  return x.y\nend\nf()\n");
        Assert.Equal(1, code);
        Assert.Equal("lunette: script.lua:3: attempt to index a nil value (local 'x')\nstack traceback:\n\tscript.lua:3: in local 'f'\n\tscript.lua:5: in main chunk\n", err);
    }

    [Fact]
    public void Cli_traceback_follows_a_function_that_called_error()
    {
        var (_, err) = RunScript("local function f() error('boom') end\nf()\n");
        Assert.StartsWith("lunette: script.lua:1: boom\nstack traceback:\n\t[C]: in function 'error'\n\tscript.lua:1: in local 'f'\n\tscript.lua:2: in main chunk", err);
    }

    [Fact]
    public void Cli_honours_tostring_on_error_objects()
    {
        var (_, err) = RunScript("error(setmetatable({}, {__tostring = function() return 'custom' end}))\n");
        Assert.StartsWith("lunette: custom\n", err);
    }

    [Fact]
    public void Uncaught_error_exception_carries_the_traceback()
    {
        var state = new LuaState { Output = new StringWriter() };
        var ex = Assert.Throws<LuaException>(() => state.DoString("local function f() error('x') end\nf()"));
        Assert.Contains("stack traceback:\n\t[C]: in function 'error'\n\t(load):1: in local 'f'", ex.Traceback);
    }

    [Fact]
    public void Xpcall_handler_sees_the_faulting_stack()
    {
        string o = Run("local function f() error('oops') end\nlocal ok, tb = xpcall(f, debug.traceback)\nprint(tb)");
        Assert.StartsWith("(load):1: oops\nstack traceback:\n", o);
        Assert.Contains("in function 'error'", o);
        Assert.Contains("(load):1: in function <(load):1>", o);
    }
}
