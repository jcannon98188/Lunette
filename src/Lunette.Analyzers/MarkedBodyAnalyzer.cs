using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lunette.Analyzers
{
    /// <summary>Base for analyzers that inspect every operation of members carrying a given marker attribute.</summary>
    public abstract class MarkedBodyAnalyzer : DiagnosticAnalyzer
    {
        private readonly DiagnosticDescriptor _rule;
        private readonly string _marker;

        protected MarkedBodyAnalyzer(DiagnosticDescriptor rule, string marker)
        {
            _rule = rule;
            _marker = marker;
        }

        public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_rule);

        public sealed override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationBlockAction(Analyze);
        }

        private void Analyze(OperationBlockAnalysisContext context)
        {
            if (!Markers.IsMarked(context.OwningSymbol, _marker) || !AppliesTo(context.OwningSymbol))
            {
                return;
            }

            foreach (var block in context.OperationBlocks)
            {
                if (block.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax)
                {
                    continue; // attribute applications are operation blocks of the member they decorate
                }

                foreach (var op in block.DescendantsAndSelf())
                {
                    var message = Check(op);
                    if (message != null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(_rule, op.Syntax.GetLocation(), message));
                    }
                }
            }
        }

        protected virtual bool AppliesTo(ISymbol owner) => true;

        /// <summary>Returns the offending construct's description, or null when the operation is fine.</summary>
        protected abstract string? Check(IOperation operation);
    }
}
