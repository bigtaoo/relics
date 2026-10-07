using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Automatic.Game
{
    /// <summary>
    /// Bench results from a phone whose log the PC cannot read (iOS without a Mac). When the app was
    /// opened from a link with a `cdn` parameter, the "[Bench]" / "[SimBench]" lines and exceptions
    /// are posted to {cdn}/bench/{name} (tools/bench/phone_cdn.py keeps them in artifacts/bench/ios/)
    /// and stay on screen; otherwise the bench quits as on desktop and Android.
    /// </summary>
    public sealed class BenchReport : MonoBehaviour
    {
        private static readonly List<string> lines = new List<string>();
        private static string cdn;

        public static void Listen()
        {
            cdn = LaunchArgs.Url("cdn");
            if (cdn == null) return;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (message.StartsWith("[Bench]") || message.StartsWith("[SimBench]"))
                    lines.Add(message);
                else if (type == LogType.Exception)
                    lines.Add(message + "\n" + stack);
            };
        }

        public static void Finish()
        {
            if (cdn == null)
            {
                Application.Quit();
                return;
            }
            var go = new GameObject("BenchReport");
            DontDestroyOnLoad(go);
            var hud = go.AddComponent<HotHud>();
            hud.Lines.AddRange(lines);
            go.AddComponent<BenchReport>().StartCoroutine(Post(hud));
        }

        private static IEnumerator Post(HotHud hud)
        {
            var name = LaunchArgs.Url("name") ?? "bench";
            var request = new UnityWebRequest($"{cdn}/bench/{name}", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n")),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            yield return request.SendWebRequest();
            hud.Lines.Insert(0, request.result == UnityWebRequest.Result.Success
                ? $"Done: sent to the PC as {name}. Swipe the app away before the next run."
                : $"Done, but sending to {cdn} failed ({request.error}): screenshot this.");
            request.Dispose();
        }
    }
}
