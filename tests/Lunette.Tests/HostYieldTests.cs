using Xunit;

namespace Lunette.Tests;

/// <summary>Host functions yield only by returning an explicit yield request, with an optional continuation.</summary>
public class HostYieldTests
{
    private static string Run(LuaState state, string source)
    {
        var sw = new StringWriter { NewLine = "\n" };
        state.Output = sw;
        state.Call(state.Load(source));
        return sw.ToString();
    }

    [Fact]
    public void Without_a_continuation_the_resume_values_become_the_results()
    {
        var state = new LuaState();
        state.Register("hostYield", (ref CallContext ctx) =>
        {
            ctx.Push("from host");
            return ctx.Yield();
        });
        Assert.Equal("true\tfrom host\ntrue\ta\tb\n", Run(state, """
            local co = coroutine.create(function()
              local x, y = hostYield()
              return x, y
            end)
            print(coroutine.resume(co))
            print(coroutine.resume(co, 'a', 'b'))
            """));
    }

    [Fact]
    public void A_continuation_receives_the_resume_values_and_computes_the_results()
    {
        var state = new LuaState();
        state.Register("ask", (ref CallContext ctx) =>
        {
            ctx.Push("question");
            return ctx.Yield((ref CallContext k) =>
            {
                long sum = 0;
                for (int i = 0; i < k.Args.Length; i++) sum += k.Args[i].AsInteger;
                k.Push(sum);
                return 1;
            });
        });
        Assert.Equal("true\tquestion\ntrue\t60\n", Run(state, """
            local co = coroutine.create(function() local total = ask() return total end)
            print(coroutine.resume(co))
            print(coroutine.resume(co, 10, 20, 30))
            """));
    }

    [Fact]
    public void A_continuation_can_yield_again()
    {
        var state = new LuaState();
        state.Register("twoStep", (ref CallContext ctx) =>
        {
            ctx.Push("one");
            return ctx.Yield((ref CallContext k) =>
            {
                k.Push("two");
                k.Push(k.Args[0]);
                return k.Yield((ref CallContext last) =>
                {
                    last.Push("finished");
                    return 1;
                });
            });
        });
        Assert.Equal("true\tone\ntrue\ttwo\tX\ntrue\tfinished\n", Run(state, """
            local co = coroutine.create(function() return twoStep() end)
            print(coroutine.resume(co))
            print(coroutine.resume(co, 'X'))
            print(coroutine.resume(co))
            """));
    }

    [Fact]
    public void Yielding_from_the_main_thread_is_a_lua_error()
    {
        var state = new LuaState();
        state.Register("hostYield", (ref CallContext ctx) => ctx.Yield());
        var e = Assert.Throws<LuaException>(() => state.Call(state.Load("hostYield()")));
        Assert.Contains("outside a coroutine", e.Message);
    }

    [Fact]
    public void Yielding_through_a_non_yieldable_boundary_is_a_lua_error_not_a_crash()
    {
        var state = new LuaState();
        state.Register("hostYield", (ref CallContext ctx) => ctx.Yield());
        // Under a pcall in the main thread the host function now runs yieldable, so the error is the main thread's.
        Assert.Equal("false\tattempt to yield from outside a coroutine\n", Run(state, "print(pcall(hostYield))"));
        // table.sort calls its comparator on a nested loop: inside a coroutine that is a real C-call boundary.
        Assert.Equal("false\tattempt to yield across a C-call boundary\n", Run(state, """
            print(coroutine.wrap(function() return pcall(table.sort, {3, 2, 1}, hostYield) end)())
            """));
    }
}
