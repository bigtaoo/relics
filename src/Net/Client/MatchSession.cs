#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Automatic.Net.Battles;
using Automatic.Net.Protocol;

namespace Automatic.Net.Client
{
    /// <summary>
    /// Client side of a match: applies server messages to a local view, runs the round's battles on the
    /// server timeline and reports their end hashes (design/01). Single-threaded: the owner calls Pump()
    /// every frame (Unity Update, or a bot loop); the network runs in MatchConnection.
    /// A reconnect is just a new connection with the resume token: the server answers with MatchState,
    /// which carries the running BattleRound, and the next Pump fast-forwards it (design/01 §5).
    /// </summary>
    public sealed class MatchSession
    {
        private const int PingEveryMs = 2000;

        public readonly Uri Server;
        public readonly string Name;
        public readonly ClockSync Clock = new ClockSync();
        /// <summary>Headless clients skip the presentation event stream; a game client keeps it.</summary>
        public bool RecordEvents;

        public MatchConnection? Connection { get; private set; }
        public int PlayerId { get; private set; } = -1;
        /// <summary>From Welcome. A game client persists it to rejoin after a restart (set it before ConnectAsync).</summary>
        public string ResumeToken { get; set; } = "";
        public Phase Phase { get; private set; }
        public int Round { get; private set; }
        public long PhaseEndsMs { get; private set; }
        public PlayerState? Self { get; private set; }
        public readonly SortedDictionary<int, PlayerInfo> Players = new SortedDictionary<int, PlayerInfo>();
        public RoundSim? Battle { get; private set; }
        public MatchEnd? End { get; private set; }

        /// <summary>A new preparation phase began (PrepStart, or MatchState in the prep phase after a rejoin).</summary>
        public event Action<MatchSession>? PrepStarted;
        public event Action<MatchSession, CommandResult>? CommandAnswered;
        public event Action<MatchSession, RoundSim>? BattleStarted;
        public event Action<MatchSession, RoundResult>? RoundEnded;

        public int Joins { get; private set; }
        public int Reports { get; private set; }
        /// <summary>Own hash differs from the server's outcome (the server's result stands, design/01 §6).</summary>
        public int LocalMismatches { get; private set; }
        /// <summary>Over every connection of this session, the live one included.</summary>
        public long BytesReceived => droppedReceived + (Connection?.BytesReceived ?? 0);
        public long BytesSent => droppedSent + (Connection?.BytesSent ?? 0);

        private readonly Dictionary<long, ulong> ownHashes = new Dictionary<long, ulong>();
        private readonly List<int> finished = new List<int>();
        private int nextSeq = 1;
        private long droppedReceived, droppedSent;
        private long nextPingAt;

        public MatchSession(Uri server, string name)
        {
            Server = server;
            Name = name;
        }

        public bool Connected => Connection != null && !Connection.Closed;

        /// <summary>Connect (or reconnect with the resume token) and send Join.</summary>
        public async Task ConnectAsync(CancellationToken ct)
        {
            DropConnection();
            Connection = await MatchConnection.ConnectAsync(Server, ct).ConfigureAwait(false);
            await Connection.SendAsync(new ClientMessage { Join = new Join { Name = Name, ResumeToken = ResumeToken } })
                .ConfigureAwait(false);
            for (int i = 0; i < 3; i++) SendPing();
            nextPingAt = Clock.LocalMs + PingEveryMs;
        }

        /// <summary>Simulate a dead network: the socket goes away without a close handshake.</summary>
        public void Kill()
        {
            Connection?.Abort();
            DropConnection();
        }

        public int Send(Command command)
        {
            command.Seq = nextSeq++;
            Connection?.Send(new ClientMessage { Command = command });
            return command.Seq;
        }

        public int Buy(int offer) => Send(new Command { Buy = new Buy { Offer = offer } });
        public int Reroll() => Send(new Command { Reroll = new Reroll() });
        public int Move(int slot, int col, int row) => Send(new Command { Move = new Move { Slot = slot, Col = col, Row = row } });

        /// <summary>Apply everything received, then advance the battles to the current server tick.</summary>
        public void Pump()
        {
            var c = Connection;
            if (c != null)
            {
                while (c.TryReceive(out var m)) Apply(m);
                if (!c.Closed && Clock.LocalMs >= nextPingAt)
                {
                    SendPing();
                    nextPingAt = Clock.LocalMs + PingEveryMs;
                }
            }
            if (Battle != null) Advance(Battle.TickAt(Clock.ServerNowMs));
        }

        private void Apply(ServerMessage m)
        {
            Clock.Hint(m.ServerMs);
            switch (m.BodyCase)
            {
                case ServerMessage.BodyOneofCase.Welcome:
                    PlayerId = m.Welcome.PlayerId;
                    ResumeToken = m.Welcome.ResumeToken;
                    Joins++;
                    break;
                case ServerMessage.BodyOneofCase.State:
                    ApplyState(m.State);
                    break;
                case ServerMessage.BodyOneofCase.Prep:
                    Phase = Phase.Prep;
                    Round = m.Prep.Round;
                    PhaseEndsMs = m.Prep.EndsMs;
                    Self = m.Prep.Self;
                    PrepStarted?.Invoke(this);
                    break;
                case ServerMessage.BodyOneofCase.Self:
                    Self = m.Self;
                    break;
                case ServerMessage.BodyOneofCase.Result:
                    CommandAnswered?.Invoke(this, m.Result);
                    break;
                case ServerMessage.BodyOneofCase.Board:
                    if (Players.TryGetValue(m.Board.PlayerId, out var p)) p.Board = m.Board.Board;
                    break;
                case ServerMessage.BodyOneofCase.Battle:
                    StartBattle(m.Battle);
                    break;
                case ServerMessage.BodyOneofCase.RoundResult:
                    EndRound(m.RoundResult);
                    break;
                case ServerMessage.BodyOneofCase.End:
                    Phase = Phase.Ended;
                    End = m.End;
                    break;
                case ServerMessage.BodyOneofCase.Pong:
                    Clock.OnPong(m.Pong.ClientMs, m.ServerMs);
                    break;
            }
        }

        private void ApplyState(MatchState s)
        {
            Phase = s.Phase;
            Round = s.Round;
            PhaseEndsMs = s.PhaseEndsMs;
            Self = s.Self;
            Players.Clear();
            foreach (var p in s.Players) Players[p.Id] = p;
            Battle = null;
            if (s.Battle != null) StartBattle(s.Battle);
            else if (s.Phase == Phase.Prep) PrepStarted?.Invoke(this);
        }

        private void StartBattle(BattleRound r)
        {
            Phase = Phase.Battle;
            Round = r.Round;
            PhaseEndsMs = r.EndsMs;
            Battle = new RoundSim(r, RecordEvents);
            BattleStarted?.Invoke(this, Battle);
        }

        private void EndRound(RoundResult r)
        {
            // The server's end time has passed, so every battle is over on its timeline: finish and report.
            if (Battle != null && Battle.Round.Round == r.Round) Advance(Battle.Round.MaxTicks);
            foreach (var o in r.Outcomes)
                if (ownHashes.TryGetValue(Key(r.Round, o.BattleId), out var h) && h != o.Hash) LocalMismatches++;
            foreach (var p in r.Players) Players[p.Id] = p;
            Battle = null;
            RoundEnded?.Invoke(this, r);
        }

        private void Advance(int tick)
        {
            var b = Battle!;
            b.AdvanceTo(tick);
            finished.Clear();
            b.TakeFinished(finished);
            for (int k = 0; k < finished.Count; k++)
            {
                var spec = b.Round.Battles[finished[k]];
                var battle = b.Battles[finished[k]];
                ulong hash = battle.Hash();
                ownHashes[Key(b.Round.Round, spec.BattleId)] = hash;
                Reports++;
                Connection?.Send(new ClientMessage
                {
                    Report = new BattleReport { Round = b.Round.Round, BattleId = spec.BattleId, Hash = hash, EndTick = battle.Tick },
                });
            }
        }

        private void SendPing() => Connection?.Send(new ClientMessage { Ping = new Ping { ClientMs = Clock.LocalMs } });

        private void DropConnection()
        {
            if (Connection == null) return;
            droppedReceived += Connection.BytesReceived;
            droppedSent += Connection.BytesSent;
            Connection.Dispose();
            Connection = null;
        }

        private static long Key(int round, int battleId) => ((long)round << 32) | (uint)battleId;
    }
}
