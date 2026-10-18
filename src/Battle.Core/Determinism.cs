using System;

// Opts this assembly into the determinism analyzer (src/Battle.Core.Analyzers). Unity runs the
// analyzer for every assembly that references Battle.Core; only marked assemblies are checked.
[assembly: Automatic.Battle.EnforceDeterminism]

namespace Automatic.Battle
{
    /// <summary>Marks an assembly whose code must give bit-identical results everywhere.</summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class EnforceDeterminismAttribute : Attribute
    {
    }

    /// <summary>
    /// Reviewed escape from the determinism analyzer for one member, local function or type.
    /// Only for code that cannot change a battle result (diagnostics, presentation helpers).
    /// Every use is listed in design/01 section 6 and in DeterminismTests.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor
        | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event,
        Inherited = false)]
    public sealed class DeterminismExemptAttribute : Attribute
    {
        public DeterminismExemptAttribute(string reason) => Reason = reason;

        public string Reason { get; }
    }
}
