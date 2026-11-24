using System.Security.Cryptography;
using SaucerNights.Core.Crypto;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Sessions;

public sealed class HostOptions
{
    /// <summary>The host's chat identity, "Name@World".</summary>
    public string HostId = string.Empty;

    public string HostName = string.Empty;
    public string Title = "Saucer Nights";
    public bool AutoApproveRebuys = true;
    public TimeSpan AnnounceInterval = TimeSpan.FromSeconds(50);
}

public sealed class TablePlayer
{
    public required string Id;
    public required string Name;
    public int Seat;
    public byte[]? SharedKey;
    public uint Nonce;
    public DateTime JoinedAt;
    public DateTime LastSeen;
}

/// <summary>Running chip account per player, for settling up after the session.</summary>
public sealed class LedgerEntry
{
    public required string Id;
    public required string Name;
    public long BuyIns;
    public long CashedOut;
    public long Current;
    public bool Seated;

    public long Net => CashedOut + Current - BuyIns;
}

public sealed class RebuyRequest
{
    public required string Id;
    public required string Name;
    public int Seat;
    public DateTime At;
}

/// <summary>
/// The host side of a table: admits players, derives their keys, relays their moves to the engine, and produces
/// outgoing frames (lazily, so a rate-limited chat channel always carries the newest snapshot).
/// </summary>
public sealed class HostSession : IDisposable
{
    private readonly IClock _clock;
    private readonly SessionKeys _keys = new();
    private readonly Dictionary<string, TablePlayer> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LedgerEntry> _ledger = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _banned = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<IMessage> _directChat = new();
    private readonly Queue<IMessage> _directLocal = new();
    private readonly List<TableEvent> _eventsChat = [];
    private readonly List<TableEvent> _eventsLocal = [];
    private readonly List<RebuyRequest> _rebuys = [];
    private int _seenVersion = -1;
    private bool _stateDirtyChat = true;
    private bool _stateDirtyLocal = true;
    private bool _rosterDirtyChat = true;
    private bool _rosterDirtyLocal = true;
    private bool _announceRequested = true;
    private DateTime _nextAnnounce;
    private DateTime _lastDiscoverReply;
    private byte _rosterVersion = 1;
    private uint _seqChat;
    private uint _seqLocal;
    private DateTime _lastStateChat;

    public HostSession(ITableGame game, HostOptions options, IClock clock)
    {
        Game = game;
        Options = options;
        _clock = clock;
        TableId = (ushort)RandomNumberGenerator.GetInt32(1, 65536);
        Game.TableId = TableId;
        _nextAnnounce = clock.Now;
    }

    public ITableGame Game { get; }

    public HostOptions Options { get; }

    public ushort TableId { get; }

    public string HostId => Options.HostId;

    public bool Closed { get; private set; }

    /// <summary>True once the "table closed" message has gone out.</summary>
    public bool Finished => Closed && _directChat.Count == 0;

    public IReadOnlyCollection<TablePlayer> Players => _players.Values;

    public IReadOnlyList<RebuyRequest> RebuyRequests => _rebuys;

    /// <summary>Human-readable notes for the host's log.</summary>
    public event Action<string>? Notice;

    public TablePlayer? PlayerAt(int seat) => _players.Values.FirstOrDefault(p => p.Seat == seat);

    public TablePlayer? Player(string id) => _players.GetValueOrDefault(id);

    public List<LedgerEntry> Ledger()
    {
        foreach (var e in _ledger.Values)
        {
            var p = _players.GetValueOrDefault(e.Id);
            e.Seated = p != null;
            e.Current = p != null ? Game.StackOf(p.Seat) : 0;
        }
        return _ledger.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public TableFlags Flags
    {
        get
        {
            var f = TableFlags.None;
            if (!Closed && _players.Count < Game.MaxSeats) f |= TableFlags.Open;
            if (Game.Kind == GameKind.Blackjack ? ((Blackjack.BlackjackEngine)Game).Rules.ProvablyFair : ((Poker.PokerEngine)Game).Rules.ProvablyFair)
                f |= TableFlags.ProvablyFair;
            if (Game is Poker.PokerEngine pe && pe.Rules.HostPlays) f |= TableFlags.HostPlays;
            if (Game.InRound) f |= TableFlags.InProgress;
            if (Game.Paused) f |= TableFlags.Paused;
            return f;
        }
    }

    public AnnounceMsg BuildAnnounce() => new()
    {
        Game = Game.Kind,
        Title = Options.Title,
        HostName = Options.HostName,
        SeatsTaken = (byte)_players.Count,
        SeatsMax = (byte)Game.MaxSeats,
        Flags = Flags,
        Rules = Game.RulesBytes(),
    };

    public TableInfo Info(DateTime now) => TableInfo.From(BuildAnnounce(), TableId, HostId, now);

    // ───────────── Inbound ─────────────

    public void OnFrame(string sender, Frame frame, DateTime now)
    {
        if (Closed) return;
        if (frame.Type != MessageType.Discover && frame.TableId != TableId) return;
        var msg = Messages.Parse(frame);
        if (msg == null) return;

        if (msg is DiscoverMsg)
        {
            if (now - _lastDiscoverReply > TimeSpan.FromSeconds(5))
            {
                _lastDiscoverReply = now;
                _announceRequested = true;
            }
            return;
        }
        if (msg is JoinMsg join)
        {
            HandleJoin(sender, join, now);
            return;
        }

        if (!_players.TryGetValue(sender, out var p))
        {
            // Not seated: spectators may still ask for the roster or a fresh snapshot.
            if (msg is ResyncMsg rs) Resync(null, rs.Need);
            return;
        }
        p.LastSeen = now;

        switch (msg)
        {
            case LeaveMsg:
                Remove(p, now, EventKind.Left);
                break;
            case ActionMsg a:
                Game.OnAction(p.Seat, a, now);
                break;
            case BetMsg b:
                Game.OnBet(p.Seat, b, now);
                break;
            case ReadyMsg r:
                Game.OnReady(p.Seat, r, now);
                break;
            case ResyncMsg rs:
                Resync(p, rs.Need);
                break;
            case RebuyMsg:
                RequestRebuy(p, now);
                break;
            case SitOutMsg so:
                Game.SetSittingOut(p.Seat, so.SitOut, now);
                break;
        }
        Sync();
    }

    private void Resync(TablePlayer? p, ResyncNeed need)
    {
        if (need.HasFlag(ResyncNeed.Roster)) _rosterDirtyChat = true;
        if (need.HasFlag(ResyncNeed.State)) _stateDirtyChat = true;
        if (need.HasFlag(ResyncNeed.Welcome) && p != null) QueueWelcome(p);
    }

    private void HandleJoin(string sender, JoinMsg join, DateTime now)
    {
        if (join.Version != ProtocolInfo.Version)
        {
            Queue(sender, new RejectMsg { Nonce = join.Nonce, Reason = RejectReason.BadVersion });
            return;
        }
        if (_banned.Contains(sender))
        {
            Queue(sender, new RejectMsg { Nonce = join.Nonce, Reason = RejectReason.Kicked });
            return;
        }
        var key = _keys.TryDerive(join.PublicKey);
        if (key == null)
        {
            Queue(sender, new RejectMsg { Nonce = join.Nonce, Reason = RejectReason.BadKey });
            return;
        }

        if (_players.TryGetValue(sender, out var existing))
        {
            // Rejoin after a reload: new key, same seat and chips.
            existing.SharedKey = key;
            existing.Nonce = join.Nonce;
            existing.LastSeen = now;
            QueueWelcome(existing);
            _stateDirtyChat = _stateDirtyLocal = true;
            return;
        }

        var seat = -1;
        if (join.PreferredSeat < Game.MaxSeats && !Game.IsSeated(join.PreferredSeat)) seat = join.PreferredSeat;
        for (var i = 0; seat < 0 && i < Game.MaxSeats; i++)
            if (!Game.IsSeated(i)) seat = i;
        if (seat < 0)
        {
            Queue(sender, new RejectMsg { Nonce = join.Nonce, Reason = RejectReason.Full });
            return;
        }

        var name = DisplayName(sender);
        var p = new TablePlayer { Id = sender, Name = name, Seat = seat, SharedKey = key, Nonce = join.Nonce, JoinedAt = now, LastSeen = now };
        _players[sender] = p;
        var stack = Game.StartingStack;
        if (!_ledger.TryGetValue(sender, out var entry)) _ledger[sender] = entry = new LedgerEntry { Id = sender, Name = name };
        entry.BuyIns += stack;
        Game.Seat(seat, stack, now);
        AddEvent(new TableEvent(EventKind.Joined, (byte)seat, stack));
        RosterChanged();
        QueueWelcome(p);
        Notice?.Invoke($"{name} sat down at seat {seat + 1}.");
        Sync();
    }

    private static string DisplayName(string id) => id;

    private void QueueWelcome(TablePlayer p) =>
        Queue(p.Id, new WelcomeMsg { Nonce = p.Nonce, Seat = (byte)p.Seat, HostPublicKey = _keys.PublicKey });

    private void Queue(string target, IMessage msg)
    {
        if (string.Equals(target, HostId, StringComparison.OrdinalIgnoreCase)) _directLocal.Enqueue(msg);
        else _directChat.Enqueue(msg);
    }

    private void RosterChanged()
    {
        _rosterVersion = (byte)(_rosterVersion == 255 ? 1 : _rosterVersion + 1);
        _rosterDirtyChat = _rosterDirtyLocal = true;
        _stateDirtyChat = _stateDirtyLocal = true;
        _announceRequested = true;
    }

    private void AddEvent(TableEvent e)
    {
        _eventsChat.Add(e);
        _eventsLocal.Add(e);
        _stateDirtyChat = _stateDirtyLocal = true;
    }

    private void Remove(TablePlayer p, DateTime now, EventKind why)
    {
        var chips = Game.Unseat(p.Seat, now);
        _players.Remove(p.Id);
        _rebuys.RemoveAll(r => r.Id == p.Id);
        if (_ledger.TryGetValue(p.Id, out var e)) e.CashedOut += chips;
        AddEvent(new TableEvent(why, (byte)p.Seat, chips));
        RosterChanged();
        Notice?.Invoke(why == EventKind.Kicked ? $"{p.Name} was removed from the table." : $"{p.Name} left with {chips:N0} chips.");
        Sync();
    }

    private void RequestRebuy(TablePlayer p, DateTime now)
    {
        if (Game.StackOf(p.Seat) >= Game.StartingStack) return;
        if (Options.AutoApproveRebuys)
        {
            ApplyRebuy(p);
            return;
        }
        if (_rebuys.Any(r => r.Id == p.Id)) return;
        _rebuys.Add(new RebuyRequest { Id = p.Id, Name = p.Name, Seat = p.Seat, At = now });
        Notice?.Invoke($"{p.Name} asked for a rebuy.");
    }

    private void ApplyRebuy(TablePlayer p)
    {
        var amount = Game.StartingStack - Game.StackOf(p.Seat);
        _rebuys.RemoveAll(r => r.Id == p.Id);
        if (amount <= 0) return;
        Game.AddChips(p.Seat, amount);
        if (_ledger.TryGetValue(p.Id, out var e)) e.BuyIns += amount;
        AddEvent(new TableEvent(EventKind.Rebuy, (byte)p.Seat, amount));
        Notice?.Invoke($"{p.Name} rebought for {amount:N0}.");
        Sync();
    }

    // ───────────── Host controls ─────────────

    public void ApproveRebuy(string id)
    {
        if (_players.TryGetValue(id, out var p)) ApplyRebuy(p);
    }

    public void DenyRebuy(string id) => _rebuys.RemoveAll(r => r.Id == id);

    public void Kick(int seat, bool ban)
    {
        var p = PlayerAt(seat);
        if (p == null) return;
        if (ban) _banned.Add(p.Id);
        Remove(p, _clock.Now, EventKind.Kicked);
    }

    /// <summary>Adds (buy-in) or removes (cash-out) chips for a seated player.</summary>
    public void AdjustChips(int seat, long delta)
    {
        var p = PlayerAt(seat);
        if (p == null || delta == 0) return;
        var before = Game.StackOf(seat);
        Game.AddChips(seat, delta);
        var applied = Game.StackOf(seat) - before;
        if (_ledger.TryGetValue(p.Id, out var e))
        {
            if (applied > 0) e.BuyIns += applied;
            else e.CashedOut += -applied;
        }
        AddEvent(new TableEvent(EventKind.ChipsAdjusted, (byte)seat, Math.Abs(applied), applied >= 0 ? (byte)1 : (byte)0));
        Sync();
    }

    public void SetPaused(bool paused)
    {
        Game.Paused = paused;
        _announceRequested = true;
        Sync();
    }

    public void StartNow()
    {
        Game.StartNow(_clock.Now);
        Sync();
    }

    public void Close(string text = "The host closed the table.")
    {
        if (Closed) return;
        foreach (var p in _players.Values.ToList())
        {
            var chips = Game.StackOf(p.Seat);
            if (_ledger.TryGetValue(p.Id, out var e)) e.CashedOut += chips;
        }
        _directChat.Clear();
        _directChat.Enqueue(new ClosedMsg { Reason = CloseReason.HostClosed, Text = text });
        _directLocal.Enqueue(new ClosedMsg { Reason = CloseReason.HostClosed, Text = text });
        Closed = true;
    }

    // ───────────── Outbound ─────────────

    public void Tick(DateTime now)
    {
        if (Closed) return;
        Game.Tick(now);
        Sync();
    }

    /// <summary>Copies new engine events into both outgoing buffers.</summary>
    private void Sync()
    {
        if (Game.Version == _seenVersion) return;
        _seenVersion = Game.Version;
        foreach (var e in Game.DrainEvents())
        {
            _eventsChat.Add(e);
            _eventsLocal.Add(e);
        }
        if (_eventsChat.Count > 64) _eventsChat.RemoveRange(0, _eventsChat.Count - 64);
        if (_eventsLocal.Count > 64) _eventsLocal.RemoveRange(0, _eventsLocal.Count - 64);
        _stateDirtyChat = _stateDirtyLocal = true;
    }

    public RosterMsg BuildRoster() => new()
    {
        Version = _rosterVersion,
        Entries = _players.Values.OrderBy(p => p.Seat)
            .Select(p => new RosterEntry((byte)p.Seat, p.Name,
                (string.Equals(p.Id, HostId, StringComparison.OrdinalIgnoreCase) ? RosterFlags.Host : RosterFlags.None)
                | (Game.IsSittingOut(p.Seat) ? RosterFlags.SittingOut : RosterFlags.None)))
            .ToList(),
    };

    public StateMsg BuildState(DateTime now, List<TableEvent> events)
    {
        var st = Game.BuildState(now);
        st.RosterVersion = _rosterVersion;
        st.Flags = Flags;
        st.Events = events.ToList();
        if (st.Poker is { } pv)
        {
            foreach (var (seat, cards) in Game.PrivateCards())
            {
                if (PlayerAt(seat)?.SharedKey is not { } key) continue;
                pv.Holes.Add(new Poker.SealedHole
                {
                    Seat = (byte)seat,
                    Data = Crypto.CardSeal.Seal(key, TableId, pv.Hand, seat, cards.Select(c => c.Code).ToArray()),
                });
            }
        }
        return st;
    }

    public static readonly TimeSpan HeartbeatActive = TimeSpan.FromSeconds(9);
    public static readonly TimeSpan HeartbeatIdle = TimeSpan.FromSeconds(30);

    /// <summary>Next frame for the linkshell, newest state first after any direct replies.</summary>
    public Frame? NextChatFrame(DateTime now)
    {
        if (_directChat.Count > 0) return _directChat.Dequeue().ToFrame(TableId, ++_seqChat);
        if (Closed) return null;
        Sync();
        if (_rosterDirtyChat)
        {
            _rosterDirtyChat = false;
            return BuildRoster().ToFrame(TableId, ++_seqChat);
        }
        // Heartbeat: a lost line must never stall the table, so the current snapshot is repeated when things go quiet.
        if (!_stateDirtyChat && _players.Count > 0 && now - _lastStateChat > (Game.InRound || Game.AwaitingPlayers ? HeartbeatActive : HeartbeatIdle))
            _stateDirtyChat = true;
        if (_stateDirtyChat)
        {
            _stateDirtyChat = false;
            _lastStateChat = now;
            var st = BuildState(now, _eventsChat);
            _eventsChat.Clear();
            return st.ToFrame(TableId, ++_seqChat);
        }
        if (_announceRequested || now >= _nextAnnounce)
        {
            _announceRequested = false;
            _nextAnnounce = now + Options.AnnounceInterval;
            return BuildAnnounce().ToFrame(TableId, ++_seqChat);
        }
        return null;
    }

    /// <summary>Next frame for the host's own in-process view (not rate limited).</summary>
    public Frame? NextLocalFrame(DateTime now)
    {
        if (_directLocal.Count > 0) return _directLocal.Dequeue().ToFrame(TableId, ++_seqLocal);
        if (Closed) return null;
        Sync();
        if (_rosterDirtyLocal)
        {
            _rosterDirtyLocal = false;
            return BuildRoster().ToFrame(TableId, ++_seqLocal);
        }
        if (_stateDirtyLocal)
        {
            _stateDirtyLocal = false;
            var st = BuildState(now, _eventsLocal);
            _eventsLocal.Clear();
            return st.ToFrame(TableId, ++_seqLocal);
        }
        return null;
    }

    public void Dispose() => _keys.Dispose();
}
