using UnityEngine;

namespace Automatic.Game
{
    /// <summary>Presentation only; proves a hot MonoBehaviour can be added at runtime.</summary>
    public sealed class Spin : MonoBehaviour
    {
        private void Update() => transform.Rotate(0f, 45f * Time.deltaTime, 0f);
    }
}
