namespace Automatic.Server;

/// <summary>Rules and timings of one match. Defaults are the real game; tests shrink the timings.</summary>
public sealed class MatchOptions
{
    public int Seats = 8;
    /// <summary>The match ends after this round even if more than one player is alive.</summary>
    public int MaxRounds = 30;
    public int StartHp = 30;
    public int StartGold = 3;
    public int BaseIncome = 5;
    public int RerollCost = 2;
    public int ShopSize = 5;
    /// <summary>Lobby full -> first preparation phase; gives clients time to sync their clocks.</summary>
    public int StartDelayMs = 500;
    public int PrepMs = 20000;
    /// <summary>Battle tick rate on the shared timeline (design/01 §3.1). Tests raise it to play faster.</summary>
    public int TickHz = 30;
    public int MaxTicks = 900;
    /// <summary>BattleRound is sent this long before tick 0, so clients normally start on time, not catching up.</summary>
    public int BattleLeadMs = 200;
    /// <summary>Extra time after the last possible tick before results are applied.</summary>
    public int ResultGraceMs = 300;
    /// <summary>Fixed seed for shops, pairing and battle seeds (tests); null = cryptographic randomness.</summary>
    public int? Seed;
    public Action<string> Log = Console.WriteLine;
}
