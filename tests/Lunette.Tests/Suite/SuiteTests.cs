using Xunit;
using Xunit.Abstractions;

namespace Lunette.Tests.Suite;

/// <summary>One test per official suite file. Files outside required-files may fail (reported); required ones may not.</summary>
public class SuiteTests(ITestOutputHelper log)
{
    public static IEnumerable<object[]> Files() => SuiteRunner.SuiteFiles().Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Files))]
    public void Suite_file(string file)
    {
        var required = SuiteRunner.LoadRequiredFiles();
        var result = SuiteRunner.Run(file, SuiteRunner.LoadWaivers());
        SuiteRunner.Record(result);
        log.WriteLine($"{file}: {result.Status} in {result.Seconds:F2}s{(result.Detail.Length > 0 ? " - " + result.Detail : "")}");
        Assert.False(RequiredFiles.Violates(required, result),
            $"required file {file} did not pass ({result.Status}): {result.Detail}");
    }

    [Fact]
    public void Manifest_and_required_files_are_consistent()
    {
        var waivers = SuiteRunner.LoadWaivers();
        var problems = RequiredFiles.Validate(
            SuiteRunner.LoadRequiredFiles(), SuiteRunner.SuiteFiles().ToHashSet(), waivers).ToList();
        Assert.Empty(problems);
        Assert.Empty(waivers.Expired(DateOnly.FromDateTime(DateTime.UtcNow)));
        foreach (var w in waivers.Waivers)
            Assert.Contains(w.File, SuiteRunner.SuiteFiles());
    }

    [Fact]
    public void Vendored_suite_is_present()
    {
        Assert.Contains("all.lua", SuiteRunner.SuiteFiles());
        Assert.True(SuiteRunner.SuiteFiles().Count >= 30);
    }
}
