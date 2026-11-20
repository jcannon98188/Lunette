using BenchmarkDotNet.Attributes;
using Lunette;

namespace Lunette.Benchmarks;

/// <summary>
/// Many-small-states cost: one state per request / entity / test. Counterpart of bench/states-puc.c (luaL_newstate + luaL_openlibs
/// + run a tiny chunk + lua_close). OperationsPerInvoke batches states so the per-state time is not dominated by harness overhead.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(InProcessConfig))]
public class StateBenchmarks
{
    private const int Ops = 200;
    private const string Tiny = "local t = {} for i = 1, 10 do t[i] = i end return #t";

    /// <summary>Create (standard libraries open) and dispose, nothing run.</summary>
    [Benchmark(OperationsPerInvoke = Ops)]
    public int CreateDispose()
    {
        int n = 0;
        for (int i = 0; i < Ops; i++)
        {
            using var s = new LuaState();
            n++;
        }
        return n;
    }

    /// <summary>Create, compile and run a tiny chunk, dispose.</summary>
    [Benchmark(OperationsPerInvoke = Ops)]
    public int CreateRunDispose()
    {
        int n = 0;
        for (int i = 0; i < Ops; i++)
        {
            using var s = new LuaState();
            s.DoString(Tiny);
            n++;
        }
        return n;
    }

    /// <summary>Create, fill 1000 table entries and 200 strings (a small but non-empty state), dispose.</summary>
    [Benchmark(OperationsPerInvoke = Ops)]
    public int CreateFillDispose()
    {
        int n = 0;
        for (int i = 0; i < Ops; i++)
        {
            using var s = new LuaState();
            s.DoString("local t = {} for i = 1, 1000 do t[i] = i end for i = 1, 200 do t['k' .. i] = 'v' .. i end");
            n++;
        }
        return n;
    }
}
