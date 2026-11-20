using Xunit;

namespace Lunette.Tests;

public class ControlFlowTests
{
    internal static string Run(string source)
    {
        var output = new StringWriter { NewLine = "\n" };
        var state = new LuaState { Output = output };
        state.DoString(source);
        return output.ToString();
    }

    [Theory]
    [InlineData("print(1 < 2, 2 < 1, 1 <= 1, 2 >= 3, 3 > 2)", "true\tfalse\ttrue\tfalse\ttrue\n")]
    [InlineData("print(1 == 1.0, 1 ~= 2, nil == false, nil == nil, true == true)", "true\ttrue\tfalse\ttrue\ttrue\n")]
    [InlineData("print(not nil, not 0, not false)", "true\tfalse\ttrue\n")]
    [InlineData("print(1 and 2, nil and 2, false or 3, nil or false, 1 or undefinedfn())", "2\tnil\t3\tfalse\t1\n")]
    [InlineData("print(1 < 2 and 2 < 3, 1 + 1 == 2)", "true\ttrue\n")]
    public void Comparison_and_logical_operators(string source, string expected) =>
        Assert.Equal(expected, Run(source));

    [Fact]
    public void If_elseif_else_picks_one_branch() =>
        Assert.Equal("20\n", Run("local x = 2 if x == 1 then print(10) elseif x == 2 then print(20) else print(30) end"));

    [Fact]
    public void If_selects_branches_by_truthiness() =>
        Assert.Equal("1\n3\n4\n", Run(
            "if 0 then print(1) end if nil then print(2) else print(3) end if false then print(0) elseif true then print(4) end"));

    [Fact]
    public void While_loop_runs_until_condition_fails() =>
        Assert.Equal("55\n", Run("local s, i = 0, 1 while i <= 10 do s = s + i i = i + 1 end print(s)"));

    [Fact]
    public void Repeat_until_sees_body_locals_and_runs_at_least_once() =>
        Assert.Equal("3\n", Run("local n = 0 repeat local done = n >= 2 n = n + 1 until done print(n)"));

    [Fact]
    public void Numeric_for_counts_up_down_and_by_float_steps() =>
        Assert.Equal("6\n6\n1.0\n1.5\n2.0\n", Run("""
            local s = 0 for i = 1, 3 do s = s + i end print(s)
            s = 0 for i = 3, 1, -1 do s = s + i end print(s)
            for i = 1, 2, 0.5 do print(i) end
            """));

    [Fact]
    public void Numeric_for_with_empty_range_does_not_run() =>
        Assert.Equal("7\n", Run("for i = 5, 1 do print(i) end print(7)"));

    [Fact]
    public void Numeric_for_with_zero_step_is_an_error()
    {
        var ex = Assert.Throws<LuaException>(() => Run("for i = 1, 2, 0 do end"));
        Assert.Contains("'for' step is zero", ex.Message);
    }

    [Fact]
    public void Numeric_for_does_not_overflow_at_the_integer_limit() =>
        Assert.Equal("2\n", Run("local n = 0 for i = 9223372036854775806, 9223372036854775807 do n = n + 1 end print(n)"));

    [Fact]
    public void Break_leaves_the_innermost_loop() =>
        Assert.Equal("1\n2\n3\n6\n", Run("""
            for i = 1, 3 do
              for j = 1, 10 do if j > 1 then break end end
              print(i)
            end
            local k = 0 while true do k = k + 1 if k > 5 then break end end
            print(k)
            """));

    [Fact]
    public void Compare_of_incompatible_types_is_an_error()
    {
        var ex = Assert.Throws<LuaException>(() => Run("print(1 < nil)"));
        Assert.Contains("attempt to compare number with nil", ex.Message);
    }
}
