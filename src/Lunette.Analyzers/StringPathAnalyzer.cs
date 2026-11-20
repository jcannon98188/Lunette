using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lunette.Analyzers
{
    /// <summary>LD004: string-library paths ([LuaStringPath]) work on bytes, never on Encoding, string, StringBuilder or ToArray().</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StringPathAnalyzer : MarkedBodyAnalyzer
    {
        public const string Id = "LD004";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            Id,
            "Byte-only string-library paths",
            "{0} is not allowed in a [LuaStringPath] member; Lua strings are bytes",
            "Lunette.Strings",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public StringPathAnalyzer() : base(Rule, Markers.StringPath)
        {
        }

        protected override string? Check(IOperation op)
        {
            if (op.IsImplicit)
            {
                return null;
            }

            switch (op)
            {
                case IInvocationOperation inv when IsEncoding(inv.TargetMethod.ContainingType):
                    return "System.Text.Encoding";
                case IPropertyReferenceOperation prop when IsEncoding(prop.Property.ContainingType):
                    return "System.Text.Encoding";
                case IObjectCreationOperation create when IsStringBuilder(create.Type):
                    return "StringBuilder";
                case IInvocationOperation inv when IsStringBuilder(inv.TargetMethod.ContainingType):
                    return "StringBuilder";
                case IInvocationOperation { TargetMethod.Name: "ToArray" }:
                    return "ToArray()";
                case IVariableDeclaratorOperation decl when decl.Symbol.Type.SpecialType == SpecialType.System_String:
                    return "a string local";
                case IParameterReferenceOperation param when param.Parameter.Type.SpecialType == SpecialType.System_String:
                    return "a string parameter";
                case IInvocationOperation { Type.SpecialType: SpecialType.System_String } when !op.ConstantValue.HasValue:
                    return "a string-returning call";
                case IBinaryOperation { Type.SpecialType: SpecialType.System_String } when !op.ConstantValue.HasValue:
                    return "string concatenation";
                case IInterpolatedStringOperation when !op.ConstantValue.HasValue:
                    return "string interpolation";
                default:
                    return null;
            }
        }

        private static bool IsEncoding(INamedTypeSymbol? type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.Name == "Encoding" && t.ContainingNamespace.ToDisplayString() == "System.Text")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStringBuilder(ITypeSymbol? type) =>
            type != null && type.Name == "StringBuilder" && type.ContainingNamespace.ToDisplayString() == "System.Text";
    }
}
