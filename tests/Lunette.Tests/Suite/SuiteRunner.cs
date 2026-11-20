using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lunette.Tests.Suite;

public enum SuiteStatus { Passed, Failed, Waived }

public sealed record SuiteResult(string File, SuiteStatus Status, double Seconds, string Detail);

/// <summary>Locates the vendored suite and runs one file at a time in a fresh <see cref="LuaState"/>.</summary>
public static class SuiteRunner
{
    public static readonly TimeSpan FileTimeout = TimeSpan.FromSeconds(180);
    private static readonly object CwdLock = new();

    /// <summary>The CLI apphost built next to the test assembly: what a suite file spawns as "the interpreter".</summary>
    public static string LauncherPath { get; } =
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Lunette.Cli.exe" : "Lunette.Cli");

    /// <summary>
    /// The only sanctioned way for a test to change the process working directory: serialized against every other
    /// such change (the suite runner's included) and restored afterwards. Tests must not use cwd-relative paths.
    /// </summary>
    public static void WithWorkingDirectory(string dir, Action body)
    {
        lock (CwdLock)
        {
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = dir;
            try { body(); }
            finally { Environment.CurrentDirectory = previous; }
        }
    }

    public static string RepoRoot { get; } = FindRepoRoot();
    public static string SuiteDir => Path.Combine(RepoRoot, "tests", "lua-suite");
    public static string WaiverPath => Path.Combine(RepoRoot, "tests", "suite", "waivers.toml");
    public static string RequiredFilesPath => Path.Combine(RepoRoot, "tests", "suite", "required-files.txt");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lunette.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root (Lunette.slnx) not found");
    }

    /// <summary>Top-level suite files, one test each. <c>all.lua</c> is the driver and is listed too.</summary>
    public static IReadOnlyList<string> SuiteFiles() =>
        Directory.GetFiles(SuiteDir, "*.lua")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();

    public static WaiverManifest LoadWaivers() =>
        File.Exists(WaiverPath) ? WaiverManifest.Parse(File.ReadAllText(WaiverPath)) : WaiverManifest.Empty;

    public static IReadOnlyList<string> LoadRequiredFiles() =>
        File.Exists(RequiredFilesPath) ? RequiredFiles.Parse(File.ReadAllText(RequiredFilesPath)) : [];

    /// <summary>
    /// Runs <paramref name="file"/> against a temp copy of the suite (so overlays never touch the vendored files),
    /// with the copy as the working directory. <c>all.lua</c> is run with <c>_U=true</c> as upstream's CI does.
    /// </summary>
    public static SuiteResult Run(string file, WaiverManifest waivers, string? suiteDir = null)
    {
        var sw = Stopwatch.StartNew();
        if (waivers.WholeFileWaiver(file) is { } w)
            return new SuiteResult(file, SuiteStatus.Waived, 0, w.Reason);

        string temp = Path.Combine(Path.GetTempPath(), "lunette-suite-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(suiteDir ?? SuiteDir, temp);
            // attrib.lua writes libs/P1/*.lua; upstream ships that directory empty, which a vendored copy cannot carry.
            Directory.CreateDirectory(Path.Combine(temp, "libs", "P1"));
            // Latin1 maps every byte to one char and back, so the suite's non-UTF-8 sources reach the engine byte for byte.
            string path = Path.Combine(temp, file);
            string source = waivers.ApplyOverlays(file, File.ReadAllText(path, Encoding.Latin1));
            if (source.StartsWith('#')) source = "--" + source; // the standalone interpreter skips a leading '#' line
            if (file == "all.lua") source = "_U=true;" + source;
            // As the standalone interpreter would: arg[0] is the script and arg[-1] the launcher (files.lua and main.lua spawn it).
            source = $"arg={{[-1]=[===[{LauncherPath}]===],[0]=[===[{file}]===]}};" + source;
            // big.lua yields 'b' and returns 'a'; all.lua drives it as a coroutine body, so do the same here.
            if (file == "big.lua")
            {
                File.WriteAllText(path, source, Encoding.Latin1);
                source = "local f = coroutine.wrap(assert(loadfile('big.lua'))); assert(f() == 'b'); assert(f() == 'a')";
            }

            var output = new StringWriter { NewLine = "\n" };
            string? error = null;
            var worker = new Thread(() =>
            {
                var state = new LuaState { Output = output };
                try { state.DoBytes(Encoding.Latin1.GetBytes(source), "@" + file); }
                catch (Exception e) { error = e.Message; }
                // Closing the state runs the end-of-state __gc finalizers (gc.lua relies on them); their errors are not the file's.
                try { state.Dispose(); }
                catch (Exception) { }
            }, 256 * 1024 * 1024) { IsBackground = true };

            string? previous = null;
            lock (CwdLock)
            {
                previous = Environment.CurrentDirectory;
                Environment.CurrentDirectory = temp;
                try
                {
                    worker.Start();
                    if (!worker.Join(FileTimeout)) error = $"timed out after {FileTimeout}";
                }
                finally { Environment.CurrentDirectory = previous; }
            }

            sw.Stop();
            return error is null
                ? new SuiteResult(file, SuiteStatus.Passed, sw.Elapsed.TotalSeconds, output.ToString())
                : new SuiteResult(file, SuiteStatus.Failed, sw.Elapsed.TotalSeconds, error);
        }
        catch (InvalidWaiverException e)
        {
            return new SuiteResult(file, SuiteStatus.Failed, sw.Elapsed.TotalSeconds, e.Message);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from))
            CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }

    private static readonly object ResultsLock = new();

    /// <summary>Appends a machine-readable per-file result (JSON lines) under the test output directory.</summary>
    public static void Record(SuiteResult r)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "suite-results.jsonl");
        string line = JsonSerializer.Serialize(new
        {
            file = r.File,
            status = r.Status.ToString().ToLowerInvariant(),
            seconds = Math.Round(r.Seconds, 3),
            detail = r.Detail.Length > 2000 ? r.Detail[..2000] : r.Detail,
        });
        lock (ResultsLock) File.AppendAllText(path, line + "\n");
    }
}

/// <summary>The growing <c>required-files</c> list: the milestone gate and the regression ratchet.</summary>
public static class RequiredFiles
{
    public static IReadOnlyList<string> Parse(string text) =>
        text.Split('\n')
            .Select(l => { int i = l.IndexOf('#'); return (i >= 0 ? l[..i] : l).Trim(); })
            .Where(l => l.Length > 0)
            .ToList();

    /// <summary>Problems with the list itself: unknown files, duplicates, or required files that are waived whole.</summary>
    public static IEnumerable<string> Validate(
        IReadOnlyList<string> required, IReadOnlyCollection<string> suiteFiles, WaiverManifest waivers)
    {
        foreach (var dup in required.GroupBy(f => f).Where(g => g.Count() > 1))
            yield return $"{dup.Key} is listed more than once";
        foreach (string f in required)
        {
            if (!suiteFiles.Contains(f)) yield return $"{f} is not a suite file";
            else if (waivers.WholeFileWaiver(f) is not null) yield return $"{f} is required but waived whole";
        }
    }

    /// <summary>A required file that does not pass fails the ratchet. Files outside the list are tolerated.</summary>
    public static bool Violates(IReadOnlyCollection<string> required, SuiteResult result) =>
        required.Contains(result.File) && result.Status != SuiteStatus.Passed;
}
