using Xunit;

namespace Lunette.Tests;

public class MetatableTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Get_and_set_metatable() =>
        Assert.Equal("true\tnil\ttrue\n", Run("""
            local mt = {}
            local t = setmetatable({}, mt)
            print(getmetatable(t) == mt, getmetatable({}), setmetatable(t, nil) == t)
            """));

    [Fact]
    public void Metatable_field_protects_the_metatable() =>
        Assert.Equal("locked\n", Run("""
            local t = setmetatable({}, {__metatable = "locked"})
            print(getmetatable(t))
            """)) ;

    [Fact]
    public void Setting_a_protected_metatable_is_an_error() =>
        Assert.Contains("cannot change a protected metatable",
            Assert.Throws<LuaException>(() => Run("local t = setmetatable({}, {__metatable = 1}) setmetatable(t, {})")).Message);

    [Fact]
    public void Index_with_a_table_chain() =>
        Assert.Equal("1\t2\tnil\n", Run("""
            local a = {x = 1}
            local b = setmetatable({y = 2}, {__index = a})
            local c = setmetatable({}, {__index = b})
            print(c.x, c.y, c.z)
            """));

    [Fact]
    public void Index_with_a_function() =>
        Assert.Equal("key\n", Run("""
            local t = setmetatable({}, {__index = function(tbl, k) return k end})
            print(t.key)
            """));

    [Fact]
    public void Newindex_with_a_function_and_with_a_table() =>
        Assert.Equal("5\tnil\t6\n", Run("""
            local log = {}
            local t = setmetatable({}, {__newindex = function(tbl, k, v) rawset(tbl, k, v + 1) end})
            t.a = 4
            local store = {}
            local u = setmetatable({}, {__newindex = store})
            u.b = 6
            print(t.a, rawget(u, "b"), store.b)
            """));

    [Fact]
    public void Newindex_is_not_called_for_existing_keys() =>
        Assert.Equal("9\n", Run("""
            local t = setmetatable({a = 1}, {__newindex = function() error("no") end})
            t.a = 9
            print(t.a)
            """));

    [Fact]
    public void Arithmetic_metamethods() =>
        Assert.Equal("11\t-9\t30\t0.5\t1\t4.0\t-1\t3\t3\n", Run("""
            local mt = {}
            local function new(v) return setmetatable({v = v}, mt) end
            mt.__add = function(a, b) return (type(a) == "table" and a.v or a) + (type(b) == "table" and b.v or b) end
            mt.__sub = function(a, b) return a.v - b end
            mt.__mul = function(a, b) return a.v * b end
            mt.__div = function(a, b) return a.v / b end
            mt.__mod = function(a, b) return a.v % b end
            mt.__pow = function(a, b) return a.v ^ b end
            mt.__unm = function(a) return -a.v end
            mt.__idiv = function(a, b) return a.v // b end
            print(new(1) + 10, new(1) - 10, new(10) * 3, new(1) / 2, new(5) % 2, new(2) ^ 2, -new(1), 1 + new(2), new(7) // 2)
            """));

    [Fact]
    public void Length_metamethod() =>
        Assert.Equal("42\t3\n", Run("""
            local t = setmetatable({1, 2, 3}, {__len = function() return 42 end})
            print(#t, rawlen(t))
            """));

    [Fact]
    public void Equality_metamethod_only_for_distinct_tables() =>
        Assert.Equal("true\tfalse\ttrue\n", Run("""
            local mt = {__eq = function(a, b) return a.id == b.id end}
            local a, b, c = setmetatable({id = 1}, mt), setmetatable({id = 1}, mt), setmetatable({id = 2}, mt)
            print(a == b, a == c, a ~= c)
            """));

    [Fact]
    public void Order_metamethods() =>
        Assert.Equal("true\tfalse\ttrue\ttrue\n", Run("""
            local mt = {}
            mt.__lt = function(a, b) return a.v < b.v end
            mt.__le = function(a, b) return a.v <= b.v end
            local a, b = setmetatable({v = 1}, mt), setmetatable({v = 2}, mt)
            print(a < b, a > b, a <= b, b >= a)
            """));

    [Fact]
    public void Call_metamethod() =>
        Assert.Equal("3\t10\t20\n", Run("""
            local t = setmetatable({}, {__call = function(self, a, b) return a + b, 10, 20 end})
            print(t(1, 2))
            """));

    [Fact]
    public void Call_metamethod_on_non_function_with_no_handler_is_an_error() =>
        Assert.Contains("attempt to call a table value",
            Assert.Throws<LuaException>(() => Run("local t = {} t()")).Message);

    [Fact]
    public void Tostring_metamethod_is_used_by_print_and_tostring() =>
        Assert.Equal("thing\tthing\n", Run("""
            local t = setmetatable({}, {__tostring = function() return "thing" end})
            print(t, tostring(t))
            """));

    [Fact]
    public void Pairs_metamethod() =>
        Assert.Equal("k\tv\n", Run("""
            local t = setmetatable({}, {__pairs = function(t) return function(_, k) if not k then return "k", "v" end end, t, nil end})
            for k, v in pairs(t) do print(k, v) end
            """));

    [Fact]
    public void Ipairs_respects_index_metamethod() =>
        Assert.Equal("3\n", Run("""
            local n = 0
            local t = setmetatable({}, {__index = function(_, i) if i <= 3 then return i end end})
            for i, v in ipairs(t) do n = n + 1 end
            print(n)
            """));

    [Fact]
    public void Metamethods_are_found_after_being_added_later() =>
        Assert.Equal("nil\t5\n", Run("""
            local mt = {}
            local t = setmetatable({}, mt)
            local before = t.x
            mt.__index = function() return 5 end
            print(before, t.x)
            """));

    [Fact]
    public void Index_chain_loop_is_an_error() =>
        Assert.Contains("'__index' chain too long",
            Assert.Throws<LuaException>(() => Run("local a, b = {}, {} setmetatable(a, {__index = b}) setmetatable(b, {__index = a}) print(a.x)")).Message);

    [Fact]
    public void Metamethod_call_from_nested_lua_calls() =>
        Assert.Equal("21\n", Run("""
            local t = setmetatable({}, {__index = function(_, k) return k * 2 end})
            local function f(n) if n == 0 then return t[10] end return f(n - 1) + 1 end
            print(f(1))
            """));

    [Fact]
    public void Arithmetic_on_a_table_without_metamethod_is_an_error() =>
        Assert.Contains("attempt to perform arithmetic on a table value",
            Assert.Throws<LuaException>(() => Run("local t = {} print(t + 1)")).Message);
}
