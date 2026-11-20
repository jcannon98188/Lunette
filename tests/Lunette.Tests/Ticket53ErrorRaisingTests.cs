using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Ticket 53: raising a runtime error or error("str") builds its message with the chunk position and variable description
/// without intermediate work that changes the text. These pin the exact messages, tracebacks and host-visible trace data so the
/// cheaper raising paths cannot drift.
/// </summary>
public class Ticket53ErrorRaisingTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    /// <summary>The message pcall reports for a chunk named "=c".</summary>
    private static string Msg(string code)
    {
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.SetGlobal("code", LuaValue.FromString(code));
        state.DoString("local f = assert(load(code, '=c')); local ok, m = pcall(f); print(ok, m)");
        return output.ToString().TrimEnd('\n');
    }

    [Theory]
    [InlineData("local t = {} return t.x.y", "false\tc:1: attempt to index a nil value (field 'x')")]
    [InlineData("local t\nreturn t.x", "false\tc:2: attempt to index a nil value (local 't')")]
    [InlineData("return undefinedglobal.x", "false\tc:1: attempt to index a nil value (global 'undefinedglobal')")]
    [InlineData("local u; return (function() return u.x end)()", "false\tc:1: attempt to index a nil value (upvalue 'u')")]
    [InlineData("local t = {}\nt.x.y = 1", "false\tc:2: attempt to index a nil value (field 'x')")]
    [InlineData("local s = 5 return s.x", "false\tc:1: attempt to index a number value (local 's')")]
    [InlineData("local t = {}\n\nreturn t:nomethod()", "false\tc:3: attempt to call a nil value (method 'nomethod')")]
    [InlineData("return 1 + {}", "false\tc:1: attempt to perform arithmetic on a table value")]
    [InlineData("local a return a + 1", "false\tc:1: attempt to perform arithmetic on a nil value (local 'a')")]
    [InlineData("return #5", "false\tc:1: attempt to get length of a number value")]
    [InlineData("return {} < 1", "false\tc:1: attempt to compare table with number")]
    [InlineData("return nilfn()", "false\tc:1: attempt to call a nil value (global 'nilfn')")]
    [InlineData("return ('x').y.z", "false\tc:1: attempt to index a nil value (field 'y')")]
    [InlineData("error('boom')", "false\tc:1: boom")]
    [InlineData("error('boom', 0)", "false\tboom")]
    [InlineData("\n\nerror('boom ' .. 7)", "false\tc:3: boom 7")]
    [InlineData("local function g() error('deep', 2) end\nlocal function h() g() end\nh()", "false\tc:2: deep")]
    [InlineData("error('lvl3', 3)", "false\t(load):1: lvl3")]
    [InlineData("error(42)", "false\t42")]
    [InlineData("error()", "false\tnil")]
    [InlineData("error('')", "false\tc:1: ")]
    [InlineData("error('café 中')", "false\tc:1: café 中")]
    [InlineData("assert(false)", "false\tc:1: assertion failed!")]
    [InlineData("assert(false, 'custom')", "false\tc:1: custom")]
    [InlineData("string.rep()", "false\tc:1: bad argument #1 to 'rep' (string expected, got no value)")]
    public void Messages_are_exactly_these(string code, string expected) => Assert.Equal(expected, Msg(code));

    [Fact]
    public void Chunk_name_forms_prefix_as_luaO_chunkid()
    {
        Assert.Equal("false\tfile.lua:1: attempt to index a nil value (global 'q')\nfalse\t[string \"return q.x\"]:1: attempt to index a nil value (global 'q')\nfalse\t[string \"line one...\"]:1: boom\n",
            Run("""
                print(pcall(load("return q.x", "@file.lua")))
                print(pcall(load("return q.x")))
                print(pcall(load("error('boom')", "line one\nline two")))
                """));
    }

    [Fact]
    public void A_string_message_with_invalid_utf8_keeps_its_current_bytes()
    {
        string output = Run("""
            local ok, m = pcall(load("error('a\\xffb')", "=c"))
            print(#m, m:byte(1, -1))
            """);
        // "c:1: a" + U+FFFD (EF BF BD) + "b": the message is decoded to text and re-encoded.
        Assert.Equal("10\t99\t58\t49\t58\t32\t97\t239\t191\t189\t98\n", output);
    }

    [Fact]
    public void Long_chunk_names_are_truncated_in_the_prefix()
    {
        string name = "=" + new string('x', 80);
        string expected = new string('x', 59);
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.SetGlobal("n", LuaValue.FromString(name));
        state.DoString("print(select(2, pcall(load('error(\"boom\")', n))))");
        Assert.Equal(expected + ":1: boom\n", output.ToString());
    }

    [Fact]
    public void Xpcall_handler_sees_the_faulting_stack_for_vm_and_error_raised_failures()
    {
        string output = Run("""
            local function vm(t) return t.x.y end
            local function str() error("boom") end
            print(select(2, xpcall(vm, debug.traceback, {})))
            print(select(2, xpcall(str, debug.traceback)))
            print(select(2, xpcall(vm, function(m) return m .. '|' .. debug.getinfo(2, 'l').currentline end, {})))
            """);
        Assert.Equal("""
            (load):1: attempt to index a nil value (field 'x')
            stack traceback:
            	(load):1: in function <(load):1>
            	[C]: in function 'xpcall'
            	(load):3: in main chunk
            (load):2: boom
            stack traceback:
            	[C]: in function 'error'
            	(load):2: in function <(load):2>
            	[C]: in function 'xpcall'
            	(load):4: in main chunk
            (load):1: attempt to index a nil value (field 'x')|1

            """.Replace("\r\n", "\n"), output.Replace("[string \"...\"]", "(load)"));
    }

    [Fact]
    public void An_uncaught_vm_error_keeps_message_line_traceback_and_trace_entries()
    {
        using var state = new LuaState();
        var e = Assert.Throws<LuaException>(() => state.DoString("local function inner(t)\n  return t.x.y\nend\nlocal function outer()\n  return inner({}) + 1\nend\nouter()", "=mods/a.lua"));
        Assert.Equal("mods/a.lua:2: attempt to index a nil value (field 'x')", e.Message);
        Assert.Equal("mods/a.lua", e.Chunk);
        Assert.Equal(2, e.Line);
        Assert.Equal(
            "stack traceback:\n\tmods/a.lua:2: in upvalue 'inner'\n\tmods/a.lua:5: in local 'outer'\n\tmods/a.lua:7: in main chunk",
            e.Traceback);
        var te = e.TraceEntries!;
        Assert.Equal([2, 5, 7], te.Where(x => !x.IsNative).Select(x => x.Line).ToArray());
    }

    [Fact]
    public void An_uncaught_error_call_keeps_traceback_and_trace_entries()
    {
        using var state = new LuaState();
        var e = Assert.Throws<LuaException>(() => state.DoString("local function g()\n  error('bad')\nend\ng()", "=mods/b.lua"));
        Assert.Equal("mods/b.lua:2: bad", e.Message);
        Assert.Equal("stack traceback:\n\t[C]: in function 'error'\n\tmods/b.lua:2: in local 'g'\n\tmods/b.lua:4: in main chunk", e.Traceback);
        Assert.True(e.TraceEntries![0].IsNative);
        Assert.Equal(2, e.TraceEntries![1].Line);
    }

    [Fact]
    public void Caught_vm_errors_close_pending_variables_and_resume_after_the_pcall()
    {
        Assert.Equal("closed\nfalse\tc:3: attempt to index a nil value (local 'n')\tafter\n", Run("""
            local f = load([[
              local x <close> = setmetatable({}, {__close = function() print('closed') end})
              local n
              return n.field
            ]], '=c')
            local ok, m = pcall(f)
            print(ok, m, 'after')
            """));
    }

    [Fact]
    public void Vm_errors_cross_native_boundaries_and_nested_pcall_levels()
    {
        Assert.Equal("false\tc:1: attempt to index a nil value (field 'x')\nfalse\tc:1: attempt to index a nil value (field 'x')\ntrue\tfalse\tattempt to call a nil value\n", Run("""
            local t = {{}, {}}
            print(pcall(table.sort, t, load("local a = ... return a.x.y", "=c")))
            print(pcall(string.gsub, 'abc', '.', load("local a = ... return a.x.y", "=c")))
            print(pcall(pcall, load("return nil.x", "=d")))
            """));
    }

    [Fact]
    public void Errors_in_coroutines_report_to_resume_and_wrap()
    {
        Assert.Equal("false\tc:1: attempt to index a nil value (local 'a')\nfalse\tc:1: boom\n", Run("""
            local co = coroutine.create(load("local a; coroutine.yield(1); return a.x", "=c"))
            coroutine.resume(co)
            print(coroutine.resume(co))
            local co2 = coroutine.create(load("error('boom')", "=c"))
            print(coroutine.resume(co2))
            """));
    }

    [Fact]
    public void Failures_unwound_in_the_loop_deliver_a_variable_number_of_results_to_the_next_call()
    {
        Assert.Equal("""
            end	false	c:1: attempt to index a nil value (global 'q')
            end	false	c:1: attempt to perform arithmetic on a nil value (global 'q')
            end	false	c:1: attempt to call a nil value (global 'q')
            end	false	c:1: attempt to index a nil value (global 'q')
            2	false	c:1: attempt to call a nil value (global 'q')

            """.Replace("\r\n", "\n"), Run("""
            local function try(code) return pcall(load(code, '=c')) end
            print('end', try("return q.x"))
            print('end', try("return q + 1"))
            print('end', try("return q()"))
            print('end', try("q.x = 1"))
            print(select('#', try("local a = q() return a")), try("local a = q() return a"))
            """));
    }

    [Fact]
    public void Error_values_of_other_types_pass_through_unchanged()
    {
        Assert.Equal("true\ttrue\ttrue\n", Run("""
            local E = {}
            local ok1, e1 = pcall(error, E)
            local ok2, e2 = pcall(function() error(E, 2) end)
            local ok3, e3 = pcall(function() error(E, 0) end)
            print(e1 == E, e2 == E, e3 == E)
            """));
    }
}
