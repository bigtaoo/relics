using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Automatic.Editor
{
    /// <summary>
    /// Building blocks for the generated particle prefabs (FxPrefabs, FxBattle): one particle
    /// system per layer with neutral defaults, plus shorthands for the modules they use.
    /// </summary>
    internal static class FxKit
    {
        internal static void Save(string name, Action<Transform> build)
        {
            var root = new GameObject(name);
            build(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, FxAssets.Dir + name + ".prefab");
            Object.DestroyImmediate(root);
        }

        /// <summary>One particle system child with neutral defaults: one shot, world space, no emission or shape.</summary>
        internal static void Layer(Transform root, string name, Material mat, float y,
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

        internal static void Burst(ParticleSystem ps, int count)
        {
            var e = ps.emission;
            e.SetBursts(new[] { new ParticleSystem.Burst(0, count) });
        }

        internal static void Rate(ParticleSystem ps, float rate)
        {
            var e = ps.emission;
            e.rateOverTime = rate;
        }

        internal static void Sphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        /// <summary>Cone or hemisphere pointing up (both point along local +Z by default).</summary>
        internal static void Up(ParticleSystem ps, ParticleSystemShapeType type, float radius, float thickness)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.angle = 0;
            shape.radius = radius;
            shape.radiusThickness = thickness;
            shape.rotation = new Vector3(-90, 0, 0);
        }

        internal static void Drag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = drag;
        }

        internal static void Spin(ParticleSystem ps, float degreesPerSecond)
        {
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = degreesPerSecond * Mathf.Deg2Rad;
        }

        /// <summary>Random frame of the 2x2 flame sheet, held for the particle's life.</summary>
        internal static void Flipbook(ParticleSystem ps)
        {
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = sheet.numTilesY = 2;
            sheet.frameOverTime = 0;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0, 3.99f);
        }

        internal static void Size(ParticleSystem ps, params (float t, float v)[] keys)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = Curve(keys);
        }

        /// <summary>Colour from `from` to `to` over life, with the given alpha keys.</summary>
        internal static void Colour(ParticleSystem ps, Color from, Color to, params (float t, float a)[] alpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(from, 0), new GradientColorKey(to, 1) },
                Array.ConvertAll(alpha, k => new GradientAlphaKey(k.a, k.t)));
            col.color = g;
        }

        internal static ParticleSystem.MinMaxCurve Curve(params (float t, float v)[] keys)
        {
            var curve = new AnimationCurve(Array.ConvertAll(keys, k => new Keyframe(k.t, k.v)));
            for (var i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0);
            var max = 1f;
            foreach (var k in keys) max = Mathf.Max(max, k.v);
            return new ParticleSystem.MinMaxCurve(max, Normalize(curve, max));
        }

        internal static AnimationCurve Normalize(AnimationCurve c, float max)
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
