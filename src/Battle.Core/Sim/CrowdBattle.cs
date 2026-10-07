#nullable enable
using System.Collections.Generic;
using Automatic.Battle.Math;

namespace Automatic.Battle.Sim
{
    public enum SimEventKind : byte
    {
        Attack,
        Damage,
        Death,
        Cast,
        Spawn,
        Heal,
        Shield,
    }

    /// <summary>What the presentation layer would consume (design/03 §8).</summary>
    public struct SimEvent
    {
        public int Tick;
        public SimEventKind Kind;
        public int Source, Target;
        public FP Amount;
    }

    /// <summary>
    /// Stress prototype of the battle logic for summon builds (design/08 §2): how much CPU a 300-unit
    /// battle costs on the server (battles per core) and on the client (catching up when switching to
    /// another battle, design/01 §4). Not the real rules; see SimConfig. Units act in id order, or the reverse.
    /// </summary>
    public sealed partial class CrowdBattle
    {
        public const int TicksPerSecond = 30;
        private const int RetargetTicks = 15;

        public readonly SimConfig Config;
        public readonly List<SimUnit> Units = new List<SimUnit>(512);
        public readonly List<SimEvent> Events = new List<SimEvent>(4096);
        public int Tick { get; private set; }
        /// <summary>-1 while running, then the winning side, or 2 for a draw.</summary>
        public int Winner { get; private set; } = -1;
        public bool Over => Winner >= 0;
        public int PeakAlive { get; private set; }
        /// <summary>Sum over the ticks of the living units: UnitTicks / Tick is the average crowd.</summary>
        public long UnitTicks { get; private set; }

        private static readonly int[] HeroColumns = { 10, 25, 45, 60 };

        private readonly Prng combat;
        private readonly Prng placement;
        private readonly SpatialGrid grid;
        private readonly int[] alive = new int[2];
        private readonly int[] summons = new int[2];
        private readonly List<int> near = new List<int>(256);
        private readonly FP width, height, maxRadius, edge;

        public CrowdBattle(SimConfig config)
        {
            Config = config;
            combat = new Prng(config.Seed);
            placement = new Prng(config.Seed ^ 0x9E3779B9u);
            grid = new SpatialGrid(config.Width, config.Height);
            width = FP.FromInt(config.Width);
            height = FP.FromInt(config.Height);
            maxRadius = FP.FromRatio(30, 100);
            edge = FP.FromRatio(1, 100);
            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < SimConfig.Heroes.Length; i++)
                {
                    // Front row tanks and fighters, back row archers and supports; side 1 mirrored.
                    var x = FP.FromRatio(HeroColumns[i % 4], 10);
                    var y = i < 4 ? FP.FromRatio(33, 10) : FP.FromRatio(12, 10);
                    var u = Add(SimConfig.Heroes[i], side, x, Mirror(side, y), false);
                    if (i >= 4 && i - 4 < config.SummonersPerSide)
                    {
                        u.SummonEvery = config.SummonEvery;
                        u.SummonBatch = config.SummonBatch;
                        u.NextSummonAt = 15 + (i - 4) * 5;
                    }
                }
                // Initial summons on a lattice over the side's half, slightly jittered.
                for (int k = 0; k < config.InitialSummons; k++)
                {
                    var x = FP.FromRatio(3 + (k % 13) * 5, 10) + Jitter();
                    var y = FP.FromRatio(5 + (k / 13) * 3, 10) + Jitter();
                    Add(SimConfig.Summon, side, x, Mirror(side, y), true);
                }
            }
        }

        public void Run()
        {
            while (!Over) Step();
        }

        public void Step()
        {
            if (Over) return;
            if (Config.UseGrid) grid.Build(Units);
            // Which side moves first is drawn every tick (one draw, always). In a fixed or alternating
            // order the side moving second got the first hit, since it steps into range after the other
            // side closed in, and an even attack interval keeps a unit on the same tick parity: one side
            // won almost every battle.
            int count = Units.Count;
            bool ascending = combat.NextInt(2) == 0;
            for (int k = 0; k < count; k++)
            {
                var u = Units[ascending ? k : count - 1 - k];
                if (u.Alive) Act(u);
            }
            Tick++;
            int total = alive[0] + alive[1];
            if (total > PeakAlive) PeakAlive = total;
            UnitTicks += total;
            if (alive[0] == 0 || alive[1] == 0) Winner = alive[0] == alive[1] ? 2 : alive[0] == 0 ? 1 : 0;
            else if (Tick >= Config.MaxTicks) Winner = TimeoutWinner();
        }

        private void Act(SimUnit u)
        {
            UpdateBuffs(u);
            if (!u.Alive) return;
            if (u.SummonEvery > 0 && Tick >= u.NextSummonAt)
            {
                Spawn(u);
                u.NextSummonAt = Tick + u.SummonEvery;
            }
            if (Tick < u.StunnedUntil) return;
            var t = u.Target >= 0 ? Units[u.Target] : null;
            if (t == null || !t.Alive || Tick >= u.RetargetAt)
            {
                t = NearestEnemy(u);
                u.Target = t == null ? -1 : t.Id;
                u.RetargetAt = Tick + RetargetTicks;
            }
            if (t == null) return;
            var dx = t.X - u.X;
            var dy = t.Y - u.Y;
            var reach = u.Range + u.Radius + t.Radius;
            if (dx * dx + dy * dy <= reach * reach)
            {
                if (Tick >= u.AttackReadyAt) Attack(u, t);
            }
            else
            {
                Move(u, dx, dy, reach);
            }
        }

        private SimUnit? NearestEnemy(SimUnit u)
        {
            SimUnit? best = null;
            var bestD = FP.Zero;
            if (!Config.UseGrid)
            {
                for (int i = 0; i < Units.Count; i++) Closer(u, Units[i], ref best, ref bestD);
                return best;
            }
            int cx = grid.CellX(u.X), cy = grid.CellY(u.Y);
            int rings = grid.Cols > grid.Rows ? grid.Cols : grid.Rows;
            for (int ring = 0; ring < rings; ring++)
            {
                near.Clear();
                grid.Ring(cx, cy, ring, near);
                for (int i = 0; i < near.Count; i++) Closer(u, Units[near[i]], ref best, ref bestD);
                // Anything in a farther ring is at least `ring` cells away.
                if (best != null && bestD <= FP.FromInt(ring * ring)) break;
            }
            return best;
        }

        private static void Closer(SimUnit u, SimUnit o, ref SimUnit? best, ref FP bestD)
        {
            if (!o.Alive || o.Side == u.Side) return;
            var dx = o.X - u.X;
            var dy = o.Y - u.Y;
            var d = dx * dx + dy * dy;
            if (best == null || d < bestD || (d == bestD && o.Id < best.Id))
            {
                best = o;
                bestD = d;
            }
        }

        private void Move(SimUnit u, FP dx, FP dy, FP reach)
        {
            var len = FixedMath.Sqrt(dx * dx + dy * dy);
            if (len == FP.Zero) return;
            var step = FP.Min(u.Speed, len - reach + edge * 10);
            var nx = u.X + dx * step / len;
            var ny = u.Y + dy * step / len;
            // Separation: push out of every overlapping unit by half the overlap. The pushes are
            // measured from the same point and summed, so the order of the neighbours does not matter
            // (applied one after another, the cell order of the grid favoured one side).
            near.Clear();
            Nearby(nx, ny, u.Radius + maxRadius);
            FP px = FP.Zero, py = FP.Zero;
            for (int i = 0; i < near.Count; i++)
            {
                var o = Units[near[i]];
                if (o == u || !o.Alive) continue;
                var ox = nx - o.X;
                var oy = ny - o.Y;
                var min = u.Radius + o.Radius;
                var d2 = ox * ox + oy * oy;
                if (d2 >= min * min || d2 == FP.Zero) continue;
                var d = FixedMath.Sqrt(d2);
                if (d == FP.Zero) continue;
                var push = (min - d) / 2;
                px += ox * push / d;
                py += oy * push / d;
            }
            nx += px;
            ny += py;
            u.X = FP.Clamp(nx, FP.Zero, width - edge);
            u.Y = FP.Clamp(ny, FP.Zero, height - edge);
        }

        /// <summary>Units around (x, y) into `near`, dead ones included; callers check distance and Alive.</summary>
        private void Nearby(FP x, FP y, FP r)
        {
            if (Config.UseGrid)
            {
                grid.Query(x, y, r, near);
                return;
            }
            for (int i = 0; i < Units.Count; i++) near.Add(i);
        }

        private void Spawn(SimUnit summoner)
        {
            int side = summoner.Side;
            int n = Config.SummonCap - summons[side];
            if (n > summoner.SummonBatch) n = summoner.SummonBatch;
            var forward = side == 0 ? FP.One : -FP.One;
            for (int k = 0; k < n; k++)
            {
                // Fan out in front of the summoner.
                var x = summoner.X + FP.FromRatio((k % 3 - 1) * 4, 10) + Jitter();
                var y = summoner.Y + forward * FP.FromRatio(4 + k / 3 * 3, 10) + Jitter();
                var s = Add(SimConfig.Summon, side, FP.Clamp(x, FP.Zero, width - edge), FP.Clamp(y, FP.Zero, height - edge), true);
                s.Summoner = summoner.Id;
                Emit(SimEventKind.Spawn, summoner.Id, s.Id, FP.Zero);
            }
        }

        private SimUnit Add(UnitTemplate t, int side, FP x, FP y, bool summon)
        {
            var u = new SimUnit
            {
                Id = Units.Count, Side = side, IsSummon = summon, X = x, Y = y,
                Radius = FP.FromRatio(t.RadiusHundredths, 100), Speed = FP.FromRatio(t.SpeedHundredths, 100),
                Hp = FP.FromInt(t.Hp), MaxHp = FP.FromInt(t.Hp), Atk = FP.FromInt(t.Atk), Def = FP.FromInt(t.Def),
                Range = SimConfig.Tenths(t.RangeTenths), CritPercent = t.CritPercent,
                AttackInterval = t.AttackInterval, AttackReadyAt = Tick + t.AttackInterval / 2,
                ManaMax = t.ManaMax, Skill = t.Skill, SkillPower = FP.FromPercent(t.SkillPercent),
                SkillRadius = SimConfig.Tenths(t.SkillRadiusTenths),
            };
            Units.Add(u);
            alive[side]++;
            if (summon) summons[side]++;
            return u;
        }

        private FP Mirror(int side, FP y) => side == 0 ? y : height - y;

        private FP Jitter() => FP.FromRatio(placement.NextInt(21) - 10, 100);
    }
}
