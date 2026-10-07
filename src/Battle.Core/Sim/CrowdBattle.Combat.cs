#nullable enable
using Automatic.Battle.Math;

namespace Automatic.Battle.Sim
{
    /// <summary>Attacks, damage, skills and buffs of the stress prototype.</summary>
    public sealed partial class CrowdBattle
    {
        private const int ManaPerAttack = 10, ManaPerHit = 5;
        private const int BurnTicks = 90, BurnEvery = 15, StunTicks = 45, AttackUpTicks = 150;

        private static readonly FP Hundred = FP.FromInt(100);
        private static readonly FP CritMultiplier = FP.FromPercent(150);
        private static readonly FP AttackUpPerCast = FP.FromPercent(10);
        private readonly SimUnit?[] lowest = new SimUnit?[3];

        private void Attack(SimUnit u, SimUnit t)
        {
            u.AttackReadyAt = Tick + u.AttackInterval;
            // Drawn on every attack, crit or not, so the stream never shifts (design/01).
            var crit = combat.Chance(u.CritPercent);
            var damage = u.Atk * AttackMultiplier(u);
            if (crit) damage = damage * CritMultiplier;
            Emit(SimEventKind.Attack, u.Id, t.Id, damage);
            Damage(u, t, damage);
            if (u.ManaMax == 0) return;
            u.Mana += ManaPerAttack;
            if (u.Mana < u.ManaMax) return;
            u.Mana = 0;
            Cast(u, t);
        }

        /// <summary>Same-kind bonuses are summed before use, so their order does not matter (design/03 §5).</summary>
        private static FP AttackMultiplier(SimUnit u)
        {
            var m = FP.One;
            for (int i = 0; i < u.BuffCount; i++)
                if (u.Buffs[i].Kind == BuffKind.AttackUp) m += u.Buffs[i].Amount;
            return m;
        }

        private void Damage(SimUnit source, SimUnit target, FP raw)
        {
            if (!target.Alive) return;
            var amount = raw * Hundred / (Hundred + target.Def);
            var absorbed = FP.Min(target.Shield, amount);
            target.Shield -= absorbed;
            target.Hp -= amount - absorbed;
            Emit(SimEventKind.Damage, source.Id, target.Id, amount);
            if (target.ManaMax > 0 && target.Mana < target.ManaMax) target.Mana += ManaPerHit;
            if (target.Hp > FP.Zero) return;
            target.Hp = FP.Zero;
            target.Alive = false;
            alive[target.Side]--;
            if (target.IsSummon) summons[target.Side]--;
            Emit(SimEventKind.Death, source.Id, target.Id, FP.Zero);
        }

        private void Cast(SimUnit u, SimUnit t)
        {
            Emit(SimEventKind.Cast, u.Id, t.Id, FP.Zero);
            AddBuff(u, BuffKind.AttackUp, AttackUpPerCast, AttackUpTicks, u.Id);
            switch (u.Skill)
            {
                case SkillKind.Blast:
                    EnemiesAround(u, t);
                    for (int i = 0; i < near.Count; i++) Damage(u, Units[near[i]], u.Atk * u.SkillPower);
                    break;
                case SkillKind.Burn:
                    EnemiesAround(u, t);
                    var perTick = u.Atk * u.SkillPower / (BurnTicks / BurnEvery);
                    for (int i = 0; i < near.Count; i++) AddBuff(Units[near[i]], BuffKind.Burn, perTick, BurnTicks, u.Id);
                    break;
                case SkillKind.Stun:
                    EnemiesAround(u, t);
                    for (int i = 0; i < near.Count; i++) Units[near[i]].StunnedUntil = Tick + StunTicks;
                    break;
                case SkillKind.Shield:
                    LowestAllies(u, 3);
                    for (int i = 0; i < lowest.Length; i++)
                    {
                        var a = lowest[i];
                        if (a == null) continue;
                        a.Shield += u.Atk * u.SkillPower;
                        Emit(SimEventKind.Shield, u.Id, a.Id, u.Atk * u.SkillPower);
                    }
                    break;
                case SkillKind.Heal:
                    LowestAllies(u, 1);
                    var h = lowest[0];
                    if (h == null) break;
                    h.Hp = FP.Min(h.MaxHp, h.Hp + u.Atk * u.SkillPower);
                    Emit(SimEventKind.Heal, u.Id, h.Id, u.Atk * u.SkillPower);
                    break;
            }
        }

        /// <summary>Living enemies of u within u.SkillRadius of t (t included) into `near`, in a fixed order.</summary>
        private void EnemiesAround(SimUnit u, SimUnit t)
        {
            near.Clear();
            Nearby(t.X, t.Y, u.SkillRadius);
            int kept = 0;
            var r2 = u.SkillRadius * u.SkillRadius;
            for (int i = 0; i < near.Count; i++)
            {
                var o = Units[near[i]];
                if (!o.Alive || o.Side == u.Side) continue;
                var dx = o.X - t.X;
                var dy = o.Y - t.Y;
                if (o != t && dx * dx + dy * dy > r2) continue;
                near[kept++] = o.Id;
            }
            near.RemoveRange(kept, near.Count - kept);
        }

        /// <summary>The n living allies with the lowest health ratio (ties: lower id) into `lowest`.</summary>
        private void LowestAllies(SimUnit u, int n)
        {
            for (int i = 0; i < lowest.Length; i++) lowest[i] = null;
            for (int i = 0; i < Units.Count; i++)
            {
                var a = Units[i];
                if (!a.Alive || a.Side != u.Side) continue;
                var c = a;
                for (int k = 0; k < n && c != null; k++)
                {
                    var l = lowest[k];
                    if (l == null || Lower(c, l))
                    {
                        lowest[k] = c;
                        c = l;
                    }
                }
            }
        }

        // a.Hp / a.MaxHp < b.Hp / b.MaxHp without dividing.
        private static bool Lower(SimUnit a, SimUnit b)
        {
            var l = a.Hp * b.MaxHp;
            var r = b.Hp * a.MaxHp;
            return l < r || (l == r && a.Id < b.Id);
        }

        private void AddBuff(SimUnit u, BuffKind kind, FP amount, int ticks, int source)
        {
            // Same kind from the same source refreshes; otherwise a new entry, or the oldest is replaced.
            int slot = -1;
            for (int i = 0; i < u.BuffCount; i++)
                if (u.Buffs[i].Kind == kind && u.Buffs[i].Source == source) slot = i;
            if (slot < 0 && u.BuffCount < SimUnit.MaxBuffs) slot = u.BuffCount++;
            if (slot < 0) slot = 0;
            u.Buffs[slot] = new Buff { Kind = kind, Amount = amount, Until = Tick + ticks, NextTick = Tick + BurnEvery, Source = source };
        }

        private void UpdateBuffs(SimUnit u)
        {
            int kept = 0;
            for (int i = 0; i < u.BuffCount; i++)
            {
                var b = u.Buffs[i];
                if (b.Kind == BuffKind.Burn && Tick >= b.NextTick)
                {
                    Damage(Units[b.Source], u, b.Amount);
                    b.NextTick += BurnEvery;
                }
                if (Tick < b.Until) u.Buffs[kept++] = b;
            }
            u.BuffCount = kept;
        }

        private void Emit(SimEventKind kind, int source, int target, FP amount)
        {
            if (Config.RecordEvents)
                Events.Add(new SimEvent { Tick = Tick, Kind = kind, Source = source, Target = target, Amount = amount });
        }

        /// <summary>Time is up: the side with more health left (as a share of its total) wins.</summary>
        private int TimeoutWinner()
        {
            FP a = FP.Zero, b = FP.Zero;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!u.Alive || u.IsSummon) continue;
                if (u.Side == 0) a += u.Hp; else b += u.Hp;
            }
            return a == b ? 2 : a > b ? 0 : 1;
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
                Mix(Tick);
                Mix(Winner);
                Mix(combat.Peek());
                Mix(placement.Peek());
                for (int i = 0; i < Units.Count; i++)
                {
                    var u = Units[i];
                    Mix(u.Alive ? 1 : 0);
                    Mix(u.X.Raw);
                    Mix(u.Y.Raw);
                    Mix(u.Hp.Raw);
                    Mix(u.Shield.Raw);
                    Mix(u.Mana);
                    Mix(u.Target);
                    Mix(u.BuffCount);
                }
                return h;
            }
        }
    }
}
