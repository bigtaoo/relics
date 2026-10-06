using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Automatic.Editor
{
    /// <summary>
    /// Effects check (design/08 §1): builds the effect prefabs (FxPrefabs), plays them on the
    /// board slice scene together with the unit animations, and renders frame sequences from the
    /// battle camera (whole board) and a closer camera into artifacts/fx/&lt;sequence&gt;/.
    /// tools/art/fx_media.py turns the frames into GIFs and contact sheets.
    /// - ability: the acting Zheng casts "five-tail flames" at the enemy across the river.
    /// - hu: five pieces of the player's board form a hand during the preparation phase.
    /// Particles are stepped by hand (Simulate) so the run is deterministic and works headless.
    /// </summary>
    public static class FxSlice
    {
        private const float Fps = 30;
        private const int Width = 960, Height = 540;

        public static void Build()
        {
            AssetDatabase.Refresh();
            FxPrefabs.Build();
            Ability();
            Hu();
        }

        private static void Ability()
        {
            var (units, clips) = FxTimeline.OpenBoard();
            var timeline = new FxTimeline(clips);
            var caster = FxTimeline.Nearest(units, BoardLayout.CellPos(-1, 0, 2));
            var target = FxTimeline.Nearest(units, BoardLayout.CellPos(1, 0, 2));
            const float release = 0.5f, flight = 0.35f;
            var hitAt = release + flight;
            timeline.Anims.Add((caster, "cast", 0));
            timeline.Anims.Add((target, "hit", hitAt));

            timeline.Effects.Add(FxTimeline.Spawn(FxPrefabs.ZhengCharge, caster.Root.position, 0));
            clips["cast"].SampleAnimation(caster.Model, release);
            var aim = target.Root.position + Vector3.up * 0.3f;
            var right = Vector3.Cross(Vector3.up, aim - caster.Root.position).normalized;
            for (var t = 1; t <= 5; t++)
            {
                var from = caster.Model.GetComponentsInChildren<Transform>().First(b => b.name == $"tail{t}.4").position;
                var to = aim + right * ((t - 3) * 0.06f);
                var control = (from + to) / 2 + Vector3.up * 0.7f + right * ((t - 3) * 0.45f); // fanned out like the tails
                var bolt = FxTimeline.Spawn(FxPrefabs.ZhengBolt, from, release + (t - 1) * 0.03f);
                bolt.Move = s =>
                {
                    var u = Mathf.Clamp01(s / flight);
                    bolt.Go.transform.position = Vector3.Lerp(Vector3.Lerp(from, control, u), Vector3.Lerp(control, to, u), u);
                };
                timeline.Effects.Add(bolt);
            }
            timeline.Effects.Add(FxTimeline.Spawn(FxPrefabs.ZhengImpact, target.Root.position, hitAt));
            Play("ability", 2.0f, timeline, new[] { caster.Root.position, target.Root.position });
        }

        private static void Hu()
        {
            var (units, clips) = FxTimeline.OpenBoard();
            var timeline = new FxTimeline(clips);
            timeline.PrepPhase(units);
            var hand = timeline.HandFormed(units, 0);
            Play("hu", 2.8f, timeline, hand.Select(u => u.Root.position).ToArray());
        }

        /// <summary>
        /// Steps animations and particles at Fps and renders each frame twice: the battle camera
        /// framing the whole table, and a closer camera framing `focus`.
        /// </summary>
        private static void Play(string name, float duration, FxTimeline timeline, Vector3[] focus)
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/fx", name));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var cam = Object.FindFirstObjectByType<Camera>();
            BoardSlice.Fit(cam, (float)Width / Height);
            var close = Object.Instantiate(cam.gameObject).GetComponent<Camera>();
            close.tag = "Untagged";
            FitPoints(close, focus.SelectMany(p => new[] { p + new Vector3(-0.55f, 0, -0.45f), p + new Vector3(0.55f, 1.0f, 0.45f) }).ToArray());

            var frames = Mathf.RoundToInt(duration * Fps);
            for (var f = 0; f <= frames; f++)
            {
                timeline.Step(f / Fps);
                BoardSlice.Render(cam, Width, Height, Path.Combine(dir, $"full_{f:D3}.png"));
                BoardSlice.Render(close, Width, Height, Path.Combine(dir, $"close_{f:D3}.png"));
            }
            var info = $"fps {Fps}, frames {frames + 1}, effect instances {timeline.Effects.Count}, " +
                       $"particle systems {timeline.SystemCount}, peak live particles {timeline.Peak}";
            File.WriteAllText(Path.Combine(dir, "info.txt"), info + "\n");
            Debug.Log($"[Fx] {name}: {info}");
            Object.DestroyImmediate(close.gameObject);
        }

        /// <summary>Moves the camera along its view axis until all points fit, with a margin.</summary>
        private static void FitPoints(Camera cam, Vector3[] points)
        {
            cam.aspect = (float)Width / Height;
            var centre = points.Aggregate(Vector3.zero, (s, p) => s + p) / points.Length;
            for (var dist = 1f; dist < 40; dist += 0.05f)
            {
                cam.transform.position = centre - cam.transform.forward * dist;
                if (points.Select(cam.WorldToViewportPoint).All(p => p.x > 0.05f && p.x < 0.95f && p.y > 0.05f && p.y < 0.95f))
                    return;
            }
        }
    }
}
