#nullable enable
using System.Diagnostics;

namespace Automatic.Net.Client
{
    /// <summary>
    /// Server clock estimate (design/01 §3.3): every Pong gives offset = server + rtt / 2 - local, and the
    /// sample with the smallest round trip wins, NTP style. Local time is a monotonic stopwatch.
    /// </summary>
    public sealed class ClockSync
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long bestRtt = long.MaxValue;

        public long Offset { get; private set; }
        public int Samples { get; private set; }
        public long BestRttMs => bestRtt;

        public long LocalMs => clock.ElapsedMilliseconds;
        public long ServerNowMs => LocalMs + Offset;

        /// <summary>Before the first Pong: any server timestamp is a rough first guess.</summary>
        public void Hint(long serverMs)
        {
            if (Samples == 0) Offset = serverMs - LocalMs;
        }

        public void OnPong(long sentLocalMs, long serverMs)
        {
            long now = LocalMs;
            long rtt = now - sentLocalMs;
            if (rtt < 0) return;
            Samples++;
            if (rtt > bestRtt) return;
            bestRtt = rtt;
            Offset = serverMs + rtt / 2 - now;
        }
    }
}
