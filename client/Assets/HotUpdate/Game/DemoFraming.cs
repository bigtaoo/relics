using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>
    /// The two camera framings of design/04 §2 for the current screen: the battle framing of the
    /// whole table and the preparation framing of the player's half in the screen area the HUD
    /// leaves free. Same angle and FOV; only the position differs. Runtime port of
    /// Editor/BoardSlice.cs Fit and Editor/UiSlice.cs Frame.
    /// </summary>
    public static class DemoFraming
    {
        public static readonly Vector2 Reference = new(1920, 1080);

        /// <summary>Canvas size in reference units, as a CanvasScaler in Expand mode makes it.</summary>
        public static Vector2 CanvasSize(int w, int h)
        {
            var scale = Mathf.Min(w / Reference.x, h / Reference.y);
            return new Vector2(w / scale, h / scale);
        }

        /// <summary>Whole table: the camera backs off from the table centre until every corner fits.</summary>
        public static Vector3 Battle(Camera cam, float halfWidth, float halfDepth)
        {
            var points = new List<Vector3>();
            foreach (var x in new[] { -halfWidth, halfWidth })
                foreach (var z in new[] { -halfDepth, halfDepth })
                    foreach (var y in new[] { 0f, 1f })
                        points.Add(new Vector3(x, y, z));
            const float margin = 0.02f;
            var t = cam.transform;
            for (var dist = 5f; dist < 60; dist += 0.05f)
            {
                t.position = new Vector3(0, 0.3f, 0) - t.forward * dist;
                if (points.Select(cam.WorldToViewportPoint).All(p => p.x > margin && p.x < 1 - margin && p.y > margin && p.y < 1 - margin))
                    break;
            }
            return t.position;
        }

        /// <summary>
        /// The player's half and bench (z from -halfDepth to `top`) in the free area: right of the
        /// hand panel, above the shop tray, below the round bar. Returns the camera position and the
        /// centre of the free area in canvas coordinates (where the hu seal lands).
        /// </summary>
        public static (Vector3 Position, Vector3 HuCentre) Prep(Camera cam, float halfWidth, float halfDepth, float top, Vector2 size)
        {
            var free = Rect.MinMaxRect((24 + 430 + 16) / size.x, (12 + 290 + 8) / size.y, 1 - 16 / size.x, 1 - 96 / size.y);
            var points = new List<Vector3>();
            foreach (var x in new[] { -halfWidth, halfWidth })
                foreach (var z in new[] { -halfDepth, top })
                    foreach (var y in new[] { 0f, 0.9f })
                        points.Add(new Vector3(x, y, z));
            FitInto(cam, points, free);
            return (cam.transform.position, new Vector3((free.center.x - 0.5f) * size.x, 90, 0));
        }

        /// <summary>Moves the camera (fixed rotation) so the points' screen bounds fill `rect` (viewport units).</summary>
        private static void FitInto(Camera cam, List<Vector3> points, Rect rect)
        {
            var centre = points.Aggregate(Vector3.zero, (s, p) => s + p) / points.Count;
            var t = cam.transform;
            for (var dist = 4f; dist < 60; dist += 0.05f)
            {
                t.position = centre - t.forward * dist;
                for (var k = 0; k < 6; k++)
                {
                    var vp = points.Select(cam.WorldToViewportPoint).ToArray();
                    var mid = new Vector2((vp.Min(p => p.x) + vp.Max(p => p.x)) / 2, (vp.Min(p => p.y) + vp.Max(p => p.y)) / 2);
                    var shift = rect.center - mid;
                    var height = 2 * dist * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);
                    t.position -= t.right * shift.x * height * cam.aspect + t.up * shift.y * height;
                }
                var fit = points.Select(cam.WorldToViewportPoint).ToArray();
                if (fit.All(p => p.x >= rect.xMin && p.x <= rect.xMax && p.y >= rect.yMin && p.y <= rect.yMax)) return;
            }
        }
    }
}
