using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Automatic.Battle.Analyzers
{
    /// <summary>
    /// Diagnostics for the Battle.Core determinism rules. All are errors and NotConfigurable:
    /// #pragma, NoWarn, .editorconfig and [SuppressMessage] cannot turn them off.
    /// The one escape is [DeterminismExempt("reason")] on the enclosing member or type.
    /// </summary>
    internal static class Rules
    {
        private const string Category = "Determinism";
        private const string Escape =
            " Battle.Core must give bit-identical results on every client and the server." +
            " If this use really cannot change a battle result, mark the enclosing member" +
            " [DeterminismExempt(\"why\")] and list it in design/01 section 6.";

        public static readonly DiagnosticDescriptor FloatingPoint = Make(
            "DET001", "Floating point in Battle.Core",
            "'{0}' is floating point; use FP",
            "float, double and decimal round differently across CPUs, compilers and IL2CPP.");

        public static readonly DiagnosticDescriptor SystemMath = Make(
            "DET002", "System.Math in Battle.Core",
            "'{0}' is not allowed; use FixedMath or FP",
            "System.Math and System.MathF work on floating point.");

        public static readonly DiagnosticDescriptor Clock = Make(
            "DET003", "Clock or environment in Battle.Core",
            "'{0}' reads the clock or the machine; take time from the simulation tick",
            "DateTime, DateTimeOffset, Stopwatch, TimeZoneInfo and Environment differ between machines and runs.");

        public static readonly DiagnosticDescriptor Random = Make(
            "DET004", "System random in Battle.Core",
            "'{0}' is not allowed; draw from the injected Prng",
            "System.Random's sequence is not specified across runtimes, and crypto RNGs are not reproducible.");

        public static readonly DiagnosticDescriptor Guid = Make(
            "DET005", "Guid in Battle.Core",
            "'{0}' is not allowed; use ids assigned by the simulation",
            "New Guids are random.");

        public static readonly DiagnosticDescriptor Threading = Make(
            "DET006", "Threading or async in Battle.Core",
            "'{0}' introduces threads or asynchrony; Battle.Core runs on one thread",
            "System.Threading, System.Threading.Tasks, System.Timers, async/await, lock and volatile are banned.");

        public static readonly DiagnosticDescriptor UnorderedIteration = Make(
            "DET007", "Unordered collection iterated in Battle.Core",
            "Iterating '{0}' has no defined order; iterate a List or sort by id first",
            "Dictionary and HashSet order depends on hashes and insertion history. Lookups are fine; foreach, LINQ and copying out are not.");

        public static readonly DiagnosticDescriptor UnstableHash = Make(
            "DET008", "Unstable hash code in Battle.Core",
            "'{0}' is not stable across processes; hash explicit integer fields",
            "string.GetHashCode is randomized per process, object.GetHashCode is per instance and System.HashCode is seeded per process.");

        public static readonly DiagnosticDescriptor ExemptionReason = Make(
            "DET009", "Determinism exemption without a reason",
            "[DeterminismExempt] needs a non-empty reason",
            "Every exemption is reviewed; the reason says why the code cannot affect a battle result.");

        public static readonly ImmutableArray<DiagnosticDescriptor> All = ImmutableArray.Create(
            FloatingPoint, SystemMath, Clock, Random, Guid, Threading, UnorderedIteration, UnstableHash, ExemptionReason);

        private static DiagnosticDescriptor Make(string id, string title, string message, string description) =>
            new DiagnosticDescriptor(
                id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
                description: description + (id == "DET009" ? "" : Escape),
                customTags: WellKnownDiagnosticTags.NotConfigurable);
    }
}
