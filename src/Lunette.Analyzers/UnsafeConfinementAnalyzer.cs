using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lunette.Analyzers
{
    /// <summary>
    /// LD005: 'unsafe' code, System.Runtime.CompilerServices.Unsafe and MemoryMarshal are confined to the
    /// Lunette.Unsafe namespace (inside the core assembly, the only one that may enable AllowUnsafeBlocks).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnsafeConfinementAnalyzer : DiagnosticAnalyzer
    {
        public const string Id = "LD005";
        public const string AllowedNamespace = "Lunette.Unsafe";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            Id,
            "Unsafe code is confined to Lunette.Unsafe",
            "{0} is only allowed inside the Lunette.Unsafe helper family",
            "Lunette.Unsafe",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
            context.RegisterOperationAction(AnalyzeMemberReference, OperationKind.PropertyReference, OperationKind.FieldReference, OperationKind.MethodReference);
            context.RegisterSyntaxNodeAction(AnalyzeUnsafeStatement, SyntaxKind.UnsafeStatement);
            context.RegisterSyntaxNodeAction(AnalyzeUnsafeModifier,
                SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.MethodDeclaration,
                SyntaxKind.ConstructorDeclaration, SyntaxKind.PropertyDeclaration, SyntaxKind.FieldDeclaration,
                SyntaxKind.LocalFunctionStatement, SyntaxKind.OperatorDeclaration, SyntaxKind.RecordDeclaration,
                SyntaxKind.RecordStructDeclaration, SyntaxKind.InterfaceDeclaration);
        }

        private static bool IsInside(ISymbol? symbol)
        {
            var ns = symbol?.ContainingNamespace?.ToDisplayString();
            return ns != null && (ns == AllowedNamespace || ns.StartsWith(AllowedNamespace + ".", System.StringComparison.Ordinal));
        }

        private static bool IsRestrictedType(INamedTypeSymbol? type) =>
            type != null
            && ((type.Name == "Unsafe" && type.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices")
                || (type.Name == "MemoryMarshal" && type.ContainingNamespace.ToDisplayString() == "System.Runtime.InteropServices"));

        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var inv = (IInvocationOperation)context.Operation;
            Report(context, inv.TargetMethod.ContainingType, inv.Syntax);
        }

        private static void AnalyzeMemberReference(OperationAnalysisContext context)
        {
            var symbol = ((IMemberReferenceOperation)context.Operation).Member;
            Report(context, symbol.ContainingType, context.Operation.Syntax);
        }

        private static void Report(OperationAnalysisContext context, INamedTypeSymbol? target, SyntaxNode node)
        {
            if (IsRestrictedType(target) && !IsInside(context.ContainingSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), target!.Name));
            }
        }

        private static void AnalyzeUnsafeStatement(SyntaxNodeAnalysisContext context)
        {
            if (!IsInside(context.ContainingSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), "an unsafe block"));
            }
        }

        private static void AnalyzeUnsafeModifier(SyntaxNodeAnalysisContext context)
        {
            var modifiers = context.Node switch
            {
                MemberDeclarationSyntax m => m.Modifiers,
                LocalFunctionStatementSyntax l => l.Modifiers,
                _ => default,
            };

            if (!modifiers.Any(SyntaxKind.UnsafeKeyword))
            {
                return;
            }

            var symbol = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken)
                ?? context.ContainingSymbol;
            if (!IsInside(symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), "the unsafe modifier"));
            }
        }
    }
}
