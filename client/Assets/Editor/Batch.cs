using System;
using HybridCLR.Editor.Installer;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace Automatic.Editor
{
    /// <summary>
    /// Headless entry points for `unity run client -- -executeMethod Automatic.Editor.Batch.X`
    /// (local CLI and CI, design/09). Each exits the editor with 0 on success, 1 on failure.
    /// </summary>
    public static class Batch
    {
        public static void InstallHybridClr() => Run(InstallHybridClrOnce);

        public static void Setup() => Run(ProjectSetup.Run);

        public static void BuildPlayer() => Run(() => PlayerBuild.Build(EditorUserBuildSettings.activeBuildTarget));

        /// <summary>CI runners start clean (.github/workflows/release-ios.yml): HybridCLR first, then the player.</summary>
        public static void CiBuildPlayer() => Run(() =>
        {
            InstallHybridClrOnce();
            PlayerBuild.Build(EditorUserBuildSettings.activeBuildTarget);
        });

        private static void InstallHybridClrOnce()
        {
            var c = new InstallerController();
            if (c.HasInstalledHybridCLR() && c.InstalledLibil2cppVersion == c.PackageVersion)
            {
                Debug.Log($"[Batch] HybridCLR {c.PackageVersion} already installed.");
                return;
            }
            c.InstallDefaultHybridCLR();
            if (!c.HasInstalledHybridCLR())
                throw new Exception("HybridCLR install failed, see log above.");
        }

        public static void BuildHotUpdate() => Run(() =>
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            HotUpdateBuild.Build(target, HotUpdateBuild.NextVersion(target), EBundledCopyOption.None);
        });

        public static void ArtPreviewZheng() => Run(() => ArtPreview.Render("zheng"));

        public static void ArtPreviewDangkang() => Run(() => ArtPreview.Render("dangkang"));

        public static void BoardSlice() => Run(Automatic.Editor.BoardSlice.Build);

        public static void FxSlice() => Run(Automatic.Editor.FxSlice.Build);

        public static void UiSlice() => Run(Automatic.Editor.UiSlice.Build);

        public static void CrowdBake() => Run(Automatic.Editor.CrowdBake.Build);

        private static void Run(Action action)
        {
            try
            {
                action();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
