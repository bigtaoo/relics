using Automatic.Net.Battles;
using Automatic.Net.Protocol;
using Google.Protobuf;
using Xunit;

namespace Automatic.Match.Tests;

public class BattleFactoryTests
{
    private static BattleSpec Spec(uint seed)
    {
        var a = BattleFactory.DefaultBoard();
        var b = BattleFactory.DefaultBoard();
        a.Pieces[0].Star = 3;
        a.Pieces[5].Col = 3;
        a.Pieces[5].Row = 0;
        b.Pieces[2].Star = 2;
        return new BattleSpec { BattleId = 0, PlayerA = 0, PlayerB = 1, A = a, B = b, Seed = seed };
    }

    private static (ulong Hash, int Tick) Run(BattleSpec spec)
    {
        var battle = BattleFactory.Create(spec, recordEvents: false);
        battle.Run();
        return (battle.Hash(), battle.Tick);
    }

    [Fact]
    public void SameSpec_SameResult_AlsoAfterAWireRoundTrip()
    {
        var spec = Spec(42);
        var copy = BattleSpec.Parser.ParseFrom(spec.ToByteArray());
        Assert.Equal(Run(spec), Run(copy));
    }

    [Fact]
    public void BoardChangesTheBattle()
    {
        var plain = new BattleSpec { Seed = 42, A = BattleFactory.DefaultBoard(), B = BattleFactory.DefaultBoard() };
        Assert.NotEqual(Run(plain).Hash, Run(Spec(42)).Hash);
    }

    [Fact]
    public void RecordingEventsDoesNotChangeTheResult()
    {
        var spec = Spec(7);
        var recorded = BattleFactory.Create(spec, recordEvents: true);
        recorded.Run();
        Assert.Equal(Run(spec).Hash, recorded.Hash());
    }

    [Fact]
    public void RoundSim_CatchUpEqualsSteppingAlong()
    {
        var round = new BattleRound { Round = 1, T0Ms = 0, TickHz = 30, MaxTicks = 900 };
        round.Battles.Add(Spec(1));
        round.Battles.Add(Spec(2));
        var live = new RoundSim(round, false);
        for (int t = 1; t <= 900; t++) live.AdvanceTo(t);
        var late = new RoundSim(round, false);
        late.AdvanceTo(late.TickAt(60_000));
        Assert.Equal(900, late.FastForwardTicks);
        for (int i = 0; i < 2; i++) Assert.Equal(live.Battles[i].Hash(), late.Battles[i].Hash());
    }
}
