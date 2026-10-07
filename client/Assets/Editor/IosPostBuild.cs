#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Automatic.Editor
{
    /// <summary>Info.plist keys Unity does not set (design/09 §4).</summary>
    public static class IosPostBuild
    {
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var file = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(file);
            var root = plist.root;
            // The validation CDN is the dev PC on the LAN (BootConfig.CdnRoot); iOS asks the player first.
            root.SetString("NSLocalNetworkUsageDescription", "Downloads game updates from a test server on your network.");
            // No encryption beyond the system's https: TestFlight then skips the export compliance question.
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(file);
        }
    }
}
#endif
