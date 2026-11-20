using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lunette.Analyzers
{
    /// <summary>LD001: no heap-allocating constructs in [LuaHotPath] code.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HotPathAllocationAnalyzer : MarkedBodyAnalyzer
    {
        public const string Id = "LD001";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            Id,
            "No heap allocation in hot paths",
            "{0} allocates and is not allowed in a [LuaHotPath] member",
            "Lunette.HotPath",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public HotPathAllocationAnalyzer() : base(Rule, Markers.HotPath)
        {
        }

        protected override string? Check(IOperation op)
        {
            switch (op)
            {
                case IObjectCreationOperation { Type: { IsReferenceType: true }, Constructor: not null }:
                    return "'new' of a reference type";
                case IArrayCreationOperation:
                    return op.IsImplicit ? "a params array" : "an array creation";
                case IConversionOperation { Operand.Type.IsValueType: true, Type.IsReferenceType: true, Conversion.Exists: true }:
                    return "boxing";
                case IDelegateCreationOperation:
                    return "a lambda, closure or method-group delegate";
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String } when op.ConstantValue.HasValue == false:
                    return "string concatenation";
                case IInterpolatedStringOperation when op.ConstantValue.HasValue == false:
                    return "string interpolation";
                case IInvocationOperation inv when IsLinq(inv.TargetMethod):
                    return "a LINQ call";
                case IForEachLoopOperation loop when !IsAllocationFreeEnumerable(Unwrap(loop.Collection).Type):
                    return "foreach over a non-array, non-span collection";
                default:
                    return null;
            }
        }

        private static IOperation Unwrap(IOperation op)
        {
            while (op is IConversionOperation conv)
            {
                op = conv.Operand;
            }

            return op;
        }

        private static bool IsLinq(IMethodSymbol method)
        {
            var type = method.ContainingType;
            return type != null
                && (type.Name == "Enumerable" || type.Name == "Queryable")
                && type.ContainingNamespace.ToDisplayString() == "System.Linq";
        }

        private static bool IsAllocationFreeEnumerable(ITypeSymbol? type)
        {
            if (type is IArrayTypeSymbol)
            {
                return true;
            }

            return type is INamedTypeSymbol named
                && (named.Name == "Span" || named.Name == "ReadOnlySpan")
                && named.ContainingNamespace.ToDisplayString() == "System";
        }
    }
}
