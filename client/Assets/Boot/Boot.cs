using System.Collections;
using UnityEngine;
using YooAsset;

namespace Automatic.Boot
{
    /// <summary>
    /// The only MonoBehaviour in the shipped boot scene: update resources, load hot code, hand over.
    /// A failed update retries by itself with growing waits and offers a retry button meanwhile.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        /// <summary>Waits before automatic retries (seconds); the last one repeats.</summary>
        private static readonly int[] RetryWaits = { 3, 5, 10, 20, 30 };

        [Tooltip("Editor only. Players always use HostPlayMode.")]
        [SerializeField] private EPlayMode editorPlayMode = EPlayMode.EditorSimulateMode;

        private string _status = "Starting";
        private ResourceUpdater _updater;
        private float _retryAt = -1f;
        private bool _retryNow;

        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            var mode = Application.isEditor ? editorPlayMode : EPlayMode.HostPlayMode;
            _updater = new ResourceUpdater(mode, SetStatus);
            yield return _updater.Initialize();
            if (_updater.Error != null)
            {
                // Local (disk) failure: retrying the same thing will not help.
                _status = "Startup failed: " + _updater.Error;
                Debug.LogError(_status);
                yield break;
            }

            for (var attempt = 0; ; attempt++)
            {
                yield return _updater.Update();
                if (_updater.Error == null) break;
                var wait = RetryWaits[Mathf.Min(attempt, RetryWaits.Length - 1)];
                Debug.LogWarning($"[Boot] Update failed (attempt {attempt + 1}), retrying in {wait} s: {_updater.Error}");
                _retryAt = Time.realtimeSinceStartup + wait;
                _retryNow = false;
                while (!_retryNow && Time.realtimeSinceStartup < _retryAt) yield return null;
                _retryAt = -1f;
            }

            _status = "Loading code";
            Debug.Log($"[Boot] Resources {_updater.Version} ready");
            yield return HotLoader.Run(_updater.Package, SetStatus);
            _status = null;
        }

        private void SetStatus(string status)
        {
            _status = status;
            Debug.Log("[Boot] " + status);
        }

        private void OnGUI()
        {
            if (_status == null) return;
            if (_retryAt >= 0f)
            {
                // Placeholder boot UI; the real loading screen replaces it (design/07 §3).
                var left = Mathf.CeilToInt(_retryAt - Time.realtimeSinceStartup);
                GUI.Label(new Rect(20, 20, 1000, 40), $"Network error, retrying in {left} s");
                GUI.Label(new Rect(20, 50, 1000, 40), _updater.Error);
                if (GUI.Button(new Rect(20, 90, 160, 40), "Retry now")) _retryNow = true;
                return;
            }
            var progress = _updater != null ? _updater.Progress : 0f;
            GUI.Label(new Rect(20, 20, 800, 40), $"{_status}  {progress:P0}");
            GUI.Label(new Rect(20, 50, 800, 40), $"Shell {Application.version}  Resources {_updater?.Version}");
        }
    }
}
