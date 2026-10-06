using System;
using System.Collections;
using System.Collections.Generic;
using YooAsset;

namespace Automatic.Boot
{
    /// <summary>
    /// Startup resource update (design/07 §3): init package → request version → load manifest →
    /// download the difference. Hot DLLs travel in the same package, so code and art share one version.
    /// </summary>
    public sealed class ResourceUpdater
    {
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

        public IEnumerator Run()
        {
            YooAssets.Initialize();
            Package = YooAssets.CreatePackage(BootConfig.PackageName);

            _report($"Initializing ({_mode})");
            var init = Package.InitializePackageAsync(CreateOptions());
            yield return init;
            if (Fail(init)) yield break;

            _report("Requesting version");
            var version = Package.RequestPackageVersionAsync();
            yield return version;
            if (Fail(version)) yield break;
            Version = version.PackageVersion;

            _report($"Loading manifest {Version}");
            var manifest = Package.LoadPackageManifestAsync(new LoadPackageManifestOptions(Version, 60));
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
                    var cdn = $"{BootConfig.LocalCdnRoot}/{BootConfig.PlatformFolder}";
                    var options = new HostPlayModeOptions();
                    options.BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                    options.BuiltinFileSystemParameters.AddParameter(EFileSystemParameter.CopyBuiltinPackageManifest, true);
                    options.CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(new RemoteService(cdn));
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
