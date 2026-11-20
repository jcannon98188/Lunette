using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lunette.Analyzers
{
    /// <summary>LD002: no 'throw' in [LuaHotPath] code; errors take the pending-error-state path (ADR 0003).</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HotPathThrowAnalyzer : MarkedBodyAnalyzer
    {
        public const string Id = "LD002";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            Id,
            "No throw in hot paths",
            "'throw' is not allowed in a [LuaHotPath] member; use the pending-error-state path or move it into a [LuaColdThrow] method",
            "Lunette.HotPath",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public HotPathThrowAnalyzer() : base(Rule, Markers.HotPath)
        {
        }

        protected override bool AppliesTo(ISymbol owner) => !Markers.HasAttribute(owner, Markers.ColdThrow);

        protected override string? Check(IOperation op) => op is IThrowOperation ? "throw" : null;
    }
}
