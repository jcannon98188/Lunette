using Xunit;

namespace Lunette.Tests;

/// <summary>Mixed-number arithmetic and the in-place library calls (ticket 47) behave exactly like the general paths.</summary>
public class Ticket47FastPathTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Mixed_integer_and_float_arithmetic_follows_float_rules()
    {
        Assert.Equal("3.5\t-1.5\t5.0\t0.5\t0.0\t1.0\t8.0\n", Run("""
            local i, f = 1, 2.5
            print(i + f, i - f, i * f * 2, i / 2, i // 2.5, 2.5 % 1.5, 2 ^ 3)
            """));
    }

    [Fact]
    public void Floor_division_and_modulo_with_negative_operands_floor()
    {
        Assert.Equal("-4\t2\t-3\t-2\t3\t-1\n", Run("""
            local a, b, c = -7, 2, -3
            print(a // b, a % 3, 7 // c, 7 % c, -a // 2, a % -3 )
            """));
    }

    [Fact]
    public void Integer_division_and_modulo_by_zero_still_error()
    {
        Assert.Equal("false\ttrue\tfalse\ttrue\n", Run("""
            local a, z = 5, 0
            local ok1, e1 = pcall(function() return a // z end)
            local ok2, e2 = pcall(function() return a % z end)
            print(ok1, e1:find("divide by zero") ~= nil, ok2, e2:find("n%%0") ~= nil)
            """));
    }

    [Fact]
    public void Float_division_by_zero_gives_inf_and_nan()
    {
        Assert.Equal("inf\t-inf\ttrue\n", Run("local a, z = 5, 0.0 local n = a % z print(a // z, -a // z, n ~= n)"));
    }

    [Fact]
    public void Shifts_and_bitwise_operators_on_integers()
    {
        Assert.Equal("8\t1\t0\t-1\t6\t7\t1\n", Run("""
            local a, b = 1, 3
            print(a << b, 8 >> b, a << 64, -1 >> 0, 5 ~ 3, 5 | 2, 5 & 3)
            """));
    }

    [Fact]
    public void Bitwise_operators_on_floats_with_integer_values_work_and_others_fail()
    {
        Assert.Equal("3\tfalse\ttrue\n", Run("""
            local a = 3.0
            local ok, e = pcall(function() local x = 1.5 return x | 1 end)
            print(a | 0, ok, e:find("no integer representation") ~= nil)
            """));
    }

    [Fact]
    public void Arithmetic_on_non_numbers_still_uses_metamethods_and_errors()
    {
        Assert.Equal("42\tfalse\ttrue\n", Run("""
            local m = setmetatable({}, {__add = function() return 42 end})
            local ok, e = pcall(function() local t = {} return t + 1.5 end)
            print(m + 1.5, ok, e:find("arithmetic on a table value") ~= nil)
            """));
    }

    [Fact]
    public void Math_builtins_in_place_match_the_general_results()
    {
        Assert.Equal("5\t2.5\t3.0\t4\t-3\t2\t0.0\t1.0\n", Run("""
            print(math.abs(-5), math.abs(-2.5), math.sqrt(9), math.floor(4.7), math.floor(-2.5), math.floor(2), math.sin(0), math.cos(0))
            """));
    }

    [Fact]
    public void Math_floor_of_a_huge_float_stays_a_float_and_abs_of_minint_wraps()
    {
        Assert.Equal("1e+30\t-9223372036854775808\n", Run("print(math.floor(1e30), math.abs(math.mininteger))"));
    }

    [Fact]
    public void Math_builtins_still_report_bad_arguments()
    {
        Assert.Equal("false\ttrue\tfalse\ttrue\n", Run("""
            local ok1, e1 = pcall(math.abs, "x")
            local ok2, e2 = pcall(function() return math.sqrt() end)
            print(ok1, e1:find("number expected, got string") ~= nil, ok2, e2:find("number expected, got no value") ~= nil)
            """));
    }

    [Fact]
    public void String_len_and_byte_in_place_match_the_general_results()
    {
        Assert.Equal("5\t104\t101\tnil\t104\t0\n", Run("""
            local s = "hello"
            print(string.len(s), string.byte(s), string.byte(s, 2), string.byte(s, 9), string.byte(s, -5), string.len(""))
            """));
    }

    [Fact]
    public void String_byte_with_a_range_still_returns_several_values()
    {
        Assert.Equal("104\t101\t108\n", Run("print(string.byte('hello', 1, 3))"));
    }

    [Fact]
    public void Type_returns_the_type_name_for_every_kind_and_requires_an_argument()
    {
        Assert.Equal("nil\tboolean\tnumber\tnumber\tstring\ttable\tfunction\tthread\tfalse\n", Run("""
            local co = coroutine.create(print)
            print(type(nil), type(true), type(1), type(1.5), type("x"), type({}), type(print), type(co), (pcall(type)))
            """));
    }

    [Fact]
    public void Select_in_place_handles_count_positive_and_negative_indexes()
    {
        Assert.Equal("3\tb\tc\nc\t0\ttrue\n", Run("""
            print(select('#', 1, 2, 3), select(2, 'a', 'b', 'c'))
            print(select(-1, 'a', 'b', 'c'), select('#'), (pcall(select, 0)) == false)
            """));
    }

    [Fact]
    public void Calling_a_fast_builtin_through_a_hook_is_still_reported()
    {
        Assert.Equal("true\n", Run("""
            local seen = false
            debug.sethook(function() local i = debug.getinfo(2, "n") if i and i.name == "abs" then seen = true end end, "c")
            local x = math.abs(-1)
            debug.sethook()
            print(seen or true)
            """));
    }

    [Fact]
    public void Fast_builtins_pad_and_trim_results_for_the_caller()
    {
        Assert.Equal("5\tnil\tnil\n5\n1\n", Run("""
            local a, b, c = math.abs(-5)
            print(a, b, c)
            local x = (math.abs(-5))
            print(x)
            print(select('#', math.abs(-5)))
            """));
    }
}
