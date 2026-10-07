using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using YooAsset;

namespace Automatic.Game
{
    /// <summary>
    /// Frame-rate test for the full board (design/08 §2, §4): started with the -bench command
    /// line switch, loads the board scene from the resource package, runs uncapped for a fixed
    /// time and logs "[Bench]" lines (frame times, GPU time, render counters, memory), then quits.
    /// -nosrpbatch turns the SRP Batcher off, to check that it is actually batching; -nooutline
    /// turns the toon outline pass off; -crowd N adds summons and -fx K effects (CrowdBench);
    /// -shot dir saves one screenshot after the warmup (raw RGB24 like ShopDemo -autoplay).
    /// For the heat soak on phones: -duration S measures for S seconds instead of 10, -fps N caps
    /// the frame rate (uncapped by default; phones still stop at the display refresh), and every
    /// 30 s a "[Bench] t=" line logs that window's frame times with the battery temperature and
    /// thermal status (Android).
    /// </summary>
    public sealed class BoardBench : MonoBehaviour
    {
        private const string Scene = "board_west";
        private const float Warmup = 3, Window = 30;

        public static bool Requested() => LaunchArgs.Has("-bench");

        private static readonly string[] RenderCounters =
        {
            "SetPass Calls Count", "Draw Calls Count", "Batches Count", "Triangles Count", "Shadow Casters Count",
            "Visible Skinned Meshes Count",
        };

        private static readonly string[] MemoryCounters =
        {
            "System Used Memory", "Total Used Memory", "Total Reserved Memory", "GC Used Memory",
            "Gfx Used Memory", "Texture Memory", "Mesh Memory",
        };

        public static void Run(ResourcePackage package)
        {
            var go = new GameObject("BoardBench");
            DontDestroyOnLoad(go);
            go.AddComponent<BoardBench>().StartCoroutine(go.GetComponent<BoardBench>().Measure(package));
        }

        private IEnumerator Measure(ResourcePackage package)
        {
            if (LaunchArgs.Has("-nosrpbatch"))
                UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            QualitySettings.vSyncCount = 0;
            // -1 is "platform default", which is 30 on phones.
            Application.targetFrameRate = int.TryParse(LaunchArgs.After("-fps"), out var cap) ? cap : 1000;
            var duration = float.TryParse(LaunchArgs.After("-duration"), out var d) ? d : 10;
            var load = package.LoadSceneAsync(Scene);
            yield return load;
            Debug.Log($"[Bench] scene {Scene} {load.Status}, screen {Screen.width}x{Screen.height}, {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}, " +
                      $"SRP Batcher {UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching}, development {Debug.isDebugBuild}");

            var crowd = CrowdBench.Requested() > 0 ? CrowdBench.Create(package, CrowdBench.Requested()) : null;
            if (crowd != null) Debug.Log("[Bench] crowd: " + crowd.Describe());

            if (LaunchArgs.Has("-nooutline"))
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader.name == "Relics/Toon") m.SetShaderPassEnabled("Outline", false);

            yield return new WaitForSecondsRealtime(Warmup);
            var shot = LaunchArgs.After("-shot");
            if (shot != null)
            {
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                System.IO.Directory.CreateDirectory(shot);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(shot, $"bench_{Screen.width}x{Screen.height}.rgb"), tex.GetRawTextureData());
                Destroy(tex);
            }
            crowd?.ResetStats();
            var render = RenderCounters.Select(n => ProfilerRecorder.StartNew(ProfilerCategory.Render, n)).ToArray();
            var renderSamples = RenderCounters.Select(_ => new List<long>()).ToArray();
            var memory = MemoryCounters.Select(n => ProfilerRecorder.StartNew(ProfilerCategory.Memory, n)).ToArray();
            var frames = new List<float>();
            var gpu = new List<double>();
            var main = new List<double>();
            var renderThread = new List<double>();
            var timings = new FrameTiming[1];
            var start = Time.realtimeSinceStartup;
            var end = start + duration;
            var windowFrames = 0;
            var nextWindow = start + Window;
            Debug.Log("[Bench] t=0s " + Heat());
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000);
                if (Time.realtimeSinceStartup >= nextWindow)
                {
                    var w = frames.Skip(windowFrames).ToList();
                    Debug.Log($"[Bench] t={nextWindow - start:F0}s avg {w.Average():F2} ms ({1000 / w.Average():F0} fps), " +
                              $"p99 {w.OrderBy(x => x).ElementAt((int)(w.Count * 0.99f)):F2} ms, gpu {Tail(gpu, w.Count)}, main {Tail(main, w.Count)}, {Heat()}");
                    windowFrames = frames.Count;
                    nextWindow += Window;
                }
                // A single frame's counters jump (some frames report a third of the triangles), so
                // keep every frame and report the median.
                for (var i = 0; i < render.Length; i++) renderSamples[i].Add(render[i].LastValue);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    if (timings[0].gpuFrameTime > 0) gpu.Add(timings[0].gpuFrameTime);
                    if (timings[0].cpuMainThreadFrameTime > 0) main.Add(timings[0].cpuMainThreadFrameTime);
                    if (timings[0].cpuRenderThreadFrameTime > 0) renderThread.Add(timings[0].cpuRenderThreadFrameTime);
                }
            }

            frames.Sort();
            var avg = frames.Average();
            Debug.Log($"[Bench] frames {frames.Count}, avg {avg:F2} ms ({1000 / avg:F0} fps), median {frames[frames.Count / 2]:F2} ms, p99 {frames[(int)(frames.Count * 0.99f)]:F2} ms");
            string Avg(List<double> v) => v.Count > 0 ? $"{v.Average():F2} ms" : "n/a";
            Debug.Log($"[Bench] gpu avg {Avg(gpu)}, cpu main thread avg {Avg(main)}, render thread avg {Avg(renderThread)}");
            if (crowd != null) Debug.Log("[Bench] crowd: " + crowd.Describe() + ", " + crowd.Particles());
            Debug.Log("[Bench] render median (min-max): " + string.Join(", ", RenderCounters.Select((n, i) =>
            {
                var v = renderSamples[i].OrderBy(x => x).ToList();
                return $"{n.Replace(" Count", "")} {v[v.Count / 2]} ({v[0]}-{v[v.Count - 1]})";
            })) + $", cameras {Camera.allCamerasCount}");
            Debug.Log("[Bench] memory MB: " + string.Join(", ", MemoryCounters.Select((n, i) =>
                $"{n} {(memory[i].Valid ? (memory[i].LastValue / 1048576.0).ToString("F1") : "n/a")}")));
            foreach (var r in render.Concat(memory)) r.Dispose();
            Application.Quit();
        }

        private static string Tail(List<double> v, int n) => v.Count > 0 ? $"{v.Skip(Mathf.Max(0, v.Count - n)).Average():F2} ms" : "n/a";

        private static string Heat()
        {
            if (Application.platform != RuntimePlatform.Android) return "heat n/a";
            try
            {
                var (status, headroom) = Jni.Thermal();
                return $"battery {Jni.BatteryCelsius():F1} C, thermal status {status}, headroom {headroom:F2}";
            }
            catch (System.Exception e)
            {
                return "heat error " + e.Message;
            }
        }
    }
}
