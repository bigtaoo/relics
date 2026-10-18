using Automatic.Net.Battles;
using Automatic.Net.Client;
using Automatic.Net.Protocol;

namespace Automatic.Bot;

/// <summary>
/// A headless player on the same client code a game client uses (MatchSession). Plays the preparation
/// phase at random, one command in flight at a time, and simulates and reports every battle. Optionally
/// drops its socket in the middle of one battle and comes back with its resume token (design/01 §5).
/// </summary>
public sealed class BotPlayer
{
    public readonly MatchSession Session;
    /// <summary>Round in which to kill the connection mid-battle; 0 = never.</summary>
    public int DropRound;
    /// <summary>Kill once the local battle clock passes this tick.</summary>
    public int DropAtTick = 100;
    public int ReconnectDelayMs = 300;
    public int ActionsPerPrep = 6;

    /// <summary>The round the bot rejoined into (null if it never dropped), read after the match.</summary>
    public RoundSim? RejoinedRound { get; private set; }
    public int Dropped { get; private set; }
    public int CommandsSent { get; private set; }
    public int CommandsRejected { get; private set; }

    private readonly Random random;
    private int inFlight = -1;
    private int actionsLeft;

    public BotPlayer(Uri server, string name, int seed)
    {
        Session = new MatchSession(server, name);
        random = new Random(seed);
        Session.PrepStarted += _ => actionsLeft = ActionsPerPrep;
        Session.CommandAnswered += (_, r) =>
        {
            if (r.Seq != inFlight) return;
            inFlight = -1;
            if (!r.Ok) CommandsRejected++;
        };
        Session.BattleStarted += (s, round) =>
        {
            inFlight = -1;
            if (Dropped > 0 && round.Round.Round == DropRound && RejoinedRound == null) RejoinedRound = round;
        };
    }

    /// <summary>Play until the match ends (or `ct` fires).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        await Session.ConnectAsync(ct);
        while (Session.Phase != Phase.Ended)
        {
            ct.ThrowIfCancellationRequested();
            Session.Pump();
            if (ShouldDrop())
            {
                Session.Kill();
                Dropped++;
                inFlight = -1;
                await Task.Delay(ReconnectDelayMs, ct);
                await Session.ConnectAsync(ct);
                continue;
            }
            if (Session.Connection is { Closed: true } closed)
            {
                Session.Pump(); // the match end may have arrived just before the close
                if (Session.Phase != Phase.Ended)
                    throw new InvalidOperationException($"{Session.Name}: connection lost: {closed.CloseReason}");
                break;
            }
            if (Session.Phase == Phase.Prep) Act();
            await Task.Delay(5, ct);
        }
    }

    private bool ShouldDrop() =>
        DropRound > 0 && Dropped == 0 && Session.Phase == Phase.Battle && Session.Round == DropRound
        && Session.Battle != null && Session.Battle.Tick >= DropAtTick;

    private void Act()
    {
        var self = Session.Self;
        if (self == null || inFlight >= 0 || actionsLeft <= 0) return;
        actionsLeft--;
        CommandsSent++;
        var buyable = new List<int>();
        for (int i = 0; i < self.Shop.Count; i++)
        {
            int slot = self.Shop[i];
            if (slot < 0) continue;
            var piece = PieceOf(self.Board, slot);
            if (piece != null && piece.Star < BattleFactory.MaxStar && piece.Star + 1 <= self.Gold) buyable.Add(i);
        }
        int roll = random.Next(10);
        if (buyable.Count > 0 && roll < 6)
            inFlight = Session.Buy(buyable[random.Next(buyable.Count)]);
        else if (self.Gold >= 2 && roll < 7)
            inFlight = Session.Reroll();
        else
            inFlight = Session.Move(random.Next(BattleFactory.Slots), random.Next(BattleFactory.Cols), random.Next(BattleFactory.Rows));
    }

    private static Piece? PieceOf(Board board, int slot)
    {
        foreach (var p in board.Pieces)
            if (p.Slot == slot) return p;
        return null;
    }
}
