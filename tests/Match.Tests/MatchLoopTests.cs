using Automatic.Bot;
using Automatic.Net.Protocol;
using Automatic.Server;
using Xunit;
using Xunit.Abstractions;

namespace Automatic.Match.Tests;

/// <summary>
/// A whole match over real WebSockets on loopback: one server, eight random bots, one of which loses its
/// socket in the middle of a battle and rejoins. Timings are shrunk (900 Hz ticks: a battle is one second).
/// </summary>
public class MatchLoopTests
{
    private const int DropBot = 3;
    private const int DropRound = 2;

    private readonly ITestOutputHelper output;

    public MatchLoopTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task EightBots_AllHashesMatch_AndADroppedBotRejoinsMidBattle()
    {
        var log = new List<string>();
        var options = new MatchOptions
        {
            Seed = 12345, MaxRounds = 5, StartDelayMs = 300, PrepMs = 400, TickHz = 900, ResultGraceMs = 300,
            Log = line => { lock (log) log.Add(line); },
        };
        await using var server = await MatchServer.StartAsync("http://127.0.0.1:0", options);
        var bots = Enumerable.Range(0, 8).Select(i => new BotPlayer(server.Url, $"bot{i}", 100 + i)).ToList();
        bots[DropBot].DropRound = DropRound;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await Task.WhenAll(bots.Select(b => Task.Run(() => b.RunAsync(timeout.Token))));
        var end = await server.Match.Completion.WaitAsync(timeout.Token);

        lock (log) foreach (var line in log) output.WriteLine(line);
        var stats = server.Match.Stats;
        foreach (var b in bots)
            output.WriteLine($"{b.Session.Name}: {b.Session.Reports} reports, down {b.Session.BytesReceived} B, up {b.Session.BytesSent} B");

        Assert.Equal(8, end.Ranking.Count);
        Assert.InRange(stats.Rounds.Count, 1, options.MaxRounds);
        Assert.Equal(0, stats.Mismatched);
        Assert.All(stats.Rounds, r => Assert.Equal(0, r.Missing));
        // Every connected client reports every battle of every round (4 battles x 8 clients while all live).
        Assert.True(stats.Matched >= stats.Rounds.Sum(r => r.Battles) * 7, $"only {stats.Matched} matching reports");
        Assert.All(bots, b => Assert.Equal(0, b.Session.LocalMismatches));
        Assert.All(bots, b => Assert.True(b.CommandsSent > 0));
        Assert.True(stats.Commands > 0);

        var dropped = bots[DropBot];
        Assert.Equal(1, dropped.Dropped);
        Assert.Equal(2, dropped.Session.Joins);
        Assert.Equal(1, stats.Rejoins);
        Assert.Equal(1, stats.Disconnects);
        var rejoined = dropped.RejoinedRound;
        Assert.NotNull(rejoined);
        Assert.Equal(DropRound, rejoined!.Round.Round);
        // It came back after the drop tick, so its first step was a catch-up to the server's tick.
        Assert.True(rejoined.FastForwardTicks >= dropped.DropAtTick, $"fast-forward {rejoined.FastForwardTicks} ticks");
        Assert.True(rejoined.Tick == rejoined.Round.MaxTicks);
        output.WriteLine($"rejoin: +{rejoined.FastForwardTicks} ticks in {rejoined.FastForwardMicros / 1000.0:0.0} ms");
    }

    [Fact]
    public async Task UnknownResumeToken_IsRefused()
    {
        var options = new MatchOptions { Seed = 1, Log = _ => { } };
        await using var server = await MatchServer.StartAsync("http://127.0.0.1:0", options);
        var session = new Automatic.Net.Client.MatchSession(server.Url, "stranger") { ResumeToken = "nope" };
        await session.ConnectAsync(CancellationToken.None);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!session.Connection!.Closed && DateTime.UtcNow < until)
        {
            session.Pump();
            await Task.Delay(10);
        }
        Assert.True(session.Connection.Closed);
        Assert.Equal(-1, session.PlayerId);
        Assert.Equal(Phase.Lobby, server.Match.Phase);
    }
}
