using SaucerNights.Core.Cards;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Poker;

/// <summary>No-limit Texas Hold'em table settings.</summary>
public sealed class PokerRules
{
    public long SmallBlind = 10;
    public long BigBlind = 20;
    public long Ante;
    public long StartingStack = 2000;
    public byte MaxSeats = 8;
    public byte TurnSeconds = 30;
    /// <summary>On by default: players check every deck afterwards, so the host can't stack it.</summary>
    public bool ProvablyFair = true;

    /// <summary>Off by default: a host who plays also sees every card, so only friends should allow it.</summary>
    public bool HostPlays;
    public bool AutoStart = true;

    public PokerRules Normalized()
    {
        var r = (PokerRules)MemberwiseClone();
        r.SmallBlind = Math.Clamp(r.SmallBlind, 1, 1_000_000_000);
        r.BigBlind = Math.Clamp(r.BigBlind, r.SmallBlind, 2_000_000_000);
        r.Ante = Math.Clamp(r.Ante, 0, r.BigBlind);
        r.StartingStack = Math.Clamp(r.StartingStack, r.BigBlind * 2, 1_000_000_000_000);
        r.MaxSeats = (byte)Math.Clamp((int)r.MaxSeats, 2, 9);
        r.TurnSeconds = (byte)Math.Clamp((int)r.TurnSeconds, 10, 120);
        return r;
    }

    public string StakesText => Ante > 0 ? $"{SmallBlind:N0}/{BigBlind:N0} (ante {Ante:N0})" : $"{SmallBlind:N0}/{BigBlind:N0}";

    public byte[] ToBytes()
    {
        var w = new ByteWriter();
        w.VarU(SmallBlind);
        w.VarU(BigBlind);
        w.VarU(Ante);
        w.VarU(StartingStack);
        w.U8(MaxSeats);
        w.U8(TurnSeconds);
        w.U8((ProvablyFair ? 1 : 0) | (HostPlays ? 2 : 0) | (AutoStart ? 4 : 0));
        return w.ToArray();
    }

    public static PokerRules FromBytes(byte[] bytes)
    {
        var r = new ByteReader(bytes);
        var rules = new PokerRules
        {
            SmallBlind = r.VarL(),
            BigBlind = r.VarL(),
            Ante = r.VarL(),
            StartingStack = r.VarL(),
            MaxSeats = r.U8(),
            TurnSeconds = r.U8(),
        };
        var f = r.U8();
        rules.ProvablyFair = (f & 1) != 0;
        rules.HostPlays = (f & 2) != 0;
        rules.AutoStart = (f & 4) != 0;
        return rules.Normalized();
    }
}

public enum PokerPhase : byte
{
    Waiting = 0,
    Starting = 1,
    Preflop = 2,
    Flop = 3,
    Turn = 4,
    River = 5,
    Showdown = 6,
}

public enum PokerSeatStatus : byte
{
    Empty = 0,
    SittingOut = 1,
    Waiting = 2,
    Active = 3,
    Folded = 4,
    AllIn = 5,
}

[Flags]
public enum PokerActions : byte
{
    None = 0,
    Fold = 1,
    Check = 2,
    Call = 4,
    Bet = 8,
    Raise = 16,
    AllIn = 32,
}

public enum PokerActionKind : byte
{
    Fold = 1,
    Check = 2,
    Call = 3,

    /// <summary>Bet or raise; the amount is the player's total bet for the street ("raise to").</summary>
    BetOrRaise = 4,
    AllIn = 5,

    /// <summary>After the hand: turn your unshown hole cards face up (folded, or won without a showdown).</summary>
    Show = 6,
}

public enum PokerLast : byte
{
    None = 0,
    SmallBlind = 1,
    BigBlind = 2,
    Check = 3,
    Call = 4,
    Bet = 5,
    Raise = 6,
    AllIn = 7,
    Fold = 8,
}

public sealed class PokerSeatView
{
    public byte Seat;
    public PokerSeatStatus Status;
    public long Stack;

    /// <summary>Chips in front of the player on this street.</summary>
    public long Bet;

    public PokerLast Last;

    /// <summary>Hole cards turned face up at showdown or shown by choice afterwards (empty otherwise).</summary>
    public List<Card> Shown = [];

    public long Won;

    public bool InHand => Status is PokerSeatStatus.Active or PokerSeatStatus.AllIn;
}

public sealed class PokerPotView
{
    public long Amount;
    public ushort Eligible;
    public ushort Winners;
}

public sealed class SealedHole
{
    public byte Seat;
    public byte[] Data = [];
}

/// <summary>Public snapshot of a hold'em table. Hole cards travel sealed for their owner only.</summary>
public sealed class PokerView
{
    public PokerPhase Phase;
    public long Hand;
    public long TurnId;
    public byte Button = 255;
    public byte SmallBlindSeat = 255;
    public byte BigBlindSeat = 255;
    public byte ActiveSeat = 255;
    public PokerActions Allowed;
    public byte DeadlineSec = 255;
    public long CurrentBet;
    public long MinRaiseTo;
    public long SmallBlind;
    public long BigBlind;

    /// <summary>Seats that were dealt in this hand (bit per seat).</summary>
    public ushort DealtMask;

    public List<Card> Board = [];
    public List<PokerPotView> Pots = [];
    public List<PokerSeatView> Seats = [];
    public byte[]? Commit;
    public PokerReveal? Reveal;
    public List<SealedHole> Holes = [];

    public PokerSeatView? Seat(int seat) => Seats.FirstOrDefault(s => s.Seat == seat);

    public long PotTotal => Pots.Sum(p => p.Amount);

    public bool BettingRound => Phase is PokerPhase.Preflop or PokerPhase.Flop or PokerPhase.Turn or PokerPhase.River;

    public void Write(ByteWriter w)
    {
        w.U8((byte)Phase);
        w.VarU(Hand);
        w.VarU(TurnId);
        w.U8(Button);
        w.U8(SmallBlindSeat);
        w.U8(BigBlindSeat);
        w.U8(ActiveSeat);
        w.U8((byte)Allowed);
        w.U8(DeadlineSec);
        w.VarU(CurrentBet);
        w.VarU(MinRaiseTo);
        w.VarU(SmallBlind);
        w.VarU(BigBlind);
        w.U16(DealtMask);
        w.Cards(Board);
        w.U8(Pots.Count);
        foreach (var p in Pots)
        {
            w.VarU(p.Amount);
            w.U16(p.Eligible);
            w.U16(p.Winners);
        }
        w.U8(Seats.Count);
        foreach (var s in Seats)
        {
            w.U8(s.Seat);
            w.U8((byte)s.Status);
            w.VarU(s.Stack);
            w.VarU(s.Bet);
            w.U8((byte)s.Last);
            w.Cards(s.Shown);
            w.VarU(s.Won);
        }
        var flags = (Commit != null ? 1 : 0) | (Reveal != null ? 2 : 0) | (Holes.Count > 0 ? 4 : 0);
        w.U8(flags);
        if (Commit != null) w.Bytes(Commit.AsSpan(0, Fair.CommitSize));
        Reveal?.Write(w);
        if (Holes.Count > 0)
        {
            w.U8(Holes.Count);
            foreach (var h in Holes)
            {
                w.U8(h.Seat);
                w.Blob(h.Data);
            }
        }
    }

    public static PokerView Read(ByteReader r)
    {
        var v = new PokerView
        {
            Phase = (PokerPhase)r.U8(),
            Hand = r.VarL(),
            TurnId = r.VarL(),
            Button = r.U8(),
            SmallBlindSeat = r.U8(),
            BigBlindSeat = r.U8(),
            ActiveSeat = r.U8(),
            Allowed = (PokerActions)r.U8(),
            DeadlineSec = r.U8(),
            CurrentBet = r.VarL(),
            MinRaiseTo = r.VarL(),
            SmallBlind = r.VarL(),
            BigBlind = r.VarL(),
            DealtMask = r.U16(),
            Board = r.Cards(),
        };
        var pots = r.U8();
        for (var i = 0; i < pots; i++) v.Pots.Add(new PokerPotView { Amount = r.VarL(), Eligible = r.U16(), Winners = r.U16() });
        var seats = r.U8();
        for (var i = 0; i < seats; i++)
        {
            v.Seats.Add(new PokerSeatView
            {
                Seat = r.U8(),
                Status = (PokerSeatStatus)r.U8(),
                Stack = r.VarL(),
                Bet = r.VarL(),
                Last = (PokerLast)r.U8(),
                Shown = r.Cards(),
                Won = r.VarL(),
            });
        }
        var flags = r.U8();
        if ((flags & 1) != 0) v.Commit = r.Fixed(Fair.CommitSize);
        if ((flags & 2) != 0) v.Reveal = PokerReveal.Read(r);
        if ((flags & 4) != 0)
        {
            var n = r.U8();
            for (var i = 0; i < n; i++) v.Holes.Add(new SealedHole { Seat = r.U8(), Data = r.Blob(16) });
        }
        return v;
    }

    /// <summary>Seats in dealing order for this hand: clockwise starting left of the button.</summary>
    public static List<int> DealOrder(ushort dealtMask, int button, int maxSeats = 16)
    {
        var order = new List<int>();
        for (var k = 1; k <= maxSeats; k++)
        {
            var seat = (button + k) % maxSeats;
            if ((dealtMask & (1 << seat)) != 0) order.Add(seat);
        }
        return order;
    }
}
