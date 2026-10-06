using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HybridCLR;
using UnityEngine;
using YooAsset;

namespace Automatic.Boot
{
    /// <summary>
    /// Loads AOT metadata and hot update assemblies from the resource package, then calls the
    /// hot layer entry point by reflection. The shell never references hot assemblies at compile time.
    /// </summary>
    public static class HotLoader
    {
        public static IEnumerator Run(ResourcePackage package, Action<string> report)
        {
#if !UNITY_EDITOR
            foreach (var name in BootConfig.AotMetadataAssemblies)
            {
                var handle = package.LoadAssetAsync<TextAsset>(name + ".dll");
                yield return handle;
                var bytes = handle.GetAssetObject<TextAsset>().bytes;
                var err = RuntimeApi.LoadMetadataForAOTAssembly(bytes, HomologousImageMode.SuperSet);
                handle.Release();
                if (err != LoadImageErrorCode.OK)
                    throw new InvalidOperationException($"AOT metadata {name}: {err}");
            }

            foreach (var name in BootConfig.HotUpdateAssemblies)
            {
                report($"Loading {name}");
                var handle = package.LoadAssetAsync<TextAsset>(name + ".dll");
                yield return handle;
                Assembly.Load(handle.GetAssetObject<TextAsset>().bytes);
                handle.Release();
            }
#else
            // In the editor the hot assemblies are already compiled and loaded by Unity.
            yield return null;
#endif
            var game = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == BootConfig.HotUpdateAssemblies.Last());
            var entry = game.GetType(BootConfig.EntryType, throwOnError: true);
            entry.GetMethod(BootConfig.EntryMethod, BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, null);
        }
    }
}
