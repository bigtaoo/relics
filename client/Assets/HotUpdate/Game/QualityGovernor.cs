using System;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>
    /// Picks and keeps the quality level and frame rate (design/12): the device tier's defaults,
    /// the player's choice (PlayerPrefs, two corner buttons), launch overrides (-quality, -fps,
    /// -tier; not saved), and one step down the ladder when Android reports heat. Only axes left on
    /// Auto are ever changed by the governor.
    /// </summary>
    public sealed class QualityGovernor : MonoBehaviour
    {
        private const string QualityKey = "quality.level", FpsKey = "quality.fps";
        private const float PollSeconds = 10, StepGapSeconds = 60;

        private Tier device;
        /// <summary>Position on <see cref="QualityTiers.Ladder"/> of the Auto axes.</summary>
        private int step;
        /// <summary>Player choices: quality -1 Auto, else (int)Tier; fps 0 Auto, else 30 or 60.</summary>
        private int quality = -1, fps;
        private bool buttons, thermal;
        private float nextPoll, lastStep = float.NegativeInfinity, fakeHeatAt = float.PositiveInfinity;
        private float smoothedDelta = 1 / 30f;

        /// <summary>Top-right corner of the buttons in GUI pixels (origin top left), set by the HUD layout; else the safe area's.</summary>
        public static Vector2? Corner;

        public Tier Quality =>quality >= 0 ? (Tier)quality : QualityTiers.Ladder[step].Quality;

        /// <summary>The frame rate to ask for; 0 on desktop Auto, which follows vsync.</summary>
        public int Fps => fps > 0 ? fps : Application.isMobilePlatform ? QualityTiers.Ladder[step].Fps : 0;

        /// <summary>Starts the governor; `buttons` shows the two corner buttons (the demo).</summary>
        public static QualityGovernor Run(bool buttons)
        {
            var go = new GameObject("QualityGovernor");
            DontDestroyOnLoad(go);
            var g = go.AddComponent<QualityGovernor>();
            g.buttons = buttons;
            g.Init();
            return g;
        }

        private void Init()
        {
            device = QualityTiers.DetectDevice();
            var tier = LaunchArgs.After("-tier");
            if (tier != null) device = QualityTiers.Parse(tier, device);
            step = QualityTiers.Start(device);

            quality = PlayerPrefs.GetInt(QualityKey, -1);
            fps = PlayerPrefs.GetInt(FpsKey, 0);
            var q = LaunchArgs.After("-quality");
            if (q != null) quality = (int)QualityTiers.Parse(q, Quality);
            if (int.TryParse(LaunchArgs.After("-fps"), out var f) && f > 0) fps = f;
            if (float.TryParse(LaunchArgs.After("-fakeheat"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var heat))
                fakeHeatAt = Time.realtimeSinceStartup + heat;
            thermal = Application.platform == RuntimePlatform.Android || fakeHeatAt < float.PositiveInfinity;
            nextPoll = Time.realtimeSinceStartup + PollSeconds;

            Debug.Log($"[Quality] device {device} ({SystemInfo.graphicsDeviceName}, {SystemInfo.systemMemorySize} MB, {SystemInfo.deviceModel})" +
                      (tier != null ? " from -tier" : ""));
            Apply("start");
        }

        private void Apply(string why)
        {
            QualityTiers.Apply(Quality, Fps);
            if (Fps == 0)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
            Debug.Log($"[Quality] {why}: {Quality} quality (render scale {QualityTiers.RenderScale(Quality)}), " +
                      $"{(Fps > 0 ? Fps + " fps" : "vsync")}; quality {(quality < 0 ? "auto" : "manual")}, fps {(fps == 0 ? "auto" : "manual")}");
        }

        private void Update()
        {
            smoothedDelta += (Time.unscaledDeltaTime - smoothedDelta) * 0.05f;
            if (!thermal || Time.realtimeSinceStartup < nextPoll) return;
            nextPoll = Time.realtimeSinceStartup + PollSeconds;
            var (status, headroom) = ReadThermal();
            if (status < 2 && !(headroom >= 0.9f)) return;
            if (Time.realtimeSinceStartup - lastStep < StepGapSeconds) return;
            lastStep = Time.realtimeSinceStartup;
            StepDown($"hot (status {status}, headroom {headroom:0.00})");
        }

        private (int Status, float Headroom) ReadThermal()
        {
            if (Time.realtimeSinceStartup >= fakeHeatAt) return (2, float.NaN);
            if (Application.platform != RuntimePlatform.Android) return (0, float.NaN);
            try
            {
                return Jni.Thermal();
            }
            catch (Exception e)
            {
                // Older than API 30: no thermal API, so the device tier alone decides.
                Debug.LogWarning("[Quality] no thermal status: " + e.Message);
                thermal = false;
                return (0, float.NaN);
            }
        }

        /// <summary>Moves the Auto axes to the next ladder step that changes what is drawn.</summary>
        private void StepDown(string why)
        {
            var (q, f) = (Quality, Fps);
            for (var next = step + 1; next < QualityTiers.Ladder.Length; next++)
            {
                step = next;
                if (Quality != q || Fps != f)
                {
                    Apply(why);
                    return;
                }
            }
            Debug.Log($"[Quality] {why}: already at the lowest step");
        }

        private void OnGUI()
        {
            if (!buttons) return;
            var scale = Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var safe = Screen.safeArea;
            var corner = Corner ?? new Vector2(safe.xMax, Screen.height - safe.yMax);
            const float w = 120, h = 36, gap = 6;
            var x = (corner.x / scale) - w - gap;
            var y = corner.y / scale + gap;
            var qLabel = quality < 0 ? $"Q Auto·{Quality}" : $"Q {Quality}";
            var fLabel = (fps == 0 ? "FPS Auto" : $"FPS {fps}") + $" ({1 / smoothedDelta:0})";
            if (GUI.Button(new Rect(x, y, w, h), qLabel))
            {
                quality = quality >= (int)Tier.High ? -1 : quality + 1;
                PlayerPrefs.SetInt(QualityKey, quality);
                PlayerPrefs.Save();
                Apply("player");
            }
            if (GUI.Button(new Rect(x, y + h + gap, w, h), fLabel))
            {
                fps = fps == 0 ? 30 : fps == 30 ? 60 : 0;
                PlayerPrefs.SetInt(FpsKey, fps);
                PlayerPrefs.Save();
                Apply("player");
            }
        }
    }
}
