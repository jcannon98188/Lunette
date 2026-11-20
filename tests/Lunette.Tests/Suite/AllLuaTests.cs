using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Lunette.Tests.Suite;

/// <summary>Runs only when LUNETTE_ALL_LUA=1 (the <c>all-lua</c> CI job): the full upstream driver is minutes long.</summary>
public sealed class AllLuaFactAttribute : FactAttribute
{
    public const string EnvVar = "LUNETTE_ALL_LUA";

    public AllLuaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvVar) != "1") Skip = $"set {EnvVar}=1 to run the whole-suite driver (CI job all-lua)";
    }
}

/// <summary>
/// The upstream driver <c>all.lua</c> in ONE standalone-CLI process: every suite file shares one global state in turn, which
/// catches cross-file state bleed the per-file <see cref="SuiteTests"/> cannot. The suite copy carries the same waiver overlays
/// as the per-file run; whole-file waivers become empty stubs, and all.lua's dump/undump round trip of each file is removed
/// because Lunette has no bytecode format (<c>no-bytecode</c>).
/// </summary>
public class AllLuaTests(ITestOutputHelper log)
{
    private const string DumpRoundTrip = "  local b = string.dump(f, strip)\n  f = assert(load(b))\n";

    [AllLuaFact]
    public void All_lua_passes_in_a_single_cli_process()
    {
        var waivers = SuiteRunner.LoadWaivers();
        string temp = Path.Combine(Path.GetTempPath(), "lunette-all-lua-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(SuiteRunner.SuiteDir, temp);
            Directory.CreateDirectory(Path.Combine(temp, "libs", "P1")); // attrib.lua writes here; the vendored copy cannot ship it empty

            foreach (string file in SuiteRunner.SuiteFiles())
            {
                string path = Path.Combine(temp, file);
                if (waivers.WholeFileWaiver(file) is { } w) { File.WriteAllText(path, "-- waived (" + w.Reason + ")\n", Encoding.Latin1); continue; }
                string source = waivers.ApplyOverlays(file, File.ReadAllText(path, Encoding.Latin1));
                if (file == "all.lua")
                {
                    Assert.Contains(DumpRoundTrip, source.Replace("\r\n", "\n"));
                    source = source.Replace("\r\n", "\n").Replace(DumpRoundTrip, "  -- waived (no-bytecode)\n");
                }
                File.WriteAllText(path, source, Encoding.Latin1);
            }

            var psi = new ProcessStartInfo(SuiteRunner.LauncherPath) { WorkingDirectory = temp, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add("_U=true"); // as upstream's CI runs it: skips soft/non-portable parts
            psi.ArgumentList.Add("all.lua");
            using var proc = Process.Start(psi)!;
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            string? timeout = null;
            if (!proc.WaitForExit(TimeSpan.FromMinutes(20)))
            {
                proc.Kill(entireProcessTree: true);
                timeout = "timed out after 20 minutes";
            }
            proc.WaitForExit();
            string tail = stdout.Length > 4000 ? stdout.ToString(stdout.Length - 4000, 4000) : stdout.ToString();
            bool passed = timeout is null && proc.ExitCode == 0;
            string detail = timeout ?? $"exited {proc.ExitCode}{Environment.NewLine}stderr: {stderr}{Environment.NewLine}stdout tail: {tail}";
            log.WriteLine($"all.lua: {(passed ? "passed" : "failed")}{Environment.NewLine}{(passed ? tail : detail)}");
            // Same ratchet as the per-file run: all.lua is listed in required-files.txt, so a failure here is blocking.
            var result = new SuiteResult("all.lua", passed ? SuiteStatus.Passed : SuiteStatus.Failed, 0, detail);
            Assert.False(RequiredFiles.Violates(SuiteRunner.LoadRequiredFiles(), result), $"required file all.lua did not pass: {detail}");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
