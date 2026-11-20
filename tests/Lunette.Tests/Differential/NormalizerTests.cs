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
    public void The_shell_name_in_child_shell_errors_is_unified()
    {
        // dash prints its argv[0]: "sh" when a C program runs `sh -c`, "/bin/sh" when .NET runs /bin/sh -c.
        Assert.Equal(DiffNormalizer.Stderr("sh: 1: nope: not found\n"), DiffNormalizer.Stderr("/bin/sh: 1: nope: not found\n"));
        Assert.NotEqual(DiffNormalizer.Stderr("sh: 1: nope: not found\n"), DiffNormalizer.Stderr("sh: 1: other: not found\n"));
    }

    [Fact]
    public void Suite_chatter_hides_interpreter_paths_temp_names_and_gc_progress_dots()
    {
        Assert.Equal("progname: X\n(tmp: /tmp/lua_T)\n",
            DiffNormalizer.SuiteChatter("progname: /root/lref/lua\n(tmp: /tmp/lua_nTBYVD)\n"));
        Assert.Equal("\nOK\n", DiffNormalizer.SuiteChatter("...\nOK\n"));
        Assert.Equal("a ... b", DiffNormalizer.SuiteChatter("a ... b"));
        Assert.Equal("took N s", DiffNormalizer.SuiteChatter("took 0.25 s"));
    }

    [Fact]
    public void Masking_numbers_hides_timings_only()
    {
        Assert.Equal("took N s, N ok", DiffNormalizer.MaskNumbers("took 0.25 s, 7 ok"));
    }
}
