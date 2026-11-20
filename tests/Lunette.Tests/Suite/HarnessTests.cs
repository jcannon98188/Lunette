using Xunit;

namespace Lunette.Tests.Suite;

public class HarnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lunette-harness-" + Guid.NewGuid().ToString("N"));

    public HarnessTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Suite(params (string Name, string Text)[] files)
    {
        foreach (var (name, text) in files) File.WriteAllText(Path.Combine(_dir, name), text);
        return _dir;
    }

    [Fact]
    public void A_passing_file_reports_passed_and_a_failing_one_reports_failed()
    {
        Suite(("ok.lua", "print(1 + 1)"), ("bad.lua", "print(1 // 0)"));
        var ok = SuiteRunner.Run("ok.lua", WaiverManifest.Empty, _dir);
        var bad = SuiteRunner.Run("bad.lua", WaiverManifest.Empty, _dir);
        Assert.Equal(SuiteStatus.Passed, ok.Status);
        Assert.Equal(SuiteStatus.Failed, bad.Status);
        Assert.Contains("divide by zero", bad.Detail);
    }

    [Fact]
    public void End_of_state_gc_finalizers_run_after_the_file_even_when_it_failed()
    {
        Suite(("ok.lua", "setmetatable({}, {__gc = function() print('closed ok') end})"),
              ("bad.lua", "setmetatable({}, {__gc = function() print('closed bad') end})\nerror('boom')"),
              ("throwing.lua", "setmetatable({}, {__gc = function() error('in gc') end})"));
        Assert.Contains("closed ok", SuiteRunner.Run("ok.lua", WaiverManifest.Empty, _dir).Detail);
        var bad = SuiteRunner.Run("bad.lua", WaiverManifest.Empty, _dir);
        Assert.Equal(SuiteStatus.Failed, bad.Status);
        Assert.Equal(SuiteStatus.Passed, SuiteRunner.Run("throwing.lua", WaiverManifest.Empty, _dir).Status);
    }

    [Fact]
    public void Only_the_suite_runner_mutates_process_global_state()
    {
        string[] mutators =
        [
            "Environment.CurrentDirectory =", "SetCurrentDirectory(", "Directory.SetCurrentDirectory",
            "SetEnvironmentVariable(", "Console.SetOut(", "Console.SetError(", "Console.SetIn(",
            "CultureInfo.CurrentCulture =", "CultureInfo.DefaultThreadCurrentCulture =",
        ];
        string testsDir = Path.Combine(SuiteRunner.RepoRoot, "tests");
        var offenders = Directory.GetFiles(testsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && Path.GetFileName(f) is not ("SuiteRunner.cs" or "HarnessTests.cs"))
            .Where(f => { string t = File.ReadAllText(f); return mutators.Any(t.Contains); })
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Whole_file_waiver_skips_the_file_and_reports_waived()
    {
        Suite(("api.lua", "print(1 // 0)"));
        var m = WaiverManifest.Parse("[[waiver]]\nfile = \"api.lua\"\nreason = \"host-C-API\"\n");
        Assert.Equal(SuiteStatus.Waived, SuiteRunner.Run("api.lua", m, _dir).Status);
    }

    [Fact]
    public void Per_assertion_overlay_changes_the_temp_copy_never_the_suite_file()
    {
        string original = "print(1)\nprint(1 // 0)\nprint(2)\n";
        Suite(("a.lua", original));
        var m = WaiverManifest.Parse(
            "[[waiver]]\nfile = \"a.lua\"\nreason = \"os-env\"\nline = 2\nanchor = \"1 // 0\"\n");
        var r = SuiteRunner.Run("a.lua", m, _dir);
        Assert.Equal(SuiteStatus.Passed, r.Status);
        Assert.Equal(original, File.ReadAllText(Path.Combine(_dir, "a.lua")));
    }

    [Fact]
    public void Overlay_preserves_line_numbers_and_rejects_a_stale_anchor()
    {
        var m = WaiverManifest.Parse(
            "[[waiver]]\nfile = \"a.lua\"\nreason = \"gc-accounting\"\nline = 2\nend_line = 3\nanchor = \"two\"\n");
        Assert.Equal("one\n-- waived (gc-accounting)\n-- waived (gc-accounting)\nfour",
            m.ApplyOverlays("a.lua", "one\ntwo\nthree\nfour"));
        Assert.Throws<InvalidWaiverException>(() => m.ApplyOverlays("a.lua", "one\nXXX\nthree\nfour"));
    }

    [Theory]
    [InlineData("[[waiver]]\nfile = \"a.lua\"\n")]
    [InlineData("[[waiver]]\nfile = \"a.lua\"\nreason = \"too-slow\"\n")]
    [InlineData("[[waiver]]\nfile = \"a.lua\"\nreason = \"bug-wontfix\"\n")]
    [InlineData("[[waiver]]\nfile = \"a.lua\"\nreason = \"os-env\"\nline = 3\n")]
    public void Waivers_without_a_valid_reason_code_or_required_fields_are_rejected(string toml) =>
        Assert.Throws<InvalidWaiverException>(() => WaiverManifest.Parse(toml));

    [Fact]
    public void Bug_wontfix_needs_ticket_and_expiry_and_expired_ones_are_reported()
    {
        var m = WaiverManifest.Parse(
            "[[waiver]]\nfile = \"a.lua\"\nreason = \"bug-wontfix\"\nticket = \"x/1\"\nexpires = \"2020-01-01\" # review\n");
        Assert.Single(m.Expired(new DateOnly(2026, 1, 1)));
        Assert.Empty(m.Expired(new DateOnly(2019, 1, 1)));
    }

    [Fact]
    public void Ratchet_fails_a_required_file_that_does_not_pass_and_tolerates_others()
    {
        var required = RequiredFiles.Parse("# milestone 1\nmath.lua\n\nstrings.lua # tier 1\n");
        Assert.Equal(["math.lua", "strings.lua"], required);
        Assert.True(RequiredFiles.Violates(required, new("math.lua", SuiteStatus.Failed, 0, "")));
        Assert.True(RequiredFiles.Violates(required, new("math.lua", SuiteStatus.Waived, 0, "")));
        Assert.False(RequiredFiles.Violates(required, new("math.lua", SuiteStatus.Passed, 0, "")));
        Assert.False(RequiredFiles.Violates(required, new("api.lua", SuiteStatus.Failed, 0, "")));
    }

    [Fact]
    public void Required_list_validation_flags_unknown_duplicate_and_waived_files()
    {
        var m = WaiverManifest.Parse("[[waiver]]\nfile = \"api.lua\"\nreason = \"host-C-API\"\n");
        var problems = RequiredFiles.Validate(
            ["math.lua", "math.lua", "nope.lua", "api.lua"], new HashSet<string> { "math.lua", "api.lua" }, m).ToList();
        Assert.Equal(3, problems.Count);
    }
}
