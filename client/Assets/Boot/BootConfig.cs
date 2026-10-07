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

        /// <summary>
        /// AOT assemblies whose metadata the interpreter needs for generics (design/07 §4). Battle.Core
        /// is in the shell (ADR-010): hot code may instantiate its generics, e.g. Prng.Shuffle&lt;T&gt;.
        /// </summary>
        public static readonly string[] AotMetadataAssemblies = { "mscorlib", "System", "System.Core", "Automatic.Battle.Core" };

        /// <summary>
        /// Hot update assemblies, in load order (dependencies first). Not Battle.Core: native it runs
        /// 12 times faster than interpreted (design/10), so it ships with the shell.
        /// </summary>
        public static readonly string[] HotUpdateAssemblies = { "Automatic.Game" };

        public const string EntryType = "Automatic.Game.GameEntry";
        public const string EntryMethod = "Start";

        /// <summary>
        /// Local CDN for the minimal validation (design/07 §7): `python -m http.server 8000`
        /// in artifacts/cdn. Replaced by the version server response later (design/07 §3).
        /// </summary>
        public const string LocalCdnRoot = "http://127.0.0.1:8000";

        /// <summary>
        /// LocalCdnRoot, or the `cdn` parameter of the link that opened the app, kept for later starts.
        /// For phones that cannot reach the PC on 127.0.0.1 (iOS has no adb reverse):
        /// relics://run?cdn=http://192.168.1.20:8000 (tools/bench/phone_cdn.py serves such links).
        /// </summary>
        public static string CdnRoot
        {
            get
            {
                var cdn = UrlQuery(LaunchUrl, "cdn");
                if (!string.IsNullOrEmpty(cdn))
                {
                    PlayerPrefs.SetString("cdn", cdn);
                    PlayerPrefs.Save();
                    return cdn;
                }
                return PlayerPrefs.GetString("cdn", LocalCdnRoot);
            }
        }

        /// <summary>The link that opened the app; on desktop `-url &lt;link&gt;` stands in for it, to try the phone path.</summary>
        public static string LaunchUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(Application.absoluteURL)) return Application.absoluteURL;
                var args = System.Environment.GetCommandLineArgs();
                var i = System.Array.IndexOf(args, "-url");
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
        }

        /// <summary>One query parameter of a URL, unescaped, or null. The hot LaunchArgs keeps its own copy.</summary>
        public static string UrlQuery(string url, string key)
        {
            var q = url?.IndexOf('?') ?? -1;
            if (q < 0) return null;
            foreach (var pair in url.Substring(q + 1).Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0 && pair.Substring(0, eq) == key)
                    return System.Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
            }
            return null;
        }

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
