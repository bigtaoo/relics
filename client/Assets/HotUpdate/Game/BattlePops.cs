using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>
    /// Battle text on the HUD (design/04 §1.4): damage numbers that pop over the piece they hit
    /// and float up, skill names over the caster, and banners across the middle. Numbers follow
    /// their piece on screen (the camera moves during the battle). The font and the outline come
    /// from the shop prefab, so there is nothing extra to load.
    /// </summary>
    public sealed class BattlePops
    {
        /// <summary>Weapon damage (normal attacks), skill damage (fire), and the crit tint.</summary>
        public static readonly Color Weapon = new(1, 0.98f, 0.93f), Skill = new(1, 0.55f, 0.2f), Crit = new(1, 0.8f, 0.25f);

        private sealed class Pop
        {
            public TextMeshProUGUI Text;
            public RectTransform Rect;
            public System.Func<Vector3> Anchor; // world point, or null for a banner
            public Vector2 Offset;
            public float Born, Life, Grow, Rise;
        }

        private readonly RectTransform hud;
        private readonly Camera cam;
        private readonly TMP_FontAsset title;
        private readonly Material outlined;
        private readonly List<Pop> pops = new();

        public BattlePops(RectTransform hud, Camera cam, RectTransform shop)
        {
            this.hud = hud;
            this.cam = cam;
            // The hand name on the hu banner wears the title shadow (underlay on, so the shell has that
            // shader variant); with no offset and some dilation it reads as a thick ink outline.
            var named = shop.Find("HuShow/Banner/Hand").GetComponent<TextMeshProUGUI>();
            title = named.font;
            outlined = new Material(named.fontSharedMaterial);
            outlined.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.16f, 0.08f, 0.04f, 1));
            outlined.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0);
            outlined.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0);
            outlined.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.8f);
            outlined.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.1f);
        }

        /// <summary>A damage number over `anchor`; crits are bigger, gold, and say so.</summary>
        public void Damage(System.Func<Vector3> anchor, int amount, bool skill, bool crit, float now)
        {
            var text = crit ? $"<size=50%>暴击</size>\n{amount}" : amount.ToString();
            // Crits sit a tier higher, so neighbours hit by the same skill stay readable.
            Add(text, anchor, new Vector2(0, crit ? 85 : 30), crit ? 76 : 58, crit ? Crit : skill ? Skill : Weapon, now, crit ? 1.2f : 0.95f, crit ? 1.6f : 1.5f, 70);
        }

        /// <summary>Skill or hand name over a piece.</summary>
        public void Callout(System.Func<Vector3> anchor, string text, float now) =>
            Add(text, anchor, new Vector2(0, 110), 50, Crit, now, 1.3f, 1.5f, 25);

        /// <summary>A line across the middle of the screen, `y` canvas units above the centre.</summary>
        public void Banner(string text, float size, Color color, float y, float now, float life) =>
            Add(text, null, new Vector2(0, y), size, color, now, life, 1.5f, 0);

        private void Add(string s, System.Func<Vector3> anchor, Vector2 offset, float size, Color color, float now, float life, float grow, float rise)
        {
            var go = new GameObject("Pop", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(hud, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900, 300);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = title;
            text.fontSharedMaterial = outlined;
            text.text = s;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.lineSpacing = -45;
            text.raycastTarget = false;
            pops.Add(new Pop { Text = text, Rect = rect, Anchor = anchor, Offset = offset, Born = now, Life = life, Grow = grow, Rise = rise });
            Place(pops[^1], now);
        }

        public void Update(float now)
        {
            for (var i = pops.Count - 1; i >= 0; i--)
            {
                var p = pops[i];
                if (now - p.Born > p.Life)
                {
                    Object.Destroy(p.Rect.gameObject);
                    pops.RemoveAt(i);
                    continue;
                }
                Place(p, now);
            }
        }

        public void Clear()
        {
            foreach (var p in pops) Object.Destroy(p.Rect.gameObject);
            pops.Clear();
        }

        /// <summary>Pops in big and settles (0.12 s), drifts up, fades over the last quarter.</summary>
        private void Place(Pop p, float now)
        {
            var age = now - p.Born;
            var pos = p.Offset;
            if (p.Anchor != null)
            {
                var screen = cam.WorldToScreenPoint(p.Anchor());
                RectTransformUtility.ScreenPointToLocalPointInRectangle(hud, screen, null, out var local);
                pos += local - hud.rect.center;
            }
            var settle = Mathf.Clamp01(age / 0.12f);
            pos.y += p.Rise * (1 - Mathf.Pow(1 - Mathf.Clamp01(age / p.Life), 2));
            p.Rect.anchoredPosition = pos;
            p.Rect.localScale = Vector3.one * Mathf.Lerp(p.Grow, 1, 1 - (1 - settle) * (1 - settle));
            p.Text.alpha = 1 - Mathf.Clamp01((age - p.Life * 0.75f) / (p.Life * 0.25f));
        }
    }
}
