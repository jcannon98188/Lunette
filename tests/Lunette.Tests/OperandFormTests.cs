using Lunette.Compiler;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Constant and register operand forms (AddK.., fused compare-and-jump JLt.., locals read in place, stores written straight into
/// the local). The seams are the compiled Proto (which opcodes appear) and running the chunk (what it prints): every behavior test
/// uses locals so the new forms are the ones that run, and expected values are the Lua 5.4 results.
/// </summary>
public class OperandFormTests
{
    private static Proto Compile(string source) => LuaCompiler.Compile(source, "=test");

    private static OpCode[] Ops(string source) => Compile(source).Code.Select(i => i.Op).ToArray();

    private static string Run(string source) => ControlFlowTests.Run(source);

    // ---- what the compiler emits ----

    [Fact]
    public void Locals_are_added_in_place_without_moves()
    {
        var ops = Ops("local a, b = 1, 2 return a + b");
        Assert.Contains(OpCode.Add, ops);
        Assert.DoesNotContain(OpCode.Move, ops);
    }

    [Fact]
    public void A_number_literal_on_the_right_becomes_a_constant_operand()
    {
        var ops = Ops("local x = 1 return x + 1");
        Assert.Contains(OpCode.AddK, ops);
        Assert.DoesNotContain(OpCode.Add, ops);
    }

    [Fact]
    public void Assigning_arithmetic_to_a_local_writes_it_directly()
    {
        Assert.Equal([OpCode.LoadK, OpCode.AddK, OpCode.Return], Ops("local x = 1 x = x + 1"));
    }

    [Fact]
    public void Negative_literal_operand_is_one_constant()
    {
        var p = Compile("local x = 1 return x * -2");
        Assert.DoesNotContain(OpCode.Unm, p.Code.Select(i => i.Op));
        Assert.Contains(p.Constants, c => c.IsInteger && c.AsInteger == -2);
    }

    [Fact]
    public void Equal_number_literals_share_a_constant()
    {
        var p = Compile("local x = 1 return x + 7, x - 7, x * 7");
        Assert.Single(p.Constants, c => c.IsInteger && c.AsInteger == 7);
    }

    [Fact]
    public void A_loop_condition_is_a_fused_compare_and_jump()
    {
        var ops = Ops("local x = 1 while x < 10 do x = x + 1 end");
        Assert.Contains(OpCode.JLtK, ops);
        Assert.DoesNotContain(OpCode.Lt, ops);
        Assert.DoesNotContain(OpCode.Test, ops);
    }

    [Fact]
    public void Register_comparisons_in_conditions_fuse_too()
    {
        Assert.Contains(OpCode.JEq, Ops("local x, y = 1, 2 if x == y then end"));
        Assert.Contains(OpCode.JLe, Ops("local x, y = 1, 2 if x >= y then end"));
    }

    [Fact]
    public void A_comparison_used_as_a_value_stays_a_value_op()
    {
        var ops = Ops("local x = 1 local b = x < 2 return b");
        Assert.Contains(OpCode.Lt, ops);
        Assert.DoesNotContain(OpCode.JLtK, ops);
    }

    // ---- behavior with the new forms ----

    [Fact]
    public void Integer_arithmetic_wraps_and_float_arithmetic_does_not()
    {
        Assert.Equal("true\t-2\ttrue\n", Run(
            "local a = math.maxinteger local b = a + 1 local c = a * 2 print(b == math.mininteger, c, 0.1 + 0.2 == 0.30000000000000004)"));
    }

    [Fact]
    public void Division_and_mixed_operands_follow_the_number_tower()
    {
        Assert.Equal("3.5\t3\t1\t1.5\t1.5\t4.5\n", Run(
            "local a, i = 7, 3 print(a / 2, a // 2, a % 2, i * 0.5, 0.5 * i, i + 1.5)"));
    }

    [Fact]
    public void Nan_fails_every_ordered_and_equal_condition()
    {
        Assert.Equal("none\tne\n", Run(
            "local n = 0/0 local r = 'none' " +
            "if n < 1 then r = 'lt' end if n <= 1 then r = 'le' end if n > 1 then r = 'gt' end if n >= 1 then r = 'ge' end if n == 1 then r = 'eq' end " +
            "local m = 'none' if n ~= 1 then m = 'ne' end print(r, m)"));
        Assert.Equal("none\tne\n", Run(
            "local n, o = 0/0, 1 local r = 'none' " +
            "if n < o then r = 'lt' end if n <= o then r = 'le' end if n > o then r = 'gt' end if n >= o then r = 'ge' end if n == n then r = 'eq' end " +
            "local m = 'none' if n ~= n then m = 'ne' end print(r, m)"));
    }

    [Fact]
    public void Negative_zero_equals_zero_but_keeps_its_sign()
    {
        Assert.Equal("eq\t-inf\t-0.0\n", Run(
            "local z = -0.0 local r = 'ne' if z == 0 then r = 'eq' end local x = 0.0 print(r, 1 / z, x * -1)"));
    }

    [Fact]
    public void Mixed_integer_and_float_constants_compare_exactly()
    {
        Assert.Equal("lt\tfalse\ttrue\n", Run(
            "local m = math.maxinteger local r = 'ge' if m < 2^63 then r = 'lt' end local e = false if m == 2^63 then e = true end local x = 1 print(r, e, x == 1.0)"));
    }

    [Fact]
    public void Equality_against_string_boolean_and_nil_constants()
    {
        Assert.Equal("1\t2\t3\t4\n", Run(
            "local s, b, n, t = 'a', true, nil, {} " +
            "local out = {} if s == 'a' then out[#out + 1] = 1 end if b == true then out[#out + 1] = 2 end if n == nil then out[#out + 1] = 3 end if t ~= 'a' then out[#out + 1] = 4 end " +
            "print(table.concat(out, '\\t'))"));
    }

    [Fact]
    public void Constant_operands_coerce_strings_like_register_operands()
    {
        Assert.Equal("11\t20\t9\t2.5\t3\t1\t100.0\n", Run(
            "local s = '10' print(s + 1, s * 2, s - 1, s / 4, s // 3, s % 3, s ^ 2)"));
    }

    [Fact]
    public void Arithmetic_errors_name_the_variable_and_not_the_constant()
    {
        string msg = Run("local function f(t) return t + 1 end print(select(2, pcall(f, {})))");
        Assert.Contains("attempt to perform arithmetic on a table value (local 't')", msg);
        msg = Run("local function f(s) return s + 1 end print(select(2, pcall(f, 'x')))");
        Assert.Contains("attempt to", msg);
        Assert.Contains("string", msg);
    }

    [Fact]
    public void Compare_errors_keep_the_operand_order_of_the_source()
    {
        Assert.Contains("attempt to compare table with number", Run("local a = {} print(select(2, pcall(function() if a < 1 then end end)))"));
        Assert.Contains("attempt to compare number with table", Run("local a = {} print(select(2, pcall(function() if a > 1 then end end)))"));
        Assert.Contains("attempt to compare number with table", Run("local a = {} local function f(b) if 1 < b then end end print(select(2, pcall(f, a)))"));
        Assert.Contains("attempt to compare nil with number", Run("local function f(b) if b <= 2 then end end print(select(2, pcall(f)))"));
    }

    [Fact]
    public void Order_and_equality_metamethods_drive_fused_conditions()
    {
        Assert.Equal("lt\tgt\tle\tge\teq\n", Run(
            "local mt = {__lt = function(a, b) return a.v < b.v end, __le = function(a, b) return a.v <= b.v end, __eq = function(a, b) return a.v == b.v end} " +
            "local a, b, c = setmetatable({v = 1}, mt), setmetatable({v = 2}, mt), setmetatable({v = 1}, mt) " +
            "local out = {} if a < b then out[#out + 1] = 'lt' end if b > a then out[#out + 1] = 'gt' end if a <= c then out[#out + 1] = 'le' end " +
            "if b >= a then out[#out + 1] = 'ge' end if a == c then out[#out + 1] = 'eq' end print(table.concat(out, '\\t'))"));
    }

    [Fact]
    public void A_falsy_metamethod_result_skips_the_branch()
    {
        Assert.Equal("no\tyes\n", Run(
            "local mt = {__lt = function() return false end} local a, b = setmetatable({}, mt), setmetatable({}, mt) " +
            "local x, y = 'yes', 'yes' if a < b then x = 'no' else x = 'no' end if not (a < b) then y = 'yes' end print(x, y)"));
    }

    [Fact]
    public void A_yield_inside_a_compare_metamethod_resumes_into_the_jump()
    {
        Assert.Equal("y\tyes\ty\tno\n", Run(
            "local yes = {__lt = function() coroutine.yield('y') return true end} " +
            "local no = {__lt = function() coroutine.yield('y') return false end} " +
            "local a, b = setmetatable({}, yes), setmetatable({}, yes) local c, d = setmetatable({}, no), setmetatable({}, no) " +
            "local co1 = coroutine.wrap(function() if a < b then return 'yes' else return 'no' end end) " +
            "local co2 = coroutine.wrap(function() if c < d then return 'yes' else return 'no' end end) " +
            "print(co1(), co1(), co2(), co2())"));
    }

    [Fact]
    public void For_loops_stop_at_the_integer_limits_without_wrapping()
    {
        Assert.Equal("2\t2\t3\n", Run(
            "local n, m, k = 0, 0, 0 " +
            "for i = math.maxinteger - 1, math.maxinteger do n = n + 1 end " +
            "for i = math.mininteger + 1, math.mininteger, -1 do m = m + 1 end " +
            "for i = 1, 3 do k = k + 1 end print(n, m, k)"));
    }

    [Fact]
    public void For_loops_with_float_parts_count_correctly()
    {
        Assert.Equal("4\t3\t0\n", Run(
            "local n, m, z = 0, 0, 0 for i = 1, 2.5, 0.5 do n = n + 1 end for i = 1.0, 3 do m = m + 1 end for i = 3, 1 do z = z + 1 end print(n, m, z)"));
    }

    [Fact]
    public void A_loop_variable_changed_in_the_body_does_not_change_the_iteration()
    {
        Assert.Equal("1,2,3\n", Run(
            "local out = {} for i = 1, 3 do out[#out + 1] = i i = 'x' end print(table.concat(out, ','))"));
    }

    [Fact]
    public void Repeat_until_fused_condition_sees_body_locals_and_closes_them()
    {
        Assert.Equal("0\t1\t2\n", Run(
            "local fs, i = {}, 0 repeat local j = i fs[#fs + 1] = function() return j end i = i + 1 until j >= 2 " +
            "print(fs[1](), fs[2](), fs[3]())"));
    }

    [Fact]
    public void Multiple_assignment_indexes_with_the_values_from_before_the_assignment()
    {
        Assert.Equal("2\t20\tnil\n", Run("local a, i = {}, 1 i, a[i] = i + 1, 20 print(i, a[1], a[2])"));
        Assert.Equal("2\t1\n", Run("local t, i, j = {1, 2}, 1, 2 t[i], t[j] = t[j], t[i] print(t[1], t[2])"));
        Assert.Equal("nil\t5\n", Run("local t, k = {}, 'x' t, k = nil, t print(t, 5)"));
    }

    [Fact]
    public void Assignments_to_a_local_from_every_expression_kind()
    {
        Assert.Equal("-2a\ttrue\ttrue\t7\tnil\ts\n", Run(
            "local x = 1 x = x + 1 x = -x x = x .. 'a' " +
            "local y = 5 y = y > 3 " +
            "local z z = not z " +
            "local w w = w or 7 " +
            "local v = 1 v = nil and v " +
            "local c = 3 c = c < 5 and 's' or 'l' " +
            "print(x, y, z, w, v, c)"));
    }

    [Fact]
    public void Index_and_method_calls_on_locals_work_in_place()
    {
        Assert.Equal("3\t4\tfoo\t10\n", Run(
            "local t = {a = 3, [2] = 4, o = {n = 'foo'}} local k = 2 local obj = {v = 10, get = function(self) return self.v end} " +
            "print(t.a, t[k], t.o.n, obj:get())"));
    }
}
