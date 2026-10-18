using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Automatic.Game
{
    public enum Tier { Low, Mid, High }

    /// <summary>
    /// Device tiers and quality levels (design/12): which tier a device is, what each quality level
    /// renders, and applying it to a private copy of the URP asset. Rules live in hot code so a
    /// misjudged device is fixed with a hot update.
    /// </summary>
    public static class QualityTiers
    {
        /// <summary>The downgrade ladder, best first (design/12 §4).</summary>
        public static readonly (Tier Quality, int Fps)[] Ladder =
        {
            (Tier.High, 60), (Tier.Mid, 60), (Tier.Mid, 30), (Tier.Low, 30),
        };

        /// <summary>Where a device tier starts on the ladder.</summary>
        public static int Start(Tier device) => device switch { Tier.High => 0, Tier.Mid => 1, _ => 2 };

        public static Tier DetectDevice()
        {
            if (!Application.isMobilePlatform) return Tier.High;
            if (Application.platform == RuntimePlatform.IPhonePlayer) return IosTier(SystemInfo.deviceModel);
            return AndroidTier(SystemInfo.graphicsDeviceName, SystemInfo.systemMemorySize);
        }

        /// <summary>iPhone14,x is the iPhone 13 (A15); iPhone12,x and 13,x are the 11 and 12 (A13, A14).</summary>
        public static Tier IosTier(string model)
        {
            model ??= "";
            var ipad = NumberAfter(model, "iPad");
            if (ipad > 0) return ipad >= 13 ? Tier.High : Tier.Mid;
            var iphone = NumberAfter(model, "iPhone");
            if (iphone < 0) return Tier.Mid;
            return iphone >= 14 ? Tier.High : iphone >= 12 ? Tier.Mid : Tier.Low;
        }

        /// <summary>By GPU, since phones are GPU-bound (design/08 §2); little memory is always low.</summary>
        public static Tier AndroidTier(string gpu, int memoryMb)
        {
            if (memoryMb > 0 && memoryMb < 3500) return Tier.Low;
            gpu ??= "";
            var adreno = NumberAfter(gpu, "Adreno");
            if (adreno > 0)
                return adreno >= 730 ? Tier.High : adreno >= 640 && adreno < 700 || adreno >= 710 ? Tier.Mid : Tier.Low;
            if (gpu.Contains("Immortalis")) return Tier.High;
            var mali = NumberAfter(gpu, "Mali-G");
            if (mali > 0)
                return mali >= 710 ? Tier.High : mali >= 610 || mali == 68 || mali == 77 || mali == 78 ? Tier.Mid : Tier.Low;
            if (gpu.Contains("Mali") || gpu.Contains("PowerVR")) return Tier.Low;
            var xclipse = NumberAfter(gpu, "Xclipse");
            if (xclipse > 0) return xclipse >= 920 ? Tier.High : Tier.Mid;
            return memoryMb >= 7500 ? Tier.Mid : Tier.Low;
        }

        /// <summary>
        /// The number after `prefix` in `s`, skipping a few non-digits ("Adreno (TM) 640"), or -1.
        /// Plain string scanning: Regex may not be in the stripped shell.
        /// </summary>
        private static int NumberAfter(string s, string prefix)
        {
            var i = s.IndexOf(prefix, StringComparison.Ordinal);
            if (i < 0) return -1;
            i += prefix.Length;
            for (var skip = 0; i < s.Length && !char.IsDigit(s[i]) && skip < 8; skip++) i++;
            var n = -1;
            for (; i < s.Length && char.IsDigit(s[i]); i++) n = (n < 0 ? 0 : n * 10) + (s[i] - '0');
            return n;
        }

        public static float RenderScale(Tier quality) => quality switch { Tier.High => 1f, Tier.Mid => 0.85f, _ => 0.7f };

        public static Tier Parse(string name, Tier fallback) => name switch
        {
            "low" => Tier.Low, "mid" => Tier.Mid, "high" => Tier.High, _ => fallback,
        };

        private static UniversalRenderPipelineAsset copy;
        private static float shadowDistance;

        /// <summary>
        /// Applies a quality level and frame rate. Works on a copy of the URP asset, so the editor
        /// never writes runtime changes into the project's asset. fps 0 leaves the frame rate alone.
        /// </summary>
        public static void Apply(Tier quality, int fps)
        {
            if (copy == null)
            {
                if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset original)) return;
                copy = Object.Instantiate(original);
                copy.name = original.name + " (runtime)";
                shadowDistance = original.shadowDistance;
                QualitySettings.renderPipeline = copy;
            }
            copy.renderScale = RenderScale(quality);
            // Distance 0 makes URP skip the shadow pass altogether.
            copy.shadowDistance = quality == Tier.Low ? 0 : shadowDistance;
            copy.mainLightShadowmapResolution = quality == Tier.High ? 2048 : 1024;
            if (fps > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = fps;
            }
        }
    }
}
