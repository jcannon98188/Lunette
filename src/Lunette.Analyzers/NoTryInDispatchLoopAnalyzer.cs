using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Lunette.Analyzers
{
    /// <summary>
    /// LD006: no 'try' (including 'using' and 'lock', which lower to try/finally) inside a [LuaInterpreterLoop] method.
    /// The only try of the dispatch lives in Interpreter.Run, outside the loop method (ADR 0003), so the JIT never has to
    /// keep the loop's registers live across an exception-handling region.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NoTryInDispatchLoopAnalyzer : MarkedBodyAnalyzer
    {
        public const string Id = "LD006";

        internal static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            Id,
            "No try in the dispatch loop",
            "'{0}' is not allowed in a [LuaInterpreterLoop] method; the dispatch loop's only try belongs in Interpreter.Run, outside the loop method",
            "Lunette.HotPath",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public NoTryInDispatchLoopAnalyzer() : base(Rule, Markers.InterpreterLoop)
        {
        }

        protected override string? Check(IOperation op) => op switch
        {
            ITryOperation => "try",
            IUsingOperation => "using",
            ILockOperation => "lock",
            _ => null,
        };
    }
}
