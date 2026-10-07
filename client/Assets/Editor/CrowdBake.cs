using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Automatic.Editor
{
    /// <summary>
    /// Assets for the crowd test (design/08 §2: summon builds with ~300 units on screen), from the
    /// low-poly Zheng (tools/art/blender/lowpoly.py), in two forms to compare:
    /// - crowd_zheng_skin.prefab: the usual Animator + SkinnedMeshRenderer unit.
    /// - vertex animation textures: every clip baked at Fps into a position and a normal texture
    ///   (one column per vertex, one row per frame), a static mesh in the same vertex order, and
    ///   Relics/Toon materials with _VAT and instancing on, drawn with Graphics.RenderMeshInstanced
    ///   (no Animator, no skinning). crowd_zheng_clips.txt lists "clip firstRow frames loop".
    /// - crowd_bar_*.mat: the board's health bar materials with instancing on.
    /// Baked in unit space: facing, scale and lift as on the board, so an instance only needs its
    /// position, yaw and size.
    /// </summary>
    internal static class CrowdBake
    {
        private const string Dir = "Assets/HotRes/Art/Crowd/";
        private const int Fps = 30;
        private static readonly string[] Clips = { "idle", "attack", "hit", "leap" };
        private static readonly string[] Tiers = { "bronze", "pottery" };

        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();
            var hi = AssetDatabase.LoadAssetAtPath<GameObject>(BoardSlice.ZhengDir + "zheng.fbx");
            var lo = AssetDatabase.LoadAssetAtPath<GameObject>(BoardSlice.ZhengDir + "zheng_lo.fbx") ?? throw new System.Exception("missing zheng_lo.fbx");
            var clips = AssetDatabase.LoadAllAssetsAtPath(BoardSlice.ZhengDir + "zheng_lo.fbx").OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            var idle = AssetDatabase.LoadAllAssetsAtPath(BoardSlice.ZhengDir + "zheng.fbx").OfType<AnimationClip>().First(c => c.name == "idle");
            var (facing, scale, _, lift) = BoardSlice.Probe(hi, idle);
            var rotation = facing * lo.transform.localRotation;

            // Skinned prefab: unit space root, the model below it placed as on the board.
            var root = new GameObject("crowd_zheng_skin");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(lo, root.transform);
            model.transform.SetLocalPositionAndRotation(new Vector3(0, lift, 0), rotation);
            model.transform.localScale = Vector3.one * scale;
            if (!model.TryGetComponent(out Animator animator)) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = BoardSlice.Controller(clips.Values);
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(BoardSlice.ZhengDir + "zheng_bronze.mat");
            PrefabUtility.SaveAsPrefabAsset(root, Dir + "crowd_zheng_skin.prefab");

            // Bake from the same placement, so baked vertices are in unit space.
            var source = smr.sharedMesh;
            int verts = source.vertexCount;
            var lengths = Clips.Select(c => Mathf.RoundToInt(clips[c].length * Fps) + (clips[c].isLooping ? 0 : 1)).ToArray();
            int rows = lengths.Sum();
            var pos = new Color[verts * rows];
            var nrm = new Color[verts * rows];
            var baked = new Mesh();
            var bounds = new Bounds();
            var table = new StringBuilder();
            int row = 0;
            for (var c = 0; c < Clips.Length; c++)
            {
                table.AppendLine($"{Clips[c]} {row} {lengths[c]} {(clips[Clips[c]].isLooping ? 1 : 0)}");
                for (var f = 0; f < lengths[c]; f++, row++)
                {
                    clips[Clips[c]].SampleAnimation(model, f / (float)Fps);
                    // useScale true: with false the result comes out lossyScale too small once moved to unit space.
                    smr.BakeMesh(baked, true);
                    var m = root.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                    var v = baked.vertices;
                    var n = baked.normals;
                    for (var i = 0; i < verts; i++)
                    {
                        var p = m.MultiplyPoint3x4(v[i]);
                        var d = m.MultiplyVector(n[i]).normalized;
                        pos[row * verts + i] = new Color(p.x, p.y, p.z, 1);
                        nrm[row * verts + i] = new Color(d.x, d.y, d.z, 0);
                        if (row == 0 && i == 0) bounds = new Bounds(p, Vector3.zero);
                        else bounds.Encapsulate(p);
                    }
                }
            }
            Object.DestroyImmediate(root);

            Texture2D Tex(Color[] px)
            {
                var t = new Texture2D(verts, rows, TextureFormat.RGBAHalf, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(px);
                t.Apply(false, true);
                return t;
            }
            var posTex = Save(Tex(pos), "crowd_zheng_vat_pos.asset");
            var nrmTex = Save(Tex(nrm), "crowd_zheng_vat_nrm.asset");

            // Static mesh: the source's topology, UVs and part colours, frame 0 as its shape.
            var mesh = new Mesh { name = "crowd_zheng_mesh" };
            mesh.vertices = Enumerable.Range(0, verts).Select(i => (Vector3)(Vector4)pos[i]).ToArray();
            mesh.normals = Enumerable.Range(0, verts).Select(i => (Vector3)(Vector4)nrm[i]).ToArray();
            mesh.uv = source.uv;
            mesh.colors32 = source.colors32;
            mesh.subMeshCount = source.subMeshCount;
            for (var s = 0; s < source.subMeshCount; s++) mesh.SetTriangles(source.GetTriangles(s), s);
            bounds.Expand(0.1f);
            mesh.bounds = bounds;
            Save(mesh, "crowd_zheng_mesh.asset");

            foreach (var tier in Tiers)
            {
                var mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(BoardSlice.ZhengDir + "zheng_" + tier + ".mat"));
                mat.EnableKeyword("_VAT");
                mat.SetFloat("_Vat", 1);
                mat.SetTexture("_VatPos", posTex);
                mat.SetTexture("_VatNrm", nrmTex);
                mat.SetFloat("_VatFps", Fps);
                mat.enableInstancing = true;
                Save(mat, "crowd_zheng_" + tier + ".mat");
            }
            // Instanced copies of the board's bar materials: a hot bundle only carries a shader's
            // instancing variant if some material in it has instancing on.
            foreach (var bar in new[] { "back", "ally", "enemy" })
            {
                var mat = new Material(AssetDatabase.LoadAssetAtPath<Material>(BoardDressing.Dir + "board_bar_" + bar + ".mat")) { enableInstancing = true };
                Save(mat, "crowd_bar_" + bar + ".mat");
            }
            File.WriteAllText(Dir + "crowd_zheng_clips.txt", table.ToString());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Crowd] {verts} verts, {source.triangles.Length / 3} tris, {source.bindposes.Length} bones, {rows} frames " +
                      $"({verts}x{rows} RGBAHalf x2 = {verts * rows * 16 / 1024} KB), bounds {bounds.size}\n{table}");
        }

        /// <summary>Writes over an existing asset in place, so its GUID (and references to it) survive rebakes.</summary>
        private static T Save<T>(T asset, string file) where T : Object
        {
            var path = Dir + file;
            var old = AssetDatabase.LoadAssetAtPath<T>(path);
            if (old == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }
            EditorUtility.CopySerialized(asset, old);
            EditorUtility.SetDirty(old);
            return old;
        }
    }
}
