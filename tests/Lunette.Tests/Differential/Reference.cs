using System.Diagnostics;
using System.Text;
using Lunette.Tests.Suite;
using Xunit;

namespace Lunette.Tests.Differential;

public sealed record RunOutput(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Locates the Reference binary (PUC Lua 5.4.9, built by ci/build-reference.sh) and runs both it and the
/// engine's CLI as separate processes. Differential tests skip when there is no Reference binary, unless
/// LUNETTE_REQUIRE_REFERENCE=1 (set in CI), in which case a missing binary is a failure.
/// </summary>
public static class Reference
{
    public static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    /// <summary>LUNETTE_REFERENCE_LUA, else .reference/lua under the repo (non-Windows only: the binary is a Linux build).</summary>
    public static string? Locate()
    {
        string? env = Environment.GetEnvironmentVariable("LUNETTE_REFERENCE_LUA");
        if (!string.IsNullOrEmpty(env)) return File.Exists(env) ? env : null;
        if (OperatingSystem.IsWindows()) return null;
        string fallback = Path.Combine(SuiteRunner.RepoRoot, ".reference", "lua");
        return File.Exists(fallback) ? fallback : null;
    }

    public static bool Required => Environment.GetEnvironmentVariable("LUNETTE_REQUIRE_REFERENCE") == "1";

    /// <summary>Non-null skips the test.</summary>
    public static string? SkipReason =>
        Locate() is not null || Required
            ? null
            : "no Reference binary (set LUNETTE_REFERENCE_LUA, or run ci/build-reference.sh on Linux)";

    public static string ReferencePath =>
        Locate() ?? throw new InvalidOperationException("LUNETTE_REQUIRE_REFERENCE=1 but no Reference binary was found");

    public static RunOutput RunReference(string cwd, params string[] args) => Run(ReferencePath, [], cwd, args);

    public static RunOutput RunEngine(string cwd, params string[] args)
    {
        string cli = System.IO.Path.Combine(AppContext.BaseDirectory, "Lunette.Cli.dll");
        return Run("dotnet", [cli], cwd, args);
    }

    private static RunOutput Run(string file, string[] prefix, string cwd, string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.Latin1,
            StandardErrorEncoding = Encoding.Latin1,
        };
        foreach (string a in prefix) psi.ArgumentList.Add(a);
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"cannot start {file}");
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(RunTimeout))
        {
            p.Kill(entireProcessTree: true);
            return new RunOutput(-1, "", $"timed out after {RunTimeout}");
        }
        return new RunOutput(p.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    /// <summary>Null when the runs agree under the normalisation rules; otherwise a readable description.</summary>
    public static string? Diverges(RunOutput theirs, RunOutput ours, Func<string, string>? extra = null)
    {
        extra ??= s => s;
        var problems = new List<string>();
        if (theirs.ExitCode != ours.ExitCode)
            problems.Add($"exit code: reference {theirs.ExitCode}, engine {ours.ExitCode}");
        string to = extra(DiffNormalizer.Stdout(theirs.Stdout)), oo = extra(DiffNormalizer.Stdout(ours.Stdout));
        if (to != oo) problems.Add($"stdout differs:\n{FirstDifference(to, oo)}");
        string te = extra(DiffNormalizer.Stderr(theirs.Stderr)), oe = extra(DiffNormalizer.Stderr(ours.Stderr));
        if (te != oe) problems.Add($"stderr differs:\n{FirstDifference(te, oe)}");
        return problems.Count == 0 ? null : string.Join("\n", problems);
    }

    private static string FirstDifference(string reference, string engine)
    {
        string[] a = reference.Split('\n'), b = engine.Split('\n');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string x = i < a.Length ? a[i] : "<end>", y = i < b.Length ? b[i] : "<end>";
            if (x != y) return $"  line {i + 1}\n    reference: {x}\n    engine:    {y}";
        }
        return "  (no line difference)";
    }
}

public sealed class ReferenceFactAttribute : FactAttribute
{
    public ReferenceFactAttribute() => Skip = Reference.SkipReason;
}

public sealed class ReferenceTheoryAttribute : TheoryAttribute
{
    public ReferenceTheoryAttribute() => Skip = Reference.SkipReason;
}
