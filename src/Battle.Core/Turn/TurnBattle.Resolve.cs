using Automatic.Battle.Math;

namespace Automatic.Battle.Turn
{
    public sealed partial class TurnBattle
    {
        /// <summary>Most targets of FiveTailFlame.</summary>
        public const int FlameTargets = 3;
        public const int CritPercentDamage = 200;
        /// <summary>Damage spreads uniformly over 90..110 % of the formula.</summary>
        public const int SpreadMin = 90, SpreadRange = 21;

        /// <summary>Nearest living enemy: front row first, then the closest column, then the lower column.</summary>
        private int Nearest(TurnUnit u)
        {
            int best = -1;
            for (int i = 0; i < Units.Count; i++)
            {
                var e = Units[i];
                if (e.Side == u.Side || !e.Alive) continue;
                if (best < 0 || Distance(u, e) < Distance(u, Units[best])) best = i;
            }
            return best;
        }

        private static int Distance(TurnUnit from, TurnUnit to)
        {
            var dc = to.Col - from.Col;
            if (dc < 0) dc = -dc;
            return to.Row * 256 + dc * 16 + to.Col;
        }

        /// <summary>Up to FlameTargets living enemies, front row first, left to right.</summary>
        private void SkillTargets(TurnUnit u)
        {
            targets.Clear();
            for (int pick = 0; pick < FlameTargets; pick++)
            {
                int best = -1;
                for (int i = 0; i < Units.Count; i++)
                {
                    var e = Units[i];
                    if (e.Side == u.Side || !e.Alive || targets.Contains(i)) continue;
                    if (best < 0 || Seat(e) < Seat(Units[best])) best = i;
                }
                if (best < 0) break;
                targets.Add(best);
            }
        }

        /// <summary>The action connects: every target takes its blow, in target order, deaths right after.</summary>
        private void Land(TurnEventKind hit)
        {
            var u = Units[actor];
            for (int k = 0; k < targets.Count; k++)
            {
                var t = Units[targets[k]];
                Emit(hit, actor, t.Id, 0, k);
                // Both draws for every blow, also on a target that died earlier in this action: the
                // number of draws must not depend on the outcome.
                var crit = damage.Chance(u.CritPercent);
                var spread = SpreadMin + damage.NextInt(SpreadRange);
                if (!t.Alive) continue;
                var amount = Damage(u, crit, spread);
                t.Hp = amount >= t.Hp ? 0 : t.Hp - amount;
                Emit(TurnEventKind.Damage, actor, t.Id, amount, k, crit);
                if (!t.Alive) Emit(TurnEventKind.Death, actor, t.Id, 0, k);
            }
        }

        /// <summary>Atk x (skill or 100 %) x (100 % + hand bonus) x spread, x2 on a crit; at least 1.</summary>
        private int Damage(TurnUnit u, bool crit, int spread)
        {
            var percent = act == TurnAct.Skill ? u.SkillPercent : 100;
            var v = FP.FromInt(u.Atk) * FP.FromPercent(percent) * FP.FromPercent(100 + u.DamageBonus) * FP.FromPercent(spread);
            if (crit) v = v * FP.FromPercent(CritPercentDamage);
            var amount = (int)v.TruncateToLong();
            return amount < 1 ? 1 : amount;
        }

        /// <summary>FNV-1a over everything that decides the battle: what clients report at the end (design/01 §6).</summary>
        public ulong Hash()
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                void Mix(long v)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        h ^= (byte)(v >> (i * 8));
                        h *= 1099511628211UL;
                    }
                }
                Mix(EngineVersion.Current);
                Mix(Tick);
                Mix(Winner);
                Mix(Round);
                Mix(damage.Peek());
                Mix(skills.Peek());
                Mix(Events.Count);
                for (int i = 0; i < Units.Count; i++)
                {
                    Mix(Units[i].Hp);
                    Mix(Units[i].DamageBonus);
                }
                return h;
            }
        }
    }
}
