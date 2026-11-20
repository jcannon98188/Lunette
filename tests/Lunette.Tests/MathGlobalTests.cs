using Xunit;

namespace Lunette.Tests;

/// <summary>The <c>math</c> global as seen from Lua code, including string coercion (manual 3.4.3).</summary>
public class MathGlobalTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Theory]
    [InlineData("print(math.floor(3.7), math.pi > 3, math.maxinteger)", "3\ttrue\t9223372036854775807")]
    [InlineData("print(math.type(1), math.type(1.0), math.type('1'), math.type(nil))", "integer\tfloat\tnil\tnil")]
    [InlineData("print(math.sqrt('16'), math.max('3', 2), math.abs('-4'))", "4.0\t3\t4")]
    [InlineData("print(math.tointeger('8'), math.tointeger(8.5), math.tointeger('x'))", "8\tnil\tnil")]
    [InlineData("print(math.floor('3.5'), math.fmod('7', '3'), math.ult('1', 2))", "3\t1\ttrue")]
    [InlineData("print('10' + 5, '0x10' * 2, '3' // 2, 10 .. '', -'2', '2' ^ 2)", "15\t32\t1\t10\t-2\t4.0")]
    [InlineData("print(3.0 | 0, '10' + 1 == 11, math.type('10' + 1), math.type('10' + 1.0))", "3\ttrue\tinteger\tfloat")]
    [InlineData("print(select('#', 1, nil, nil), select(2, 'a', 'b', 'c'), select(-1, 'a', 'b', 'c'))", "3\tb\tc")]
    public void Lua_level_behavior(string src, string expected) => Assert.Equal(expected + "\n", Run(src));

    [Theory]
    [InlineData("math.floor('x')", "bad argument #1 to 'floor' (number expected, got string)")]
    [InlineData("return 1.5 | 0", "number has no integer representation")]
    [InlineData("return 'a' | 0", "attempt to perform bitwise operation on a string value")]
    // 5.4 core does not coerce strings for bitwise ops (the suite's bwcoercion.lua adds it back via metamethods).
    [InlineData("return '3' | 0", "attempt to perform bitwise operation on a string value")]
    [InlineData("return 'a' + 1", "attempt to perform arithmetic on a string value")]
    [InlineData("select(0)", "bad argument #1 to 'select' (index out of range)")]
    public void Errors(string src, string fragment) =>
        Assert.Contains(fragment, Assert.Throws<LuaException>(() => Run(src)).Message);

    [Fact]
    public void Random_with_fixed_seed_is_reproducible()
    {
        // No reference 5.4.9 binary was available to pin literal values; the algorithm is PUC's xoshiro256**.
        Assert.Equal("true\n", Run("""
            math.randomseed(42) local a, b = math.random(1, 1000), math.random()
            math.randomseed(42) print(a == math.random(1, 1000) and b == math.random())
            """));
    }
}
