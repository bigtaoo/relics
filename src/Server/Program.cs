using Automatic.Server;

// Hosts one 8-player match and exits when it ends.
//   Automatic.Server [--port 9050] [--seed N] [--rounds N] [--prep-ms N] [--tick-hz N]
var options = new MatchOptions();
int port = 9050;
for (int i = 0; i + 1 < args.Length; i += 2)
{
    int v = int.Parse(args[i + 1]);
    switch (args[i])
    {
        case "--port": port = v; break;
        case "--seed": options.Seed = v; break;
        case "--rounds": options.MaxRounds = v; break;
        case "--prep-ms": options.PrepMs = v; break;
        case "--tick-hz": options.TickHz = v; break;
        default: Console.Error.WriteLine($"unknown option {args[i]}"); return 2;
    }
}

await using var server = await MatchServer.StartAsync($"http://127.0.0.1:{port}", options);
Console.WriteLine($"[Server] match on {server.Url}");
await server.Match.Completion;
return 0;
