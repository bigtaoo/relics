using System.Diagnostics;

namespace Automatic.Server;

/// <summary>Monotonic server time in Unix milliseconds: wall clock at start plus a stopwatch.</summary>
public static class ServerClock
{
    private static readonly long StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    public static long NowMs => StartMs + Watch.ElapsedMilliseconds;
}
