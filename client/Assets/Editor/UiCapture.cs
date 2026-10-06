using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Automatic.Editor
{
    /// <summary>
    /// Offscreen capture for the UI slice (design/08 §3). The game draws the HUD as a screen-space
    /// overlay, after and outside post-processing, which a camera render cannot capture. So the
    /// UI is rendered by its own camera twice, over black and over white: the difference gives the
    /// exact coverage, and the black pass is the premultiplied colour. That is composited over the
    /// board render (with post) in linear space. The same trick renders the card portraits with a
    /// transparent background.
    /// </summary>
    internal static class UiCapture
    {
        public const int UiLayer = 5;

        /// <summary>Camera's render as linear colours.</summary>
        public static Color[] Read(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            for (var i = 0; i < px.Length; i++) px[i] = px[i].linear;
            return px;
        }

        /// <summary>Premultiplied colour (alpha = coverage) of what `cam` draws, from a black and a white pass.</summary>
        public static Color[] ReadAlpha(Camera cam, int w, int h)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var black = Read(cam, w, h);
            cam.backgroundColor = Color.white;
            var white = Read(cam, w, h);
            for (var i = 0; i < black.Length; i++)
            {
                var b = black[i];
                var a = 1 - ((white[i].r - b.r) + (white[i].g - b.g) + (white[i].b - b.b)) / 3;
                black[i] = new Color(b.r, b.g, b.b, Mathf.Clamp01(a));
            }
            return black;
        }

        /// <summary>Board camera render with the UI camera's output on top, saved as PNG.</summary>
        public static void Composite(Camera board, Camera ui, int w, int h, string file)
        {
            var under = Read(board, w, h);
            var over = ReadAlpha(ui, w, h);
            var px = new Color32[under.Length];
            for (var i = 0; i < px.Length; i++)
            {
                var o = over[i];
                px[i] = new Color(o.r + under[i].r * (1 - o.a), o.g + under[i].g * (1 - o.a), o.b + under[i].b * (1 - o.a)).gamma;
            }
            Save(px, w, h, file);
        }

        private static void Save(Color32[] px, int w, int h, string file)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>
        /// Card portraits: Zheng at rest as an artifact (first frame of `awaken`), one per cost
        /// tier, three-quarter view, transparent background. Rendered in a scratch scene.
        /// </summary>
        public static Sprite[] Portraits(string dir)
        {
            const int size = 384;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(45, -30, 0);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BoardSlice.ZhengDir + "zheng.fbx");
            var awaken = AssetDatabase.LoadAllAssetsAtPath(BoardSlice.ZhengDir + "zheng.fbx").OfType<AnimationClip>().First(c => c.name == "awaken");
            var unit = (GameObject)PrefabUtility.InstantiatePrefab(model);
            awaken.SampleAnimation(unit, 0);
            var horn = unit.GetComponentsInChildren<Transform>().First(t => t.name == "horn").position - ArtPreview.Bounds(unit).center;
            // Face left of camera, turned towards it, so head, body and the five tails all read.
            unit.transform.rotation = Quaternion.Euler(0, -55, 0) * Quaternion.FromToRotation(new Vector3(horn.x, 0, horn.z).normalized, Vector3.left) * unit.transform.rotation;

            var cam = new GameObject("PortraitCamera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.transform.rotation = Quaternion.Euler(18, 0, 0);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;

            var sprites = new Sprite[UiAssets.Tiers.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(BoardSlice.ZhengDir + "zheng_" + UiAssets.Tiers[i].Material + ".mat");
                foreach (var r in unit.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                var b = ArtPreview.Bounds(unit);
                cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.y) * 1.08f;
                cam.transform.position = b.center - cam.transform.forward * (b.extents.magnitude * 3);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = b.extents.magnitude * 6;
                var px = ReadAlpha(cam, size, size);
                var out32 = px.Select(p => p.a < 1e-3f ? new Color32(0, 0, 0, 0)
                    : (Color32)new Color(p.r / p.a, p.g / p.a, p.b / p.a).gamma.WithAlpha(p.a)).ToArray();
                var path = dir + "portrait_zheng_" + UiAssets.Tiers[i].Material + "_tex.png";
                Save(out32, size, size, path);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            return sprites;
        }

        private static Color WithAlpha(this Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
