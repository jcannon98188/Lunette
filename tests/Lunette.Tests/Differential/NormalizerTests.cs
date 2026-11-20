using Xunit;

namespace Lunette.Tests.Differential;

public class NormalizerTests
{
    [Fact]
    public void Addresses_are_masked_in_stdout()
    {
        Assert.Equal("table: ADDR\nfunction: ADDR\n", DiffNormalizer.Stdout("table: 0x5581a0c4e2a0\nfunction: builtin: 0x1f\n"));
        Assert.Equal("p=0xADDR", DiffNormalizer.Stdout("p=0x00007ffd12345678"));
    }

    [Fact]
    public void Ordinary_hex_is_left_alone()
    {
        Assert.Equal("255 0xff\n", DiffNormalizer.Stdout("255 0xff\n"));
    }

    [Fact]
    public void Stderr_unifies_the_program_name_and_drops_the_traceback()
    {
        string theirs = "lua: x.lua:3: boom\nstack traceback:\n\t[C]: in ?\n";
        string ours = "lunette: x.lua:3: boom\nstack traceback:\n\tx.lua:3: in main chunk\n";
        Assert.Equal(DiffNormalizer.Stderr("/root/ref/lua: x.lua:3: boom\n"), DiffNormalizer.Stderr("lunette: x.lua:3: boom\n"));
        Assert.Equal(DiffNormalizer.Stderr(theirs), DiffNormalizer.Stderr(ours));
        Assert.Equal("lua: x.lua:3: boom\n", DiffNormalizer.Stderr(ours));
    }

    [Fact]
    public void Real_message_differences_survive_normalisation()
    {
        Assert.NotEqual(
            DiffNormalizer.Stderr("lua: x.lua:3: attempt to add\n"),
            DiffNormalizer.Stderr("lunette: x.lua:3: attempt to subtract\n"));
    }

    [Fact]
    public void Masking_numbers_hides_timings_only()
    {
        Assert.Equal("took N s, N ok", DiffNormalizer.MaskNumbers("took 0.25 s, 7 ok"));
    }
}
