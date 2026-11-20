using Xunit;

namespace Lunette.Tests;

/// <summary>pairs, next and ipairs run in the interpreter loop for plain tables; every observable result is the builtin's.</summary>
public class IterationFastPathTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Pairs_on_a_plain_table_returns_next_the_table_and_nil()
    {
        Assert.Equal("true\ttrue\tnil\n", Run("local t = {} local f, s, c = pairs(t) print(f == next, s == t, c)"));
    }

    [Fact]
    public void Pairs_results_are_padded_and_trimmed_to_what_the_caller_wants()
    {
        Assert.Equal("true\ttrue\tnil\ntrue\n3\n", Run("""
            local t = {}
            local a, b, c, d = pairs(t)
            print(a == next, b == t, d)
            local x = (pairs(t))
            print(x == next)
            print(select('#', pairs(t)))
            """));
    }

    [Fact]
    public void Pairs_still_honours_a_pairs_metamethod()
    {
        Assert.Equal("1\t2\t3\n", Run("""
            local t = setmetatable({}, {__pairs = function(s) return 1, 2, 3 end})
            print(pairs(t))
            """));
    }

    [Fact]
    public void Pairs_on_a_non_table_fails_in_the_iterator_like_before()
    {
        Assert.Equal("false\tbad argument #1 to 'for iterator' (table expected, got nil)\n", Run("""
            print(pcall(function() for k in pairs(nil) do end end))
            """).Replace("(load):1: ", ""));
    }

    [Fact]
    public void Traversal_visits_array_and_hash_entries_exactly_once()
    {
        Assert.Equal("10\t55\t6\n", Run("""
            local t = {}
            for i = 1, 10 do t[i] = i end
            for i = 1, 6 do t["k" .. i] = i end
            local n, sum, hsum = 0, 0, 0
            for k, v in pairs(t) do
              n = n + 1
              if type(k) == "number" then sum = sum + v else hsum = hsum + 1 end
            end
            print(n - 6, sum, hsum)
            """));
    }

    [Fact]
    public void Clearing_fields_during_traversal_is_allowed()
    {
        Assert.Equal("0\n", Run("""
            local t = {}
            for i = 1, 50 do t["k" .. i] = i end
            for k in pairs(t) do t[k] = nil end
            print(next(t) == nil and 0 or 1)
            """));
    }

    [Fact]
    public void Next_with_explicit_arguments_matches_the_builtin()
    {
        Assert.Equal("1\ta\tnil\ttrue\n", Run("""
            local t = {"a"}
            local k, v = next(t)
            local k2 = next(t, k)
            print(k, v, k2, select('#', next({})) == 1)
            """));
    }

    [Fact]
    public void Next_rejects_a_bad_table_and_a_stale_key()
    {
        Assert.Equal(
            "false\tbad argument #1 to 'next' (table expected, got number)\n" +
            "false\tinvalid key to 'next'\n",
            Run("""
                print(pcall(next, 5))
                print(pcall(next, {}, "nope"))
                """).Replace("(load):1: ", "").Replace("(load):2: ", ""));
    }

    [Fact]
    public void Ipairs_stops_at_the_first_nil_and_sees_index_metamethods()
    {
        Assert.Equal("3\t4\n", Run("""
            local n = 0
            for _ in ipairs({1, 2, 3, nil, 5}) do n = n + 1 end
            local proxy = setmetatable({}, {__index = function(_, i) if i <= 4 then return i end end})
            local m = 0
            for _ in ipairs(proxy) do m = m + 1 end
            print(n, m)
            """));
    }

    [Fact]
    public void Ipairs_values_and_indices_are_delivered_in_order()
    {
        Assert.Equal("1:x 2:y 3:z \n", Run("""
            local out = ""
            for i, v in ipairs({"x", "y", "z"}) do out = out .. i .. ":" .. v .. " " end
            print(out)
            """));
    }

    [Fact]
    public void Iteration_works_the_same_under_a_hook()
    {
        Assert.Equal("6\ttrue\n", Run("""
            local calls = 0
            debug.sethook(function() calls = calls + 1 end, "cr")
            local sum = 0
            for _, v in ipairs({1, 2, 3}) do sum = sum + v end
            for k, v in pairs({7}) do end
            debug.sethook()
            print(sum, calls > 0)
            """));
    }

    [Fact]
    public void Iteration_may_yield_inside_the_loop_body()
    {
        Assert.Equal("1\n2\n3\ndone\n", Run("""
            local co = coroutine.wrap(function()
              for _, v in ipairs({1, 2, 3}) do coroutine.yield(v) end
              return "done"
            end)
            print(co()) print(co()) print(co()) print(co())
            """));
    }
}
