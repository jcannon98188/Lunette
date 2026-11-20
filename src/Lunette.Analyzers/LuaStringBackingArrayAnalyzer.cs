using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lunette.Analyzers
{
    /// <summary>LD003: a LuaString's backing byte[] is indexed only by LuaString's own header-aware accessors.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LuaStringBackingArrayAnalyzer : DiagnosticAnalyzer
    {
        public const string Id = "LD003";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            Id,
            "No direct indexing of a LuaString's backing array",
            "Index a LuaString through its header-aware accessors, not its backing byte[]",
            "Lunette.Strings",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(Analyze, OperationKind.ArrayElementReference);
        }

        private static void Analyze(OperationAnalysisContext context)
        {
            var element = (IArrayElementReferenceOperation)context.Operation;
            var member = Unwrap(element.ArrayReference);

            ISymbol? symbol = member switch
            {
                IFieldReferenceOperation f => f.Field,
                IPropertyReferenceOperation p => p.Property,
                _ => null,
            };

            if (symbol == null || !IsLuaString(symbol.ContainingType) || !IsByteArray(member.Type))
            {
                return;
            }

            // LuaString itself owns the layout and may index its own storage.
            for (ISymbol? s = context.ContainingSymbol; s != null; s = s.ContainingSymbol)
            {
                if (s is INamedTypeSymbol t && SymbolEqualityComparer.Default.Equals(t, symbol.ContainingType))
                {
                    return;
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, element.Syntax.GetLocation()));
        }

        private static IOperation Unwrap(IOperation op)
        {
            while (op is IConversionOperation conv)
            {
                op = conv.Operand;
            }

            return op;
        }

        private static bool IsLuaString(INamedTypeSymbol? type) => type != null && type.Name == "LuaString";

        private static bool IsByteArray(ITypeSymbol? type) =>
            type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte };
    }
}
