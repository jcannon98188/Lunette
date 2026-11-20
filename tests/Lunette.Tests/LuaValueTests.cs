using System.Runtime.CompilerServices;
using Xunit;

namespace Lunette.Tests;

public class LuaValueTests
{
    [Fact]
    public void LuaValue_is_sixteen_bytes() =>
        Assert.Equal(16, Unsafe.SizeOf<LuaValue>());

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
