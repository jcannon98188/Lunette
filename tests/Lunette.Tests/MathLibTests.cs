using Lunette.Lib;
using Xunit;

namespace Lunette.Tests;

public class MathLibTests
{
    private static readonly LuaState State = new();

    private static LuaValue I(long n) => LuaValue.FromInteger(n);
    private static LuaValue F(double d) => LuaValue.FromFloat(d);

    private static string Call(MathLib lib, string name, params LuaValue[] args)
    {
        var fn = (BuiltinFunction)lib.Members[name].Ref!;
        LuaValue[]? res = fn.Invoke(State, args);
        return string.Join("\t", (res ?? []).Select(v => v.ToString()));
    }

    private static string Call(string name, params LuaValue[] args) => Call(new MathLib(), name, args);

    [Fact]
    public void Constants()
    {
        var m = new MathLib().Members;
        Assert.Equal("3.1415926535898", m["pi"].ToString());
        Assert.Equal("inf", m["huge"].ToString());
        Assert.Equal("9223372036854775807", m["maxinteger"].ToString());
        Assert.Equal("-9223372036854775808", m["mininteger"].ToString());
    }

    [Theory]
    [InlineData("abs", -3L, "3")]
    [InlineData("abs", long.MinValue, "-9223372036854775808")]
    public void Abs_of_integers_wraps_and_stays_integer(string fn, long arg, string expected) =>
        Assert.Equal(expected, Call(fn, I(arg)));

    [Fact]
    public void Abs_of_float_stays_float() => Assert.Equal("2.5", Call("abs", F(-2.5)));

    [Fact]
    public void Floor_and_ceil_return_integers_when_they_fit_else_floats()
    {
        Assert.Equal("3", Call("floor", F(3.7)));
        Assert.Equal("-4", Call("floor", F(-3.2)));
        Assert.Equal("4", Call("ceil", F(3.2)));
        Assert.Equal("-3", Call("ceil", F(-3.7)));
        Assert.Equal("5", Call("floor", I(5)));
        Assert.Equal("1.1805916207174e+21", Call("floor", F(Math.Pow(2, 70))));
        Assert.Equal("inf", Call("ceil", F(double.PositiveInfinity)));
        Assert.Equal("-9223372036854775808", Call("floor", F(-9223372036854775808.0)));
    }

    [Fact]
    public void Fmod_truncates_toward_zero()
    {
        Assert.Equal("-1", Call("fmod", I(-7), I(3)));
        Assert.Equal("1", Call("fmod", I(7), I(-3)));
        Assert.Equal("0", Call("fmod", I(long.MinValue), I(-1)));
        Assert.Equal("-1.0", Call("fmod", F(-7), I(3)));
        Assert.Equal("1.5", Call("fmod", F(7.5), I(3)));
        var ex = Assert.Throws<LuaException>(() => Call("fmod", I(1), I(0)));
        Assert.Equal("bad argument #2 to 'fmod' (zero)", ex.Message);
    }

    [Fact]
    public void Modf_splits_integral_and_fractional_parts()
    {
        Assert.Equal("3.0\t0.5", Call("modf", F(3.5)));
        Assert.Equal("-3.0\t-0.5", Call("modf", F(-3.5)));
        Assert.Equal("inf\t0.0", Call("modf", F(double.PositiveInfinity)));
        Assert.Equal("5\t0.0", Call("modf", I(5)));
    }

    [Fact]
    public void Float_functions_always_return_floats()
    {
        Assert.Equal("4.0", Call("sqrt", I(16)));
        Assert.Equal("1.0", Call("exp", I(0)));
        Assert.Equal("0.0", Call("sin", I(0)));
        Assert.Equal("1.0", Call("cos", I(0)));
        Assert.Equal("0.0", Call("asin", I(0)));
        Assert.Equal("0.78539816339745", Call("atan", I(1)));
        Assert.Equal("2.3561944901923", Call("atan", I(1), I(-1)));
        Assert.Equal("3.0", Call("log", I(8), I(2)));
        Assert.Equal("2.0", Call("log", I(100), I(10)));
        Assert.Equal("1.0", Call("log", F(Math.E)));
        Assert.Equal("4.0", Call("log", I(81), I(3)));
    }

    [Fact]
    public void Tointeger_converts_only_exact_values()
    {
        Assert.Equal("3", Call("tointeger", F(3.0)));
        Assert.Equal("nil", Call("tointeger", F(3.5)));
        Assert.Equal("7", Call("tointeger", I(7)));
        Assert.Equal("nil", Call("tointeger", LuaValue.Nil));
        Assert.Equal("nil", Call("tointeger", F(9223372036854775808.0)));
    }

    [Fact]
    public void Ult_compares_as_unsigned()
    {
        Assert.Equal("true", Call("ult", I(1), I(-1)));
        Assert.Equal("false", Call("ult", I(-1), I(1)));
    }

    [Fact]
    public void Max_and_min_keep_the_original_subtype_and_compare_exactly()
    {
        Assert.Equal("3", Call("max", I(1), I(3), I(2)));
        Assert.Equal("3.5", Call("max", I(1), F(3.5), I(2)));
        Assert.Equal("1", Call("min", I(1), F(3.5), I(2)));
        Assert.Equal("9007199254740993", Call("max", I(9007199254740993), F(9007199254740992.0)));
        var ex = Assert.Throws<LuaException>(() => Call("max"));
        Assert.Equal("bad argument #1 to 'max' (value expected)", ex.Message);
    }

    [Fact]
    public void Argument_errors_name_the_function_and_type()
    {
        var ex = Assert.Throws<LuaException>(() => Call("floor", LuaValue.Nil));
        Assert.Equal("bad argument #1 to 'floor' (number expected, got nil)", ex.Message);
        ex = Assert.Throws<LuaException>(() => Call("ult", F(1.5), I(1)));
        Assert.Equal("bad argument #1 to 'ult' (number has no integer representation)", ex.Message);
    }

    [Fact]
    public void Random_is_deterministic_for_a_seed_and_respects_ranges()
    {
        var a = new MathLib();
        var b = new MathLib();
        Assert.Equal("42\t0", Call(a, "randomseed", I(42)));
        Call(b, "randomseed", I(42));
        for (int i = 0; i < 200; i++)
        {
            string x = Call(a, "random", I(3), I(9));
            Assert.Equal(x, Call(b, "random", I(3), I(9)));
            long n = long.Parse(x);
            Assert.InRange(n, 3, 9);
        }
        for (int i = 0; i < 200; i++)
        {
            double d = double.Parse(Call(a, "random"), System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(d, 0.0, 0.999999999999999);
        }
        Assert.Equal("1", Call(a, "random", I(1)));
        Assert.Equal("5", Call(a, "random", I(5), I(5)));
        long any = long.Parse(Call(a, "random", I(0)));
        _ = any;
        var ex = Assert.Throws<LuaException>(() => Call(a, "random", I(3), I(2)));
        Assert.Equal("bad argument #1 to 'random' (interval is empty)", ex.Message);
        ex = Assert.Throws<LuaException>(() => Call(a, "random", I(1), I(2), I(3)));
        Assert.Equal("wrong number of arguments", ex.Message);
    }

    [Fact]
    public void Random_covers_the_full_integer_range_without_bias_errors()
    {
        var m = new MathLib();
        Call(m, "randomseed", I(7));
        for (int i = 0; i < 100; i++)
        {
            long n = long.Parse(Call(m, "random", I(long.MinValue), I(long.MaxValue)));
            _ = n;
        }
    }
}
