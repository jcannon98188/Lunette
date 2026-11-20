using Xunit;

namespace Lunette.Tests;

public class NumberTowerTests
{
    private static string Run(string source)
    {
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.DoString(source);
        return output.ToString();
    }

    [Theory]
    [InlineData("print(5 & 3)", "1\n")]
    [InlineData("print(5 | 3)", "7\n")]
    [InlineData("print(5 ~ 3)", "6\n")]
    [InlineData("print(~0)", "-1\n")]
    [InlineData("print(1 << 62)", "4611686018427387904\n")]
    [InlineData("print(1 << 63)", "-9223372036854775808\n")]
    [InlineData("print(1 << 64)", "0\n")]
    [InlineData("print(-1 >> 1)", "9223372036854775807\n")]
    [InlineData("print(-1 >> 64)", "0\n")]
    [InlineData("print(1 << -1)", "0\n")]
    [InlineData("print(4 >> -1)", "8\n")]
    [InlineData("print(3.0 & 1)", "1\n")]
    [InlineData("print(0xF0 | 0x0F)", "255\n")]
    [InlineData("print(1 | 2 ~ 3 & 4 << 1)", "3\n")]
    [InlineData("print(2 + 3 << 1)", "10\n")]
    [InlineData("print(~5 + 1)", "-5\n")]
    public void Bitwise_operators_work_on_64_bit_integers(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Theory]
    [InlineData("print(1e100)", "1e+100\n")]
    [InlineData("print(2^53)", "9.007199254741e+15\n")]
    [InlineData("print(123456789012345.0)", "1.2345678901234e+14\n")]
    [InlineData("print(12345678901234.0)", "12345678901234.0\n")]
    [InlineData("print(-0.0)", "-0.0\n")]
    [InlineData("print(0.0)", "0.0\n")]
    [InlineData("print(1e-5)", "1e-05\n")]
    [InlineData("print(0.0001)", "0.0001\n")]
    [InlineData("print(3.14159265358979)", "3.1415926535898\n")]
    [InlineData("print(100.0)", "100.0\n")]
    [InlineData("print(1e14)", "1e+14\n")]
    [InlineData("print(5e-324)", "4.9406564584125e-324\n")]
    [InlineData("print(0.1 + 0.2)", "0.3\n")]
    [InlineData("print(2^63)", "9.2233720368548e+18\n")]
    [InlineData("print(-1e15)", "-1e+15\n")]
    public void Floats_format_like_printf_percent_14g(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Theory]
    [InlineData("print(9223372036854775807)", "9223372036854775807\n")]
    [InlineData("print(9223372036854775808)", "9.2233720368548e+18\n")]
    [InlineData("print(0xffffffffffffffff)", "-1\n")]
    [InlineData("print(0x7fffffffffffffff)", "9223372036854775807\n")]
    [InlineData("print(0x10000000000000000)", "0\n")]
    [InlineData("print(0x.8)", "0.5\n")]
    [InlineData("print(0xA.8p0)", "10.5\n")]
    [InlineData("print(0x1p4)", "16.0\n")]
    [InlineData("print(0xep-1)", "7.0\n")]
    [InlineData("print(3e2)", "300.0\n")]
    [InlineData("print(.5)", "0.5\n")]
    [InlineData("print(5.)", "5.0\n")]
    public void Numeric_literals_follow_lua_rules(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Theory]
    [InlineData("print(1 < 1.5, 2^53 == 2^53 + 1, 9007199254740993 < 9007199254740992.0)", "true\ttrue\tfalse\n")]
    [InlineData("print(9223372036854775807 < 9223372036854775808.0, 9223372036854775807 == 9223372036854775808.0)", "true\tfalse\n")]
    [InlineData("print(-9223372036854775807 - 1 == -2^63, -9223372036854775807 - 1 <= -2^63)", "true\ttrue\n")]
    [InlineData("print(0/0 == 0/0, 1 < 0/0, 0/0 < 1)", "false\tfalse\tfalse\n")]
    public void Mixed_integer_float_comparison_is_exact(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Theory]
    [InlineData("print(5 // 0.0)", "inf\n")]
    [InlineData("print(-5 // 0.0)", "-inf\n")]
    [InlineData("print(5.0 % (1/0))", "5.0\n")]
    [InlineData("print(-5 % (1/0))", "inf\n")]
    [InlineData("print(5 % -(1/0))", "-inf\n")]
    [InlineData("print(5.3 % 0 ~= 5.3 % 0)", "true\n")]
    [InlineData("print(-9223372036854775807 - 1, (-9223372036854775807 - 1) // -1)", "-9223372036854775808\t-9223372036854775808\n")]
    [InlineData("print((-9223372036854775807 - 1) % -1)", "0\n")]
    [InlineData("print(3 % -2, -3 % 2, 3.5 % -2, -3.5 % 2)", "-1\t1\t-0.5\t0.5\n")]
    [InlineData("print(2^-1, (-2)^0.5 ~= (-2)^0.5)", "0.5\ttrue\n")]
    public void Float_division_and_modulo_edge_cases(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Theory]
    [InlineData("print(1 % 0)", "attempt to perform 'n%0'")]
    [InlineData("print(1 // 0)", "attempt to divide by zero")]
    public void Integer_division_by_zero_messages_match_puc(string source, string fragment)
    {
        var ex = Assert.Throws<LuaException>(() => Run(source));
        Assert.Contains(fragment, ex.Message);
    }

    [Theory]
    [InlineData("10", "10")]
    [InlineData("  10  ", "10")]
    [InlineData("\t\n10\r\f\v", "10")]
    [InlineData("-10", "-10")]
    [InlineData("+10", "10")]
    [InlineData("0x10", "16")]
    [InlineData("-0x10", "-16")]
    [InlineData("0xffffffffffffffff", "-1")]
    [InlineData("9223372036854775807", "9223372036854775807")]
    [InlineData("-9223372036854775808", "-9223372036854775808")]
    [InlineData("9223372036854775808", "9.2233720368548e+18")]
    [InlineData("1e2", "100.0")]
    [InlineData(".5", "0.5")]
    [InlineData("5.", "5.0")]
    [InlineData("0x.1", "0.0625")]
    [InlineData("0x1p-1", "0.5")]
    [InlineData("-0x1.8p1", "-3.0")]
    [InlineData("1E+2", "100.0")]
    [InlineData("1e-2", "0.01")]
    public void String_to_number_accepts_the_manual_syntax(string text, string expected)
    {
        Assert.True(LuaNumber.TryParse(text, out LuaValue v));
        Assert.Equal(expected, v.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1 2")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("0x")]
    [InlineData("0xg")]
    [InlineData("inf")]
    [InlineData("nan")]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData("1_000")]
    [InlineData("1,5")]
    [InlineData("0x1p")]
    [InlineData("1..2")]
    [InlineData("--1")]
    [InlineData("10f")]
    public void String_to_number_rejects_malformed_text(string text) =>
        Assert.False(LuaNumber.TryParse(text, out _));

    [Theory]
    [InlineData(3.0, true, 3L)]
    [InlineData(-0.0, true, 0L)]
    [InlineData(3.5, false, 0L)]
    [InlineData(9223372036854775808.0, false, 0L)]
    [InlineData(-9223372036854775808.0, true, long.MinValue)]
    [InlineData(double.NaN, false, 0L)]
    [InlineData(double.PositiveInfinity, false, 0L)]
    public void Float_converts_to_integer_only_when_exact(double d, bool ok, long expected)
    {
        Assert.Equal(ok, LuaNumber.FloatToInteger(d, out long n));
        Assert.Equal(expected, n);
    }

    [Fact]
    public void Bitwise_on_a_float_without_integer_representation_fails()
    {
        var ex = Assert.Throws<LuaException>(() => Run("print(1.5 & 1)"));
        Assert.Contains("number has no integer representation", ex.Message);
    }

    [Fact]
    public void Bitwise_on_a_non_number_fails()
    {
        var ex = Assert.Throws<LuaException>(() => Run("print(nil | 1)"));
        Assert.Contains("attempt to perform bitwise operation on a nil value", ex.Message);
    }
}
