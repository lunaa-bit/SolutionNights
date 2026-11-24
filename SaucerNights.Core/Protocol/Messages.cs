using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Crypto;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Protocol;

public static class ProtocolInfo
{
    /// <summary>Bumped whenever the wire format changes incompatibly.</summary>
    public const byte Version = 1;
}

public enum GameKind : byte
{
    Blackjack = 1,
    Poker = 2,
}

public enum MessageType : byte
{
    // Player → host
    Discover = 1,
    Join = 2,
    Leave = 3,
    Action = 4,
    Bet = 5,
    Ready = 6,
    Resync = 7,
    Rebuy = 8,
    SitOut = 9,

    // Host → everyone
    Announce = 32,
    Welcome = 33,
    Reject = 34,
    Roster = 35,
    State = 36,
    Closed = 38,

    Fragment = 240,
}

[Flags]
public enum TableFlags : byte
{
    None = 0,
    Open = 1,
    ProvablyFair = 2,
    HostPlays = 4,
    InProgress = 8,
    Paused = 16,
}

public enum RejectReason : byte
{
    Full = 1,
    Closed = 2,
    BadVersion = 3,
    Kicked = 4,
    BadKey = 5,
}

public enum CloseReason : byte
{
    HostClosed = 1,
    HostLeft = 2,
}

[Flags]
public enum ResyncNeed : byte
{
    None = 0,
    Roster = 1,
    State = 2,
    Welcome = 4,
}

public interface IMessage
{
    MessageType Type { get; }

    void Write(ByteWriter w);
}

public static class Messages
{
    public static byte[] Payload(IMessage m)
    {
        var w = new ByteWriter();
        m.Write(w);
        return w.ToArray();
    }

    public static Frame ToFrame(this IMessage m, ushort tableId, uint seq) => new(tableId, seq, m.Type, Payload(m));

    /// <summary>Parses a frame payload; returns null for unknown types or malformed payloads.</summary>
    public static IMessage? Parse(Frame f)
    {
        try
        {
            var r = new ByteReader(f.Payload);
            return f.Type switch
            {
                MessageType.Discover => new DiscoverMsg(),
                MessageType.Join => JoinMsg.Read(r),
                MessageType.Leave => new LeaveMsg(),
                MessageType.Action => ActionMsg.Read(r),
                MessageType.Bet => BetMsg.Read(r),
                MessageType.Ready => ReadyMsg.Read(r),
                MessageType.Resync => ResyncMsg.Read(r),
                MessageType.Rebuy => new RebuyMsg(),
                MessageType.SitOut => SitOutMsg.Read(r),
                MessageType.Announce => AnnounceMsg.Read(r),
                MessageType.Welcome => WelcomeMsg.Read(r),
                MessageType.Reject => RejectMsg.Read(r),
                MessageType.Roster => RosterMsg.Read(r),
                MessageType.State => StateMsg.Read(r),
                MessageType.Closed => ClosedMsg.Read(r),
                _ => null,
            };
        }
        catch (ProtocolException)
        {
            return null;
        }
    }
}

// ───────────────────────────── Player → host ─────────────────────────────

/// <summary>"Is anyone hosting?" Hosts answer with an <see cref="AnnounceMsg"/>.</summary>
public sealed class DiscoverMsg : IMessage
{
    public MessageType Type => MessageType.Discover;

    public void Write(ByteWriter w) { }
}

public sealed class JoinMsg : IMessage
{
    public uint Nonce;
    public byte Version = ProtocolInfo.Version;
    public byte[] PublicKey = [];
    public byte PreferredSeat = 255;

    public MessageType Type => MessageType.Join;

    public void Write(ByteWriter w)
    {
        w.U32(Nonce);
        w.U8(Version);
        w.Bytes(PublicKey.AsSpan(0, SessionKeys.PublicKeySize));
        w.U8(PreferredSeat);
    }

    public static JoinMsg Read(ByteReader r) => new()
    {
        Nonce = r.U32(),
        Version = r.U8(),
        PublicKey = r.Fixed(SessionKeys.PublicKeySize),
        PreferredSeat = r.U8(),
    };
}

public sealed class LeaveMsg : IMessage
{
    public MessageType Type => MessageType.Leave;

    public void Write(ByteWriter w) { }
}

/// <summary>A move during play. <see cref="TurnId"/> makes retries idempotent: stale moves are ignored.</summary>
public sealed class ActionMsg : IMessage
{
    public long Round;
    public long TurnId;
    public byte Kind;
    public long Amount;
    public byte Hand;

    public MessageType Type => MessageType.Action;

    public void Write(ByteWriter w)
    {
        w.VarU(Round);
        w.VarU(TurnId);
        w.U8(Kind);
        w.VarU(Amount);
        w.U8(Hand);
    }

    public static ActionMsg Read(ByteReader r) => new()
    {
        Round = r.VarL(),
        TurnId = r.VarL(),
        Kind = r.U8(),
        Amount = r.VarL(),
        Hand = r.U8(),
    };
}

/// <summary>Blackjack wager for the round, with the player's contribution to the shuffle.</summary>
public sealed class BetMsg : IMessage
{
    public long Round;
    public long Amount;
    public byte[] Seed = [];

    public MessageType Type => MessageType.Bet;

    public void Write(ByteWriter w)
    {
        w.VarU(Round);
        w.VarU(Amount);
        w.Bytes(Seed.AsSpan(0, Fairness.Fair.ClientSeedSize));
    }

    public static BetMsg Read(ByteReader r) => new()
    {
        Round = r.VarL(),
        Amount = r.VarL(),
        Seed = r.Fixed(Fairness.Fair.ClientSeedSize),
    };
}

/// <summary>Poker: "deal me in" for the next hand, with the player's shuffle seed.</summary>
public sealed class ReadyMsg : IMessage
{
    public long Hand;
    public byte[] Seed = [];

    public MessageType Type => MessageType.Ready;

    public void Write(ByteWriter w)
    {
        w.VarU(Hand);
        w.Bytes(Seed.AsSpan(0, Fairness.Fair.ClientSeedSize));
    }

    public static ReadyMsg Read(ByteReader r) => new() { Hand = r.VarL(), Seed = r.Fixed(Fairness.Fair.ClientSeedSize) };
}

public sealed class ResyncMsg : IMessage
{
    public ResyncNeed Need;

    public MessageType Type => MessageType.Resync;

    public void Write(ByteWriter w) => w.U8((byte)Need);

    public static ResyncMsg Read(ByteReader r) => new() { Need = (ResyncNeed)r.U8() };
}

public sealed class RebuyMsg : IMessage
{
    public MessageType Type => MessageType.Rebuy;

    public void Write(ByteWriter w) { }
}

public sealed class SitOutMsg : IMessage
{
    public bool SitOut;

    public MessageType Type => MessageType.SitOut;

    public void Write(ByteWriter w) => w.Bool(SitOut);

    public static SitOutMsg Read(ByteReader r) => new() { SitOut = r.Bool() };
}

// ───────────────────────────── Host → everyone ─────────────────────────────

/// <summary>Lobby advertisement; also the host's heartbeat.</summary>
public sealed class AnnounceMsg : IMessage
{
    public GameKind Game;
    public byte Version = ProtocolInfo.Version;
    public string Title = string.Empty;
    public string HostName = string.Empty;
    public byte SeatsTaken;
    public byte SeatsMax;
    public TableFlags Flags;
    public byte[] Rules = [];

    public MessageType Type => MessageType.Announce;

    public void Write(ByteWriter w)
    {
        w.U8((byte)Game);
        w.U8(Version);
        w.Str(Title, 40);
        w.Str(HostName, 48);
        w.U8(SeatsTaken);
        w.U8(SeatsMax);
        w.U8((byte)Flags);
        w.Blob(Rules);
    }

    public static AnnounceMsg Read(ByteReader r) => new()
    {
        Game = (GameKind)r.U8(),
        Version = r.U8(),
        Title = r.Str(40),
        HostName = r.Str(48),
        SeatsTaken = r.U8(),
        SeatsMax = r.U8(),
        Flags = (TableFlags)r.U8(),
        Rules = r.Blob(128),
    };

    public BlackjackRules? BlackjackRules => Game == GameKind.Blackjack ? Blackjack.BlackjackRules.FromBytes(Rules) : null;

    public PokerRules? PokerRules => Game == GameKind.Poker ? Poker.PokerRules.FromBytes(Rules) : null;
}

public sealed class WelcomeMsg : IMessage
{
    public uint Nonce;
    public byte Seat;
    public byte[] HostPublicKey = [];

    public MessageType Type => MessageType.Welcome;

    public void Write(ByteWriter w)
    {
        w.U32(Nonce);
        w.U8(Seat);
        w.Bytes(HostPublicKey.AsSpan(0, SessionKeys.PublicKeySize));
    }

    public static WelcomeMsg Read(ByteReader r) => new() { Nonce = r.U32(), Seat = r.U8(), HostPublicKey = r.Fixed(SessionKeys.PublicKeySize) };
}

public sealed class RejectMsg : IMessage
{
    public uint Nonce;
    public RejectReason Reason;

    public MessageType Type => MessageType.Reject;

    public void Write(ByteWriter w)
    {
        w.U32(Nonce);
        w.U8((byte)Reason);
    }

    public static RejectMsg Read(ByteReader r) => new() { Nonce = r.U32(), Reason = (RejectReason)r.U8() };
}

[Flags]
public enum RosterFlags : byte
{
    None = 0,
    Host = 1,
    SittingOut = 2,
    Away = 4,
}

public sealed record RosterEntry(byte Seat, string Name, RosterFlags Flags);

/// <summary>Who sits where. Versioned so clients can tell when they missed a change.</summary>
public sealed class RosterMsg : IMessage
{
    public byte Version;
    public List<RosterEntry> Entries = [];

    public MessageType Type => MessageType.Roster;

    public RosterEntry? BySeat(int seat) => Entries.FirstOrDefault(e => e.Seat == seat);

    public void Write(ByteWriter w)
    {
        w.U8(Version);
        w.U8(Entries.Count);
        foreach (var e in Entries)
        {
            w.U8(e.Seat);
            w.Str(e.Name, 48);
            w.U8((byte)e.Flags);
        }
    }

    public static RosterMsg Read(ByteReader r)
    {
        var m = new RosterMsg { Version = r.U8() };
        var n = r.U8();
        for (var i = 0; i < n; i++) m.Entries.Add(new RosterEntry(r.U8(), r.Str(48), (RosterFlags)r.U8()));
        return m;
    }
}

/// <summary>
/// The complete public state of the table (snapshot), plus what happened since the previous snapshot. Because every
/// snapshot is complete, a missed message never desynchronises a player — the next one repairs it.
/// </summary>
public sealed class StateMsg : IMessage
{
    public GameKind Game;
    public byte RosterVersion;
    public TableFlags Flags;
    public List<TableEvent> Events = [];
    public BlackjackView? Blackjack;
    public PokerView? Poker;

    public MessageType Type => MessageType.State;

    public const int MaxEvents = 12;

    public void Write(ByteWriter w)
    {
        w.U8((byte)Game);
        w.U8(RosterVersion);
        w.U8((byte)Flags);
        var events = Events.Count > MaxEvents ? Events.Skip(Events.Count - MaxEvents).ToList() : Events;
        w.U8(events.Count);
        foreach (var e in events) e.Write(w);
        if (Game == GameKind.Blackjack) Blackjack!.Write(w);
        else Poker!.Write(w);
    }

    public static StateMsg Read(ByteReader r)
    {
        var m = new StateMsg { Game = (GameKind)r.U8(), RosterVersion = r.U8(), Flags = (TableFlags)r.U8() };
        var n = r.U8();
        for (var i = 0; i < n; i++) m.Events.Add(TableEvent.Read(r));
        switch (m.Game)
        {
            case GameKind.Blackjack:
                m.Blackjack = BlackjackView.Read(r);
                break;
            case GameKind.Poker:
                m.Poker = PokerView.Read(r);
                break;
            default:
                throw new ProtocolException("Unknown game.");
        }
        return m;
    }
}

public sealed class ClosedMsg : IMessage
{
    public CloseReason Reason;
    public string Text = string.Empty;

    public MessageType Type => MessageType.Closed;

    public void Write(ByteWriter w)
    {
        w.U8((byte)Reason);
        w.Str(Text, 80);
    }

    public static ClosedMsg Read(ByteReader r) => new() { Reason = (CloseReason)r.U8(), Text = r.Str(80) };
}

// ───────────────────────────── Events ─────────────────────────────

public enum EventKind : byte
{
    None = 0,

    // Table
    Joined = 1,
    Left = 2,
    Kicked = 3,
    SatOut = 4,
    SatIn = 5,
    Rebuy = 6,
    ChipsAdjusted = 7,
    Paused = 8,
    Resumed = 9,
    Timeout = 10,

    // Blackjack
    BettingOpen = 20,
    BetPlaced = 21,
    Dealt = 22,
    Hit = 23,
    Stand = 24,
    Double = 25,
    Split = 26,
    Surrender = 27,
    InsuranceTaken = 28,
    InsuranceDeclined = 29,
    Bust = 30,
    Blackjack = 31,
    DealerBlackjack = 32,
    DealerBust = 33,
    DealerStands = 34,
    Win = 35,
    Lose = 36,
    Push = 37,
    InsurancePaid = 38,

    // Poker
    HandStarted = 50,
    PostSmallBlind = 51,
    PostBigBlind = 52,
    PostAnte = 53,
    Fold = 54,
    Check = 55,
    Call = 56,
    Bet = 57,
    Raise = 58,
    AllIn = 59,
    Flop = 60,
    Turn = 61,
    River = 62,
    Showdown = 63,
    WinPot = 64,
    UncalledReturned = 65,

    /// <summary>A player chose to show their cards after the hand. Amount = card codes (low byte, next byte); Extra = 1 if they had folded.</summary>
    ShowCards = 66,
}

/// <summary>Something that happened at the table — drives the log, sounds and animations.</summary>
public readonly record struct TableEvent(EventKind Kind, byte Seat, long Amount = 0, byte Extra = 0)
{
    public const byte NoSeat = 255;

    public void Write(ByteWriter w)
    {
        w.U8((byte)Kind);
        w.U8(Seat);
        w.VarU(Amount);
        w.U8(Extra);
    }

    public static TableEvent Read(ByteReader r) => new((EventKind)r.U8(), r.U8(), r.VarL(), r.U8());
}
