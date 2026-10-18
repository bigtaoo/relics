using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Automatic.Battle.Analyzers
{
    /// <summary>Symbols resolved once per compilation, and the type classification built on them.</summary>
    internal sealed class KnownTypes
    {
        public const string EnforceAttributeName = "Automatic.Battle.EnforceDeterminismAttribute";
        public const string ExemptAttributeName = "Automatic.Battle.DeterminismExemptAttribute";

        private static readonly (string Name, DiagnosticDescriptor Rule)[] BannedNames =
        {
            ("System.Math", Rules.SystemMath),
            ("System.MathF", Rules.SystemMath),
            ("System.DateTime", Rules.Clock),
            ("System.DateTimeOffset", Rules.Clock),
            ("System.TimeZoneInfo", Rules.Clock),
            ("System.Environment", Rules.Clock),
            ("System.Diagnostics.Stopwatch", Rules.Clock),
            ("System.Random", Rules.Random),
            ("System.Security.Cryptography.RandomNumberGenerator", Rules.Random),
            ("System.Guid", Rules.Guid),
            ("System.HashCode", Rules.UnstableHash),
        };

        // Matched on the type itself and its base classes, not on interfaces it implements
        // (SortedDictionary implements IDictionary but iterates in key order).
        private static readonly string[] UnorderedNames =
        {
            "System.Collections.Generic.Dictionary`2",
            "System.Collections.Generic.Dictionary`2+KeyCollection",
            "System.Collections.Generic.Dictionary`2+ValueCollection",
            "System.Collections.Generic.HashSet`1",
            "System.Collections.Generic.IDictionary`2",
            "System.Collections.Generic.IReadOnlyDictionary`2",
            "System.Collections.Generic.ISet`1",
            "System.Collections.Generic.IReadOnlySet`1",
            "System.Collections.Hashtable",
            "System.Collections.IDictionary",
            "System.Collections.Concurrent.ConcurrentDictionary`2",
            "System.Collections.Concurrent.ConcurrentBag`1",
            "System.Collections.Immutable.ImmutableDictionary`2",
            "System.Collections.Immutable.ImmutableHashSet`1",
            "System.Collections.Immutable.IImmutableDictionary`2",
            "System.Collections.Immutable.IImmutableSet`1",
        };

        private readonly Dictionary<INamedTypeSymbol, DiagnosticDescriptor> _banned;
        private readonly HashSet<INamedTypeSymbol> _unordered;

        public INamedTypeSymbol ExemptAttribute { get; }

        private KnownTypes(Compilation compilation, INamedTypeSymbol exemptAttribute)
        {
            ExemptAttribute = exemptAttribute;
            _banned = new Dictionary<INamedTypeSymbol, DiagnosticDescriptor>(SymbolEqualityComparer.Default);
            foreach (var (name, rule) in BannedNames)
            {
                var type = compilation.GetTypeByMetadataName(name);
                if (type != null) _banned[type] = rule;
            }
            _unordered = new HashSet<INamedTypeSymbol>(
                UnorderedNames.Select(compilation.GetTypeByMetadataName).Where(t => t != null)!,
                SymbolEqualityComparer.Default);
        }

        /// <summary>
        /// Null unless the compilation opts in with [assembly: EnforceDeterminism]. Unity applies an
        /// analyzer to its asmdef and to every assembly referencing it; the marker keeps those out.
        /// </summary>
        public static KnownTypes? Create(Compilation compilation)
        {
            var enforce = compilation.GetTypeByMetadataName(EnforceAttributeName);
            var exempt = compilation.GetTypeByMetadataName(ExemptAttributeName);
            if (enforce == null || exempt == null) return null;
            var marked = compilation.Assembly.GetAttributes()
                .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, enforce));
            return marked ? new KnownTypes(compilation, exempt) : null;
        }

        /// <summary>The rule a named type breaks by itself (not through its type arguments), or null.</summary>
        public DiagnosticDescriptor? Classify(ITypeSymbol? type)
        {
            if (type == null) return null;
            if (IsFloatingPoint(type)) return Rules.FloatingPoint;
            for (var t = (type as INamedTypeSymbol)?.OriginalDefinition; t != null; t = t.ContainingType)
            {
                if (_banned.TryGetValue(t, out var rule)) return rule;
                if (IsThreadingNamespace(t.ContainingNamespace)) return Rules.Threading;
            }
            return null;
        }

        /// <summary>Like <see cref="Classify"/>, but also looks into arrays, pointers and type arguments.</summary>
        public DiagnosticDescriptor? ClassifyDeep(ITypeSymbol? type)
        {
            switch (type)
            {
                case null: return null;
                case IArrayTypeSymbol array: return ClassifyDeep(array.ElementType);
                case IPointerTypeSymbol pointer: return ClassifyDeep(pointer.PointedAtType);
            }
            var rule = Classify(type);
            if (rule != null || !(type is INamedTypeSymbol named)) return rule;
            foreach (var arg in named.TypeArguments)
            {
                rule = ClassifyDeep(arg);
                if (rule != null) return rule;
            }
            return null;
        }

        public bool IsUnordered(ITypeSymbol? type)
        {
            if (type is ITypeParameterSymbol) return false;
            for (var t = type as INamedTypeSymbol; t != null; t = t.BaseType)
                if (_unordered.Contains(t.OriginalDefinition)) return true;
            return false;
        }

        public static bool IsFloatingPoint(ITypeSymbol? type) =>
            type != null && (type.SpecialType == SpecialType.System_Single
                || type.SpecialType == SpecialType.System_Double
                || type.SpecialType == SpecialType.System_Decimal);

        public static bool IsEnumerable(ITypeSymbol? type) =>
            type != null && (type.SpecialType == SpecialType.System_Collections_IEnumerable
                || type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable));

        private static bool IsThreadingNamespace(INamespaceSymbol? ns)
        {
            if (ns == null || ns.IsGlobalNamespace) return false;
            var name = ns.ToDisplayString();
            return name == "System.Threading" || name.StartsWith("System.Threading.") || name == "System.Timers";
        }
    }
}
