using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace Automatic.Editor
{
    /// <summary>
    /// Generated UI assets for the shop slice (design/08 §3), written to Assets/HotRes/Ui/ in
    /// place so GUIDs stay stable. Visual language: lacquerware trays (black-red with a gold
    /// line), parchment cards in a frame tinted by the cost tier, mahjong tiles for the hand
    /// tracker, a cinnabar seal for "hu", ink-brush banners. Text is Noto Serif SC (OFL), two
    /// static weights made by tools/ui/make_fonts.py.
    /// </summary>
    internal static class UiAssets
    {
        public const string Dir = "Assets/HotRes/Ui/";
        private const string FontDir = Dir + "Fonts/";

        public static readonly Color Lacquer = new(0.17f, 0.08f, 0.06f), Paper = new(0.95f, 0.89f, 0.74f);
        public static readonly Color Ink = new(0.2f, 0.13f, 0.09f), Cinnabar = new(0.8f, 0.2f, 0.12f), GoldLine = new(0.86f, 0.66f, 0.3f);

        /// <summary>Frame tint and display name per cost tier, cheapest first (04 §6).</summary>
        public static readonly (string Material, string Label, Color Frame)[] Tiers =
        {
            ("pottery", "陶", new(0.62f, 0.6f, 0.56f)),
            ("bronze", "青铜", new(0.36f, 0.56f, 0.5f)),
            ("jade", "玉", new(0.55f, 0.8f, 0.6f)),
            ("gold", "金", new(0.98f, 0.78f, 0.3f)),
        };

        public static Sprite LacquerPanel, PaperPanel, Frame, Halo, Tile, TileBack, Dash, Seal, Coin, Glow, Button, Banner, Lock, Disc;
        public static TMP_FontAsset Body, Title;
        public static Material TitleShadow;

        public static void Build()
        {
            EnsureTmp();
            Directory.CreateDirectory(Dir);
            LacquerPanel = Sprite("ui_lacquer", 128, 128, 40, LacquerPixel);
            PaperPanel = Sprite("ui_paper", 128, 128, 28, PaperPixel);
            Frame = Sprite("ui_frame", 128, 128, 40, FramePixel);
            Tile = Sprite("ui_tile", 96, 128, 32, TilePixel);
            TileBack = Sprite("ui_tile_back", 96, 128, 32, (x, y) => TilePixel(x, y, faceUp: false));
            Dash = Sprite("ui_dash", 96, 96, 32, DashPixel);
            Seal = Sprite("ui_seal", 256, 256, 0, SealPixel);
            Coin = Sprite("ui_coin", 64, 64, 0, CoinPixel);
            Glow = Sprite("ui_glow", 128, 128, 0, (x, y) => new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - Len(x - 64, y - 64) / 64), 2)));
            // Soft glow hugging a rounded box: sliced around a card to mark it.
            Halo = Sprite("ui_halo", 160, 160, 64, (x, y) => new Color(1, 1, 1, Mathf.Exp(-Mathf.Max(0, Box(x, y, 80, 80, 48, 48, 18)) / 9)));
            Button = Sprite("ui_button", 128, 64, 24, ButtonPixel);
            Banner = Sprite("ui_banner", 512, 96, 0, BannerPixel);
            Lock = Sprite("ui_lock", 64, 64, 0, LockPixel);
            Disc = Sprite("ui_disc", 64, 64, 0, (x, y) => new Color(1, 1, 1, Edge(Len(x - 32, y - 32) - 30)));
            Body = Font("ui_serif_body");
            Title = Font("ui_serif_title");
            TitleShadow = Variant("ui_title_shadow", Title, m =>
            {
                m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, 0.6f));
                m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
                m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
            });
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// TMP shaders and settings come from the "TMP Essential Resources" package, extracted once
        /// into Assets/TextMesh Pro/ and committed (AssetDatabase.ImportPackage is asynchronous, so a
        /// batch run cannot import it and use it in the same call).
        /// </summary>
        private static void EnsureTmp()
        {
            if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
                throw new Exception("TMP Essential Resources missing: Window > TextMeshPro > Import TMP Essential Resources");
        }

        // ---- Fonts --------------------------------------------------------------------------

        /// <summary>
        /// Dynamic SDF font asset over the subset TTF: glyphs are added to the atlas on demand, so
        /// player names and new text work without rebuilding. Saved with its atlas and material.
        /// </summary>
        private static TMP_FontAsset Font(string name)
        {
            var path = FontDir + name + "_sdf.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;
            var ttf = AssetDatabase.LoadAssetAtPath<Font>(FontDir + name + ".ttf") ?? throw new Exception("missing " + name + ".ttf, run tools/ui/make_fonts.py");
            var asset = TMP_FontAsset.CreateFontAsset(ttf, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic);
            asset.name = name + "_sdf";
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTexture.name = name + "_atlas";
            asset.material.name = name + "_mat";
            AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static Material Variant(string name, TMP_FontAsset font, Action<Material> set)
        {
            var path = FontDir + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(font.material);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.CopyPropertiesFromMaterial(font.material);
            mat.shaderKeywords = font.material.shaderKeywords;
            set(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---- Sprites ------------------------------------------------------------------------

        private static Sprite Sprite(string name, int w, int h, int border, Func<float, float, Color> pixel)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(Enumerable.Range(0, w * h).Select(i => pixel(i % w + 0.5f, i / w + 0.5f)).ToArray());
            var path = Dir + name + "_tex.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static float Len(float x, float y) => Mathf.Sqrt(x * x + y * y);

        /// <summary>Signed distance to a rounded box centred at (cx, cy) with half size (hx, hy).</summary>
        private static float Box(float x, float y, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x - cx) - hx + r, qy = Mathf.Abs(y - cy) - hy + r;
            return Len(Mathf.Max(qx, 0), Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        /// <summary>Coverage of a shape from its signed distance (1 inside, 1-pixel soft edge).</summary>
        private static float Edge(float d) => Mathf.Clamp01(0.5f - d);

        private static float Line(float d, float width) => Edge(Mathf.Abs(d) - width / 2);

        private static Color Over(Color under, Color over, float a) => A(Color.Lerp(under, new Color(over.r, over.g, over.b, 1), a), Mathf.Max(under.a, a));

        private static Color A(Color c, float a)
        {
            c.a = a;
            return c;
        }

        private static float Grain(float x, float y) => Mathf.Repeat(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.547f, 1);

        private static Color LacquerPixel(float x, float y)
        {
            var d = Box(x, y, 64, 64, 64, 64, 22);
            var c = A(Color.Lerp(Lacquer * 0.75f, Lacquer, y / 128), 1);
            c = Over(c, GoldLine, Line(d + 9, 2.5f));
            c = Over(c, GoldLine * 0.7f, Line(d + 15, 1));
            return A(c, Edge(d) * 0.96f);
        }

        private static Color PaperPixel(float x, float y)
        {
            var d = Box(x, y, 64, 64, 64, 64, 14);
            var c = Paper * (0.94f + 0.06f * Grain(Mathf.Floor(x / 2), Mathf.Floor(y)));
            c = Over(A(c, 1), Ink, Line(d + 3, 3));
            return A(c, Edge(d));
        }

        /// <summary>White bevelled frame, tinted per tier by the Image colour; the inside is transparent.</summary>
        private static Color FramePixel(float x, float y)
        {
            var outer = Box(x, y, 64, 64, 64, 64, 20);
            var inner = Box(x, y, 64, 64, 50, 50, 10);
            var t = Mathf.Clamp01(-outer / 14);
            var light = 0.75f + 0.25f * (y - x) / 128 + 0.2f * Mathf.Sin(t * Mathf.PI); // lit from the top left
            var c = new Color(light, light, light, Edge(outer) * Edge(-inner));
            return A(Over(c, Ink, Line(inner, 2) * Edge(outer)), Mathf.Max(c.a, Line(inner, 2)));
        }

        /// <summary>Mahjong tile: ivory face over a jade-green back that shows as the tile's thickness.</summary>
        private static Color TilePixel(float x, float y) => TilePixel(x, y, true);

        private static Color TilePixel(float x, float y, bool faceUp)
        {
            var body = Box(x, y, 48, 64, 48, 64, 14);
            var face = Box(x, y, 48, 70, 46, 56, 12);
            var jade = new Color(0.22f, 0.5f, 0.38f);
            var ivory = new Color(0.97f, 0.94f, 0.85f);
            var c = Over(new Color(0, 0, 0, 0), jade * (0.75f + 0.25f * y / 128), Edge(body));
            c = Over(c, faceUp ? ivory * (0.93f + 0.07f * y / 128) : jade * 1.15f, Edge(face));
            return Over(c, Ink * 1.4f, Line(body, 1.5f) * 0.6f);
        }

        private static Color DashPixel(float x, float y)
        {
            var d = Box(x, y, 48, 48, 46, 46, 14);
            var along = Mathf.Repeat(Mathf.Atan2(y - 48, x - 48) / (Mathf.PI * 2) * 24, 1);
            return new Color(1, 1, 1, Line(d + 2, 3) * (along < 0.55f ? 1 : 0) + Edge(d) * 0.12f);
        }

        /// <summary>Cinnabar seal, square with worn edges and an inner border; the character goes on top as text.</summary>
        private static Color SealPixel(float x, float y)
        {
            var wear = (Grain(Mathf.Floor(x / 3), Mathf.Floor(y / 3)) - 0.5f) * 5;
            var d = Box(x, y, 128, 128, 118, 118, 10) + wear;
            var c = Cinnabar * (0.9f + 0.1f * Grain(x, y));
            var inner = Line(Box(x, y, 128, 128, 102, 102, 6) + wear * 0.5f, 5);
            var a = Edge(d) * (1 - inner) * (Grain(x * 0.7f, y * 1.3f) > 0.93f ? 0.55f : 1); // flecks where the ink missed
            return A(c, a);
        }

        /// <summary>Round coin with a square hole (ban liang / wu zhu style).</summary>
        private static Color CoinPixel(float x, float y)
        {
            var r = Len(x - 32, y - 32);
            var hole = Box(x, y, 32, 32, 8, 8, 1);
            var gold = new Color(0.95f, 0.75f, 0.3f) * (0.85f + 0.3f * (y - x + 64) / 128);
            var c = Over(new Color(0, 0, 0, 0), new Color(0.45f, 0.28f, 0.1f), Edge(r - 30));
            c = Over(c, gold, Edge(r - 27));
            c = Over(c, new Color(0.6f, 0.42f, 0.15f), Line(r - 22, 1.5f) + Line(Box(x, y, 32, 32, 12, 12, 1), 1.5f));
            return A(c, c.a * Edge(-hole));
        }

        private static Color ButtonPixel(float x, float y)
        {
            var d = Box(x, y, 64, 32, 64, 32, 16);
            var c = A(Color.Lerp(Cinnabar * 0.75f, Cinnabar * 1.1f, y / 64), 1);
            c = Over(c, new Color(1, 0.85f, 0.6f), Line(d + 5, 1.5f) * 0.8f);
            return A(c, Edge(d));
        }

        /// <summary>One ink-brush stroke, ragged at both ends and along its edges.</summary>
        private static Color BannerPixel(float x, float y)
        {
            var u = x / 512;
            var half = 40 * Mathf.Clamp01(u * 9) * Mathf.Clamp01((1 - u) * 6) + 4 * (Grain(Mathf.Floor(x / 4), 1) - 0.5f);
            var dry = Grain(Mathf.Floor(x / 2), Mathf.Floor(y / 5)) > 0.82f && Mathf.Abs(y - 48) > half * 0.6f ? 0.35f : 1;
            return A(Ink * 0.8f, Edge(Mathf.Abs(y - 48) - half) * dry * 0.95f);
        }

        private static Color LockPixel(float x, float y)
        {
            var body = Box(x, y, 32, 24, 18, 14, 4);
            var shackle = Line(Len(x - 32, y - 40) - 11, 5) * (y > 38 ? 1 : 0) + Line(Mathf.Abs(x - 32) - 11, 5) * (y > 30 && y <= 40 ? 1 : 0);
            var hole = Len(x - 32, y - 25) - 3.5f;
            return new Color(1, 1, 1, Mathf.Clamp01(Edge(body) * Edge(-hole) + shackle));
        }
    }
}
