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

    // ---- metamethod fast paths (ticket 52): Frame-pushing __index/__newindex/__eq/arithmetic handlers and table chains ----

    [Fact]
    public void Index_function_found_in_the_middle_of_a_table_chain_gets_that_table() =>
        Assert.Equal("k\t1\n", Run("""
            local base
            base = setmetatable({}, {__index = function(t, k) return t == base and k or "wrong" end})
            local mid = setmetatable({}, {__index = base})
            local obj = setmetatable({x = 1}, {__index = mid})
            print(obj.k, obj.x)
            """));

    [Fact]
    public void Method_found_through_a_deep_chain_and_not_found() =>
        Assert.Equal("3\tnil\n", Run("""
            local A = {}; A.__index = A; function A:get() return self.v end
            local B = setmetatable({}, A); B.__index = B
            local C = setmetatable({}, B); C.__index = C
            local o = setmetatable({v = 3}, C)
            print(o:get(), o.nothing)
            """));

    [Fact]
    public void Index_chain_ending_in_a_string_handler_reaches_the_string_library() =>
        Assert.Equal("5\n", Run("""
            local o = setmetatable({}, {__index = setmetatable({}, {__index = "hello"})})
            print(o.len("abcde"))
            """));

    [Fact]
    public void Index_through_a_chain_with_a_non_indexable_handler_is_an_error() =>
        Assert.Contains("attempt to index a number value",
            Assert.Throws<LuaException>(() => Run("local o = setmetatable({}, {__index = setmetatable({}, {__index = 5})}) print(o.x)")).Message);

    [Fact]
    public void Newindex_chain_of_tables_then_function_receives_the_last_table() =>
        Assert.Equal("true\tv\n", Run("""
            local sink = {}
            local last
            last = setmetatable({}, {__newindex = function(t, k, v) sink[k] = v; sink.same = rawequal(t, last) end})
            local mid = setmetatable({}, {__newindex = last})
            local o = setmetatable({}, {__newindex = mid})
            o.k = "v"
            print(sink.same, sink.k)
            """));

    [Fact]
    public void Newindex_with_an_existing_key_in_the_middle_of_the_chain_is_a_raw_store() =>
        Assert.Equal("nil\t9\n", Run("""
            local mid = setmetatable({k = 1}, {__newindex = function() error("no") end})
            local o = setmetatable({}, {__newindex = mid})
            o.k = 9
            print(rawget(o, "k"), rawget(mid, "k"))
            """));

    [Fact]
    public void Newindex_with_a_nil_key_reports_the_positioned_error() =>
        Assert.Contains("table index is nil",
            Assert.Throws<LuaException>(() => Run("local o = setmetatable({}, {__newindex = {}}) o[nil] = 1")).Message);

    [Fact]
    public void Eq_handler_of_either_operand_and_not_for_the_same_table() =>
        Assert.Equal("true\ttrue\tfalse\t3\ttrue\n", Run("""
            local calls = 0
            local mt = {__eq = function(a, b) calls = calls + 1 return a.id == b.id end}
            local a, b, c = setmetatable({id = 1}, mt), {id = 1}, setmetatable({id = 2}, mt)
            local r1, r2, r3 = a == b, b == a, a == c
            print(r1, r2, r3, calls, a == a)
            """));

    [Fact]
    public void Eq_handler_result_drives_branches_and_jumps() =>
        Assert.Equal("eq\tne\ttrue\tfalse\n", Run("""
            local mt = {__eq = function(a, b) return a.id == b.id end}
            local function mk(i) return setmetatable({id = i}, mt) end
            local x, y, z = mk(1), mk(1), mk(2)
            local r1, r2
            if x == y then r1 = "eq" else r1 = "ne" end
            if x == z then r2 = "eq" else r2 = "ne" end
            print(r1, r2, x ~= z, x ~= y)
            """));

    [Fact]
    public void Arithmetic_handler_picks_the_first_operand_then_the_second() =>
        Assert.Equal("L\tR\tL\n", Run("""
            local L = setmetatable({}, {__add = function() return "L" end})
            local R = setmetatable({}, {__add = function() return "R" end})
            local plain = {}
            print(L + R, plain + R, L + 1)
            """));

    [Fact]
    public void Arithmetic_handler_with_a_constant_operand() =>
        Assert.Equal("6\n", Run("""
            local v = setmetatable({n = 3}, {__mul = function(a, k) return a.n * k end})
            print(v * 2)
            """));

    [Fact]
    public void Metamethod_handlers_can_yield_in_coroutines() =>
        Assert.Equal("1\t2\t3\t4\t5\tdone\n", Run("""
            local mt = {}
            mt.__index = function(t, k) return coroutine.yield(1) end
            mt.__newindex = function(t, k, v) coroutine.yield(v) end
            mt.__eq = function(a, b) coroutine.yield(3) return true end
            mt.__add = function(a, b) coroutine.yield(4) return 40 end
            mt.__call = function(self, x) coroutine.yield(x) return x end
            local a, b = setmetatable({}, mt), setmetatable({}, mt)
            local co = coroutine.wrap(function()
                local v = a.x
                a.y = 2
                local e = a == b
                local s = a + b
                local c = a(5)
                return "done"
            end)
            local out = {}
            for i = 1, 5 do out[#out + 1] = co(i) end
            out[#out + 1] = co()
            print(table.concat(out, "\t"))
            """));

    [Fact]
    public void Call_on_a_table_with_arguments_and_results() =>
        Assert.Equal("6\t3\t5\n", Run("""
            local obj = setmetatable({n = 3}, {__call = function(self, a, b) return a + b, self.n, a + self.n end})
            local x, y, z = obj(2, 4)
            print(x, y, z)
            """));

    [Fact]
    public void Call_on_a_chain_of_callable_tables() =>
        Assert.Equal("3\t1\t2\t3\n", Run("""
            local inner = setmetatable({}, {__call = function(self, outer, a, b, c) return a, b, c end})
            local outer = setmetatable({}, {__call = inner})
            print(select("#", outer(1, 2, 3)), outer(1, 2, 3))
            """));

    [Fact]
    public void Setmetatable_fast_path_keeps_protection_and_argument_errors() =>
        Assert.Equal("true\ttrue\tnil\ttrue\n", Run("""
            local t = {}
            local r = setmetatable(t, {__index = {}})
            local p = setmetatable({}, {__metatable = "locked"})
            print(r == t, not pcall(setmetatable, t, 1), getmetatable(setmetatable({}, nil)), not pcall(setmetatable, p, {}))
            """));

    [Fact]
    public void Setmetatable_marks_gc_metatables() =>
        Assert.Equal("true\n", Run("""
            local closed = false
            do local o = setmetatable({}, {__gc = function() closed = true end}) end
            collectgarbage() collectgarbage()
            print(closed)
            """));
}
