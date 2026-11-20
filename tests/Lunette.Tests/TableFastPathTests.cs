using Xunit;

namespace Lunette.Tests;

/// <summary>
/// The table fast paths of ticket 43: reference-returning string and integer lookups, string-key hashing by mask, integer keys in the
/// hash part, sized construction and metatable-aware raw stores. Each behaves exactly like the generic <c>Get</c>/<c>Set</c>.
/// </summary>
public class TableFastPathTests
{
    private static LuaValue I(long v) => LuaValue.FromInteger(v);
    private static LuaValue S(string v) => LuaValue.FromString(v);

    /// <summary>A string with the same bytes as <paramref name="s"/> but a different backing array (not interned with it).</summary>
    private static byte[] Fresh(string s) => LuaString.Create(System.Text.Encoding.UTF8.GetBytes(s)).Backing;

    [Fact]
    public void GetStr_finds_a_key_stored_under_an_equal_but_distinct_string()
    {
        var t = new LuaTable();
        t.Set(S("alpha"), I(1));
        Assert.Equal(1, t.GetStrRef(Fresh("alpha")).AsInteger);
        Assert.True(t.GetStrRef(Fresh("beta")).IsNil);
    }

    [Fact]
    public void GetStr_on_an_empty_table_is_nil() =>
        Assert.True(new LuaTable().GetStrRef(Fresh("x")).IsNil);

    [Fact]
    public void GetStr_survives_many_keys_removal_and_reinsertion()
    {
        var t = new LuaTable();
        for (int i = 0; i < 1000; i++) t.Set(S("key" + i), I(i));
        for (int i = 0; i < 1000; i++) Assert.Equal(i, t.GetStrRef(Fresh("key" + i)).AsInteger);
        for (int i = 0; i < 1000; i += 2) t.Set(S("key" + i), LuaValue.Nil);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(i % 2 == 0, t.GetStrRef(Fresh("key" + i)).IsNil);
        for (int i = 0; i < 1000; i += 2) t.Set(S("key" + i), I(-i));
        for (int i = 0; i < 1000; i += 2) Assert.Equal(-i, t.GetStrRef(Fresh("key" + i)).AsInteger);
    }

    [Fact]
    public void GetStr_does_not_confuse_a_string_with_a_number_or_table_key()
    {
        var t = new LuaTable();
        t.Set(I(1), I(10));
        t.Set(LuaValue.FromTable(new LuaTable()), I(20));
        t.Set(S("1"), I(30));
        Assert.Equal(30, t.GetStrRef(Fresh("1")).AsInteger);
        Assert.Equal(10, t.Get(I(1)).AsInteger);
    }

    [Fact]
    public void SetStr_overwrites_inserts_and_ignores_nil_for_an_absent_key()
    {
        var t = new LuaTable();
        t.SetStr(Fresh("a"), I(1));
        t.SetStr(Fresh("a"), I(2));
        t.SetStr(Fresh("b"), LuaValue.Nil); // nothing to store
        Assert.Equal(2, t.Get(S("a")).AsInteger);
        Assert.True(t.Get(S("b")).IsNil);
        Assert.False(t.Next(LuaValue.Nil, out var k, out _) && k.Ref is byte[] kb && new LuaString(kb).ToString() == "b");
    }

    [Fact]
    public void SetStr_clears_the_absent_metamethod_cache()
    {
        var mt = new LuaTable();
        Assert.True(mt.GetTagMethod(TagMethod.Index).IsNil); // caches the absence
        mt.SetStr(LuaString.Create("__index"u8).Backing, LuaValue.FromTable(new LuaTable()));
        Assert.False(mt.GetTagMethod(TagMethod.Index).IsNil);
    }

    [Fact]
    public void GetInt_reads_array_hash_and_missing_keys()
    {
        var t = new LuaTable();
        for (int i = 1; i <= 10; i++) t.Set(I(i), I(i * 2));
        t.Set(I(1L << 40), I(99));
        t.Set(I(-5), I(55));
        Assert.Equal(6, t.GetIntRef(3).AsInteger);
        Assert.Equal(99, t.GetIntRef(1L << 40).AsInteger);
        Assert.Equal(55, t.GetIntRef(-5).AsInteger);
        Assert.True(t.GetIntRef(11).IsNil);
        Assert.True(t.GetIntRef(0).IsNil);
        Assert.True(t.GetIntRef(long.MinValue).IsNil);
    }

    [Fact]
    public void Integer_keys_around_the_32_bit_boundary_hash_consistently()
    {
        var t = new LuaTable();
        long[] keys = [0, -1, int.MaxValue, 0xFFFFFFFFL, 0x100000000L, 0x1FFFFFFFFL, long.MaxValue, long.MinValue, 1L << 62];
        for (int i = 0; i < keys.Length; i++) t.Set(I(keys[i]), I(i));
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 0; i < keys.Length; i++) Assert.Equal(i, t.Get(I(keys[i])).AsInteger);
            for (int j = 0; j < 20; j++) t.Set(I(1000 + j), I(j)); // force rehashes of the hash part
        }
    }

    [Fact]
    public void GetRef_normalises_float_keys_and_rejects_nil_and_nan()
    {
        var t = new LuaTable();
        t.Set(I(3), I(7));
        t.Set(LuaValue.FromFloat(2.5), I(8));
        Assert.Equal(7, t.GetRef(LuaValue.FromFloat(3.0)).AsInteger);
        Assert.Equal(8, t.GetRef(LuaValue.FromFloat(2.5)).AsInteger);
        Assert.True(t.GetRef(LuaValue.Nil).IsNil);
        Assert.True(t.GetRef(LuaValue.FromFloat(double.NaN)).IsNil);
        Assert.True(t.GetRef(LuaValue.FromBoolean(true)).IsNil);
    }

    [Fact]
    public void A_ref_to_an_absent_key_is_nil_and_stays_nil_after_unrelated_stores()
    {
        var t = new LuaTable();
        Assert.True(t.GetIntRef(5).IsNil);
        t.Set(I(5), I(1));
        Assert.True(new LuaTable().GetIntRef(5).IsNil);
        Assert.True(new LuaTable().GetStrRef(Fresh("q")).IsNil);
    }

    [Fact]
    public void Sized_construction_gives_an_array_and_a_hash_part_without_rehashing()
    {
        var t = new LuaTable(4, 3);
        t.SetInt(1, I(1));
        t.SetInt(4, I(4));
        t.SetStr(Fresh("x"), I(5));
        t.SetStr(Fresh("y"), I(6));
        t.SetStr(Fresh("z"), I(7));
        Assert.Equal(4, t.Length());
        Assert.Equal(7, t.Get(S("z")).AsInteger);
        Assert.Equal(5, t.Get(S("x")).AsInteger);
    }

    [Fact]
    public void SetList_style_fill_matches_individual_sets()
    {
        var t = new LuaTable(5, 0);
        LuaValue[] src = [I(10), I(20), I(30), I(40), I(50)];
        t.SetRange(0, src, 0, 5);
        Assert.Equal(5, t.Length());
        for (int i = 0; i < 5; i++) Assert.Equal((i + 1) * 10, t.Get(I(i + 1)).AsInteger);
        t.SetRange(5, src, 0, 2); // grows the array part
        Assert.Equal(7, t.Length());
        Assert.Equal(20, t.Get(I(7)).AsInteger);
    }

    [Fact]
    public void Appending_one_past_the_array_keeps_length_and_contents()
    {
        var t = new LuaTable();
        for (int i = 1; i <= 5000; i++) t.Set(I(i), I(i));
        Assert.Equal(5000, t.Length());
        for (int i = 1; i <= 5000; i += 499) Assert.Equal(i, t.GetIntRef(i).AsInteger);
        t.Set(I(2), LuaValue.Nil);
        Assert.Equal(5000, t.Length());
    }

    [Fact]
    public void Next_walks_array_then_hash_with_string_and_integer_keys()
    {
        var t = new LuaTable();
        for (int i = 1; i <= 6; i++) t.Set(I(i), I(i));
        for (int i = 0; i < 6; i++) t.Set(S("s" + i), I(100 + i));
        t.Set(I(1L << 33), I(7));
        int count = 0;
        long sum = 0;
        var key = LuaValue.Nil;
        while (t.Next(key, out var k, out var v))
        {
            count++;
            sum += v.AsInteger;
            key = k;
        }
        Assert.Equal(13, count);
        Assert.Equal(1 + 2 + 3 + 4 + 5 + 6 + 100 + 101 + 102 + 103 + 104 + 105 + 7, sum);
    }

    [Fact]
    public void A_metatable_without_newindex_allows_the_raw_store_path()
    {
        var t = new LuaTable();
        Assert.True(t.NoNewIndex);
        var mt = new LuaTable();
        t.Metatable = mt;
        Assert.True(t.NoNewIndex);
        mt.Set(S("__newindex"), LuaValue.FromTable(new LuaTable()));
        Assert.False(t.NoNewIndex);
        mt.Set(S("__newindex"), LuaValue.Nil);
        Assert.True(t.NoNewIndex);
    }
}
