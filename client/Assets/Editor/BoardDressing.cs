using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace Automatic.Editor
{
    /// <summary>
    /// Board dimensions (design/08 §2). Provisional: rows and columns are still to be confirmed
    /// from the reference game (design/02 §3). Side -1 is the local player (near the camera),
    /// side +1 the opponent; row 0 is the front row.
    /// </summary>
    internal static class BoardLayout
    {
        public const int Cols = 5, Rows = 3, BenchSlots = 8;
        public const float Cell = 1, MidGap = 0.6f, BenchGap = 0.35f, BenchPitch = 0.72f;
        public const float BenchZ = MidGap / 2 + Rows * Cell + BenchGap + BenchPitch / 2;
        public const float HalfWidth = BenchSlots * BenchPitch / 2 + 0.22f;
        public const float HalfDepth = BenchZ + BenchPitch / 2 + 0.18f;

        public static Vector3 CellPos(int side, int row, int col) =>
            new((col - (Cols - 1) / 2f) * Cell, 0, side * (MidGap / 2 + Cell / 2 + row * Cell));

        public static Vector3 BenchPos(int side, int slot) => new((slot - (BenchSlots - 1) / 2f) * BenchPitch, 0, side * BenchZ);
    }

    /// <summary>
    /// Generated environment for the board slice: a "Shan Hai map" table (design/04 §6) with the
    /// grid, river and bench slots inked into one texture, the western mountains (red rock)
    /// around it, sun, and post-processing. All assets go to Assets/HotRes/Art/Board/ and are
    /// rewritten in place, so GUIDs stay stable across runs.
    /// </summary>
    internal static class BoardDressing
    {
        public const string Dir = "Assets/HotRes/Art/Board/";
        private const float TableThickness = 0.15f;
        private static readonly Color Paper = new(0.88f, 0.79f, 0.62f);
        private static readonly Color Ink = new(0.22f, 0.14f, 0.09f);

        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();

            var w = BoardLayout.HalfWidth;
            var d = BoardLayout.HalfDepth;
            var tableTex = Texture("board_table", Mathf.RoundToInt(w * 2 * 128), Mathf.RoundToInt(d * 2 * 128),
                (u, v) => TablePixel(Mathf.Lerp(-w, w, u), Mathf.Lerp(-d, d, v)), TextureWrapMode.Clamp);
            var groundTex = Texture("board_ground", 1024, 1024, GroundPixel, TextureWrapMode.Clamp);

            var top = Spawn("TableTop", SaveMesh("board_table_top", Quad(w, d)),
                Toon("board_table", Color.white, tableTex, outline: 0));
            top.transform.position = new Vector3(0, 0.001f, 0);
            var slab = Spawn("TableSlab", Primitive(PrimitiveType.Cube), Toon("board_wood", new Color(0.42f, 0.25f, 0.15f), null, 0.004f));
            slab.transform.position = new Vector3(0, -TableThickness / 2, 0);
            slab.transform.localScale = new Vector3(w * 2, TableThickness, d * 2);
            var ground = Spawn("Ground", SaveMesh("board_ground", Quad(20, 20)), Toon("board_ground", Color.white, groundTex, 0));
            ground.transform.position = new Vector3(0, -TableThickness, 4);

            Mountains();
            Sun();
            PostProcessing();
        }

        // ---- Table texture ----------------------------------------------------------------

        private static Color TablePixel(float x, float z)
        {
            var c = Paper * (0.9f + 0.1f * Fbm(x * 1.5f + 11, z * 1.5f + 7));
            var az = Mathf.Abs(z);
            var gridHalf = BoardLayout.Cols * BoardLayout.Cell / 2;
            var gridFar = BoardLayout.MidGap / 2 + BoardLayout.Rows * BoardLayout.Cell;
            if (Mathf.Abs(x) <= gridHalf && az >= BoardLayout.MidGap / 2 && az <= gridFar)
            {
                // Player half washed cool, opponent half warm; cells chequered for counting.
                c = Color.Lerp(c, z < 0 ? new Color(0.62f, 0.72f, 0.78f) : new Color(0.9f, 0.6f, 0.48f), 0.16f);
                var cx = (x + gridHalf) / BoardLayout.Cell;
                var cz = (az - BoardLayout.MidGap / 2) / BoardLayout.Cell;
                if (((int)cx + (int)cz) % 2 == 1) c *= 0.94f;
                var edge = Mathf.Min(Mathf.Min(Frac(cx), 1 - Frac(cx)), Mathf.Min(Frac(cz), 1 - Frac(cz))) * BoardLayout.Cell;
                c = Color.Lerp(c, Ink, Line(edge, 0.016f) * 0.85f);
            }
            else if (Mathf.Abs(x) <= gridHalf && az < BoardLayout.MidGap / 2)
            {
                // The river between the halves, as on a xiangqi board.
                c = Color.Lerp(c, new Color(0.55f, 0.68f, 0.66f), 0.4f);
                var wave = Mathf.Abs(Frac(z * 7 + Mathf.Sin(x * 5) * 0.4f) - 0.5f);
                c = Color.Lerp(c, Ink, Line(wave / 7, 0.008f) * 0.25f);
            }
            else if (Mathf.Abs(az - BoardLayout.BenchZ) < BoardLayout.BenchPitch / 2)
            {
                var slot = Mathf.Clamp(Mathf.Round(x / BoardLayout.BenchPitch + (BoardLayout.BenchSlots - 1) / 2f), 0, BoardLayout.BenchSlots - 1);
                var p = BoardLayout.BenchPos(Math.Sign(z), (int)slot);
                var r = Vector2.Distance(new Vector2(x, z), new Vector2(p.x, p.z));
                c = Color.Lerp(c, Ink, Line(Mathf.Abs(r - 0.3f), 0.012f) * 0.7f);
            }
            // Double frame line along the table edge.
            var border = Mathf.Min(BoardLayout.HalfWidth - Mathf.Abs(x), BoardLayout.HalfDepth - az);
            c = Color.Lerp(c, Ink, Mathf.Max(Line(Mathf.Abs(border - 0.06f), 0.014f), Line(Mathf.Abs(border - 0.11f), 0.006f)) * 0.9f);
            return c;
        }

        /// <summary>Old-map ground: blotchy paper with faint contour lines, redder towards the hills.</summary>
        private static Color GroundPixel(float u, float v)
        {
            var h = Fbm(u * 6, v * 6);
            var c = Paper * (0.82f + 0.12f * Fbm(u * 20 + 3, v * 20 + 5));
            c = Color.Lerp(c, new Color(0.78f, 0.52f, 0.38f), Mathf.Clamp01(h - 0.45f) * 0.8f);
            return Color.Lerp(c, Ink, Line(Mathf.Abs(Frac(h * 10) - 0.5f), 0.04f) * 0.18f);
        }

        // ---- Mountains, light, post -------------------------------------------------------

        private static void Mountains()
        {
            var rock = Toon("board_rock", new Color(0.74f, 0.38f, 0.24f), null, 0.012f);
            var rockDark = Toon("board_rock_dark", new Color(0.55f, 0.3f, 0.22f), null, 0.012f);
            var rng = new Random(5);
            var parent = new GameObject("Mountains").transform;
            // Flanks left and right of the table, a taller range behind the opponent.
            for (var i = 0; i < 24; i++)
            {
                var back = i >= 16;
                var x = back ? Mathf.Lerp(-9, 9, (i - 16 + (float)rng.NextDouble() * 0.6f) / 8) : (i % 2 == 0 ? -1 : 1) * Mathf.Lerp(5.8f, 9.5f, (float)rng.NextDouble());
                var z = back ? Mathf.Lerp(8, 11, (float)rng.NextDouble()) : Mathf.Lerp(-1, 9, (i / 2 + (float)rng.NextDouble()) / 8);
                var radius = Mathf.Lerp(0.6f, 1.1f, (float)rng.NextDouble()) * (back ? 1.4f : 1);
                var mesh = SaveMesh("board_mountain_" + i, Mountain(rng, radius, radius * Mathf.Lerp(1.6f, 2.4f, (float)rng.NextDouble())));
                var go = Spawn("Mountain" + i, mesh, i % 3 == 0 ? rockDark : rock);
                go.transform.SetParent(parent);
                go.transform.position = new Vector3(x, -TableThickness, z);
            }
        }

        private static void Sun()
        {
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.color = new Color(1, 0.96f, 0.88f);
            light.transform.rotation = Quaternion.Euler(55, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
        }

        private static void PostProcessing()
        {
            var path = Dir + "board_volume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            Get<Bloom>(profile).threshold.Override(1.0f);
            Get<Bloom>(profile).intensity.Override(0.5f);
            Get<ColorAdjustments>(profile).saturation.Override(8);
            Get<ColorAdjustments>(profile).contrast.Override(6);
            Get<Vignette>(profile).intensity.Override(0.22f);
            EditorUtility.SetDirty(profile);

            var volume = new GameObject("PostProcessing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T c)) return c;
            c = profile.Add<T>();
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        // ---- Asset helpers ----------------------------------------------------------------

        public static Material Toon(string name, Color color, Texture2D tex, float outline) => Mat(name, "Relics/Toon", m =>
        {
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetColor("_ShadeColor", new Color(0.7f, 0.62f, 0.66f));
            m.SetFloat("_OutlineWidth", outline);
            m.SetFloat("_RimStrength", 0);
        });

        public static Material Unlit(string name, Color color) => Mat(name, "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", color));

        private static Material Mat(string name, string shader, Action<Material> set)
        {
            var path = Dir + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(shader) ?? throw new Exception(shader + " shader not found"));
                AssetDatabase.CreateAsset(mat, path);
            }
            set(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Mesh SaveMesh(string name, Mesh mesh)
        {
            var path = Dir + name + "_mesh.asset"; // YooAsset addresses by file name, so not "<name>.asset" next to "<name>.mat"
            mesh.name = name;
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, old);
            Object.DestroyImmediate(mesh);
            return old;
        }

        public static Mesh Primitive(PrimitiveType type) => Resources.GetBuiltinResource<Mesh>(type switch
        {
            PrimitiveType.Cube => "Cube.fbx",
            _ => "Quad.fbx",
        });

        public static GameObject Spawn(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static Texture2D Texture(string name, int w, int h, Func<float, float, Color> pixel, TextureWrapMode wrap)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var px = new Color[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    px[y * w + x] = pixel((x + 0.5f) / w, (y + 0.5f) / h);
            tex.SetPixels(px);
            var path = Dir + name + "_tex.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Upward-facing quad of half size (w, d) on the XZ plane, UVs 0..tiles.</summary>
        private static Mesh Quad(float w, float d, float tiles = 1)
        {
            var m = new Mesh
            {
                vertices = new[] { new Vector3(-w, 0, -d), new Vector3(-w, 0, d), new Vector3(w, 0, d), new Vector3(w, 0, -d) },
                uv = new[] { Vector2.zero, new Vector2(0, tiles), new Vector2(tiles, tiles), new Vector2(tiles, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Unit cylinder (diameter 1, height 1, base at y = 0) with smooth sides and a flat top.</summary>
        public static Mesh Cylinder(int sides)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            for (var i = 0; i < sides; i++)
            {
                var a = i * Mathf.PI * 2 / sides;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts.AddRange(new[] { dir * 0.5f, dir * 0.5f + Vector3.up, dir * 0.5f + Vector3.up });
                normals.AddRange(new[] { dir, dir, Vector3.up });
                int j = (i + 1) % sides;
                tris.AddRange(new[] { i * 3, i * 3 + 1, j * 3, j * 3, i * 3 + 1, j * 3 + 1 });
            }
            verts.Add(Vector3.up);
            normals.Add(Vector3.up);
            for (var i = 0; i < sides; i++)
                tris.AddRange(new[] { verts.Count - 1, (i + 1) % sides * 3 + 2, i * 3 + 2 });
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Stylised peak: jittered rings narrowing to an off-centre tip, smooth normals for the outline.</summary>
        private static Mesh Mountain(Random rng, float radius, float height)
        {
            const int sides = 9;
            float[] ringY = { 0, 0.4f, 0.75f }, ringR = { 1, 0.62f, 0.3f };
            var verts = new Vector3[sides * ringY.Length + 1];
            var lean = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * radius * 0.4f;
            for (var r = 0; r < ringY.Length; r++)
                for (var s = 0; s < sides; s++)
                {
                    var a = (s + (float)rng.NextDouble() * 0.4f) / sides * Mathf.PI * 2;
                    var rr = radius * ringR[r] * Mathf.Lerp(0.75f, 1.15f, (float)rng.NextDouble());
                    verts[r * sides + s] = new Vector3(Mathf.Cos(a) * rr + lean.x * ringY[r], height * ringY[r], Mathf.Sin(a) * rr + lean.y * ringY[r]);
                }
            verts[^1] = new Vector3(lean.x, height, lean.y);
            var tris = new System.Collections.Generic.List<int>();
            for (var r = 0; r < ringY.Length; r++)
                for (var s = 0; s < sides; s++)
                {
                    int a = r * sides + s, b = r * sides + (s + 1) % sides;
                    if (r == ringY.Length - 1)
                    {
                        tris.AddRange(new[] { a, verts.Length - 1, b });
                        continue;
                    }
                    int c = a + sides, e = b + sides;
                    tris.AddRange(new[] { a, c, b, b, c, e });
                }
            var m = new Mesh { vertices = verts, triangles = tris.ToArray() };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static float Fbm(float x, float y) =>
            Mathf.PerlinNoise(x, y) * 0.55f + Mathf.PerlinNoise(x * 2.1f + 17, y * 2.1f + 31) * 0.3f + Mathf.PerlinNoise(x * 4.3f + 5, y * 4.3f + 9) * 0.15f;

        private static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>Anti-aliased line: 1 at distance 0, fading to 0 at <paramref name="width"/>.</summary>
        private static float Line(float distance, float width) => 1 - Mathf.Clamp01((distance - width * 0.5f) / (width * 0.5f));
    }
}
