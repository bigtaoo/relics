using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Automatic.Battle.Analyzers
{
    /// <summary>
    /// Enforces the determinism rules of CLAUDE.md in assemblies marked [assembly: EnforceDeterminism]
    /// (Battle.Core). Syntax checks catch what is written (type names, literals, keywords); operation
    /// checks catch what is implied (metadata members returning banned types, implicit conversions,
    /// unordered collections handed to IEnumerable consumers).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeterminismAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules.All;

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                var known = KnownTypes.Create(start.Compilation);
                if (known == null) return;

                var syntax = new SyntaxChecks(known);
                start.RegisterSyntaxNodeAction(syntax.TypeOrMemberName,
                    SyntaxKind.PredefinedType, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
                start.RegisterSyntaxNodeAction(syntax.ThreadingKeyword,
                    SyntaxKind.AwaitExpression, SyntaxKind.LockStatement,
                    SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement,
                    SyntaxKind.UsingStatement, SyntaxKind.LocalDeclarationStatement,
                    SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement,
                    SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression,
                    SyntaxKind.AnonymousMethodExpression, SyntaxKind.FieldDeclaration);
                start.RegisterSyntaxNodeAction(syntax.ExemptionAttribute, SyntaxKind.Attribute);

                var ops = new OperationChecks(known);
                start.RegisterOperationAction(ops.Literal, OperationKind.Literal);
                start.RegisterOperationAction(ops.Conversion, OperationKind.Conversion);
                start.RegisterOperationAction(ops.MemberUse,
                    OperationKind.Invocation, OperationKind.PropertyReference,
                    OperationKind.FieldReference, OperationKind.EventReference);
                start.RegisterOperationAction(ops.Loop, OperationKind.Loop);
            });
        }
    }
}
