using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automatic.Game
{
    /// <summary>
    /// The portrait HUD (ADR-011, art/ui/README.md §5.2), one layout for every platform. The game
    /// is a column no wider than MaxAspect, centred with dark side margins on wider screens (PC,
    /// tablets). The shop prefab is still authored for a 1920x1080 landscape canvas, so this
    /// rearranges it: round bar at the top, hand panel under it on the left, the board in the
    /// middle, the shop tray at the bottom (five cards, then gold, level up, refresh and lock).
    /// Phones scale the UI by the physical screen size, so buttons and text keep a touchable,
    /// readable size; elsewhere the column is DesktopWidth reference units wide.
    /// </summary>
    public sealed class HudLayout
    {
        /// <summary>Physical size of one reference unit on a phone: 26-unit body text at 2.5 mm, 80-unit buttons at 7.7 mm.</summary>
        private const float MmPerUnit = 0.096f;

        /// <summary>Column width in reference units where the physical size is unknown (PC, tablets).</summary>
        private const float DesktopWidth = 680;

        /// <summary>Widest column shown (width / height); wider screens get side margins.</summary>
        public const float MaxAspect = 0.6f;

        /// <summary>Phones to simulate on PC with -device (portrait; safe area as fractions of the screen).</summary>
        private static readonly (string Name, float ShortMm, Rect Safe)[] Devices =
        {
            // iPhone 13: 1170x2532, 460 ppi, 844 pt tall; notch 47 pt at the top, home indicator 34 pt.
            ("iphone13", 64.6f, Rect.MinMaxRect(0, 34f / 844, 1, 1 - 47f / 844)),
            // Galaxy A16: 1080x2340, ~385 ppi; Unity does not draw into the cutout by default.
            ("a16", 71.3f, new Rect(0, 0, 1, 1)),
        };

        private const float Gap = 8, Column = 351, BarScale = 0.8f, BarHeight = 72 * BarScale, Row = 80;

        /// <summary>Screen width in mm on a phone (simulated or measured); 0 elsewhere.</summary>
        public readonly float ShortMm;

        private readonly Rect safe;
        private Camera cam, margins;
        private CanvasScaler scaler;
        private RectTransform container;
        private Vector2Int screen;
        private float topHeight, width;

        /// <summary>The game's column in screen pixels (the camera's pixel rect).</summary>
        public Rect ColumnPx { get; private set; }

        /// <summary>Where the board goes in the preparation phase (where the hu show lands too) and in the battle.</summary>
        public RectTransform Free, BattleArea;

        private HudLayout(float shortMm, Rect safe)
        {
            ShortMm = shortMm;
            this.safe = safe;
        }

        /// <summary>
        /// -device iphone13|a16 simulates a phone on PC; a mobile build measures its own screen.
        /// Tablets (short side over 110 mm) and PC have no physical scale.
        /// </summary>
        public static HudLayout Detect()
        {
            var device = LaunchArgs.After("-device");
            if (device != null)
            {
                foreach (var d in Devices)
                    if (d.Name == device) return new HudLayout(d.ShortMm, d.Safe);
                Debug.LogWarning($"[Demo] unknown -device {device}");
            }
            var s = Screen.safeArea;
            var safe = Rect.MinMaxRect(s.xMin / Screen.width, s.yMin / Screen.height, s.xMax / Screen.width, s.yMax / Screen.height);
            if (!Application.isMobilePlatform || Screen.dpi <= 0) return new HudLayout(0, safe);
            var shortMm = Mathf.Min(Screen.width, Screen.height) / Screen.dpi * 25.4f;
            return new HudLayout(shortMm > 110 ? 0 : shortMm, safe);
        }

        /// <summary>Sets up the canvas and the camera's column; returns the safe-area container the HUD goes into.</summary>
        public RectTransform Setup(Canvas canvas, CanvasScaler canvasScaler, Camera camera)
        {
            cam = camera;
            scaler = canvasScaler;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            container = new GameObject("Safe", typeof(RectTransform)).GetComponent<RectTransform>();
            container.SetParent(canvas.transform, false);
            screen = Vector2Int.zero;
            Refresh();
            return container;
        }

        /// <summary>Follows the screen size (PC window resizes): column, camera rect, UI scale, container.</summary>
        public void Refresh()
        {
            if (screen.x == Screen.width && screen.y == Screen.height) return;
            screen = new Vector2Int(Screen.width, Screen.height);
            float w = Screen.width, h = Screen.height;
            var cw = Mathf.Min(w, h * MaxAspect);
            ColumnPx = new Rect((w - cw) / 2, 0, cw, h);
            cam.rect = new Rect(ColumnPx.x / w, 0, cw / w, 1);
            // Side margins: the camera only clears its own rect, so another one clears the screen.
            if (cw < w && margins == null)
            {
                margins = new GameObject("Margins").AddComponent<Camera>();
                margins.transform.SetParent(cam.transform.parent, false);
                margins.cullingMask = 0;
                margins.clearFlags = CameraClearFlags.SolidColor;
                margins.backgroundColor = new Color(0.07f, 0.05f, 0.04f);
                margins.depth = cam.depth - 1;
            }
            // Width in reference units stays the same for a device, so the layout is applied once.
            scaler.scaleFactor = ShortMm > 0 ? cw * MmPerUnit / ShortMm : cw / DesktopWidth;
            var px = Rect.MinMaxRect(Mathf.Max(ColumnPx.xMin, safe.xMin * w), safe.yMin * h, Mathf.Min(ColumnPx.xMax, safe.xMax * w), safe.yMax * h);
            container.anchorMin = new Vector2(px.xMin / w, px.yMin / h);
            container.anchorMax = new Vector2(px.xMax / w, px.yMax / h);
            container.offsetMin = container.offsetMax = Vector2.zero;
            width = px.width / scaler.scaleFactor;
            // Debug buttons right of the hand panel, under the round bar.
            QualityGovernor.Corner = new Vector2(px.xMax, h - px.yMax + (2 * Gap + BarHeight) * scaler.scaleFactor);
        }

        public void Apply(RectTransform shop)
        {
            var bar = Find(shop, "RoundBar");
            Place(bar, new Vector2(0.5f, 1), new Vector2(0, -Gap), BarScale);
            topHeight = 2 * Gap + BarHeight + HandPanel(Find(shop, "HandPanel"), -(2 * Gap + BarHeight));
            var trayHeight = Tray(Find(shop, "ShopTray"), width);
            Free = Area(shop, "Free", new Vector2(Gap, trayHeight + Gap), new Vector2(-Gap, -topHeight - Gap));
            BattleArea = Area(shop, "Battle", new Vector2(Gap, Gap), new Vector2(-Gap, -topHeight - Gap));
        }

        /// <summary>
        /// Hand panel at the top left under the round bar: the hand being waited on in full at 0.85,
        /// the others as one line (name, fan, what is missing). Returns its height.
        /// </summary>
        private static float HandPanel(RectTransform panel, float top)
        {
            const float s = 0.85f;
            Find(panel, "Header").gameObject.SetActive(false);
            var y = -14f;
            for (var h = 0; panel.Find("Hand" + h) != null; h++)
            {
                var entry = Find(panel, "Hand" + h);
                var waiting = entry.Find("Status/Ting") != null;
                if (!waiting)
                {
                    foreach (Transform c in entry)
                        if (c.name.StartsWith("Tile")) c.gameObject.SetActive(false);
                    Find(entry, "Status").anchoredPosition = new Vector2(0, -46);
                    Find(entry, "Status/Line").GetComponent<TMP_Text>().fontSize = 26;
                }
                Place(entry, new Vector2(0.5f, 1), new Vector2(0, y), s);
                y -= (waiting ? 190 : 88) * s + 6;
            }
            var height = -y + 8;
            panel.sizeDelta = new Vector2(Column, height);
            Place(panel, new Vector2(0, 1), new Vector2(Gap, top));
            return height;
        }

        /// <summary>
        /// Tray across the bottom: the five cards as wide as the column allows, under them one row
        /// of gold and level, level up, refresh and lock (the thumb's reach). Returns its height.
        /// </summary>
        private static float Tray(RectTransform tray, float width)
        {
            var pitch = (width - 2 * Gap) / 5;
            var cardScale = Mathf.Min(0.62f, (pitch - 6) / 224);
            var cardHeight = 256 * cardScale;
            var height = 3 * Gap + cardHeight + Row;
            tray.anchorMin = Vector2.zero;
            tray.anchorMax = new Vector2(1, 0);
            tray.pivot = new Vector2(0.5f, 0);
            tray.anchoredPosition = Vector2.zero;
            tray.sizeDelta = new Vector2(0, height);
            for (var i = 0; tray.Find("Card" + i) != null; i++)
            {
                var card = Find(tray, "Card" + i);
                card.anchoredPosition = new Vector2((i - 2) * pitch, height / 2 - Gap - cardHeight / 2);
                card.localScale = Vector3.one * cardScale;
            }

            // Bottom row, left to right: economy, then three buttons sharing the rest.
            const float economy = 170;
            var y = -height / 2 + Gap + Row / 2;
            var eco = Find(tray, "Economy");
            eco.sizeDelta = new Vector2(economy, Row);
            Centre(eco, new Vector2(0.5f, 0.5f), new Vector2(-width / 2 + Gap + economy / 2, y));
            Economy(eco);
            var button = (width - 2 * Gap - economy - 3 * Gap) / 3;
            var x = -width / 2 + 2 * Gap + economy + button / 2;
            var buttons = new[] { Find(tray, "Economy/LevelUp") ?? Find(tray, "LevelUp"), Find(tray, "Actions/Refresh") ?? Find(tray, "Refresh"), Find(tray, "Actions/Lock") ?? Find(tray, "Lock") };
            foreach (var b in buttons)
            {
                b.SetParent(tray, false);
                Centre(b, new Vector2(0.5f, 0.5f), new Vector2(x, y));
                b.sizeDelta = new Vector2(button, Row);
                x += button + Gap;
            }
            // Buttons with a cost: label, coin and cost pulled together for the narrower button.
            foreach (var b in new[] { buttons[0], buttons[1] })
            {
                Find(b, "Label").anchoredPosition = new Vector2(-22, 0);
                Find(b, "Coin").anchoredPosition = new Vector2(26, 0);
                Find(b, "Cost").anchoredPosition = new Vector2(50, 0);
            }
            return height;
        }

        /// <summary>Gold on top, level and XP under it, the XP bar along the bottom (170 x 80).</summary>
        private static void Economy(RectTransform eco)
        {
            Find(eco, "Coin").anchoredPosition = new Vector2(-62, 14);
            Find(eco, "Coin").sizeDelta = new Vector2(44, 44);
            var gold = Find(eco, "Gold");
            gold.anchoredPosition = new Vector2(30, 14);
            gold.sizeDelta = new Vector2(110, 56);
            gold.GetComponent<TMP_Text>().fontSize = 46;
            foreach (var name in new[] { "Level", "Xp" })
            {
                var line = Find(eco, name);
                line.anchoredPosition = new Vector2(0, -20);
                line.sizeDelta = new Vector2(160, 30);
                line.GetComponent<TMP_Text>().fontSize = name == "Level" ? 24 : 20;
            }
            Bar(Find(eco, "XpBack"), new Vector2(0, -38), 160);
            Bar(Find(eco, "XpFill"), new Vector2(-40, -38), 80);
        }

        // ---- Helpers ---------------------------------------------------------------------------

        private static RectTransform Find(Transform parent, string path) => (RectTransform)parent.Find(path);

        /// <summary>Anchors and pivots `rt` at `anchor` (a corner or edge), then places and scales it.</summary>
        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, float scale = 1)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.localScale = Vector3.one * scale;
        }

        /// <summary>Anchors `rt` at `anchor` with its pivot in the middle.</summary>
        private static void Centre(RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
        }

        private static void Bar(RectTransform rt, Vector2 pos, float width)
        {
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, 8);
        }

        /// <summary>An invisible area of the shop root given by its offsets from the edges.</summary>
        private static RectTransform Area(RectTransform shop, string name, Vector2 min, Vector2 max)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(shop, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = min;
            rt.offsetMax = max;
            return rt;
        }

        /// <summary>Area of `rt` in the camera's viewport units (the HUD is a screen-space overlay: world units are pixels).</summary>
        public Rect Viewport(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var col = ColumnPx;
            return Rect.MinMaxRect((c[0].x - col.x) / col.width, c[0].y / col.height, (c[2].x - col.x) / col.width, c[2].y / col.height);
        }
    }
}
