using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Automatic.Editor
{
    /// <summary>
    /// Writes a character's toon material variants (ToonVariants) and renders them from the fixed
    /// battle camera (design/04 §2) into artifacts/art_preview/: a lineup of all variants side by
    /// side, then 5 frames of every clip per variant, so an import can be checked headless.
    /// Unit "dangkang" reads Assets/HotRes/Art/Dangkang/dangkang.fbx, writes artifacts/art_preview/dangkang/.
    /// </summary>
    public static class ArtPreview
    {
        private const int Size = 512;
        private const int FramesPerClip = 5;
        private static readonly Quaternion CameraYaw = Quaternion.Euler(0, 135, 0);

        public static void Render(string unit)
        {
            AssetDatabase.Refresh();
            var dir = $"Assets/HotRes/Art/{char.ToUpperInvariant(unit[0])}{unit[1..]}/{unit}";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_basecolor.png");
            var mats = ToonVariants.For(unit).Select(v => (v.Name, Mat: v.Write(dir + "_" + v.Name + ".mat", tex))).ToArray();
            AssetDatabase.SaveAssets();
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/art_preview", unit));
            Directory.CreateDirectory(outDir);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(dir + ".fbx") ?? throw new Exception($"missing {dir}.fbx");
            var clips = AssetDatabase.LoadAllAssetsAtPath(dir + ".fbx").OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();

            NewStage();
            var units = mats.Select(m => Spawn(model, m.Mat)).ToArray();
            var size = Bounds(units[0]).size.x;
            var across = CameraYaw * Vector3.right; // a row across the view, left to right in the shot
            for (var i = 0; i < units.Length; i++)
                units[i].transform.position = across * ((i - (units.Length - 1) / 2f) * size * 1.1f);
            var idle = clips.First(c => c.name == "idle");
            foreach (var u in units) idle.SampleAnimation(u, 0);
            Shoot(units, 2048, 768, 0.55f, Path.Combine(outDir, "lineup.png"));

            foreach (var (u, (name, _)) in units.Zip(mats, (u, m) => (u, m)))
            {
                foreach (var other in units) other.SetActive(other == u);
                u.transform.position = Vector3.zero;
                var variantDir = Path.Combine(outDir, name);
                Directory.CreateDirectory(variantDir);
                foreach (var clip in clips)
                    for (var f = 0; f < FramesPerClip; f++)
                    {
                        clip.SampleAnimation(u, clip.length * f / (FramesPerClip - 1));
                        Shoot(new[] { u }, Size, Size, 1, Path.Combine(variantDir, $"{clip.name}_{f}.png"));
                    }
                Debug.Log($"[ArtPreview] {unit} {name}: {clips.Length} clips");
            }
        }

        private static void NewStage()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
        }

        private static GameObject Spawn(GameObject model, Material mat)
        {
            var go = UnityEngine.Object.Instantiate(model);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            return go;
        }

        internal static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Battle camera: fixed oblique top-down view, units seen from the front-left.</summary>
        private static void Shoot(GameObject[] targets, int w, int h, float distance, string file)
        {
            var b = Bounds(targets[0]);
            foreach (var t in targets) b.Encapsulate(Bounds(t));
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.86f, 0.84f, 0.78f);
            cam.fieldOfView = 30;
            cam.aspect = (float)w / h;
            cam.transform.position = b.center + CameraYaw * Quaternion.Euler(40, 0, 0) * Vector3.back * (b.extents.magnitude * 3.2f * distance);
            cam.transform.LookAt(b.center);
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            // The first render after units spawn reads skinned vertex buffers that are not filled yet
            // (the lineup came out all one material, or black); a throwaway render fills them.
            cam.Render();
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }
    }
}
