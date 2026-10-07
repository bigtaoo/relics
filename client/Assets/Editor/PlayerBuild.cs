using System.IO;
using System.Linq;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Automatic.Editor
{
    /// <summary>
    /// Builds the shell (AOT player) with the current resources bundled into StreamingAssets,
    /// and publishes the same version to the local CDN. Output: artifacts/player/{platform}.
    /// `-release` on the editor command line builds a non-development player into
    /// artifacts/player/{platform}-release, still allowed to use the http CDN, to measure what the
    /// development build costs (the logic bench, design/08 §2).
    /// </summary>
    public static class PlayerBuild
    {
        [MenuItem("Automatic/3. Build Player (active target)", priority = 3)]
        public static void BuildActiveTarget() => Build(EditorUserBuildSettings.activeBuildTarget);

        public static void Build(BuildTarget target)
        {
            // Generates link.xml, bridge functions and the stripped AOT dlls used as metadata.
            PrebuildCommand.GenerateAll();
            var version = HotUpdateBuild.Build(target, HotUpdateBuild.NextVersion(target), YooAsset.Editor.EBundledCopyOption.ClearAndCopyAll);

            var release = System.Environment.GetCommandLineArgs().Contains("-release");
            var dir = Path.Combine(HotUpdateBuild.RepoRoot, "artifacts", "player", HotUpdateBuild.PlatformFolder(target) + (release ? "-release" : ""));
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(dir, PlayerFileName(target)),
                target = target,
                // Development: allows http to the local CDN during validation (ProjectSetup).
                options = release ? BuildOptions.None : BuildOptions.Development,
            };
            var http = PlayerSettings.insecureHttpOption;
            if (release) PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            try
            {
                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Player build {report.summary.result}");
            }
            finally
            {
                PlayerSettings.insecureHttpOption = http;
            }
            Debug.Log($"[Player] {target} shell {PlayerSettings.bundleVersion}, resources {version}, {(release ? "release" : "development")} -> {dir}");
        }

        private static string PlayerFileName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android: return "Relics.apk";
                case BuildTarget.iOS: return "Xcode";
                default: return "Relics.exe";
            }
        }
    }
}
