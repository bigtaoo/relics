using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Automatic.Battle.Analyzers
{
    /// <summary>Checks on what the source spells out: type names, static members reached by simple name, keywords.</summary>
    internal sealed class SyntaxChecks
    {
        private readonly KnownTypes _known;

        public SyntaxChecks(KnownTypes known) => _known = known;

        /// <summary>
        /// `float`, `System.Random`, `Task&lt;int&gt;`, and members reached without a type in front
        /// (using static, inherited). Members written as `Type.Member` are covered by the type name.
        /// </summary>
        public void TypeOrMemberName(SyntaxNodeAnalysisContext c)
        {
            var node = (TypeSyntax)c.Node;
            // `var` takes the initializer's type, which is reported where it comes from.
            if (node is IdentifierNameSyntax id && id.IsVar) return;
            var info = c.SemanticModel.GetSymbolInfo(node, c.CancellationToken);
            var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
            switch (symbol)
            {
                case ITypeSymbol type:
                    Report(c, node, _known.Classify(type), type.ToDisplayString());
                    break;
                case IMethodSymbol _:
                case IPropertySymbol _:
                case IFieldSymbol _:
                case IEventSymbol _:
                    if (!IsQualified(node))
                        Report(c, node, _known.Classify(symbol.ContainingType), symbol.ContainingType.Name + "." + symbol.Name);
                    break;
            }
        }

        public void ThreadingKeyword(SyntaxNodeAnalysisContext c)
        {
            switch (c.Node)
            {
                case AwaitExpressionSyntax a: Report(c, a.AwaitKeyword, "await"); break;
                case LockStatementSyntax l: Report(c, l.LockKeyword, "lock"); break;
                case CommonForEachStatementSyntax f: Report(c, f.AwaitKeyword, "await foreach"); break;
                case UsingStatementSyntax u: Report(c, u.AwaitKeyword, "await using"); break;
                case LocalDeclarationStatementSyntax d: Report(c, d.AwaitKeyword, "await using"); break;
                case MethodDeclarationSyntax m: ReportModifier(c, m.Modifiers, SyntaxKind.AsyncKeyword); break;
                case LocalFunctionStatementSyntax lf: ReportModifier(c, lf.Modifiers, SyntaxKind.AsyncKeyword); break;
                case AnonymousFunctionExpressionSyntax fn: Report(c, fn.AsyncKeyword, "async"); break;
                case FieldDeclarationSyntax fd: ReportModifier(c, fd.Modifiers, SyntaxKind.VolatileKeyword); break;
            }
        }

        /// <summary>DET009: an exemption must say why.</summary>
        public void ExemptionAttribute(SyntaxNodeAnalysisContext c)
        {
            var attribute = (AttributeSyntax)c.Node;
            if (!(c.SemanticModel.GetSymbolInfo(attribute, c.CancellationToken).Symbol is IMethodSymbol ctor)
                || !SymbolEqualityComparer.Default.Equals(ctor.ContainingType, _known.ExemptAttribute))
                return;
            var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();
            var reason = argument == null
                ? null
                : c.SemanticModel.GetConstantValue(argument.Expression, c.CancellationToken).Value as string;
            if (!Exemptions.IsValidReason(reason))
                c.ReportDiagnostic(Diagnostic.Create(Rules.ExemptionReason, attribute.GetLocation()));
        }

        private static bool IsQualified(SyntaxNode node) => node.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Name == node,
            MemberBindingExpressionSyntax _ => true,
            QualifiedNameSyntax qualified => qualified.Right == node,
            AliasQualifiedNameSyntax alias => alias.Name == node,
            _ => false,
        };

        private void ReportModifier(SyntaxNodeAnalysisContext c, SyntaxTokenList modifiers, SyntaxKind kind)
        {
            var token = modifiers.FirstOrDefault(t => t.IsKind(kind));
            Report(c, token, token.ValueText);
        }

        private void Report(SyntaxNodeAnalysisContext c, SyntaxToken token, string what)
        {
            if (token.IsKind(SyntaxKind.None)) return;
            Exemptions.ReportUnlessExempt(c.ReportDiagnostic, _known, c.SemanticModel, c.Node,
                token.GetLocation(), Rules.Threading, what, c.CancellationToken);
        }

        private void Report(SyntaxNodeAnalysisContext c, SyntaxNode node, DiagnosticDescriptor? rule, string what) =>
            Exemptions.ReportUnlessExempt(c.ReportDiagnostic, _known, c.SemanticModel, node,
                node.GetLocation(), rule, what, c.CancellationToken);
    }
}
