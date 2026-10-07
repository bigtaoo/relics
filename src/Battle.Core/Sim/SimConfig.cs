using Automatic.Battle.Math;

namespace Automatic.Battle.Sim
{
    /// <summary>Stats of one unit kind, authored in readable units (percent, tenths, ticks).</summary>
    public struct UnitTemplate
    {
        public int Hp, Atk, Def;
        /// <summary>Attack range in tenths of a board cell; size and speed (per tick) in hundredths.</summary>
        public int RangeTenths, RadiusHundredths, SpeedHundredths;
        public int AttackInterval, CritPercent, ManaMax;
        public SkillKind Skill;
        public int SkillPercent, SkillRadiusTenths;

        public UnitTemplate(int hp, int atk, int def, int range, int radius, int speed, int interval, int crit,
            int mana = 0, SkillKind skill = SkillKind.None, int skillPercent = 0, int skillRadius = 0)
        {
            Hp = hp; Atk = atk; Def = def;
            RangeTenths = range; RadiusHundredths = radius; SpeedHundredths = speed;
            AttackInterval = interval; CritPercent = crit; ManaMax = mana;
            Skill = skill; SkillPercent = skillPercent; SkillRadiusTenths = skillRadius;
        }
    }

    /// <summary>
    /// One stress scenario. The rules are a stand-in (design/02 still waits for captures): a real-time
    /// auto battle with targeting, movement with separation, attacks, crits, mana, area skills, burns,
    /// stuns, shields, heals and summoners, sized so the logic costs at least what the real rules will.
    /// </summary>
    public sealed class SimConfig
    {
        public string Name = "";
        public uint Seed;
        /// <summary>30 Hz, 30 s battle phase (design/00, 01 §3).</summary>
        public int MaxTicks = 900;
        public int Width = 7, Height = 8;
        public int SummonersPerSide;
        public int SummonEvery = 45, SummonBatch = 6;
        /// <summary>Living summons per side; summoners refill the side up to this.</summary>
        public int SummonCap;
        public int InitialSummons;
        /// <summary>false: every search scans the whole unit list (the naive cost, for comparison).</summary>
        public bool UseGrid = true;
        public bool RecordEvents = true;

        /// <summary>Eight heroes per side: two tanks, two fighters, two archers, two supports.</summary>
        public static readonly UnitTemplate[] Heroes =
        {
            new UnitTemplate(1800, 60, 60, 6, 30, 5, 30, 10, 60, SkillKind.Shield, 150),
            new UnitTemplate(1300, 90, 35, 6, 30, 5, 27, 20, 50, SkillKind.Blast, 180, 15),
            new UnitTemplate(1300, 90, 35, 6, 30, 5, 27, 20, 50, SkillKind.Blast, 180, 15),
            new UnitTemplate(1800, 60, 60, 6, 30, 5, 30, 10, 60, SkillKind.Stun, 0, 10),
            new UnitTemplate(900, 80, 20, 30, 30, 5, 24, 25, 40, SkillKind.Burn, 200, 15),
            new UnitTemplate(1000, 50, 25, 25, 30, 5, 30, 10, 50, SkillKind.Heal, 300),
            new UnitTemplate(1000, 50, 25, 25, 30, 5, 30, 10, 50, SkillKind.Shield, 150),
            new UnitTemplate(900, 80, 20, 30, 30, 5, 24, 25, 40, SkillKind.Burn, 200, 15),
        };

        /// <summary>Low-poly summon (the crowd test's 400-triangle zheng).</summary>
        public static readonly UnitTemplate Summon = new UnitTemplate(220, 22, 10, 3, 15, 6, 30, 10);

        public static FP Tenths(int v) => FP.FromRatio(v, 10);

        /// <summary>A normal battle: eight heroes a side, no summons.</summary>
        public static SimConfig Duel16(uint seed) => new SimConfig { Name = "duel16", Seed = seed };

        /// <summary>Summon build on both sides: four summoners a side keep up to 142 summons each (300 units).</summary>
        public static SimConfig Summon300(uint seed) =>
            new SimConfig { Name = "summon300", Seed = seed, SummonersPerSide = 4, SummonBatch = 12, SummonCap = 142 };

        /// <summary>Worst case: 300 units from the first tick, refilled by the summoners.</summary>
        public static SimConfig Crowd300(uint seed) =>
            new SimConfig { Name = "crowd300", Seed = seed, SummonersPerSide = 4, SummonCap = 142, InitialSummons = 142 };

        /// <summary>Headroom check: 500 units.</summary>
        public static SimConfig Crowd500(uint seed) =>
            new SimConfig { Name = "crowd500", Seed = seed, SummonersPerSide = 4, SummonCap = 242, InitialSummons = 242 };

        public static SimConfig[] All(uint seed) => new[] { Duel16(seed), Summon300(seed), Crowd300(seed), Crowd500(seed) };
    }
}
