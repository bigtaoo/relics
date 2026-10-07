using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Automatic.Editor
{
    /// <summary>
    /// Generated mask atlas, material and meshes for the effects (design/08 §1), written to
    /// Assets/HotRes/Art/Fx/ in place (GUIDs stay stable). Every mask is a tile of one atlas and
    /// every effect layer draws with the one material (Relics/Fx), so effects batch together
    /// instead of switching material per layer (art/fx/README.md §5). A layer's look (FxLook)
    /// picks its tile with the texture sheet module and carries the colour intensity and opacity
    /// as particle custom data.
    /// Atlas 1024x512: seal 512 (left half); ring 256 at the top right; flame frames, glow and
    /// pillar 128 below it; one 256 tile and two 128 tiles still free.
    /// </summary>
    internal static class FxAssets
    {
        public const string Dir = "Assets/HotRes/Art/Fx/";
        private const int AtlasW = 1024, AtlasH = 512;

        public static Material Atlas;
        public static FxLook Glow, Flame, Ring, Seal, Pillar, Smoke;
        public static Mesh Tube;

        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var px = new float[AtlasW * AtlasH];
            Seal = Paint(px, new FxLook(2, 1, 0, 1, 1.4f, 0.6f), SealPixel);
            Ring = Paint(px, new FxLook(4, 2, 2, 1, 1.6f, 0.5f),
                (x, y) => Band(R(x, y), 0.43f, 0.035f) + 0.35f * Band(R(x, y), 0.38f, 0.08f));
            Flame = new FxLook(8, 4, 20, 4, 1.5f, 0.7f);
            for (var f = 0; f < 4; f++)
            {
                var (fx, fy) = (f % 2, f / 2);
                Paint(px, new FxLook(8, 4, 20 + f, 1, 0, 0), (x, y) => FlamePixel((x + fx) / 2, (y + fy) / 2));
            }
            Glow = Paint(px, new FxLook(8, 4, 28, 1, 1.6f, 0.35f), (x, y) => Mathf.Pow(Mathf.Clamp01(1 - R(x, y) * 2), 2.2f));
            Pillar = Paint(px, new FxLook(8, 4, 29, 1, 1.3f, 0.25f), (u, v) =>
                Mathf.Pow(1 - v, 1.6f) * Mathf.Clamp01(v * 12) * (0.65f + 0.35f * Mathf.Sin(u * Mathf.PI * 2 * 6)));
            Smoke = new FxLook(8, 4, 20, 4, 1, 1) { Under = true }; // the flame frames, opaque, under the glow
            Atlas = Mat("fx_atlas", SaveMask("fx_atlas", px));
            Tube = BoardDressing.SaveMesh(Dir, "fx_tube", OpenTube(24));
        }

        /// <summary>2x2 sheet of ragged flame blobs (noise-eaten soft discs); each cell becomes an atlas frame.</summary>
        private static float FlamePixel(float x, float y)
        {
            int cell = (x >= 0.5f ? 1 : 0) + (y >= 0.5f ? 2 : 0);
            float cx = x * 2 % 1, cy = y * 2 % 1;
            var disc = Mathf.Clamp01(1 - R(cx, cy) * 2.1f);
            var n = Noise(cx * 5 + cell * 17.3f, cy * 5 - cell * 9.1f) * 0.65f + Noise(cx * 11 + cell * 3.7f, cy * 11) * 0.35f;
            return Mathf.Clamp01((disc * 1.4f - (1 - n) * 0.7f) * 1.8f) * Mathf.Clamp01(disc * 4);
        }

        /// <summary>
        /// Seal on the board for the formed hand: a bronze-mirror back (TLV pattern), i.e. outer
        /// double rim, ring of ticks, square frame with T / L / V marks, central knob.
        /// </summary>
        private static float SealPixel(float x, float y)
        {
            float dx = x - 0.5f, dy = y - 0.5f, r = R(x, y);
            var a = Mathf.Atan2(dy, dx);
            var v = Band(r, 0.47f, 0.012f) + Band(r, 0.44f, 0.006f) + Band(r, 0.30f, 0.006f);
            var tick = Mathf.Abs(Mathf.Repeat(a / (Mathf.PI * 2) * 32, 1) - 0.5f);
            v += Band(tick, 0, 0.12f) * Band(r, 0.405f, 0.03f);
            float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy), box = Mathf.Max(ax, ay);
            v += Band(box, 0.16f, 0.008f) + Band(box, 0.145f, 0.004f);
            // T marks outside the square's edges, L marks at the rim, V marks at the corners.
            var along = Mathf.Min(ax, ay);
            var across = Mathf.Max(ax, ay);
            v += Band(along, 0, 0.01f) * Mathf.Clamp01((across - 0.165f) * 400) * Mathf.Clamp01((0.215f - across) * 400);
            v += Band(across, 0.215f, 0.008f) * Mathf.Clamp01((0.045f - along) * 400);
            v += Band(across, 0.345f, 0.008f) * Mathf.Clamp01((0.06f - along) * 400) * Mathf.Clamp01((along - 0.005f) * 400);
            v += Band(along, 0.06f, 0.008f) * Mathf.Clamp01((across - 0.30f) * 400) * Mathf.Clamp01((0.345f - across) * 400);
            var diag = Mathf.Abs(ax - ay);
            v += Band(diag, 0, 0.008f) * Mathf.Clamp01((box - 0.2f) * 400) * Mathf.Clamp01((0.27f - box) * 400);
            v += Mathf.Clamp01((0.055f - r) * 200) * 0.9f + Band(r, 0.075f, 0.006f);
            return Mathf.Clamp01(v) * Mathf.Clamp01((0.49f - r) * 100);
        }

        private static float R(float x, float y) => Mathf.Sqrt((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f));

        /// <summary>1 on a line of the given width centred at c, soft edges.</summary>
        private static float Band(float d, float c, float width) => Mathf.Clamp01(1 - Mathf.Abs(d - c) / width);

        private static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            float H(int i, int j) => Mathf.Repeat(Mathf.Sin(i * 127.1f + j * 311.7f) * 43758.547f, 1);
            return Mathf.Lerp(Mathf.Lerp(H(ix, iy), H(ix + 1, iy), fx), Mathf.Lerp(H(ix, iy + 1), H(ix + 1, iy + 1), fx), fy);
        }

        /// <summary>Fills the look's tile (frame 0 at the top left, as the texture sheet module counts) with the mask.</summary>
        private static FxLook Paint(float[] px, FxLook look, Func<float, float, float> value)
        {
            int w = AtlasW / look.TilesX, h = AtlasH / look.TilesY;
            int x0 = look.Frame % look.TilesX * w, y0 = AtlasH - (look.Frame / look.TilesX + 1) * h;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    px[(y0 + y) * AtlasW + x0 + x] = value((x + 0.5f) / w, (y + 0.5f) / h);
            return look;
        }

        private static Texture2D SaveMask(string name, float[] values)
        {
            var tex = new Texture2D(AtlasW, AtlasH, TextureFormat.RGB24, false);
            tex.SetPixels(Array.ConvertAll(values, v => new Color(v, v, v)));
            var path = Dir + name + "_tex.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false; // a mask, not a colour
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Mat(string name, Texture2D mask)
        {
            var path = Dir + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Relics/Fx") ?? throw new Exception("Relics/Fx shader not found"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", mask);
            mat.renderQueue = 3010;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Open cylinder, diameter 1, height 1, base at y = 0; v runs up the side.</summary>
        private static Mesh OpenTube(int sides)
        {
            var verts = new Vector3[(sides + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[sides * 6];
            for (var i = 0; i <= sides; i++)
            {
                var a = i * Mathf.PI * 2 / sides;
                var p = new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f);
                verts[i * 2] = p;
                verts[i * 2 + 1] = p + Vector3.up;
                uvs[i * 2] = new Vector2((float)i / sides, 0);
                uvs[i * 2 + 1] = new Vector2((float)i / sides, 1);
                if (i == sides) break;
                var t = i * 6;
                (tris[t], tris[t + 1], tris[t + 2]) = (i * 2, i * 2 + 1, i * 2 + 2);
                (tris[t + 3], tris[t + 4], tris[t + 5]) = (i * 2 + 1, i * 2 + 3, i * 2 + 2);
            }
            var m = new Mesh { vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>
    /// A layer's share of the effect atlas: frames [Frame, Frame + Frames) of a TilesX x TilesY
    /// grid (one at random per particle when Frames > 1), with the colour intensity (HDR, feeds
    /// bloom) and opacity (0 additive, 1 alpha blend) that used to be per material. Under layers
    /// (smoke, dust) sort before the others, as the separate smoke material's lower queue did.
    /// </summary>
    internal sealed class FxLook
    {
        public readonly int TilesX, TilesY, Frame, Frames;
        public readonly float Intensity, Opacity;
        public bool Under;

        public FxLook(int tilesX, int tilesY, int frame, int frames, float intensity, float opacity) =>
            (TilesX, TilesY, Frame, Frames, Intensity, Opacity) = (tilesX, tilesY, frame, frames, intensity, opacity);
    }
}
