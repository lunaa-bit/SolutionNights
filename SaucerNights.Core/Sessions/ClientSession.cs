using System.Security.Cryptography;
using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Crypto;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Sessions;

/// <summary>A table as seen in the lobby.</summary>
public sealed class TableInfo
{
    public ushort TableId;
    public string HostId = string.Empty;
    public GameKind Game;
    public string Title = string.Empty;
    public string HostName = string.Empty;
    public byte SeatsTaken;
    public byte SeatsMax;
    public TableFlags Flags;
    public byte[] Rules = [];
    public byte Version;
    public DateTime LastSeen;

    public BlackjackRules? BlackjackRules => Game == GameKind.Blackjack && Rules.Length > 0 ? Blackjack.BlackjackRules.FromBytes(Rules) : null;

    public PokerRules? PokerRules => Game == GameKind.Poker && Rules.Length > 0 ? Poker.PokerRules.FromBytes(Rules) : null;

    public bool Compatible => Version == ProtocolInfo.Version;

    public string StakesText => Game == GameKind.Blackjack
        ? BlackjackRules is { } b ? $"{b.MinBet:N0} - {b.MaxBet:N0}" : "?"
        : PokerRules is { } p ? p.StakesText : "?";

    public static TableInfo From(AnnounceMsg a, ushort tableId, string hostId, DateTime now) => new()
    {
        TableId = tableId,
        HostId = hostId,
        Game = a.Game,
        Title = a.Title,
        HostName = a.HostName,
        SeatsTaken = a.SeatsTaken,
        SeatsMax = a.SeatsMax,
        Flags = a.Flags,
        Rules = a.Rules,
        Version = a.Version,
        LastSeen = now,
    };
}

public enum ClientStatus
{
    Spectating,
    Joining,
    Seated,
    Rejected,
    Closed,
    Left,
}

/// <summary>A new snapshot arrived; <see cref="Previous"/> is what was shown before.</summary>
public sealed record StateUpdate(StateMsg? Previous, StateMsg Current, IReadOnlyList<TableEvent> Events, DateTime At);

/// <summary>
/// A player's (or spectator's) view of one table. Holds the latest snapshot, opens this player's sealed hole cards,
/// sends moves with retries, contributes shuffle seeds and verifies each finished round.
/// </summary>
public sealed class ClientSession : IDisposable
{
    private sealed class Pending
    {
        public required IMessage Msg;
        public DateTime SentAt;
        public int Attempts;
    }

    private readonly IClock _clock;
    private readonly SessionKeys _keys = new();
    private readonly List<IMessage> _out = [];
    private readonly Dictionary<long, byte[]> _commits = new();
    private readonly Dictionary<long, byte[]> _mySeeds = new();
    private Pending? _pendingAction;
    private Pending? _pendingBet;
    private byte[]? _sharedKey;
    private uint _nonce;
    private DateTime _joinSentAt;
    private int _joinAttempts;
    private DateTime _lastResync;
    private DateTime _lastRekey;
    private long _readySentFor = -1;
    private long _verifiedRound = -1;
    private int _verifiedOpen = -1;
    private uint _seq;

    public ClientSession(TableInfo table, string selfId, IClock clock)
    {
        Table = table;
        SelfId = selfId;
        _clock = clock;
        LastHostFrame = clock.Now;
    }

    public TableInfo Table { get; }

    public string SelfId { get; }

    public ClientStatus Status { get; private set; } = ClientStatus.Spectating;

    public int MySeat { get; private set; } = -1;

    public RejectReason? RejectedFor { get; private set; }

    public string? ClosedText { get; private set; }

    public RosterMsg Roster { get; private set; } = new() { Version = 0 };

    public StateMsg? State { get; private set; }

    public DateTime StateAt { get; private set; }

    public DateTime LastHostFrame { get; private set; }

    public BlackjackView? Blackjack => State?.Blackjack;

    public PokerView? Poker => State?.Poker;

    /// <summary>This player's hole cards for <see cref="HoleHand"/>.</summary>
    public Card[] Hole { get; private set; } = [];

    public long HoleHand { get; private set; } = -1;

    public List<FairnessResult> Fairness { get; } = [];

    public bool IsSeated => Status == ClientStatus.Seated && MySeat >= 0;

    public bool AwaitingHost => _pendingAction != null || _pendingBet != null;

    public event Action<StateUpdate>? Updated;

    public event Action<string>? Notice;

    public string NameAt(int seat) => Roster.BySeat(seat)?.Name ?? $"Seat {seat + 1}";

    /// <summary>Seconds left on the current deadline, counting down locally between snapshots.</summary>
    public double SecondsLeft(DateTime now)
    {
        var sec = Blackjack?.DeadlineSec ?? Poker?.DeadlineSec ?? 255;
        if (sec == 255) return double.NaN;
        return Math.Max(0, sec - (now - StateAt).TotalSeconds);
    }

    // ───────────── Commands ─────────────

    public void Join(int preferredSeat = 255)
    {
        if (Status is ClientStatus.Seated or ClientStatus.Closed) return;
        _nonce = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        Status = ClientStatus.Joining;
        RejectedFor = null;
        _joinAttempts = 0;
        _joinSentAt = _clock.Now;
        Enqueue(new JoinMsg { Nonce = _nonce, PublicKey = _keys.PublicKey, PreferredSeat = (byte)Math.Clamp(preferredSeat, 0, 255) });
    }

    public void Leave()
    {
        if (Status is ClientStatus.Seated or ClientStatus.Joining) Enqueue(new LeaveMsg());
        Status = ClientStatus.Left;
        MySeat = -1;
    }

    public void PlaceBet(long amount)
    {
        if (!IsSeated || Blackjack is not { Phase: BjPhase.Betting } v) return;
        var seed = Fair.NewClientSeed();
        _mySeeds[v.Round] = seed;
        var msg = new BetMsg { Round = v.Round, Amount = amount, Seed = seed };
        _pendingBet = new Pending { Msg = msg, SentAt = _clock.Now };
        Enqueue(msg);
    }

    public void Act(byte kind, long amount = 0, byte hand = 0)
    {
        if (!IsSeated || State == null) return;
        var (round, turn) = Blackjack is { } b ? (b.Round, b.TurnId) : (Poker!.Hand, Poker.TurnId);
        var msg = new ActionMsg { Round = round, TurnId = turn, Kind = kind, Amount = amount, Hand = hand };
        _pendingAction = new Pending { Msg = msg, SentAt = _clock.Now };
        Enqueue(msg);
    }

    /// <summary>Poker: true if the hand is over and the player still holds cards nobody has seen.</summary>
    public bool CanShowCards =>
        IsSeated && Poker is { Phase: PokerPhase.Showdown } v && (v.DealtMask & (1 << MySeat)) != 0
        && HoleHand == v.Hand && Hole.Length == 2 && v.Seat(MySeat) is { Shown.Count: 0 };

    /// <summary>Poker: turn the player's hole cards face up for the table once the hand is over.</summary>
    public void ShowCards()
    {
        if (CanShowCards) Act((byte)PokerActionKind.Show);
    }

    public void SitOut(bool sitOut)
    {
        if (IsSeated) Enqueue(new SitOutMsg { SitOut = sitOut });
    }

    public void RequestRebuy()
    {
        if (IsSeated) Enqueue(new RebuyMsg());
    }

    public void RequestResync(ResyncNeed need)
    {
        var now = _clock.Now;
        if (now - _lastResync < TimeSpan.FromSeconds(4)) return;
        _lastResync = now;
        Enqueue(new ResyncMsg { Need = need });
    }

    /// <summary>Queues a message, replacing an older unsent one of the same kind.</summary>
    private void Enqueue(IMessage msg)
    {
        if (msg.Type is MessageType.Action or MessageType.Bet or MessageType.Ready or MessageType.Resync or MessageType.SitOut or MessageType.Join)
            _out.RemoveAll(m => m.Type == msg.Type);
        _out.Add(msg);
    }

    public Frame? NextFrame(DateTime now)
    {
        if (_out.Count == 0) return null;
        var msg = _out[0];
        _out.RemoveAt(0);
        if (msg == _pendingAction?.Msg) _pendingAction.SentAt = now;
        if (msg == _pendingBet?.Msg) _pendingBet.SentAt = now;
        return msg.ToFrame(Table.TableId, ++_seq);
    }

    public int QueuedFrames => _out.Count;

    // ───────────── Inbound ─────────────

    public void OnFrame(string sender, Frame frame, DateTime now)
    {
        if (frame.TableId != Table.TableId || !string.Equals(sender, Table.HostId, StringComparison.OrdinalIgnoreCase)) return;
        var msg = Messages.Parse(frame);
        if (msg == null) return;
        LastHostFrame = now;

        switch (msg)
        {
            case WelcomeMsg w when w.Nonce == _nonce:
                _sharedKey = _keys.TryDerive(w.HostPublicKey);
                MySeat = w.Seat;
                Status = ClientStatus.Seated;
                HoleHand = -1;
                Notice?.Invoke($"You sat down at seat {w.Seat + 1}.");
                if (State?.Poker is { } pv) OpenHole(pv);
                break;
            case RejectMsg r when r.Nonce == _nonce:
                Status = ClientStatus.Rejected;
                RejectedFor = r.Reason;
                Notice?.Invoke(r.Reason switch
                {
                    RejectReason.Full => "The table is full.",
                    RejectReason.Kicked => "The host has removed you from this table.",
                    RejectReason.BadVersion => "Your Saucer Nights version doesn't match the host's.",
                    _ => "The host could not seat you.",
                });
                break;
            case RosterMsg roster:
                Roster = roster;
                if (IsSeated && roster.BySeat(MySeat) == null)
                {
                    Status = ClientStatus.Left;
                    MySeat = -1;
                    Notice?.Invoke("You are no longer seated at this table.");
                }
                break;
            case StateMsg st:
                ApplyState(st, now);
                break;
            case AnnounceMsg a:
                Table.SeatsTaken = a.SeatsTaken;
                Table.Flags = a.Flags;
                Table.Title = a.Title;
                Table.LastSeen = now;
                break;
            case ClosedMsg c:
                Status = ClientStatus.Closed;
                ClosedText = c.Text;
                Notice?.Invoke(c.Text);
                break;
        }
    }

    private void ApplyState(StateMsg st, DateTime now)
    {
        var prev = State;
        State = st;
        StateAt = now;

        if (st.RosterVersion != Roster.Version) RequestResync(ResyncNeed.Roster);

        if (st.Blackjack is { } bj)
        {
            if (bj.Commit != null) _commits[bj.Round] = bj.Commit;
            var mine = IsSeated ? bj.Seat(MySeat) : null;
            if (_pendingBet?.Msg is BetMsg pb && (bj.Phase != BjPhase.Betting || bj.Round != pb.Round || mine?.Bet == pb.Amount))
                _pendingBet = null;
            if (_pendingAction?.Msg is ActionMsg pa
                && (bj.Round != pa.Round || bj.TurnId != pa.TurnId || (bj.Phase == BjPhase.Insurance && mine?.InsuranceState != InsuranceState.Undecided)))
                _pendingAction = null;
            if (bj.Phase == BjPhase.Settled && bj.Reveal != null && bj.Round != _verifiedRound)
            {
                _verifiedRound = bj.Round;
                var decks = Table.BlackjackRules?.Decks ?? 6;
                Record(Verifier.Blackjack(bj, _commits.GetValueOrDefault(bj.Round), Table.TableId, decks, MySeat, _mySeeds.GetValueOrDefault(bj.Round), now));
            }
        }
        else if (st.Poker is { } pk)
        {
            if (pk.Commit != null) _commits[pk.Hand] = pk.Commit;
            if (_pendingAction?.Msg is ActionMsg pa
                && (pk.Hand != pa.Round
                    || (pa.Kind == (byte)PokerActionKind.Show ? pk.Phase != PokerPhase.Showdown || pk.Seat(MySeat) is not { Shown.Count: 0 } : pk.TurnId != pa.TurnId)))
                _pendingAction = null;
            if (pk.Holes.Count > 0) OpenHole(pk);
            if (pk.Phase == PokerPhase.Starting && IsSeated && _readySentFor != pk.Hand)
            {
                _readySentFor = pk.Hand;
                var seed = Fair.NewClientSeed();
                _mySeeds[pk.Hand] = seed;
                Enqueue(new ReadyMsg { Hand = pk.Hand, Seed = seed });
            }
            // Checked again whenever more cards are opened (a player showing their hand after the showdown).
            if (pk.Phase == PokerPhase.Showdown && pk.Reveal != null && (pk.Hand != _verifiedRound || pk.Reveal.OpenCount != _verifiedOpen))
            {
                var again = pk.Hand == _verifiedRound;
                _verifiedRound = pk.Hand;
                _verifiedOpen = pk.Reveal.OpenCount;
                var hole = HoleHand == pk.Hand ? Hole : [];
                var result = Verifier.Poker(pk, _commits.GetValueOrDefault(pk.Hand), Table.TableId, MySeat, _mySeeds.GetValueOrDefault(pk.Hand), hole, now);
                // A hand keeps one entry: a later pass replaces the earlier one unless that one already failed.
                var last = Fairness.Count > 0 && Fairness[^1].Round == pk.Hand ? Fairness[^1] : null;
                if (!again || last == null) Record(result);
                else if (last.Status != FairStatus.Failed)
                {
                    Fairness.RemoveAt(Fairness.Count - 1);
                    Record(result);
                }
            }
        }

        // Forget bookkeeping for old rounds.
        var round = st.Blackjack?.Round ?? st.Poker!.Hand;
        foreach (var k in _commits.Keys.Where(k => k < round - 3).ToList()) _commits.Remove(k);
        foreach (var k in _mySeeds.Keys.Where(k => k < round - 3).ToList()) _mySeeds.Remove(k);

        Updated?.Invoke(new StateUpdate(prev, st, st.Events, now));
    }

    private void Record(FairnessResult r)
    {
        Fairness.Add(r);
        if (Fairness.Count > 50) Fairness.RemoveAt(0);
        if (r.Status == FairStatus.Failed) Notice?.Invoke($"Fairness check FAILED for round {r.Round}: {r.Summary}");
    }

    private void OpenHole(PokerView pk)
    {
        if (!IsSeated || HoleHand == pk.Hand) return;
        var sealedHole = pk.Holes.FirstOrDefault(h => h.Seat == MySeat);
        if (sealedHole == null) return;
        if (_sharedKey != null && CardSeal.TryOpen(_sharedKey, Table.TableId, pk.Hand, MySeat, sealedHole.Data, out var bytes) && bytes.All(b => b < 52))
        {
            Hole = bytes.Select(b => new Card(b)).ToArray();
            HoleHand = pk.Hand;
            return;
        }
        // Keys out of step (e.g. the host restarted): rejoin to exchange keys again.
        var now = _clock.Now;
        if (now - _lastRekey < TimeSpan.FromSeconds(10)) return;
        _lastRekey = now;
        _nonce = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        Enqueue(new JoinMsg { Nonce = _nonce, PublicKey = _keys.PublicKey, PreferredSeat = (byte)MySeat });
    }

    /// <summary>How long without a word from the host before the table is given up for lost.</summary>
    public static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(150);

    public void Tick(DateTime now)
    {
        if (Status is ClientStatus.Seated or ClientStatus.Spectating or ClientStatus.Joining
            && now - LastHostFrame > HostTimeout
            && !string.Equals(Table.HostId, SelfId, StringComparison.OrdinalIgnoreCase))
        {
            Status = ClientStatus.Closed;
            ClosedText = "Lost contact with the host. They may have logged out or left the linkshell.";
            Notice?.Invoke(ClosedText);
            return;
        }
        if (Status == ClientStatus.Joining && now - _joinSentAt > TimeSpan.FromSeconds(7))
        {
            if (++_joinAttempts >= 4)
            {
                Status = ClientStatus.Rejected;
                Notice?.Invoke("The host didn't answer. They may have left, or the linkshell may be busy.");
            }
            else
            {
                _joinSentAt = now;
                Enqueue(new JoinMsg { Nonce = _nonce, PublicKey = _keys.PublicKey, PreferredSeat = 255 });
            }
        }
        Retry(ref _pendingAction, now);
        Retry(ref _pendingBet, now);
    }

    private void Retry(ref Pending? p, DateTime now)
    {
        if (p == null || _out.Contains(p.Msg) || now - p.SentAt < TimeSpan.FromSeconds(5)) return;
        if (++p.Attempts > 2)
        {
            p = null;
            RequestResync(ResyncNeed.State);
            return;
        }
        Enqueue(p.Msg);
    }

    public void Dispose() => _keys.Dispose();
}
