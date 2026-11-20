using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Lunette;

namespace Lunette.Benchmarks;

/// <summary>
/// In-process toolchain: BenchmarkDotNet's generated-project toolchain does not recognise the preview target framework,
/// and the in-process one also keeps scheduled runs fast. (Disassembly is the profiling runbook's job, via DOTNET_JitDisasm.)
/// </summary>
public sealed class InProcessConfig : ManualConfig
{
    public InProcessConfig()
    {
        AddJob(Job.Default.WithToolchain(InProcessNoEmitToolchain.Instance).WithLaunchCount(1).WithWarmupCount(4).WithIterationCount(12));
    }
}

/// <summary>One benchmark per bench/micro/*.lua snippet: compile once in setup, then time running the chunk.</summary>
[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[Config(typeof(InProcessConfig))]
public class SnippetBenchmarks
{
    private LuaState _state = null!;
    private LuaFunction _chunk = null!;

    public static IEnumerable<string> Snippets() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "micro"), "*.lua")
            .Select(p => Path.GetFileNameWithoutExtension(p)!)
            .OrderBy(n => n, StringComparer.Ordinal);

    [ParamsSource(nameof(Snippets))]
    public string Snippet { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _state = new LuaState();
        string path = Path.Combine(AppContext.BaseDirectory, "micro", Snippet + ".lua");
        _chunk = _state.Load(File.ReadAllText(path), "@" + Snippet);
    }

    [GlobalCleanup]
    public void Cleanup() => _state.Dispose();

    [Benchmark]
    public LuaValue Run() => _state.Call(_chunk);
}

public static class Program
{
    // Usage: dotnet run -c Release --project bench/Lunette.Benchmarks -- [BenchmarkDotNet args, e.g. --filter "*call-lua*" --artifacts <dir>]
    public static void Main(string[] args)
    {
        string[] effective = args.Length == 0 ? ["--filter", "*"] : args;
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(effective);
    }
}
