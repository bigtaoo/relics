using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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

        private sealed class Unit
        {
            public Transform Root;
            public GameObject Model;
        }

        /// <summary>An effect instance: spawned at Start, optionally moved along a path (local time in seconds).</summary>
        private sealed class Fx
        {
            public float Start;
            public GameObject Go;
            public Action<float> Move;
            public bool Live;
        }

        public static void Build()
        {
            AssetDatabase.Refresh();
            FxPrefabs.Build();
            Ability();
            Hu();
        }

        private static void Ability()
        {
            var (units, clips) = OpenBoard();
            var caster = Nearest(units, BoardLayout.CellPos(-1, 0, 2));
            var target = Nearest(units, BoardLayout.CellPos(1, 0, 2));
            const float release = 0.5f, flight = 0.35f;
            var hitAt = release + flight;
            var anims = new List<(Unit, string, float)> { (caster, "cast", 0), (target, "hit", hitAt) };

            var fx = new List<Fx> { Spawn(FxPrefabs.ZhengCharge, caster.Root.position, 0) };
            clips["cast"].SampleAnimation(caster.Model, release);
            var aim = target.Root.position + Vector3.up * 0.3f;
            var right = Vector3.Cross(Vector3.up, aim - caster.Root.position).normalized;
            for (var t = 1; t <= 5; t++)
            {
                var from = caster.Model.GetComponentsInChildren<Transform>().First(b => b.name == $"tail{t}.4").position;
                var to = aim + right * ((t - 3) * 0.06f);
                var control = (from + to) / 2 + Vector3.up * 0.7f + right * ((t - 3) * 0.45f); // fanned out like the tails
                var start = release + (t - 1) * 0.03f;
                var bolt = Spawn(FxPrefabs.ZhengBolt, from, start);
                bolt.Move = s =>
                {
                    var u = Mathf.Clamp01(s / flight);
                    bolt.Go.transform.position = Vector3.Lerp(Vector3.Lerp(from, control, u), Vector3.Lerp(control, to, u), u);
                };
                fx.Add(bolt);
            }
            fx.Add(Spawn(FxPrefabs.ZhengImpact, target.Root.position, hitAt));
            Play("ability", 2.0f, units, clips, anims, fx, new[] { caster.Root.position, target.Root.position });
        }

        private static void Hu()
        {
            var (units, clips) = OpenBoard();
            // Preparation phase: only the player's side, every piece still an artifact (rest pose).
            foreach (var u in units.Where(u => u.Root.position.z > 0)) u.Root.gameObject.SetActive(false);
            // No acting unit and no health bars outside battle.
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Where(t => t.name is "ActiveRing" or "BarBack" or "BarFill"))
                t.gameObject.SetActive(false);
            var bronze = AssetDatabase.LoadAssetAtPath<Material>(BoardSlice.ZhengDir + "zheng_bronze.mat");
            foreach (var r in Nearest(units, BoardLayout.CellPos(-1, 0, 2)).Model.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(bronze, r.sharedMaterials.Length).ToArray();
            var anims = units.Where(u => u.Root.gameObject.activeSelf).Select(u => (u, "awaken", 1e6f)).ToList();

            var cells = new[] { (0, 1), (0, 3), (1, 4), (2, 2), (1, 0) }; // a ring, linked in this order
            var hand = cells.Select(c => Nearest(units, BoardLayout.CellPos(-1, c.Item1, c.Item2))).ToArray();
            var fx = new List<Fx>();
            float At(int i) => 0.15f + 0.12f * i; // pieces light up one by one, like tiles turned over
            for (var i = 0; i < hand.Length; i++)
            {
                anims.RemoveAll(a => a.Item1 == hand[i]);
                anims.Add((hand[i], "awaken", At(i)));
                fx.Add(Spawn(FxPrefabs.HuPiece, hand[i].Root.position, At(i)));
                if (i == 0) continue;
                fx.Add(LinkFx(hand[i - 1].Root.position, hand[i].Root.position, At(i) + 0.05f));
            }
            fx.Add(LinkFx(hand[^1].Root.position, hand[0].Root.position, At(hand.Length - 1) + 0.1f));
            var centre = hand.Aggregate(Vector3.zero, (s, u) => s + u.Root.position) / hand.Length;
            fx.Add(Spawn(FxPrefabs.HuSeal, centre, At(hand.Length - 1) + 0.15f));
            Play("hu", 2.8f, units, clips, anims, fx, hand.Select(u => u.Root.position).ToArray());
        }

        private static Fx LinkFx(Vector3 a, Vector3 b, float start)
        {
            var link = Spawn(FxPrefabs.HuLink, (a + b) / 2, start);
            var d = b - a;
            link.Go.transform.rotation = Quaternion.FromToRotation(Vector3.right, new Vector3(d.x, 0, d.z));
            link.Go.transform.localScale = new Vector3(new Vector3(d.x, 0, d.z).magnitude, 1, 1);
            return link;
        }

        private static (List<Unit>, Dictionary<string, AnimationClip>) OpenBoard()
        {
            EditorSceneManager.OpenScene(BoardSlice.ScenePath, OpenSceneMode.Single);
            var clips = AssetDatabase.LoadAllAssetsAtPath(BoardSlice.ZhengDir + "zheng.fbx").OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            var units = GameObject.Find("Units").transform.Cast<Transform>()
                .Select(r => new Unit { Root = r, Model = r.GetComponentInChildren<Animator>().gameObject }).ToList();
            return (units, clips);
        }

        private static Unit Nearest(List<Unit> units, Vector3 p) => units.OrderBy(u => (u.Root.position - p).sqrMagnitude).First();

        private static Fx Spawn(string prefab, Vector3 pos, float start)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(FxPrefabs.Load(prefab));
            go.transform.position = pos;
            go.SetActive(false);
            return new Fx { Start = start, Go = go };
        }

        /// <summary>
        /// Steps animations and particles at Fps and renders each frame twice: the battle camera
        /// framing the whole table, and a closer camera framing `focus`.
        /// </summary>
        private static void Play(string name, float duration, List<Unit> units, Dictionary<string, AnimationClip> clips,
            List<(Unit Unit, string Clip, float Start)> anims, List<Fx> fx, Vector3[] focus)
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/fx", name));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var cam = Object.FindFirstObjectByType<Camera>();
            BoardSlice.Fit(cam, (float)Width / Height);
            var close = Object.Instantiate(cam.gameObject).GetComponent<Camera>();
            close.tag = "Untagged";
            FitPoints(close, focus.SelectMany(p => new[] { p + new Vector3(-0.55f, 0, -0.45f), p + new Vector3(0.55f, 1.0f, 0.45f) }).ToArray());

            var seed = 1u;
            var peak = 0;
            const int substeps = 4;
            var frames = Mathf.RoundToInt(duration * Fps);
            for (var f = 0; f <= frames; f++)
            {
                var t = f / Fps;
                foreach (var (unit, clip, start) in anims)
                {
                    var c = clips[clip];
                    if (t < start) c.SampleAnimation(unit.Model, 0);
                    else if (t - start <= c.length) c.SampleAnimation(unit.Model, t - start);
                    else clips["idle"].SampleAnimation(unit.Model, (t - start - c.length) % clips["idle"].length);
                }
                foreach (var e in fx)
                {
                    if (t < e.Start) continue;
                    var systems = e.Go.GetComponentsInChildren<ParticleSystem>(true);
                    if (!e.Live)
                    {
                        e.Live = true;
                        e.Go.SetActive(true);
                        e.Move?.Invoke(0);
                        foreach (var ps in systems)
                        {
                            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            ps.useAutoRandomSeed = false;
                            ps.randomSeed = seed++;
                            ps.Simulate(0, false, true, false);
                        }
                        continue;
                    }
                    for (var s = 1; s <= substeps; s++)
                    {
                        e.Move?.Invoke(t - 1 / Fps * (1 - (float)s / substeps) - e.Start);
                        foreach (var ps in systems) ps.Simulate(1 / Fps / substeps, false, false, false);
                    }
                }
                peak = Mathf.Max(peak, fx.Where(e => e.Live).Sum(e => e.Go.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount)));
                BoardSlice.Render(cam, Width, Height, Path.Combine(dir, $"full_{f:D3}.png"));
                BoardSlice.Render(close, Width, Height, Path.Combine(dir, $"close_{f:D3}.png"));
            }
            var systemCount = fx.Sum(e => e.Go.GetComponentsInChildren<ParticleSystem>().Length);
            var info = $"fps {Fps}, frames {frames + 1}, effect instances {fx.Count}, particle systems {systemCount}, peak live particles {peak}";
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
