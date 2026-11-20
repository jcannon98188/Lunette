using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Result placement of the Call/Return/TailCall fast paths: scalar copies for up to three values, Array.Copy beyond, nil padding,
/// open and closed upvalue reads, and builtin results through the shared result arrays. Expected values are the Lua 5.4 results.
/// </summary>
public class CallReturnTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Theory]
    [InlineData("local function f() return 1, 2, 3, 4, 5 end local a, b, c = f() print(a, b, c)", "1\t2\t3")]
    [InlineData("local function f() return 1 end local a, b, c = f() print(a, b, c)", "1\tnil\tnil")]
    [InlineData("local function f() end local a, b = f() print(a, b)", "nil\tnil")]
    [InlineData("local function f() return 1, 2, 3, 4 end print(f())", "1\t2\t3\t4")]
    [InlineData("local function f() return 1, 2 end print((f()))", "1")]
    [InlineData("local function f() return 'a', 'b', 'c' end print(select('#', f()), (select(2, f())))", "3\tb")]
    [InlineData("local function f() return 1, 2, 3, 4, 5, 6 end local t = {f()} print(#t, t[6])", "6\t6")]
    public void Return_places_results(string source, string expected) => Assert.Equal(expected + "\n", Run(source));

    [Theory]
    [InlineData("local function g(a, b, c) return c, b, a end local function f(a, b, c) return g(a, b, c) end print(f(1, 2, 3))", "3\t2\t1")]
    [InlineData("local function g(...) return select('#', ...), ... end local function f(a, b, c, d, e) return g(a, b, c, d, e) end print(f(1, 2, 3, 4, 5))", "5\t1\t2\t3\t4\t5")]
    [InlineData("local function g(a) return a end local function f(a) return g(a) end print(f(7), f())", "7\tnil")]
    public void Tail_calls_move_their_arguments(string source, string expected) => Assert.Equal(expected + "\n", Run(source));

    [Fact]
    public void A_missing_argument_is_nil_and_extra_arguments_are_dropped()
    {
        Assert.Equal("nil\tnil\n1\t2\n", Run("local function f(a, b) return a, b end print(f()) print(f(1, 2, 3))"));
    }

    [Fact]
    public void Upvalues_are_read_open_and_closed()
    {
        const string src = """
            local function counter()
              local n = 0
              return function() n = n + 1 return n end, function() return n end
            end
            local inc, get = counter()
            inc() inc()
            local x = 5
            local function open() return x end
            x = 6
            print(get(), open(), inc(), get())
            """;
        Assert.Equal("2\t6\t3\t3\n", Run(src));
    }

    [Fact]
    public void Recursion_reuses_frames_correctly()
    {
        Assert.Equal("55\t89\n", Run("local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end print(fib(10), fib(11))"));
    }

    [Fact]
    public void Builtin_results_are_adjusted_to_the_wanted_count()
    {
        Assert.Equal("1\t2\tnil\n3\n99\t4\n", Run("local a, b, c = math.min(1), 2 print(a, b, c) print((string.byte('abc', 3, 3) - 96)) print(string.byte('ab', 1, 2) + 2, 4)"));
    }
}
