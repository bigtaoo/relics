using System.Collections.Generic;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>Plain text lines on screen (hot check, bench results). Scaled by dpi so a phone can read them.</summary>
    public sealed class HotHud : MonoBehaviour
    {
        public List<string> Lines = new List<string>();

        private void OnGUI()
        {
            var scale = Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
            var width = Screen.width / scale - 40;
            var heights = new float[Lines.Count];
            var total = 0f;
            for (int i = 0; i < Lines.Count; i++)
                total += heights[i] = style.CalcHeight(new GUIContent(Lines[i]), width) + 4;
            GUI.Box(new Rect(10, 10, width + 20, total + 20), GUIContent.none);
            var y = 20f;
            for (int i = 0; i < Lines.Count; i++)
            {
                GUI.Label(new Rect(20, y, width, heights[i]), Lines[i], style);
                y += heights[i];
            }
        }
    }
}
