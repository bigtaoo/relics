using System.Collections;
using UnityEngine;
using YooAsset;

namespace Automatic.Boot
{
    /// <summary>
    /// The only MonoBehaviour in the shipped boot scene: update resources, load hot code, hand over.
    /// </summary>
    public sealed class Boot : MonoBehaviour
    {
        [Tooltip("Editor only. Players always use HostPlayMode.")]
        [SerializeField] private EPlayMode editorPlayMode = EPlayMode.EditorSimulateMode;

        private string _status = "Starting";
        private ResourceUpdater _updater;

        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            var mode = Application.isEditor ? editorPlayMode : EPlayMode.HostPlayMode;
            _updater = new ResourceUpdater(mode, SetStatus);
            yield return _updater.Run();
            if (_updater.Error != null)
            {
                _status = "Update failed: " + _updater.Error;
                Debug.LogError(_status);
                yield break;
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
            var progress = _updater != null ? _updater.Progress : 0f;
            GUI.Label(new Rect(20, 20, 800, 40), $"{_status}  {progress:P0}");
            GUI.Label(new Rect(20, 50, 800, 40), $"Shell {Application.version}  Resources {_updater?.Version}");
        }
    }
}
