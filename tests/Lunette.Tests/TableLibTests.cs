using Xunit;

namespace Lunette.Tests;

/// <summary>The <c>table</c> library, observed through Lua source.</summary>
public class TableLibTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Insert_appends_or_shifts()
    {
        Assert.Equal("a\tb\tc\n", Run("""
            local t = {'a'}
            table.insert(t, 'c')
            table.insert(t, 2, 'b')
            print(table.unpack(t))
            """));
    }

    [Fact]
    public void Insert_rejects_a_position_out_of_bounds()
    {
        Assert.Contains("position out of bounds", Run("print(pcall(table.insert, {1,2}, 5, 'x'))"));
    }

    [Fact]
    public void Remove_shifts_down_and_returns_the_value()
    {
        Assert.Equal("b\ta\tc\t2\n", Run("""
            local t = {'a', 'b', 'c'}
            local v = table.remove(t, 2)
            print(v, t[1], t[2], #t)
            """));
    }

    [Fact]
    public void Concat_joins_a_range_with_a_separator()
    {
        Assert.Equal("2,3,4\t\n", Run("print(table.concat({1,2,3,4}, ',', 2), table.concat({}))"));
    }

    [Fact]
    public void Concat_reports_the_offending_index()
    {
        Assert.Contains("invalid value (table) at index 2 in table for 'concat'", Run("print(pcall(table.concat, {'a', {}}))"));
    }

    [Fact]
    public void Unpack_returns_the_requested_range()
    {
        Assert.Equal("2\t3\tnil\n", Run("print(table.unpack({1,2,3}, 2, 4))"));
    }

    [Fact]
    public void Pack_counts_the_arguments_including_nils()
    {
        Assert.Equal("3\n", Run("print(table.pack(1, nil, nil).n)"));
    }

    [Fact]
    public void Move_copies_overlapping_ranges_in_the_safe_direction()
    {
        Assert.Equal("1\t1\t2\t3\n", Run("""
            local t = {1,2,3}
            table.move(t, 1, 3, 2)
            print(t[1], t[2], t[3], t[4])
            """));
    }

    [Fact]
    public void Sort_orders_with_the_default_and_a_custom_comparison()
    {
        Assert.Equal("1 2 3 5 9\n9 5 3 2 1\n", Run("""
            local t = {5, 2, 9, 1, 3}
            table.sort(t)
            print(table.concat(t, ' '))
            table.sort(t, function(a, b) return a > b end)
            print(table.concat(t, ' '))
            """));
    }

    [Fact]
    public void Sort_detects_an_inconsistent_order_function()
    {
        Assert.Contains("invalid order function for sorting", Run("""
            local t = {}
            for i = 1, 200 do t[i] = i % 7 end
            print(pcall(table.sort, t, function() return true end))
            """));
    }

    [Fact]
    public void Functions_respect_index_and_len_metamethods()
    {
        Assert.Equal("2\tx\ty\n", Run("""
            local p = setmetatable({}, {
              __index = function(_, k) return k == 1 and 'x' or k == 2 and 'y' or nil end,
              __len = function() return 2 end,
            })
            print(#p, table.unpack(p, 1, 2))
            """));
    }
}
