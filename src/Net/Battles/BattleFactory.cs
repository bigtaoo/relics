#nullable enable
using Automatic.Battle.Math;
using Automatic.Battle.Sim;
using Automatic.Net.Protocol;

namespace Automatic.Net.Battles
{
    /// <summary>
    /// Turns a BattleSpec (two frozen boards + seed, design/01 §3.2) into a battle of the core. The client,
    /// the bots and the server all build battles here, so this is part of the deterministic input: change
    /// it like core code (every side's result changes with it).
    ///
    /// The core's stress prototype has no rosters yet, so a board only tunes the eight heroes it already
    /// fields: star level scales health and attack, the cell sets the start position. Replace this with the
    /// real roster once the core takes one.
    /// </summary>
    public static class BattleFactory
    {
        public const int Slots = 8;
        public const int Cols = 7;
        /// <summary>Rows of one half; row Rows - 1 is the front line.</summary>
        public const int Rows = 4;
        public const int MaxStar = 3;

        /// <summary>Health and attack per star level, percent. Star 0 = not bought yet.</summary>
        private static readonly int[] StarPercent = { 70, 100, 180, 320 };
        private static readonly int[] DefaultCols = { 0, 2, 4, 6 };

        public static CrowdBattle Create(BattleSpec spec, bool recordEvents)
        {
            var config = SimConfig.Duel16(spec.Seed);
            config.RecordEvents = recordEvents;
            var battle = new CrowdBattle(config);
            var boards = new[] { spec.A, spec.B };
            var heroIndex = new int[2];
            for (int i = 0; i < battle.Units.Count; i++)
            {
                var u = battle.Units[i];
                if (u.IsSummon) continue;
                int slot = heroIndex[u.Side]++;
                var piece = Find(boards[u.Side], slot);
                if (piece == null) continue;
                var scale = FP.FromPercent(StarPercent[Clamp(piece.Star, 0, MaxStar)]);
                u.MaxHp = u.MaxHp * scale;
                u.Hp = u.MaxHp;
                u.Atk = u.Atk * scale;
                // Own half is y in [0, Rows) for side 0; side 1 is mirrored, front row facing the middle.
                var x = FP.FromRatio(Clamp(piece.Col, 0, Cols - 1) * 2 + 1, 2);
                var y = FP.FromRatio(Clamp(piece.Row, 0, Rows - 1) * 2 + 1, 2);
                u.X = x;
                u.Y = u.Side == 0 ? y : FP.FromInt(config.Height) - y;
            }
            return battle;
        }

        /// <summary>A fresh board: every slot unbought, front row slots 0-3, back row 4-7.</summary>
        public static Board DefaultBoard()
        {
            var board = new Board();
            for (int slot = 0; slot < Slots; slot++)
            {
                board.Pieces.Add(new Piece
                {
                    Slot = slot,
                    Star = 0,
                    Col = DefaultCols[slot % 4],
                    Row = slot < 4 ? Rows - 1 : 1,
                });
            }
            return board;
        }

        public static bool InBounds(int col, int row) => col >= 0 && col < Cols && row >= 0 && row < Rows;

        private static Piece? Find(Board? board, int slot)
        {
            if (board == null) return null;
            for (int i = 0; i < board.Pieces.Count; i++)
                if (board.Pieces[i].Slot == slot) return board.Pieces[i];
            return null;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
