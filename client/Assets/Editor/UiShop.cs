using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Automatic.Editor
{
    /// <summary>
    /// Preparation-phase HUD for the UI slice (design/08 §3), built from UiAssets and saved as
    /// the hot-update prefab Assets/HotRes/Ui/ui_shop.prefab (UGUI + TextMeshPro only, no game
    /// scripts). Laid out for a 1920x1080 reference canvas that grows on the long side (Expand):
    /// - shop tray at the bottom: gold, level and XP on the left, five cards, refresh and lock on the right;
    /// - hand tracker at the top left: hands in progress as mahjong tiles, with the tenpai line
    ///   (which piece completes it, how many are left in the pool);
    /// - round bar at the top; the hu show (seal + hand name) in the centre, hidden until played.
    /// Content is the slice's example data; the game fills the same hierarchy at runtime.
    /// </summary>
    internal static class UiShop
    {
        public const string PrefabPath = UiAssets.Dir + "ui_shop.prefab";
        public static readonly Vector2 Reference = new(1920, 1080);
        public static readonly Vector2 CardSize = new(224, 256);

        /// <summary>The shop: name, tier index (UiAssets.Tiers), cost, faction, completes the hand.</summary>
        public static readonly (string Name, int Tier, int Cost, string Faction, bool Hu)[] Cards =
        {
            ("天狗", 1, 2, "西", false),
            ("举父", 0, 1, "西", false),
            ("毕方", 2, 3, "西", true),
            ("九尾狐", 3, 4, "南", false),
            ("狰", 0, 1, "西", false),
        };

        /// <summary>Hands in progress: name, fan, pieces (null-marked when missing), status line.</summary>
        public static readonly (string Name, string Fan, (string Piece, bool Have)[] Tiles)[] Hands =
        {
            ("西山五兽", "三番", new[] { ("狰", true), ("天狗", true), ("举父", true), ("鸾鸟", true), ("毕方", false) }),
            ("南山三兽", "一番", new[] { ("九尾狐", true), ("鹿蜀", false), ("类", false) }),
        };

        public const int HuCard = 2, Pool = 3;

        public static GameObject Build(Sprite[] portraits)
        {
            var root = new GameObject("ShopUi", typeof(RectTransform)).GetComponent<RectTransform>();
            Stretch(root);
            TopBar(root);
            HandPanel(root);
            Tray(root, portraits);
            HuShow(root);
            Directory.CreateDirectory(UiAssets.Dir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
            Object.DestroyImmediate(root.gameObject);
            return prefab;
        }

        private static void TopBar(RectTransform root)
        {
            var bar = Img("RoundBar", root, UiAssets.LacquerPanel, Color.white, new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(460, 72));
            Text("Round", bar, "第 6 回合 · 准备", UiAssets.Body, 30, UiAssets.Paper, new Vector2(-34, 0), new Vector2(340, 60));
            var disc = Img("Timer", bar, UiAssets.Disc, UiAssets.Cinnabar, new Vector2(1, 0.5f), new Vector2(-48, 0), new Vector2(54, 54));
            Text("Seconds", disc, "18", UiAssets.Title, 30, UiAssets.Paper, Vector2.zero, new Vector2(54, 54));
        }

        private static void HandPanel(RectTransform root)
        {
            var panel = Img("HandPanel", root, UiAssets.LacquerPanel, Color.white, new Vector2(0, 1), new Vector2(24, -16), new Vector2(430, 468), new Vector2(0, 1));
            Text("Header", panel, "牌型", UiAssets.Body, 26, UiAssets.GoldLine, new Vector2(0, -36), new Vector2(380, 36), TextAlignmentOptions.Left, new Vector2(0.5f, 1));
            var y = -76f;
            for (var h = 0; h < Hands.Length; h++)
            {
                var (name, fan, tiles) = Hands[h];
                var entry = Node("Hand" + h, panel, new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(380, 190), new Vector2(0.5f, 1));
                Text("Name", entry, name, UiAssets.Title, 34, UiAssets.Paper, Vector2.zero, new Vector2(380, 44), TextAlignmentOptions.Left, new Vector2(0.5f, 1));
                Text("Fan", entry, fan, UiAssets.Title, 28, new Color(1, 0.5f, 0.32f), Vector2.zero, new Vector2(380, 44), TextAlignmentOptions.Right, new Vector2(0.5f, 1));
                for (var i = 0; i < tiles.Length; i++)
                {
                    var (piece, have) = tiles[i];
                    var tile = Img("Tile" + i, entry, have ? UiAssets.Tile : UiAssets.Dash, have ? Color.white : new Color(1, 0.9f, 0.7f, 0.7f),
                        new Vector2(0, 1), new Vector2(i * 76 + 2, -50), new Vector2(66, 88), new Vector2(0, 1));
                    var label = Text("Piece", tile, string.Join("\n", piece.ToCharArray()), UiAssets.Body, piece.Length > 2 ? 18 : 22,
                        have ? UiAssets.Ink : new Color(1, 0.9f, 0.7f, 0.55f), new Vector2(0, 4), new Vector2(60, 80));
                    label.lineSpacing = -18;
                    // Lit copy of the tile for the hu show (tiles turn over one by one).
                    var lit = Img("Lit", tile, UiAssets.Glow, new Color(1, 0.8f, 0.4f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 140));
                    lit.transform.SetAsFirstSibling();
                }
                var missing = tiles.Where(t => !t.Have).Select(t => t.Piece).ToArray();
                var status = Node("Status", entry, new Vector2(0, 1), new Vector2(0, -146), new Vector2(380, 40), new Vector2(0, 1));
                if (missing.Length == 1)
                {
                    var seal = Img("Ting", status, UiAssets.Seal, Color.white, new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(40, 40));
                    Text("Char", seal, "听", UiAssets.Title, 28, UiAssets.Paper, Vector2.zero, new Vector2(40, 40));
                    Text("Line", status, $"等 <b>{missing[0]}</b>　池中余 <color=#F2C25A><b>{Pool}</b></color> 张", UiAssets.Body, 26, UiAssets.Paper,
                        new Vector2(70, 0), new Vector2(310, 40), TextAlignmentOptions.Left, new Vector2(0, 0.5f));
                }
                else
                {
                    Text("Line", status, $"差 {missing.Length} 张：{string.Join("、", missing)}", UiAssets.Body, 24, new Color(1, 0.9f, 0.7f, 0.6f),
                        new Vector2(0, 0), new Vector2(380, 40), TextAlignmentOptions.Left, new Vector2(0, 0.5f));
                    entry.gameObject.AddComponent<CanvasGroup>().alpha = 0.7f;
                }
                y -= 196;
            }
        }

        private static void Tray(RectTransform root, Sprite[] portraits)
        {
            var tray = Img("ShopTray", root, UiAssets.LacquerPanel, Color.white, new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(1880, 290), new Vector2(0.5f, 0));

            var left = Node("Economy", tray, new Vector2(0, 0.5f), new Vector2(170, 0), new Vector2(300, 250));
            Img("Coin", left, UiAssets.Coin, Color.white, new Vector2(0.5f, 0.5f), new Vector2(-80, 78), new Vector2(56, 56));
            Text("Gold", left, "37", UiAssets.Title, 54, new Color(1, 0.82f, 0.4f), new Vector2(20, 78), new Vector2(140, 64), TextAlignmentOptions.Left);
            Text("Level", left, "人口 6", UiAssets.Body, 28, UiAssets.Paper, new Vector2(0, 18), new Vector2(240, 40), TextAlignmentOptions.Left);
            Text("Xp", left, "4/8", UiAssets.Body, 22, UiAssets.Paper * 0.85f, new Vector2(0, 18), new Vector2(240, 40), TextAlignmentOptions.Right);
            Img("XpBack", left, null, new Color(0, 0, 0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -14), new Vector2(240, 12));
            Img("XpFill", left, null, UiAssets.GoldLine, new Vector2(0.5f, 0.5f), new Vector2(-60, -14), new Vector2(120, 12));
            ButtonWithCost("LevelUp", left, "升级", 4, new Vector2(0, -78));

            for (var i = 0; i < Cards.Length; i++)
                Card(tray, i, portraits[Cards[i].Tier], new Vector2((i - 2) * 244, 0));

            var right = Node("Actions", tray, new Vector2(1, 0.5f), new Vector2(-170, 0), new Vector2(300, 250));
            ButtonWithCost("Refresh", right, "刷新", 2, new Vector2(0, 30));
            var lockButton = Img("Lock", right, UiAssets.LacquerPanel, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(200, 64));
            Img("Icon", lockButton, UiAssets.Lock, UiAssets.Paper, new Vector2(0.5f, 0.5f), new Vector2(-46, 0), new Vector2(40, 40));
            Text("Label", lockButton, "锁定", UiAssets.Body, 26, UiAssets.Paper, new Vector2(20, 0), new Vector2(100, 48));
        }

        private static void ButtonWithCost(string name, RectTransform parent, string label, int cost, Vector2 pos)
        {
            var button = Img(name, parent, UiAssets.Button, Color.white, new Vector2(0.5f, 0.5f), pos, new Vector2(200, 64));
            Text("Label", button, label, UiAssets.Title, 30, UiAssets.Paper, new Vector2(-24, 0), new Vector2(100, 56), material: UiAssets.TitleShadow);
            Img("Coin", button, UiAssets.Coin, Color.white, new Vector2(0.5f, 0.5f), new Vector2(42, 0), new Vector2(30, 30));
            Text("Cost", button, cost.ToString(), UiAssets.Title, 28, UiAssets.Paper, new Vector2(72, 0), new Vector2(40, 56));
        }

        private static void Card(Image tray, int index, Sprite portrait, Vector2 pos)
        {
            var (name, tier, cost, faction, hu) = Cards[index];
            var (_, tierLabel, frameColor) = UiAssets.Tiers[tier];
            var card = Node("Card" + index, tray, new Vector2(0.5f, 0.5f), pos, CardSize);
            var glow = Img("Glow", card, UiAssets.Halo, new Color(1, 0.35f, 0.15f, hu ? 1 : 0), new Vector2(0.5f, 0.5f), Vector2.zero, CardSize + new Vector2(64, 64));
            glow.raycastTarget = false;
            Img("Frame", card, UiAssets.Frame, frameColor, new Vector2(0.5f, 0.5f), Vector2.zero, CardSize);
            var face = Img("Face", card, UiAssets.PaperPanel, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, CardSize - new Vector2(26, 26));
            Img("Backdrop", face, UiAssets.Glow, new Color(frameColor.r, frameColor.g, frameColor.b, 0.55f), new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(230, 190), new Vector2(0.5f, 0.5f));
            var art = Img("Portrait", face, portrait, Color.white, new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(190, 170), new Vector2(0.5f, 0.5f));
            art.preserveAspect = true;
            Img("NameBar", face, null, new Color(UiAssets.Ink.r, UiAssets.Ink.g, UiAssets.Ink.b, 0.1f), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(190, 46));
            Text("Name", face, name, UiAssets.Title, name.Length > 2 ? 30 : 34, UiAssets.Ink, new Vector2(-22, 30), new Vector2(140, 46), TextAlignmentOptions.Left, new Vector2(0.5f, 0));
            Img("Coin", face, UiAssets.Coin, Color.white, new Vector2(0.5f, 0), new Vector2(54, 30), new Vector2(28, 28));
            Text("Cost", face, cost.ToString(), UiAssets.Title, 32, UiAssets.Ink, new Vector2(80, 30), new Vector2(30, 46), anchor: new Vector2(0.5f, 0));
            var tag = Img("Faction", face, UiAssets.Disc, UiAssets.Ink, new Vector2(0, 1), new Vector2(26, -26), new Vector2(38, 38));
            Text("Char", tag, faction, UiAssets.Title, 24, UiAssets.Paper, Vector2.zero, new Vector2(38, 38));
            Text("Tier", face, tierLabel, UiAssets.Body, 20, UiAssets.Ink * 0.8f, new Vector2(-14, -26), new Vector2(60, 30), TextAlignmentOptions.Right, new Vector2(1, 1));
            if (!hu) return;
            var badge = Img("HuBadge", card, UiAssets.Seal, Color.white, new Vector2(1, 1), new Vector2(-8, -8), new Vector2(76, 76));
            badge.transform.localRotation = Quaternion.Euler(0, 0, -10);
            Text("Char", badge, "胡", UiAssets.Title, 52, UiAssets.Paper, new Vector2(0, 2), new Vector2(76, 76));
        }

        /// <summary>Centre show when the hand forms: a big seal stamped down and the hand name on an ink stroke.</summary>
        private static void HuShow(RectTransform root)
        {
            var show = Node("HuShow", root, new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(700, 520));
            Img("Flash", show, UiAssets.Glow, new Color(1, 0.6f, 0.3f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1100, 1100));
            var banner = Img("Banner", show, UiAssets.Banner, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(720, 130));
            Text("Hand", banner, Hands[0].Name, UiAssets.Title, 66, UiAssets.Paper, new Vector2(-40, 2), new Vector2(420, 110), material: UiAssets.TitleShadow);
            Text("Fan", banner, Hands[0].Fan, UiAssets.Title, 40, new Color(1, 0.78f, 0.35f), new Vector2(225, 0), new Vector2(120, 110), material: UiAssets.TitleShadow);
            var seal = Img("Seal", show, UiAssets.Seal, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(300, 300));
            Text("Char", seal, "胡", UiAssets.Title, 230, UiAssets.Paper, new Vector2(0, 8), new Vector2(300, 300));
            show.gameObject.SetActive(false);
        }

        // ---- Hierarchy helpers -------------------------------------------------------------

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static RectTransform Node(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.gameObject.layer = 5; // UI
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static Image Img(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var image = Node(name, parent, anchor, pos, size, pivot ?? anchor).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Node(string name, Image parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null) =>
            Node(name, parent.transform, anchor, pos, size, pivot);

        private static Image Img(string name, Image parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null) =>
            Img(name, parent.transform, sprite, color, anchor, pos, size, pivot);

        private static TextMeshProUGUI Text(string name, Component parent, string text, TMP_FontAsset font, float size, Color color, Vector2 pos, Vector2 box,
            TextAlignmentOptions align = TextAlignmentOptions.Center, Vector2? anchor = null, Material material = null)
        {
            var a = anchor ?? new Vector2(0.5f, 0.5f);
            var tmp = Node(name, parent.transform, a, pos, box, a).gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            if (material != null) tmp.fontSharedMaterial = material;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
