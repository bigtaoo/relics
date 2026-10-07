using UnityEngine;
using YooAsset;

namespace Automatic.Game
{
    /// <summary>
    /// Hot layer entry point, called by the AOT shell via reflection (design/07 §2).
    /// Default: the playable shop demo (08 §3); -bench: board frame-rate test; -hotcheck: the
    /// hot-update check (cube and golden battle, 07 §7).
    /// </summary>
    public static class GameEntry
    {
        public const string PackageName = "DefaultPackage";

        /// <summary>Edit this and publish only a hot update to validate code hot update (design/07 §7 step 3).</summary>
        public const string BuildLabel = "hot code v1";

        public static void Start()
        {
            var package = YooAssets.GetPackage(PackageName);
            if (BoardBench.Requested())
            {
                BoardBench.Run(package);
                return;
            }
            if (!LaunchArgs.Has("-hotcheck"))
            {
                ShopDemo.Run(package);
                return;
            }
            var cube = package.LoadAssetSync<GameObject>("HotCube").InstantiateSync();
            cube.AddComponent<Spin>();

            var hud = new GameObject("HotHud").AddComponent<HotHud>();
            hud.Lines = GoldenSelfCheck.Run();
            hud.Lines.Insert(0, BuildLabel);
            // Also logged so a headless or CI run can verify without reading the screen.
            var color = cube.GetComponent<Renderer>().sharedMaterial.color;
            Debug.Log($"[Hot] cube color {ColorUtility.ToHtmlStringRGB(color)}");
            foreach (var line in hud.Lines)
                Debug.Log("[Hot] " + line);
            Object.DontDestroyOnLoad(hud.gameObject);
        }
    }
}
