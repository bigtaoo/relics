using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YooAsset;
using Random = UnityEngine.Random;

namespace Automatic.Game
{
    /// <summary>
    /// Playable version of the UI slice (design/08 §3): the board scene in the preparation phase
    /// with the shop HUD over it. Clicking the glowing card plays the hu show (cards, tiles, seal,
    /// with the hand-formed effect on the board: only formed pieces come alive, 04 §6) and then
    /// the battle start (tray away, camera to the whole table, the opponent arrives), then a short
    /// battle run by Battle.Core (BattleShow: attacks, the hand going off, a skill, deaths). Click or
    /// R afterwards to start over; -autoplay dir runs it by itself and saves screenshots. -seed N
    /// picks the battle (default BattleLineup.DemoSeed); -seek T jumps the playing battle to tick T
    /// at -seekat S seconds into it (default 3; 0 joins right at the start); J jumps 5 s ahead.
    /// Everything is loaded from the resource package; the scene and the prefab carry no game scripts.
    /// The timeline runs on unscaled time: the battle's hit stop must not move it.
    /// </summary>
    public sealed class ShopDemo : MonoBehaviour
    {
        private const string Scene = "board_west", ShopPrefab = "ui_shop";
        private const string HuPiece = "fx_hu_piece", HuLink = "fx_hu_link", HuSeal = "fx_hu_seal";
        private const int HuCard = 2;
        /// <summary>The battle (BattleShow) starts once the battle start has settled.</summary>
        private const float BattleAt = HuShowUi.Finish + 0.2f;

        private static readonly int seekTick = int.TryParse(LaunchArgs.After("-seek"), out var tick) ? tick : -1;
        private static readonly float seekAt = float.TryParse(LaunchArgs.After("-seekat"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at) ? at : 3;
        private static readonly uint seed = uint.TryParse(LaunchArgs.After("-seed"), out var s) ? s : BattleLineup.DemoSeed();

        private sealed class Unit
        {
            public Transform Root;
            public Animator Animator;
            public Renderer[] Renderers;
            public string Tier => Root.name.Substring("Unit ".Length);
            /// <summary>The model object is named after its kind (zheng, kuafu), as are its materials.</summary>
            public string Kind => Animator.name;
            public bool Started;
        }

        private ResourcePackage package;
        private readonly Dictionary<string, GameObject> prefabs = new();
        private bool loading;

        // Per scene load.
        private Camera cam;
        private HuShowUi ui;
        private TextMeshProUGUI hint;
        private List<Unit> allies, enemies, hand;
        private Vector3[] enemyAt;
        private Vector3 newcomerAt;
        private List<Transform> markers;
        private readonly List<(float At, Action Do)> events = new();
        private int fired;
        private float clickedAt = -1;
        private float halfWidth, halfDepth, prepTop;
        private Vector3 prepPos, battlePos, huCentre;
        private HudLayout layout;
        private Rect freeAt;
        private Vector2Int reported;
        private BattleShow battle;
        /// <summary>Battle time minus time since the battle started: moved by seeking.</summary>
        private float shift;
        private bool jumped;
        /// <summary>Where the last jump joined the battle: the actions before it are not played.</summary>
        private float joinedAt;

        public static void Run(ResourcePackage package)
        {
            var go = new GameObject("ShopDemo");
            DontDestroyOnLoad(go);
            var demo = go.AddComponent<ShopDemo>();
            demo.package = package;
            demo.StartCoroutine(demo.Load());
        }

        private IEnumerator Load()
        {
            loading = true;
            var scene = package.LoadSceneAsync(Scene);
            yield return scene;
            foreach (var name in new[] { ShopPrefab, HuPiece, HuLink, HuSeal }.Concat(BattleShow.Prefabs))
                if (!prefabs.ContainsKey(name)) prefabs[name] = package.LoadAssetSync<GameObject>(name).AssetObject as GameObject;
            Setup();
            loading = false;
            Debug.Log($"[Demo] scene {Scene} {scene.Status}, {allies.Count} allies, {enemies.Count} opponents, hand of {hand.Count}");
            if (autoplay != null) StartCoroutine(Autoplay());
        }

        /// <summary>
        /// -autoplay dir: clicks the card by itself, saves screenshots along the timeline as raw
        /// RGB24 (bottom-up, size in the name; the player has no image encoder module), quits.
        /// </summary>
        private static readonly string autoplay = LaunchArgs.After("-autoplay");

        private IEnumerator Autoplay()
        {
            System.IO.Directory.CreateDirectory(autoplay);
            yield return new WaitForSeconds(1);
            var shots = new[] { 0.3f, 1.0f, 1.9f, 2.7f, 4.2f, 5.2f, 7.2f };
            yield return Capture("0.0");
            yield return new WaitForSeconds(0.2f);
            Click();
            var count = shots.Length + 1;
            foreach (var at in shots)
            {
                while (Clock < at) yield return null;
                yield return Capture(at.ToString("F1"));
            }
            // The battle by its own clock: each action's blow, the hand going off, the result; with
            // -seek also the moment after the jump.
            foreach (var h in battle.Highlights)
            {
                if (seekTick >= 0 && !jumped && h >= seekAt)
                {
                    while (!jumped) yield return null;
                    yield return Capture($"seek{seekTick}");
                    count++;
                }
                if (h < BattleTime || h < joinedAt) continue;
                while (BattleTime < h) yield return null;
                yield return Capture($"b{h:F1}");
                count++;
            }
            yield return new WaitForSeconds(0.5f);
            Debug.Log($"[Demo] autoplay done, {count} screenshots in {autoplay}");
            Application.Quit();
        }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            // Size of the pieces on screen: spacing of the player's front row (art/ui/README.md §5).
            var front = allies.Max(u => u.Root.position.z);
            var xs = allies.Where(u => u.Root.position.z > front - 0.01f).Select(u => cam.WorldToScreenPoint(u.Root.position).x).OrderBy(x => x).ToList();
            var cell = Enumerable.Range(1, xs.Count - 1).Min(i => xs[i] - xs[i - 1]);
            var width = layout.ColumnPx.width;
            Debug.Log($"[Demo] {name}: front-row cell {cell:F0} of {width:F0} px" + (layout.ShortMm > 0 ? $" = {cell / width * layout.ShortMm:F1} mm" : ""));
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(autoplay, $"demo_{name}_{Screen.width}x{Screen.height}.rgb"), tex.GetRawTextureData());
            Destroy(tex);
        }

        private void Setup()
        {
            cam = Camera.main;
            var units = GameObject.Find("Units").transform.Cast<Transform>().Select(r => new Unit
            {
                Root = r,
                Animator = r.GetComponentInChildren<Animator>(),
                Renderers = r.GetComponentInChildren<Animator>().GetComponentsInChildren<Renderer>(),
            }).ToList();
            // The scene is saved in the battle phase: the formed hands wear the living material,
            // every other piece the material of its cost tier.
            bool Formed(Unit u) => u.Renderers[0].sharedMaterial.name.EndsWith("_living");
            allies = units.Where(u => u.Root.position.z < 0).ToList();
            enemies = units.Where(u => u.Root.position.z > 0).OrderBy(u => u.Root.position.z).ThenBy(u => u.Root.position.x).ToList();
            enemyAt = enemies.Select(u => u.Root.position).ToArray();
            // The player's hand, linked as a ring; the last one is the piece bought from the shop.
            var formed = allies.Where(Formed).ToList();
            var c = formed.Aggregate(Vector3.zero, (s, u) => s + u.Root.position) / formed.Count;
            hand = formed.OrderByDescending(u => Mathf.Atan2(u.Root.position.z - c.z, u.Root.position.x - c.x)).ToList();
            newcomerAt = hand[^1].Root.position;
            markers = units.SelectMany(u => u.Root.Cast<Transform>()).Where(t => t.name is "BarBack" or "BarFill" or "ActiveRing").ToList();

            (halfWidth, halfDepth) = DemoFraming.Table();
            prepTop = enemyAt.Min(p => p.z) - 0.1f; // the far bank of the river

            // Preparation phase: only the player's side, every piece an artifact of its tier. Pieces
            // always move, artifacts too (only the material tells them apart); each idles out of
            // step with its neighbours so the board never looks frozen.
            foreach (var u in enemies) u.Root.gameObject.SetActive(false);
            foreach (var m in markers) m.gameObject.SetActive(false);
            foreach (var u in allies)
            {
                Wear(u, LoadMaterial(u.Kind + "_" + u.Tier));
                Play(u, "idle", Random.value);
            }

            var canvas = new GameObject("Hud", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            layout = HudLayout.Detect();
            var hud = layout.Setup(canvas.GetComponent<Canvas>(), canvas.GetComponent<CanvasScaler>(), cam);
            var shop = Instantiate(prefabs[ShopPrefab], hud).GetComponent<RectTransform>();
            layout.Apply(shop);
            ui = new HuShowUi(shop, HuCard) { HuScale = 0.65f };
            // The prefab's images take no raycasts (Editor/UiShop.cs), so the card gets a clear hit area.
            var hit = ui.Card.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.canvasRenderer.cullTransparentMesh = true;
            var button = ui.Card.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Click);
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            hint = Instantiate(shop.Find("RoundBar/Round").gameObject, hud).GetComponent<TextMeshProUGUI>();
            var rt = hint.rectTransform;
            // At the bottom, where the shop tray was before the battle.
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 24);
            rt.sizeDelta = new Vector2(600, 50);
            hint.alignment = TextAlignmentOptions.Center;
            hint.color = new Color(0.22f, 0.14f, 0.09f); // ink on the parchment table
            hint.text = "点击任意处重来（R）";
            hint.gameObject.SetActive(false);

            BattleShow.Reset();
            battle = new BattleShow(cam, units.Select(u => u.Root), hand.Select(u => u.Root), name => prefabs[name],
                new BattlePops((RectTransform)hud, cam, shop), shop.Find("RoundBar/Round").GetComponent<TextMeshProUGUI>(), seed);
            events.Clear();
            fired = 0;
            clickedAt = -1;
            shift = 0;
            jumped = false;
            joinedAt = 0;
            freeAt = default;
            Schedule();
        }

        /// <summary>
        /// The board side of the timeline (Editor/FxTimeline.cs HandFormed): each piece of the hand
        /// lights up (pillar) and awakens, turning living inside the pillar's flash; links draw the
        /// hand's shape; the seal lands at the centre. Then the battle starts.
        /// </summary>
        private void Schedule()
        {
            for (var i = 0; i < hand.Count; i++)
            {
                var u = hand[i];
                var at = HuShowUi.Board + HuShowUi.LightAt(i);
                events.Add((at, () =>
                {
                    u.Animator.CrossFade("awaken", 0.12f, 0, 0);
                    Spawn(HuPiece, u.Root.position);
                }));
                events.Add((at + 0.1f, () => Wear(u, LoadMaterial(u.Kind + "_living"))));
                if (i > 0)
                {
                    var a = hand[i - 1].Root.position;
                    events.Add((at + 0.05f, () => Link(a, u.Root.position)));
                }
            }
            events.Add((HuShowUi.Board + HuShowUi.LightAt(hand.Count - 1) + 0.1f, () => Link(hand[^1].Root.position, hand[0].Root.position)));
            var centre = hand.Aggregate(Vector3.zero, (s, u) => s + u.Root.position) / hand.Count;
            events.Add((HuShowUi.Board + HuShowUi.SealAt(hand.Count), () => Spawn(HuSeal, centre)));
            events.Add((HuShowUi.Bars, () =>
            {
                foreach (var m in markers) m.gameObject.SetActive(true);
            }));
            events.Add((BattleAt, () => battle.Begin()));
            events.Sort((x, y) => x.At.CompareTo(y.At));
        }

        private void Click()
        {
            if (clickedAt < 0) clickedAt = Time.unscaledTime;
        }

        /// <summary>Seconds since the click (plus the press), unscaled.</summary>
        private float Clock => clickedAt < 0 ? 0 : Time.unscaledTime - clickedAt + HuShowUi.Press;

        /// <summary>The battle's clock, negative before it starts.</summary>
        private float BattleTime => clickedAt < 0 ? -BattleAt : Clock - BattleAt + shift;

        /// <summary>Jumps the playing battle to battle time `b` (BattleShow.SeekTo).</summary>
        private void Jump(float b)
        {
            var from = BattleTime;
            joinedAt = battle.SeekTo(b);
            shift += b - from;
            jumped = true;
        }

        private void Update()
        {
            if (loading || ui == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
            if (Input.GetKeyDown(KeyCode.R) || (hint.gameObject.activeSelf && Input.GetMouseButtonDown(0)))
            {
                BattleShow.Reset();
                ui = null;
                StartCoroutine(Load());
                return;
            }

            // The framings follow the HUD's free areas, recomputed whenever they move on screen
            // (window resizes, and once the canvas has laid out).
            layout.Refresh();
            if (layout.Viewport(layout.Free) != freeAt)
            {
                freeAt = layout.Viewport(layout.Free);
                prepPos = DemoFraming.Prep(cam, halfWidth, halfDepth, prepTop, freeAt);
                battlePos = DemoFraming.Battle(cam, layout.Viewport(layout.BattleArea));
                if (reported != new Vector2Int(Screen.width, Screen.height))
                {
                    reported = new Vector2Int(Screen.width, Screen.height);
                    var a = allies.Select(u => u.Root).ToList();
                    var e = enemies.Select(u => u.Root).ToList();
                    Debug.Log(Readability.Report("prep", cam, prepPos, layout, a, e, enemyAt));
                    Debug.Log(Readability.Report("battle", cam, battlePos, layout, a, e, enemyAt));
                }
                var show = new Vector3[4];
                layout.Free.GetWorldCorners(show);
                huCentre = ui.Shop.InverseTransformPoint((show[0] + show[2]) / 2);
                huCentre.z = 0;
            }

            var t = Clock;
            while (fired < events.Count && events[fired].At <= t) events[fired++].Do();
            ui.Pose(t, Time.time, huCentre);

            // The bought piece drops onto the board as the card lands (dropped, not scaled: see the editor slice).
            var newcomer = hand[^1].Root;
            var drop = Mathf.InverseLerp(HuShowUi.FlyEnd - 0.15f, HuShowUi.FlyEnd, t);
            newcomer.gameObject.SetActive(drop > 0);
            newcomer.position = newcomerAt + Vector3.up * 1.5f * (1 - drop) * (1 - drop);

            // The camera keeps its angle and glides from the preparation framing to the whole table;
            // the opponent's pieces drop onto their cells row by row.
            cam.transform.position = Vector3.Lerp(prepPos, battlePos, HuShowUi.Smooth(Mathf.InverseLerp(HuShowUi.MoveStart, HuShowUi.MoveEnd, t)));
            if (t >= BattleAt)
            {
                if (seekTick >= 0 && !jumped && BattleTime >= seekAt) Jump((float)seekTick / Battle.Turn.TurnTiming.TicksPerSecond);
                if (Input.GetKeyDown(KeyCode.J)) Jump(BattleTime + 5);
                battle.Tick(BattleTime);
                cam.transform.position += battle.CameraOffset(cam.transform.position);
                if (BattleTime >= battle.End + 1.2f && !hint.gameObject.activeSelf) hint.gameObject.SetActive(true);
            }
            for (var i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                var d = Mathf.InverseLerp(0, 0.25f, t - (HuShowUi.Arrive + 0.025f * i));
                e.Root.gameObject.SetActive(d > 0);
                e.Root.position = enemyAt[i] + Vector3.up * 1.5f * (1 - d) * (1 - d);
                if (d > 0 && !e.Started) Play(e, "idle", Random.value);
            }

            // An awakened piece settles into its idle loop.
            foreach (var u in hand)
            {
                var state = u.Animator.GetCurrentAnimatorStateInfo(0);
                if (u.Started && state.IsName("awaken") && state.normalizedTime >= 1) u.Animator.CrossFade("idle", 0.15f);
            }
        }

        private static void Play(Unit u, string clip, float at)
        {
            u.Started = true;
            u.Animator.speed = Random.Range(0.9f, 1.1f);
            u.Animator.Play(clip, 0, at);
        }

        private Material LoadMaterial(string name) => package.LoadAssetSync<Material>(name).AssetObject as Material;

        private static void Wear(Unit u, Material mat)
        {
            foreach (var r in u.Renderers)
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        }

        private GameObject Spawn(string prefab, Vector3 pos)
        {
            var go = Instantiate(prefabs[prefab], pos, prefabs[prefab].transform.rotation);
            Destroy(go, 5);
            return go;
        }

        private void Link(Vector3 a, Vector3 b)
        {
            var go = Spawn(HuLink, (a + b) / 2);
            var d = new Vector3(b.x - a.x, 0, b.z - a.z);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.right, d);
            go.transform.localScale = new Vector3(d.magnitude, 1, 1);
        }
    }
}
