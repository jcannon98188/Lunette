using Xunit;

namespace Lunette.Tests;

public class FunctionTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Functions_take_arguments_and_return_values() =>
        Assert.Equal("5\n", Run("local function add(a, b) return a + b end print(add(2, 3))"));

    [Fact]
    public void Missing_arguments_are_nil_and_extra_arguments_are_dropped() =>
        Assert.Equal("1\tnil\n1\t2\n", Run("""
            local function f(a, b) return a, b end
            print(f(1))
            print(f(1, 2, 3))
            """));

    [Fact]
    public void Global_function_statements_define_globals() =>
        Assert.Equal("9\n", Run("function sq(x) return x * x end print(sq(3))"));

    [Fact]
    public void Function_without_return_yields_no_values() =>
        Assert.Equal("\nnil\n", Run("local function f() end print(f()) local x = f() print(x)"));

    [Fact]
    public void Multiple_returns_expand_at_the_end_of_lists_and_truncate_elsewhere() =>
        Assert.Equal("1\t2\t3\n1\t10\n1\t4\n", Run("""
            local function f() return 1, 2, 3 end
            print(f())
            print((f()), 10)
            print(f(), 4)
            """));

    [Fact]
    public void Local_and_assignment_adjust_multiple_values() =>
        Assert.Equal("1\t2\tnil\n3\t4\n", Run("""
            local function f() return 1, 2 end
            local a, b, c = f()
            print(a, b, c)
            a, b = 3, 4
            print(a, b)
            """));

    [Fact]
    public void Recursion_works() =>
        Assert.Equal("3628800\n", Run("local function fact(n) if n <= 1 then return 1 end return n * fact(n - 1) end print(fact(10))"));

    [Fact]
    public void Deep_non_tail_recursion_does_not_overflow_the_dotnet_stack() =>
        Assert.Equal("100000\n", Run("local function depth(n) if n == 0 then return 0 end return 1 + depth(n - 1) end print(depth(100000))"));

    [Fact]
    public void Infinite_recursion_raises_a_stack_overflow_error()
    {
        var ex = Assert.Throws<LuaException>(() => Run("local function f() return 1 + f() end f()"));
        Assert.Contains("stack overflow", ex.Message);
    }

    [Fact]
    public void Tail_calls_run_in_constant_stack_space() =>
        Assert.Equal("7\n", Run("local function loop(n) if n == 0 then return 7 end return loop(n - 1) end print(loop(2000000))"));

    [Fact]
    public void Calling_a_non_function_is_an_error()
    {
        var ex = Assert.Throws<LuaException>(() => Run("local x = 1 x()"));
        Assert.Contains("attempt to call a number value", ex.Message);
    }

    [Fact]
    public void Closures_capture_variables_not_values() =>
        Assert.Equal("1\n2\n3\n", Run("""
            local function counter()
              local n = 0
              return function() n = n + 1 return n end
            end
            local c = counter()
            print(c()) print(c()) print(c())
            """));

    [Fact]
    public void Closures_from_one_scope_share_an_upvalue() =>
        Assert.Equal("1\n2\n", Run("""
            local function make()
              local n = 0
              local function inc() n = n + 1 end
              local function get() return n end
              return inc, get
            end
            local inc, get = make()
            inc() print(get()) inc() print(get())
            """));

    [Fact]
    public void Open_upvalues_see_later_writes_to_the_enclosing_local() =>
        Assert.Equal("5\n", Run("local x = 1 local function f() return x end x = 5 print(f())"));

    [Fact]
    public void Each_loop_iteration_gets_a_fresh_local() =>
        Assert.Equal("1\n2\n3\n", Run("""
            local f1, f2, f3
            for i = 1, 3 do
              local v = i
              local function g() return v end
              if i == 1 then f1 = g elseif i == 2 then f2 = g else f3 = g end
            end
            print(f1()) print(f2()) print(f3())
            """));

    [Fact]
    public void For_loop_control_variable_is_fresh_per_iteration() =>
        Assert.Equal("1\n2\n", Run("""
            local f1, f2
            for i = 1, 2 do
              if i == 1 then f1 = function() return i end else f2 = function() return i end end
            end
            print(f1()) print(f2())
            """));

    [Fact]
    public void Nested_closures_capture_through_intermediate_functions() =>
        Assert.Equal("6\n", Run("""
            local a = 1
            local function outer()
              local b = 2
              return function() local c = 3 return function() return a + b + c end end
            end
            print(outer()()())
            """));

    [Fact]
    public void Break_closes_upvalues_of_the_loop_body() =>
        Assert.Equal("7\n", Run("""
            local f
            while true do local v = 7 f = function() return v end break end
            print(f())
            """));

    [Fact]
    public void Varargs_are_forwarded_counted_and_truncated() =>
        Assert.Equal("1\t2\t3\n1\n2\t3\nnil\n", Run("""
            local function f(...) return ... end
            print(f(1, 2, 3))
            print((f(1, 2, 3)))
            local function g(a, ...) return ... end
            print(g(1, 2, 3))
            local function h(...) local a = ... return a end
            print(h())
            """));

    [Fact]
    public void Vararg_in_the_middle_is_truncated_to_one_value() =>
        Assert.Equal("1\t9\n", Run("local function f(...) return ..., 9 end print(f(1, 2, 3))"));

    [Fact]
    public void Varargs_are_available_in_the_main_chunk_and_not_inside_nested_functions()
    {
        var ex = Assert.Throws<LuaException>(() => Run("local function f() return ... end"));
        Assert.Contains("cannot use '...' outside a vararg function", ex.Message);
    }

    [Fact]
    public void Calls_in_the_middle_of_arguments_keep_registers_straight() =>
        Assert.Equal("9\n", Run("""
            local function id(...) return ... end
            local function add3(a, b, c) return a + b + c end
            print(add3(id(1), id(2, 99), id(3, 4, 5)) + 3)
            """));

    [Fact]
    public void Call_results_feed_further_calls_and_expressions() =>
        Assert.Equal("6\n", Run("local function f(x) return function(y) return x + y end end print(f(1)(2) + f(1)(2))"));

    [Fact]
    public void Print_and_assert_are_available_as_builtins()
    {
        Assert.Equal("1\n", Run("assert(true) assert(1 == 1, 2) print(1)"));
        Assert.Throws<LuaException>(() => Run("assert(false)"));
    }
}
