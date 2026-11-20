using Xunit;

namespace Lunette.Tests;

/// <summary>Userdata over arbitrary .NET objects, and UTF-8 interop at the boundary.</summary>
public class UserdataAndInteropTests
{
    private sealed class Counter
    {
        public long Value;
    }

    private static Counter SelfOf(ReadOnlySpan<LuaValue> args)
    {
        Assert.True(args[0].TryGet(out LuaUserdata? u));
        return (Counter)u!.Value;
    }

    private static LuaState StateWithCounterType()
    {
        var state = new LuaState();
        var methods = new LuaTable();
        methods.Set("inc", LuaValue.FromFunction(state.CreateFunction((ref CallContext ctx) =>
        {
            var self = SelfOf(ctx.Args);
            self.Value += ctx.Args.Length > 1 ? ctx.Args[1].AsInteger : 1;
            ctx.Push(self.Value);
            return 1;
        })));
        var mt = new LuaTable();
        mt.Set("__index", LuaValue.FromTable(methods));
        mt.Set("__tostring", LuaValue.FromFunction(state.CreateFunction((ref CallContext ctx) =>
        {
            ctx.Push("Counter(" + SelfOf(ctx.Args).Value + ")");
            return 1;
        })));
        state.RegisterMetatable<Counter>(mt);
        return state;
    }

    [Fact]
    public void Userdata_exposes_a_dotnet_object_through_a_registered_metatable()
    {
        LuaState state = StateWithCounterType();
        var counter = new Counter();
        state.SetGlobal("c", LuaValue.FromUserdata(state.CreateUserdata(counter)));
        Assert.Equal(3, state.Call(state.Load("c:inc() c:inc() return c:inc()")).AsInteger);
        Assert.Equal(3, counter.Value);
        Assert.Equal("userdata", state.Call(state.Load("return type(c)")).ToString());
        Assert.Equal("Counter(3)", state.Call(state.Load("return tostring(c)")).ToString());
    }

    [Fact]
    public void A_userdata_without_a_metatable_rejects_indexing_with_a_lua_error()
    {
        var state = new LuaState();
        state.SetGlobal("u", LuaValue.FromUserdata(state.CreateUserdata(new object())));
        Assert.Contains("attempt to index a userdata value", Assert.Throws<LuaException>(() => state.Call(state.Load("return u.x"))).Message);
    }

    [Fact]
    public void Derived_types_use_the_base_types_metatable()
    {
        var state = new LuaState();
        var mt = new LuaTable();
        state.RegisterMetatable<Exception>(mt);
        Assert.Same(mt, state.CreateUserdata(new InvalidOperationException()).Metatable);
    }

    [Fact]
    public void Strings_cross_the_boundary_as_utf8_bytes()
    {
        var state = new LuaState();
        state.SetGlobal("s", "héllo");
        Assert.Equal(6, state.Call(state.Load("return #s")).AsInteger);
        Assert.True(state.GetGlobal("s").TryGetBytes(out ReadOnlySpan<byte> bytes));
        Assert.Equal(new byte[] { 0x68, 0xC3, 0xA9, 0x6C, 0x6C, 0x6F }, bytes.ToArray());
        Assert.True(state.GetGlobal("s").TryGetString(out string? back));
        Assert.Equal("héllo", back);
    }

    [Fact]
    public void Invalid_utf8_is_replaced_by_default_and_rejected_in_strict_mode()
    {
        LuaValue v = LuaValue.FromBytes([0x61, 0xFF, 0x62]);
        Assert.True(v.TryGetString(out string? lenient));
        Assert.Equal("a�b", lenient);
        Assert.False(v.TryGetStrictString(out _));
        Assert.True(LuaValue.FromString("ok").TryGetStrictString(out string? strict));
        Assert.Equal("ok", strict);
    }

    [Fact]
    public void Latin1_is_an_explicit_raw_byte_conversion()
    {
        LuaValue v = LuaValue.FromLatin1("café");
        Assert.True(v.TryGetBytes(out ReadOnlySpan<byte> bytes));
        Assert.Equal(new byte[] { 0x63, 0x61, 0x66, 0xE9 }, bytes.ToArray());
        Assert.True(v.TryGetLatin1String(out string? s));
        Assert.Equal("café", s);
    }

    [Fact]
    public void Typed_accessors_report_the_lua_type_without_coercion()
    {
        Assert.Equal(LuaType.Nil, default(LuaValue).Type);
        Assert.Equal(LuaType.Boolean, ((LuaValue)true).Type);
        Assert.Equal(LuaType.Number, ((LuaValue)1.5).Type);
        Assert.Equal(LuaType.String, ((LuaValue)"x").Type);
        Assert.Equal(LuaType.Table, LuaValue.FromTable(new LuaTable()).Type);
        LuaValue i = 7;
        Assert.True(i.TryGet(out long l));
        Assert.Equal(7, l);
        Assert.True(i.TryGet(out double d));
        Assert.Equal(7.0, d);
        Assert.False(i.TryGet(out bool _));
        LuaValue f = 1.5;
        Assert.False(f.TryGetInteger(out _));
        Assert.False(((LuaValue)"7").TryGetNumber(out _));
    }
}
