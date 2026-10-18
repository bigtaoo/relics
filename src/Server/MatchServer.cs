using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Automatic.Server;

/// <summary>
/// Kestrel hosting one match on a WebSocket endpoint, /match (ADR-013). Used by Program and by the tests,
/// which bind port 0 and read the actual address back from Url.
/// </summary>
public sealed class MatchServer : IAsyncDisposable
{
    public const string Path = "/match";

    public readonly Match Match;
    public readonly Uri Url;
    private readonly WebApplication app;
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;

    private MatchServer(WebApplication app, Match match, Uri url)
    {
        this.app = app;
        Match = match;
        Url = url;
        loop = Task.Run(() => match.RunAsync(stop.Token));
    }

    /// <summary>`listen` like "http://127.0.0.1:9050"; port 0 picks a free one.</summary>
    public static async Task<MatchServer> StartAsync(string listen, MatchOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(listen);
        var app = builder.Build();
        var match = new Match(options);
        int nextId = 0;
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });
        app.Map(Path, async (HttpContext http) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            var c = new ServerConnection(Interlocked.Increment(ref nextId), socket, match);
            await c.RunAsync(http.RequestAborted);
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var url = new UriBuilder(address) { Scheme = "ws", Path = Path }.Uri;
        return new MatchServer(app, match, url);
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { await loop; } catch (OperationCanceledException) { }
        await app.StopAsync();
        await app.DisposeAsync();
        stop.Dispose();
    }
}
