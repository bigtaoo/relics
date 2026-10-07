using Automatic.Battle.Math;
using Automatic.Battle.Sim;
using Xunit;

namespace Automatic.Battle.Tests;

public class CrowdBattleTests
{
    // Locked so every runtime (.NET server, IL2CPP + HybridCLR on Windows, Android, iOS) can be
    // checked against the same values: SimBench in the player logs them (design/01 §6).
    [Theory]
    [InlineData("duel16", 0x040c957b5c4950dfUL)]
    [InlineData("summon300", 0x8004355070fae8ebUL)]
    [InlineData("crowd300", 0x660a6f5c9dccd6faUL)]
    public void GoldenHash(string name, ulong expected)
    {
        var b = new CrowdBattle(Make(name, 1));
        b.Run();
        Assert.Equal(expected, b.Hash());
    }

    [Fact]
    public void SameSeedSameBattle()
    {
        var a = new CrowdBattle(SimConfig.Summon300(7));
        var b = new CrowdBattle(SimConfig.Summon300(7));
        a.Run();
        b.Run();
        Assert.Equal(a.Hash(), b.Hash());
        Assert.Equal(a.Events.Count, b.Events.Count);
        Assert.NotEqual(a.Hash(), Run(SimConfig.Summon300(8)));
    }

    [Fact]
    public void EventsDoNotChangeTheOutcome()
    {
        var quiet = SimConfig.Crowd300(3);
        quiet.RecordEvents = false;
        Assert.Equal(Run(SimConfig.Crowd300(3)), Run(quiet));
    }

    [Fact]
    public void SidesWinAboutEqually()
    {
        var wins = new int[3];
        for (uint s = 1; s <= 30; s++)
        {
            var b = new CrowdBattle(SimConfig.Duel16(s));
            b.Run();
            wins[b.Winner]++;
        }
        Assert.InRange(wins[0], 8, 22);
    }

    [Fact]
    public void SqrtIsTheFloor()
    {
        for (ulong n = 0; n < 20000; n++) Check(n);
        for (ulong n = 1; n < ulong.MaxValue / 3; n = n * 3 + 1) Check(n);
        Assert.Equal(FP.FromInt(3), FixedMath.Sqrt(FP.FromInt(9)));
        Assert.Equal(FP.FromRaw(14142), FixedMath.Sqrt(FP.FromInt(2)));

        static void Check(ulong n)
        {
            var r = FixedMath.ISqrt(n);
            Assert.True(r * r <= n && (r + 1) * (r + 1) > n, $"isqrt({n}) = {r}");
        }
    }

    private static ulong Run(SimConfig c)
    {
        var b = new CrowdBattle(c);
        b.Run();
        return b.Hash();
    }

    private static SimConfig Make(string name, uint seed) => name switch
    {
        "duel16" => SimConfig.Duel16(seed),
        "summon300" => SimConfig.Summon300(seed),
        _ => SimConfig.Crowd300(seed),
    };
}
