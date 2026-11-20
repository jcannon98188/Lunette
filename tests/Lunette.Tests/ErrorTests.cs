using Xunit;

namespace Lunette.Tests;

public class ErrorTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    // The chunk name is "=(load)", so positions read "(load):<line>:".

    [Fact]
    public void Pcall_returns_true_and_the_results_on_success() =>
        Assert.Equal("true\t1\t2\t3\n", Run("print(pcall(function(...) return ... end, 1, 2, 3))"));

    [Fact]
    public void Pcall_returns_false_and_a_positioned_message_on_error() =>
        Assert.Equal("false\t(load):1: boom\n", Run("print(pcall(function() error('boom') end))"));

    [Fact]
    public void Error_levels_pick_the_reported_position() =>
        Assert.Equal("(load):1: a\n(load):4: b\nc\n", Run("""
            local ok, e = pcall(function() error('a', 1) end) print(e)
            local function lvl2() error('b', 2) end
            ok, e = pcall(function()
              lvl2() end) print(e)
            ok, e = pcall(function() error('c', 0) end) print(e)
            """));

    [Fact]
    public void Error_with_non_string_values_passes_them_through_unchanged()
    {
        Assert.Equal("false\ttable\n", Run("local t = {} local ok, e = pcall(function() error(t) end) print(ok, e == t and 'table')"));
        Assert.Equal("false\tnil\n", Run("print(pcall(function() error(nil) end))"));
        Assert.Equal("false\t42\n", Run("print(pcall(error, 42))"));
    }

    [Fact]
    public void Error_called_directly_by_pcall_has_no_position() =>
        Assert.Equal("false\tx\n", Run("print(pcall(error, 'x'))"));

    [Fact]
    public void Pcall_of_a_non_callable_reports_it_without_throwing() =>
        Assert.Equal("false\tattempt to call a nil value\n", Run("print(pcall(nil))"));

    [Fact]
    public void Pcall_without_arguments_is_an_argument_error() =>
        Assert.Equal("false\tbad argument #1 to 'pcall' (value expected)\n", Run("print(pcall(pcall))"));

    [Fact]
    public void Nested_pcalls_catch_at_the_nearest_level() =>
        Assert.Equal("true\tfalse\t(load):1: inner\n", Run(
            "print(pcall(function() return pcall(function() error('inner') end) end))"));

    [Fact]
    public void Error_rethrown_from_a_handler_reaches_the_outer_pcall() =>
        Assert.Equal("false\t(load):1: again\n", Run(
            "print(pcall(function() local ok, e = pcall(error, 'first') error('again') end))"));

    [Fact]
    public void Pcall_result_count_follows_the_call_site()
    {
        Assert.Equal("true\tnil\n", Run("local a, b = pcall(function() end) print(a, b)"));
        Assert.Equal("false\tx\tnil\n", Run("local a, b, c = pcall(error, 'x') print(a, b, c)"));
        Assert.Equal("3\n", Run("local t = {pcall(function() return 1, 2 end)} print(#t)"));
        Assert.Equal("2\n", Run("local t = {pcall(error, 'x')} print(#t)"));
    }

    [Fact]
    public void Pcall_in_tail_position_passes_its_results_on() =>
        Assert.Equal("false\t(load):1: t\n", Run("local function f() return pcall(function() error('t') end) end print(f())"));

    [Fact]
    public void Runtime_errors_inside_pcall_are_caught_with_variable_names()
    {
        Assert.Equal("false\t(load):1: attempt to index a nil value (global 'x')\n", Run("print(pcall(function() return x.y end))"));
        Assert.Equal("false\t(load):1: attempt to index a nil value (local 'v')\n", Run("print(pcall(function() local v v.y = 1 end))"));
        Assert.Equal("false\t(load):1: attempt to index a nil value (field 'a')\n", Run("local t = {} print(pcall(function() return t.a.b end))"));
        Assert.Equal("false\t(load):1: attempt to index a nil value (upvalue 'u')\n", Run("local u print(pcall(function() return u.k end))"));
        Assert.Equal("false\t(load):1: attempt to call a nil value (global 'nofn')\n", Run("print(pcall(function() nofn() end))"));
        Assert.Equal("false\t(load):1: attempt to call a nil value (method 'm')\n", Run("local t = {} print(pcall(function() t:m() end))"));
        Assert.Equal("false\t(load):1: attempt to call a nil value (field 'f')\n", Run("local t = {} print(pcall(function() t.f() end))"));
        Assert.Equal("false\t(load):1: attempt to perform arithmetic on a nil value (global 'n')\n", Run("print(pcall(function() return n + 1 end))"));
        Assert.Equal("false\t(load):1: attempt to perform arithmetic on a nil value (local 'z')\n", Run("print(pcall(function() local z return 1 + z end))"));
    }

    [Fact]
    public void Generic_for_over_a_non_function_names_the_iterator() =>
        Assert.Equal("false\t(load):1: attempt to call a number value (for iterator 'for iterator')\n",
            Run("print(pcall(function() for i in 5 do end end))"));

    [Fact]
    public void Library_errors_get_the_callers_position()
    {
        Assert.Equal("false\t(load):1: bad argument #1 to 'rawlen' (table or string expected, got number)\n", Run("print(pcall(function() rawlen(1) end))"));
        Assert.Equal("false\tbad argument #1 to 'rawlen' (table or string expected, got number)\n", Run("print(pcall(rawlen, 1))"));
    }

    [Fact]
    public void Assert_returns_all_arguments_when_true() =>
        Assert.Equal("1\t2\t3\n", Run("print(assert(1, 2, 3))"));

    [Fact]
    public void Assert_failures_use_the_message_or_a_default()
    {
        Assert.Equal("false\t(load):1: assertion failed!\n", Run("print(pcall(function() assert(false) end))"));
        Assert.Equal("false\tX\n", Run("print(pcall(assert, false, 'X', 1))"));
        Assert.Equal("false\tnil\n", Run("print(pcall(assert, nil, nil))"));
        Assert.Equal("true\n", Run("local t = {} local ok, e = pcall(assert, false, t) print(e == t)"));
        Assert.Contains("value expected", Run("print(pcall(assert))"));
    }

    [Fact]
    public void Xpcall_runs_the_handler_with_the_error_and_returns_its_result()
    {
        Assert.Equal("false\tH\n", Run("print(xpcall(function() error('oops') end, function(m) return 'H' end))"));
        Assert.Equal("false\t(load):1: oops\n", Run("print(xpcall(function() error('oops') end, function(m) return m end))"));
        Assert.Equal("true\t7\n", Run("print(xpcall(function(a, b) return a + b end, print, 3, 4))"));
        Assert.Equal("false\tx\n", Run("print(xpcall(function() error({msg = 'x'}) end, function(r) return r.msg end))"));
    }

    [Fact]
    public void Xpcall_requires_a_function_handler() =>
        Assert.Contains("bad argument #2 to 'xpcall' (function expected, got no value)", Run("print(pcall(xpcall, print))"));

    [Fact]
    public void Xpcall_handler_sees_the_faulting_stack_and_may_call_functions()
    {
        // The handler runs before the stack unwinds; it can call back into Lua without disturbing the result.
        Assert.Equal("false\t15\n", Run("""
            local function deep(n) if n == 0 then error('x') end deep(n - 1) end
            print(xpcall(deep, function(m) local s = 0 for i = 1, 5 do s = s + i end return s end, 10))
            """));
    }

    [Fact]
    public void Error_in_an_xpcall_handler_is_passed_back_to_the_handler_until_it_gives_up()
    {
        Assert.Equal("false\terror in error handling\n", Run("print(xpcall(error, error))"));
        Assert.Equal("false\trecovered\n", Run("""
            local calls = 0
            print(xpcall(function() error('first') end, function(m)
              calls = calls + 1
              if calls < 3 then error('again') end
              return 'recovered'
            end))
            """));
    }

    [Fact]
    public void Stack_overflow_is_catchable_and_the_state_stays_usable()
    {
        string src = """
            local function loop() return 1 + loop() end
            local ok, msg = pcall(loop)
            print(ok, string_find_overflow)
            """;
        Assert.Equal("false\tnil\n", Run(src));
        Assert.Contains("stack overflow", Run("local function loop() return 1 + loop() end local ok, msg = pcall(loop) print(msg)"));
        Assert.Equal("ok\n", Run("local function loop() return 1 + loop() end pcall(loop) print('ok')"));
    }

    [Fact]
    public void Xpcall_handler_has_headroom_after_a_stack_overflow() =>
        Assert.Equal("false\t15\n", Run("""
            local function loop() return 1 + loop() end
            print(xpcall(loop, function(m) local s = 0 for i = 1, 5 do s = s + i end return s end))
            """));

    [Fact]
    public void Overflow_inside_the_handler_gives_error_in_error_handling() =>
        Assert.Equal("false\terror in error handling\n", Run("""
            local function loop() return 1 + loop() end
            local function inner(m) return pcall(loop) end
            print(xpcall(loop, function(m)
              local ok, e = pcall(loop)
              return e
            end))
            """));

    [Fact]
    public void Errors_in_metamethods_unwind_through_the_host_boundary()
    {
        Assert.Equal("false\t(load):1: meta\n", Run("""
            local t = setmetatable({}, {__index = function() error('meta') end})
            print(pcall(function() return t.x end))
            """));
        Assert.Equal("ok\n", Run("""
            local t = setmetatable({}, {__index = function() error('meta') end})
            pcall(function() return t.x end)
            print('ok')
            """));
    }

    [Fact]
    public void Upvalues_of_unwound_frames_are_closed_with_their_values()
    {
        Assert.Equal("5\n", Run("""
            local get
            pcall(function()
              local x = 5
              get = function() return x end
              error('stop')
            end)
            print(get())
            """));
    }

    [Fact]
    public void Execution_continues_with_correct_registers_after_a_caught_error() =>
        Assert.Equal("1\t2\t3\tfalse\n", Run("""
            local a, b, c = 1, 2, 3
            local ok = pcall(function() error('e') end)
            print(a, b, c, ok)
            """));

    [Fact]
    public void An_uncaught_error_reaches_the_host_with_its_value()
    {
        var ex = Assert.Throws<LuaException>(() => Run("error('top')"));
        Assert.Equal("(load):1: top", ex.Message);
        var ex2 = Assert.Throws<LuaException>(() => Run("error({})"));
        Assert.Equal("(error object is a table value)", ex2.Message);
    }

    [Fact]
    public void Pcall_through_a_native_boundary_unwinds_to_the_right_frame()
    {
        // pcall(pcall, f): the inner pcall runs through the builtin body (no direct Lua closure callee).
        Assert.Equal("true\tfalse\t(load):1: z\n", Run("print(pcall(pcall, function() error('z') end))"));
        Assert.Equal("true\ttrue\t9\n", Run("print(pcall(pcall, function() return 9 end))"));
    }

    [Fact]
    public void A_library_function_called_by_pcall_reports_its_qualified_name() =>
        Assert.Equal("false\tbad argument #2 to 'string.format' (number has no integer representation)\n", Run("print(pcall(string.format, '%d', 3.5))"));

    [Fact]
    public void Format_reports_a_missing_argument_before_a_bad_conversion() =>
        Assert.Equal("false\tbad argument #2 to 'string.format' (no value)\nfalse\tbad argument #2 to 'string.format' (no value)\n",
            Run("print(pcall(string.format, '%')) print(pcall(string.format, '%5%'))"));
}
