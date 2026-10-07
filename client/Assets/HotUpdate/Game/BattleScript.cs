using System.Collections.Generic;

namespace Automatic.Game
{
    /// <summary>A board cell: side -1 is the player's half, +1 the opponent's; row 0 faces the river.</summary>
    public readonly struct Cell
    {
        public readonly int Side, Row, Col;
        public Cell(int side, int row, int col) => (Side, Row, Col) = (side, row, col);
    }

    public enum ActKind { Attack, Skill, Hand }

    /// <summary>One blow landing on one piece; Kill when it takes the piece's last health.</summary>
    public readonly struct Blow
    {
        public readonly Cell Target;
        public readonly int Damage;
        public readonly bool Crit, Kill;
        public Blow(Cell target, int damage, bool crit = false, bool kill = false) => (Target, Damage, Crit, Kill) = (target, damage, crit, kill);
    }

    public sealed class Act
    {
        public ActKind Kind;
        public Cell Actor;
        public Blow[] Blows = System.Array.Empty<Blow>();
        public string Name;
    }

    /// <summary>
    /// The battle the demo plays after the battle start (design/08 §1): a hand-written stand-in for
    /// the event stream Battle.Core will produce (03 §8). Sides take turns, front row first; every
    /// act has a fixed length, as the core's tick table will give it (01 §3), and the presentation
    /// has to fit inside it.
    /// </summary>
    public static class BattleScript
    {
        /// <summary>Seconds per act, standing in for the core's tick table.</summary>
        public static float Length(ActKind kind) => kind switch
        {
            ActKind.Attack => 1.6f,
            ActKind.Skill => 2.7f,
            _ => 2.5f,
        };

        public const float Gap = 0.15f;

        /// <summary>Health at the start of the battle (of 1000) of the pieces the script hits.</summary>
        public static readonly Dictionary<(int, int, int), int> Health = new()
        {
            [(1, 0, 1)] = 560,
            [(1, 0, 2)] = 520,
            [(1, 0, 3)] = 900,
            [(-1, 0, 1)] = 820,
            [(-1, 0, 2)] = 700,
            [(-1, 0, 3)] = 760,
        };

        public const string HandName = "西山五兽", HandEffect = "全体伤害 +30%", SkillName = "五尾焰";

        public static readonly Act[] Acts =
        {
            new() { Kind = ActKind.Attack, Actor = Me(0, 1), Blows = new[] { new Blow(Them(0, 1), 132) } },
            new() { Kind = ActKind.Attack, Actor = Them(0, 2), Blows = new[] { new Blow(Me(0, 2), 98) } },
            new() { Kind = ActKind.Attack, Actor = Me(0, 3), Blows = new[] { new Blow(Them(0, 3), 264, crit: true) } },
            new() { Kind = ActKind.Attack, Actor = Them(0, 1), Blows = new[] { new Blow(Me(0, 1), 104) } },
            // The formed hand goes off (06): its pieces light up again, every piece on the board gains.
            new() { Kind = ActKind.Hand, Actor = Me(0, 1), Name = HandName },
            new()
            {
                Kind = ActKind.Skill, Actor = Me(1, 4), Name = SkillName,
                Blows = new[] { new Blow(Them(0, 1), 286), new Blow(Them(0, 2), 540, crit: true, kill: true), new Blow(Them(0, 3), 302) },
            },
            new() { Kind = ActKind.Attack, Actor = Them(0, 3), Blows = new[] { new Blow(Me(0, 3), 91) } },
            new() { Kind = ActKind.Attack, Actor = Me(0, 2), Blows = new[] { new Blow(Them(0, 1), 158, kill: true) } },
        };

        /// <summary>Start of each act, seconds from the start of the battle; the last entry is the end.</summary>
        public static float[] Starts()
        {
            var starts = new float[Acts.Length + 1];
            for (var i = 0; i < Acts.Length; i++) starts[i + 1] = starts[i] + Length(Acts[i].Kind) + Gap;
            return starts;
        }

        private static Cell Me(int row, int col) => new(-1, row, col);
        private static Cell Them(int row, int col) => new(1, row, col);
    }
}
