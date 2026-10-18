using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Automatic.Battle.Analyzers
{
    /// <summary>[DeterminismExempt("reason")] on any enclosing member, local function or type silences every rule but DET009.</summary>
    internal static class Exemptions
    {
        public static bool IsExempt(SyntaxNode node, SemanticModel model, INamedTypeSymbol exemptAttribute, CancellationToken ct)
        {
            for (var n = node; n != null; n = n.Parent)
            {
                switch (n)
                {
                    case BaseFieldDeclarationSyntax field:
                        if (field.Declaration.Variables.Any(v => HasValidExemption(model.GetDeclaredSymbol(v, ct), exemptAttribute)))
                            return true;
                        break;
                    case MemberDeclarationSyntax member:
                        if (HasValidExemption(model.GetDeclaredSymbol(member, ct), exemptAttribute)) return true;
                        break;
                    case LocalFunctionStatementSyntax local:
                        if (HasValidExemption(model.GetDeclaredSymbol(local, ct), exemptAttribute)) return true;
                        break;
                }
            }
            return false;
        }

        private static bool HasValidExemption(ISymbol? symbol, INamedTypeSymbol exemptAttribute) =>
            symbol != null && symbol.GetAttributes().Any(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, exemptAttribute)
                && a.ConstructorArguments.Length == 1
                && IsValidReason(a.ConstructorArguments[0].Value as string));

        public static bool IsValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason);

        /// <summary>Reports <paramref name="rule"/> at <paramref name="location"/> unless it is null or <paramref name="node"/> is exempt.</summary>
        public static void ReportUnlessExempt(
            Action<Diagnostic> report, KnownTypes known, SemanticModel model, SyntaxNode node,
            Location location, DiagnosticDescriptor? rule, string what, CancellationToken ct)
        {
            if (rule == null || IsExempt(node, model, known.ExemptAttribute, ct)) return;
            report(Diagnostic.Create(rule, location, what));
        }
    }
}
