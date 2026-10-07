using System.Collections.Generic;
using Automatic.Battle.Math;

namespace Automatic.Battle.Sim
{
    /// <summary>
    /// Uniform grid of the living units, rebuilt every tick by a counting sort over the unit list,
    /// so the units in a cell are always in id order. Cells are one board cell (1.0) wide.
    /// It is a snapshot of the tick's start: a unit that moved to another cell earlier in the same
    /// tick is still found in its old cell. Deterministic, but not the same battle as a full scan.
    /// </summary>
    public sealed class SpatialGrid
    {
        public readonly int Cols, Rows;
        private readonly int[] start;
        private readonly int[] fill;
        private int[] items = new int[256];

        public SpatialGrid(int cols, int rows)
        {
            Cols = cols;
            Rows = rows;
            start = new int[cols * rows + 1];
            fill = new int[cols * rows];
        }

        public int CellX(FP x) => Clamp(FixedMath.Floor(x), Cols);
        public int CellY(FP y) => Clamp(FixedMath.Floor(y), Rows);

        public void Build(List<SimUnit> units)
        {
            System.Array.Clear(start, 0, start.Length);
            int n = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.Alive) continue;
                start[CellY(u.Y) * Cols + CellX(u.X) + 1]++;
                n++;
            }
            for (int c = 1; c < start.Length; c++) start[c] += start[c - 1];
            if (items.Length < n) items = new int[n * 2];
            System.Array.Copy(start, fill, fill.Length);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.Alive) continue;
                items[fill[CellY(u.Y) * Cols + CellX(u.X)]++] = u.Id;
            }
        }

        /// <summary>Appends the ids in the cells overlapping the square [x-r, x+r] x [y-r, y+r].</summary>
        public void Query(FP x, FP y, FP r, List<int> result)
        {
            int x0 = CellX(x - r), x1 = CellX(x + r), y0 = CellY(y - r), y1 = CellY(y + r);
            for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
                Cell(cx, cy, result);
        }

        /// <summary>Appends the ids of the cells at Chebyshev distance exactly `ring` from (cx, cy).</summary>
        public void Ring(int cx, int cy, int ring, List<int> result)
        {
            if (ring == 0)
            {
                Cell(cx, cy, result);
                return;
            }
            for (int x = cx - ring; x <= cx + ring; x++)
            {
                Cell(x, cy - ring, result);
                Cell(x, cy + ring, result);
            }
            for (int y = cy - ring + 1; y <= cy + ring - 1; y++)
            {
                Cell(cx - ring, y, result);
                Cell(cx + ring, y, result);
            }
        }

        private void Cell(int cx, int cy, List<int> result)
        {
            if (cx < 0 || cy < 0 || cx >= Cols || cy >= Rows) return;
            int c = cy * Cols + cx;
            for (int i = start[c]; i < start[c + 1]; i++) result.Add(items[i]);
        }

        private static int Clamp(int v, int count) => v < 0 ? 0 : v >= count ? count - 1 : v;
    }
}
