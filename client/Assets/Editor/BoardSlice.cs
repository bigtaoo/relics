using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace Automatic.Editor
{
    /// <summary>
    /// Board slice (design/08 §2): one full board (both halves and benches filled with Zheng in
    /// mixed cost-tier materials, team pedestals and health bars, one unit acting), saved as a
    /// hot-update scene and rendered from the battle camera at the three target aspect ratios
    /// (08 §6) into artifacts/board/, with renderer and triangle counts in stats.txt.
    /// </summary>
    public static class BoardSlice
    {
        internal const string ScenePath = "Assets/HotRes/Scenes/board_west.unity";
        internal const string ZhengDir = "Assets/HotRes/Art/Zheng/";
        private const float Pitch = 50, Fov = 30;
        private const float UnitLength = 0.85f, BenchScale = 0.8f;
        private const float PedestalHeight = 0.05f;
        // Units face the other side turned by this much, so the camera sees their profile:
        // a quadruped seen straight from behind or in front reads as a blob.
        private const float Turn = 40;

        // Cost tiers weighted towards the cheap end, as a mid-game board would be.
        private static readonly string[] Tiers = { "pottery", "pottery", "pottery", "bronze", "bronze", "bronze", "jade", "jade", "gold" };

        private static readonly (string Name, int W, int H)[] Shots = { ("phone_19.5x9", 2340, 1080), ("pad_4x3", 1440, 1080), ("pc_16x9", 1920, 1080) };

        public static void Build()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BoardDressing.Build();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ZhengDir + "zheng.fbx") ?? throw new Exception("missing zheng.fbx");
            var clips = AssetDatabase.LoadAllAssetsAtPath(ZhengDir + "zheng.fbx").OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ZhengDir + "zheng_basecolor.png");
            var mats = ToonVariants.All.ToDictionary(v => v.Name, v => v.Write(ZhengDir + "zheng_" + v.Name + ".mat", tex));
            var controller = Controller(clips.Values);
            var team = new[]
            {
                (Pedestal: BoardDressing.Toon("board_pedestal_ally", new Color(0.32f, 0.52f, 0.72f), null, 0.006f), Bar: BoardDressing.Unlit("board_bar_ally", new Color(0.3f, 0.85f, 0.35f))),
                (Pedestal: BoardDressing.Toon("board_pedestal_enemy", new Color(0.72f, 0.3f, 0.24f), null, 0.006f), Bar: BoardDressing.Unlit("board_bar_enemy", new Color(0.92f, 0.22f, 0.18f))),
            };
            var barBack = BoardDressing.Unlit("board_bar_back", new Color(0.1f, 0.08f, 0.07f));
            var ring = BoardDressing.Unlit("board_active_ring", new Color(1.6f, 1.25f, 0.4f));
            var ringMesh = BoardDressing.SaveMesh("board_ring", Ring(0.4f, 0.5f));
            var pedestalMesh = BoardDressing.SaveMesh("board_pedestal", BoardDressing.Cylinder(32));

            // Orientation and scale come from a probe: the model's front is where the horn is.
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(model);
            clips["idle"].SampleAnimation(probe, 0);
            var bounds = ArtPreview.Bounds(probe);
            var horn = probe.GetComponentsInChildren<Transform>().First(t => t.name == "horn").position - bounds.center;
            var facing = Quaternion.FromToRotation(new Vector3(horn.x, 0, horn.z).normalized, Vector3.forward);
            var scale = UnitLength / Mathf.Max(bounds.size.x, bounds.size.z);
            var height = bounds.size.y * scale;
            var lift = -bounds.min.y * scale; // the model origin is not at its feet
            Debug.Log($"[Board] model bounds {bounds.min} .. {bounds.max}, scale {scale:F3}");
            Object.DestroyImmediate(probe);

            var rng = new Random(7);
            var units = new GameObject("Units").transform;
            void Place(Vector3 pos, int side, string mat, string clip, float time, float size, bool active = false)
            {
                var root = new GameObject("Unit").transform;
                root.SetParent(units);
                root.position = pos;
                var pedestal = BoardDressing.Spawn("Pedestal", pedestalMesh, team[side > 0 ? 1 : 0].Pedestal);
                pedestal.transform.SetParent(root, false);
                pedestal.transform.localScale = new Vector3(0.72f * size, PedestalHeight, 0.72f * size);

                var unit = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
                unit.transform.localPosition = new Vector3(0, PedestalHeight + lift * size, 0);
                unit.transform.localRotation = Quaternion.Euler(0, (side > 0 ? 180 : 0) + Turn, 0) * facing * model.transform.localRotation;
                unit.transform.localScale = Vector3.one * scale * size;
                foreach (var r in unit.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = Enumerable.Repeat(mats[mat], r.sharedMaterials.Length).ToArray();
                if (!unit.TryGetComponent(out Animator animator)) animator = unit.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                clips[clip].SampleAnimation(unit, time);

                Bar(root, team[side > 0 ? 1 : 0].Bar, barBack, PedestalHeight + height * size + 0.12f, (float)rng.NextDouble() * 0.6f + 0.4f);
                if (!active) return;
                var marker = BoardDressing.Spawn("ActiveRing", ringMesh, ring);
                marker.transform.SetParent(root, false);
                marker.transform.localPosition = new Vector3(0, PedestalHeight + 0.004f, 0);
            }

            foreach (var side in new[] { -1, 1 })
            {
                for (var row = 0; row < BoardLayout.Rows; row++)
                    for (var col = 0; col < BoardLayout.Cols; col++)
                    {
                        var acting = side < 0 && row == 0 && col == 2;
                        var hit = side > 0 && row == 0 && col == 2;
                        Place(BoardLayout.CellPos(side, row, col), side, acting ? "living" : Tiers[rng.Next(Tiers.Length)],
                            acting ? "attack" : hit ? "hit" : "idle", acting || hit ? 0.35f : (float)rng.NextDouble() * 2, 1, acting);
                    }
                for (var slot = 0; slot < BoardLayout.BenchSlots; slot++)
                    Place(BoardLayout.BenchPos(side, slot), side, Tiers[rng.Next(Tiers.Length)], "idle", (float)rng.NextDouble() * 2, BenchScale);
            }

            var cam = NewCamera(16f / 9);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/board"));
            Directory.CreateDirectory(outDir);
            foreach (var (name, w, h) in Shots)
            {
                Fit(cam, (float)w / h);
                Render(cam, w, h, Path.Combine(outDir, name + ".png"));
            }
            Fit(cam, 16f / 9);
            var stats = Stats(cam);
            File.WriteAllText(Path.Combine(outDir, "stats.txt"), stats);
            Debug.Log("[Board] " + stats.Replace("\n", " | "));
        }

        /// <summary>Health bar facing the battle camera: dark back and a team-coloured fill.</summary>
        private static void Bar(Transform parent, Material fill, Material back, float y, float hp)
        {
            const float w = 0.56f, h = 0.06f;
            var rot = Quaternion.Euler(Pitch, 0, 0);
            var quad = BoardDressing.Primitive(PrimitiveType.Quad);
            var bg = BoardDressing.Spawn("BarBack", quad, back);
            bg.transform.SetParent(parent, false);
            bg.transform.SetLocalPositionAndRotation(new Vector3(0, y, 0), rot);
            bg.transform.localScale = new Vector3(w + 0.03f, h + 0.03f, 1);
            var fg = BoardDressing.Spawn("BarFill", quad, fill);
            fg.transform.SetParent(parent, false);
            fg.transform.SetLocalPositionAndRotation(new Vector3(-w * (1 - hp) / 2, y, 0) + rot * Vector3.back * 0.003f, rot);
            fg.transform.localScale = new Vector3(w * hp, h, 1);
        }

        private static AnimatorController Controller(IEnumerable<AnimationClip> clips)
        {
            var path = ZhengDir + "zheng_anim.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller != null) return controller;
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;
            foreach (var clip in clips.OrderBy(c => c.name != "idle"))
                machine.AddState(clip.name).motion = clip;
            return controller;
        }

        private static Camera NewCamera(float aspect)
        {
            var cam = new GameObject("BattleCamera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.fieldOfView = Fov;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.8f, 0.7f, 0.55f);
            cam.transform.rotation = Quaternion.Euler(Pitch, 0, 0);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            Fit(cam, aspect);
            return cam;
        }

        /// <summary>Pulls the camera back until the whole table, with units standing on it, is in view.</summary>
        internal static void Fit(Camera cam, float aspect)
        {
            cam.aspect = aspect;
            var w = BoardLayout.HalfWidth;
            var d = BoardLayout.HalfDepth;
            var points = new List<Vector3>();
            foreach (var x in new[] { -w, w })
                foreach (var z in new[] { -d, d })
                    foreach (var y in new[] { 0f, 1f })
                        points.Add(new Vector3(x, y, z));
            const float margin = 0.02f;
            for (var dist = 5f; dist < 60; dist += 0.05f)
            {
                cam.transform.position = new Vector3(0, 0.3f, 0) - cam.transform.forward * dist;
                if (points.Select(cam.WorldToViewportPoint).All(p => p.x > margin && p.x < 1 - margin && p.y > margin && p.y < 1 - margin))
                    return;
            }
        }

        internal static void Render(Camera cam, int w, int h, string file)
        {
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }

        /// <summary>Static budget numbers plus a rough editor render time (not a device measurement).</summary>
        private static string Stats(Camera cam)
        {
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var skinned = renderers.OfType<SkinnedMeshRenderer>().ToArray();
            long Tris(Mesh m) => m == null ? 0 : Enumerable.Range(0, m.subMeshCount).Sum(i => (long)m.GetIndexCount(i)) / 3;
            var skinnedTris = skinned.Sum(r => Tris(r.sharedMesh));
            var staticTris = renderers.OfType<MeshRenderer>().Sum(r => Tris(r.GetComponent<MeshFilter>().sharedMesh));
            var bones = skinned.Sum(r => r.bones.Length);
            var materials = renderers.SelectMany(r => r.sharedMaterials).Distinct().Count();

            const int frames = 60;
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            cam.Render();
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < frames; i++) cam.Render();
            var tex = new Texture2D(1, 1);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); // waits for the GPU
            RenderTexture.active = null;
            var ms = sw.Elapsed.TotalMilliseconds / frames;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);

            var s = new StringBuilder();
            s.AppendLine($"units {skinned.Length}, skinned tris {skinnedTris}, bones {bones}");
            s.AppendLine($"static renderers {renderers.Length - skinned.Length}, static tris {staticTris}");
            s.AppendLine($"total tris {skinnedTris + staticTris}, distinct materials {materials}");
            s.AppendLine($"editor render 1920x1080: {ms:F2} ms/frame (this PC, editor, not a device number)");
            s.Append($"UnityStats after render: batches {UnityStats.batches}, setpass {UnityStats.setPassCalls}, tris {UnityStats.triangles}");
            return s.ToString();
        }

        /// <summary>Flat ring on the XZ plane marking the acting unit.</summary>
        private static Mesh Ring(float inner, float outer)
        {
            const int n = 48;
            var verts = new Vector3[n * 2];
            var tris = new int[n * 6];
            for (var i = 0; i < n; i++)
            {
                var a = i * Mathf.PI * 2 / n;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts[i * 2] = dir * inner;
                verts[i * 2 + 1] = dir * outer;
                int j = (i + 1) % n, t = i * 6;
                (tris[t], tris[t + 1], tris[t + 2]) = (i * 2, j * 2, i * 2 + 1);
                (tris[t + 3], tris[t + 4], tris[t + 5]) = (i * 2 + 1, j * 2, j * 2 + 1);
            }
            var m = new Mesh { vertices = verts, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
