using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Automatic.Game
{
    /// <summary>
    /// The battle presentation slice (design/08 §1): plays BattleScript on the board after the
    /// battle start, as the presentation layer will play the core's event stream. Each act is
    /// staged inside its fixed length: pieces pounce on their target and back, claws and flames
    /// land with a hit stop, a camera shake and a damage number; dying artifacts shatter; the
    /// formed hand lights up again and buffs the board; the skill pushes the camera in.
    /// Time is the battle's own clock (seconds from its start), driven by ShopDemo.
    /// </summary>
    public sealed class BattleShow
    {
        public const string Charge = "fx_zheng_charge", Bolt = "fx_zheng_bolt", Impact = "fx_zheng_impact";
        public const string Claw = "fx_claw", Shatter = "fx_shatter", Buff = "fx_buff", Dust = "fx_dust";
        public const string HuPiece = "fx_hu_piece", HuLink = "fx_hu_link";
        public static readonly string[] Prefabs = { Charge, Bolt, Impact, Claw, Shatter, Buff, Dust };

        private const float BarWidth = 0.56f, Turn = 40, ImpactAt = 0.34f, Release = 0.5f, Flight = 0.35f;
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
        private readonly MaterialPropertyBlock flash = new();
        private int fired;
        private float stopUntil = -1, shakeAt = -1, shakeAmp, push;
        private Vector3 focus;

        /// <summary>When the battle is over (seconds from its start).</summary>
        public readonly float End;

        public BattleShow(Camera cam, IEnumerable<Transform> units, IEnumerable<Transform> handInOrder, Func<string, GameObject> prefab, BattlePops pops, TMPro.TextMeshProUGUI round)
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
            foreach (var (cell, health) in BattleScript.Health)
                if (board.TryGetValue(cell, out var p))
                {
                    p.Health = p.Shown = health / 1000f;
                    Bar(p);
                }
            var starts = BattleScript.Starts();
            for (var i = 0; i < BattleScript.Acts.Length; i++) Stage(BattleScript.Acts[i], starts[i]);
            End = starts[^1];
            events.Add((End + 0.2f, () => pops.Banner("胜", 230, BattlePops.Crit, 60, Now, 2.6f)));
            events.Sort((x, y) => x.At.CompareTo(y.At));
        }

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
                if (b >= from && b < to + 0.1f) pose(Mathf.Clamp01((b - from) / (to - from)));
            foreach (var p in pieces.Where(p => p.Fill != null))
            {
                p.Shown = Mathf.MoveTowards(p.Shown, p.Health, Time.deltaTime * 1.5f);
                Bar(p);
            }
            pops.Update(Now);
            // (HuShowUi sets the round bar to the battle every frame.)
            if (b >= End && round != null) round.text = round.text.Replace("战斗", "战斗结束");
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

        /// <summary>Puts the time scale back (the demo restarts mid hit stop).</summary>
        public static void Reset() => Time.timeScale = 1;

        // ---- Acts ---------------------------------------------------------------------------

        private void Stage(Act act, float s)
        {
            var actor = board[(act.Actor.Side, act.Actor.Row, act.Actor.Col)];
            if (act.Kind != ActKind.Hand) events.Add((s, () => MoveRing(actor)));
            switch (act.Kind)
            {
                case ActKind.Attack: Attack(actor, act.Blows[0], s); break;
                case ActKind.Skill: Skill(actor, act, s); break;
                default: HandGoesOff(s); break;
            }
        }

        /// <summary>Pounce onto the target (0.4 s), claw and bite (contact 0.34 s in), pounce back.</summary>
        private void Attack(Piece a, Blow blow, float s)
        {
            var target = board[(blow.Target.Side, blow.Target.Row, blow.Target.Col)];
            Vector3 home = default, strike = default;
            events.Add((s, () =>
            {
                home = a.Home;
                var dir = Flat(target.Root.position - a.Root.position);
                strike = target.Home - dir * 0.6f + Vector3.up * (home.y - target.Home.y);
                Spawn(Dust, a.Root.position);
            }));
            Leap(a, () => home, () => strike, s);
            events.Add((s + 0.4f, () =>
            {
                Play(a, "attack");
                Spawn(Dust, strike - Vector3.up * strike.y);
            }));
            var contact = s + 0.4f + ImpactAt;
            events.Add((contact, () =>
            {
                var dir = Flat(target.Root.position - a.Root.position);
                var fx = Spawn(Claw, target.Home + Vector3.up * 0.25f);
                fx.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir);
            }));
            Struck(target, blow, contact, false);
            Leap(a, () => strike, () => home, s + 1.05f);
            events.Add((s + 1.45f, () =>
            {
                Spawn(Dust, home - Vector3.up * home.y);
                Play(a, "idle", 0.2f);
            }));
            motions.Add((s + 1.45f, s + 1.6f, u => a.Model.rotation = Quaternion.Slerp(a.Model.rotation, a.Rest, u)));
        }

        /// <summary>
        /// Five-tail flames: the camera pushes in, the caster rears up over a sigil, one bolt leaves
        /// each tail tip at the release and fans out onto the targets (2 / 1 / 2 bolts).
        /// </summary>
        private void Skill(Piece caster, Act act, float s)
        {
            var targets = act.Blows.Select(b => board[(b.Target.Side, b.Target.Row, b.Target.Col)]).ToArray();
            events.Add((s, () =>
            {
                focus = (caster.Home + targets.Aggregate(Vector3.zero, (sum, t) => sum + t.Home) / targets.Length) / 2 + Vector3.up * 0.3f;
                Play(caster, "cast");
                Spawn(Charge, caster.Root.position);
                pops.Callout(() => caster.Model.position, act.Name, Now);
            }));
            motions.Add((s, s + 0.45f, u => push = u));
            motions.Add((s + 1.9f, s + 2.45f, u => push = 1 - u));
            var which = new[] { 0, 0, 1, 2, 2 };
            for (var t = 1; t <= 5; t++)
            {
                var tail = t;
                var target = targets[which[t - 1]];
                var go = default(GameObject);
                Vector3 from = default, control = default, to = default;
                var launch = s + Release + (t - 1) * 0.03f;
                events.Add((launch, () =>
                {
                    from = caster.Model.GetComponentsInChildren<Transform>().First(b => b.name == $"tail{tail}.4").position;
                    to = target.Home + Vector3.up * 0.3f;
                    var right = Vector3.Cross(Vector3.up, to - from).normalized;
                    control = (from + to) / 2 + Vector3.up * 0.8f + right * ((tail - 3) * 0.4f);
                    go = Spawn(Bolt, from);
                }));
                motions.Add((launch, launch + Flight, u =>
                {
                    if (go != null) go.transform.position = Vector3.Lerp(Vector3.Lerp(from, control, u), Vector3.Lerp(control, to, u), u);
                }));
            }
            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                var last = Array.LastIndexOf(which, i);
                var hit = s + Release + last * 0.03f + Flight;
                events.Add((hit, () => Spawn(Impact, target.Root.position)));
                Struck(target, act.Blows[i], hit, true);
            }
            events.Add((s + 1.6f, () => Play(caster, "idle", 0.25f)));
        }

        /// <summary>
        /// The formed hand goes off: its name across the screen, each piece hops inside a light
        /// pillar in turn, links redraw the hand's shape, then every piece on the board is buffed.
        /// </summary>
        private void HandGoesOff(float s)
        {
            events.Add((s, () =>
            {
                MoveRing(null);
                pops.Banner($"{BattleScript.HandName} · 发动", 92, BattlePops.Crit, 250, Now, 2.1f);
            }));
            events.Add((s + 0.3f, () => pops.Banner(BattleScript.HandEffect, 50, BattlePops.Weapon, 165, Now, 1.8f)));
            for (var i = 0; i < hand.Count; i++)
            {
                var p = hand[i];
                var at = s + 0.2f + 0.1f * i;
                events.Add((at, () => Spawn(HuPiece, p.Root.position)));
                motions.Add((at, at + 0.4f, u => p.Model.position = p.Home + Vector3.up * 0.22f * Mathf.Sin(u * Mathf.PI)));
                if (i == 0) continue;
                var prev = hand[i - 1];
                events.Add((at + 0.05f, () => Link(prev.Root.position, p.Root.position)));
            }
            events.Add((s + 0.25f + 0.1f * hand.Count, () => Link(hand[^1].Root.position, hand[0].Root.position)));
            var centre = hand.Aggregate(Vector3.zero, (sum, p) => sum + p.Home) / hand.Count;
            var mine = board.Where(kv => kv.Key.Item1 < 0).Select(kv => kv.Value).OrderBy(p => (p.Home - centre).sqrMagnitude).ToList();
            for (var k = 0; k < mine.Count; k++)
            {
                var p = mine[k];
                events.Add((s + 1.0f + 0.04f * k, () =>
                {
                    if (p.Model.gameObject.activeSelf) Spawn(Buff, p.Root.position);
                }));
            }
        }

        // ---- Pieces -------------------------------------------------------------------------

        /// <summary>The blow lands at `at`: hit stop, shake, number, health, flash and squash; or death.</summary>
        private void Struck(Piece target, Blow blow, float at, bool skill)
        {
            events.Add((at, () =>
            {
                HitStop(blow.Crit || skill ? 0.11f : 0.06f);
                Shake(blow.Crit ? 0.09f : skill ? 0.07f : 0.04f);
                pops.Damage(() => target.Model.position + Vector3.up * 0.55f, blow.Damage, skill, blow.Crit, Now);
                target.Health = blow.Kill ? 0 : Mathf.Max(0.02f, target.Health - blow.Damage / 1000f);
                Play(target, blow.Kill ? "death" : "hit");
                Flash(target, true);
            }));
            events.Add((at + 0.09f, () => Flash(target, false)));
            motions.Add((at, at + 0.25f, u =>
            {
                var k = 0.18f * Mathf.Exp(-u * 6) * Mathf.Cos(u * 9);
                target.Model.localScale = Vector3.Scale(target.Scale, new Vector3(1 + k, 1 - k, 1 + k));
            }));
            if (blow.Kill)
                events.Add((at + 0.8f, () =>
                {
                    var fx = Spawn(Shatter, target.Root.position);
                    fx.transform.Find("Shards").GetComponent<ParticleSystemRenderer>().sharedMaterial = target.Renderers[0].sharedMaterial;
                    target.Model.gameObject.SetActive(false);
                    foreach (var name in new[] { "BarBack", "BarFill", "ActiveRing" })
                    {
                        var t = target.Root.Find(name);
                        if (t != null) t.gameObject.SetActive(false);
                    }
                    Shake(0.05f);
                }));
            else
                events.Add((at + 0.5f, () => Play(target, "idle", 0.2f)));
        }

        /// <summary>A pounce from `from` to `to` over 0.4 s, turning to face the way it goes.</summary>
        private void Leap(Piece p, Func<Vector3> from, Func<Vector3> to, float s)
        {
            events.Add((s, () => Play(p, "leap")));
            motions.Add((s, s + 0.4f, u =>
            {
                Vector3 a = from(), b = to();
                var k = HuShowUi.Smooth(Mathf.InverseLerp(0.1f, 0.85f, u));
                p.Model.position = Vector3.Lerp(a, b, k) + Vector3.up * 0.4f * 4 * k * (1 - k);
                var face = Quaternion.AngleAxis(Vector3.SignedAngle(p.Forward, Flat(b - a), Vector3.up), Vector3.up) * p.Rest;
                p.Model.rotation = Quaternion.Slerp(p.Model.rotation, face, Mathf.Clamp01(u * 5));
            }));
        }

        private void MoveRing(Piece p)
        {
            if (ring == null) return;
            ring.gameObject.SetActive(p != null);
            if (p != null) ring.SetParent(p.Root, false);
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

        /// <summary>Freezes animation and particles for a moment (real time), so the blow lands.</summary>
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
