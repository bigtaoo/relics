using Automatic.Net.Battles;
using Automatic.Net.Protocol;

namespace Automatic.Server;

/// <summary>
/// The preparation phase, fully server-authoritative (design/01 §2): a placeholder economy until the real
/// one exists. A shop offer names a board slot; buying it raises that piece one star for (star + 1) gold.
/// A disconnected player is left alone (design/01 §5): income still accrues, the board fights as it stands.
/// </summary>
public sealed partial class Match
{
    private void StartPrep()
    {
        Round++;
        Phase = Phase.Prep;
        phaseEndsMs = ServerClock.NowMs + Options.PrepMs;
        deadline = phaseEndsMs;
        foreach (var seat in Seats)
        {
            seat.BytesSentAtRoundStart = seat.BytesSent;
            seat.ReportsThisRound = 0;
            if (!seat.Alive) continue;
            seat.Gold += Options.BaseIncome + Math.Min(seat.Gold / 10, 3);
            RollShop(seat);
        }
        Stats.BeginRound(Round);
        foreach (var seat in Seats)
            Send(seat, new ServerMessage { Prep = new PrepStart { Round = Round, EndsMs = phaseEndsMs, Self = seat.State() } });
    }

    private void RollShop(Seat seat)
    {
        seat.Shop.Clear();
        var open = seat.Board.Pieces.Where(p => p.Star < BattleFactory.MaxStar).Select(p => p.Slot).ToList();
        for (int i = 0; i < Options.ShopSize; i++)
            seat.Shop.Add(open.Count == 0 ? -1 : open[NextInt(open.Count)]);
    }

    private void OnCommand(Seat seat, Command c)
    {
        string error = Phase != Phase.Prep ? "not in preparation"
            : !seat.Alive ? "eliminated"
            : c.ActionCase switch
            {
                Command.ActionOneofCase.Buy => Buy(seat, c.Buy.Offer),
                Command.ActionOneofCase.Reroll => Reroll(seat),
                Command.ActionOneofCase.Move => Move(seat, c.Move),
                _ => "unknown command",
            };
        bool ok = error.Length == 0;
        if (ok) Stats.Commands++;
        else Stats.RejectedCommands++;
        Send(seat, new ServerMessage { Result = new CommandResult { Seq = c.Seq, Ok = ok, Error = error } });
        if (!ok) return;
        Send(seat, new ServerMessage { Self = seat.State() });
        if (c.ActionCase != Command.ActionOneofCase.Reroll)
            Broadcast(new ServerMessage { Board = new PublicBoard { PlayerId = seat.Id, Board = seat.Board.Clone() } });
    }

    private string Buy(Seat seat, int offer)
    {
        if (offer < 0 || offer >= seat.Shop.Count || seat.Shop[offer] < 0) return "no such offer";
        var piece = seat.Piece(seat.Shop[offer]);
        if (piece.Star >= BattleFactory.MaxStar) return "max star";
        int cost = piece.Star + 1;
        if (seat.Gold < cost) return "not enough gold";
        seat.Gold -= cost;
        piece.Star++;
        seat.Shop[offer] = -1;
        return "";
    }

    private string Reroll(Seat seat)
    {
        if (seat.Gold < Options.RerollCost) return "not enough gold";
        seat.Gold -= Options.RerollCost;
        RollShop(seat);
        return "";
    }

    private string Move(Seat seat, Move m)
    {
        if (m.Slot < 0 || m.Slot >= BattleFactory.Slots) return "no such slot";
        if (!BattleFactory.InBounds(m.Col, m.Row)) return "out of bounds";
        foreach (var p in seat.Board.Pieces)
            if (p.Slot != m.Slot && p.Col == m.Col && p.Row == m.Row) return "cell taken";
        var piece = seat.Piece(m.Slot);
        piece.Col = m.Col;
        piece.Row = m.Row;
        return "";
    }
}
