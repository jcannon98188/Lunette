using Xunit;

namespace Lunette.Tests.Suite;

public class SuiteShardTests
{
    private static readonly string[] Files = ["a.lua", "b.lua", "c.lua", "d.lua", "e.lua", "f.lua", "g.lua"];

    [Fact]
    public void Unset_spec_selects_everything()
    {
        Assert.Equal(Files, SuiteShard.Select(Files, null));
        Assert.Equal(Files, SuiteShard.Select(Files, ""));
    }

    [Fact]
    public void Shards_partition_the_files_exactly()
    {
        var all = Enumerable.Range(1, 4).SelectMany(i => SuiteShard.Select(Files, $"{i}/4")).Order().ToList();
        Assert.Equal(Files, all);
        Assert.Equal(["a.lua", "e.lua"], SuiteShard.Select(Files, "1/4"));
    }

    [Theory]
    [InlineData("0/4")]
    [InlineData("5/4")]
    [InlineData("x")]
    [InlineData("1/0")]
    public void Malformed_spec_is_rejected(string spec) =>
        Assert.Throws<ArgumentException>(() => SuiteShard.Select(Files, spec));
}
