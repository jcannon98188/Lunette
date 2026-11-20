using Xunit;

namespace Lunette.Tests;

/// <summary>In-place table.unpack, tonumber, math.max/min and builtins in tail position (ticket 55) behave exactly like the general paths. Expected values come from PUC Lua 5.4.9.</summary>
public class Ticket55FastPathTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Unpack_with_ranges_pads_and_trims_to_the_wanted_count()
    {
        Assert.Equal("2\t3\t4\t1\tnil\tnil\tnil\tnil\n8\t3\t0\n5\tnil\tnil\n", Run("""
            local t = { 1, 2, 3, 4, 5, 6, 7, 8 }
            local a, b, c = table.unpack(t, 2, 4)
            local d, e = table.unpack(t, 1, 1)
            local g, h, i = table.unpack({}, 1, 0)
            print(a, b, c, d, e, g, h, i)
            print(select('#', table.unpack(t)), select('#', table.unpack(t, 2, 4)), select('#', table.unpack(t, 3, 2)))
            print((table.unpack(t, 5)), table.unpack({}, 1, 2))
            """));
    }

    [Fact]
    public void Unpack_defaults_negative_bounds_and_holes()
    {
        Assert.Equal("1\t2\t3\n-1\t0\t1\t2\n3\t3\n", Run("""
            print(table.unpack({ 1, 2, 3 }))
            print(table.unpack({ [-1] = -1, [0] = 0, 1, 2 }, -1, 2))
            local h = { 1, nil, 3 }
            print(select('#', table.unpack(h, 1, 3)), (select(3, table.unpack(h, 1, 3))))
            """));
    }

    [Fact]
    public void Unpack_errors_metamethods_and_large_ranges_take_the_general_path()
    {
        Assert.Equal("false\ttrue\tfalse\ttrue\n100\t7\t100\n", Run("""
            local ok1, e1 = pcall(table.unpack, 1, 2, 3)
            local ok2, e2 = pcall(table.unpack, {}, 1, 1e7)
            print(ok1, e1:find("attempt to index a number value") ~= nil, ok2, e2:find("too many results to unpack") ~= nil)
            local p = setmetatable({}, { __index = function(_, k) return k * 7 end })
            print(select('#', table.unpack(p, 1, 100)), (table.unpack(p, 1, 1)), select('#', table.unpack({}, 1, 100)))
            """));
    }

    [Fact]
    public void Tonumber_in_place_matches_the_general_conversion()
    {
        Assert.Equal("12\t12.5\t31\tnil\tnil\t5\t3.0\tnil\t100.0\tnil\n", Run("""
            print(tonumber("12"), tonumber(" 12.5 "), tonumber("0x1f"), tonumber("abc"), tonumber(nil), tonumber(5), tonumber(3.0), tonumber({}), tonumber("1e2"), tonumber(""))
            """));
    }

    [Fact]
    public void Tonumber_with_a_base_and_without_arguments_keeps_its_errors()
    {
        Assert.Equal("255\tfalse\ttrue\tfalse\ttrue\n", Run("""
            local ok, e = pcall(tonumber)
            local ok2, e2 = pcall(tonumber, 10, 16)
            print(tonumber("ff", 16), ok, e:find("bad argument #1 to 'tonumber' %(value expected%)") ~= nil, ok2, e2:find("string expected, got number") ~= nil)
            """));
    }

    [Fact]
    public void Max_and_min_of_two_numbers_follow_the_number_tower()
    {
        Assert.Equal("5\t5.5\t2\t-1.5\t3\t1\t3.0\t2.5\t1\ntrue\ttrue\n9\t1\n", Run("""
            print(math.max(2, 5), math.max(5.5, 2), math.min(2, 5), math.min(-1.5, 2), math.max(3, 3.0), math.min(1, 1.0), math.max(3.0, 3), math.max(1, 2.5), math.min(1, 2.5))
            local nan = 0 / 0
            print(math.max(nan, 1) ~= math.max(nan, 1), math.max(1, nan) == 1)
            print(math.max(1, 9, 4), math.min(3, 1, 2))
            """));
    }

    [Fact]
    public void Max_and_min_without_arguments_keep_their_error()
    {
        Assert.Equal("false\ttrue\n", Run("""
            local ok, e = pcall(math.min)
            print(ok, e:find("value expected") ~= nil)
            """));
    }

    [Fact]
    public void Select_unpack_tonumber_and_max_in_tail_position_deliver_their_results()
    {
        Assert.Equal("3\t20\t30\n1\t2\t3\n0\t7\n7\tnil\n4\n", Run("""
            local function count(...) return select('#', ...) end
            local function from2(...) return select(2, ...) end
            local function up(t) return table.unpack(t) end
            local function none(...) return select('#') end
            local function neg(...) return select(-1, ...) end
            print(count(1, 2, nil), from2(10, 20, 30))
            print(up({ 1, 2, 3 }))
            print(none(), neg(5, 6, 7))
            local function tn(x) return tonumber(x) end
            print(tn("7"), tn("z"))
            local function mx(a, b) return math.max(a, b) end
            print(mx(3, 4))
            """));
    }

    [Fact]
    public void Tail_called_builtin_errors_keep_their_messages()
    {
        Assert.Equal("false\ttrue\tfalse\ttrue\n", Run("""
            local function bad(...) return select(0, ...) end
            local ok, e = pcall(bad, 1)
            local function badu() return table.unpack(1) end
            local ok2, e2 = pcall(badu)
            print(ok, e:find("bad argument #1 to 'select' %(index out of range%)") ~= nil, ok2, e2:find("attempt to get length of a number value") ~= nil)
            """));
    }
}
