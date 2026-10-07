using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace Automatic.Boot
{
    /// <summary>
    /// Startup resource update (design/07 §3): init package → request version → load manifest →
    /// download the difference. Hot DLLs travel in the same package, so code and art share one version.
    /// <see cref="Initialize"/> runs once; <see cref="Update"/> can be rerun after a network failure,
    /// files that finished downloading stay in the cache.
    /// </summary>
    public sealed class ResourceUpdater
    {
        /// <summary>A download with no new bytes for this long is aborted and retried (seconds).</summary>
        private const int WatchdogSeconds = 15;
        /// <summary>Files at least this large resume an interrupted download (needs Range support on the CDN).</summary>
        private const long ResumeMinimumBytes = 1 << 20;
        /// <summary>Timeout for the small version and manifest requests (seconds).</summary>
        private const int RequestTimeout = 15;

        public ResourcePackage Package { get; private set; }
        public string Error { get; private set; }
        public string Version { get; private set; }
        public float Progress { get; private set; }

        private readonly EPlayMode _mode;
        private readonly Action<string> _report;

        public ResourceUpdater(EPlayMode mode, Action<string> report)
        {
            _mode = mode;
            _report = report;
        }

        public IEnumerator Initialize()
        {
            YooAssets.Initialize();
            Package = YooAssets.CreatePackage(BootConfig.PackageName);

            _report($"Initializing ({_mode})");
            var init = Package.InitializePackageAsync(CreateOptions());
            yield return init;
            Fail(init);
        }

        public IEnumerator Update()
        {
            Error = null;
            Progress = 0f;
            _report("Requesting version");
            var version = Package.RequestPackageVersionAsync(new RequestPackageVersionOptions(true, RequestTimeout));
            yield return version;
            if (Fail(version)) yield break;
            Version = version.PackageVersion;

            _report($"Loading manifest {Version}");
            var manifest = Package.LoadPackageManifestAsync(new LoadPackageManifestOptions(Version, RequestTimeout));
            yield return manifest;
            if (Fail(manifest)) yield break;

            var downloader = Package.CreateResourceDownloader(new ResourceDownloaderOptions(10, 3));
            if (downloader.TotalDownloadCount > 0)
            {
                // TODO(design/07 §3): show size and ask first on cellular networks.
                _report($"Downloading {downloader.TotalDownloadCount} files, {downloader.TotalDownloadBytes / 1024} KB");
                downloader.StartDownload();
                while (!downloader.IsDone)
                {
                    Progress = downloader.Progress;
                    yield return null;
                }
                if (Fail(downloader)) yield break;
            }
            Progress = 1f;
        }

        private bool Fail(AsyncOperationBase op)
        {
            if (op.Status == EOperationStatus.Succeeded) return false;
            Error = op.Error;
            return true;
        }

        private InitializePackageOptions CreateOptions()
        {
            switch (_mode)
            {
                case EPlayMode.EditorSimulateMode:
                {
                    var build = EditorSimulateBuildInvoker.Build(BootConfig.PackageName, (int)EBundleType.VirtualAssetBundle);
                    var options = new EditorSimulateModeOptions();
                    options.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory);
                    return options;
                }
                case EPlayMode.OfflinePlayMode:
                {
                    var options = new OfflinePlayModeOptions();
                    options.BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                    return options;
                }
                default:
                {
                    var cdn = $"{BootConfig.CdnRoot}/{BootConfig.PlatformFolder}";
                    var options = new HostPlayModeOptions();
                    options.BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                    options.BuiltinFileSystemParameters.AddParameter(EFileSystemParameter.CopyBuiltinPackageManifest, true);
                    // YooAsset's default cache on Windows sits next to the exe (Relics_Data/yoo), which is
                    // read-only under Program Files. The per-user data folder works on every platform.
                    var cacheRoot = $"{Application.persistentDataPath}/yoo/{BootConfig.PackageName}";
                    var cache = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(new RemoteService(cdn), cacheRoot);
                    // Defaults hang forever on a CDN that stops sending, restart big files from zero and
                    // only check that cached files exist, so a damaged file is never replaced (design/07 §3).
                    cache.AddParameter(EFileSystemParameter.DownloadWatchdogTimeout, WatchdogSeconds);
                    cache.AddParameter(EFileSystemParameter.DownloadResumeMinimumSize, ResumeMinimumBytes);
                    cache.AddParameter(EFileSystemParameter.FileVerifyLevel, EFileVerifyLevel.High);
                    options.CacheFileSystemParameters = cache;
                    return options;
                }
            }
        }

        private sealed class RemoteService : IRemoteService
        {
            private readonly string _root;
            public RemoteService(string root) => _root = root;
            public IReadOnlyList<string> GetRemoteUrls(string fileName) => new[] { $"{_root}/{fileName}" };
        }
    }
}
