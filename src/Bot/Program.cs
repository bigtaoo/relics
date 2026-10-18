using Automatic.Bot;

// Plays N random bots against a match server and prints what each saw.
//   Automatic.Bot [--url ws://127.0.0.1:9050/match] [--bots 8] [--drop SEAT:ROUND]
var url = new Uri("ws://127.0.0.1:9050/match");
int count = 8, dropBot = -1, dropRound = 0;
for (int i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--url": url = new Uri(args[i + 1]); break;
        case "--bots": count = int.Parse(args[i + 1]); break;
        case "--drop":
            var parts = args[i + 1].Split(':');
            dropBot = int.Parse(parts[0]);
            dropRound = int.Parse(parts[1]);
            break;
        default: Console.Error.WriteLine($"unknown option {args[i]}"); return 2;
    }
}

var bots = Enumerable.Range(0, count).Select(i => new BotPlayer(url, $"bot{i}", 1000 + i)).ToList();
if (dropBot >= 0) bots[dropBot].DropRound = dropRound;
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
await Task.WhenAll(bots.Select(b => Task.Run(() => b.RunAsync(timeout.Token))));

int bad = 0;
foreach (var b in bots)
{
    var s = b.Session;
    bad += s.LocalMismatches;
    string rejoin = b.RejoinedRound is { } r ? $", rejoined round {r.Round.Round} +{r.FastForwardTicks} ticks in {r.FastForwardMicros / 1000.0:0.0} ms" : "";
    Console.WriteLine($"[Bot] {s.Name} seat {s.PlayerId}: {s.Reports} reports, {s.LocalMismatches} mismatches, " +
                      $"{b.CommandsSent} commands ({b.CommandsRejected} rejected), down {s.BytesReceived} B, up {s.BytesSent} B{rejoin}");
}
Console.WriteLine($"[Bot] ranking {string.Join(",", bots[0].Session.End!.Ranking)}");
return bad == 0 ? 0 : 1;
