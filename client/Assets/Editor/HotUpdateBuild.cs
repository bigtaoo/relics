using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Automatic.Boot;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.HotUpdate;
using HybridCLR.Editor.Meta;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace Automatic.Editor
{
    /// <summary>
    /// Builds a hot update (code + assets in one YooAsset package) and publishes it to the local CDN
    /// folder artifacts/cdn/{platform}. Version line: {shell}.{n} (design/09 §3).
    /// </summary>
    public static class HotUpdateBuild
    {
        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

        [MenuItem("Automatic/2. Build Hot Update (active target)", priority = 2)]
        public static void BuildActiveTarget()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            Build(target, NextVersion(target), EBundledCopyOption.None);
        }

        /// <param name="newShell">Built for a player build that ships its own Battle.Core: skips the core check.</param>
        public static string Build(BuildTarget target, string version, EBundledCopyOption bundledCopy, bool newShell = false)
        {
            if (!newShell) ShellCore.Check(target);
            CompileDllCommand.CompileDll(target);
            CheckShellApi(target);
            CopyDlls(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target), BootConfig.HotUpdateAssemblies);
            CopyDlls(SettingsUtil.GetAssembliesPostIl2CppStripDir(target), BootConfig.AotMetadataAssemblies);
            AssetDatabase.Refresh();

            var output = BuildPackage(target, version, bundledCopy);
            Publish(output, CdnDir(target), version);
            Debug.Log($"[HotUpdate] {target} {version} published to {CdnDir(target)}");
            return version;
        }

        /// <summary>
        /// Hot code can only call what the shipped shell kept: IL2CPP strips engine and BCL API the
        /// shell's own code did not use, and calling it fails at run time with MissingMethodException
        /// (design/07 §4). Checked against the stripped AOT dlls of the last player build for this
        /// target, which is the shell on the CDN; a hot update that needs more needs a new shell.
        /// </summary>
        private static void CheckShellApi(BuildTarget target)
        {
            var stripped = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            if (!Directory.Exists(stripped))
                throw new DirectoryNotFoundException($"{stripped} missing: run 'Automatic/3. Build Player' first.");
            // Hot dlls reference the BCL through the netstandard facade; with the AOT profile's
            // facade next to the stripped dlls its forwards resolve into the stripped mscorlib,
            // otherwise every BCL reference would go unchecked.
            var aotDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/ShellApiCheck", target.ToString()));
            if (Directory.Exists(aotDir)) Directory.Delete(aotDir, true);
            Directory.CreateDirectory(aotDir);
            foreach (var dll in Directory.GetFiles(stripped, "*.dll")) File.Copy(dll, Path.Combine(aotDir, Path.GetFileName(dll)));
            var os = Application.platform switch { RuntimePlatform.OSXEditor => "macos", RuntimePlatform.LinuxEditor => "linux", _ => "win32" };
            File.Copy(Path.Combine(EditorApplication.applicationContentsPath, $"MonoBleedingEdge/lib/mono/unityaot-{os}/Facades/netstandard.dll"),
                Path.Combine(aotDir, "netstandard.dll"));

            var checker = new MissingMetadataChecker(aotDir, BootConfig.HotUpdateAssemblies);
            // It loads the facade without what the facade forwards to (only a dll that references
            // mscorlib directly would load it), so load those first: no false "missing System.Object".
            var cache = (AssemblyCache)typeof(MissingMetadataChecker).GetField("_assCache", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(checker);
            foreach (var bcl in new[] { "mscorlib", "System", "System.Core" }) cache.LoadModule(bcl);
            var hotDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            var failed = BootConfig.HotUpdateAssemblies.Where(n => !checker.Check(Path.Combine(hotDir, n + ".dll"))).ToArray();
            if (failed.Length > 0)
                throw new Exception($"Hot code in {string.Join(", ", failed)} uses API the shell stripped (errors above). " +
                                    "Avoid it, or add it to Assets/Boot/link.xml and ship a new shell.");
        }

        private static void CopyDlls(string sourceDir, string[] names)
        {
            foreach (var name in names)
            {
                var src = Path.Combine(sourceDir, name + ".dll");
                if (!File.Exists(src))
                    throw new FileNotFoundException(
                        $"{src} missing. AOT metadata dlls come from a player build: run 'Automatic/3. Build Player' first.");
                File.Copy(src, Path.Combine(ProjectSetup.HotDllDir, name + ".dll.bytes"), overwrite: true);
            }
        }

        private static string BuildPackage(BuildTarget target, string version, EBundledCopyOption bundledCopy)
        {
            var p = new ScriptableBuildParameters
            {
                BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = EBuildPipeline.ScriptableBuildPipeline.ToString(),
                BuildBundleType = (int)YooAsset.EBundleType.AssetBundle,
                BuildTarget = target,
                PackageName = BootConfig.PackageName,
                PackageVersion = version,
                EnableSharePackRule = true,
                VerifyBuildingResult = true,
                FileNameStyle = YooAsset.EFileNameStyle.HashName,
                BundledCopyOption = bundledCopy,
                CompressOption = ECompressOption.LZ4,
                BuiltinShadersBundleName = DefaultBundlePackRule.CreateShadersPackRuleResult()
                    .GetBundleName(BootConfig.PackageName, BundleCollectorSettingData.Setting.UniqueBundleName),
            };
            var result = new ScriptableBuildPipeline().Run(p, true);
            if (!result.Success)
                throw new Exception($"YooAsset build failed at {result.FailedTask}: {result.ErrorInfo}");
            return result.OutputPackageDirectory;
        }

        /// <summary>
        /// Copies bundles first and the .version file last, so the live version never points at
        /// files that are not uploaded yet. Refuses a version that is not newer (design/09 §3).
        /// </summary>
        private static void Publish(string outputDir, string cdnDir, string version)
        {
            Directory.CreateDirectory(cdnDir);
            var live = LiveVersion(cdnDir);
            if (live != null && Version.Parse(version) <= Version.Parse(live))
                throw new InvalidOperationException($"Hot update {version} is not newer than live {live}.");

            var files = Directory.GetFiles(outputDir);
            foreach (var f in files.Where(f => !f.EndsWith(".version")))
                File.Copy(f, Path.Combine(cdnDir, Path.GetFileName(f)), overwrite: true);
            foreach (var f in files.Where(f => f.EndsWith(".version")))
                File.Copy(f, Path.Combine(cdnDir, Path.GetFileName(f)), overwrite: true);
        }

        private static string LiveVersion(string cdnDir)
        {
            if (!Directory.Exists(cdnDir)) return null;
            var file = Directory.GetFiles(cdnDir, "*.version").FirstOrDefault();
            return file == null ? null : File.ReadAllText(file).Trim();
        }

        public static string NextVersion(BuildTarget target)
        {
            var shell = PlayerSettings.bundleVersion;
            var live = LiveVersion(CdnDir(target));
            int n = 0;
            if (live != null && live.StartsWith(shell + ".") && int.TryParse(live.Substring(shell.Length + 1), out var last))
                n = last + 1;
            return $"{shell}.{n}";
        }

        public static string CdnDir(BuildTarget target) => Path.Combine(RepoRoot, "artifacts", "cdn", PlatformFolder(target));

        public static string PlatformFolder(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                default: return "PC";
            }
        }
    }
}
