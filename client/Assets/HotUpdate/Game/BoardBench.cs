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
    /// time and logs "[Bench]" lines (frame times, GPU time, render counters), then quits.
    /// </summary>
    public sealed class BoardBench : MonoBehaviour
    {
        private const string Scene = "board_west";
        private const float Warmup = 3, Duration = 10;

        public static bool Requested() => System.Environment.GetCommandLineArgs().Contains("-bench");

        public static void Run(ResourcePackage package)
        {
            var go = new GameObject("BoardBench");
            DontDestroyOnLoad(go);
            go.AddComponent<BoardBench>().StartCoroutine(go.GetComponent<BoardBench>().Measure(package));
        }

        private IEnumerator Measure(ResourcePackage package)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var load = package.LoadSceneAsync(Scene);
            yield return load;
            Debug.Log($"[Bench] scene {Scene} {load.Status}, screen {Screen.width}x{Screen.height}, {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}");

            yield return new WaitForSecondsRealtime(Warmup);
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var frames = new List<float>();
            var gpu = new List<double>();
            var timings = new FrameTiming[1];
            var end = Time.realtimeSinceStartup + Duration;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                    gpu.Add(timings[0].gpuFrameTime);
            }

            frames.Sort();
            var avg = frames.Average();
            Debug.Log($"[Bench] frames {frames.Count}, avg {avg:F2} ms ({1000 / avg:F0} fps), median {frames[frames.Count / 2]:F2} ms, p99 {frames[(int)(frames.Count * 0.99f)]:F2} ms");
            Debug.Log(gpu.Count > 0 ? $"[Bench] gpu avg {gpu.Average():F2} ms" : "[Bench] gpu time unavailable");
            Debug.Log($"[Bench] batches {batches.LastValue}, setpass {setPass.LastValue}, triangles {tris.LastValue}");
            Application.Quit();
        }
    }
}
