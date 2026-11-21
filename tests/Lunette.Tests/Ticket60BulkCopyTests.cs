using Xunit;

namespace Lunette.Tests;

/// <summary>table.insert/remove/move on plain tables use bulk array copies (ticket 60) and must be indistinguishable from the element-wise loops. Expected values come from PUC Lua 5.4.9.</summary>
public class Ticket60BulkCopyTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    private const string Show = "local function show(t, n) local p = {} for i = 1, n or #t do p[#p + 1] = tostring(t[i]) end print(#t, table.concat(p, ',')) end\n";

    [Fact]
    public void Insert_and_remove_shift_the_array_and_keep_the_border()
    {
        Assert.Equal("6\t0,1,2,3,4,5\n7\t0,1,2,m,3,4,5\n0\tm\t5\n4\t1,2,3,4\n", Run(Show + """
            local t = { 1, 2, 3, 4, 5 }
            table.insert(t, 1, 0)
            show(t)
            table.insert(t, 4, "m")
            show(t)
            print(table.remove(t, 1), table.remove(t, 3), table.remove(t))
            show(t)
            """));
    }

    [Fact]
    public void Remove_from_a_table_with_holes_matches_the_reference_border()
    {
        Assert.Equal("1\t7\n7\tnil,3,nil,5,6,7,8,nil\n", Run(Show + """
            local h = { 1, nil, 3, nil, 5, 6, 7, 8 }
            print(table.remove(h, 1), #h)
            show(h, 8)
            """));
    }

    [Fact]
    public void Move_handles_overlap_in_both_directions_and_other_tables()
    {
        Assert.Equal("5\t1,1,2,3,4\n5\t1,2,3,4,4\n0\tnil,nil,nil,nil,1,2,3\n", Run(Show + """
            local m = { 1, 2, 3, 4, 5 }
            table.move(m, 1, 4, 2)
            show(m)
            table.move(m, 2, 5, 1)
            show(m)
            local d = {}
            table.move({ 1, 2, 3 }, 1, 3, 5, d)
            show(d, 7)
            """));
    }

    [Fact]
    public void Errors_keep_their_messages()
    {
        Assert.Equal(
            "false\tbad argument #2 to 'table.insert' (position out of bounds)\n" +
            "false\tbad argument #2 to 'table.insert' (position out of bounds)\n" +
            "false\twrong number of arguments to 'insert'\n" +
            "false\tbad argument #2 to 'table.remove' (position out of bounds)\n" +
            "nil\tnil\tnil\n", Run("""
            print(pcall(table.insert, { 1, 2 }, 5, "x"))
            print(pcall(table.insert, { 1, 2 }, 0, "x"))
            print(pcall(table.insert, { 1, 2 }, 1, 2, 3))
            print(pcall(table.remove, { 1, 2 }, 5))
            print(table.remove({}, 0), table.remove({}, 1), table.remove({ 1 }, 2))
            """));
    }

    [Fact]
    public void Metamethods_take_the_element_wise_path()
    {
        Assert.Equal("r3 w4=3 r2 w3=2 w2=z\n", Run("""
            local log = {}
            local p = setmetatable({}, { __index = function(_, k) log[#log + 1] = "r" .. k return k end,
              __newindex = function(_, k, v) log[#log + 1] = "w" .. k .. "=" .. tostring(v) end, __len = function() return 3 end })
            table.insert(p, 2, "z")
            print(table.concat(log, " "))
            """));
    }
}
