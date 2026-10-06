using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Automatic.Editor
{
    /// <summary>
    /// UI slice (design/08 §3): the preparation-phase shop with the tenpai hint over the board,
    /// and the hu show where buying the last piece forms the hand, with the UI and the board
    /// effect (FxTimeline.HandFormed) on one timeline. Renders into artifacts/ui/: a still per
    /// target aspect ratio (08 §6) and the hu sequence frames; tools/ui/ui_media.py makes the
    /// review media in art/ui/.
    /// </summary>
    public static class UiSlice
    {
        private const float Fps = 30;
        private static readonly (string Name, int W, int H)[] Shots = { ("phone_19.5x9", 2340, 1080), ("pad_4x3", 1440, 1080), ("pc_16x9", 1920, 1080) };
        private const int SeqW = 1280, SeqH = 720;

        // Hu sequence timeline (seconds).
        private const float Press = 0.6f, FlyStart = 0.75f, FlyEnd = 1.15f, Board = 1.2f, Hold = 3.4f, Dock = 3.8f, End = 4.4f;

        public static void Build()
        {
            AssetDatabase.Refresh();
            UiAssets.Build();
            var portraits = UiCapture.Portraits(UiAssets.Dir);
            var prefab = UiShop.Build(portraits);

            var (units, clips) = FxTimeline.OpenBoard();
            var timeline = new FxTimeline(clips);
            timeline.PrepPhase(units);
            var hand = timeline.HandFormed(units, Board);
            var newcomer = hand[^1].Root; // the piece bought from the shop

            var board = Object.FindFirstObjectByType<Camera>();
            board.cullingMask &= ~(1 << UiCapture.UiLayer);
            var (ui, canvas) = UiRig();
            var shop = Object.Instantiate(prefab, canvas).GetComponent<RectTransform>(); // not a prefab instance: the show reparents the card

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/ui"));
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            timeline.Step(0);
            newcomer.gameObject.SetActive(false);
            foreach (var (name, w, h) in Shots)
            {
                Frame(board, ui, canvas, w, h);
                Show(shop, 0, newcomer);
                UiCapture.Composite(board, ui, w, h, Path.Combine(outDir, name + ".png"));
            }

            var seqDir = Path.Combine(outDir, "hu");
            Directory.CreateDirectory(seqDir);
            Frame(board, ui, canvas, SeqW, SeqH);
            var frames = Mathf.RoundToInt(End * Fps);
            for (var f = 0; f <= frames; f++)
            {
                var t = f / Fps;
                timeline.Step(t);
                Show(shop, t, newcomer);
                UiCapture.Composite(board, ui, SeqW, SeqH, Path.Combine(seqDir, $"frame_{f:D3}.png"));
            }
            var info = $"fps {Fps}, frames {frames + 1}, effect instances {timeline.Effects.Count}, " +
                       $"particle systems {timeline.SystemCount}, peak live particles {timeline.Peak}, {Stats(shop)}";
            File.WriteAllText(Path.Combine(outDir, "info.txt"), info + "\n");
            Debug.Log("[Ui] " + info);
        }

        /// <summary>UI camera (orthographic, UI layer only) looking at a world-space canvas far below the board.</summary>
        private static (Camera, RectTransform) UiRig()
        {
            var canvasGo = new GameObject("UiCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.layer = UiCapture.UiLayer;
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = canvasGo.GetComponent<RectTransform>();
            canvas.position = new Vector3(0, -1000, 0);
            var cam = new GameObject("UiCamera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.cullingMask = 1 << UiCapture.UiLayer;
            cam.transform.position = canvas.position + Vector3.back * 100;
            cam.nearClipPlane = 1;
            cam.farClipPlane = 200;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            return (cam, canvas);
        }

        /// <summary>
        /// Sizes the canvas like a CanvasScaler in Expand mode (at least 1920x1080 reference units),
        /// and frames the player's half and bench in the screen area the HUD leaves free.
        /// </summary>
        private static void Frame(Camera board, Camera ui, RectTransform canvas, int w, int h)
        {
            var scale = Mathf.Min(w / UiShop.Reference.x, h / UiShop.Reference.y);
            var size = new Vector2(w / scale, h / scale);
            canvas.sizeDelta = size;
            ui.orthographicSize = size.y / 2;
            ui.aspect = board.aspect = (float)w / h;

            // Free area: right of the hand panel, above the shop tray, below the round bar.
            var free = Rect.MinMaxRect((24 + 430 + 16) / size.x, (12 + 290 + 8) / size.y, 1 - 16 / size.x, 1 - 96 / size.y);
            var points = new List<Vector3>();
            foreach (var x in new[] { -BoardLayout.HalfWidth, BoardLayout.HalfWidth })
                foreach (var z in new[] { -BoardLayout.HalfDepth, BoardLayout.MidGap / 2 + 0.4f })
                    foreach (var y in new[] { 0f, 0.9f })
                        points.Add(new Vector3(x, y, z));
            FitInto(board, points, free);
            huCentre = new Vector3((free.center.x - 0.5f) * size.x, 90, 0);
        }

        /// <summary>Where the hu show plays: centred over the board area the HUD leaves free.</summary>
        private static Vector3 huCentre;

        /// <summary>Moves the camera (fixed rotation) so the points' screen bounds fill `rect` (viewport units).</summary>
        private static void FitInto(Camera cam, List<Vector3> points, Rect rect)
        {
            var centre = points.Aggregate(Vector3.zero, (s, p) => s + p) / points.Count;
            var t = cam.transform;
            for (var dist = 4f; dist < 60; dist += 0.05f)
            {
                t.position = centre - t.forward * dist;
                for (var k = 0; k < 6; k++)
                {
                    var vp = points.Select(cam.WorldToViewportPoint).ToArray();
                    var mid = new Vector2((vp.Min(p => p.x) + vp.Max(p => p.x)) / 2, (vp.Min(p => p.y) + vp.Max(p => p.y)) / 2);
                    var shift = rect.center - mid;
                    var height = 2 * dist * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);
                    t.position -= t.right * shift.x * height * cam.aspect + t.up * shift.y * height;
                }
                var fit = points.Select(cam.WorldToViewportPoint).ToArray();
                if (fit.All(p => p.x >= rect.xMin && p.x <= rect.xMax && p.y >= rect.yMin && p.y <= rect.yMax)) return;
            }
        }

        // ---- The hu show, as a pure function of time ----------------------------------------

        private static void Show(RectTransform shop, float t, Transform newcomer)
        {
            var tray = shop.Find("ShopTray");
            var card = (RectTransform)(tray.Find("Card" + UiShop.HuCard) ?? shop.Find("Card" + UiShop.HuCard));
            var entry = shop.Find("HandPanel/Hand0");
            var missing = UiShop.Hands[0].Tiles.Length - 1;
            var slot = (RectTransform)entry.Find("Tile" + missing);

            // Waiting: the completing card breathes; pressed, it dips, then flies into the hand's empty tile.
            Alpha(card.Find("Glow"), t < Press ? 0.65f + 0.3f * Mathf.Sin(t * 7) : 0.95f);
            card.SetParent(shop, false); // above every panel while it flies
            var home = shop.InverseTransformPoint(tray.TransformPoint(new Vector3((UiShop.HuCard - 2) * 244, ((RectTransform)tray).rect.center.y, 0)));
            var target = shop.InverseTransformPoint(slot.TransformPoint(slot.rect.center));
            var u = Smooth(Mathf.InverseLerp(FlyStart, FlyEnd, t));
            var arc = Vector3.up * 160 * Mathf.Sin(u * Mathf.PI);
            card.localPosition = Vector3.Lerp(home, target, u) + arc;
            var press = t < Press ? 1 : t < FlyStart ? 1 - 0.07f * Mathf.Sin(Mathf.InverseLerp(Press, FlyStart, t) * Mathf.PI) : 1;
            card.localScale = Vector3.one * press * Mathf.Lerp(1, slot.rect.height / UiShop.CardSize.y, u);
            card.gameObject.SetActive(t < FlyEnd);

            // The bought piece drops onto the board as the card lands.
            var drop = Mathf.InverseLerp(FlyEnd - 0.15f, FlyEnd, t);
            newcomer.gameObject.SetActive(drop > 0);
            newcomer.localScale = Vector3.one * Back(drop);

            // The empty tile becomes a real one; then the tiles turn over in step with the pieces on the board.
            var filled = t >= FlyEnd;
            var slotImage = slot.GetComponent<Image>();
            slotImage.sprite = filled ? UiAssets.Tile : UiAssets.Dash;
            slotImage.color = filled ? Color.white : new Color(1, 0.9f, 0.7f, 0.7f);
            slot.GetComponentInChildren<TextMeshProUGUI>().color = filled ? UiAssets.Ink : new Color(1, 0.9f, 0.7f, 0.55f);
            for (var i = 0; i <= missing; i++)
            {
                var tile = entry.Find("Tile" + i);
                var since = t - (Board + FxTimeline.LightAt(i));
                Alpha(tile.Find("Lit"), since < 0 ? 0 : Mathf.Lerp(1, 0.55f, since / 0.4f));
                tile.localScale = Vector3.one * (since < 0 ? 1 : 1 + 0.18f * Mathf.Exp(-since * 10));
            }

            // Seal stamped down when the board's seal lands, the hand name brushed out under it,
            // then the show shrinks into the hand tracker, which now reads "formed".
            var stamp = t - (Board + FxTimeline.SealAt);
            var hu = shop.Find("HuShow");
            hu.gameObject.SetActive(stamp >= 0 && t < Dock);
            var seal = hu.Find("Seal");
            seal.localScale = Vector3.one * (stamp < 0.12f ? Mathf.Lerp(2.4f, 0.92f, Smooth(stamp / 0.12f)) : Mathf.Lerp(0.92f, 1, Mathf.Clamp01((stamp - 0.12f) / 0.1f)));
            AlphaTree(seal, Mathf.Clamp01(stamp / 0.06f));
            Alpha(hu.Find("Flash"), stamp < 0.12f ? 0 : 0.7f * Mathf.Exp(-(stamp - 0.12f) * 5));
            var banner = hu.Find("Banner").GetComponent<Image>();
            banner.type = Image.Type.Filled;
            banner.fillMethod = Image.FillMethod.Horizontal;
            banner.fillAmount = Smooth(Mathf.InverseLerp(0.2f, 0.45f, stamp));
            foreach (var text in banner.GetComponentsInChildren<TextMeshProUGUI>())
                text.alpha = Mathf.InverseLerp(0.35f, 0.55f, stamp);
            var dock = Smooth(Mathf.InverseLerp(Hold, Dock, t));
            // The seal docks onto the small seal of the status line (40 px, at 40,-20 in the line).
            var statusAt = shop.InverseTransformPoint(entry.Find("Status").TransformPoint(new Vector3(40, -20, 0))) - new Vector3(0, 80 * 0.13f, 0);
            hu.localPosition = Vector3.Lerp(huCentre, statusAt, dock);
            hu.localScale = Vector3.one * Mathf.Lerp(1, 0.13f, dock);
            var shake = stamp is > 0.1f and < 0.4f ? 7 * Mathf.Exp(-(stamp - 0.1f) * 12) * Mathf.Sin(stamp * 90) : 0;
            shop.anchoredPosition = new Vector2(shake, shake * 0.6f);

            var formed = t >= Dock;
            var status = entry.Find("Status");
            status.Find("Ting/Char").GetComponent<TextMeshProUGUI>().text = formed ? "胡" : "听";
            status.Find("Line").GetComponent<TextMeshProUGUI>().text = formed
                ? "已成　战斗中发动"
                : $"等 <b>{UiShop.Hands[0].Tiles[missing].Piece}</b>　池中余 <color=#F2C25A><b>{UiShop.Pool}</b></color> 张";
            Canvas.ForceUpdateCanvases();
        }

        private static float Smooth(float x) => x * x * (3 - 2 * x);

        /// <summary>Ease out with a small overshoot.</summary>
        private static float Back(float x) => x <= 0 ? 0 : 1 + 2.2f * Mathf.Pow(x - 1, 3) + 1.2f * Mathf.Pow(x - 1, 2);

        private static void Alpha(Transform t, float a)
        {
            var g = t.GetComponent<Graphic>();
            var c = g.color;
            c.a = a;
            g.color = c;
        }

        private static void AlphaTree(Transform t, float a)
        {
            if (!t.TryGetComponent(out CanvasGroup group)) group = t.gameObject.AddComponent<CanvasGroup>();
            group.alpha = a;
        }

        private static string Stats(RectTransform shop)
        {
            var graphics = shop.GetComponentsInChildren<Graphic>(true);
            var texts = graphics.OfType<TextMeshProUGUI>().Count();
            var sprites = graphics.OfType<Image>().Select(i => i.sprite).Where(s => s != null).Select(s => s.texture).Distinct().ToArray();
            var bytes = sprites.Sum(Tex => (long)UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(Tex));
            return $"ui graphics {graphics.Length} ({texts} texts), sprite textures {sprites.Length} ({bytes / 1024} KB in editor)";
        }
    }
}
