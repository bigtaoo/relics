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
            BakeCiVersion();
            // Generates link.xml, bridge functions and the stripped AOT dlls used as metadata.
            PrebuildCommand.GenerateAll();
            var version = HotUpdateBuild.Build(target, HotUpdateBuild.NextVersion(target), YooAsset.Editor.EBundledCopyOption.ClearAndCopyAll, newShell: true);

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
                ShellCore.Record(target);
            }
            finally
            {
                PlayerSettings.insecureHttpOption = http;
            }
            Debug.Log($"[Player] {target} shell {PlayerSettings.bundleVersion}, resources {version}, {(release ? "release" : "development")} -> {dir}");
        }

        /// <summary>
        /// CI bakes the shell version and build number into the player (design/09 §3): a shell left
        /// at the project's version would look like a dev build. `-shellVersion X.Y.N -buildNumber N`.
        /// Signing is not set here: the workflow patches the exported Xcode project.
        /// </summary>
        private static void BakeCiVersion()
        {
            var shell = Arg("-shellVersion");
            if (shell != null) PlayerSettings.bundleVersion = shell;
            var build = Arg("-buildNumber");
            if (build != null)
            {
                PlayerSettings.iOS.buildNumber = build;
                PlayerSettings.Android.bundleVersionCode = int.Parse(build);
            }
        }

        private static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
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
