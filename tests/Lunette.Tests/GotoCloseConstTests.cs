using Xunit;

namespace Lunette.Tests;

/// <summary>goto and labels, const locals and to-be-closed variables, observed through Lua source only.</summary>
public class GotoCloseConstTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    private static string CompileError(string source) =>
        Assert.Throws<LuaException>(() => Run(source)).Message;

    // ---- goto ----

    [Fact]
    public void Goto_continue_skips_to_the_end_of_the_loop_body() =>
        Assert.Equal("1\n3\n", Run("for i = 1, 3 do if i == 2 then goto continue end print(i) ::continue:: end"));

    [Fact]
    public void Goto_backward_builds_a_loop() =>
        Assert.Equal("1\n2\n3\n", Run("local i = 1 ::top:: if i <= 3 then print(i) i = i + 1 goto top end"));

    [Fact]
    public void Goto_leaves_nested_loops() =>
        Assert.Equal("1\t1\ndone\n", Run("""
            for i = 1, 3 do for j = 1, 3 do if j == 2 then goto out end print(i, j) end end
            ::out:: print('done')
            """));

    [Fact]
    public void Goto_forward_and_backward_chain() =>
        Assert.Equal("13\n", Run("""
            local x
            do
              local y = 12
              goto l1
              ::l2:: x = x + 1 goto l3
              ::l1:: x = y goto l2
            end
            ::l3:: print(x)
            """));

    [Fact]
    public void Backward_goto_gives_each_pass_a_fresh_captured_local() =>
        Assert.Equal("1\t2\t3\n", Run("""
            local fs = {}
            do
              local i = 1
              ::top::
              local x = i
              fs[#fs + 1] = function() return x end
              i = i + 1
              if i <= 3 then goto top end
            end
            print(fs[1](), fs[2](), fs[3]())
            """));

    [Fact]
    public void Goto_may_jump_over_a_local_to_the_end_of_its_block() =>
        Assert.Equal("ok\n", Run("do goto l1 local a = 23 ::l1:: ; end print('ok')"));

    [Fact]
    public void Same_label_name_is_fine_in_sibling_blocks_and_functions() =>
        Assert.Equal("a\nb\nc\n", Run("""
            do goto l print('x') ::l:: print('a') end
            do goto l print('y') ::l:: print('b') end
            local function f() goto l print('z') ::l:: print('c') end f()
            """));

    [Fact]
    public void Goto_without_a_visible_label_is_a_compile_error()
    {
        Assert.Contains("no visible label 'l1' for <goto> at line 1", CompileError("goto l1"));
        Assert.Contains("no visible label 'l1'", CompileError("goto l1; do ::l1:: end"));
        Assert.Contains("no visible label 'l1'", CompileError("do ::l1:: end goto l1"));
        Assert.Contains("no visible label 'l1'", CompileError("local function f() ::l1:: end goto l1"));
    }

    [Fact]
    public void Repeated_label_is_a_compile_error()
    {
        Assert.Contains("label 'l1' already defined on line 1", CompileError("::l1:: ::l1::"));
        Assert.Contains("label 'l1' already defined", CompileError("::l1:: do ::l1:: end"));
    }

    [Fact]
    public void Goto_into_the_scope_of_a_local_is_a_compile_error()
    {
        Assert.Contains("jumps into the scope of local 'aa'", CompileError("goto l1; local aa ::l1:: ::l2:: print(3)"));
        Assert.Contains("jumps into the scope of local 'aa'", CompileError("do local bb, cc; goto l1; end local aa ::l1:: print(3)"));
        Assert.Contains("jumps into the scope of local 'xuxu'", CompileError("""
            repeat
              if x then goto cont end
              local xuxu = 10
              ::cont::
            until xuxu < x
            """));
    }

    // ---- const ----

    [Fact]
    public void Const_local_reads_like_any_local() =>
        Assert.Equal("5\t1\t3\n", Run("local x <const> = 5 local a <const>, b = 1, 2 b = 3 print(x, a, b)"));

    [Fact]
    public void Assigning_to_a_const_is_a_compile_error()
    {
        Assert.Contains("attempt to assign to const variable 'x'", CompileError("local x <const> = 1 x = 2"));
        Assert.Contains("attempt to assign to const variable 'x'", CompileError("local x <const> = 1 local function f() x = 2 end"));
        Assert.Contains("attempt to assign to const variable 'x'", CompileError("local x <const> = 1 function x() end"));
        Assert.Contains("attempt to assign to const variable 'x'", CompileError("local x <close> = nil x = 2"));
    }

    [Fact]
    public void Const_captured_by_a_closure_reads_through()
    {
        Assert.Equal("7\n", Run("local x <const> = 7 local function f() return x end print(f())"));
    }

    [Fact]
    public void Unknown_attribute_is_a_compile_error() =>
        Assert.Contains("unknown attribute 'foo'", CompileError("local x <foo> = 1"));

    // ---- to-be-closed ----

    private const string Mk =
        "local function mk(n) return setmetatable({}, {__close = function(o, e) print('close', n, e) end}) end\n";

    [Fact]
    public void Close_runs_in_reverse_order_at_block_exit() =>
        Assert.Equal("body\nclose\t2\tnil\nclose\t1\tnil\nafter\n", Run(Mk + """
            do local a <close> = mk(1) local b <close> = mk(2) print('body') end
            print('after')
            """));

    [Fact]
    public void Close_runs_on_break_and_goto()
    {
        Assert.Equal("close\t1\tnil\nclose\t2\tnil\nend\n", Run(Mk + """
            while true do local a <close> = mk(1) break end
            do local b <close> = mk(2) goto out end
            ::out:: print('end')
            """));
    }

    [Fact]
    public void Close_runs_on_return_after_results_are_computed() =>
        Assert.Equal("close\t1\tnil\nr\t5\n", Run(Mk + """
            local function f() local a <close> = mk(1) return 'r', 2 + 3 end
            print(f())
            """));

    [Fact]
    public void Return_of_a_call_inside_a_close_scope_is_not_a_tail_call() =>
        Assert.Equal("inner\nclose\t1\tnil\n9\n", Run(Mk + """
            local function g() print('inner') return 9 end
            local function f() local a <close> = mk(1) return g() end
            print(f())
            """));

    [Fact]
    public void Close_runs_with_the_error_when_a_protected_call_unwinds() =>
        Assert.Equal("close\t1\t(load):3: boom\nfalse\t(load):3: boom\n", Run(Mk + """
            print(pcall(function() local a <close> = mk(1)
              error('boom') end))
            """));

    [Fact]
    public void Error_in_a_close_handler_replaces_the_error_and_the_rest_still_close() =>
        Assert.Equal("close\touter\tnew\nfalse\tnew\n", Run("""
            local function bad() return setmetatable({}, {__close = function() error('new', 0) end}) end
            local function good() return setmetatable({}, {__close = function(o, e) print('close', 'outer', e) end}) end
            print(pcall(function()
              local o <close> = good()
              local b <close> = bad()
              error('old', 0)
            end))
            """));

    [Fact]
    public void Nil_and_false_are_accepted_but_other_non_closable_values_are_not()
    {
        Assert.Equal("ok\n", Run("do local a <close> = nil local b <close> = false end print('ok')"));
        Assert.Contains("variable 'x' got a non-closable value", Assert.Throws<LuaException>(() => Run("local x <close> = {}")).Message);
    }

    [Fact]
    public void Multiple_close_variables_in_one_list_is_a_compile_error() =>
        Assert.Contains("multiple to-be-closed variables in local list", CompileError("local a <close>, b <close> = nil, nil"));

    [Fact]
    public void Generic_for_closes_its_fourth_value_on_exit_and_break() =>
        Assert.Equal("1\n2\n3\nclose\tfor\tnil\n1\nclose\tbrk\tnil\n", Run(Mk + """
            local function iter(s, c) if c < 3 then return c + 1 end end
            for i in iter, nil, 0, mk('for') do print(i) end
            for i in iter, nil, 0, mk('brk') do print(i) break end
            """));

    [Fact]
    public void Close_handler_value_is_the_variable_itself() =>
        Assert.Equal("true\n", Run("""
            local t = setmetatable({}, {__close = function(o) print(o == t0) end})
            t0 = t
            do local x <close> = t end
            """));
}
