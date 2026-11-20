using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Dynamic half of the interpreter-loop JIT budget (spec ticket 17, decision 4), run in a fresh child process so
/// the loop is jitted under observation: (1) an in-process EventListener (in the probe) must never see a
/// [LuaInterpreterLoop] method jitted as MinOptJitted; (2) a scoped DOTNET_JitDisasm of the loop must not
/// contain calls to helpers meant to be inlined (the silent inlining cliff at 921 locals), beyond the recorded
/// baseline in jit-budgets.json. Both need the plain JIT path (R2R and NativeAOT skip these thresholds).
/// </summary>
// Not part of the LUA_SAFE_ACCESS variant: the baselines in jit-budgets.json describe the unsafe helpers; the bounds-checked twin
// legitimately leaves LuaValue.FromFloat / IsFalsy calls in the loop. (Budgets are default-build gates only.)
#if !LUA_SAFE_ACCESS
public class JitProbeTests
{
    private sealed record ProbeRun(string Stdout, string DisasmFile);

    private static ProbeRun RunProbe(string[] methods, (string Key, string Value)? extraEnv = null)
    {
        string probe = Path.Combine(AppContext.BaseDirectory, "Lunette.JitProbe.dll");
        string disasm = Path.Combine(Path.GetTempPath(), $"lunette-jit-{Guid.NewGuid():N}.txt");
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(probe);
        foreach (string m in methods) psi.ArgumentList.Add(m);
        psi.Environment["DOTNET_JitStdOutFile"] = disasm;
        psi.Environment["DOTNET_JitDisasmSummary"] = "1";
        psi.Environment["DOTNET_JitDisasm"] = string.Join(' ', methods.Select(m => m[(m.LastIndexOf('.') + 1)..]));
        // Plain JIT path: no ReadyToRun images, default tiering (AggressiveOptimization must still go straight to FullOpts).
        psi.Environment["DOTNET_ReadyToRun"] = "0";
        psi.Environment.Remove("DOTNET_TieredCompilation");
        if (extraEnv is { } e) psi.Environment[e.Key] = e.Value;
        using Process p = Process.Start(psi)!;
        string err = p.StandardError.ReadToEnd();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"probe failed ({p.ExitCode}): {err}\n{output}");
        return new ProbeRun(output, disasm);
    }

    private static string[] LoopNames() =>
        JitBudgetTests.LoopMethods().Select(m => $"{m.DeclaringType!.FullName}.{m.Name}").ToArray();

    [Fact]
    public void Interpreter_loop_is_never_jitted_as_minopts()
    {
        AllocationTests.RequireOptimized();
        if (Optimizer.Disabled) return;
        string[] names = LoopNames();
        ProbeRun run = RunProbe(names);
        string[] lines = run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("DONE", lines);
        foreach (string name in names)
        {
            string[] jits = lines.Where(l => l.StartsWith($"JIT {name} ", StringComparison.Ordinal)).ToArray();
            Assert.True(jits.Length > 0, $"no MethodLoadVerbose event seen for {name}; the listener is not observing the JIT");
            Assert.DoesNotContain(jits, l => l.Contains("MinOptJitted", StringComparison.Ordinal));
        }
        // JitStdOutFile summary agrees: a MinOpts fallback is printed as "switched MinOpts" (or "MinOpts" tier).
        string summary = File.ReadAllText(run.DisasmFile);
        foreach (string name in names)
        {
            string method = name[(name.LastIndexOf('.') + 1)..];
            foreach (string l in summary.Split('\n').Where(l => l.Contains("JIT compiled", StringComparison.Ordinal) && l.Contains($":{method}(", StringComparison.Ordinal)))
                Assert.DoesNotContain("MinOpts", l);
        }
    }

    [Fact]
    public void The_probe_reports_a_forced_minopts_downgrade()
    {
        // Proves the check can fail: DOTNET_JITMinOpts=1 forces every method to MinOpts, and the probe must say so.
        ProbeRun run = RunProbe(LoopNames(), ("DOTNET_JITMinOpts", "1"));
        Assert.Contains("MinOptJitted", run.Stdout);
        Assert.Contains("MinOpts", File.ReadAllText(run.DisasmFile));
    }

    /// <summary>The machine-code listing of one generic instantiation of the loop, cut out of the JitDisasm output.</summary>
    private static string Listing(string disasm, string typeArgument)
    {
        string header = $"Assembly listing for method Lunette.Vm.Interpreter:RunLoop[Lunette.Vm.{typeArgument}]";
        int start = disasm.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no JIT listing for RunLoop<{typeArgument}>; the probe did not run that loop");
        int next = disasm.IndexOf("; Assembly listing for method", start + header.Length, StringComparison.Ordinal);
        return next < 0 ? disasm[start..] : disasm[start..next];
    }

    [Fact]
    public void Fast_loop_variant_contains_no_hook_checks_while_the_traced_variant_does()
    {
        AllocationTests.RequireOptimized();
        if (Optimizer.Disabled) return;
        // LUNETTE_PROBE_TRACED makes the probe also run under debug.sethook, so both instantiations get jitted.
        ProbeRun run = RunProbe(LoopNames(), ("LUNETTE_PROBE_TRACED", "1"));
        string disasm = File.ReadAllText(run.DisasmFile);
        var hookCall = new Regex(@"\bcall\s+\[Lunette\.Vm\.Interpreter:(Trace\w+|FireHook)\(");

        // The traced loop must show the pattern, or the fast-loop check below could never fail.
        Assert.Matches(hookCall, Listing(disasm, "TracedLoop"));
        Assert.DoesNotMatch(hookCall, Listing(disasm, "FastLoop"));
    }

    [Fact]
    public void Interpreter_loop_has_no_new_unreinlined_helper_calls()
    {
        AllocationTests.RequireOptimized();
        if (Optimizer.Disabled) return;
        ProbeRun run = RunProbe(LoopNames());
        string text = File.ReadAllText(run.DisasmFile);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "jit-budgets.json")));
        string[] prefixes = doc.RootElement.GetProperty("inlineIntendedPrefixes").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var baseline = doc.RootElement.GetProperty("inlineLeftovers").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());

        var found = new Dictionary<string, int>();
        foreach (Match m in Regex.Matches(text, @"\bcall\s+\[(?<target>[^\]\(]+)\("))
        {
            string target = m.Groups["target"].Value; // e.g. Lunette.LuaValue:FromBoolean
            if (prefixes.Any(p => target.StartsWith(p, StringComparison.Ordinal)))
                found[target] = found.GetValueOrDefault(target) + 1;
        }

        var failures = found.Where(kv => kv.Value > baseline.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key} called {kv.Value}x in the loop (baseline {baseline.GetValueOrDefault(kv.Key)}): the JIT stopped inlining it");
        Assert.True(!failures.Any(), string.Join("; ", failures));

        // Ratchet down: a stale baseline entry hides a regression back to the old count.
        var stale = baseline.Where(kv => found.GetValueOrDefault(kv.Key) < kv.Value).Select(kv => $"{kv.Key} now {found.GetValueOrDefault(kv.Key)} < baseline {kv.Value}");
        Assert.True(!stale.Any(), "lower inlineLeftovers in jit-budgets.json: " + string.Join("; ", stale));
    }
}
#endif
