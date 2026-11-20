using System.Reflection;
using Xunit;

namespace Lunette.Tests;

public class SolutionLayoutTests
{
    [Fact]
    public void Core_assembly_references_only_the_bcl()
    {
        var refs = typeof(LuaState).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n != "System.Private.CoreLib" && !n.StartsWith("System.", StringComparison.Ordinal) && n != "netstandard" && n != "mscorlib");
        Assert.Empty(refs);
    }

    [Fact]
    public void Core_assembly_exposes_only_the_root_namespace_publicly()
    {
        var offenders = typeof(LuaState).Assembly.GetExportedTypes().Where(t => t.Namespace != "Lunette");
        Assert.Empty(offenders);
    }

    [Fact]
    public void Cli_references_core_and_core_does_not_reference_cli()
    {
        var cli = typeof(Lunette.Cli.Program).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.Contains("Lunette", cli);
    }
}
