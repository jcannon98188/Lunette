using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Error-message and debug-library fidelity with PUC Lua 5.4.9 (ticket 27). Expected strings were taken from the reference binary.
/// The chunk name is "=(load)", so positions read "(load):&lt;line&gt;:".
/// </summary>
public class ErrorFidelityTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    // ---- function names at the call site (luaL_argerror) ----

    [Theory]
    [InlineData("local sf = string.format\nprint(pcall(function() return sf('%d', {}) end))", "false\t(load):2: bad argument #2 to 'sf' (number expected, got table)\n")]
    [InlineData("local t = {f = string.rep}\nprint(pcall(function() return t.f() end))", "false\t(load):2: bad argument #1 to 'f' (string expected, got no value)\n")]
    [InlineData("print(pcall(function() local a = string.rep; return a() end))", "false\t(load):1: bad argument #1 to 'a' (string expected, got no value)\n")]
    [InlineData("print(pcall(function() return ('x'):rep({}) end))", "false\t(load):1: bad argument #1 to 'rep' (number expected, got table)\n")]
    [InlineData("local t = {f = string.rep}\nprint(pcall(function() return t:f() end))", "false\t(load):2: calling 'f' on bad self (string expected, got table)\n")]
    public void Argument_errors_name_the_function_from_its_call_site(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Fact]
    public void A_builtin_used_as_a_metamethod_is_named_after_the_event() =>
        Assert.Equal(
            "false\t(load):1: bad argument #1 to 'index' (string expected, got table)\n" +
            "false\t(load):2: bad argument #1 to 'add' (number expected, got table)\n",
            Run("""
                print(pcall(function() return setmetatable({}, {__index = string.rep}).x end))
                print(pcall(function() return setmetatable({}, {__add = math.floor}) + 1 end))
                """));

    [Fact]
    public void An_error_from_a_builtin_run_as_a_metamethod_carries_the_line_of_the_faulting_instruction() =>
        Assert.Equal("false\t(load):5: bad argument #1 to 'add' (number expected, got table)\n", Run("""
            local t = setmetatable({}, {__add = math.floor})
            print(pcall(function()
              local a = 1
              local b = 2
              return t + a
            end))
            """));

    [Fact]
    public void A_builtin_called_straight_from_pcall_is_named_by_its_global_path() =>
        Assert.Equal(
            "false\tbad argument #1 to 'math.random' (number expected, got table)\n" +
            "false\tbad argument #1 to 'string.rep' (string expected, got no value)\n",
            Run("print(pcall(math.random, {}))\nprint(pcall(string.rep))"));

    // ---- stack overflow on the first frame of a pcall target ----

    [Fact]
    public void Pcall_catches_a_stack_overflow_of_its_own_target_frame() =>
        Assert.Equal("true\n", Run("""
            local depth, firstFail = 0, nil
            local function r()
              depth = depth + 1
              local ok = pcall(r)
              if not ok and not firstFail then firstFail = depth end
              return ok
            end
            r()
            print(firstFail == depth)
            """));

    // ---- debug.traceback base level ----

    [Fact]
    public void A_script_run_by_the_driver_has_the_C_entry_at_the_bottom_of_its_traceback()
    {
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.RunScript("print(debug.traceback('x'))"u8.ToArray(), "=s", []);
        Assert.Equal("x\nstack traceback:\n\ts:1: in main chunk\n\t[C]: in ?\n", output.ToString());
    }

    [Fact]
    public void A_chunk_run_by_the_host_has_no_C_entry_at_the_bottom_of_its_traceback() =>
        Assert.Equal("x\nstack traceback:\n\t(load):1: in main chunk\n", Run("print(debug.traceback('x'))"));

    // ---- getuservalue / setuservalue ----

    [Fact]
    public void Userdata_carry_one_settable_user_value()
    {
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.SetGlobal("u", LuaValue.FromUserdata(state.CreateUserdata(new object())));
        state.DoString("""
            print(debug.getuservalue(u))
            print(debug.setuservalue(u, 'v') == u)
            print(debug.getuservalue(u))
            print(debug.getuservalue(u, 2))
            print(debug.setuservalue(u, 'w', 2))
            print(debug.getuservalue(1))
            print(pcall(debug.setuservalue, 1, 2))
            print(pcall(debug.setuservalue, u))
            """);
        Assert.Equal(
            "nil\ttrue\n" +
            "true\n" +
            "v\ttrue\n" +
            "nil\n" +
            "nil\n" +
            "nil\n" +
            "false\tbad argument #1 to 'debug.setuservalue' (userdata expected, got number)\n" +
            "false\tbad argument #2 to 'debug.setuservalue' (value expected)\n",
            output.ToString());
    }

    // ---- warn ----

    [Fact]
    public void Warn_writes_through_the_gc_warning_sink_once_enabled()
    {
        var state = new LuaState { Output = new StringWriter() };
        var warnings = new List<string>();
        state.Gc.Warn = warnings.Add;
        state.DoString("warn('off') warn('@on') warn('a', 'b') warn('@off') warn('hidden')");
        Assert.Equal(["ab"], warnings);
    }

    // ---- hooks ----

    [Fact]
    public void Call_and_return_events_fire_for_a_builtin_called_by_a_builtin() =>
        Assert.Equal(
            "return:sethook:C call:pcall:C call:nil:C return:nil:C return:pcall:C call:sethook:C\n",
            Run("""
                local ev = {}
                local function hk(e)
                  local i = debug.getinfo(2, 'nS')
                  ev[#ev + 1] = e .. ':' .. tostring(i.name) .. ':' .. i.what
                end
                debug.sethook(hk, 'cr')
                pcall(string.len, 'x')
                debug.sethook()
                print(table.concat(ev, ' '))
                """));

    [Fact]
    public void A_hook_set_inside_a_callback_reaches_the_calling_loop() =>
        Assert.Equal("true\t5\t6\n", Run("""
            local lines = {}
            local function lh(e, l) lines[#lines + 1] = l end
            local function setit() debug.sethook(lh, 'l') return 0 end
            string.gsub("a", "a", setit)
            local x = 1
            local y = 2
            debug.sethook()
            print(#lines > 0, lines[1], lines[2])
            """));

    [Fact]
    public void A_hook_set_inside_a_metamethod_reaches_the_calling_loop() =>
        Assert.Equal("true\n", Run("""
            local lines = {}
            local function lh(e, l) lines[#lines + 1] = l end
            local obj = setmetatable({}, {__index = function() debug.sethook(lh, 'l') end})
            local z = obj.foo
            local w = 2
            debug.sethook()
            print(#lines > 0)
            """));

    // ---- parser close-matching messages ----

    [Theory]
    [InlineData("x = (1 + \n 2", "[string \"x = (1 + ...\"]:2: ')' expected (to close '(' at line 1) near <eof>")]
    [InlineData("f(1,\n 2", "[string \"f(1,...\"]:2: ')' expected (to close '(' at line 1) near <eof>")]
    [InlineData("x = {1,\n 2", "[string \"x = {1,...\"]:2: '}' expected (to close '{' at line 1) near <eof>")]
    [InlineData("local t = {\n 1, 2\n y = 3", "[string \"local t = {...\"]:3: '}' expected (to close '{' at line 1) near 'y'")]
    [InlineData("x = {1, 2 3}", "[string \"x = {1, 2 3}\"]:1: '}' expected near '3'")]
    [InlineData("function f(\n a, b\n x end", "[string \"function f(...\"]:3: ')' expected near 'x'")]
    public void Unclosed_brackets_name_their_opener_when_it_is_on_another_line(string chunk, string expected)
    {
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.SetGlobal("src", chunk);
        state.DoString("print(select(2, load(src)))");
        Assert.Equal(expected + "\n", output.ToString());
    }
}
