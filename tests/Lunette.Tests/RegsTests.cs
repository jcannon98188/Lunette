using Lunette.Unsafe;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Behaviour of the Lunette.Unsafe register helpers. These run against whichever implementation is compiled in: the
/// unsafe one by default, the bounds-checked safe twin (src/Lunette/Unsafe/RegsSafe.cs) under LUA_SAFE_ACCESS.
/// </summary>
public class RegsTests
{
    [Fact]
    public void SetInteger_stores_an_integer_over_any_previous_value()
    {
        var r = new LuaValue[3];
        r[0] = LuaValue.FromFloat(1.5);
        r[1] = LuaValue.FromInteger(7);
        Regs.SetInteger(ref Regs.At(r, 0), 42);
        Regs.SetInteger(ref Regs.At(r, 1), -9);
        Regs.SetInteger(ref Regs.At(r, 2), long.MinValue);
        Assert.True(r[0].IsInteger); Assert.Equal(42, r[0].Bits);
        Assert.True(r[1].IsInteger); Assert.Equal(-9, r[1].Bits);
        Assert.True(r[2].IsInteger); Assert.Equal(long.MinValue, r[2].Bits);
    }

    [Fact]
    public void SetFloat_stores_a_float_over_any_previous_value()
    {
        var r = new LuaValue[2];
        r[0] = LuaValue.FromInteger(3);
        Regs.SetFloat(ref Regs.At(r, 0), double.NaN);
        Regs.SetFloat(ref Regs.At(r, 1), -0.0);
        Assert.True(r[0].IsFloat); Assert.Equal(BitConverter.DoubleToInt64Bits(double.NaN), r[0].Bits);
        Assert.True(r[1].IsFloat); Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), r[1].Bits);
    }

    [Fact]
    public void SetBits_overwrites_the_payload_and_keeps_the_number_type()
    {
        var r = new[] { LuaValue.FromInteger(1), LuaValue.FromFloat(1.0) };
        Regs.SetBits(ref Regs.At(r, 0), 99);
        Regs.SetBits(ref Regs.At(r, 1), BitConverter.DoubleToInt64Bits(2.5));
        Assert.True(r[0].IsInteger); Assert.Equal(99, r[0].Bits);
        Assert.True(r[1].IsFloat); Assert.Equal(2.5, BitConverter.Int64BitsToDouble(r[1].Bits));
    }

    [Fact]
    public void Copy_copies_numbers_and_references()
    {
        var table = LuaValue.FromTable(new LuaTable());
        var r = new[] { LuaValue.FromInteger(1), LuaValue.FromInteger(2), LuaValue.FromFloat(2.0), table, default };
        Regs.Copy(ref Regs.At(r, 0), in Regs.At(r, 1)); // same tag: payload only
        Assert.True(r[0].IsInteger); Assert.Equal(2, r[0].Bits);
        Regs.Copy(ref Regs.At(r, 0), in Regs.At(r, 2)); // tag changes
        Assert.True(r[0].IsFloat); Assert.Equal(r[2].Bits, r[0].Bits);
        Regs.Copy(ref Regs.At(r, 0), in Regs.At(r, 3));
        Assert.Same(table.Ref, r[0].Ref);
        Regs.Copy(ref Regs.At(r, 0), in Regs.At(r, 4));
        Assert.True(r[0].IsNil);
    }

    [Fact]
    public void IsFalsy_is_true_only_for_nil_and_false()
    {
        var r = new[] { default, LuaValue.FromBoolean(false), LuaValue.FromBoolean(true), LuaValue.FromInteger(0), LuaValue.FromFloat(0) };
        bool[] expected = [true, true, false, false, false];
        for (int i = 0; i < r.Length; i++) Assert.Equal(expected[i], Regs.IsFalsy(ref Regs.At(r, i)));
    }

    [Fact]
    public void Reg_addresses_the_frame_register_through_the_base_reference()
    {
        var co = new Lunette.Vm.Coroutine { Stack = new LuaValue[8] };
        var f = new Lunette.Vm.Frame { Base = 3 };
        ref LuaValue rb = ref Regs.At(co.Stack, f.Base);
        Regs.SetInteger(ref Regs.Reg(co, f, ref rb, 2), 41);
        Assert.True(co.Stack[5].IsInteger);
        Assert.Equal(41, co.Stack[5].Bits);
        Assert.True(co.Stack[3].IsNil);
        Assert.Equal(41, Regs.Reg(co, f, ref rb, 2).Bits);
    }

#if LUA_SAFE_ACCESS
    [Fact]
    public void Safe_twin_Reg_checks_bounds()
    {
        var co = new Lunette.Vm.Coroutine { Stack = new LuaValue[4] };
        var f = new Lunette.Vm.Frame { Base = 2 };
        Assert.Throws<IndexOutOfRangeException>(() => Regs.Reg(co, f, ref Regs.At(co.Stack, 2), 2));
    }

    [Fact]
    public void Safe_twin_checks_bounds()
    {
        var r = new LuaValue[2];
        Assert.Throws<IndexOutOfRangeException>(() => Regs.At(r, 2));
        Assert.Throws<IndexOutOfRangeException>(() => Regs.At(r, -1));
    }
#endif
}
