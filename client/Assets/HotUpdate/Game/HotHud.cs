using System.Collections.Generic;
using UnityEngine;

namespace Automatic.Game
{
    public sealed class HotHud : MonoBehaviour
    {
        public List<string> Lines = new List<string>();

        private void OnGUI()
        {
            for (int i = 0; i < Lines.Count; i++)
                GUI.Label(new Rect(20, 20 + i * 24, 900, 24), Lines[i]);
        }
    }
}
