using System.Diagnostics.Tracing;
using Lunette;

// Usage: Lunette.JitProbe <Namespace.Type.Method>...
// Runs a Lua workload that exercises the interpreter loop, then prints one line per observed jit of each named
// method: "JIT <Namespace.Type.Method> tier=<MethodFlags tier name>". The test asserts no line says MinOptJitted.
var wanted = new HashSet<string>(args);
using var listener = new JitListener(wanted);

var state = new LuaState { Output = TextWriter.Null };
state.DoString("""
    local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end
    local t = {}
    local s = 0
    for round = 1, 20 do
        for i = 1, 5000 do t[i % 64 + 1] = i; s = s + i % 7 + fib(3) end
        pcall(error, "x")
    end
    return s
    """);

// LUNETTE_PROBE_TRACED: also run under a hook so the traced instantiation of the loop gets jitted (the hook-check test).
if (Environment.GetEnvironmentVariable("LUNETTE_PROBE_TRACED") == "1")
{
    state.DoString("""
        local n = 0
        debug.sethook(function() n = n + 1 end, "l")
        local function f(x) return x + 1 end
        for i = 1, 1000 do f(i) end
        debug.sethook()
        """);
}

// EventListener delivery from the runtime is asynchronous; give it time to drain.
Thread.Sleep(1500);
foreach (string line in listener.Lines) Console.WriteLine(line);
Console.WriteLine("DONE");

internal sealed class JitListener(HashSet<string> wanted) : EventListener
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines { get { lock (_lines) return _lines.ToArray(); } }

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x10); // JitKeyword: MethodLoadVerbose
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName is null || !e.EventName.StartsWith("MethodLoadVerbose", StringComparison.Ordinal) || e.PayloadNames is null) return;
        string ns = (string)e.Payload![e.PayloadNames.IndexOf("MethodNamespace")]!;
        string name = (string)e.Payload[e.PayloadNames.IndexOf("MethodName")]!;
        uint flags = Convert.ToUInt32(e.Payload[e.PayloadNames.IndexOf("MethodFlags")]);
        string full = $"{ns}.{name}";
        if (!wanted.Contains(full)) return;
        int tier = (int)((flags >> 7) & 0x7);
        string tierName = tier switch
        {
            0 => "Unknown", 1 => "MinOptJitted", 2 => "Optimized", 3 => "QuickJitted",
            4 => "OptimizedTier1", 5 => "OptimizedTier1OSR", _ => "Tier" + tier,
        };
        lock (_lines) _lines.Add($"JIT {full} tier={tierName}");
    }
}
