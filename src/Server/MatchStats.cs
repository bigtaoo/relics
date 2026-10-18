using System.Diagnostics;

namespace Automatic.Server;

/// <summary>What a match cost and whether the clients agreed with it, per round and in total.</summary>
public sealed class MatchStats
{
    public sealed class RoundStats
    {
        public int Round;
        public int Battles;
        public int Matched;
        public int Mismatched;
        /// <summary>Hashes a connected client still owed when the round ended.</summary>
        public int Missing;
        public int Stray;
        /// <summary>Server-side simulation of every battle of the round.</summary>
        public long SimMicros;
        /// <summary>Time the match loop spent handling inputs and deadlines this round (includes SimMicros).</summary>
        public long LoopMicros;
        /// <summary>Whole-process CPU this round (Kestrel, sockets, GC and the match loop).</summary>
        public long CpuMicros;
        public long BytesPerClientAvg;
        public long BytesPerClientMax;
        /// <summary>The BattleRound frame alone (every board and seed of the round).</summary>
        public int BattleRoundBytes;

        public string Describe() =>
            $"round {Round}: {Battles} battles, hashes {Matched} ok / {Mismatched} mismatch / {Missing} missing; " +
            $"sim {SimMicros / 1000.0:0.0} ms, loop {LoopMicros / 1000.0:0.0} ms, cpu {CpuMicros / 1000.0:0.0} ms; " +
            $"down {BytesPerClientAvg} B/client avg, {BytesPerClientMax} max (BattleRound {BattleRoundBytes} B)";
    }

    public readonly List<RoundStats> Rounds = new();
    public RoundStats Current { get; private set; } = new();
    public long LoopMicros;
    public int Commands;
    public int RejectedCommands;
    public int Disconnects;
    public int Rejoins;

    private long loopAtRoundStart;
    private TimeSpan cpuAtRoundStart;

    public int Matched => Rounds.Sum(r => r.Matched) + (Rounds.Contains(Current) ? 0 : Current.Matched);
    public int Mismatched => Rounds.Sum(r => r.Mismatched) + (Rounds.Contains(Current) ? 0 : Current.Mismatched);

    public void BeginRound(int round)
    {
        Current = new RoundStats { Round = round };
        loopAtRoundStart = LoopMicros;
        cpuAtRoundStart = Process.GetCurrentProcess().TotalProcessorTime;
    }

    public void EndRound(IReadOnlyList<Seat> seats)
    {
        Current.LoopMicros = LoopMicros - loopAtRoundStart;
        Current.CpuMicros = (long)(Process.GetCurrentProcess().TotalProcessorTime - cpuAtRoundStart).TotalMicroseconds;
        var bytes = seats.Select(s => s.BytesSent - s.BytesSentAtRoundStart).ToList();
        Current.BytesPerClientAvg = bytes.Count == 0 ? 0 : (long)bytes.Average();
        Current.BytesPerClientMax = bytes.Count == 0 ? 0 : bytes.Max();
        Rounds.Add(Current);
    }

    public string Summary() =>
        $"{Rounds.Count} rounds, hashes {Matched} ok / {Mismatched} mismatch, " +
        $"{Commands} commands ({RejectedCommands} rejected), {Disconnects} disconnects, {Rejoins} rejoins";
}
