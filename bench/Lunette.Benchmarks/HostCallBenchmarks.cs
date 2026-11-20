using BenchmarkDotNet.Attributes;
using Lunette;

namespace Lunette.Benchmarks;

/// <summary>
/// Embedder per-frame pattern: the host calls a Lua function repeatedly with a few arguments and reads its results.
/// Counterpart of bench/embed-puc.c (same three function shapes, called from C with lua_call).
/// OperationsPerInvoke batches calls so the per-call time is not dominated by harness overhead.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(InProcessConfig))]
public class HostCallBenchmarks
{
    private const int Ops = 1000;
    private LuaState _state = null!;
    private LuaFunction _f0 = null!, _f3r1 = null!, _f3r3 = null!;
    private readonly LuaValue[] _args = new LuaValue[3];

    [GlobalSetup]
    public void Setup()
    {
        _state = new LuaState();
        _f0 = Fn(_state.Load("return function() return 1 end"));
        _f3r1 = Fn(_state.Load("return function(a, b, c) return a + b end"));
        _f3r3 = Fn(_state.Load("return function(a, b, c) return a + b, a * b, c end"));
    }

    private LuaFunction Fn(LuaFunction chunk) { _state.Call(chunk).TryGet<LuaFunction>(out var f); return f!; }

    [GlobalCleanup]
    public void Cleanup() => _state.Dispose();

    /// <summary>Allocation-free overload, no arguments, first result.</summary>
    [Benchmark(OperationsPerInvoke = Ops)]
    public long Call0_First()
    {
        long s = 0;
        for (int i = 0; i < Ops; i++) s += _state.Call(_f0).AsInteger;
        return s;
    }

    /// <summary>Allocation-free overload, three arguments, first result.</summary>
    [Benchmark(OperationsPerInvoke = Ops)]
    public long Call3_First()
    {
        long s = 0;
        for (int i = 0; i < Ops; i++) s += _state.Call(_f3r1, i, i + 1, i + 2).AsInteger;
        return s;
    }

    /// <summary>Span overload, three arguments, one result: returns a LuaValue[] (CallValues).</summary>
    [Benchmark(OperationsPerInvoke = Ops)]
    public long Call3_Span_Ret1()
    {
        long s = 0;
        var a = _args;
        for (int i = 0; i < Ops; i++)
        {
            a[0] = i; a[1] = i + 1; a[2] = i + 2;
            s += _state.Call(_f3r1, a)[0].AsInteger;
        }
        return s;
    }

    /// <summary>Span overload, three arguments, three results.</summary>
    [Benchmark(OperationsPerInvoke = Ops)]
    public long Call3_Span_Ret3()
    {
        long s = 0;
        var a = _args;
        for (int i = 0; i < Ops; i++)
        {
            a[0] = i; a[1] = i + 1; a[2] = i + 2;
            var r = _state.Call(_f3r3, a);
            s += r[0].AsInteger + r[2].AsInteger;
        }
        return s;
    }
}
