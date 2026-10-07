using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Automatic.Game
{
    /// <summary>
    /// The HUD side of the hu show and the battle start on the shop prefab (ui_shop), as a pure
    /// function of time. Runtime port of the editor slice's timeline (Editor/UiSlice.cs Show and
    /// BattleStart); the two must keep the same times so the rendered review media match the demo.
    /// </summary>
    public sealed class HuShowUi
    {
        // Hu show (seconds from the start of the waiting state).
        public const float Press = 0.6f, FlyStart = 0.75f, FlyEnd = 1.15f, Board = 1.2f, Hold = 3.4f, Dock = 3.8f;
        // Battle start (seconds, continuing the hu timeline).
        public const float Fight = 4.6f, MoveStart = 4.75f, MoveEnd = 5.65f, Arrive = 5.2f, Bars = 5.9f, Finish = 6.6f;

        /// <summary>When piece i of the hand lights up, relative to Board (Editor/FxTimeline.cs LightAt).</summary>
        public static float LightAt(int i) => 0.15f + 0.12f * i;

        public static float SealAt(int pieces) => LightAt(pieces - 1) + 0.15f;

        public readonly RectTransform Shop, Card;

        /// <summary>
        /// How far the shop tray is out (0 hidden below the screen, 1 in place). Phones with the
        /// shop on demand (PhoneLayout) start it at 0; the battle start slides it away either way.
        /// </summary>
        public float TrayOpen = 1;

        /// <summary>Size of the hu show in the middle (phones scale it down, PhoneLayout).</summary>
        public float HuScale = 1;

        private readonly RectTransform tray, slot, hu;
        private readonly Transform entry, seal;
        private readonly Transform[] tiles;
        private readonly Image slotImage, flash, banner, glow;
        private readonly TextMeshProUGUI slotText, statusChar, statusLine, round;
        private readonly Sprite tileSprite, dashSprite;
        private readonly Color ink, dashColor, dashTextColor;
        private readonly string waitingChar, waitingLine, prepRound;
        private readonly Vector3 home;
        private readonly float trayY, cardScale;
        private readonly int pieces;

        public HuShowUi(RectTransform shop, int card)
        {
            Shop = shop;
            tray = (RectTransform)shop.Find("ShopTray");
            Card = (RectTransform)tray.Find("Card" + card);
            home = Card.localPosition;
            cardScale = Card.localScale.x;
            glow = Card.Find("Glow").GetComponent<Image>();
            entry = shop.Find("HandPanel/Hand0");
            pieces = 0;
            while (entry.Find("Tile" + pieces) != null) pieces++;
            tiles = new Transform[pieces];
            for (var i = 0; i < pieces; i++) tiles[i] = entry.Find("Tile" + i);
            slot = (RectTransform)tiles[pieces - 1];
            slotImage = slot.GetComponent<Image>();
            slotText = slot.GetComponentInChildren<TextMeshProUGUI>();
            tileSprite = tiles[0].GetComponent<Image>().sprite;
            ink = tiles[0].GetComponentInChildren<TextMeshProUGUI>().color;
            dashSprite = slotImage.sprite;
            dashColor = slotImage.color;
            dashTextColor = slotText.color;

            hu = (RectTransform)shop.Find("HuShow");
            seal = hu.Find("Seal");
            flash = hu.Find("Flash").GetComponent<Image>();
            banner = hu.Find("Banner").GetComponent<Image>();
            banner.type = Image.Type.Filled;
            banner.fillMethod = Image.FillMethod.Horizontal;
            var status = entry.Find("Status");
            statusChar = status.Find("Ting/Char").GetComponent<TextMeshProUGUI>();
            statusLine = status.Find("Line").GetComponent<TextMeshProUGUI>();
            waitingChar = statusChar.text;
            waitingLine = statusLine.text;
            round = shop.Find("RoundBar/Round").GetComponent<TextMeshProUGUI>();
            prepRound = round.text;
            trayY = tray.anchoredPosition.y;
        }

        /// <summary>
        /// Poses the HUD at time t (t below Press: waiting, the completing card breathing with `breath`).
        /// `huCentre` is where the seal lands, in shop coordinates.
        /// </summary>
        public void Pose(float t, float breath, Vector3 huCentre)
        {
            // Waiting: the completing card breathes; pressed, it dips, then flies into the hand's empty tile.
            SetAlpha(glow, t < Press ? 0.65f + 0.3f * Mathf.Sin(breath * 7) : 0.95f);
            var flying = t >= Press;
            if (flying && Card.parent != Shop) Card.SetParent(Shop, true); // above every panel while it flies
            var from = Shop.InverseTransformPoint(tray.TransformPoint(home));
            var target = Shop.InverseTransformPoint(slot.TransformPoint(slot.rect.center));
            var u = Smooth(Mathf.InverseLerp(FlyStart, FlyEnd, t));
            if (flying) Card.localPosition = Vector3.Lerp(from, target, u) + Vector3.up * 160 * Mathf.Sin(u * Mathf.PI);
            var press = t < Press || t >= FlyStart ? 1 : 1 - 0.07f * Mathf.Sin(Mathf.InverseLerp(Press, FlyStart, t) * Mathf.PI);
            // Layouts may scale the cards and the hand panel (PhoneLayout): land at the tile's size on screen.
            Card.localScale = Vector3.one * press * Mathf.Lerp(cardScale, slot.rect.height * InShop(slot) / Card.rect.height, u);
            Card.gameObject.SetActive(t < FlyEnd);

            // The empty tile becomes a real one; then the tiles turn over in step with the pieces on the board.
            var filled = t >= FlyEnd;
            slotImage.sprite = filled ? tileSprite : dashSprite;
            slotImage.color = filled ? Color.white : dashColor;
            slotText.color = filled ? ink : dashTextColor;
            for (var i = 0; i < pieces; i++)
            {
                var since = t - (Board + LightAt(i));
                SetAlpha(tiles[i].Find("Lit").GetComponent<Image>(), since < 0 ? 0 : Mathf.Lerp(1, 0.55f, since / 0.4f));
                tiles[i].localScale = Vector3.one * (since < 0 ? 1 : 1 + 0.18f * Mathf.Exp(-since * 10));
            }

            // Seal stamped down when the board's seal lands, the hand name brushed out under it,
            // then the show shrinks into the hand tracker, which now reads "formed".
            var stamp = t - (Board + SealAt(pieces));
            hu.gameObject.SetActive(stamp >= 0 && t < Dock);
            seal.localScale = Vector3.one * (stamp < 0.12f ? Mathf.Lerp(2.4f, 0.92f, Smooth(stamp / 0.12f)) : Mathf.Lerp(0.92f, 1, Mathf.Clamp01((stamp - 0.12f) / 0.1f)));
            if (!seal.TryGetComponent(out CanvasGroup group)) group = seal.gameObject.AddComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp01(stamp / 0.06f);
            SetAlpha(flash, stamp < 0.12f ? 0 : 0.7f * Mathf.Exp(-(stamp - 0.12f) * 5));
            banner.fillAmount = Smooth(Mathf.InverseLerp(0.2f, 0.45f, stamp));
            foreach (var text in banner.GetComponentsInChildren<TextMeshProUGUI>())
                text.alpha = Mathf.InverseLerp(0.35f, 0.55f, stamp);
            var dock = Smooth(Mathf.InverseLerp(Hold, Dock, t));
            // The seal docks onto the small seal of the status line (40 px, at 40,-20 in the line).
            // (300 px seal, so 0.13 of it; less where the panel is scaled down.)
            var docked = 0.13f * InShop(statusLine.transform.parent);
            var statusAt = Shop.InverseTransformPoint(statusLine.transform.parent.TransformPoint(new Vector3(40, -20, 0))) - new Vector3(0, 80 * docked, 0);
            hu.localPosition = Vector3.Lerp(huCentre, statusAt, dock);
            hu.localScale = Vector3.one * Mathf.Lerp(HuScale, docked, dock);
            var shake = stamp is > 0.1f and < 0.4f ? 7 * Mathf.Exp(-(stamp - 0.1f) * 12) * Mathf.Sin(stamp * 90) : 0;
            Shop.anchoredPosition = new Vector2(shake, shake * 0.6f);

            var formed = t >= Dock;
            statusChar.text = formed ? "胡" : waitingChar;
            statusLine.text = formed ? "已成　战斗中发动" : waitingLine;

            // Battle start: the shop tray slides off and the round bar switches to the battle.
            var slide = Mathf.Max(Smooth(Mathf.InverseLerp(Fight, Fight + 0.35f, t)), 1 - TrayOpen);
            tray.anchoredPosition = new Vector2(tray.anchoredPosition.x, trayY - slide * (tray.rect.height + 40));
            round.text = t < Fight ? prepRound : prepRound.Replace("准备", "战斗");
        }

        public static float Smooth(float x) => x * x * (3 - 2 * x);

        /// <summary>Scale of `t` relative to the shop root.</summary>
        private float InShop(Transform t) => t.lossyScale.x / Shop.lossyScale.x;

        private static void SetAlpha(Graphic g, float a)
        {
            var c = g.color;
            c.a = a;
            g.color = c;
        }
    }
}
