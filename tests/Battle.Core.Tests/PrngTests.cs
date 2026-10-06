using Automatic.Battle.Math;
using Xunit;

namespace Automatic.Battle.Tests;

public class PrngTests
{
    // Reference values produced by daydayup engine/math/prng.ts (engine v78) on 2026-10-05.
    // If these break, the port has diverged from the proven TS generator.
    [Theory]
    [InlineData(0u, 1587069247u, new[] { 543, 650, 463, 66, 85, 891 })]
    [InlineData(1u, 4034652357u, new[] { 871, 292, 320, 437, 942, 628 })]
    [InlineData(42u, 1892812290u, new[] { 703, 307, 85, 895, 155, 827 })]
    [InlineData(4294967295u, 1923535031u, new[] { 314, 53, 480, 737, 421, 601 })]
    public void MatchesTypeScriptReference(uint seed, uint stateAfter, int[] expected)
    {
        var prng = new Prng(seed);
        var actual = new int[expected.Length];
        for (int i = 0; i < actual.Length; i++) actual[i] = prng.NextInt(1000);
        Assert.Equal(expected, actual);
        Assert.Equal(stateAfter, prng.Peek());
    }

    [Fact]
    public void ShuffleMatchesTypeScriptReference()
    {
        var list = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        new Prng(7).Shuffle(list);
        Assert.Equal(new[] { 4, 2, 3, 1, 7, 8, 0, 5, 6, 9 }, list);
    }

    [Fact]
    public void ShuffleReachesAllOrdersOfFive()
    {
        var seen = new HashSet<string>();
        for (uint seed = 1; seed <= 6000; seed++)
        {
            var list = new List<int> { 0, 1, 2, 3, 4 };
            new Prng(seed).Shuffle(list);
            seen.Add(string.Join(",", list));
        }
        Assert.Equal(120, seen.Count);
    }

    [Fact]
    public void WeightedIndexNeverPicksZeroWeight()
    {
        var prng = new Prng(3);
        var weights = new[] { 0, 5, 0, 5 };
        for (int i = 0; i < 1000; i++)
        {
            int idx = prng.WeightedIndex(weights);
            Assert.True(idx == 1 || idx == 3);
        }
    }

    [Fact]
    public void NextIntRejectsNonPositiveMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Prng(1).NextInt(0));
    }
}
