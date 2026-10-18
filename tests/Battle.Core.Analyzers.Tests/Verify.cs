using Automatic.Battle.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Automatic.Battle.Analyzers.Tests;

/// <summary>Runs the analyzer on a snippet compiled like Battle.Core: netstandard2.1, C# 9, marked assembly.</summary>
internal static class Verify
{
    private const string Attributes = @"
namespace Automatic.Battle
{
    [System.AttributeUsage(System.AttributeTargets.Assembly)]
    public sealed class EnforceDeterminismAttribute : System.Attribute { }

    public sealed class DeterminismExemptAttribute : System.Attribute
    {
        public DeterminismExemptAttribute(string reason) => Reason = reason;
        public string Reason { get; }
    }
}";

    public const string Marker = "[assembly: Automatic.Battle.EnforceDeterminism]";

    /// <summary>Expects exactly the diagnostics marked in <paramref name="source"/> as {|DET00x:...|}.</summary>
    public static Task Analyze(string source, bool marked = true)
    {
        var test = new CSharpAnalyzerTest<DeterminismAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard21,
            TestState = { Sources = { (marked ? Marker : "") + Attributes, source } },
        };
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var project = solution.GetProject(projectId)!;
            var parse = ((CSharpParseOptions)project.ParseOptions!).WithLanguageVersion(LanguageVersion.CSharp9);
            return solution.WithProjectParseOptions(projectId, parse);
        });
        return test.RunAsync();
    }
}
