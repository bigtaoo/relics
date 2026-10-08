using System;
using UnityEditor;
using UnityEngine;

namespace Automatic.Editor
{
    /// <summary>
    /// Relics/Toon material variants for one unit (design/04 §6, design/08 §1): the four cost-tier
    /// materials, pottery → bronze → jade → gold, plus the unit's own living state. All share the
    /// unit's bronze base texture; every variant but bronze recolours it (see Toon.shader).
    /// </summary>
    public sealed class ToonVariants
    {
        public string Name;
        public float Recolor = 1;
        public float BodyLum = 0.19f, SpotLum = 0.125f;
        public Color Body, Spots, Horn, TailRoot, TailTip;
        public float TailGlow;
        public Color Shade = new(0.55f, 0.5f, 0.62f);
        public Color Highlight = Color.black;
        public float HighlightSize = 0.08f;
        public Color Rim = new(1, 0.92f, 0.7f);
        public float RimPower = 4, RimStrength = 0.35f;

        /// <summary>
        /// Per unit: base texture median linear luminance of body and patina (Toon.shader divides
        /// by them to get shading detail), and the living state colours.
        /// </summary>
        private static readonly (string Unit, float BodyLum, float SpotLum, ToonVariants Living)[] Units =
        {
            // Living state (art/zheng concept/r2/living_v4.png): red-gold leopard, dark spots, gold horn, flame tails.
            ("zheng", 0.19f, 0.125f, new()
            {
                Name = "living", Body = new(0.85f, 0.36f, 0.08f), Spots = new(0.2f, 0.1f, 0.06f),
                Horn = new(0.9f, 0.68f, 0.2f), TailRoot = new(0.25f, 0.7f, 0.5f), TailTip = new(1, 0.5f, 0.05f),
                TailGlow = 0.5f, Shade = new(0.75f, 0.55f, 0.58f), Highlight = new(0.3f, 0.25f, 0.2f),
            }),
            // Living state (art/dangkang concept/living_v1.png): tan boar, ivory tusks. Its patina is
            // much darker than Zheng's and sits in the crevices, so the spots get a slightly darker
            // brown. No masks for the cream belly and pink snout: they stay body colour.
            ("dangkang", 0.15f, 0.03f, new()
            {
                Name = "living", Body = new(0.74f, 0.4f, 0.2f), Spots = new(0.55f, 0.3f, 0.16f),
                Horn = new(0.95f, 0.9f, 0.78f), TailRoot = new(0.74f, 0.4f, 0.2f), TailTip = new(0.74f, 0.4f, 0.2f),
                Shade = new(0.75f, 0.58f, 0.6f), Highlight = new(0.25f, 0.2f, 0.15f),
            }),
        };

        /// <summary>The five variants of <paramref name="unit"/>: four cost tiers, then living.</summary>
        public static ToonVariants[] For(string unit)
        {
            var u = Array.Find(Units, x => x.Unit == unit);
            if (u.Unit == null) throw new ArgumentException("no toon variants for " + unit);
            var all = new ToonVariants[Tiers.Length + 1];
            for (var i = 0; i < Tiers.Length; i++) all[i] = (ToonVariants)Tiers[i].MemberwiseClone();
            all[^1] = (ToonVariants)u.Living.MemberwiseClone();
            foreach (var v in all) (v.BodyLum, v.SpotLum) = (u.BodyLum, u.SpotLum);
            return all;
        }

        private static readonly ToonVariants[] Tiers =
        {
            // Grey pottery: matte, cool grey clay with darker fired spots. Not red clay: that read as
            // the living state (red-gold) and as the enemy's red pedestals (art/board/README.md).
            Plain("pottery", new(0.6f, 0.59f, 0.56f), new(0.33f, 0.32f, 0.31f), new(0.72f, 0.7f, 0.66f),
                shade: new(0.55f, 0.54f, 0.62f), rimStrength: 0.15f),
            // The base texture as authored: bronze with patina.
            new() { Name = "bronze", Recolor = 0, Highlight = new(0.5f, 0.42f, 0.25f), HighlightSize = 0.06f },
            // Pale jade with deeper green inclusions; light shadows and a strong rim read as translucent.
            Plain("jade", new(0.42f, 0.66f, 0.5f), new(0.2f, 0.42f, 0.3f), new(0.6f, 0.78f, 0.62f),
                shade: new(0.6f, 0.75f, 0.7f), highlight: new(0.5f, 0.55f, 0.5f), highlightSize: 0.12f,
                rim: new(0.85f, 1, 0.9f), rimPower: 2.5f, rimStrength: 0.45f),
            // Gilded: bright gold, deeper gold spots, hard highlight.
            Plain("gold", new(0.9f, 0.74f, 0.24f), new(0.72f, 0.5f, 0.1f), new(0.95f, 0.82f, 0.4f),
                shade: new(0.7f, 0.52f, 0.45f), highlight: new(1, 0.9f, 0.6f), highlightSize: 0.1f,
                rim: new(1, 0.85f, 0.4f), rimStrength: 0.5f),
        };

        private static ToonVariants Plain(string name, Color body, Color spots, Color horn, Color shade,
            Color? highlight = null, float highlightSize = 0.08f, Color? rim = null, float rimPower = 4, float rimStrength = 0.35f) =>
            new()
            {
                Name = name, Body = body, Spots = spots, Horn = horn, TailRoot = body, TailTip = body, Shade = shade,
                Highlight = highlight ?? Color.black, HighlightSize = highlightSize,
                Rim = rim ?? new Color(1, 0.92f, 0.7f), RimPower = rimPower, RimStrength = rimStrength,
            };

        /// <summary>Creates or updates the material asset at <paramref name="path"/>.</summary>
        public Material Write(string path, Texture2D baseMap)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Relics/Toon") ?? throw new Exception("Relics/Toon shader not found"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetFloat("_Recolor", Recolor);
            mat.SetFloat("_BodyLum", BodyLum);
            mat.SetFloat("_SpotLum", SpotLum);
            mat.SetColor("_BodyColor", Body);
            mat.SetColor("_SpotColor", Spots);
            mat.SetColor("_HornColor", Horn);
            mat.SetColor("_TailRootColor", TailRoot);
            mat.SetColor("_TailTipColor", TailTip);
            mat.SetFloat("_TailGlow", TailGlow);
            mat.SetColor("_ShadeColor", Shade);
            mat.SetColor("_SpecColor", Highlight);
            mat.SetFloat("_SpecSize", HighlightSize);
            mat.SetColor("_RimColor", Rim);
            mat.SetFloat("_RimPower", RimPower);
            mat.SetFloat("_RimStrength", RimStrength);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
