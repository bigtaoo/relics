using System.Collections.Generic;
using Automatic.Battle.Math;

namespace Automatic.Battle.Turn
{
    /// <summary>
    /// Minimal turn-based battle, standing in for the Sanzhan rules until design/02 has its captures:
    /// hands go off before the first round, then the units act one at a time (02 §3: first row first,
    /// left to right, then the next row; the sides alternate, side 0 first). An action is a normal attack
    /// on the nearest enemy (front row first, then the nearest column) or, if the unit's skill fires, the
    /// skill. Every action has a fixed length in ticks (TurnTiming), so the event stream carries the whole
    /// timeline. The battle ends when a side is wiped out, or at TurnTiming.MaxTicks on remaining health.
    ///
    /// Step() runs one tick. RunTo(t) fast-forwards without presentation (spectating, reconnecting: 01 §4).
    /// </summary>
    public sealed partial class TurnBattle
    {
        public readonly List<TurnUnit> Units = new List<TurnUnit>();
        public readonly List<TurnEvent> Events = new List<TurnEvent>(256);
        /// <summary>The next tick to run: after RunTo(t), every event before tick t has been emitted.</summary>
        public int Tick { get; private set; }
        /// <summary>-1 while running, then the winning side, or 2 for a draw.</summary>
        public int Winner { get; private set; } = -1;
        public bool Over => Winner >= 0;
        public int Round { get; private set; }

        private readonly List<TurnHandSpec> hands;
        /// <summary>Crit and damage spread: two draws per blow, always both.</summary>
        private readonly Prng damage;
        /// <summary>Whether a skill fires: one draw per action of a unit that has a skill.</summary>
        private readonly Prng skills;
        private readonly List<int> order = new List<int>(16);
        private readonly List<int> targets = new List<int>(4);
        private int nextInOrder, handsDone;

        // The action in flight: only one unit acts at a time.
        private int actStart = -1, actor = -1;
        private TurnAct act;

        public TurnBattle(TurnSetup setup)
        {
            damage = new Prng(setup.Seed);
            skills = new Prng(setup.Seed ^ 0x85EBCA6Bu);
            hands = new List<TurnHandSpec>(setup.Hands);
            for (int i = 0; i < setup.Units.Count; i++)
            {
                var s = setup.Units[i];
                Units.Add(new TurnUnit
                {
                    Id = i, Side = s.Side, Row = s.Row, Col = s.Col, Hp = s.Hp, MaxHp = s.MaxHp, Atk = s.Atk,
                    CritPercent = s.CritPercent, Skill = s.Skill, SkillPercent = s.SkillPercent, SkillRate = s.SkillRate,
                });
            }
            actStart = 0;
        }

        public void Run()
        {
            while (!Over) Step();
        }

        /// <summary>Runs every tick before `tick` (or until the battle ends).</summary>
        public void RunTo(int tick)
        {
            while (!Over && Tick < tick) Step();
        }

        public void Step()
        {
            if (Over) return;
            if (actor < 0 && Tick == actStart) Begin();
            else if (actor >= 0)
            {
                var at = Tick - actStart;
                if (act == TurnAct.Attack && at == TurnTiming.AttackHitAt) Land(TurnEventKind.AttackHit);
                else if (act == TurnAct.Skill && at == TurnTiming.SkillHitAt) Land(TurnEventKind.SkillHit);
                if (at == TurnTiming.Length(act)) Finish();
            }
            Tick++;
        }

        /// <summary>Starts the next action at this tick, or ends the battle if there is none.</summary>
        private void Begin()
        {
            if (handsDone < hands.Count)
            {
                var h = hands[handsDone];
                act = TurnAct.Hand;
                actor = h.Leader;
                for (int i = 0; i < Units.Count; i++)
                    if (Units[i].Side == h.Side) Units[i].DamageBonus += h.DamagePercent;
                Emit(TurnEventKind.HandTrigger, actor, -1, h.DamagePercent, handsDone);
                handsDone++;
                return;
            }
            var next = NextActor();
            if (next < 0 || Tick + TurnTiming.AttackTicks > TurnTiming.MaxTicks)
            {
                End(TimeoutWinner());
                return;
            }
            actor = next;
            var u = Units[actor];
            // The skill roll comes first: the targets depend on it.
            if (u.Skill != TurnSkill.None && skills.Chance(u.SkillRate) && Tick + TurnTiming.SkillTicks <= TurnTiming.MaxTicks)
            {
                act = TurnAct.Skill;
                SkillTargets(u);
                Emit(TurnEventKind.SkillCast, actor, targets[0], targets.Count, 0);
                return;
            }
            act = TurnAct.Attack;
            targets.Clear();
            targets.Add(Nearest(u));
            Emit(TurnEventKind.AttackStart, actor, targets[0], 0, 0);
        }

        private void Finish()
        {
            actor = -1;
            var winner = WipeWinner();
            if (winner >= 0) End(winner);
            else actStart = Tick + TurnTiming.GapTicks;
        }

        private void End(int winner)
        {
            Winner = winner;
            Emit(TurnEventKind.BattleEnd, -1, -1, winner, 0);
        }

        /// <summary>The next living unit in the round's order; a new round starts when the order runs out.</summary>
        private int NextActor()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                while (nextInOrder < order.Count)
                {
                    var id = order[nextInOrder++];
                    if (Units[id].Alive) return id;
                }
                if (WipeWinner() >= 0) return -1;
                Round++;
                BuildOrder();
            }
            return -1;
        }

        /// <summary>02 §3: each side's living units by row, then column; the sides alternate, side 0 first.</summary>
        private void BuildOrder()
        {
            order.Clear();
            nextInOrder = 0;
            var a = BySeat(0);
            var b = BySeat(1);
            for (int i = 0; i < a.Count || i < b.Count; i++)
            {
                if (i < a.Count) order.Add(a[i]);
                if (i < b.Count) order.Add(b[i]);
            }
        }

        private List<int> BySeat(int side)
        {
            var list = new List<int>(8);
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Side != side || !u.Alive) continue;
                var at = list.Count;
                while (at > 0 && Seat(Units[list[at - 1]]) > Seat(u)) at--;
                list.Insert(at, i);
            }
            return list;
        }

        private static int Seat(TurnUnit u) => u.Row * 16 + u.Col;

        private int WipeWinner()
        {
            bool a = false, b = false;
            for (int i = 0; i < Units.Count; i++)
            {
                if (!Units[i].Alive) continue;
                if (Units[i].Side == 0) a = true; else b = true;
            }
            return a && b ? -1 : a ? 0 : b ? 1 : 2;
        }

        /// <summary>Remaining health as a share of the side's full health; equal shares are a draw.</summary>
        private int TimeoutWinner()
        {
            var w = WipeWinner();
            if (w >= 0) return w;
            long hp0 = 0, max0 = 0, hp1 = 0, max1 = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Side == 0) { hp0 += u.Hp; max0 += u.MaxHp; }
                else { hp1 += u.Hp; max1 += u.MaxHp; }
            }
            // hp0 / max0 against hp1 / max1, cross-multiplied.
            long l = hp0 * max1, r = hp1 * max0;
            return l == r ? 2 : l > r ? 0 : 1;
        }

        private void Emit(TurnEventKind kind, int source, int target, int amount, int index, bool crit = false) =>
            Events.Add(new TurnEvent
            {
                Tick = Tick, Kind = kind, Act = act, Actor = source, Target = target, Amount = amount, Index = index, Crit = crit,
            });
    }
}
