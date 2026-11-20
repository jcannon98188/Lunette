using Xunit;

namespace Lunette.Tests;

/// <summary>Engine behaviours found by running the official suite against the full base library.</summary>
public class IntegrationRegressionTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void A_parameter_list_cannot_start_with_a_comma()
    {
        Assert.Contains("<name> expected near ','", Run("print(load('function a (, ...) end'))"));
    }

    [Fact]
    public void Numeric_for_coerces_numeric_strings()
    {
        Assert.Equal("5\n", Run("local n = 0 for i = '10', '1', '-2' do n = n + 1 end print(n)"));
    }

    [Fact]
    public void Numeric_for_names_the_bad_value_type()
    {
        Assert.Contains("bad 'for' initial value (number expected, got table)", Run("print(pcall(function() for i = {}, 1 do end end))"));
    }

    [Fact]
    public void Nested_pcall_depth_is_bounded_like_the_reference_c_stack()
    {
        Assert.Equal("false\n", Run("""
            local function loop() assert(pcall(loop)) end
            print((pcall(loop)))
            """));
    }

    [Fact]
    public void A_host_function_can_be_a_coroutine_body_and_yield_through_pcall()
    {
        Assert.Equal("true\t1\ntrue\ttrue\t7\n", Run("""
            local co = coroutine.create(pcall)
            print(coroutine.resume(co, function() return coroutine.yield(1) end))
            print(coroutine.resume(co, 7))
            """));
    }

    [Fact]
    public void Pcall_of_pcall_stays_yieldable()
    {
        Assert.Equal("1\ntrue\ttrue\t9\n", Run("""
            local f = coroutine.wrap(function() return xpcall(pcall, print, function() return coroutine.yield(1) end) end)
            print(f())
            print(f(9))
            """));
    }

    [Fact]
    public void Pairs_metamethod_may_yield()
    {
        Assert.Equal("2\n", Run("""
            local t = setmetatable({}, {__pairs = function(t) local n = coroutine.yield() return next, {n}, nil end})
            local co = coroutine.wrap(function() local c = 0 for _ in pairs(t) do c = c + 1 end return c end)
            co()
            print(co(2) + 1)
            """));
    }

    [Fact]
    public void Calling_a_non_function_metamethod_names_the_event()
    {
        Assert.Contains("attempt to call a number value (metamethod 'add')",
            Run("print(pcall(function() return setmetatable({}, {__add = 34}) + 1 end))"));
    }

    [Fact]
    public void Errors_use_the_dunder_name_of_a_table_type()
    {
        Assert.Contains("attempt to perform arithmetic on a My Type value",
            Run("local x = setmetatable({}, {__name = 'My Type'}) print(pcall(function() return x + 1 end))"));
    }

    [Fact]
    public void String_format_rejects_flags_a_conversion_does_not_take()
    {
        Assert.Contains("invalid conversion '%010c' to 'format'", Run("print(pcall(string.format, '%010c', 65))"));
    }

    [Fact]
    public void Closing_a_coroutine_from_its_own_close_handler_is_an_error_not_a_crash()
    {
        Assert.Equal("true\t20\nin close\tfalse\tcannot close a running coroutine\ntrue\n", Run("""
            local co
            co = coroutine.create(function()
              local x <close> = setmetatable({}, {__close = function() print('in close', pcall(coroutine.close, co)) end})
              coroutine.yield(20)
            end)
            print(coroutine.resume(co))
            print(coroutine.close(co))
            """));
    }
}
