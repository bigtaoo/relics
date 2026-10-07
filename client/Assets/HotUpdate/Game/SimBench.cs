using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Automatic.Battle.Sim;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Automatic.Game
{
    /// <summary>
    /// Battle logic stress test in the player (design/08 §2): the same CrowdBattle as tools/SimBench,
    /// here inside the HybridCLR interpreter, since Battle.Core is hot code. Started with -simbench;
    /// -seeds N battles per scenario (default 3). Logs "[SimBench]" lines and quits. A whole battle's
    /// time is what catching up costs when switching to another battle at its end (design/01 §4); the
    /// worst tick is what the live battle costs in one frame.
    /// </summary>
    public sealed class SimBench : MonoBehaviour
    {
        // Same values as tests/Battle.Core.Tests/CrowdBattleTests.GoldenHash; keep them in sync.
        private static readonly (string Name, ulong Hash)[] Golden =
        {
            ("duel16", 0x040c957b5c4950dfUL), ("summon300", 0x8004355070fae8ebUL), ("crowd300", 0x660a6f5c9dccd6faUL),
        };

        public static bool Requested() => LaunchArgs.Has("-simbench");

        public static void Run()
        {
            var go = new GameObject("SimBench");
            DontDestroyOnLoad(go);
            var bench = go.AddComponent<SimBench>();
            bench.StartCoroutine(bench.Measure());
        }

        private IEnumerator Measure()
        {
            var seeds = int.TryParse(LaunchArgs.After("-seeds"), out var n) ? n : 3;
            Debug.Log($"[SimBench] {SystemInfo.deviceModel}, {SystemInfo.processorType}, {SystemInfo.processorCount} cores, " +
                      $"{Application.platform}, development {Debug.isDebugBuild}, {seeds} battles per scenario");
            yield return null;
            // The interpreter's first run of a method also transforms its IL.
            new CrowdBattle(SimConfig.Duel16(1000)).Run();
            yield return null;
            foreach (var (name, golden) in Golden)
            {
                var ms = new List<double>();
                double worstTick = 0;
                long ticks = 0, unitTicks = 0;
                var hash = 0UL;
                for (uint s = 1; s <= seeds; s++)
                {
                    var watch = Stopwatch.StartNew();
                    var b = new CrowdBattle(Make(name, s));
                    while (!b.Over)
                    {
                        // Stopwatch.Restart is stripped from the shell; the timestamp is not.
                        var before = Stopwatch.GetTimestamp();
                        b.Step();
                        var t = (Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency;
                        if (t > worstTick) worstTick = t;
                    }
                    ms.Add(watch.Elapsed.TotalMilliseconds);
                    ticks += b.Tick;
                    unitTicks += b.UnitTicks;
                    if (s == 1) hash = b.Hash();
                    // One battle per frame keeps the app responsive (Android flags a frozen app).
                    yield return null;
                }
                ms.Sort();
                Debug.Log($"[SimBench] {name}: {ms[ms.Count / 2]:F0} ms per battle (min {ms[0]:F0}, max {ms[ms.Count - 1]:F0}), " +
                          $"{ticks / seeds} ticks, {unitTicks / ticks} units on average, {ms[ms.Count / 2] * seeds / ticks:F3} ms per tick, " +
                          $"worst tick {worstTick:F2} ms, hash {hash:x16} {(hash == golden ? "PASS" : $"FAIL (expected {golden:x16})")}");
            }
            Debug.Log("[SimBench] done");
            Application.Quit();
        }

        private static SimConfig Make(string name, uint seed) =>
            name == "duel16" ? SimConfig.Duel16(seed) : name == "summon300" ? SimConfig.Summon300(seed) : SimConfig.Crowd300(seed);
    }
}
