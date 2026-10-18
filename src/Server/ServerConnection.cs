using System.Net.WebSockets;
using System.Threading.Channels;
using Automatic.Net.Client;
using Automatic.Net.Protocol;
using Google.Protobuf;

namespace Automatic.Server;

/// <summary>
/// One client socket. Reads frames into the match's input queue (answering Ping directly, so clock
/// samples don't wait for the match loop) and writes queued frames from a single writer task.
/// </summary>
public sealed class ServerConnection
{
    private readonly WebSocket socket;
    private readonly Match match;
    private readonly Channel<byte[]> outbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource cts = new();

    public readonly int Id;
    /// <summary>Owned by the match loop.</summary>
    public Seat? Seat;
    public long BytesSent;
    public long BytesReceived;

    public ServerConnection(int id, WebSocket socket, Match match)
    {
        Id = id;
        this.socket = socket;
        this.match = match;
    }

    public void Send(byte[] frame)
    {
        if (outbox.Writer.TryWrite(frame)) Interlocked.Add(ref BytesSent, frame.Length);
    }

    public void Close() => outbox.Writer.TryComplete();

    public async Task RunAsync(CancellationToken aborted)
    {
        using var link = CancellationTokenSource.CreateLinkedTokenSource(aborted, cts.Token);
        var writer = WriteLoop(link.Token);
        try
        {
            await ReadLoop(link.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or InvalidDataException or InvalidProtocolBufferException)
        {
            // Lost client; the match treats every way of going away the same.
        }
        finally
        {
            match.Post(new Match.Left(this));
            outbox.Writer.TryComplete();
            cts.Cancel();
            try { await writer; } catch (Exception) { /* socket already gone */ }
        }
    }

    private async Task ReadLoop(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var r = await socket.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) return;
            message.Write(buffer, 0, r.Count);
            if (message.Length > MatchConnection.MaxMessageBytes) throw new InvalidDataException("message too large");
            if (!r.EndOfMessage) continue;
            Interlocked.Add(ref BytesReceived, message.Length);
            var m = ClientMessage.Parser.ParseFrom(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            if (m.BodyCase == ClientMessage.BodyOneofCase.Ping)
                Send(new ServerMessage { ServerMs = ServerClock.NowMs, Pong = new Pong { ClientMs = m.Ping.ClientMs } }.ToByteArray());
            else
                match.Post(new Match.Received(this, m));
        }
    }

    private async Task WriteLoop(CancellationToken ct)
    {
        await foreach (var frame in outbox.Reader.ReadAllAsync(ct))
            await socket.SendAsync(frame, WebSocketMessageType.Binary, true, ct);
        // Completed by the match (end or replaced seat): close politely.
        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", ct);
            cts.CancelAfter(2000);
        }
    }
}
