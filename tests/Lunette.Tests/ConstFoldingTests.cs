using Lunette.Compiler;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// A <c>local x &lt;const&gt; = literal</c> is a compile-time constant: it occupies no register, emits no initializing code and is
/// not captured as an upvalue. Observed on the compiled Proto; behavior is covered by GotoCloseConstTests.
/// </summary>
public class ConstFoldingTests
{
    private static Proto Compile(string source) => LuaCompiler.Compile(source, "=test");

    private static OpCode[] Ops(Proto p) => p.Code.Select(i => i.Op).ToArray();

    [Fact]
    public void Const_local_with_a_numeric_literal_takes_no_register_and_loads_at_each_use()
    {
        var p = Compile("local x <const> = 5 return x");
        Assert.Equal([OpCode.LoadK, OpCode.Return, OpCode.Return], Ops(p));
        Assert.Equal(1, p.MaxStack);
        Assert.DoesNotContain(p.LocVars, v => v.Name == "x");
    }

    [Fact]
    public void Const_local_does_not_shift_the_register_of_later_locals()
    {
        var p = Compile("local x <const> = 5 local y = 1 return y");
        Assert.Contains(p.LocVars, v => v.Name == "y" && v.Reg == 0);
    }

    [Fact]
    public void Const_nil_true_and_string_literals_fold()
    {
        Assert.Equal([OpCode.LoadNil, OpCode.Return, OpCode.Return], Ops(Compile("local x <const> = nil return x")));
        Assert.Equal([OpCode.LoadBool, OpCode.Return, OpCode.Return], Ops(Compile("local x <const> = true return x")));
        Assert.Equal([OpCode.LoadK, OpCode.Return, OpCode.Return], Ops(Compile("local x <const> = 'hi' return x")));
    }

    [Fact]
    public void Negated_numeric_literal_folds()
    {
        var p = Compile("local x <const> = -3 return x");
        Assert.Equal([OpCode.LoadK, OpCode.Return, OpCode.Return], Ops(p));
        Assert.Contains(p.Constants, c => c.IsInteger && c.AsInteger == -3);
    }

    [Fact]
    public void Const_local_used_in_a_nested_function_is_not_an_upvalue()
    {
        var p = Compile("local x <const> = 7 return function() return x end");
        Assert.Empty(p.Protos[0].Upvalues);
        Assert.DoesNotContain(OpCode.GetUpval, Ops(p.Protos[0]));
    }

    [Fact]
    public void Const_with_a_non_constant_initializer_still_uses_a_register()
    {
        var p = Compile("local x <const> = f() return x");
        Assert.Contains(p.LocVars, v => v.Name == "x");
    }

    [Fact]
    public void Folded_const_can_be_shadowed_by_a_regular_local()
    {
        Assert.Equal("2\n", ControlFlowTests.Run("local x <const> = 1 do local x = 2 print(x) end"));
    }

    [Fact]
    public void Goto_close_levels_ignore_folded_consts()
    {
        Assert.Equal("1\n2\n", ControlFlowTests.Run("""
            local k <const> = 10
            local a = 1
            do
              local c <const> = 5
              local t <close> = setmetatable({}, {__close = function() print(2) end})
              print(a)
              goto out
            end
            ::out::
            """));
    }
}
