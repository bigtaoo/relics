using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>
    /// Readability numbers of a framing (design/08 §4, art/board/README.md): the on-screen size of
    /// the cells, pedestals (team colour; the piece is about as wide), health bars and the acting
    /// ring at four places, from the nearest (the player's front row) to the farthest (the
    /// opponent's bench). In pixels, and in millimetres on a phone (HudLayout.ShortMm).
    /// </summary>
    public static class Readability
    {
        /// <summary>The acting ring's radii, as the battle draws it (BattleShow.Ring.cs).</summary>
        private const float RingOuter = BattleShow.RingOuter, RingInner = BattleShow.RingInner;

        /// <summary>One "[Readability]" line for the camera at `at`; the camera is put back afterwards.</summary>
        public static string Report(string framing, Camera cam, Vector3 at, HudLayout layout, IReadOnlyList<Transform> allies, IReadOnlyList<Transform> enemies,
            IReadOnlyList<Vector3> enemyGround)
        {
            var keep = cam.transform.position;
            cam.transform.position = at;
            var col = layout.ColumnPx.width;
            string Size(float px) => layout.ShortMm > 0 ? $"{px / col * layout.ShortMm:F1}" : $"{px:F0}";
            float Px(Vector3 a, Vector3 b) => Vector2.Distance(cam.WorldToScreenPoint(a), cam.WorldToScreenPoint(b));

            string Place(string name, Transform unit, Vector3 ground, float pitch)
            {
                ground.y = 0;
                var pedestal = unit.Find("Pedestal").localScale.x;
                var bar = unit.Find("BarBack");
                var barAt = ground + Vector3.up * bar.localPosition.y;
                var right = bar.localRotation * Vector3.right * bar.localScale.x / 2;
                var up = bar.localRotation * Vector3.up * bar.localScale.y / 2;
                var near = ground + Vector3.back * RingOuter;
                return $"{name} pitch {Size(Px(ground, ground + Vector3.right * pitch))}, pedestal {Size(Px(ground, ground + Vector3.right * pedestal))}, " +
                       $"bar {Size(Px(barAt - right, barAt + right))}x{Size(Px(barAt - up, barAt + up))}, " +
                       $"ring {Size(Px(ground - Vector3.right * RingOuter, ground + Vector3.right * RingOuter))} " +
                       $"band {Size(Px(ground + Vector3.right * RingInner, ground + Vector3.right * RingOuter))}/{Size(Px(near, near + Vector3.forward * (RingOuter - RingInner)))}";
            }

            // Nearest to farthest: the player's front row and bench, the opponent's back row and bench.
            const float benchZ = 3.5f;
            Transform Centre(IEnumerable<Transform> us) => us.OrderBy(u => Mathf.Abs(u.position.x)).First();
            var allyFront = Centre(allies.Where(u => u.position.z > allies.Where(a => a.position.z > -benchZ).Max(a => a.position.z) - 0.01f));
            var allyBench = Centre(allies.Where(u => u.position.z < -benchZ));
            var cells = Enumerable.Range(0, enemies.Count).Where(i => enemyGround[i].z < benchZ).ToList();
            var back = cells.Max(i => enemyGround[i].z);
            var enemyBack = cells.Where(i => enemyGround[i].z > back - 0.01f).OrderBy(i => Mathf.Abs(enemyGround[i].x)).First();
            var enemyBench = Enumerable.Range(0, enemies.Count).Where(i => enemyGround[i].z >= benchZ).OrderBy(i => Mathf.Abs(enemyGround[i].x)).First();
            var line = $"[Readability] {framing} {Screen.width}x{Screen.height} ({(layout.ShortMm > 0 ? "mm" : "px")}): " + string.Join(" | ",
                Place("ally front", allyFront, allyFront.position, 1),
                Place("ally bench", allyBench, allyBench.position, 0.72f),
                Place("enemy back", enemies[enemyBack], enemyGround[enemyBack], 1),
                Place("enemy bench", enemies[enemyBench], enemyGround[enemyBench], 0.72f));
            cam.transform.position = keep;
            return line;
        }
    }
}
