using UnityEngine;

namespace Automatic.Game
{
    public sealed partial class BattleShow
    {
        /// <summary>
        /// The acting ring's band: from just outside the pedestal (0.36) to 0.55, wider than the
        /// scene's 0.4-0.5 (Editor/BoardSlice.cs Ring), which read 1 mm thin on a phone.
        /// </summary>
        public const float RingInner = 0.37f, RingOuter = 0.55f;
        /// <summary>
        /// It breathes up to this much wider, this often a second; 0.55 * 1.15 still stops short of
        /// the neighbour's pedestal (1 - 0.36). art/board/README.md, acting ring.
        /// </summary>
        private const float RingPulse = 0.15f, RingRate = 2;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        /// <summary>Gold on the player's half; cyan on the opponent's, where gold turns orange on the red pedestals and the gold numbers.</summary>
        private static readonly Color Gold = new Color(1.6f, 1.25f, 0.4f), Cyan = new Color(0.35f, 1.45f, 1.9f);

        private readonly MaterialPropertyBlock tint = new();
        private Renderer ringRenderer;
        private float ringLift;
        private Piece acting;

        private void SetUpRing()
        {
            if (ring == null) return;
            ringRenderer = ring.GetComponent<Renderer>();
            ringLift = ring.localPosition.y;
            ring.GetComponent<MeshFilter>().sharedMesh = RingMesh(RingInner, RingOuter);
        }

        private void MoveRing(Piece p)
        {
            if (ring == null) return;
            acting = p;
            ring.gameObject.SetActive(p != null);
            if (p == null) return;
            ring.SetParent(p.Root, false);
            tint.Clear();
            tint.SetColor(BaseColor, p.Root.position.z > 0 ? Cyan : Gold);
            ringRenderer.SetPropertyBlock(tint);
        }

        /// <summary>Every frame: the ring breathes and stays under the unit, also while it pounces.</summary>
        private void UpdateRing()
        {
            // A still ring 1 mm thin is lost on the red half and under the numbers; a moving one is not.
            if (ring == null || acting == null || !ring.gameObject.activeInHierarchy) return;
            var k = 1 + RingPulse * (0.5f - 0.5f * Mathf.Cos(Now * 2 * Mathf.PI * RingRate));
            ring.localScale = new Vector3(k, 1, k);
            var at = acting.Model.position;
            ring.position = new Vector3(at.x, acting.Root.position.y + ringLift, at.z);
        }

        /// <summary>Flat ring on the XZ plane (same as Editor/BoardSlice.cs Ring).</summary>
        private static Mesh RingMesh(float inner, float outer)
        {
            const int n = 48;
            var verts = new Vector3[n * 2];
            var tris = new int[n * 6];
            for (var i = 0; i < n; i++)
            {
                var a = i * Mathf.PI * 2 / n;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts[i * 2] = dir * inner;
                verts[i * 2 + 1] = dir * outer;
                int j = (i + 1) % n, t = i * 6;
                (tris[t], tris[t + 1], tris[t + 2]) = (i * 2, j * 2, i * 2 + 1);
                (tris[t + 3], tris[t + 4], tris[t + 5]) = (i * 2 + 1, j * 2, j * 2 + 1);
            }
            var m = new Mesh { vertices = verts, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
