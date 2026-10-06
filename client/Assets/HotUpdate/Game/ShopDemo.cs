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
    /// the battle start (tray away, camera to the whole table, the opponent arrives). Click or R
    /// afterwards to start over; -autoplay dir runs it by itself and saves screenshots. Everything
    /// is loaded from the resource package; the scene and the prefab carry no game scripts.
    /// </summary>
    public sealed class ShopDemo : MonoBehaviour
    {
        private const string Scene = "board_west", ShopPrefab = "ui_shop";
        private const string HuPiece = "fx_hu_piece", HuLink = "fx_hu_link", HuSeal = "fx_hu_seal";
        private const int HuCard = 2;

        private sealed class Unit
        {
            public Transform Root;
            public Animator Animator;
            public Renderer[] Renderers;
            public string Tier => Root.name.Substring("Unit ".Length);
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
        private Material living;
        private readonly List<(float At, Action Do)> events = new();
        private int fired;
        private float clickedAt = -1;
        private float halfWidth, halfDepth, prepTop;
        private Vector2Int screen;
        private Vector3 prepPos, battlePos, huCentre;

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
            foreach (var name in new[] { ShopPrefab, HuPiece, HuLink, HuSeal })
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
        private static readonly string autoplay = ArgAfter("-autoplay");

        private IEnumerator Autoplay()
        {
            System.IO.Directory.CreateDirectory(autoplay);
            yield return new WaitForSeconds(1);
            var shots = new[] { 0.3f, 1.0f, 1.9f, 2.7f, 4.2f, 5.2f, 7.2f };
            yield return Capture("0.0");
            yield return new WaitForSeconds(0.2f);
            Click();
            foreach (var at in shots)
            {
                while (Time.time - clickedAt + HuShowUi.Press < at) yield return null;
                yield return Capture(at.ToString("F1"));
            }
            yield return new WaitForSeconds(0.5f);
            Debug.Log($"[Demo] autoplay done, {shots.Length + 1} screenshots in {autoplay}");
            Application.Quit();
        }

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(autoplay, $"demo_{name}_{Screen.width}x{Screen.height}.rgb"), tex.GetRawTextureData());
            Destroy(tex);
        }

        private static string ArgAfter(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
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
            bool Formed(Unit u) => u.Renderers[0].sharedMaterial.name == "zheng_living";
            living = units.Where(Formed).Select(u => u.Renderers[0].sharedMaterial).First();
            var tiers = units.Where(u => !Formed(u)).GroupBy(u => u.Tier).ToDictionary(g => g.Key, g => g.First().Renderers[0].sharedMaterial);
            allies = units.Where(u => u.Root.position.z < 0).ToList();
            enemies = units.Where(u => u.Root.position.z > 0).OrderBy(u => u.Root.position.z).ThenBy(u => u.Root.position.x).ToList();
            enemyAt = enemies.Select(u => u.Root.position).ToArray();
            // The player's hand, linked as a ring; the last one is the piece bought from the shop.
            var formed = allies.Where(Formed).ToList();
            var c = formed.Aggregate(Vector3.zero, (s, u) => s + u.Root.position) / formed.Count;
            hand = formed.OrderByDescending(u => Mathf.Atan2(u.Root.position.z - c.z, u.Root.position.x - c.x)).ToList();
            newcomerAt = hand[^1].Root.position;
            markers = units.SelectMany(u => u.Root.Cast<Transform>()).Where(t => t.name is "BarBack" or "BarFill" or "ActiveRing").ToList();

            var table = GameObject.Find("TableTop").GetComponent<Renderer>().bounds;
            halfWidth = table.extents.x;
            halfDepth = table.extents.z;
            prepTop = enemyAt.Min(p => p.z) - 0.1f; // the far bank of the river

            // Preparation phase: only the player's side, every piece an artifact of its tier. Pieces
            // always move, artifacts too (only the material tells them apart); each idles out of
            // step with its neighbours so the board never looks frozen.
            foreach (var u in enemies) u.Root.gameObject.SetActive(false);
            foreach (var m in markers) m.gameObject.SetActive(false);
            foreach (var u in allies)
            {
                Wear(u, tiers[u.Tier]);
                Play(u, "idle", Random.value);
            }

            var canvas = new GameObject("Hud", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = DemoFraming.Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var shop = Instantiate(prefabs[ShopPrefab], canvas.transform).GetComponent<RectTransform>();
            ui = new HuShowUi(shop, HuCard);
            var button = ui.Card.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Click);
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            hint = Instantiate(shop.Find("RoundBar/Round").gameObject, canvas.transform).GetComponent<TextMeshProUGUI>();
            var rt = hint.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-32, -28);
            rt.sizeDelta = new Vector2(600, 50);
            hint.alignment = TextAlignmentOptions.Right;
            hint.color = new Color(0.22f, 0.14f, 0.09f); // ink on the parchment table
            hint.text = "点击任意处重来（R）";
            hint.gameObject.SetActive(false);

            events.Clear();
            fired = 0;
            clickedAt = -1;
            screen = Vector2Int.zero;
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
                events.Add((at + 0.1f, () => Wear(u, living)));
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
            events.Add((HuShowUi.Finish + 0.3f, () => hint.gameObject.SetActive(true)));
            events.Sort((x, y) => x.At.CompareTo(y.At));
        }

        private void Click()
        {
            if (clickedAt < 0) clickedAt = Time.time;
        }

        private void Update()
        {
            if (loading || ui == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
            if (Input.GetKeyDown(KeyCode.R) || (hint.gameObject.activeSelf && Input.GetMouseButtonDown(0)))
            {
                ui = null;
                StartCoroutine(Load());
                return;
            }

            if (screen.x != Screen.width || screen.y != Screen.height)
            {
                screen = new Vector2Int(Screen.width, Screen.height);
                battlePos = DemoFraming.Battle(cam, halfWidth, halfDepth);
                (prepPos, huCentre) = DemoFraming.Prep(cam, halfWidth, halfDepth, prepTop, DemoFraming.CanvasSize(Screen.width, Screen.height));
            }

            var t = clickedAt < 0 ? 0 : Time.time - clickedAt + HuShowUi.Press;
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
