using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>
    /// The two camera framings of design/04 §2: the battle framing of the whole table and the
    /// preparation framing of the player's half, each fitted into the screen area the HUD leaves
    /// free (HudLayout). Same angle and FOV; only the position differs. Runtime port of
    /// Editor/BoardSlice.cs Fit and Editor/UiSlice.cs Frame.
    /// </summary>
    public static class DemoFraming
    {
        /// <summary>Space left beside the outermost pedestals (world units).</summary>
        private const float Margin = 0.08f;

        /// <summary>
        /// Half the framed width and depth: as wide as the pieces (the outermost pedestals, the
        /// bench's), so the table's rim may run off a portrait screen's sides and every piece is 7%
        /// bigger; as deep as the table (the board scene's TableTop).
        /// </summary>
        public static (float HalfWidth, float HalfDepth) Table()
        {
            var depth = GameObject.Find("TableTop").GetComponent<Renderer>().bounds.extents.z;
            var width = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.name == "Pedestal")
                .Max(r => Mathf.Abs(r.bounds.center.x) + r.bounds.extents.x);
            return (width + Margin, depth);
        }

        /// <summary>
        /// The whole table (pieces up to 1 high, Table()) fitted into `area` (viewport units). A
        /// portrait screen fits it by width; the height left over goes above the table, where the
        /// battle banners show, instead of below it.
        /// </summary>
        public static Vector3 Battle(Camera cam, Rect area)
        {
            var (w, d) = Table();
            Fit(cam, w, -d, d, 1, area);
            // Slide down at the same distance (a refit would step back and shrink the pieces).
            var points = Corners(w, -d, d, 1);
            var t = cam.transform;
            var dist = Vector3.Dot(points.Aggregate(Vector3.zero, (s, p) => s + p) / points.Count - t.position, t.forward);
            var height = 2 * dist * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);
            for (var k = 0; k < 6; k++)
                t.position += t.up * (points.Min(p => cam.WorldToViewportPoint(p).y) - area.yMin) * height;
            return t.position;
        }

        /// <summary>The player's half and bench (z from -halfDepth to `top`) fitted into `free` (viewport units).</summary>
        public static Vector3 Prep(Camera cam, float halfWidth, float halfDepth, float top, Rect free) =>
            Fit(cam, halfWidth, -halfDepth, top, 0.9f, free);

        /// <summary>The table from z `near` to `far` (pieces up to `height`) fitted into `rect` (viewport units).</summary>
        public static Vector3 Fit(Camera cam, float halfWidth, float near, float far, float height, Rect rect)
        {
            FitInto(cam, Corners(halfWidth, near, far, height), rect);
            return cam.transform.position;
        }

        private static List<Vector3> Corners(float halfWidth, float near, float far, float height)
        {
            var points = new List<Vector3>();
            foreach (var x in new[] { -halfWidth, halfWidth })
                foreach (var z in new[] { near, far })
                    foreach (var y in new[] { 0f, height })
                        points.Add(new Vector3(x, y, z));
            return points;
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
