#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Automatic.Net.Protocol;
using Google.Protobuf;

namespace Automatic.Net.Client
{
    /// <summary>
    /// One WebSocket to the match server (ADR-013). Receives on a background task into a queue that the
    /// owner drains on its own thread (Unity: the main thread), so no game state is touched off-thread.
    /// Sends are serialized through a single writer.
    /// </summary>
    public sealed class MatchConnection : IDisposable
    {
        public const int MaxMessageBytes = 256 * 1024;

        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly ConcurrentQueue<ServerMessage> inbox = new ConcurrentQueue<ServerMessage>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        public long BytesReceived;
        public long BytesSent;
        public int MessagesReceived;
        /// <summary>Set once the socket is gone: closed by either side, aborted or failed.</summary>
        public volatile bool Closed;
        public string? CloseReason;

        public static async Task<MatchConnection> ConnectAsync(Uri uri, CancellationToken ct)
        {
            var c = new MatchConnection();
            await c.socket.ConnectAsync(uri, ct).ConfigureAwait(false);
            _ = c.ReceiveLoop();
            return c;
        }

        public bool TryReceive(out ServerMessage message) => inbox.TryDequeue(out message!);

        /// <summary>Fire and forget; failures close the connection.</summary>
        public void Send(ClientMessage message) => _ = SendAsync(message);

        public async Task SendAsync(ClientMessage message)
        {
            if (Closed) return;
            var bytes = message.ToByteArray();
            await sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, cts.Token)
                    .ConfigureAwait(false);
                Interlocked.Add(ref BytesSent, bytes.Length);
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
            finally
            {
                sendLock.Release();
            }
        }

        /// <summary>Drop the connection without a close handshake, like a lost network or a killed app.</summary>
        public void Abort()
        {
            Fail("aborted");
            socket.Abort();
        }

        public void Dispose()
        {
            Fail("disposed");
            socket.Dispose();
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[16 * 1024];
            var message = new MemoryStream();
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        Fail("closed by server");
                        return;
                    }
                    message.Write(buffer, 0, r.Count);
                    if (message.Length > MaxMessageBytes) throw new InvalidDataException("message too large");
                    if (!r.EndOfMessage) continue;
                    Interlocked.Add(ref BytesReceived, message.Length);
                    Interlocked.Increment(ref MessagesReceived);
                    inbox.Enqueue(ServerMessage.Parser.ParseFrom(message.GetBuffer(), 0, (int)message.Length));
                    message.SetLength(0);
                }
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        private void Fail(string reason)
        {
            if (Closed) return;
            CloseReason = reason;
            Closed = true;
            cts.Cancel();
        }
    }
}
