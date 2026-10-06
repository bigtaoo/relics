using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Automatic.Editor
{
    /// <summary>A unit on the board slice scene: pedestal root and the animated model under it.</summary>
    internal sealed class SliceUnit
    {
        public Transform Root;
        public GameObject Model;
    }

    /// <summary>An effect instance: spawned at Start, optionally moved along a path (local time in seconds).</summary>
    internal sealed class SliceFx
    {
        public float Start;
        public GameObject Go;
        public Action<float> Move;
        public bool Live;
    }

    /// <summary>
    /// Plays unit animations and particle effects on the board slice scene on a fixed timeline
    /// (design/08 §1, §3). Particles are stepped by hand (Simulate) with fixed seeds, so the run
    /// is deterministic and works headless. Shared by FxSlice and UiSlice.
    /// </summary>
    internal sealed class FxTimeline
    {
        private const int Substeps = 4;

        public readonly List<(SliceUnit Unit, string Clip, float Start)> Anims = new();
        public readonly List<SliceFx> Effects = new();
        /// <summary>Material changes: the unit wears After from At on, Before until then.</summary>
        public readonly List<(SliceUnit Unit, Material Before, Material After, float At)> Swaps = new();
        public int Peak;

        private readonly Dictionary<string, AnimationClip> clips;
        private uint seed = 1;
        private float last = -1;

        public FxTimeline(Dictionary<string, AnimationClip> clips) => this.clips = clips;

        /// <summary>Advances to time t; call with increasing times (frame by frame).</summary>
        public void Step(float t)
        {
            foreach (var (unit, clip, start) in Anims)
            {
                var c = clips[clip];
                if (t < start) clips["idle"].SampleAnimation(unit.Model, t % clips["idle"].length); // idles until its clip starts
                else if (t - start <= c.length) c.SampleAnimation(unit.Model, t - start);
                else clips["idle"].SampleAnimation(unit.Model, (t - start - c.length) % clips["idle"].length);
            }
            foreach (var (unit, before, after, at) in Swaps) Wear(unit, t < at ? before : after);
            var dt = last < 0 ? 0 : t - last;
            last = t;
            foreach (var e in Effects)
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
                for (var s = 1; s <= Substeps; s++)
                {
                    e.Move?.Invoke(t - dt * (1 - (float)s / Substeps) - e.Start);
                    foreach (var ps in systems) ps.Simulate(dt / Substeps, false, false, false);
                }
            }
            Peak = Mathf.Max(Peak, Effects.Where(e => e.Live).Sum(e => e.Go.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount)));
        }

        public int SystemCount => Effects.Sum(e => e.Go.GetComponentsInChildren<ParticleSystem>(true).Length);

        // ---- Board helpers ------------------------------------------------------------------

        /// <summary>Opens the board slice scene (not saved afterwards) and returns its units and Zheng's clips.</summary>
        public static (List<SliceUnit>, Dictionary<string, AnimationClip>) OpenBoard()
        {
            EditorSceneManager.OpenScene(BoardSlice.ScenePath, OpenSceneMode.Single);
            var clips = AssetDatabase.LoadAllAssetsAtPath(BoardSlice.ZhengDir + "zheng.fbx").OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            var units = GameObject.Find("Units").transform.Cast<Transform>()
                .Select(r => new SliceUnit { Root = r, Model = r.GetComponentInChildren<Animator>().gameObject }).ToList();
            return (units, clips);
        }

        public static SliceUnit Nearest(List<SliceUnit> units, Vector3 p) => units.OrderBy(u => (u.Root.position - p).sqrMagnitude).First();

        public static Material Variant(string variant) =>
            AssetDatabase.LoadAssetAtPath<Material>(BoardSlice.ZhengDir + "zheng_" + variant + ".mat");

        public static void Wear(SliceUnit unit, Material mat)
        {
            foreach (var r in unit.Model.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        }

        public static SliceFx Spawn(string prefab, Vector3 pos, float start)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(FxPrefabs.Load(prefab));
            go.transform.position = pos;
            go.SetActive(false);
            return new SliceFx { Start = start, Go = go };
        }

        /// <summary>
        /// Preparation phase: only the player's side, no acting unit, no health bars, every piece an
        /// artifact of its cost tier, idling out of step with its neighbours (pieces always move);
        /// the board scene is saved in the battle phase, where the formed hand is already living.
        /// </summary>
        public void PrepPhase(List<SliceUnit> units)
        {
            foreach (var u in units.Where(u => u.Root.position.z > 0)) u.Root.gameObject.SetActive(false);
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Where(t => t.name is "ActiveRing" or "BarBack" or "BarFill"))
                t.gameObject.SetActive(false);
            var mine = units.Where(u => u.Root.gameObject.activeSelf).ToList();
            foreach (var u in mine) Wear(u, Variant(BoardSlice.TierOf(u.Root)));
            Anims.AddRange(mine.Select((u, i) => (u, "idle", -(i * 1.37f % 4))));
        }

        /// <summary>The example hand: five cells of the player's half forming a ring, linked in this order.</summary>
        public static readonly (int Row, int Col)[] HandCells = { (0, 1), (0, 3), (1, 4), (2, 2), (1, 0) };

        /// <summary>When piece i of the hand lights up, relative to the start of the hand-formed show.</summary>
        public static float LightAt(int i) => 0.15f + 0.12f * i; // one by one, like tiles turned over

        public static float SealAt => LightAt(HandCells.Length - 1) + 0.15f;

        /// <summary>
        /// Hand-formed show starting at `offset`: each piece lights up (pillar) and awakens, turning
        /// from artifact to living inside the pillar's flash (only formed pieces come alive, 04 §6),
        /// links draw the hand's shape, the seal lands at the centre. Returns the hand's units in order.
        /// </summary>
        public SliceUnit[] HandFormed(List<SliceUnit> units, float offset)
        {
            var hand = HandCells.Select(c => Nearest(units, BoardLayout.CellPos(-1, c.Row, c.Col))).ToArray();
            var living = Variant("living");
            for (var i = 0; i < hand.Length; i++)
            {
                var at = offset + LightAt(i);
                Anims.RemoveAll(a => a.Unit == hand[i]);
                Anims.Add((hand[i], "awaken", at));
                Swaps.Add((hand[i], Variant(BoardSlice.TierOf(hand[i].Root)), living, at + 0.1f));
                Effects.Add(Spawn(FxPrefabs.HuPiece, hand[i].Root.position, at));
                if (i == 0) continue;
                Effects.Add(Link(hand[i - 1].Root.position, hand[i].Root.position, at + 0.05f));
            }
            Effects.Add(Link(hand[^1].Root.position, hand[0].Root.position, offset + LightAt(hand.Length - 1) + 0.1f));
            var centre = hand.Aggregate(Vector3.zero, (s, u) => s + u.Root.position) / hand.Length;
            Effects.Add(Spawn(FxPrefabs.HuSeal, centre, offset + SealAt));
            return hand;
        }

        private static SliceFx Link(Vector3 a, Vector3 b, float start)
        {
            var link = Spawn(FxPrefabs.HuLink, (a + b) / 2, start);
            var d = new Vector3(b.x - a.x, 0, b.z - a.z);
            link.Go.transform.rotation = Quaternion.FromToRotation(Vector3.right, d);
            link.Go.transform.localScale = new Vector3(d.magnitude, 1, 1);
            return link;
        }
    }
}
