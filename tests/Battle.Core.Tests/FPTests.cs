using Automatic.Battle.Math;
using Xunit;

namespace Automatic.Battle.Tests;

public class FPTests
{
    [Fact]
    public void FactoriesScaleCorrectly()
    {
        Assert.Equal(30000, FP.FromInt(3).Raw);
        Assert.Equal(3550, FP.FromRatio(355, 1000).Raw);
        Assert.Equal(12500, FP.FromPercent(125).Raw);
        Assert.Equal(7, FP.FromRaw(7).Raw);
    }

    [Fact]
    public void MultiplyAndDivideTruncateTowardZero()
    {
        // 10 * 0.3333 = 3.333
        Assert.Equal("3.333", (FP.FromInt(10) * FP.FromRatio(1, 3)).ToString());
        // 1 / 3 = 0.3333 (truncated)
        Assert.Equal(3333, (FP.One / FP.FromInt(3)).Raw);
        // -1 / 3 truncates toward zero, not toward negative infinity
        Assert.Equal(-3333, (-FP.One / FP.FromInt(3)).Raw);
        Assert.Equal(-3, FP.FromRatio(-35, 10).TruncateToLong());
    }

    [Fact]
    public void IntOperandIsDimensionless()
    {
        Assert.Equal(FP.FromInt(6), FP.FromInt(2) * 3);
        Assert.Equal(FP.FromInt(6), 3 * FP.FromInt(2));
        Assert.Equal(FP.FromRatio(1, 2), FP.One / 2);
    }

    [Fact]
    public void OverflowThrowsInsteadOfWrapping()
    {
        var big = FP.FromRaw(long.MaxValue / 2);
        Assert.Throws<OverflowException>(() => big + big + big);
        Assert.Throws<OverflowException>(() => big * big);
    }

    [Fact]
    public void DivisionByZeroThrows()
    {
        Assert.Throws<DivideByZeroException>(() => FP.One / FP.Zero);
        Assert.Throws<DivideByZeroException>(() => FP.One / 0);
        Assert.Throws<DivideByZeroException>(() => FP.FromRatio(1, 0));
    }

    [Fact]
    public void ComparisonAndClamp()
    {
        var a = FP.FromInt(1);
        var b = FP.FromInt(2);
        var a2 = FP.FromInt(1);
        Assert.True(a < b && b > a && a <= a2 && a >= a2 && a != b && a == a2);
        Assert.Equal(b, FP.Clamp(FP.FromInt(5), a, b));
        Assert.Equal(a, FP.Clamp(FP.Zero, a, b));
        Assert.Equal(a, FP.Min(a, b));
        Assert.Equal(b, FP.Max(a, b));
    }

    [Fact]
    public void ToStringIsExact()
    {
        Assert.Equal("0", FP.Zero.ToString());
        Assert.Equal("-1.25", (-FP.FromPercent(125)).ToString());
        Assert.Equal("0.0001", FP.FromRaw(1).ToString());
    }
}
