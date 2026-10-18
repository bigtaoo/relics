#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using Automatic.Battle.Sim;
using Automatic.Net.Protocol;

namespace Automatic.Net.Battles
{
    /// <summary>
    /// Every battle of one round, kept on the server's timeline: tick = (server now - t0) * tickHz
    /// (design/01 §3-4). The client steps all of them, not only its own, so it can spectate any battle and
    /// report every end hash. Arriving late (reconnect, spectating) is a plain fast-forward to the current tick.
    /// </summary>
    public sealed class RoundSim
    {
        public readonly BattleRound Round;
        public readonly CrowdBattle[] Battles;
        private readonly bool[] reported;

        /// <summary>Ticks simulated so far, the same for every battle (a finished battle stops counting itself).</summary>
        public int Tick { get; private set; }
        /// <summary>The first catch-up: how far behind the timeline this round started, and what it cost.</summary>
        public int FastForwardTicks { get; private set; } = -1;
        public long FastForwardMicros { get; private set; }

        public RoundSim(BattleRound round, bool recordEvents)
        {
            Round = round;
            Battles = new CrowdBattle[round.Battles.Count];
            reported = new bool[Battles.Length];
            for (int i = 0; i < Battles.Length; i++) Battles[i] = BattleFactory.Create(round.Battles[i], recordEvents);
        }

        public int TickAt(long serverMs)
        {
            long t = (serverMs - Round.T0Ms) * Round.TickHz / 1000;
            return t < 0 ? 0 : t > Round.MaxTicks ? Round.MaxTicks : (int)t;
        }

        public bool AllOver
        {
            get
            {
                for (int i = 0; i < Battles.Length; i++) if (!Battles[i].Over) return false;
                return true;
            }
        }

        /// <summary>Step every battle up to `tick`. The first call is the catch-up and is measured.</summary>
        public void AdvanceTo(int tick)
        {
            if (tick > Round.MaxTicks) tick = Round.MaxTicks;
            Stopwatch? sw = FastForwardTicks < 0 ? Stopwatch.StartNew() : null;
            int from = Tick;
            while (Tick < tick)
            {
                for (int i = 0; i < Battles.Length; i++) Battles[i].Step();
                Tick++;
            }
            if (sw != null)
            {
                FastForwardTicks = Tick - from;
                FastForwardMicros = sw.ElapsedTicks * 1000000 / Stopwatch.Frequency;
            }
        }

        /// <summary>Battles that ended since the last call, each returned once (to report its hash).</summary>
        public void TakeFinished(List<int> into)
        {
            for (int i = 0; i < Battles.Length; i++)
            {
                if (reported[i] || !Battles[i].Over) continue;
                reported[i] = true;
                into.Add(i);
            }
        }
    }
}
