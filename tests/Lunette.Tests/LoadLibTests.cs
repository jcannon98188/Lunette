using Xunit;

namespace Lunette.Tests;

/// <summary>load, dofile, require and package, plus the _ENV model they rely on.</summary>
public class LoadLibTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Load_compiles_a_string_chunk()
    {
        Assert.Equal("3\n", Run("print(load('return 1 + 2')())"));
    }

    [Fact]
    public void Load_returns_nil_and_a_message_for_a_syntax_error()
    {
        Assert.Equal("nil\t[string \"x =\"]:1: unexpected symbol near <eof>\n", Run("print(load('x ='))"));
    }

    [Fact]
    public void Load_reads_pieces_from_a_function()
    {
        Assert.Equal("7\n", Run("""
            local parts = {'return ', '3 + ', '4'}
            local i = 0
            print(load(function() i = i + 1 return parts[i] end)())
            """));
    }

    [Fact]
    public void Load_uses_the_given_environment_for_globals()
    {
        Assert.Equal("10\tnil\n", Run("""
            local env = {}
            load('x = 10', 'chunk', 't', env)()
            print(env.x, x)
            """));
    }

    [Fact]
    public void Load_in_binary_mode_rejects_text()
    {
        Assert.Contains("attempt to load a text chunk", Run("print(load('return 1', 'c', 'b'))"));
    }

    [Fact]
    public void Local_env_redirects_global_access()
    {
        Assert.Equal("5\n", Run("""
            local function f()
              local _ENV = {v = 5}
              return v
            end
            print(f())
            """));
    }

    [Fact]
    public void Env_is_an_upvalue_of_the_main_chunk()
    {
        Assert.Equal("true\n", Run("print(_ENV == _G)"));
    }

    [Fact]
    public void Index_error_names_a_global_reached_through_env()
    {
        Assert.Contains("global 'nothing'", Run("print(pcall(function() return nothing.x end))"));
    }

    [Fact]
    public void Require_returns_and_caches_a_preloaded_module()
    {
        Assert.Equal("1\ttrue\n", Run("""
            local calls = 0
            package.preload.mod = function() calls = calls + 1 return {} end
            local a = require 'mod'
            local b = require 'mod'
            print(calls, a == b)
            """));
    }

    [Fact]
    public void Require_reports_every_place_it_searched()
    {
        string r = Run("print(pcall(require, 'no_such_module_x'))");
        Assert.Contains("module 'no_such_module_x' not found", r);
        Assert.Contains("no field package.preload['no_such_module_x']", r);
    }

    [Fact]
    public void Standard_libraries_are_registered_in_package_loaded()
    {
        Assert.Equal("true\ttrue\ttrue\n", Run("print(package.loaded.string == string, package.loaded._G == _G, require('table') == table)"));
    }

    [Fact]
    public void Dofile_and_loadfile_run_a_file_and_report_a_missing_one()
    {
        string path = Path.Combine(Path.GetTempPath(), "lunette-dofile-" + Guid.NewGuid().ToString("N") + ".lua");
        File.WriteAllText(path, "#!shebang line\nreturn 40 + 2\n");
        try
        {
            string lua = path.Replace("\\", "/");
            Assert.Equal("42\n", Run($"print(dofile('{lua}'))"));
            Assert.Equal("42\n", Run($"print(loadfile('{lua}')())"));
        }
        finally { File.Delete(path); }
        Assert.Contains("cannot open", Run("print(loadfile('/no/such/file.lua'))"));
    }

    [Fact]
    public void Constants_beyond_the_sixteen_bit_load_range_are_reachable()
    {
        Assert.Equal("70000\n", Run("""
            local parts = {'local t = {0'}
            for i = 1, 70000 do parts[#parts + 1] = i end
            parts[#parts + 1] = '} return t[70001]'
            print(load(table.concat(parts, ';'))())
            """));
    }
}
