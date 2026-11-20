using Lunette.Cli;
using Xunit;

namespace Lunette.Tests;

public class CliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = Program.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Runs_a_script_file_and_prints_arithmetic_results()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lua");
        File.WriteAllText(path, "print(1 + 2)\nprint(10 / 4)\nprint(7 // 2)\n");
        try
        {
            var (code, o, e) = Run(path);
            Assert.Equal(0, code);
            Assert.Equal("3\n2.5\n3\n", o);
            Assert.Equal("", e);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reports_runtime_errors_on_stderr_with_exit_code_1()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lua");
        File.WriteAllText(path, "print(1 // 0)");
        try
        {
            var (code, _, e) = Run(path);
            Assert.Equal(1, code);
            Assert.Contains("attempt to divide by zero", e);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Dash_e_runs_a_statement()
    {
        var (code, o, _) = Run("-e", "print('hi')");
        Assert.Equal(0, code);
        Assert.Equal("hi\n", o);
    }

    [Fact]
    public void Script_arguments_reach_the_arg_table_and_the_vararg()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lua");
        File.WriteAllText(path, "print(#arg, arg[1], arg[2], ...)\nprint(arg[0] == " + "'" + path.Replace("\\", "\\\\") + "')\n");
        try
        {
            var (code, o, e) = Run(path, "a", "b");
            Assert.Equal(0, code);
            Assert.Equal("2\ta\tb\ta\tb\ntrue\n", o);
            Assert.Equal("", e);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Dash_l_requires_a_module_before_the_script()
    {
        var (code, o, _) = Run("-l", "string", "-e", "print(string ~= nil)");
        Assert.Equal(0, code);
        Assert.Equal("true\n", o);
    }

    [Fact]
    public void Missing_script_file_exits_1()
    {
        var (code, _, e) = Run("definitely-missing.lua");
        Assert.Equal(1, code);
        Assert.Contains("cannot open", e);
    }
}
