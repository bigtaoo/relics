using Automatic.Battle.Math;

namespace Automatic.Battle.Sim
{
    public enum SkillKind
    {
        None,
        /// <summary>Damage every enemy within SkillRadius of the target.</summary>
        Blast,
        /// <summary>Burn (damage over time) on every enemy within SkillRadius of the target.</summary>
        Burn,
        /// <summary>Stun the target and the enemies next to it.</summary>
        Stun,
        /// <summary>Shield the three allies with the least health.</summary>
        Shield,
        /// <summary>Heal the ally with the least health.</summary>
        Heal,
    }

    public enum BuffKind
    {
        Burn,
        AttackUp,
    }

    public struct Buff
    {
        public BuffKind Kind;
        public FP Amount;
        public int Until;
        public int NextTick;
        public int Source;
    }

    /// <summary>One unit of the stress prototype. Plain fields, no behaviour: CrowdBattle runs them.</summary>
    public sealed class SimUnit
    {
        public const int MaxBuffs = 6;

        public int Id;
        public int Side;
        public bool Alive = true;
        public bool IsSummon;
        public int Summoner = -1;

        public FP X, Y, Radius, Speed;
        public FP Hp, MaxHp, Shield;
        public FP Atk, Def, Range;
        public int CritPercent;

        public int AttackInterval;
        public int AttackReadyAt;
        public int Mana, ManaMax;
        public SkillKind Skill;
        public FP SkillPower, SkillRadius;

        public int Target = -1;
        public int RetargetAt;
        public int StunnedUntil;

        /// <summary>Summoners: spawn SummonBatch units every SummonEvery ticks while the side is under its cap.</summary>
        public int SummonEvery, SummonBatch, NextSummonAt;

        public readonly Buff[] Buffs = new Buff[MaxBuffs];
        public int BuffCount;
    }
}
