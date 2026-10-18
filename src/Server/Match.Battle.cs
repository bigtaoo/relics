using System.Diagnostics;
using Automatic.Net.Battles;
using Automatic.Net.Protocol;

namespace Automatic.Server;

/// <summary>
/// The battle phase (design/01 §3-4, §6): pair the living players, freeze their boards into one BattleRound
/// with a seed per battle, broadcast it, and simulate every battle here too. Clients report each battle's
/// end hash; ours is the result whatever they say, a different hash is only logged and counted.
/// </summary>
public sealed partial class Match
{
    private BattleRound? currentRound;
    private BattleOutcome[] outcomes = Array.Empty<BattleOutcome>();

    private void StartBattle()
    {
        var alive = Seats.Where(s => s.Alive).ToList();
        for (int i = alive.Count - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (alive[i], alive[j]) = (alive[j], alive[i]);
        }
        long t0 = ServerClock.NowMs + Options.BattleLeadMs;
        var round = new BattleRound
        {
            Round = Round, T0Ms = t0, TickHz = Options.TickHz, MaxTicks = Options.MaxTicks,
            EndsMs = t0 + (long)Options.MaxTicks * 1000 / Options.TickHz + Options.ResultGraceMs,
        };
        for (int i = 0; i < alive.Count; i += 2)
        {
            var a = alive[i];
            var spec = new BattleSpec { BattleId = round.Battles.Count, PlayerA = a.Id, A = a.Board.Clone(), Seed = NextSeed() };
            if (i + 1 < alive.Count)
            {
                spec.PlayerB = alive[i + 1].Id;
                spec.B = alive[i + 1].Board.Clone();
            }
            else
            {
                // Odd count: fight a ghost, a copy of another living player's board that takes no damage.
                var other = alive[NextInt(alive.Count - 1)];
                spec.PlayerB = -1;
                spec.B = other.Board.Clone();
            }
            round.Battles.Add(spec);
        }

        var sw = Stopwatch.StartNew();
        var sim = new RoundSim(round, recordEvents: false);
        sim.AdvanceTo(round.MaxTicks);
        outcomes = new BattleOutcome[sim.Battles.Length];
        for (int i = 0; i < outcomes.Length; i++)
        {
            var b = sim.Battles[i];
            outcomes[i] = new BattleOutcome { BattleId = i, Winner = b.Over ? b.Winner : 2, Hash = b.Hash(), EndTick = b.Tick };
        }
        Stats.Current.SimMicros = sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
        Stats.Current.Battles = outcomes.Length;

        currentRound = round;
        Phase = Phase.Battle;
        phaseEndsMs = round.EndsMs;
        deadline = phaseEndsMs;
        Stats.Current.BattleRoundBytes = Broadcast(new ServerMessage { Battle = round });
    }

    private void OnReport(Seat seat, BattleReport r)
    {
        if (Phase != Phase.Battle || r.Round != Round || r.BattleId < 0 || r.BattleId >= outcomes.Length)
        {
            Stats.Current.Stray++;
            return;
        }
        seat.ReportsThisRound++;
        var o = outcomes[r.BattleId];
        if (r.Hash == o.Hash && r.EndTick == o.EndTick)
        {
            Stats.Current.Matched++;
            return;
        }
        Stats.Current.Mismatched++;
        Options.Log($"[Match] DESYNC round {Round} battle {r.BattleId} seat {seat.Id}: " +
                    $"client {r.Hash:x16}@{r.EndTick} server {o.Hash:x16}@{o.EndTick}");
    }

    private void EndRound()
    {
        var round = currentRound!;
        foreach (var o in outcomes)
        {
            var spec = round.Battles[o.BattleId];
            if (o.Winner == 2)
            {
                Damage(spec.PlayerA, 1);
                Damage(spec.PlayerB, 1);
            }
            else Damage(o.Winner == 0 ? spec.PlayerB : spec.PlayerA, 2 + Round);
        }
        // Everyone who fell this round shares the round; the one with more health left places higher.
        var fallen = Seats.Where(s => s.Place == 0 && s.Hp <= 0).OrderBy(s => s.Hp).ThenByDescending(s => s.Id).ToList();
        int aliveAfter = Seats.Count(s => s.Place == 0) - fallen.Count;
        for (int i = 0; i < fallen.Count; i++) fallen[i].Place = aliveAfter + fallen.Count - i;

        int expected = outcomes.Length;
        foreach (var seat in Seats)
            if (seat.Connected && seat.ReportsThisRound < expected) Stats.Current.Missing += expected - seat.ReportsThisRound;

        var result = new RoundResult { Round = Round };
        result.Outcomes.AddRange(outcomes);
        result.Players.AddRange(Seats.Select(s => s.Info()));
        Broadcast(new ServerMessage { RoundResult = result });
        currentRound = null;

        Stats.EndRound(Seats);
        Options.Log($"[Match] {Stats.Current.Describe()}");
        if (Seats.Count(s => s.Alive) <= 1 || Round >= Options.MaxRounds) EndMatch();
        else StartPrep();
    }

    private void Damage(int seatId, int amount)
    {
        if (seatId < 0) return; // ghost
        var seat = Seats[seatId];
        if (seat.Alive) seat.Hp -= amount;
    }
}
