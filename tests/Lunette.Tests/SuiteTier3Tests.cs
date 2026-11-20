using Xunit;

namespace Lunette.Tests;

/// <summary>Engine fixes found by db.lua, errors.lua and locals.lua (ticket 38). Expected strings come from the reference binary.</summary>
public class SuiteTier3Tests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Builtin_called_from_a_builtin_reports_its_qualified_name() =>
        Assert.Equal("false\tbad argument #1 to 'table.sort' (table expected, got number)\n",
            Run("print(pcall(table.sort, {1,2,3}, table.sort))"));

    [Fact]
    public void Userdata_arguments_are_named_by_their_metatable() =>
        Assert.Equal("false\tbad argument #1 to 'math.sin' (number expected, got FILE*)\n",
            Run("print(pcall(math.sin, io.stdin))"));

    [Fact]
    public void Upvalue_ids_are_light_userdata_in_argument_errors() =>
        Assert.Contains("light userdata",
            Run("print(pcall(debug.setuservalue, debug.upvalueid(function() return debug end, 1), {}))"));

    [Fact]
    public void Too_many_locals_is_a_compile_error() =>
        Assert.Contains("too many local variables (limit is 200) in function at line 1",
            Run("local s = 'function f()\\n local ' .. string.rep('a,', 300) .. 'b'\nprint(select(2, load(s)))"));

    [Fact]
    public void Deep_nesting_is_a_compile_error_not_a_crash() =>
        Assert.Contains("too many C levels",
            Run("print(select(2, load(string.rep('(', 500))))"));

    [Fact]
    public void Syntax_errors_quote_the_raw_token() =>
        Assert.Contains("near '[[a]]'", Run("print(select(2, load('[[a]]')))"));

    [Fact]
    public void Traceback_of_a_pcall_of_a_builtin_names_pcall() =>
        Assert.Contains("in function 'pcall'", Run("print(select(2, pcall(debug.traceback)))"));

    [Fact]
    public void A_yield_function_can_be_a_close_handler()
    {
        const string src = """
            local co = coroutine.wrap(function()
              local x <close> = setmetatable({}, {__close = coroutine.yield})
              return 1
            end)
            print(select('#', co()))
            print(co())
            """;
        Assert.Equal("2\n1\n", Run(src));
    }
}
