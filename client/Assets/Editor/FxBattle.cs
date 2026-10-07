using UnityEditor;
using UnityEngine;
using static Automatic.Editor.FxKit;

namespace Automatic.Editor
{
    /// <summary>
    /// Effects of the battle presentation slice (design/08 §1, the battle the player watches):
    /// - fx_claw: the melee hit (three claw streaks, flash, sparks, a small shock ring); root at the
    ///   target's chest, local +X along the blow.
    /// - fx_shatter: an artifact piece breaking when it dies (shards of its own material bouncing
    ///   on the table, dust); the presenter puts the piece's material on the shards.
    /// - fx_buff: a hand's battle effect landing on a piece (golden rings rising, motes).
    /// - fx_dust: a puff where a piece takes off or lands.
    /// Same rules as FxPrefabs: hot-update assets, no scripts, positions set by the caller.
    /// </summary>
    internal static class FxBattle
    {
        public const string Claw = "fx_claw", Shatter = "fx_shatter", Buff = "fx_buff", Dust = "fx_dust";

        private static readonly Color Pale = new(1, 0.95f, 0.8f), Hot = new(1, 0.78f, 0.35f), Fire = new(1, 0.42f, 0.1f);
        private static readonly Color Gold = new(1, 0.7f, 0.2f), Earth = new(0.55f, 0.45f, 0.33f);

        public static void Build()
        {
            var shard = BoardDressing.SaveMesh(FxAssets.Dir, "fx_shard", ShardMesh());
            var bronze = AssetDatabase.LoadAssetAtPath<Material>(BoardSlice.ZhengDir + "zheng_bronze.mat");
            Save(Claw, ClawFx);
            Save(Shatter, root => ShatterFx(root, shard, bronze));
            Save(Buff, BuffFx);
            Save(Dust, DustFx);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Root at the target's chest; local +X is the direction of the blow.</summary>
        private static void ClawFx(Transform root)
        {
            // Three parallel streaks raking down and through, drawn as velocity-stretched sparks.
            for (var k = 0; k < 3; k++)
            {
                var offset = (k - 1) * 0.09f;
                Layer(root, "Streak" + k, FxAssets.Glow, 0, (main, ps, r) =>
                {
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    Burst(ps, 1);
                    main.startDelay = 0.015f * k;
                    main.startLifetime = 0.16f;
                    main.startSize = 0.16f;
                    main.startSpeed = 0;
                    var v = ps.velocityOverLifetime;
                    v.enabled = true;
                    v.space = ParticleSystemSimulationSpace.Local;
                    v.x = 3.2f;
                    v.y = -2.4f;
                    v.z = 0;
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = 0.13f;
                    r.lengthScale = 2;
                    Colour(ps, Pale, Hot, (0, 1), (0.6f, 1), (1, 0));
                });
                root.Find("Streak" + k).localPosition = new Vector3(-0.28f, 0.22f + offset, offset * 0.5f);
            }
            Layer(root, "Flash", FxAssets.Glow, 0, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startDelay = 0.03f;
                main.startLifetime = 0.14f;
                main.startSize = 1.1f;
                Size(ps, (0, 0.5f), (1, 1));
                Colour(ps, Pale, Hot, (0, 1), (1, 0));
            });
            Layer(root, "Sparks", FxAssets.Glow, 0, (main, ps, r) =>
            {
                Burst(ps, 18);
                main.startDelay = 0.03f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.055f);
                main.gravityModifier = 1.2f;
                Sphere(ps, 0.05f);
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.05f;
                r.lengthScale = 1;
                Colour(ps, Pale, Fire, (0, 1), (1, 0));
            });
            Layer(root, "Shock", FxAssets.Ring, -0.25f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startDelay = 0.03f;
                main.startLifetime = 0.3f;
                main.startSize = 1.1f;
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                Size(ps, (0, 0.2f), (0.5f, 0.85f), (1, 1));
                Colour(ps, Pale, Hot, (0, 0.9f), (1, 0));
            });
        }

        /// <summary>
        /// Root at the piece's feet: shards of the piece (its material, set by the caller) burst up
        /// and bounce on the table, dust rolls out. The shards stay a moment, then sink away.
        /// </summary>
        private static void ShatterFx(Transform root, Mesh shard, Material material)
        {
            var floor = new GameObject("Floor").transform;
            floor.SetParent(root, false);
            Layer(root, "Shards", material, 0.25f, (main, ps, r) =>
            {
                Burst(ps, 22);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 1.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
                main.startRotation3D = true;
                main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.gravityModifier = 2.2f;
                main.startColor = Color.black; // Relics/Toon reads vertex colour as part masks: black = body
                Up(ps, ParticleSystemShapeType.Hemisphere, 0.2f, 1);
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.separateAxes = true;
                rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-8, 8);
                var hit = ps.collision;
                hit.enabled = true;
                hit.type = ParticleSystemCollisionType.Planes;
                hit.SetPlane(0, floor);
                hit.bounce = 0.35f;
                hit.dampen = 0.45f;
                hit.radiusScale = 0.5f;
                Size(ps, (0, 1), (0.75f, 1), (1, 0));
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = shard;
                r.alignment = ParticleSystemRenderSpace.World;
            });
            Layer(root, "Flash", FxAssets.Glow, 0.3f, (main, ps, r) =>
            {
                Burst(ps, 1);
                main.startLifetime = 0.15f;
                main.startSize = 1.2f;
                Colour(ps, Pale, Hot, (0, 0.9f), (1, 0));
            });
            Layer(root, "Dust", FxAssets.Smoke, 0.08f, (main, ps, r) =>
            {
                Burst(ps, 12);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                Up(ps, ParticleSystemShapeType.Circle, 0.2f, 1);
                var shape = ps.shape;
                shape.rotation = new Vector3(90, 0, 0); // flat on the table, pushing outwards
                Drag(ps, 3);
                Size(ps, (0, 0.5f), (1, 1.4f));
                Colour(ps, Earth, Earth, (0, 0), (0.15f, 0.6f), (1, 0));
            });
        }

        /// <summary>Root at the piece's feet: two golden rings rise around it, motes stream up.</summary>
        private static void BuffFx(Transform root)
        {
            foreach (var (name, delay) in new[] { ("Ring", 0f), ("Ring2", 0.18f) })
                Layer(root, name, FxAssets.Ring, 0.05f, (main, ps, r) =>
                {
                    Burst(ps, 1);
                    main.startDelay = delay;
                    main.startLifetime = 0.7f;
                    main.startSize = 0.85f;
                    var v = ps.velocityOverLifetime;
                    v.enabled = true;
                    v.space = ParticleSystemSimulationSpace.World;
                    v.x = v.z = 0;
                    v.y = 1.1f;
                    r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                    Size(ps, (0, 1.2f), (0.3f, 1), (1, 0.7f));
                    Colour(ps, Pale, Gold, (0, 0), (0.15f, 1), (1, 0));
                });
            Layer(root, "Motes", FxAssets.Glow, 0.05f, (main, ps, r) =>
            {
                main.duration = 0.6f;
                Rate(ps, 40);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
                Up(ps, ParticleSystemShapeType.Cone, 0.3f, 0);
                Colour(ps, Pale, Gold, (0, 1), (0.7f, 1), (1, 0));
            });
        }

        /// <summary>Root on the table: a low ring of dust.</summary>
        private static void DustFx(Transform root)
        {
            Layer(root, "Dust", FxAssets.Smoke, 0.05f, (main, ps, r) =>
            {
                Burst(ps, 7);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.24f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                Up(ps, ParticleSystemShapeType.Circle, 0.15f, 1);
                var shape = ps.shape;
                shape.rotation = new Vector3(90, 0, 0);
                Drag(ps, 4);
                Size(ps, (0, 0.6f), (1, 1.3f));
                Colour(ps, Earth, Earth, (0, 0), (0.15f, 0.5f), (1, 0));
            });
        }

        /// <summary>A chunky shard: an irregular tetrahedron, about unit size, flat-shaded.</summary>
        private static Mesh ShardMesh()
        {
            var p = new[] { new Vector3(-0.5f, -0.3f, -0.4f), new Vector3(0.55f, -0.25f, -0.3f), new Vector3(0, -0.35f, 0.6f), new Vector3(0.05f, 0.5f, 0) };
            var faces = new[] { (0, 2, 1), (0, 1, 3), (1, 2, 3), (2, 0, 3) };
            var verts = new Vector3[12];
            var uvs = new Vector2[12];
            var tris = new int[12];
            for (var f = 0; f < 4; f++)
            {
                var (a, b, c) = faces[f];
                verts[f * 3] = p[a];
                verts[f * 3 + 1] = p[b];
                verts[f * 3 + 2] = p[c];
                // A patch of the body texture (the sheet's middle), so the shards show its spots.
                uvs[f * 3] = new Vector2(0.4f, 0.4f);
                uvs[f * 3 + 1] = new Vector2(0.5f, 0.4f);
                uvs[f * 3 + 2] = new Vector2(0.45f, 0.5f);
                tris[f * 3] = f * 3;
                tris[f * 3 + 1] = f * 3 + 1;
                tris[f * 3 + 2] = f * 3 + 2;
            }
            var m = new Mesh { vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
