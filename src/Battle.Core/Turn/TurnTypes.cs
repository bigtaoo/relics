using System.Collections.Generic;

namespace Automatic.Battle.Turn
{
    /// <summary>
    /// Action lengths in logic ticks (design/01 §3.1, ADR-014). The presentation fits every action into
    /// these: it may stretch, squash or hold inside an action but never make one longer. Changing a value
    /// changes battle results: bump EngineVersion.
    /// </summary>
    public static class TurnTiming
    {
        public const int TicksPerSecond = 10;
        /// <summary>Normal attack: leap in, strike, leap back. Damage lands at AttackHitAt.</summary>
        public const int AttackTicks = 16, AttackHitAt = 7;
        /// <summary>Skill: callout and camera push, release, bolts land at SkillHitAt.</summary>
        public const int SkillTicks = 27, SkillHitAt = 9;
        /// <summary>A hand going off: banner, pillars, links, the buff spreading over its side.</summary>
        public const int HandTicks = 25;
        /// <summary>Pause between two actions.</summary>
        public const int GapTicks = 2;
        /// <summary>30 s battle phase (design/00). No action starts that would end after it.</summary>
        public const int MaxTicks = 300;

        public static int Length(TurnAct act) =>
            act == TurnAct.Attack ? AttackTicks : act == TurnAct.Skill ? SkillTicks : HandTicks;
    }

    public enum TurnAct { Attack, Skill, Hand }

    public enum TurnSkill
    {
        None,
        /// <summary>Zheng's five-tail flames: five bolts at up to three enemies, front row first.</summary>
        FiveTailFlame,
    }

    /// <summary>
    /// One unit in the setup. Side 0 is the player and acts first; row 0 faces the river, columns 0..4.
    /// Hp is the current value (a unit may start hurt), MaxHp the full bar.
    /// </summary>
    public struct TurnUnitSpec
    {
        public int Side, Row, Col;
        public int Hp, MaxHp, Atk, CritPercent;
        public TurnSkill Skill;
        /// <summary>Skill damage in percent of Atk, and the chance in percent that the skill replaces the attack.</summary>
        public int SkillPercent, SkillRate;
    }

    /// <summary>A formed hand (design/06): goes off before the first round and raises its side's damage.</summary>
    public struct TurnHandSpec
    {
        public int Side;
        public int DamagePercent;
        /// <summary>The unit (index into TurnSetup.Units) that the presentation centres the hand on.</summary>
        public int Leader;
    }

    public sealed class TurnSetup
    {
        public uint Seed;
        public readonly List<TurnUnitSpec> Units = new List<TurnUnitSpec>();
        public readonly List<TurnHandSpec> Hands = new List<TurnHandSpec>();
    }

    public enum TurnEventKind
    {
        /// <summary>Actor = the hand's leader, Amount = damage bonus percent, Index = hand index.</summary>
        HandTrigger,
        /// <summary>Actor starts a normal attack on Target.</summary>
        AttackStart,
        /// <summary>The attack connects (followed by Damage at the same tick).</summary>
        AttackHit,
        /// <summary>Actor starts its skill; Amount = the number of targets.</summary>
        SkillCast,
        /// <summary>The skill reaches Target; Index = the target's place in the skill's target list.</summary>
        SkillHit,
        /// <summary>Actor deals Amount to Target; Crit; Act says whether it was an attack or the skill.</summary>
        Damage,
        Death,
        /// <summary>Amount = winner: side 0, side 1, or 2 for a draw.</summary>
        BattleEnd,
    }

    /// <summary>One entry of the event stream the presentation consumes (design/03 §8), in emission order.</summary>
    public struct TurnEvent
    {
        public int Tick;
        public TurnEventKind Kind;
        public TurnAct Act;
        public int Actor, Target, Amount, Index;
        public bool Crit;

        public override string ToString() =>
            $"{Tick,4} {Kind,-11} {Act,-6} actor {Actor} target {Target} amount {Amount}{(Crit ? " crit" : "")} #{Index}";
    }

    /// <summary>Live state of one unit. Hp stays a whole number (damage is truncated before it is dealt).</summary>
    public sealed class TurnUnit
    {
        public int Id, Side, Row, Col;
        public int Hp, MaxHp, Atk, CritPercent;
        public TurnSkill Skill;
        public int SkillPercent, SkillRate;
        /// <summary>Damage bonus from hands that went off, in percent.</summary>
        public int DamageBonus;
        public bool Alive => Hp > 0;
    }
}
