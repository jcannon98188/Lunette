using Xunit;

namespace Lunette.Tests;

public class TableTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Constructors_build_array_and_record_parts() =>
        Assert.Equal("10\t20\t30\t1\t2\n", Run("""
            local t = {10, 20, 30, x = 1, ["y"] = 2}
            print(t[1], t[2], t[3], t.x, t.y)
            """));

    [Fact]
    public void Field_and_index_assignment() =>
        Assert.Equal("5\t7\t9\n", Run("""
            local t = {}
            t.a = 5
            t[1] = 7
            local k = "z"
            t[k] = 9
            print(t.a, t[1], t.z)
            """));

    [Fact]
    public void Nested_tables_and_chained_access_and_assignment() =>
        Assert.Equal("3\t4\n", Run("""
            local t = {a = {b = {c = 3}}}
            t.a.b.d = 4
            print(t.a.b.c, t.a.b.d)
            """));

    [Fact]
    public void Multiple_assignment_to_fields() =>
        Assert.Equal("1\t2\n", Run("local t = {} t.x, t.y = 1, 2 print(t.x, t.y)"));

    [Fact]
    public void Last_constructor_item_expands_multiple_results() =>
        Assert.Equal("3\t1\t2\t3\n4\n", Run("""
            local function f() return 1, 2, 3 end
            local t = {f()}
            print(#t, t[1], t[2], t[3])
            local u = {f(), f()}
            print(#u)
            """));

    [Fact]
    public void Constructor_with_varargs() =>
        Assert.Equal("3\t3\n", Run("""
            local function f(...) local t = {...} local a, b, c = ... return #t, c end
            print(f(1, 2, 3))
            """));

    [Fact]
    public void Constructor_holes_keep_the_array_size_like_puc() =>
        Assert.Equal("4\t3\t1\n", Run("""
            print(#{1, 2, nil, 4}, #{1, nil, 3}, #{1, nil, nil})
            """));

    [Fact]
    public void Length_of_sequences_and_after_removal() =>
        Assert.Equal("0\n3\n2\n", Run("""
            local t = {}
            print(#t)
            t[1] = 1 t[2] = 2 t[3] = 3
            print(#t)
            t[3] = nil
            print(#t)
            """));

    [Fact]
    public void Methods_via_colon_syntax_and_definition() =>
        Assert.Equal("7\n", Run("""
            local obj = {n = 5}
            function obj:add(x) return self.n + x end
            print(obj:add(2))
            """));

    [Fact]
    public void Dotted_function_names() =>
        Assert.Equal("3\n", Run("""
            local m = {sub = {}}
            function m.sub.f(a, b) return a + b end
            print(m.sub.f(1, 2))
            """));

    [Fact]
    public void Calls_with_string_and_table_arguments() =>
        Assert.Equal("hi\t2\n", Run("""
            local function id(x) return x end
            print(id"hi", #id{1, 2})
            """));

    [Fact]
    public void Generic_for_with_pairs_visits_everything() =>
        Assert.Equal("6\t5\n", Run("""
            local t = {1, 2, 3, x = 10, y = 20}
            local s, n = 0, 0
            for k, v in pairs(t) do n = n + 1 if type(k) == "number" then s = s + v end end
            print(s, n)
            """));

    [Fact]
    public void Generic_for_with_ipairs_stops_at_first_nil() =>
        Assert.Equal("6\n", Run("""
            local s = 0
            for i, v in ipairs({1, 2, 3, nil, 5}) do s = s + v end
            print(s)
            """));

    [Fact]
    public void Generic_for_supports_break_and_custom_iterators() =>
        Assert.Equal("3\n", Run("""
            local function it(max, i) i = i + 1 if i <= max then return i end end
            local last
            for i in it, 5, 0 do last = i if i == 3 then break end end
            print(last)
            """));

    [Fact]
    public void Next_returns_nil_when_empty_and_pairs_returns_next() =>
        Assert.Equal("nil\ttrue\n", Run("""
            print(next({}), pairs({}) == next)
            """));

    [Fact]
    public void Assigning_nil_to_existing_fields_during_traversal_is_legal() =>
        Assert.Equal("0\n", Run("""
            local t = {}
            for i = 1, 20 do t[i] = i t[i + 100] = i end
            for k in pairs(t) do t[k] = nil end
            print(next(t) == nil and 0 or 1)
            """));

    [Fact]
    public void Raw_access_functions() =>
        Assert.Equal("1\t2\ttrue\tfalse\t3\n", Run("""
            local t = setmetatable({}, {__index = function() return 99 end, __newindex = function() error("no") end})
            rawset(t, "a", 1)
            rawset(t, 1, 2)
            print(rawget(t, "a"), rawget(t, 1), rawequal(t, t), rawequal(t, {}), rawlen({1, 2, 3}))
            """));

    [Fact]
    public void Type_names() =>
        Assert.Equal("table\tnumber\tnil\tboolean\tfunction\tstring\n", Run("""
            print(type({}), type(1), type(nil), type(true), type(print), type("x"))
            """));

    [Fact]
    public void Indexing_a_non_table_is_an_error() =>
        Assert.Contains("attempt to index a nil value",
            Assert.Throws<LuaException>(() => Run("local x = nil; print(x.y)")).Message);

    [Fact]
    public void Nil_key_assignment_is_an_error() =>
        Assert.Contains("table index is nil",
            Assert.Throws<LuaException>(() => Run("local t = {} t[nil] = 1")).Message);

    [Fact]
    public void Float_keys_normalise() =>
        Assert.Equal("a\tb\n", Run("""
            local t = {}
            t[1.0] = "a"
            t[2] = "b"
            print(t[1], t[2.0])
            """));

    [Fact]
    public void Constructors_longer_than_one_flush_batch() =>
        Assert.Equal("120\t120\t7260\n", Run("""
            local t = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120}
            local s = 0
            for i = 1, #t do s = s + t[i] end
            print(#t, t[120], s)
            """));

    [Fact]
    public void Method_calls_on_nested_fields_and_chains() =>
        Assert.Equal("6\n", Run("""
            local a = {b = {n = 1}}
            function a.b:add(x) self.n = self.n + x return self end
            print(a.b:add(2):add(3).n)
            """));

    [Fact]
    public void Constructor_mixing_record_and_list_fields_and_trailing_separator() =>
        Assert.Equal("1\t2\t3\t4\n", Run("""
            local t = {1, x = 3; 2, y = 4,}
            print(t[1], t[2], t.x, t.y)
            """));
}
