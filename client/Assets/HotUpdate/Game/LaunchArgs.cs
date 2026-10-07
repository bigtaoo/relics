using System;
using System.Linq;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>
    /// Launch switches (-bench, -crowd N, ...). On desktop they are the process command line. On
    /// Android they come from the intent extra Unity also reads its own player arguments from:
    /// `adb shell am start -n com.gamestao.relics/com.unity3d.player.UnityPlayerGameActivity -e unity "-bench -crowd 254"`.
    /// Read through raw JNI calls, so the hot code needs no AOT generic instantiations.
    /// On iOS (no command line, no adb) they come from the link that opened the app:
    /// relics://run?cdn=http://192.168.1.20:8000&amp;name=vat254&amp;args=-bench+-crowd+254+-vat.
    /// </summary>
    public static class LaunchArgs
    {
        private static string[] command;
        private static string[] all;

        private static string[] Command => command ??= Environment.GetCommandLineArgs().Concat(AndroidExtra()).ToArray();
        private static string[] All => all ??= Command.Concat(Split(Url("args"))).ToArray();

        public static bool Has(string name) => All.Contains(name);

        /// <summary>The value after `name`, or null.</summary>
        public static string After(string name)
        {
            var i = Array.IndexOf(All, name);
            return i >= 0 && i + 1 < All.Length ? All[i + 1] : null;
        }

        /// <summary>
        /// A query parameter of the link that opened the app, or null (same parser as the shell's
        /// BootConfig.UrlQuery). On desktop `-url &lt;link&gt;` stands in for the link.
        /// </summary>
        public static string Url(string key)
        {
            var url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url))
            {
                var i = Array.IndexOf(Command, "-url");
                url = i >= 0 && i + 1 < Command.Length ? Command[i + 1] : null;
            }
            var q = url?.IndexOf('?') ?? -1;
            if (q < 0) return null;
            foreach (var pair in url.Substring(q + 1).Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0 && pair.Substring(0, eq) == key)
                    return Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
            }
            return null;
        }

        private static string[] Split(string args) =>
            string.IsNullOrEmpty(args) ? Array.Empty<string>() : args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private static string[] AndroidExtra()
        {
            if (Application.platform != RuntimePlatform.Android) return Array.Empty<string>();
            try
            {
                return Split(Jni.IntentString("unity"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Args] cannot read the launch intent: " + e.Message);
                return Array.Empty<string>();
            }
        }
    }
}
