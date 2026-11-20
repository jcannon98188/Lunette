using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Embedding tests. The seam is the public host API only: Load, Call, host functions, globals, yields, limits.
/// Nothing here touches engine internals.
/// </summary>
public class HostApiTests
{
    [Fact]
    public void Load_returns_a_function_that_can_be_called_for_its_results()
    {
        var state = new LuaState();
        LuaFunction chunk = state.Load("return 1 + 2, 'x'");
        LuaValue[] results = state.Call(chunk, ReadOnlySpan<LuaValue>.Empty);
        Assert.Equal(2, results.Length);
        Assert.True(results[0].TryGetInteger(out long n));
        Assert.Equal(3, n);
        Assert.Equal("x", results[1].ToString());
    }

    [Fact]
    public void Load_reports_syntax_errors_as_a_lua_error_with_the_chunk_name()
    {
        var state = new LuaState();
        var e = Assert.Throws<LuaException>(() => state.Load("return +", "=mychunk"));
        Assert.StartsWith("mychunk:1:", e.Message);
    }

    [Fact]
    public void Globals_round_trip_between_host_and_script()
    {
        var state = new LuaState();
        state.SetGlobal("answer", 42);
        state.Call(state.Load("doubled = answer * 2"));
        Assert.True(state.GetGlobal("doubled").TryGetInteger(out long n));
        Assert.Equal(84, n);
        Assert.True(state.GetGlobal("missing").IsNil);
    }

    [Fact]
    public void Call_overloads_pass_up_to_four_arguments_and_return_the_first_result()
    {
        var state = new LuaState();
        LuaFunction sum = state.Load("local a, b, c, d = ... return (a or 0) + (b or 0) + (c or 0) + (d or 0), 'ignored'");
        Assert.Equal(0, state.Call(sum).AsInteger);
        Assert.Equal(1, state.Call(sum, 1).AsInteger);
        Assert.Equal(3, state.Call(sum, 1, 2).AsInteger);
        Assert.Equal(6, state.Call(sum, 1, 2, 3).AsInteger);
        Assert.Equal(10, state.Call(sum, 1, 2, 3, 4).AsInteger);
    }

    [Fact]
    public void A_lua_error_thrown_by_a_called_function_surfaces_as_LuaException()
    {
        var state = new LuaState();
        LuaFunction boom = state.Load("error('boom', 0)");
        var e = Assert.Throws<LuaException>(() => state.Call(boom));
        Assert.Equal("boom", e.Message);
        // the state stays usable
        Assert.Equal(7, state.Call(state.Load("return 7")).AsInteger);
    }

    [Fact]
    public void A_host_function_reads_span_arguments_and_pushes_results()
    {
        var state = new LuaState();
        state.Register("sumAndCount", (ref CallContext ctx) =>
        {
            long sum = 0;
            for (int i = 0; i < ctx.Args.Length; i++) sum += ctx.Args[i].AsInteger;
            ctx.Push(sum);
            ctx.Push(ctx.Args.Length);
            return 2;
        });
        LuaValue[] r = state.Call(state.Load("return sumAndCount(10, 20, 30)"), ReadOnlySpan<LuaValue>.Empty);
        Assert.Equal(60, r[0].AsInteger);
        Assert.Equal(3, r[1].AsInteger);
    }

    [Fact]
    public void A_host_function_can_call_back_into_lua()
    {
        var state = new LuaState();
        state.Register("twice", (ref CallContext ctx) =>
        {
            LuaFunction f = ctx.Args[0].TryGet(out LuaFunction? fn) ? fn! : throw ctx.Error("function expected");
            LuaValue once = ctx.State.Call(f, ctx.Args[1]);
            ctx.Push(ctx.State.Call(f, once));
            return 1;
        });
        Assert.Equal(40, state.Call(state.Load("return twice(function(x) return x * 2 end, 10)")).AsInteger);
    }

    [Fact]
    public void A_host_function_error_gets_a_script_position_and_is_catchable_by_pcall()
    {
        var state = new LuaState();
        state.Register("fail", (ref CallContext ctx) => throw ctx.Error("nope"));
        LuaValue[] r = state.Call(state.Load("return pcall(fail)"), ReadOnlySpan<LuaValue>.Empty);
        Assert.True(r[0].IsBoolean && r[0].IsFalsy);
        Assert.Contains("nope", r[1].ToString());
    }

    [Fact]
    public void Typed_wrappers_convert_arguments_and_results()
    {
        var state = new LuaState();
        state.Register("half", (double x) => x / 2);
        state.Register("shout", (string s) => s.ToUpperInvariant() + "!");
        Assert.Equal(2.5, state.Call(state.Load("return half(5)")).AsFloat);
        Assert.Equal("HÉLLO!", state.Call(state.Load("return shout('héllo')")).ToString());
        var e = Assert.Throws<LuaException>(() => state.Call(state.Load("return half('x')")));
        Assert.Contains("bad argument #1 to 'half'", e.Message);
    }
}
