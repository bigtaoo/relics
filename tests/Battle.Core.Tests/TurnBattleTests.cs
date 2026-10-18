using System.Collections.Generic;
using System.Linq;
using Automatic.Battle.Turn;
using Xunit;
using Xunit.Abstractions;

namespace Automatic.Battle.Tests;

public class TurnBattleTests
{
    private readonly ITestOutputHelper output;

    public TurnBattleTests(ITestOutputHelper output) => this.output = output;

    private static TurnUnitSpec Unit(int side, int row, int col, int hp, int atk, int crit) =>
        new TurnUnitSpec { Side = side, Row = row, Col = col, Hp = hp, MaxHp = hp, Atk = atk, CritPercent = crit };

    /// <summary>The shop demo's lineup (client BattleLineup): three against three, a zheng with the flames, one hand.</summary>
    private static TurnSetup Lineup(uint seed)
    {
        var s = new TurnSetup { Seed = seed };
        s.Units.Add(Unit(0, 0, 1, 900, 260, 20));
        s.Units.Add(Unit(0, 0, 2, 800, 280, 15));
        s.Units.Add(Unit(0, 0, 3, 900, 260, 25));
        var zheng = Unit(0, 1, 4, 700, 170, 20);
        zheng.Skill = TurnSkill.FiveTailFlame;
        zheng.SkillPercent = 200;
        zheng.SkillRate = 50;
        s.Units.Add(zheng);
        s.Units.Add(Unit(1, 0, 1, 700, 300, 15));
        s.Units.Add(Unit(1, 0, 2, 600, 280, 15));
        s.Units.Add(Unit(1, 0, 3, 800, 300, 15));
        s.Hands.Add(new TurnHandSpec { Side = 0, DamagePercent = 30, Leader = 0 });
        return s;
    }

    private static TurnBattle Run(uint seed)
    {
        var b = new TurnBattle(Lineup(seed));
        b.Run();
        return b;
    }

    [Fact]
    public void PrintsOneBattle()
    {
        var b = Run(1);
        foreach (var e in b.Events) output.WriteLine(e.ToString());
        output.WriteLine($"winner {b.Winner} tick {b.Tick} hash {b.Hash():x16}");
        var w = new int[3];
        int skills = 0, ticks = 0;
        for (uint seed = 1; seed <= 200; seed++)
        {
            var r = Run(seed);
            w[r.Winner]++;
            ticks += r.Tick;
            skills += r.Events.Count(e => e.Kind == TurnEventKind.SkillCast);
        }
        output.WriteLine($"lineup: side 0 {w[0]}, side 1 {w[1]}, draw {w[2]}, ticks avg {ticks / 200}, skills {skills}");
    }

    [Fact]
    public void GoldenHash()
    {
        // Changing this value means the battle rules changed: bump EngineVersion (design/07 §5).
        Assert.Equal(0x2bb3323495291ac8UL, Run(1).Hash());
    }

    [Fact]
    public void SameSeedSameEvents()
    {
        for (uint seed = 1; seed <= 50; seed++)
        {
            var a = Run(seed);
            var b = Run(seed);
            Assert.Equal(a.Events, b.Events);
            Assert.Equal(a.Hash(), b.Hash());
        }
    }

    [Fact]
    public void FastForwardThenPlayIsTheSameBattle()
    {
        var whole = Run(7);
        foreach (var at in new[] { 0, 1, 7, 30, 61, 100, whole.Tick })
        {
            var b = new TurnBattle(Lineup(7));
            b.RunTo(at);
            Assert.True(b.Events.All(e => e.Tick < at));
            Assert.Equal(whole.Events.Count(e => e.Tick < at), b.Events.Count);
            b.Run();
            Assert.Equal(whole.Events, b.Events);
            Assert.Equal(whole.Hash(), b.Hash());
        }
    }

    [Fact]
    public void TimelineFollowsTheTimingTable()
    {
        for (uint seed = 1; seed <= 50; seed++)
        {
            var b = Run(seed);
            var ev = b.Events;
            Assert.True(ev.Zip(ev.Skip(1), (x, y) => x.Tick <= y.Tick).All(ok => ok));
            Assert.Equal(TurnEventKind.HandTrigger, ev[0].Kind);
            Assert.Equal(0, ev[0].Tick);
            var end = ev.Last();
            Assert.Equal(TurnEventKind.BattleEnd, end.Kind);
            Assert.Equal(b.Winner, end.Amount);
            Assert.True(end.Tick <= TurnTiming.MaxTicks);
            // One action at a time: every start is at least one action length plus the gap after the previous one.
            var starts = ev.Where(e => e.Kind is TurnEventKind.HandTrigger or TurnEventKind.AttackStart or TurnEventKind.SkillCast).ToList();
            for (int i = 1; i < starts.Count; i++)
                Assert.Equal(starts[i - 1].Tick + TurnTiming.Length(starts[i - 1].Act) + TurnTiming.GapTicks, starts[i].Tick);
            foreach (var hit in ev.Where(e => e.Kind == TurnEventKind.AttackHit))
                Assert.Contains(starts, s => s.Act == TurnAct.Attack && s.Tick + TurnTiming.AttackHitAt == hit.Tick);
            foreach (var hit in ev.Where(e => e.Kind == TurnEventKind.SkillHit))
                Assert.Contains(starts, s => s.Act == TurnAct.Skill && s.Tick + TurnTiming.SkillHitAt == hit.Tick);
        }
    }

    [Fact]
    public void HealthFollowsTheDamageEvents()
    {
        var setup = Lineup(3);
        var b = Run(3);
        for (int i = 0; i < setup.Units.Count; i++)
        {
            var dealt = b.Events.Where(e => e.Kind == TurnEventKind.Damage && e.Target == i).Sum(e => e.Amount);
            var died = b.Events.Count(e => e.Kind == TurnEventKind.Death && e.Target == i);
            Assert.Equal(System.Math.Max(0, setup.Units[i].Hp - dealt), b.Units[i].Hp);
            Assert.Equal(b.Units[i].Alive ? 0 : 1, died);
            // Nothing acts or is hit after its death.
            var deathTick = b.Events.Where(e => e.Kind == TurnEventKind.Death && e.Target == i).Select(e => e.Tick).DefaultIfEmpty(int.MaxValue).First();
            Assert.DoesNotContain(b.Events, e => e.Tick > deathTick && (e.Actor == i || e.Target == i));
        }
    }

    [Fact]
    public void TheHandRaisesDamageByItsPercent()
    {
        var with = Run(5);
        var setup = Lineup(5);
        setup.Hands.Clear();
        var without = new TurnBattle(setup);
        without.Run();
        // Same draws, same first blow: only the bonus differs.
        var a = with.Events.First(e => e.Kind == TurnEventKind.Damage);
        var b = without.Events.First(e => e.Kind == TurnEventKind.Damage);
        Assert.Equal(a.Crit, b.Crit);
        Assert.InRange(a.Amount, b.Amount * 13 / 10 - 1, b.Amount * 13 / 10 + 1);
    }

    [Fact]
    public void BothSidesCanWin()
    {
        // Mirrored three against three: side 0 moves first, the draws decide the rest.
        var winners = new List<int>();
        for (uint seed = 1; seed <= 200; seed++)
        {
            var s = new TurnSetup { Seed = seed };
            for (int side = 0; side < 2; side++)
            for (int col = 1; col <= 3; col++)
                s.Units.Add(Unit(side, 0, col, 1000, 300, 20));
            var b = new TurnBattle(s);
            b.Run();
            winners.Add(b.Winner);
        }
        output.WriteLine($"mirror: side 0 {winners.Count(w => w == 0)}, side 1 {winners.Count(w => w == 1)}, draw {winners.Count(w => w == 2)}");
        Assert.Contains(0, winners);
        Assert.Contains(1, winners);
    }
}
