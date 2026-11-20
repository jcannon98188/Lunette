using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Lunette.Analyzers.Tests;

internal static class Verify
{
    // The core assembly declares these markers itself; the analyzers match them by simple name.
    private const string Markers = """
        using System;
        namespace Lunette
        {
            [AttributeUsage(AttributeTargets.All)] internal sealed class LuaHotPathAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.All)] internal sealed class LuaColdThrowAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.All)] internal sealed class LuaStringPathAttribute : Attribute { }
        }

        """;

    /// <summary>Verifies <paramref name="source"/> (with {|ID:...|} markup) against one analyzer; markers are prepended.</summary>
    public static Task Analyzer<TAnalyzer>(string source, bool allowUnsafe = false)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = "using System;\nusing Lunette;\n" + source,
        };
        test.TestState.Sources.Add(Markers);
        if (allowUnsafe)
        {
            test.SolutionTransforms.Add((solution, projectId) =>
            {
                var project = solution.GetProject(projectId)!;
                var options = (Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions)project.CompilationOptions!;
                return solution.WithProjectCompilationOptions(projectId, options.WithAllowUnsafe(true));
            });
        }

        return test.RunAsync();
    }
}
