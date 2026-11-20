using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Lunette.Tests;

/// <summary>The core must not depend on dynamic-code assemblies (Native AOT and trimming, spec ticket 10).</summary>
public class AotCompatibilityTests
{
    private static readonly string[] DynamicCodeNamespaces =
        ["System.Reflection.Emit", "System.Linq.Expressions", "System.CodeDom", "Microsoft.CSharp"];

    private static (HashSet<string> Assemblies, HashSet<string> Namespaces) ReadReferences()
    {
        using var stream = File.OpenRead(typeof(LuaState).Assembly.Location);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var assemblies = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToHashSet();
        var namespaces = md.TypeReferences.Select(h => md.GetString(md.GetTypeReference(h).Namespace)).ToHashSet();
        return (assemblies, namespaces);
    }

    [Fact]
    public void Core_assembly_references_no_dynamic_code_namespaces()
    {
        var (_, namespaces) = ReadReferences();
        foreach (string ns in DynamicCodeNamespaces)
            Assert.DoesNotContain(namespaces, n => n == ns || n.StartsWith(ns + "."));
    }

    [Fact]
    public void Core_assembly_references_no_dynamic_code_assemblies()
    {
        var (assemblies, _) = ReadReferences();
        Assert.DoesNotContain("System.Linq.Expressions", assemblies);
        Assert.DoesNotContain("System.Reflection.Emit", assemblies);
        Assert.DoesNotContain("Microsoft.CSharp", assemblies);
    }

    [Fact]
    public void Cli_project_publishes_with_native_aot()
    {
        string csproj = File.ReadAllText(Path.Combine(Suite.SuiteRunner.RepoRoot, "src", "Lunette.Cli", "Lunette.Cli.csproj"));
        Assert.Contains("<PublishAot>true</PublishAot>", csproj);
    }
}
