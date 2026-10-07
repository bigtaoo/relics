using System.IO;
using Automatic.Boot;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using YooAsset.Editor;

namespace Automatic.Editor
{
    /// <summary>
    /// One-click, idempotent project configuration for the hot update validation (design/07 §7).
    /// Run once after opening the project, then again whenever these settings drift.
    /// </summary>
    public static class ProjectSetup
    {
        public const string HotResRoot = "Assets/HotRes";
        public const string HotDllDir = HotResRoot + "/Dlls";
        private const string BootScene = "Assets/Boot/Boot.unity";

        [MenuItem("Automatic/1. Setup Project", priority = 1)]
        public static void Run()
        {
            ConfigurePlayer();
            ConfigureHybridClr();
            EnsureRenderPipeline();
            EnsureOutlinePass();
            EnsureHotResources();
            ConfigureCollector();
            EnsureBootScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Done. Next: HybridCLR/Installer (once), then Automatic/3. Build Player.");
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "bigtaoo";
            PlayerSettings.productName = "Relics";
            // Shell version; CI overwrites it (design/09 §3). Never ship 0.0.0.
            PlayerSettings.bundleVersion = "0.1.0";
            foreach (var target in new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS })
            {
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetApiCompatibilityLevel(target, ApiCompatibilityLevel.NET_Standard);
            }
            // GPU skinning: 300 skinned units 4.9 -> 4.0 ms on the PC (design/08 §2); the A16 still to compare.
            PlayerSettings.meshDeformation = MeshDeformation.GPUBatched;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            // Local CDN is plain http during validation; tighten when a real CDN exists.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        }

        private static void ConfigureHybridClr()
        {
            var s = HybridCLRSettings.Instance;
            s.enable = true;
            s.hotUpdateAssemblies = BootConfig.HotUpdateAssemblies;
            s.patchAOTAssemblies = new[] { "mscorlib.dll", "System.dll", "System.Core.dll" };
            HybridCLRSettings.Save();
        }

        private static void EnsureRenderPipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;
            Directory.CreateDirectory("Assets/Settings");
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/Settings/URP-Renderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, "Assets/Settings/URP-Pipeline.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
        }

        /// <summary>
        /// Draws every "Outline" pass of Relics/Toon in one go after the opaques. Left in the forward
        /// draw, URP renders each object's forward and outline passes back to back, the shader switches
        /// on every draw and the SRP Batcher cannot batch: 234 SetPass calls on the full board instead
        /// of 6 (design/08 §2). The renderer ships in the player, so this is a shell change.
        /// </summary>
        private static void EnsureOutlinePass()
        {
            const string featureName = "Toon Outline";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP-Renderer.asset");
            if (renderer.rendererFeatures.Exists(f => f != null && f.name == featureName)) return;
            var feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = featureName;
            feature.settings.passTag = featureName;
            feature.settings.Event = RenderPassEvent.AfterRenderingOpaques;
            feature.settings.filterSettings.RenderQueueType = RenderQueueType.Opaque;
            feature.settings.filterSettings.LayerMask = ~0;
            feature.settings.filterSettings.PassNames = new[] { "Outline" };
            AssetDatabase.AddObjectToAsset(feature, renderer);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            var so = new SerializedObject(renderer);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.InsertArrayElementAtIndex(features.arraySize);
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.InsertArrayElementAtIndex(map.arraySize);
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(renderer);
        }

        private static void EnsureHotResources()
        {
            Directory.CreateDirectory(HotDllDir);
            const string matPath = HotResRoot + "/Bronze.mat";
            const string prefabPath = HotResRoot + "/HotCube.prefab";
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                // Bronze: the "relic" material of design/04 §6. Change it to test art hot update.
                var mat = new Material(shader) { color = new Color(0.55f, 0.38f, 0.18f) };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                PrefabUtility.SaveAsPrefabAsset(cube, prefabPath);
                Object.DestroyImmediate(cube);
            }
            AssetDatabase.Refresh();
        }

        private static void ConfigureCollector()
        {
            var setting = BundleCollectorSettingData.Setting;
            var package = setting.Packages.Find(p => p.PackageName == BootConfig.PackageName)
                          ?? BundleCollectorSettingData.CreatePackage(BootConfig.PackageName);
            package.EnableAddressable = true;
            var group = package.Groups.Find(g => g.GroupName == "Default")
                        ?? BundleCollectorSettingData.CreateGroup(package, "Default");
            var collector = group.Collectors.Find(c => c.CollectPath == HotResRoot);
            if (collector == null)
            {
                BundleCollectorSettingData.CreateCollector(group, new BundleCollector
                {
                    CollectPath = HotResRoot,
                    CollectorGUID = AssetDatabase.AssetPathToGUID(HotResRoot),
                    AddressRuleName = nameof(AddressByFileName),
                    PackRuleName = nameof(PackDirectoryMaterialsApart),
                    FilterRuleName = nameof(CollectAll),
                });
            }
            else
            {
                collector.PackRuleName = nameof(PackDirectoryMaterialsApart);
            }
            BundleCollectorSettingData.SaveFile();
        }

        private static void EnsureBootScene()
        {
            if (!File.Exists(BootScene))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                new GameObject("Boot").AddComponent<Boot.Boot>();
                var cam = Camera.main.transform;
                cam.position = new Vector3(0f, 3f, -4f);
                cam.LookAt(Vector3.zero);
                EditorSceneManager.SaveScene(scene, BootScene);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScene, true) };
        }
    }
}
