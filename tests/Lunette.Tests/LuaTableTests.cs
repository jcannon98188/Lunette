using Xunit;

namespace Lunette.Tests;

public class LuaTableTests
{
    private static LuaValue I(long v) => LuaValue.FromInteger(v);
    private static LuaValue S(string v) => LuaValue.FromString(v);

    [Fact]
    public void Get_of_missing_key_is_nil() =>
        Assert.True(new LuaTable().Get(I(1)).IsNil);

    [Fact]
    public void Set_then_get_round_trips_integer_string_float_and_boolean_keys()
    {
        var t = new LuaTable();
        t.Set(I(1), I(10));
        t.Set(S("x"), I(20));
        t.Set(LuaValue.FromFloat(2.5), I(30));
        t.Set(LuaValue.FromBoolean(true), I(40));
        Assert.Equal(10, t.Get(I(1)).AsInteger);
        Assert.Equal(20, t.Get(S("x")).AsInteger);
        Assert.Equal(30, t.Get(LuaValue.FromFloat(2.5)).AsInteger);
        Assert.Equal(40, t.Get(LuaValue.FromBoolean(true)).AsInteger);
        Assert.True(t.Get(LuaValue.FromBoolean(false)).IsNil);
    }

    [Fact]
    public void Float_keys_with_integer_values_normalise_to_integer_keys()
    {
        var t = new LuaTable();
        t.Set(LuaValue.FromFloat(3.0), I(7));
        Assert.Equal(7, t.Get(I(3)).AsInteger);
        t.Set(I(4), I(8));
        Assert.Equal(8, t.Get(LuaValue.FromFloat(4.0)).AsInteger);
    }

    [Fact]
    public void Nil_and_NaN_keys_cannot_be_set()
    {
        var t = new LuaTable();
        Assert.Equal("table index is nil", Assert.Throws<LuaException>(() => t.Set(LuaValue.Nil, I(1))).Message);
        Assert.Equal("table index is NaN", Assert.Throws<LuaException>(() => t.Set(LuaValue.FromFloat(double.NaN), I(1))).Message);
        Assert.True(t.Get(LuaValue.FromFloat(double.NaN)).IsNil);
    }

    [Fact]
    public void Sequence_built_in_order_has_its_length_as_border()
    {
        var t = new LuaTable();
        for (int i = 1; i <= 100; i++) t.Set(I(i), I(i));
        Assert.Equal(100, t.Length());
    }

    [Fact]
    public void Appending_in_reverse_order_still_finds_the_border()
    {
        var t = new LuaTable();
        for (int i = 50; i >= 1; i--) t.Set(I(i), I(i));
        Assert.Equal(50, t.Length());
    }

    [Fact]
    public void Array_part_sizing_follows_pucs_more_than_half_used_rule()
    {
        // Matches PUC 5.4: t = {}; t[1]=1; t[2]=2; t[4]=4 rehashes into array size 4, so the border is 4.
        var t = new LuaTable();
        t.Set(I(1), I(1));
        t.Set(I(2), I(2));
        t.Set(I(4), I(4));
        Assert.Equal(4, t.Length());
    }

    [Fact]
    public void Constructor_sized_array_keeps_holes_inside_the_array_part()
    {
        // local t = {1, 2, nil, 4}  -> #t == 4 in PUC (array part of size 4)
        var t = new LuaTable();
        t.Resize(4, 0);
        t.SetInt(1, I(1));
        t.SetInt(2, I(2));
        t.SetInt(4, I(4));
        Assert.Equal(4, t.Length());
        // local t = {1, nil, 3}  -> #t == 3
        var u = new LuaTable();
        u.Resize(3, 0);
        u.SetInt(1, I(1));
        u.SetInt(3, I(3));
        Assert.Equal(3, u.Length());
        // {nil, nil, 3}: last array slot present -> 3
        var v = new LuaTable();
        v.Resize(3, 0);
        v.SetInt(3, I(3));
        Assert.Equal(3, v.Length());
        // {1, nil, nil}: binary search in the array -> 1
        var w = new LuaTable();
        w.Resize(3, 0);
        w.SetInt(1, I(1));
        Assert.Equal(1, w.Length());
    }

    [Fact]
    public void Length_continues_into_the_hash_part()
    {
        var t = new LuaTable();
        t.Resize(0, 8);
        for (int i = 1; i <= 5; i++) t.Set(I(i), I(i));
        Assert.Equal(5, t.Length());
    }

    [Fact]
    public void Shrinking_via_nil_assignment_moves_the_border_down()
    {
        var t = new LuaTable();
        for (int i = 1; i <= 8; i++) t.Set(I(i), I(i));
        t.Set(I(8), LuaValue.Nil);
        Assert.Equal(7, t.Length());
        t.Set(I(7), LuaValue.Nil);
        t.Set(I(6), LuaValue.Nil);
        Assert.Equal(5, t.Length());
    }

    [Fact]
    public void Next_visits_every_entry_exactly_once_and_survives_clearing_during_traversal()
    {
        var t = new LuaTable();
        for (int i = 1; i <= 10; i++) t.Set(I(i), I(i * 10));
        for (int i = 0; i < 10; i++) t.Set(S("k" + i), I(i));
        var seen = 0;
        var key = LuaValue.Nil;
        while (t.Next(key, out var k, out var v))
        {
            seen++;
            t.Set(k, LuaValue.Nil);
            key = k;
        }
        Assert.Equal(20, seen);
        Assert.False(t.Next(LuaValue.Nil, out _, out _));
    }

    [Fact]
    public void Next_with_an_unknown_key_is_an_error() =>
        Assert.Equal("invalid key to 'next'",
            Assert.Throws<LuaException>(() => new LuaTable().Next(S("nope"), out _, out _)).Message);

    [Fact]
    public void Many_colliding_inserts_and_removals_stay_consistent()
    {
        var t = new LuaTable();
        for (int i = 0; i < 1000; i++) t.Set(I(i * 7919), I(i));
        for (int i = 0; i < 1000; i++) Assert.Equal(i, t.Get(I(i * 7919)).AsInteger);
        for (int i = 0; i < 1000; i += 2) t.Set(I(i * 7919), LuaValue.Nil);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(i % 2 == 0, t.Get(I(i * 7919)).IsNil);
    }

    [Fact]
    public void Absent_metamethod_flags_are_cleared_when_the_table_changes()
    {
        var mt = new LuaTable();
        Assert.True(mt.AbsentTagMethod(TagMethod.Index));
        Assert.True(mt.AbsentTagMethod(TagMethod.Index)); // cached
        mt.Set(S("__index"), I(1));
        Assert.False(mt.AbsentTagMethod(TagMethod.Index));
    }
}
