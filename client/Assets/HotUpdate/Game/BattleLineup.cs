using System.Collections.Generic;
using System.Linq;
using Automatic.Battle.Turn;

namespace Automatic.Game
{
    /// <summary>
    /// The battle the shop demo plays (design/08 §1), run by Battle.Core's TurnBattle: three of the
    /// player's front row and the zheng with the five-tail flames against the opponent's front row,
    /// the player's formed hand going off first. The lineup is content data (design/03 §7), so it
    /// lives here until the data tables exist; tests/Battle.Core.Tests/TurnBattleTests.cs keeps a copy.
    /// The other pieces on the board watch.
    /// </summary>
    public static class BattleLineup
    {
        public const string HandName = "西山五兽", SkillName = "五尾焰";

        public static string HandEffect(int percent) => $"全体伤害 +{percent}%";

        public static TurnSetup Setup(uint seed)
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

        /// <summary>The board cell of a unit: core side 0 (the player) is the client's side -1.</summary>
        public static (int, int, int) CellOf(TurnUnit u) => (u.Side == 0 ? -1 : 1, u.Row, u.Col);

        /// <summary>
        /// The first seed whose battle shows everything the demo is for: the skill, two crits on
        /// attacks (a crit among the skill's hits is drowned by the others), and the player winning.
        /// The search runs the core a few times (a few ms); the seed is logged.
        /// </summary>
        public static uint DemoSeed()
        {
            for (uint seed = 1; seed < 1000; seed++)
            {
                var b = new TurnBattle(Setup(seed));
                b.Run();
                if (b.Winner == 0 && b.Events.Any(e => e.Kind == TurnEventKind.SkillCast) && AttackCrits(b.Events) >= 2)
                    return seed;
            }
            return 1;
        }

        private static int AttackCrits(IReadOnlyList<TurnEvent> events)
        {
            var attack = false;
            var crits = 0;
            foreach (var e in events)
            {
                if (e.Kind is TurnEventKind.AttackStart or TurnEventKind.SkillCast or TurnEventKind.HandTrigger)
                    attack = e.Kind == TurnEventKind.AttackStart;
                else if (e.Kind == TurnEventKind.Damage && e.Crit && attack)
                    crits++;
            }
            return crits;
        }

        private static TurnUnitSpec Unit(int side, int row, int col, int hp, int atk, int crit) =>
            new() { Side = side, Row = row, Col = col, Hp = hp, MaxHp = hp, Atk = atk, CritPercent = crit };
    }
}
