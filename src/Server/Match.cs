using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Channels;
using Automatic.Net.Protocol;
using Google.Protobuf;

namespace Automatic.Server;

/// <summary>
/// One 8-player match (design/01). All state is owned by a single loop that drains an input queue and
/// fires phase deadlines, so nothing here needs a lock. Phases: lobby -> (prep -> battle) x rounds -> ended.
/// Preparation is server-authoritative (Match.Prep.cs); battles are simulated here and on every client
/// from the same BattleRound, and the client hashes are checked against ours (Match.Battle.cs).
/// </summary>
public sealed partial class Match
{
    public abstract record Input;
    public sealed record Received(ServerConnection Connection, ClientMessage Message) : Input;
    public sealed record Left(ServerConnection Connection) : Input;

    public readonly MatchOptions Options;
    public readonly MatchStats Stats = new();
    public readonly List<Seat> Seats = new();
    public Phase Phase { get; private set; } = Phase.Lobby;
    public int Round { get; private set; }

    private readonly Channel<Input> inputs = Channel.CreateUnbounded<Input>();
    private readonly TaskCompletionSource<MatchEnd> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Random? seeded;
    private long deadline = long.MaxValue;
    private long phaseEndsMs;

    public Match(MatchOptions options)
    {
        Options = options;
        if (options.Seed is int s) seeded = new Random(s);
    }

    public Task<MatchEnd> Completion => completion.Task;

    public void Post(Input input) => inputs.Writer.TryWrite(input);

    public async Task RunAsync(CancellationToken ct)
    {
        var busy = new Stopwatch();
        try
        {
            while (Phase != Phase.Ended)
            {
                long wait = deadline - ServerClock.NowMs;
                var ready = inputs.Reader.WaitToReadAsync(ct).AsTask();
                if (wait > 0 && !ready.IsCompleted)
                    await Task.WhenAny(ready, Task.Delay(TimeSpan.FromMilliseconds(Math.Min(wait, int.MaxValue)), ct));
                ct.ThrowIfCancellationRequested();
                busy.Restart();
                while (inputs.Reader.TryRead(out var input)) Handle(input);
                if (ServerClock.NowMs >= deadline) OnDeadline();
                Stats.LoopMicros += busy.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            completion.TrySetCanceled(ct);
        }
        catch (Exception e)
        {
            completion.TrySetException(e);
            throw;
        }
    }

    private void Handle(Input input)
    {
        switch (input)
        {
            case Received { Message.BodyCase: ClientMessage.BodyOneofCase.Join } r:
                OnJoin(r.Connection, r.Message.Join);
                break;
            case Received r when r.Connection.Seat is { } seat && seat.Connection == r.Connection:
                if (r.Message.Command != null) OnCommand(seat, r.Message.Command);
                else if (r.Message.Report != null) OnReport(seat, r.Message.Report);
                break;
            case Left l when l.Connection.Seat is { } seat && seat.Connection == l.Connection:
                seat.Connection = null;
                Stats.Disconnects++;
                Options.Log($"[Match] seat {seat.Id} disconnected ({Phase} round {Round})");
                break;
        }
    }

    private void OnDeadline()
    {
        deadline = long.MaxValue;
        switch (Phase)
        {
            case Phase.Lobby: StartPrep(); break;
            case Phase.Prep: StartBattle(); break;
            case Phase.Battle: EndRound(); break;
        }
    }

    private void OnJoin(ServerConnection c, Join join)
    {
        Seat? seat;
        bool resume = join.ResumeToken.Length > 0;
        if (resume)
        {
            seat = Seats.Find(s => s.Token == join.ResumeToken);
        }
        else if (Phase == Phase.Lobby && Seats.Count < Options.Seats)
        {
            seat = new Seat(Seats.Count, Convert.ToHexString(RandomNumberGenerator.GetBytes(16))) { Name = join.Name };
            seat.Hp = Options.StartHp;
            seat.Gold = Options.StartGold;
            Seats.Add(seat);
        }
        else seat = null;
        if (seat == null)
        {
            Options.Log($"[Match] connection {c.Id} refused (resume={resume}, {Phase})");
            c.Close();
            return;
        }
        // A seat has one connection: the newest wins (the old one is usually a dead socket nobody closed yet).
        if (seat.Connection != null && seat.Connection != c) seat.Connection.Close();
        seat.Connection = c;
        c.Seat = seat;
        if (resume)
        {
            seat.Rejoins++;
            Stats.Rejoins++;
            Options.Log($"[Match] seat {seat.Id} rejoined ({Phase} round {Round})");
        }
        Send(seat, new ServerMessage { Welcome = new Welcome { PlayerId = seat.Id, ResumeToken = seat.Token, Seats = Options.Seats } });
        Send(seat, new ServerMessage { State = StateFor(seat) });
        if (Phase == Phase.Lobby && Seats.Count == Options.Seats && deadline == long.MaxValue)
            deadline = ServerClock.NowMs + Options.StartDelayMs;
    }

    /// <summary>Everything a (re)joining client needs (design/01 §5): phase, everyone's public info, own private state, running battles.</summary>
    private MatchState StateFor(Seat seat)
    {
        var s = new MatchState { Phase = Phase, Round = Round, PhaseEndsMs = phaseEndsMs, Self = seat.State() };
        foreach (var other in Seats) s.Players.Add(other.Info());
        if (Phase == Phase.Battle) s.Battle = currentRound;
        return s;
    }

    private void EndMatch()
    {
        var ranking = Seats.Where(s => s.Alive).OrderByDescending(s => s.Hp).ThenBy(s => s.Id).ToList();
        for (int i = 0; i < ranking.Count; i++) ranking[i].Place = i + 1;
        ranking.AddRange(Seats.Where(s => !ranking.Contains(s)).OrderBy(s => s.Place));
        var end = new MatchEnd();
        end.Ranking.AddRange(ranking.Select(s => s.Id));
        Phase = Phase.Ended;
        Broadcast(new ServerMessage { End = end });
        foreach (var s in Seats) s.Connection?.Close();
        Options.Log($"[Match] ended after round {Round}: ranking {string.Join(",", end.Ranking)}; {Stats.Summary()}");
        completion.TrySetResult(end);
    }

    private void Send(Seat seat, ServerMessage m)
    {
        if (seat.Connection == null) return;
        m.ServerMs = ServerClock.NowMs;
        var frame = m.ToByteArray();
        seat.BytesSent += frame.Length;
        seat.Connection.Send(frame);
    }

    /// <summary>Serialized once, the same frame to every connected seat (spectators included). Returns its size.</summary>
    private int Broadcast(ServerMessage m)
    {
        m.ServerMs = ServerClock.NowMs;
        var frame = m.ToByteArray();
        foreach (var seat in Seats)
        {
            if (seat.Connection == null) continue;
            seat.BytesSent += frame.Length;
            seat.Connection.Send(frame);
        }
        return frame.Length;
    }

    private int NextInt(int exclusiveMax) =>
        seeded?.Next(exclusiveMax) ?? RandomNumberGenerator.GetInt32(exclusiveMax);

    private uint NextSeed()
    {
        if (seeded != null) return (uint)seeded.NextInt64(0, 1L << 32);
        Span<byte> b = stackalloc byte[4];
        RandomNumberGenerator.Fill(b);
        return BitConverter.ToUInt32(b);
    }
}
