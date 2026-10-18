using System;
using System.Collections.Generic;
using System.Linq;
using Automatic.Battle.Turn;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>
    /// The battle presentation slice (design/08 §1): runs the demo battle (BattleLineup) in
    /// Battle.Core and stages its event stream on the board. Every action is staged inside the
    /// length the core gives it (TurnTiming): pieces pounce on their target and back, claws and
    /// flames land at the core's hit tick with a hit stop, a camera shake and a damage number; dying
    /// artifacts shatter; the formed hand lights up again and buffs the board; the skill pushes the
    /// camera in. Time is the battle's own clock (seconds from its start, unscaled: the hit stop
    /// freezes animators and particles but never moves the timeline), driven by ShopDemo.
    /// SeekTo joins the battle at any time, with the state the core fast-forwards to.
    /// </summary>
    public sealed partial class BattleShow
    {
        public const string Charge = "fx_zheng_charge", Bolt = "fx_zheng_bolt", Impact = "fx_zheng_impact";
        public const string Claw = "fx_claw", Shatter = "fx_shatter", Buff = "fx_buff", Dust = "fx_dust";
        public const string HuPiece = "fx_hu_piece", HuLink = "fx_hu_link";
        public static readonly string[] Prefabs = { Charge, Bolt, Impact, Claw, Shatter, Buff, Dust };

        private const float BarWidth = 0.56f, Turn = 40;
        private static readonly int RimColor = Shader.PropertyToID("_RimColor"), RimPower = Shader.PropertyToID("_RimPower"), RimStrength = Shader.PropertyToID("_RimStrength");

        private sealed class Piece
        {
            public Transform Root, Model, Fill;
            public Animator Animator;
            public Renderer[] Renderers;
            public Vector3 Home, Scale;
            public Quaternion Rest;
            public Vector3 Forward; // facing at rest, on the table
            public float Health = 1, Shown = 1;
        }

        private readonly Camera cam;
        private readonly Func<string, GameObject> prefab;
        private readonly BattlePops pops;
        private readonly TMPro.TextMeshProUGUI round;
        private readonly Dictionary<(int, int, int), Piece> board = new();
        private readonly List<Piece> pieces, hand;
        private readonly Transform ring;
        private readonly List<(float At, Action Do)> events = new();
        private readonly List<(float From, float To, Action<float> Pose)> motions = new();
        private readonly List<GameObject> spawned = new();
        private readonly MaterialPropertyBlock flash = new();
        private int fired;
        private float stopUntil = -1, shakeAt = -1, shakeAmp, push, joinAt;
        private Vector3 focus;

        private readonly TurnSetup setup;
        private readonly TurnBattle whole;
        /// <summary>The piece of each core unit (TurnBattle.Units order).</summary>
        private readonly Piece[] actors;
        private readonly List<float> starts = new();
        private readonly List<float> highlights = new();

        /// <summary>When the battle is over (seconds from its start).</summary>
        public readonly float End;
        public int Winner => whole.Winner;
        /// <summary>Moments worth a screenshot: each action's blow, the hand going off, the result.</summary>
        public IReadOnlyList<float> Highlights => highlights;

        public BattleShow(Camera cam, IEnumerable<Transform> units, IEnumerable<Transform> handInOrder, Func<string, GameObject> prefab, BattlePops pops, TMPro.TextMeshProUGUI round, uint seed)
        {
            this.cam = cam;
            this.prefab = prefab;
            this.pops = pops;
            this.round = round;
            pieces = units.Select(r => new Piece { Root = r, Animator = r.GetComponentInChildren<Animator>(), Fill = r.Find("BarFill") }).ToList();
            foreach (var p in pieces)
            {
                p.Model = p.Animator.transform;
                p.Renderers = p.Model.GetComponentsInChildren<Renderer>();
                var side = p.Root.position.z > 0 ? 1 : -1;
                p.Forward = Quaternion.Euler(0, (side > 0 ? 180 : 0) + Turn, 0) * Vector3.forward;
                if (p.Fill != null) p.Health = p.Shown = p.Fill.localScale.x / BarWidth;
                var cell = CellOf(p.Root.position);
                if (cell.HasValue) board[cell.Value] = p;
            }
            hand = handInOrder.Select(r => pieces.First(p => p.Root == r)).ToList();
            ring = pieces.Select(p => p.Root.Find("ActiveRing")).FirstOrDefault(r => r != null);
            SetUpRing();

            setup = BattleLineup.Setup(seed);
            whole = new TurnBattle(setup);
            whole.Run();
            actors = whole.Units.Select(u => board[BattleLineup.CellOf(u)]).ToArray();
            for (var i = 0; i < actors.Length; i++)
            {
                actors[i].Health = actors[i].Shown = (float)setup.Units[i].Hp / setup.Units[i].MaxHp;
                Bar(actors[i]);
            }
            End = Seconds(whole.Events[^1].Tick);
            StageAll();
            var result = whole.Winner == 0 ? "胜" : whole.Winner == 1 ? "败" : "平";
            events.Add((End + 0.2f, () => pops.Banner(result, 230, BattlePops.Crit, 60, Now, 2.6f)));
            highlights.Add(End + 0.6f);
            events.Sort((x, y) => x.At.CompareTo(y.At));
            Debug.Log($"[Battle] seed {seed}: {whole.Events.Count} core events, {starts.Count} actions, winner {whole.Winner} at tick {whole.Events[^1].Tick} ({End:F1} s), hash {whole.Hash():x16}");
        }

        private static float Seconds(int tick) => (float)tick / TurnTiming.TicksPerSecond;

        /// <summary>Called once the pieces stand where the battle starts (the opponent has landed).</summary>
        public void Begin()
        {
            foreach (var p in pieces)
            {
                p.Home = p.Model.position;
                p.Rest = p.Model.rotation;
                p.Scale = p.Model.localScale;
            }
        }

        private float Now => Time.time;

        /// <summary>Advances to battle time `b`; call every frame.</summary>
        public void Tick(float b)
        {
            if (stopUntil >= 0 && Time.unscaledTime >= stopUntil)
            {
                Time.timeScale = 1;
                stopUntil = -1;
            }
            while (fired < events.Count && events[fired].At <= b) events[fired++].Do();
            foreach (var (from, to, pose) in motions)
                if (from >= joinAt && b >= from && b < to + 0.1f) pose(Mathf.Clamp01((b - from) / (to - from)));
            foreach (var p in pieces.Where(p => p.Fill != null))
            {
                p.Shown = Mathf.MoveTowards(p.Shown, p.Health, Time.deltaTime * 1.5f);
                Bar(p);
            }
            UpdateRing();
            pops.Update(Now);
            // (HuShowUi sets the round bar to the battle every frame.)
            if (b >= End && round != null) round.text = round.text.Replace("战斗", "战斗结束");
        }

        /// <summary>
        /// Joins the battle at time `b` (spectating, reconnecting, or jumping while it plays; 01 §4).
        /// An action in flight is not shown half-way: the presentation picks up at the next action's
        /// start, with the state the core fast-forwards to (RunTo, no presentation). Returns that time.
        /// </summary>
        public float SeekTo(float b)
        {
            joinAt = starts.FirstOrDefault(s => s >= b - 0.001f);
            if (joinAt < b - 0.001f) joinAt = End;
            var tick = Mathf.RoundToInt(joinAt * TurnTiming.TicksPerSecond);
            var state = new TurnBattle(setup);
            state.RunTo(tick);

            Reset();
            stopUntil = shakeAt = -1;
            push = 0;
            MoveRing(null);
            pops.Clear();
            foreach (var go in spawned.Where(go => go != null)) UnityEngine.Object.Destroy(go);
            spawned.Clear();
            foreach (var p in pieces)
            {
                p.Model.SetPositionAndRotation(p.Home, p.Rest);
                p.Model.localScale = p.Scale;
                Flash(p, false);
            }
            for (var i = 0; i < actors.Length; i++)
            {
                var p = actors[i];
                var alive = state.Units[i].Alive;
                p.Health = p.Shown = (float)state.Units[i].Hp / state.Units[i].MaxHp;
                Bar(p);
                Show(p, alive);
                if (alive) Play(p, "idle", 0.1f);
            }
            fired = events.FindIndex(e => e.At >= joinAt);
            if (fired < 0) fired = events.Count;
            var hp = string.Join(" ", state.Units.Select(u => u.Hp));
            Debug.Log($"[Battle] seek to {b:F2} s: joins at tick {tick} ({joinAt:F1} s), core ran {state.Events.Count} events, hp {hp}");
            return joinAt;
        }

        private static void Bar(Piece p)
        {
            var s = p.Fill.localScale;
            p.Fill.localScale = new Vector3(BarWidth * p.Shown, s.y, s.z);
            var at = p.Fill.localPosition;
            p.Fill.localPosition = new Vector3(-BarWidth * (1 - p.Shown) / 2, at.y, at.z);
        }

        /// <summary>Camera push towards the skill and shake, added to the camera's position.</summary>
        public Vector3 CameraOffset(Vector3 at)
        {
            var offset = (focus - at) * 0.3f * HuShowUi.Smooth(push);
            if (shakeAt >= 0)
            {
                var age = Time.unscaledTime - shakeAt;
                var amp = shakeAmp * Mathf.Exp(-age * 14);
                var t = cam.transform;
                offset += (t.right * Mathf.Sin(age * 97) + t.up * Mathf.Cos(age * 83)) * amp;
            }
            return offset;
        }

        /// <summary>Puts the time scale back (the demo restarts or seeks mid hit stop).</summary>
        public static void Reset() => Time.timeScale = 1;

        // ---- Pieces -------------------------------------------------------------------------

        /// <summary>A shattered piece is gone: model and bar hidden (and the ring, if it stood there).</summary>
        private static void Show(Piece p, bool on)
        {
            p.Model.gameObject.SetActive(on);
            foreach (var name in on ? new[] { "BarBack", "BarFill" } : new[] { "BarBack", "BarFill", "ActiveRing" })
            {
                var t = p.Root.Find(name);
                if (t != null) t.gameObject.SetActive(on);
            }
        }

        private static void Play(Piece p, string clip, float fade = 0.05f)
        {
            p.Animator.speed = 1;
            p.Animator.CrossFade(clip, fade, 0, 0);
        }

        /// <summary>White rim light over the whole piece for a few frames.</summary>
        private void Flash(Piece p, bool on)
        {
            foreach (var r in p.Renderers)
            {
                if (!on)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                flash.SetColor(RimColor, new Color(2.2f, 2, 1.8f, 1));
                flash.SetFloat(RimPower, 1.2f);
                flash.SetFloat(RimStrength, 1);
                r.SetPropertyBlock(flash);
            }
        }

        /// <summary>
        /// Freezes animation and particles for a moment (real time), so the blow lands. The battle
        /// clock runs on: what comes next still starts at its tick.
        /// </summary>
        private void HitStop(float seconds)
        {
            Time.timeScale = 0.03f;
            stopUntil = Time.unscaledTime + seconds;
        }

        private void Shake(float amplitude)
        {
            shakeAt = Time.unscaledTime;
            shakeAmp = amplitude;
        }

        private GameObject Spawn(string name, Vector3 at)
        {
            var source = prefab(name);
            var go = UnityEngine.Object.Instantiate(source, at, source.transform.rotation);
            UnityEngine.Object.Destroy(go, 4);
            spawned.RemoveAll(g => g == null);
            spawned.Add(go);
            return go;
        }

        private void Link(Vector3 a, Vector3 b)
        {
            var go = Spawn(HuLink, (a + b) / 2);
            var d = Flat(b - a) * (b - a).magnitude;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.right, d);
            go.transform.localScale = new Vector3(d.magnitude, 1, 1);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z).normalized;

        /// <summary>The cell under a world position (BoardLayout: 1 unit cells, 0.6 river), or null for the bench.</summary>
        private static (int, int, int)? CellOf(Vector3 p)
        {
            var side = p.z > 0 ? 1 : -1;
            var row = Mathf.RoundToInt(Mathf.Abs(p.z) - 0.8f);
            var col = Mathf.RoundToInt(p.x + 2);
            var exact = Mathf.Abs(Mathf.Abs(p.z) - (0.8f + row)) < 0.05f && Mathf.Abs(p.x - (col - 2)) < 0.05f;
            return exact && row is >= 0 and < 3 && col is >= 0 and < 5 ? (side, row, col) : null;
        }
    }
}
