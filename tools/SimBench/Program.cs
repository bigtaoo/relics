using System.Diagnostics;
using Automatic.Battle.Sim;

// Battle logic stress test on the server runtime (design/08 §2).
// usage: dotnet run -c Release --project tools/SimBench [-- --seeds N --seconds S]
var seeds = Arg("--seeds", 20);
var seconds = Arg("--seconds", 10);
Console.WriteLine($".NET {Environment.Version}, {Environment.ProcessorCount} logical cores, {(Debugger.IsAttached ? "debugger" : "no debugger")}");
Console.WriteLine();
Console.WriteLine("scenario          | ticks | units avg/peak/made | ms/battle median (p95) | worst tick ms | events | alloc KB | winners 0/1/draw | hash seed 1");

var cases = new List<Func<uint, SimConfig>>(SimConfigAll());
cases.Add(s => With(SimConfig.Crowd300(s), "crowd300 no events", c => c.RecordEvents = false));
cases.Add(s => With(SimConfig.Crowd300(s), "crowd300 no grid", c => c.UseGrid = false));
foreach (var make in cases) Report(make, seeds);

Console.WriteLine();
Console.WriteLine("Capacity, summon300 (the heaviest: about 250 units the whole 30 s) without events (the server needs no event stream):");
var single = Throughput(1, seconds / 2.0);
var all = Throughput(Environment.ProcessorCount, seconds);
Console.WriteLine($"  1 thread: {single:F1} battles/s; {Environment.ProcessorCount} threads: {all:F1} battles/s ({all / Environment.ProcessorCount:F1} per thread)");
// 8 players: 4 battles a round; a round is 20 s preparation + 30 s battle.
Console.WriteLine($"  if every battle were a summon battle: {single * 50 / 4:F0} matches per core (1 thread), {all * 50 / 4:F0} matches on this machine");

static IEnumerable<Func<uint, SimConfig>> SimConfigAll() => new Func<uint, SimConfig>[]
{
    SimConfig.Duel16, SimConfig.Summon300, SimConfig.Crowd300, SimConfig.Crowd500,
};

static SimConfig With(SimConfig c, string name, Action<SimConfig> change)
{
    c.Name = name;
    change(c);
    return c;
}

static void Report(Func<uint, SimConfig> make, int seeds)
{
    for (uint s = 1000; s < 1003; s++) new CrowdBattle(make(s)).Run();
    var ms = new List<double>();
    double worstTick = 0;
    long ticks = 0, events = 0, alloc = 0, peak = 0, unitTicks = 0, made = 0;
    var wins = new int[3];
    ulong hash1 = 0;
    var tickWatch = new Stopwatch();
    for (uint s = 1; s <= seeds; s++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var b = new CrowdBattle(make(s));
        while (!b.Over)
        {
            tickWatch.Restart();
            b.Step();
            worstTick = Math.Max(worstTick, tickWatch.Elapsed.TotalMilliseconds);
        }
        ms.Add(watch.Elapsed.TotalMilliseconds);
        alloc += GC.GetAllocatedBytesForCurrentThread() - before;
        ticks += b.Tick;
        events += b.Events.Count;
        peak = Math.Max(peak, b.PeakAlive);
        unitTicks += b.UnitTicks;
        made += b.Units.Count;
        wins[b.Winner]++;
        if (s == 1) hash1 = b.Hash();
    }
    // Same seed again must give the same battle.
    var again = new CrowdBattle(make(1));
    again.Run();
    var name = make(1).Name;
    ms.Sort();
    Console.WriteLine($"{name,-17} | {ticks / seeds,5} | {unitTicks / ticks,5}/{peak,3}/{made / seeds,4}  | {ms[ms.Count / 2],8:F2} ({ms[(int)(ms.Count * 0.95)],6:F2})        | {worstTick,13:F3} | {events / seeds,6} | {alloc / seeds / 1024,8} | {wins[0],2}/{wins[1],2}/{wins[2],2}         | {hash1:x16}" +
                      (again.Hash() == hash1 ? "" : "  NOT DETERMINISTIC"));
}

static double Throughput(int threads, double seconds)
{
    long done = 0;
    var until = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
    var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
    {
        uint seed = (uint)(t * 100000);
        while (Stopwatch.GetTimestamp() < until)
        {
            var c = SimConfig.Summon300(seed++);
            c.RecordEvents = false;
            new CrowdBattle(c).Run();
            Interlocked.Increment(ref done);
        }
    })).ToList();
    var watch = Stopwatch.StartNew();
    workers.ForEach(w => w.Start());
    workers.ForEach(w => w.Join());
    return done / watch.Elapsed.TotalSeconds;
}

int Arg(string name, int fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
}
