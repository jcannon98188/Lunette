using System.Text;
using Lunette.Tests.Suite;
using Xunit;
using Xunit.Abstractions;

namespace Lunette.Tests.Differential;

/// <summary>Shared plumbing for the three differential parts: corpus, generated programs, suite files.</summary>
public static class DiffSupport
{
    public static string DifferentialDir => Path.Combine(SuiteRunner.RepoRoot, "tests", "differential");
    public static string CorpusDir => Path.Combine(DifferentialDir, "corpus");

    /// <summary>Reason-coded divergences; keys are "corpus/x.lua", "gen/seed-N" or "suite/x.lua". Additions need CODEOWNERS review.</summary>
    public static WaiverManifest LoadWaivers()
    {
        string path = Path.Combine(DifferentialDir, "waivers.toml");
        return File.Exists(path) ? WaiverManifest.Parse(File.ReadAllText(path)) : WaiverManifest.Empty;
    }

    /// <summary>
    /// Fails with the divergence unless a waiver with a valid reason code covers <paramref name="key"/>.
    /// A failing program is saved under LUNETTE_DIFF_ARTIFACTS (default: test output) for CI to upload.
    /// </summary>
    public static void Check(string key, string source, string? divergence, ITestOutputHelper log)
    {
        if (LoadWaivers().WholeFileWaiver(key) is { } waiver)
        {
            if (divergence is null)
                Assert.Fail($"{key} now agrees with the Reference binary; remove its waiver from tests/differential/waivers.toml");
            log.WriteLine($"{key}: waived ({waiver.Reason})");
            return;
        }
        if (divergence is null) return;
        string dir = Environment.GetEnvironmentVariable("LUNETTE_DIFF_ARTIFACTS")
            ?? Path.Combine(AppContext.BaseDirectory, "diff-failures");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, key.Replace('/', '_')), source, Encoding.Latin1);
        Assert.Fail($"{key} diverges from the Reference binary (add a reason-coded entry to tests/differential/waivers.toml only if intended):\n{divergence}");
    }

    public static void Compare(string key, string cwd, string script, string source, ITestOutputHelper log,
        string[]? preArgs = null, Func<string, string>? extra = null)
    {
        string[] args = [.. preArgs ?? [], script];
        var theirs = Reference.RunReference(cwd, args);
        var ours = Reference.RunEngine(cwd, args);
        Check(key, source, Reference.Diverges(theirs, ours, extra), log);
    }
}

/// <summary>Part 1: the checked-in corpus of hand-written semantic probes (tests/differential/corpus).</summary>
public class CorpusDifferentialTests(ITestOutputHelper log)
{
    public static IEnumerable<object[]> Files() =>
        Directory.GetFiles(DiffSupport.CorpusDir, "*.lua").Select(f => new object[] { Path.GetFileName(f) }).OrderBy(a => (string)a[0]);

    [ReferenceTheory]
    [MemberData(nameof(Files))]
    public void Corpus_program_agrees_with_the_reference(string file) =>
        DiffSupport.Compare("corpus/" + file, DiffSupport.CorpusDir, file,
            File.ReadAllText(Path.Combine(DiffSupport.CorpusDir, file), Encoding.Latin1), log);

    [Fact]
    public void Corpus_is_present_and_waivers_name_real_programs()
    {
        var files = Directory.GetFiles(DiffSupport.CorpusDir, "*.lua").Select(Path.GetFileName).ToHashSet();
        Assert.True(files.Count >= 6);
        foreach (var w in DiffSupport.LoadWaivers().Waivers.Where(w => w.File.StartsWith("corpus/")))
            Assert.Contains(w.File["corpus/".Length..], files);
        Assert.Empty(DiffSupport.LoadWaivers().Expired(DateOnly.FromDateTime(DateTime.UtcNow)));
    }
}

/// <summary>
/// Part 2: seeded generated programs. PRs run a fixed seed set; the nightly run sets LUNETTE_DIFF_SEEDS=start:count
/// for fresh seeds. A failing seed is minimised by hand and committed to the corpus.
/// </summary>
public class GeneratedDifferentialTests(ITestOutputHelper log)
{
    public static IEnumerable<object[]> Seeds()
    {
        int start = 1, count = 25;
        string? spec = Environment.GetEnvironmentVariable("LUNETTE_DIFF_SEEDS");
        if (spec is not null)
        {
            string[] parts = spec.Split(':');
            start = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            count = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        }
        return Enumerable.Range(start, count).Select(s => new object[] { s });
    }

    [Fact]
    public void Generator_is_deterministic_per_seed()
    {
        Assert.Equal(ProgramGenerator.Generate(7), ProgramGenerator.Generate(7));
        Assert.NotEqual(ProgramGenerator.Generate(7), ProgramGenerator.Generate(8));
    }

    [ReferenceTheory]
    [MemberData(nameof(Seeds))]
    public void Generated_program_agrees_with_the_reference(int seed)
    {
        string dir = Path.Combine(Path.GetTempPath(), "lunette-gen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string source = ProgramGenerator.Generate(seed);
            File.WriteAllText(Path.Combine(dir, "gen.lua"), source, Encoding.Latin1);
            DiffSupport.Compare($"gen/seed-{seed}", dir, "gen.lua", source, log);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

/// <summary>
/// Part 3: the official suite's own chunks, run as whole programs through both binaries (exit status, stdout with
/// numbers masked, stderr). Only files in required-files are compared, since a file the engine does not yet pass
/// diverges by definition. all.lua (the driver), big.lua (needs a coroutine driver) and heavy.lua are excluded as upstream does.
/// </summary>
public class SuiteChunkDifferentialTests(ITestOutputHelper log)
{
    // main.lua: PUC Lua 5.4.9 from a plain `make linux` (no readline) fails the file's own interactive-prompt check
    // (main.lua:385), so the Reference is no oracle for it; the engine passes it per file in SuiteTests.
    // attrib.lua: needs the upstream C test libraries (libs/lib1.so and friends), which a plain Reference build lacks, so the
    // Reference fails it; the engine's own copy has those parts waived (host-C-API) in SuiteTests.
    private static readonly HashSet<string> Excluded = ["all.lua", "big.lua", "heavy.lua", "main.lua", "attrib.lua"];

    public static IEnumerable<object[]> Files()
    {
        var waivers = SuiteRunner.LoadWaivers();
        return SuiteRunner.LoadRequiredFiles()
            .Where(f => !Excluded.Contains(f) && waivers.WholeFileWaiver(f) is null)
            .Select(f => new object[] { f });
    }

    [ReferenceTheory]
    [MemberData(nameof(Files))]
    public void Suite_file_agrees_with_the_reference(string file)
    {
        string temp = Path.Combine(Path.GetTempPath(), "lunette-diffsuite-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(SuiteRunner.SuiteDir, temp);
            string path = Path.Combine(temp, file);
            string source = SuiteRunner.LoadWaivers().ApplyOverlays(file, File.ReadAllText(path, Encoding.Latin1));
            File.WriteAllText(path, source, Encoding.Latin1);
            DiffSupport.Compare("suite/" + file, temp, file, source, log,
                preArgs: ["-e", "_U=true"], extra: DiffNormalizer.SuiteChatter);
        }
        finally { try { Directory.Delete(temp, recursive: true); } catch (IOException) { } }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
