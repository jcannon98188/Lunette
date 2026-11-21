using System.Runtime.CompilerServices;
using Xunit;

namespace Lunette.Tests;

public class LuaValueTests
{
    [Fact]
    public void LuaValue_is_sixteen_bytes() =>
        Assert.Equal(16, System.Runtime.CompilerServices.Unsafe.SizeOf<LuaValue>());

#if !LUA_SAFE_ACCESS // RawValue exists only in the unsafe helpers; the safe twin has no raw view
    [Fact]
    public void RawValue_view_has_the_layout_of_LuaValue()
    {
        Assert.Equal(16, System.Runtime.CompilerServices.Unsafe.SizeOf<Lunette.Unsafe.RawValue>());
        var v = LuaValue.FromInteger(0x1234_5678_9ABCL);
        var raw = System.Runtime.CompilerServices.Unsafe.As<LuaValue, Lunette.Unsafe.RawValue>(ref v);
        Assert.Same(LuaValue.IntegerTag, raw.Ref);
        Assert.Equal(0x1234_5678_9ABCL, raw.Bits);
        var f = LuaValue.FromFloat(-0.5);
        raw = System.Runtime.CompilerServices.Unsafe.As<LuaValue, Lunette.Unsafe.RawValue>(ref f);
        Assert.Same(LuaValue.FloatTag, raw.Ref);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.5), raw.Bits);
    }
#endif

    /// <summary>The type tags are string literals so the runtime allocates them on the frozen heap, which lets the JIT treat them as
    /// constants and skip the write barrier on a tag store. Correctness never depends on it; this notices a runtime that un-freezes them.</summary>
    [Fact]
    public void Type_tags_are_frozen_objects()
    {
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported) return; // Native AOT: GetGeneration is not meaningful
        foreach (object tag in new[] { LuaValue.IntegerTag, LuaValue.FloatTag, LuaValue.TrueTag, LuaValue.FalseTag })
            Assert.Equal(int.MaxValue, GC.GetGeneration(tag));
    }

    [Fact]
    public void Default_value_is_nil() =>
        Assert.True(default(LuaValue).IsNil);

    [Fact]
    public void Integers_and_floats_round_trip_and_stay_distinct()
    {
        var i = LuaValue.FromInteger(-42);
        var f = LuaValue.FromFloat(-42.0);
        Assert.True(i.IsInteger);
        Assert.False(i.IsFloat);
        Assert.Equal(-42L, i.AsInteger);
        Assert.True(f.IsFloat);
        Assert.Equal(-42.0, f.AsFloat);
    }
}
