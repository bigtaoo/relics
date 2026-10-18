using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Automatic.Battle.Analyzers
{
    /// <summary>Checks on what the source implies: literal types, conversions, metadata members, foreach collections.</summary>
    internal sealed class OperationChecks
    {
        private readonly KnownTypes _known;

        public OperationChecks(KnownTypes known) => _known = known;

        /// <summary>`1.5`, `2f`, `3m`.</summary>
        public void Literal(OperationAnalysisContext c)
        {
            if (KnownTypes.IsFloatingPoint(c.Operation.Type))
                Report(c, c.Operation.Syntax, c.Operation.Syntax.GetLocation(), Rules.FloatingPoint, c.Operation.Syntax.ToString());
        }

        public void Conversion(OperationAnalysisContext c)
        {
            var conversion = (IConversionOperation)c.Operation;
            var operand = conversion.Operand;

            // An integer passed to a framework method taking double: nothing in the source says double.
            if (conversion.IsImplicit
                && KnownTypes.IsFloatingPoint(conversion.Type)
                && !KnownTypes.IsFloatingPoint(operand.Type)
                && conversion.Parent is IArgumentOperation argument
                && argument.Parameter != null
                && !IsInSource(argument.Parameter.ContainingSymbol))
            {
                Report(c, operand.Syntax, operand.Syntax.GetLocation(), Rules.FloatingPoint,
                    argument.Parameter.ContainingSymbol.Name + "(" + argument.Parameter.Type.ToDisplayString() + ")");
            }

            // A Dictionary/HashSet handed to anything that will enumerate it: LINQ, List ctor, AddRange, string.Join.
            if (_known.IsUnordered(operand.Type)
                && !_known.IsUnordered(conversion.Type)
                && KnownTypes.IsEnumerable(conversion.Type)
                && !(conversion.Parent is IForEachLoopOperation))
            {
                Report(c, operand.Syntax, operand.Syntax.GetLocation(), Rules.UnorderedIteration, operand.Type!.ToDisplayString());
            }
        }

        public void MemberUse(OperationAnalysisContext c)
        {
            ISymbol member;
            ITypeSymbol? result;
            switch (c.Operation)
            {
                case IInvocationOperation invocation:
                    member = invocation.TargetMethod;
                    result = invocation.TargetMethod.ReturnType;
                    CheckInvocation(c, invocation);
                    break;
                case IPropertyReferenceOperation property: member = property.Property; result = property.Property.Type; break;
                case IFieldReferenceOperation field: member = field.Field; result = field.Field.Type; break;
                case IEventReferenceOperation ev: member = ev.Event; result = ev.Event.Type; break;
                default: return;
            }

            // Source members are checked where they are declared; members of a banned type are
            // reported at the type name or wherever the receiver came from.
            if (IsInSource(member) || _known.Classify(member.ContainingType) != null) return;
            Report(c, c.Operation.Syntax, NameLocation(c.Operation.Syntax), _known.ClassifyDeep(result),
                member.ContainingType.Name + "." + member.Name);
        }

        public void Loop(OperationAnalysisContext c)
        {
            if (!(c.Operation is IForEachLoopOperation loop)) return;
            var collection = loop.Collection;
            while (collection is IConversionOperation conversion && conversion.IsImplicit) collection = conversion.Operand;
            if (_known.IsUnordered(collection.Type))
                Report(c, collection.Syntax, collection.Syntax.GetLocation(), Rules.UnorderedIteration, collection.Type!.ToDisplayString());
        }

        private void CheckInvocation(OperationAnalysisContext c, IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;
            if (IsUnstableHash(method))
                Report(c, invocation.Syntax, NameLocation(invocation.Syntax), Rules.UnstableHash,
                    method.ContainingType.ToDisplayString() + "." + method.Name);

            if ((method.Name == "GetEnumerator" || method.Name == "CopyTo") && _known.IsUnordered(invocation.Instance?.Type))
                Report(c, invocation.Syntax, NameLocation(invocation.Syntax), Rules.UnorderedIteration,
                    invocation.Instance!.Type!.ToDisplayString());
        }

        /// <summary>
        /// object/ValueType hashes are per instance or reflection-based; string hashes (also through
        /// StringComparer and EqualityComparer&lt;string&gt;) are randomized per process.
        /// </summary>
        private static bool IsUnstableHash(IMethodSymbol method)
        {
            if (method.Name != "GetHashCode") return false;
            var owner = method.ContainingType.SpecialType;
            return owner == SpecialType.System_Object
                || owner == SpecialType.System_ValueType
                || owner == SpecialType.System_String
                || method.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);
        }

        private static bool IsInSource(ISymbol symbol) => symbol.Locations.Any(l => l.IsInSource);

        private static Location NameLocation(SyntaxNode syntax) => syntax switch
        {
            InvocationExpressionSyntax invocation => NameLocation(invocation.Expression),
            MemberAccessExpressionSyntax access => access.Name.GetLocation(),
            MemberBindingExpressionSyntax binding => binding.Name.GetLocation(),
            _ => syntax.GetLocation(),
        };

        private void Report(OperationAnalysisContext c, SyntaxNode node, Location location, DiagnosticDescriptor? rule, string what)
        {
            if (rule == null) return;
            // Operations handed to analyzers always come from a semantic model.
            Exemptions.ReportUnlessExempt(c.ReportDiagnostic, _known, c.Operation.SemanticModel!, node, location, rule, what, c.CancellationToken);
        }
    }
}
