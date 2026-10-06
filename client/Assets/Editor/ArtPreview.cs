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
    /// Sets up a character's toon material and renders every animation clip from the fixed
    /// battle camera (design/04 §2) into artifacts/art_preview/, so an import can be checked headless.
    /// </summary>
    public static class ArtPreview
    {
        private const string Dir = "Assets/HotRes/Art/Zheng/";
        private const int Size = 512;
        private const int FramesPerClip = 5;

        public static void Zheng()
        {
            AssetDatabase.Refresh();
            var mat = ToonMaterial(Dir + "zheng_toon.mat", Dir + "zheng_basecolor.png");
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/art_preview"));
            Render(Dir + "zheng.fbx", mat, outDir);
        }

        private static Material ToonMaterial(string path, string texturePath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Relics/Toon") ?? throw new Exception("Relics/Toon shader not found");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static void Render(string fbxPath, Material mat, string outDir)
        {
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath) ?? throw new Exception("missing " + fbxPath);
            var go = UnityEngine.Object.Instantiate(model);
            var renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            Debug.Log($"[ArtPreview] {fbxPath} bounds {bounds}");

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

            // Battle camera: fixed oblique top-down view, unit seen from the front-left.
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.86f, 0.84f, 0.78f);
            cam.fieldOfView = 30;
            var target = bounds.center;
            cam.transform.position = target + Quaternion.Euler(40, 135, 0) * Vector3.back * (bounds.extents.magnitude * 3.2f);
            cam.transform.LookAt(target);
            var rt = new RenderTexture(Size, Size, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;

            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            foreach (var clip in clips)
            {
                for (var i = 0; i < FramesPerClip; i++)
                {
                    clip.SampleAnimation(go, clip.length * i / (FramesPerClip - 1));
                    cam.Render();
                    Save(rt, Path.Combine(outDir, $"{clip.name}_{i}.png"));
                }
                Debug.Log($"[ArtPreview] clip {clip.name} {clip.length:0.00}s loop={clip.isLooping}");
            }
        }

        private static void Save(RenderTexture rt, string file)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
