using System.Reflection;
using Xunit;

namespace Automatic.Battle.Tests;

public class DeterminismTests
{
    private static readonly Assembly Core = typeof(EnforceDeterminismAttribute).Assembly;

    [Fact]
    public void CoreIsMarkedForTheAnalyzer()
    {
        // Without the marker the analyzer silently checks nothing.
        Assert.NotNull(Core.GetCustomAttribute<EnforceDeterminismAttribute>());
    }

    [Fact]
    public void ExemptionsAreTheReviewedList()
    {
        // Adding an exemption? Add it here and to design/01 section 6, with the reason.
        var expected = new string[] { };

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var actual = Core.GetTypes()
            .SelectMany(t => new MemberInfo[] { t }.Concat(t.GetMembers(all)))
            .Where(m => m.GetCustomAttribute<DeterminismExemptAttribute>() != null)
            .Select(m => m is Type t ? t.FullName! : m.DeclaringType!.FullName + "." + m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
    }
}
