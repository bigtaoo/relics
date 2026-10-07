using System;
using System.Linq;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>
    /// Launch switches (-bench, -crowd N, ...). On desktop they are the process command line. On
    /// Android they come from the intent extra Unity also reads its own player arguments from:
    /// `adb shell am start -n com.bigtaoo.Relics/com.unity3d.player.UnityPlayerGameActivity -e unity "-bench -crowd 254"`.
    /// Read through raw JNI calls, so the hot code needs no AOT generic instantiations.
    /// </summary>
    public static class LaunchArgs
    {
        private static string[] all;

        private static string[] All => all ??= Environment.GetCommandLineArgs().Concat(AndroidExtra()).ToArray();

        public static bool Has(string name) => All.Contains(name);

        /// <summary>The value after `name`, or null.</summary>
        public static string After(string name)
        {
            var i = Array.IndexOf(All, name);
            return i >= 0 && i + 1 < All.Length ? All[i + 1] : null;
        }

        private static string[] AndroidExtra()
        {
            if (Application.platform != RuntimePlatform.Android) return Array.Empty<string>();
            try
            {
                var extra = Jni.IntentString("unity");
                return string.IsNullOrEmpty(extra) ? Array.Empty<string>() : extra.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Args] cannot read the launch intent: " + e.Message);
                return Array.Empty<string>();
            }
        }
    }
}
