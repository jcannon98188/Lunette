using Lunette.Compiler;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// The generic-for iteration step for next/ipairs runs fused in the untraced loop (ticket 48); results, error text,
/// hook counts and the to-be-closed value are exactly those of the five-instruction sequence.
/// </summary>
public class Ticket48GenericForTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Generic_for_keeps_the_five_instruction_shape_with_a_fusable_first_instruction()
    {
        var p = LuaCompiler.Compile("for k, v in pairs({}) do end", "=test");
        int at = Array.FindIndex(p.Code, i => i.Op == OpCode.TForMove);
        Assert.True(at >= 0);
        Assert.Equal([OpCode.Move, OpCode.Move, OpCode.Call, OpCode.TForLoop], p.Code.Skip(at + 1).Take(4).Select(i => i.Op).ToArray());
    }

    [Fact]
    public void Pairs_visits_array_and_hash_parts()
    {
        Assert.Equal("6\t3.0\t60\n", Run("""
            local t = {10, 20, 30, a = 1, b = 2, c = 3}
            local ks, n, vs = 0, 0, 0
            for k, v in pairs(t) do n = n + 1 if type(k) == 'number' then ks = ks + k vs = vs + v end end
            print(ks, ks / 2, vs)
            """));
    }

    [Fact]
    public void Empty_tables_and_loop_bodies_that_do_nothing_terminate()
    {
        Assert.Equal("x\ty\n", Run("""
            local ipairs, A = ipairs, {}
            local function main() for _, a in ipairs(A) do end for k in pairs(A) do end return 'y' end
            for k in ipairs({}) do end
            print('x', main())
            """));
    }

    [Fact]
    public void Ipairs_stops_at_first_nil_and_follows_index_metamethod()
    {
        Assert.Equal("3\t5\n", Run("""
            local a = 0
            for i, v in ipairs({1, 2, 3, nil, 5}) do a = a + 1 end
            local b = 0
            for i, v in ipairs(setmetatable({}, {__index = function(_, i) if i <= 5 then return i end end})) do b = b + 1 end
            print(a, b)
            """));
    }

    [Fact]
    public void One_and_three_loop_variables()
    {
        Assert.Equal("6\tnil\n", Run("""
            local s = 0
            for k in ipairs({1, 2, 3}) do s = s + k end
            local z
            for a, b, c in pairs({1}) do z = c end
            print(s, z)
            """));
    }

    [Fact]
    public void Assigning_nil_to_existing_fields_during_traversal_is_allowed()
    {
        Assert.Equal("0\n", Run("""
            local t = {}
            for i = 1, 20 do t['k' .. i] = i end
            for k in pairs(t) do t[k] = nil end
            print(next(t) and 1 or 0)
            """));
    }

    [Fact]
    public void Invalid_key_to_next_is_reported()
    {
        Assert.Equal("false\tinvalid key to 'next'\n", Run("""
            local ok, e = pcall(function() for k in next, {}, 'nope' do end end)
            print(ok, (e:gsub('^.-: ', '')))
            """));
    }

    [Fact]
    public void Calling_a_non_function_names_the_for_iterator()
    {
        Assert.Equal("false\tattempt to call a number value (for iterator 'for iterator')\n", Run("""
            local ok, e = pcall(function() for k in 5 do end end)
            print(ok, (e:gsub('^.-:%d+: ', '')))
            """));
    }

    [Fact]
    public void Ipairs_of_a_non_table_errors_like_before()
    {
        Assert.Equal("false\tattempt to index a nil value\n", Run("""
            local ok, e = pcall(function() for k in ipairs(nil) do end end)
            print(ok, (e:gsub('^.-:%d+: ', '')))
            """));
    }

    [Fact]
    public void Ipairs_iterator_called_with_a_non_number_control_errors()
    {
        Assert.Equal("true\n", Run("""
            local f, t = ipairs({1})
            print((pcall(function() for k in f, t, 'x' do end end)))
            """));
    }

    [Fact]
    public void Custom_iterator_may_yield_inside_a_generic_for()
    {
        Assert.Equal("1\n2\n3\ndone\n", Run("""
            local co = coroutine.wrap(function()
              for i in function(_, c) c = c + 1 if c <= 3 then coroutine.yield(c) return c end end, nil, 0 do end
              return 'done'
            end)
            print(co()) print(co()) print(co()) print(co())
            """));
    }

    [Fact]
    public void Next_loop_inside_a_coroutine_with_yields_in_the_body()
    {
        Assert.Equal("3\n", Run("""
            local co = coroutine.wrap(function() local n = 0 for k, v in pairs({1, 2, 3}) do coroutine.yield() n = n + 1 end return n end)
            co() co() co() print(co())
            """));
    }

    [Fact]
    public void Fourth_value_is_closed_on_break_and_normal_exit()
    {
        Assert.Equal("closed 1\nclosed 2\n", Run("""
            local function it(t, c) c = c + 1 if c <= 2 then return c end end
            local function mk(n) return setmetatable({}, {__close = function() print('closed ' .. n) end}) end
            for i in it, nil, 0, mk(1) do end
            for i in it, nil, 0, mk(2) do break end
            """));
    }

    [Fact]
    public void Count_hook_sees_the_same_instruction_counts()
    {
        Assert.Equal("55\n", Run("""
            local n = 0
            local t = {1, 2, 3}
            debug.sethook(function() n = n + 1 end, '', 1)
            for k, v in pairs(t) do end
            for k, v in ipairs(t) do end
            debug.sethook()
            print(n)
            """));
    }

    [Fact]
    public void Call_hook_reports_the_iterator_builtin_and_ntransfer()
    {
        Assert.Equal("call next\nreturn next 2\ncall next\nreturn next 2\ncall next\nreturn next 2\n", Run("""
            local t = {10, 20}
            local log = {}
            debug.sethook(function(e)
              local i = debug.getinfo(2, 'nr')
              if i.name == 'for iterator' or i.name == 'next' then
                log[#log + 1] = e .. ' next' .. (e == 'return' and ' ' .. i.ntransfer or '')
              end
            end, 'cr')
            for k, v in next, t do end
            debug.sethook()
            print(table.concat(log, '\n'))
            """));
    }

    [Fact]
    public void A_hook_set_inside_the_loop_body_takes_effect_for_later_iterations()
    {
        Assert.Equal("7\n", Run("""
            local n = 0
            for k in ipairs({1, 2, 3}) do
              if k == 1 then debug.sethook(function() n = n + 1 end, 'l') end
              if k == 3 then debug.sethook() end
            end
            print(n)
            """));
    }
}
