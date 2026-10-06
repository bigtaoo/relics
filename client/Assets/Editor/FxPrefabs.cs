using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Automatic.Editor
{
    /// <summary>
    /// The two effects of the art slice (design/08 §1), as particle-system prefabs in
    /// Assets/HotRes/Art/Fx/ (hot-updatable assets, no scripts on them):
    /// - Zheng ability "five-tail flames": charge at the caster, five flame bolts (one per tail)
    ///   that the battle presenter flies along arcs, impact at the target.
    /// - Hand formed ("hu"): a light pillar on each piece of the hand, links between the pieces,
    ///   a bronze-mirror seal with shock rings at the hand's centre.
    /// Positions that depend on the board (bolt path, link ends) are set by the caller.
    /// </summary>
    internal static class FxPrefabs
    {
        public const string ZhengCharge = "fx_zheng_charge", ZhengBolt = "fx_zheng_bolt", ZhengImpact = "fx_zheng_impact";
        public const string HuPiece = "fx_hu_piece", HuLink = "fx_hu_link", HuSeal = "fx_hu_seal";

        private static readonly Color Fire = new(1, 0.42f, 0.1f), Hot = new(1, 0.78f, 0.35f), Ember = new(0.6f, 0.08f, 0.03f);
        private static readonly Color Gold = new(1, 0.7f, 0.2f), Pale = new(1, 0.9f, 0.6f);

        public static void Build()
        {
            FxAssets.Build();
            Save(ZhengCharge, Charge);
            Save(ZhengBolt, Bolt);
            Save(ZhengImpact, Impact);
            Save(HuPiece, Piece);
            Save(HuLink, Link);
            Save(HuSeal, SealFx);
            AssetDatabase.SaveAssets();
        }

        public static GameObject Load(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(FxAssets.Dir + name + ".prefab") ?? throw new Exception("missing " + name);

        // ---- Zheng ability ------------------------------------------------------------------

        /// <summary>Root at the caster's feet. Sigil under it, embers drawn into the chest, growing core.</summary>
        private static void Charge(Transform root)
        {
            Layer(root, "Sigil", FxAssets.Ring, 0.06f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 0.9f;
                main.startSize = 1.3f;
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                Size(ps, (0, 0.3f), (0.3f, 1), (1, 1));
                Spin(ps, 120);
                Colour(ps, Fire, Fire, (0, 0), (0.1f, 1), (0.6f, 1), (1, 0));
            });
            Layer(root, "Gather", FxAssets.Glow, 0.38f, (main, ps, r) =>
            {
                main.duration = 0.5f;
                Rate(ps, 90);
                main.startLifetime = 0.35f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.75f;
                shape.radiusThickness = 0;
                var v = ps.velocityOverLifetime;
                v.enabled = true;
                v.radial = -2.2f;
                Colour(ps, Hot, Fire, (0, 0), (0.3f, 1), (1, 0.6f));
            });
            Layer(root, "Core", FxAssets.Glow, 0.38f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 0.62f;
                main.startSize = 0.8f;
                Size(ps, (0, 0.15f), (0.85f, 1), (1, 1.3f));
                Colour(ps, Hot, Fire, (0, 0.3f), (0.85f, 1), (1, 0));
            });
        }

        /// <summary>One flame bolt, moved by the caller from caster to target in about 0.35 s.</summary>
        private static void Bolt(Transform root)
        {
            Layer(root, "Head", FxAssets.Glow, 0, (main, ps, r) =>
            {
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                Burst(ps, 1);
                main.startLifetime = 0.4f;
                main.startSize = 0.34f;
                Colour(ps, Hot, Fire, (0, 1), (0.85f, 1), (1, 0));
            });
            Layer(root, "Trail", FxAssets.Flame, 0, (main, ps, r) =>
            {
                main.duration = 0.38f;
                Rate(ps, 160);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.32f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.32f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                Flipbook(ps);
                Size(ps, (0, 1), (1, 0.2f));
                Colour(ps, Fire, Ember, (0, 1), (0.6f, 0.8f), (1, 0));
            });
            Layer(root, "Sparks", FxAssets.Glow, 0, (main, ps, r) =>
            {
                main.duration = 0.38f;
                Rate(ps, 60);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
                main.gravityModifier = 0.5f;
                Sphere(ps, 0.05f);
                Colour(ps, Pale, Fire, (0, 1), (1, 0));
            });
        }

        /// <summary>Root at the target's feet: flash, fireball, sparks, shock ring, smoke, embers.</summary>
        private static void Impact(Transform root)
        {
            const float body = 0.3f;
            Layer(root, "Flash", FxAssets.Glow, body, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 0.18f;
                main.startSize = 1.5f;
                Size(ps, (0, 0.6f), (1, 1));
                Colour(ps, Pale, Hot, (0, 1), (1, 0));
            });
            Layer(root, "Fireball", FxAssets.Flame, body, (main, ps, r) =>
            {
                Burst(ps, 22);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.gravityModifier = -0.3f;
                Sphere(ps, 0.1f);
                Drag(ps, 4);
                Flipbook(ps);
                Size(ps, (0, 0.6f), (1, 1.3f));
                Colour(ps, Hot, Ember, (0, 1), (0.5f, 0.9f), (1, 0));
            });
            Layer(root, "Sparks", FxAssets.Glow, body, (main, ps, r) =>
            {
                Burst(ps, 30);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.06f);
                main.gravityModifier = 1.2f;
                Up(ps, ParticleSystemShapeType.Hemisphere, 0.05f, 0);
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.06f;
                r.lengthScale = 1;
                Colour(ps, Pale, Fire, (0, 1), (1, 0));
            });
            Layer(root, "Shock", FxAssets.Ring, 0.06f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 0.4f;
                main.startSize = 1.9f;
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                Size(ps, (0, 0.15f), (0.4f, 0.85f), (1, 1));
                Colour(ps, Hot, Fire, (0, 1), (1, 0));
            });
            Layer(root, "Smoke", FxAssets.Smoke, body, (main, ps, r) =>
            {
                Burst(ps, 8);
                main.startDelay = 0.12f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.gravityModifier = -0.15f;
                Sphere(ps, 0.15f);
                Flipbook(ps);
                Size(ps, (0, 0.6f), (1, 1.4f));
                var smoke = new Color(0.25f, 0.18f, 0.14f);
                Colour(ps, smoke, smoke, (0, 0), (0.2f, 0.55f), (1, 0));
            });
            Layer(root, "Embers", FxAssets.Glow, body, (main, ps, r) =>
            {
                Burst(ps, 14);
                main.startDelay = 0.05f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
                main.gravityModifier = -0.4f;
                Sphere(ps, 0.15f);
                Colour(ps, Hot, Fire, (0, 1), (0.7f, 1), (1, 0));
            });
        }

        // ---- Hand formed --------------------------------------------------------------------

        /// <summary>Root at a piece's feet: light pillar shooting up, flash, ground ring, rising motes.</summary>
        private static void Piece(Transform root)
        {
            Layer(root, "Pillar", FxAssets.Pillar, 0.02f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 1.3f;
                main.startSize3D = true;
                main.startSizeX = 0.55f;
                main.startSizeY = 1.8f;
                main.startSizeZ = 0.55f;
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.separateAxes = true;
                size.x = size.z = Curve((0, 1.15f), (1, 0.55f));
                size.y = Curve((0, 0), (0.22f, 1), (1, 1));
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = FxAssets.Tube;
                r.alignment = ParticleSystemRenderSpace.World;
                Colour(ps, Pale, Gold, (0, 0), (0.08f, 0.8f), (0.5f, 0.6f), (1, 0));
            });
            Layer(root, "Flash", FxAssets.Glow, 0.35f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 0.3f;
                main.startSize = 1.3f;
                Colour(ps, Pale, Gold, (0, 1), (1, 0));
            });
            Layer(root, "Ring", FxAssets.Ring, 0.07f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 0.7f;
                main.startSize = 1.3f;
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                Size(ps, (0, 0.3f), (0.4f, 0.9f), (1, 1));
                Colour(ps, Pale, Gold, (0, 1), (1, 0));
            });
            Layer(root, "Motes", FxAssets.Glow, 0.05f, (main, ps, r) =>
            {
                main.duration = 0.9f;
                Rate(ps, 45);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                Up(ps, ParticleSystemShapeType.Cone, 0.33f, 0);
                Colour(ps, Pale, Gold, (0, 1), (0.6f, 1), (1, 0));
            });
        }

        /// <summary>
        /// Beads of light along a link between two pieces. Root at the midpoint with local X along
        /// the link; the caller sets localScale.x to the link length (shape-only scaling).
        /// </summary>
        private static void Link(Transform root)
        {
            Layer(root, "Beads", FxAssets.Glow, 0.08f, (main, ps, r) =>
            {
                main.scalingMode = ParticleSystemScalingMode.Shape;
                Burst(ps, 60);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.16f);
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
                shape.radius = 0.5f;
                shape.radiusMode = ParticleSystemShapeMultiModeValue.BurstSpread;
                var v = ps.velocityOverLifetime;
                v.enabled = true;
                v.space = ParticleSystemSimulationSpace.World;
                v.x = v.z = 0;
                v.y = 0.15f;
                Colour(ps, Pale, Gold, (0, 0), (0.1f, 1), (1, 0));
            });
        }

        /// <summary>Root at the hand's centre on the board: seal stamped down, glow, two shock rings, motes.</summary>
        private static void SealFx(Transform root)
        {
            Layer(root, "Seal", FxAssets.Seal, 0.03f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 1.9f;
                main.startSize = 3.6f;
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                Size(ps, (0, 0), (0.12f, 1.08f), (0.25f, 1), (1, 1));
                Spin(ps, 15);
                Colour(ps, Pale, Gold, (0, 0), (0.08f, 1), (0.6f, 0.9f), (1, 0));
            });
            Layer(root, "Glow", FxAssets.Glow, 0.05f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 0.8f;
                main.startSize = 3.5f;
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                Colour(ps, Pale, Gold, (0, 0.8f), (1, 0));
            });
            foreach (var (name, delay, size, life) in new[] { ("Shock", 0.05f, 7f, 0.7f), ("Shock2", 0.25f, 5f, 0.6f) })
                Layer(root, name, FxAssets.Ring, 0.06f, (main, ps, r) =>
                {
                    Burst(ps, 1);
                    main.startDelay = delay;
                    main.startLifetime = life;
                    main.startSize = size;
                    r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                    Size(ps, (0, 0.05f), (0.5f, 0.85f), (1, 1));
                    Colour(ps, Pale, Gold, (0, 1), (1, 0));
                });
            Layer(root, "Motes", FxAssets.Glow, 0.05f, (main, ps, r) =>
            {
                main.duration = 1.2f;
                Rate(ps, 90);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1, 1.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                Up(ps, ParticleSystemShapeType.Cone, 1.6f, 1);
                Colour(ps, Pale, Gold, (0, 1), (0.6f, 1), (1, 0));
            });
        }

        // ---- Helpers ------------------------------------------------------------------------

        private static void Save(string name, Action<Transform> build)
        {
            var root = new GameObject(name);
            build(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, FxAssets.Dir + name + ".prefab");
            Object.DestroyImmediate(root);
        }

        /// <summary>One particle system child with neutral defaults: one shot, world space, no emission or shape.</summary>
        private static void Layer(Transform root, string name, Material mat, float y,
            Action<ParticleSystem.MainModule, ParticleSystem, ParticleSystemRenderer> set)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0, y, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1;
            main.loop = false;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = 0;
            main.startColor = Color.white;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            set(main, ps, r);
        }

        private static void Burst(ParticleSystem ps, int count)
        {
            var e = ps.emission;
            e.SetBursts(new[] { new ParticleSystem.Burst(0, count) });
        }

        private static void Rate(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.rateOverTime = rate;
        }

        private static void Sphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        /// <summary>Cone or hemisphere pointing up (both point along local +Z by default).</summary>
        private static void Up(ParticleSystem ps, ParticleSystemShapeType type, float radius, float thickness)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.angle = 0;
            shape.radius = radius;
            shape.radiusThickness = thickness;
            shape.rotation = new Vector3(-90, 0, 0);
        }

        private static void Drag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = drag;
        }

        private static void Spin(ParticleSystem ps, float degreesPerSecond)
        {
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = degreesPerSecond * Mathf.Deg2Rad;
        }

        /// <summary>Random frame of the 2x2 flame sheet, held for the particle's life.</summary>
        private static void Flipbook(ParticleSystem ps)
        {
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = sheet.numTilesY = 2;
            sheet.frameOverTime = 0;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0, 3.99f);
        }

        private static void Size(ParticleSystem ps, params (float t, float v)[] keys)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = Curve(keys);
        }

        /// <summary>Colour from `from` to `to` over life, with the given alpha keys.</summary>
        private static void Colour(ParticleSystem ps, Color from, Color to, params (float t, float a)[] alpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(from, 0), new GradientColorKey(to, 1) },
                Array.ConvertAll(alpha, k => new GradientAlphaKey(k.a, k.t)));
            col.color = g;
        }

        private static ParticleSystem.MinMaxCurve Curve(params (float t, float v)[] keys)
        {
            var curve = new AnimationCurve(Array.ConvertAll(keys, k => new Keyframe(k.t, k.v)));
            for (var i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0);
            var max = 1f;
            foreach (var k in keys) max = Mathf.Max(max, k.v);
            return new ParticleSystem.MinMaxCurve(max, Normalize(curve, max));
        }

        private static AnimationCurve Normalize(AnimationCurve c, float max)
        {
            var keys = c.keys;
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i].value /= max;
                keys[i].inTangent /= max;
                keys[i].outTangent /= max;
            }
            return new AnimationCurve(keys);
        }
    }
}
