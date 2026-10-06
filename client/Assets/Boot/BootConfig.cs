using UnityEngine;

namespace Automatic.Boot
{
    /// <summary>
    /// Constants shared by the AOT shell and the editor build scripts.
    /// The hot layer keeps its own copy of PackageName (it must not reference this assembly).
    /// </summary>
    public static class BootConfig
    {
        public const string PackageName = "DefaultPackage";

        /// <summary>AOT assemblies whose metadata the interpreter needs for generics (design/07 §4).</summary>
        public static readonly string[] AotMetadataAssemblies = { "mscorlib", "System", "System.Core" };

        /// <summary>Hot update assemblies, in load order (dependencies first).</summary>
        public static readonly string[] HotUpdateAssemblies = { "Automatic.Battle.Core", "Automatic.Game" };

        public const string EntryType = "Automatic.Game.GameEntry";
        public const string EntryMethod = "Start";

        /// <summary>
        /// Local CDN for the minimal validation (design/07 §7): `python -m http.server 8000`
        /// in artifacts/cdn. Replaced by the version server response later (design/07 §3).
        /// </summary>
        public const string LocalCdnRoot = "http://127.0.0.1:8000";

        public static string PlatformFolder
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.Android: return "Android";
                    case RuntimePlatform.IPhonePlayer: return "iOS";
                    default: return "PC";
                }
            }
        }
    }
}
