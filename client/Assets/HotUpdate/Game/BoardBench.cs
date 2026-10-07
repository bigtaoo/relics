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
    /// turns the toon outline pass off.
    /// </summary>
    public sealed class BoardBench : MonoBehaviour
    {
        private const string Scene = "board_west";
        private const float Warmup = 3, Duration = 10;

        public static bool Requested() => System.Environment.GetCommandLineArgs().Contains("-bench");

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
            if (System.Environment.GetCommandLineArgs().Contains("-nosrpbatch"))
                UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var load = package.LoadSceneAsync(Scene);
            yield return load;
            Debug.Log($"[Bench] scene {Scene} {load.Status}, screen {Screen.width}x{Screen.height}, {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}, " +
                      $"SRP Batcher {UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching}, development {Debug.isDebugBuild}");

            if (System.Environment.GetCommandLineArgs().Contains("-nooutline"))
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader.name == "Relics/Toon") m.SetShaderPassEnabled("Outline", false);

            yield return new WaitForSecondsRealtime(Warmup);
            var render = RenderCounters.Select(n => ProfilerRecorder.StartNew(ProfilerCategory.Render, n)).ToArray();
            var renderSamples = RenderCounters.Select(_ => new List<long>()).ToArray();
            var memory = MemoryCounters.Select(n => ProfilerRecorder.StartNew(ProfilerCategory.Memory, n)).ToArray();
            var frames = new List<float>();
            var gpu = new List<double>();
            var timings = new FrameTiming[1];
            var end = Time.realtimeSinceStartup + Duration;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000);
                // A single frame's counters jump (some frames report a third of the triangles), so
                // keep every frame and report the median.
                for (var i = 0; i < render.Length; i++) renderSamples[i].Add(render[i].LastValue);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                    gpu.Add(timings[0].gpuFrameTime);
            }

            frames.Sort();
            var avg = frames.Average();
            Debug.Log($"[Bench] frames {frames.Count}, avg {avg:F2} ms ({1000 / avg:F0} fps), median {frames[frames.Count / 2]:F2} ms, p99 {frames[(int)(frames.Count * 0.99f)]:F2} ms");
            Debug.Log(gpu.Count > 0 ? $"[Bench] gpu avg {gpu.Average():F2} ms" : "[Bench] gpu time unavailable");
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
    }
}
