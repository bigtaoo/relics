using System;
using System.Collections.Generic;
using System.Linq;
using Automatic.Battle.Turn;
using UnityEngine;

namespace Automatic.Game
{
    public sealed partial class BattleShow
    {
        /// <summary>
        /// Attack: the pounce takes Pounce, the claw clip connects ImpactAt into its play, so the claw
        /// lands at the core's hit tick (TurnTiming.AttackHitAt). Skill: the bolts leave the tails
        /// around Release and fly for Flight, the first target is hit at TurnTiming.SkillHitAt.
        /// </summary>
        private const float ImpactAt = 0.34f, Pounce = (float)TurnTiming.AttackHitAt / TurnTiming.TicksPerSecond - ImpactAt;
        private const float Flight = 0.4f, BoltStep = 0.03f, TargetStep = 0.04f;

        /// <summary>One blow as the core dealt it; HealthAfter is the target's share of health left.</summary>
        private struct Blow
        {
            public Piece Target;
            public int Damage;
            public bool Crit, Kill;
            public float HealthAfter;
        }

        /// <summary>Reads the core's event stream action by action and stages each one at its tick.</summary>
        private void StageAll()
        {
            var hp = setup.Units.Select(u => u.Hp).ToArray();
            var blows = new List<Blow>();
            var targets = new List<Piece>();
            TurnEvent start = default;
            var open = false;

            void Flush()
            {
                if (!open) return;
                var s = Seconds(start.Tick);
                starts.Add(s);
                var actor = actors[start.Actor];
                if (start.Act != TurnAct.Hand) events.Add((s, () => MoveRing(actor)));
                switch (start.Act)
                {
                    case TurnAct.Attack:
                        Attack(actor, actors[start.Target], blows.Count > 0 ? blows[0] : default, s);
                        highlights.Add(s + Seconds(TurnTiming.AttackHitAt) + 0.02f);
                        break;
                    case TurnAct.Skill:
                        Skill(actor, targets.ToArray(), blows.ToArray(), s);
                        highlights.Add(s + Seconds(TurnTiming.SkillHitAt) + 0.02f);
                        break;
                    default:
                        HandGoesOff(start.Amount, s);
                        highlights.Add(s + 1.2f);
                        break;
                }
                blows.Clear();
                targets.Clear();
                open = false;
            }

            foreach (var e in whole.Events)
                switch (e.Kind)
                {
                    case TurnEventKind.HandTrigger:
                    case TurnEventKind.AttackStart:
                    case TurnEventKind.SkillCast:
                        Flush();
                        start = e;
                        open = true;
                        break;
                    case TurnEventKind.AttackHit:
                    case TurnEventKind.SkillHit:
                        targets.Add(actors[e.Target]);
                        break;
                    case TurnEventKind.Damage:
                        hp[e.Target] = Math.Max(0, hp[e.Target] - e.Amount);
                        blows.Add(new Blow { Target = actors[e.Target], Damage = e.Amount, Crit = e.Crit, HealthAfter = (float)hp[e.Target] / setup.Units[e.Target].MaxHp });
                        break;
                    case TurnEventKind.Death:
                        var k = blows.FindLastIndex(b => b.Target == actors[e.Target]);
                        var kill = blows[k];
                        kill.Kill = true;
                        blows[k] = kill;
                        break;
                    case TurnEventKind.BattleEnd:
                        Flush();
                        break;
                }
        }

        /// <summary>Pounce onto the target, claw and bite at the hit tick, pounce back; 1.6 s in all.</summary>
        private void Attack(Piece a, Piece target, Blow blow, float s)
        {
            Vector3 home = default, strike = default;
            events.Add((s, () =>
            {
                home = a.Home;
                var dir = Flat(target.Root.position - a.Root.position);
                strike = target.Home - dir * 0.6f + Vector3.up * (home.y - target.Home.y);
                Spawn(Dust, a.Root.position);
            }));
            Leap(a, () => home, () => strike, s, Pounce);
            events.Add((s + Pounce, () =>
            {
                Play(a, "attack");
                Spawn(Dust, strike - Vector3.up * strike.y);
            }));
            var contact = s + Pounce + ImpactAt;
            events.Add((contact, () =>
            {
                var dir = Flat(target.Root.position - a.Root.position);
                var fx = Spawn(Claw, target.Home + Vector3.up * 0.25f);
                fx.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir);
            }));
            if (blow.Target != null) Struck(blow, contact, false);
            Leap(a, () => strike, () => home, s + 1.05f, 0.4f);
            events.Add((s + 1.45f, () =>
            {
                Spawn(Dust, home - Vector3.up * home.y);
                Play(a, "idle", 0.2f);
            }));
            motions.Add((s + 1.45f, s + 1.6f, u => a.Model.rotation = Quaternion.Slerp(a.Model.rotation, a.Rest, u)));
        }

        /// <summary>
        /// Five-tail flames: the camera pushes in, the caster rears up over a sigil, one bolt leaves
        /// each tail tip and fans out onto the targets (2 / 1 / 2 bolts on three, 3 / 2 on two). The
        /// first target is hit at the core's hit tick, the others a beat later, inside the action.
        /// </summary>
        private void Skill(Piece caster, Piece[] targets, Blow[] blows, float s)
        {
            events.Add((s, () =>
            {
                focus = (caster.Home + targets.Aggregate(Vector3.zero, (sum, t) => sum + t.Home) / targets.Length) / 2 + Vector3.up * 0.3f;
                Play(caster, "cast");
                Spawn(Charge, caster.Root.position);
                pops.Callout(() => caster.Model.position, BattleLineup.SkillName, Now);
            }));
            motions.Add((s, s + 0.45f, u => push = u));
            motions.Add((s + 1.9f, s + 2.45f, u => push = 1 - u));
            var which = targets.Length switch
            {
                3 => new[] { 0, 0, 1, 2, 2 },
                2 => new[] { 0, 0, 0, 1, 1 },
                _ => new[] { 0, 0, 0, 0, 0 },
            };
            var hits = Enumerable.Range(0, targets.Length).Select(i => s + Seconds(TurnTiming.SkillHitAt) + i * TargetStep).ToArray();
            for (var t = 1; t <= 5; t++)
            {
                var tail = t;
                var aim = which[t - 1];
                var target = targets[aim];
                var go = default(GameObject);
                Vector3 from = default, control = default, to = default;
                // The last bolt on each target lands on its hit; the ones before leave a step earlier.
                var launch = hits[aim] - Flight - (Array.LastIndexOf(which, aim) - (t - 1)) * BoltStep;
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
                events.Add((hits[i], () => Spawn(Impact, target.Root.position)));
                var blow = blows.FirstOrDefault(b => b.Target == target);
                if (blow.Target != null) Struck(blow, hits[i], true);
            }
            events.Add((s + 1.6f, () => Play(caster, "idle", 0.25f)));
        }

        /// <summary>
        /// The formed hand goes off: its name across the screen, each piece hops inside a light
        /// pillar in turn, links redraw the hand's shape, then every piece on the board is buffed.
        /// </summary>
        private void HandGoesOff(int percent, float s)
        {
            events.Add((s, () =>
            {
                MoveRing(null);
                pops.Banner($"{BattleLineup.HandName} · 发动", 92, BattlePops.Crit, 250, Now, 2.1f);
            }));
            events.Add((s + 0.3f, () => pops.Banner(BattleLineup.HandEffect(percent), 50, BattlePops.Weapon, 165, Now, 1.8f)));
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

        /// <summary>The blow lands at `at`: hit stop, shake, number, health, flash and squash; or death.</summary>
        private void Struck(Blow blow, float at, bool skill)
        {
            var target = blow.Target;
            events.Add((at, () =>
            {
                HitStop(blow.Crit || skill ? 0.11f : 0.06f);
                Shake(blow.Crit ? 0.09f : skill ? 0.07f : 0.04f);
                pops.Damage(() => target.Model.position + Vector3.up * 0.55f, blow.Damage, skill, blow.Crit, Now);
                target.Health = blow.Kill ? 0 : Mathf.Max(0.02f, blow.HealthAfter);
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
                    Show(target, false);
                    Shake(0.05f);
                }));
            else
                events.Add((at + 0.5f, () => Play(target, "idle", 0.2f)));
        }

        /// <summary>A pounce from `from` to `to` over `seconds`, turning to face the way it goes.</summary>
        private void Leap(Piece p, Func<Vector3> from, Func<Vector3> to, float s, float seconds)
        {
            events.Add((s, () => Play(p, "leap")));
            motions.Add((s, s + seconds, u =>
            {
                Vector3 a = from(), b = to();
                var k = HuShowUi.Smooth(Mathf.InverseLerp(0.1f, 0.85f, u));
                p.Model.position = Vector3.Lerp(a, b, k) + Vector3.up * 0.4f * 4 * k * (1 - k);
                var face = Quaternion.AngleAxis(Vector3.SignedAngle(p.Forward, Flat(b - a), Vector3.up), Vector3.up) * p.Rest;
                p.Model.rotation = Quaternion.Slerp(p.Model.rotation, face, Mathf.Clamp01(u * 5));
            }));
        }
    }
}
