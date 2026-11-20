using Xunit;

namespace Lunette.Tests;

public class ArithmeticTests
{
    private static string Run(string source)
    {
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.DoString(source);
        return output.ToString();
    }

    [Theory]
    [InlineData("print(1 + 2)", "3\n")]
    [InlineData("print(7 - 10)", "-3\n")]
    [InlineData("print(6 * 7)", "42\n")]
    [InlineData("print(2 + 3 * 4)", "14\n")]
    [InlineData("print((2 + 3) * 4)", "20\n")]
    [InlineData("print(-5 + 2)", "-3\n")]
    [InlineData("print(0xff)", "255\n")]
    [InlineData("print(9223372036854775807 + 1)", "-9223372036854775808\n")]
    public void Integer_arithmetic_stays_integer(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Theory]
    [InlineData("print(1.5 + 1)", "2.5\n")]
    [InlineData("print(1 + 2.0)", "3.0\n")]
    [InlineData("print(10 / 2)", "5.0\n")]
    [InlineData("print(7 / 2)", "3.5\n")]
    [InlineData("print(2 ^ 10)", "1024.0\n")]
    [InlineData("print(2 ^ 3 ^ 2)", "512.0\n")]
    [InlineData("print(-2 ^ 2)", "-4.0\n")]
    [InlineData("print(1e15)", "1e+15\n")]
    [InlineData("print(0.1)", "0.1\n")]
    [InlineData("print(1 / 3)", "0.33333333333333\n")]
    [InlineData("print(1 / 0)", "inf\n")]
    [InlineData("print(-1 / 0)", "-inf\n")]
    [InlineData("print(3 // 2.0)", "1.0\n")]
    public void Float_arithmetic(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Theory]
    [InlineData("print(7 // 2)", "3\n")]
    [InlineData("print(-7 // 2)", "-4\n")]
    [InlineData("print(7 % 3)", "1\n")]
    [InlineData("print(-7 % 3)", "2\n")]
    [InlineData("print(7 % -3)", "-2\n")]
    [InlineData("print(5.5 % 2)", "1.5\n")]
    public void Floor_division_and_modulo_round_toward_negative_infinity(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Fact]
    public void Print_separates_arguments_with_tabs_and_handles_nil_and_booleans() =>
        Assert.Equal("1\t2.0\tnil\ttrue\tfalse\n", Run("print(1, 2.0, nil, true, false)"));

    [Fact]
    public void Print_with_no_arguments_prints_an_empty_line() =>
        Assert.Equal("\n", Run("print()"));

    [Fact]
    public void Locals_and_globals_hold_values_across_statements() =>
        Assert.Equal("7\n11\n", Run("local a = 3 -- comment\nlocal b = a + 4\nx = b + a + 1\nprint(b) print(x)"));

    [Fact]
    public void Integer_division_by_zero_raises_a_lua_error()
    {
        var ex = Assert.Throws<LuaException>(() => Run("print(1 // 0)"));
        Assert.Contains("attempt to divide by zero", ex.Message);
    }

    [Fact]
    public void Arithmetic_on_nil_raises_a_lua_error()
    {
        var ex = Assert.Throws<LuaException>(() => Run("print(nil + 1)"));
        Assert.Contains("attempt to perform arithmetic on a nil value", ex.Message);
    }

    [Fact]
    public void Syntax_errors_raise_a_lua_error() =>
        Assert.Throws<LuaException>(() => Run("print(1 +)"));
}
