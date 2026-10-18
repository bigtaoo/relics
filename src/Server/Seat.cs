using Automatic.Net.Battles;
using Automatic.Net.Protocol;

namespace Automatic.Server;

/// <summary>One player of the match. Lives as long as the match, across reconnects. Match loop only.</summary>
public sealed class Seat
{
    public readonly int Id;
    public readonly string Token;
    public string Name = "";
    public ServerConnection? Connection;

    public int Hp;
    /// <summary>0 while alive, else the final place.</summary>
    public int Place;
    public int Gold;
    public readonly List<int> Shop = new();
    public Board Board = BattleFactory.DefaultBoard();

    /// <summary>Bytes sent to this seat over all its connections (match loop sends only).</summary>
    public long BytesSent;
    public long BytesSentAtRoundStart;
    public int Rejoins;
    public int ReportsThisRound;

    public Seat(int id, string token)
    {
        Id = id;
        Token = token;
    }

    public bool Alive => Place == 0;
    public bool Connected => Connection != null;

    public Piece Piece(int slot)
    {
        foreach (var p in Board.Pieces)
            if (p.Slot == slot) return p;
        throw new ArgumentOutOfRangeException(nameof(slot));
    }

    public PlayerState State()
    {
        var s = new PlayerState { Gold = Gold, Board = Board.Clone() };
        s.Shop.AddRange(Shop);
        return s;
    }

    public PlayerInfo Info() => new()
    {
        Id = Id, Name = Name, Hp = Hp, Connected = Connected, Board = Board.Clone(), Place = Place,
    };
}
