using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YooAsset;

namespace Automatic.Game
{
    /// <summary>
    /// Crowd load for BoardBench (design/08 §2: summon builds put ~300 units on screen): adds
    /// -crowd N low-poly summons on both halves of the full board, each attacking every 1.5-3.5 s,
    /// with instanced health bars; and with -fx K keeps K hit effects playing at random summons
    /// (pooled, re-triggered when done). Summons are drawn one of two ways:
    /// - default: Animator + SkinnedMeshRenderer per summon (-optimize strips the bone
    ///   GameObjects, so the Animator writes skin matrices directly);
    /// - -vat: baked vertex animation, all summons in a few Graphics.RenderMeshInstanced calls.
    /// </summary>
    public sealed class CrowdBench : MonoBehaviour
    {
        private const float Size = 0.5f, BarWidth = 0.32f, BarHeight = 0.04f, BarY = 0.4f, Pitch = 50, Turn = 40;
        private static readonly int ClipId = Shader.PropertyToID("_Clip");
        private static readonly string[] Effects = { BattleShow.Claw, BattleShow.Claw, BattleShow.Impact, BattleShow.Dust, BattleShow.Buff };

        private sealed class Summon
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public int Side;
            public float Health, NextAttack, AttackEnd = -1;
            public Animator Animator;
            public Vector4 Clip;
        }

        private sealed class Effect
        {
            public Transform Root;
            public ParticleSystem[] Systems;
            public float Life, Until;
        }

        public static int Requested() => int.TryParse(ShopDemo.ArgAfter("-crowd"), out var n) ? n : 0;

        private readonly List<Summon> summons = new();
        private readonly List<Effect> effects = new();
        private readonly Dictionary<string, (int Row, int Frames, bool Loop)> clips = new();
        private bool vat;
        private Mesh mesh, quad;
        private Material[] bodies;
        private Material barBack;
        private Material[] barFill;
        private Matrix4x4[] matrices;
        private Vector4[] clipData;
        private MaterialPropertyBlock[] blocks;
        private readonly List<int> particleSamples = new();
        private int frame;

        public static CrowdBench Create(ResourcePackage package, int count)
        {
            var bench = new GameObject("CrowdBench").AddComponent<CrowdBench>();
            bench.Spawn(package, count);
            return bench;
        }

        public string Describe() => $"{summons.Count} summons ({(vat ? "VAT instanced" : "skinned" + (Optimized ? ", optimized hierarchy" : ""))}), " +
                                    $"{effects.Count} effects, board units {GameObject.Find("Units")?.transform.childCount ?? 0}";

        /// <summary>Live particles over the measured frames (sampled every 10th frame).</summary>
        public string Particles() => particleSamples.Count == 0 ? "particles none" :
            $"particles avg {particleSamples.Average():F0}, max {particleSamples.Max()}";

        public void ResetStats() => particleSamples.Clear();

        private static bool Optimized => System.Environment.GetCommandLineArgs().Contains("-optimize");

        private void Spawn(ResourcePackage package, int count)
        {
            vat = System.Environment.GetCommandLineArgs().Contains("-vat");
            // UnityEngine.Random, not System.Random: the shell strips System.Random.NextDouble (HotUpdateBuild.CheckShellApi).
            Random.InitState(1);
            var units = GameObject.Find("Units").transform;
            Material Load(string name) => package.LoadAssetSync<Material>(name).AssetObject as Material;
            quad = units.GetChild(0).Find("BarFill").GetComponent<MeshFilter>().sharedMesh;
            barBack = Load("crowd_bar_back");
            barFill = new[] { Load("crowd_bar_ally"), Load("crowd_bar_enemy") };

            if (vat)
            {
                mesh = package.LoadAssetSync<Mesh>("crowd_zheng_mesh").AssetObject as Mesh;
                bodies = new[] { Load("crowd_zheng_bronze"), Load("crowd_zheng_pottery") };
                foreach (var line in (package.LoadAssetSync<TextAsset>("crowd_zheng_clips").AssetObject as TextAsset).text.Split('\n').Where(l => l.Trim().Length > 0))
                {
                    var f = line.Trim().Split(' ');
                    clips[f[0]] = (int.Parse(f[1]), int.Parse(f[2]), f[3] == "1");
                }
                blocks = new[] { new MaterialPropertyBlock(), new MaterialPropertyBlock() };
            }
            var skin = vat ? null : package.LoadAssetSync<GameObject>("crowd_zheng_skin").AssetObject as GameObject;
            var pottery = vat ? null : Load("zheng_pottery");

            // Jittered grid over each half, from the river to the bench.
            foreach (var side in new[] { -1, 1 })
            {
                var n = side < 0 ? count / 2 : count - count / 2;
                const float w = 5.4f, d = 2.9f, z0 = 0.45f;
                var cols = Mathf.CeilToInt(Mathf.Sqrt(n * w / d));
                var rows = Mathf.CeilToInt(n / (float)cols);
                for (var i = 0; i < n; i++)
                {
                    var x = ((i % cols) + 0.5f + Jitter(0.3f)) / cols * w - w / 2;
                    var z = side * (z0 + ((i / cols) + 0.5f + Jitter(0.3f)) / rows * d);
                    var s = new Summon
                    {
                        Position = new Vector3(x, 0, z),
                        Rotation = Quaternion.Euler(0, (side > 0 ? 180 : 0) + Turn + Jitter(30), 0),
                        Side = side,
                        Health = 0.3f + Random.value * 0.7f,
                        NextAttack = 0.5f + Random.value * 3,
                    };
                    if (vat) s.Clip = Clip("idle", -Random.value * 4);
                    else
                    {
                        var go = Instantiate(skin, s.Position, s.Rotation);
                        go.transform.localScale = Vector3.one * Size;
                        s.Animator = go.GetComponentInChildren<Animator>();
                        if (side > 0) go.GetComponentInChildren<Renderer>().sharedMaterial = pottery;
                        if (Optimized) AnimatorUtility.OptimizeTransformHierarchy(s.Animator.gameObject, new string[0]);
                        s.Animator.Play("idle", 0, Random.value);
                    }
                    summons.Add(s);
                }
            }
            matrices = new Matrix4x4[summons.Count];
            clipData = new Vector4[summons.Count];

            if (!int.TryParse(ShopDemo.ArgAfter("-fx"), out var fx)) return;
            var prefabs = Effects.Distinct().ToDictionary(n => n, n => package.LoadAssetSync<GameObject>(n).AssetObject as GameObject);
            for (var i = 0; i < fx; i++)
            {
                var root = Instantiate(prefabs[Effects[i % Effects.Length]]).transform;
                var systems = root.GetComponentsInChildren<ParticleSystem>();
                var life = systems.Max(p => p.main.duration + p.main.startLifetime.constantMax);
                effects.Add(new Effect { Root = root, Systems = systems, Life = life, Until = i * life / fx });
            }
        }

        private float Jitter(float amount) => (Random.value * 2 - 1) * amount;

        private Vector4 Clip(string name, float start)
        {
            var c = clips[name];
            return new Vector4(c.Row, c.Frames, start, c.Loop ? 1 : 0);
        }

        private void Update()
        {
            var now = Time.timeSinceLevelLoad;
            foreach (var s in summons)
            {
                if (s.AttackEnd > 0 && now >= s.AttackEnd)
                {
                    s.AttackEnd = -1;
                    if (vat) s.Clip = Clip("idle", now);
                    else s.Animator.Play("idle", 0, 0);
                }
                if (now < s.NextAttack) continue;
                s.NextAttack = now + 1.5f + Random.value * 2;
                s.AttackEnd = now + 0.8f;
                if (vat) s.Clip = Clip("attack", now);
                else s.Animator.Play("attack", 0, 0);
            }

            foreach (var e in effects)
            {
                if (now < e.Until) continue;
                e.Until = now + e.Life;
                var at = summons[Random.Range(0, summons.Count)];
                e.Root.SetPositionAndRotation(at.Position + new Vector3(0, 0.15f, 0), Quaternion.Euler(0, Random.Range(0, 360), 0));
                foreach (var p in e.Systems)
                {
                    p.Clear();
                    p.Play(false);
                }
            }
            if (effects.Count > 0 && ++frame % 10 == 0)
                particleSamples.Add(effects.Sum(e => e.Systems.Sum(p => p.particleCount)));

            DrawBars();
            if (vat) DrawBodies();
        }

        private void DrawBodies()
        {
            for (var side = 0; side < 2; side++)
            {
                var n = 0;
                foreach (var s in summons)
                {
                    if ((s.Side > 0 ? 1 : 0) != side) continue;
                    matrices[n] = Matrix4x4.TRS(s.Position, s.Rotation, Vector3.one * Size);
                    clipData[n++] = s.Clip;
                }
                blocks[side].SetVectorArray(ClipId, clipData);
                var rp = new RenderParams(bodies[side])
                {
                    matProps = blocks[side],
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                    receiveShadows = true,
                    worldBounds = new Bounds(Vector3.zero, new Vector3(12, 4, 12)),
                };
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                    Graphics.RenderMeshInstanced(rp, mesh, sub, matrices, n);
            }
        }

        /// <summary>Back and team-coloured fill above each summon, facing the battle camera, all instanced.</summary>
        private void DrawBars()
        {
            var rot = Quaternion.Euler(Pitch, 0, 0);
            var front = rot * Vector3.back * 0.003f;
            var bounds = new Bounds(Vector3.zero, new Vector3(12, 4, 12));
            var n = 0;
            foreach (var s in summons)
                matrices[n++] = Matrix4x4.TRS(s.Position + new Vector3(0, BarY, 0), rot, new Vector3(BarWidth + 0.02f, BarHeight + 0.02f, 1));
            Graphics.RenderMeshInstanced(new RenderParams(barBack) { worldBounds = bounds }, quad, 0, matrices, n);
            for (var side = 0; side < 2; side++)
            {
                n = 0;
                foreach (var s in summons)
                    if ((s.Side > 0 ? 1 : 0) == side)
                        matrices[n++] = Matrix4x4.TRS(s.Position + new Vector3(-BarWidth * (1 - s.Health) / 2, BarY, 0) + front, rot, new Vector3(BarWidth * s.Health, BarHeight, 1));
                Graphics.RenderMeshInstanced(new RenderParams(barFill[side]) { worldBounds = bounds }, quad, 0, matrices, n);
            }
        }
    }
}
