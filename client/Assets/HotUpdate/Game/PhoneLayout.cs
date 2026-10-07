using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automatic.Game
{
    /// <summary>
    /// The shop HUD on phones (design/08 §3, art/ui/README.md §5). The PC layout shrunk onto a phone
    /// leaves buttons at 4 mm and text at 1.6 mm, so phones get a UI scale set by the physical screen
    /// size and their own arrangement of the same prefab, inside the safe area. In landscape the board
    /// is limited by the screen height, so the layouts differ in how much height the shop takes:
    /// A, the PC tray at phone size, always out; B, the shop on demand behind a button; C, a slimmer
    /// tray always out, with the round bar moved off the top. Hand panel and round bar go to a left
    /// column in all three (the board has room to spare on the sides).
    /// </summary>
    public sealed class PhoneLayout
    {
        /// <summary>Physical size of one reference unit on a phone: 26-unit body text at 2.5 mm, 80-unit buttons at 7.7 mm.</summary>
        private const float MmPerUnit = 0.096f;

        /// <summary>Phones to simulate on PC with -device (landscape; safe area as fractions of the screen).</summary>
        private static readonly (string Name, float ShortMm, Rect Safe)[] Devices =
        {
            // iPhone 13: 2532x1170, 460 ppi; notch and rounded corners 47 pt each side, home indicator 21 pt.
            ("iphone13", 64.6f, Rect.MinMaxRect(141f / 2532, 63f / 1170, 1 - 141f / 2532, 1)),
            // Galaxy A16: 2340x1080, ~385 ppi; Unity does not draw into the cutout by default.
            ("a16", 71.3f, new Rect(0, 0, 1, 1)),
        };

        private const float Column = 351, Gap = 8;

        public readonly char Variant;
        public readonly float Scale, ShortMm;
        public readonly Rect Safe;

        /// <summary>
        /// Where the board goes in the preparation phase, the battle, and where the hu show lands.
        /// Layout B: ShowArea is also where the board goes while the shop is open (above the tray).
        /// </summary>
        public RectTransform Free, BattleArea, ShowArea;

        /// <summary>Layout B: opens the shop tray. Hidden once it is open.</summary>
        public RectTransform ShopButton, Wallet;

        private PhoneLayout(char variant, float shortMm, Rect safe)
        {
            Variant = variant;
            Scale = MmPerUnit * DemoFraming.Reference.y / shortMm;
            ShortMm = shortMm;
            Safe = safe;
        }

        /// <summary>
        /// -device iphone13|a16 simulates a phone on PC; a mobile build measures its own screen.
        /// Tablets and PC (short side over 110 mm) keep the PC layout: returns null. -layout A|B|C.
        /// </summary>
        public static PhoneLayout Detect()
        {
            var variant = char.ToUpperInvariant((ShopDemo.ArgAfter("-layout") ?? "A")[0]);
            var device = ShopDemo.ArgAfter("-device");
            if (device != null)
            {
                foreach (var d in Devices)
                    if (d.Name == device) return new PhoneLayout(variant, d.ShortMm, d.Safe);
                Debug.LogWarning($"[Demo] unknown -device {device}");
                return null;
            }
            if (!Application.isMobilePlatform || Screen.dpi <= 0) return null;
            var shortMm = Mathf.Min(Screen.width, Screen.height) / Screen.dpi * 25.4f;
            if (shortMm > 110) return null;
            var s = Screen.safeArea;
            return new PhoneLayout(variant, shortMm, Rect.MinMaxRect(s.xMin / Screen.width, s.yMin / Screen.height, s.xMax / Screen.width, s.yMax / Screen.height));
        }

        /// <summary>Sets the canvas scale and returns the safe-area container the HUD goes into.</summary>
        public RectTransform Setup(Canvas canvas, CanvasScaler scaler)
        {
            scaler.referenceResolution = DemoFraming.Reference / Scale;
            var safe = new GameObject("Safe", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(canvas.transform, false);
            safe.anchorMin = Safe.min;
            safe.anchorMax = Safe.max;
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            return safe;
        }

        public void Apply(RectTransform shop)
        {
            var panelHeight = HandPanel(Find(shop, "HandPanel"));
            var bar = Find(shop, "RoundBar");
            var tray = Find(shop, "ShopTray");
            var left = Column + 2 * Gap;
            float trayHeight;
            if (Variant == 'C')
            {
                trayHeight = 180;
                Place(bar, new Vector2(0, 1), new Vector2(Gap, -2 * Gap - panelHeight), 0.76f);
                Tray(tray, trayHeight, 0.55f, 134);
                CompactEconomy(Find(tray, "Economy"));
                Actions(Find(tray, "Actions"), -115, 40, new Vector2(200, 76));
                Free = Area(shop, "Free", new Vector2(left, trayHeight + Gap), new Vector2(-Gap, -Gap));
                ShowArea = Free;
            }
            else
            {
                trayHeight = 210;
                Tray(tray, trayHeight, 0.66f, 160);
                Economy(Find(tray, "Economy"));
                Actions(Find(tray, "Actions"), -130, 46, new Vector2(210, 80));
                if (Variant == 'B')
                {
                    Place(bar, new Vector2(0, 1), new Vector2(Gap, -2 * Gap - panelHeight), 0.76f);
                    ShopOnDemand(shop, tray);
                    Free = Area(shop, "Free", new Vector2(left, Gap), new Vector2(-Gap, -Gap));
                    // The hu card is bought with the shop open: the seal lands above the tray.
                    ShowArea = Area(shop, "Show", new Vector2(left, trayHeight + Gap), new Vector2(-Gap, -Gap));
                }
                else
                {
                    Place(bar, new Vector2(0.5f, 1), new Vector2(0, -Gap), 0.85f);
                    Free = Area(shop, "Free", new Vector2(left, trayHeight + Gap), new Vector2(-Gap, -2 * Gap - 72 * 0.85f));
                    ShowArea = Free;
                }
            }
            BattleArea = Area(shop, "Battle", new Vector2(left, Gap), new Vector2(-Gap, Free.offsetMax.y));
        }

        /// <summary>
        /// Hand panel at the top left: the hand being waited on in full at 0.85, the others as one
        /// line (name, fan, what is missing). Returns its height.
        /// </summary>
        private static float HandPanel(RectTransform panel)
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
            Place(panel, new Vector2(0, 1), new Vector2(Gap, -Gap));
            return height;
        }

        private static void Tray(RectTransform tray, float height, float cardScale, float pitch)
        {
            tray.anchorMin = Vector2.zero;
            tray.anchorMax = new Vector2(1, 0);
            tray.pivot = new Vector2(0.5f, 0);
            tray.anchoredPosition = Vector2.zero;
            tray.sizeDelta = new Vector2(0, height);
            for (var i = 0; tray.Find("Card" + i) != null; i++)
            {
                var card = Find(tray, "Card" + i);
                card.anchoredPosition = new Vector2((i - 2) * pitch, 0);
                card.localScale = Vector3.one * cardScale;
            }
        }

        private static void Economy(RectTransform eco)
        {
            Centre(eco, new Vector2(0, 0.5f), new Vector2(140, 0));
            Find(eco, "Coin").anchoredPosition = new Vector2(-70, 70);
            Find(eco, "Gold").anchoredPosition = new Vector2(30, 70);
            Find(eco, "Level").anchoredPosition = Find(eco, "Xp").anchoredPosition = new Vector2(0, 28);
            Find(eco, "Level").sizeDelta = Find(eco, "Xp").sizeDelta = new Vector2(200, 40);
            Bar(Find(eco, "XpBack"), new Vector2(0, -2), 200);
            Bar(Find(eco, "XpFill"), new Vector2(-50, -2), 100);
            Button(Find(eco, "LevelUp"), new Vector2(0, -54), new Vector2(210, 80));
        }

        private static void CompactEconomy(RectTransform eco)
        {
            Centre(eco, new Vector2(0, 0.5f), new Vector2(130, 0));
            Find(eco, "Coin").anchoredPosition = new Vector2(-80, 58);
            Find(eco, "Gold").anchoredPosition = new Vector2(15, 58);
            Find(eco, "Level").anchoredPosition = Find(eco, "Xp").anchoredPosition = new Vector2(0, 12);
            Find(eco, "Level").sizeDelta = Find(eco, "Xp").sizeDelta = new Vector2(200, 40);
            Find(eco, "XpBack").gameObject.SetActive(false);
            Find(eco, "XpFill").gameObject.SetActive(false);
            Button(Find(eco, "LevelUp"), new Vector2(0, -46), new Vector2(200, 76));
        }

        private static void Actions(RectTransform actions, float x, float y, Vector2 button)
        {
            Centre(actions, new Vector2(1, 0.5f), new Vector2(x, 0));
            Button(Find(actions, "Refresh"), new Vector2(0, y), button);
            Button(Find(actions, "Lock"), new Vector2(0, -y), button);
        }

        /// <summary>Layout B: the tray waits below the screen; a shop button and the gold stay in the left column.</summary>
        private void ShopOnDemand(RectTransform shop, RectTransform tray)
        {
            ShopButton = Object.Instantiate(Find(tray, "Actions/Lock"), shop);
            ShopButton.name = "ShopButton";
            Find(ShopButton, "Icon").gameObject.SetActive(false);
            var label = Find(ShopButton, "Label");
            label.anchoredPosition = Vector2.zero;
            label.sizeDelta = new Vector2(200, 60);
            var text = label.GetComponent<TMP_Text>();
            text.text = "商店";
            text.fontSize = 40;
            ShopButton.sizeDelta = new Vector2(Column, 100);
            Place(ShopButton, Vector2.zero, new Vector2(Gap, Gap));
            // The shop holds a card that completes a hand: the button carries the hu seal of that card.
            var badge = Object.Instantiate(Find(tray, "Card2/HuBadge"), ShopButton);
            Place(badge, Vector2.one, new Vector2(-10, -6), 0.8f);
            badge.localRotation = Quaternion.Euler(0, 0, -10);
            ShopButton.GetComponent<Image>().raycastTarget = true;

            Wallet = Object.Instantiate(Find(shop, "RoundBar"), shop);
            Wallet.name = "Wallet";
            Find(Wallet, "Timer").gameObject.SetActive(false);
            var coin = Object.Instantiate(Find(tray, "Economy/Coin"), Wallet);
            Place(coin, new Vector2(0, 0.5f), new Vector2(30, 0));
            var line = Find(Wallet, "Round");
            line.anchoredPosition = new Vector2(30, 0);
            line.GetComponent<TMP_Text>().text = "<color=#FFD166>37</color>　人口 6　<size=24>4/8</size>";
            Place(Wallet, Vector2.zero, new Vector2(Gap, 2 * Gap + 100), 0.76f);
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

        private static void Button(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Bar(RectTransform rt, Vector2 pos, float width)
        {
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
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

        /// <summary>Screen area of `rt` in viewport units (the HUD is a screen-space overlay: world units are pixels).</summary>
        public static Rect Viewport(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x / Screen.width, c[0].y / Screen.height, c[2].x / Screen.width, c[2].y / Screen.height);
        }
    }
}
