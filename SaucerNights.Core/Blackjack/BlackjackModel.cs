using SaucerNights.Core.Cards;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Blackjack;

/// <summary>House rules for a blackjack table. Sent to players in the lobby announcement.</summary>
public sealed class BlackjackRules
{
    public byte Decks = 6;
    public bool DealerHitsSoft17;
    public bool SixToFive;
    public bool DoubleAnyTwo = true;
    public bool DoubleAfterSplit = true;
    public byte MaxHands = 4;
    public bool ResplitAces;
    public bool HitSplitAces;
    public bool LateSurrender = true;
    public bool Insurance = true;
    public bool ProvablyFair = true;
    public bool AutoDeal = true;
    public byte MaxSeats = 6;
    public byte TurnSeconds = 30;
    public byte BetSeconds = 15;
    public long MinBet = 10;
    public long MaxBet = 500;
    public long StartingStack = 1000;

    public BlackjackRules Normalized()
    {
        var r = (BlackjackRules)MemberwiseClone();
        r.Decks = (byte)Math.Clamp((int)r.Decks, 1, 8);
        r.MaxHands = (byte)Math.Clamp((int)r.MaxHands, 1, 4);
        r.MaxSeats = (byte)Math.Clamp((int)r.MaxSeats, 1, 7);
        r.TurnSeconds = (byte)Math.Clamp((int)r.TurnSeconds, 10, 120);
        r.BetSeconds = (byte)Math.Clamp((int)r.BetSeconds, 5, 120);
        r.MinBet = Math.Clamp(r.MinBet, 1, 1_000_000_000);
        r.MaxBet = Math.Clamp(r.MaxBet, r.MinBet, 10_000_000_000);
        r.StartingStack = Math.Clamp(r.StartingStack, r.MinBet, 100_000_000_000);
        return r;
    }

    public string PayoutText => SixToFive ? "6 to 5" : "3 to 2";

    public long BlackjackWinnings(long bet) => SixToFive ? bet * 6 / 5 : bet * 3 / 2;

    public byte[] ToBytes()
    {
        var w = new ByteWriter();
        w.U8(Decks);
        var f1 = (DealerHitsSoft17 ? 1 : 0) | (SixToFive ? 2 : 0) | (DoubleAnyTwo ? 4 : 0) | (DoubleAfterSplit ? 8 : 0)
                 | (ResplitAces ? 16 : 0) | (HitSplitAces ? 32 : 0) | (LateSurrender ? 64 : 0) | (Insurance ? 128 : 0);
        w.U8(f1);
        w.U8((ProvablyFair ? 1 : 0) | (AutoDeal ? 2 : 0));
        w.U8(MaxHands);
        w.U8(MaxSeats);
        w.U8(TurnSeconds);
        w.U8(BetSeconds);
        w.VarU(MinBet);
        w.VarU(MaxBet);
        w.VarU(StartingStack);
        return w.ToArray();
    }

    public static BlackjackRules FromBytes(byte[] bytes)
    {
        var r = new ByteReader(bytes);
        var rules = new BlackjackRules { Decks = r.U8() };
        var f1 = r.U8();
        var f2 = r.U8();
        rules.DealerHitsSoft17 = (f1 & 1) != 0;
        rules.SixToFive = (f1 & 2) != 0;
        rules.DoubleAnyTwo = (f1 & 4) != 0;
        rules.DoubleAfterSplit = (f1 & 8) != 0;
        rules.ResplitAces = (f1 & 16) != 0;
        rules.HitSplitAces = (f1 & 32) != 0;
        rules.LateSurrender = (f1 & 64) != 0;
        rules.Insurance = (f1 & 128) != 0;
        rules.ProvablyFair = (f2 & 1) != 0;
        rules.AutoDeal = (f2 & 2) != 0;
        rules.MaxHands = r.U8();
        rules.MaxSeats = r.U8();
        rules.TurnSeconds = r.U8();
        rules.BetSeconds = r.U8();
        rules.MinBet = r.VarL();
        rules.MaxBet = r.VarL();
        rules.StartingStack = r.VarL();
        return rules.Normalized();
    }
}

public static class BjMath
{
    /// <summary>Best total of the visible cards and whether it is soft (an ace counted as 11).</summary>
    public static (int Total, bool Soft) Evaluate(IEnumerable<Card> cards)
    {
        var sum = 0;
        var ace = false;
        foreach (var c in cards)
        {
            if (c.IsHidden) continue;
            sum += c.BlackjackPoints;
            if (c.Rank == 14) ace = true;
        }
        return ace && sum + 10 <= 21 ? (sum + 10, true) : (sum, false);
    }

    public static int Total(IEnumerable<Card> cards) => Evaluate(cards).Total;

    public static bool IsNatural(IReadOnlyList<Card> cards) => cards.Count == 2 && Total(cards) == 21;

    /// <summary>"Soft 17", "21", "Bust", "Blackjack".</summary>
    public static string Describe(IReadOnlyList<Card> cards, bool natural = false)
    {
        var visible = cards.Where(c => !c.IsHidden).ToList();
        if (visible.Count == 0) return string.Empty;
        if (natural || (cards.Count == 2 && visible.Count == 2 && Total(visible) == 21 && natural)) return "Blackjack";
        var (total, soft) = Evaluate(visible);
        if (total > 21) return "Bust";
        return soft && total != 21 ? $"Soft {total}" : total.ToString();
    }
}

public enum BjPhase : byte
{
    Waiting = 0,
    Betting = 1,
    Insurance = 2,
    PlayerTurns = 3,
    DealerTurn = 4,
    Settled = 5,
}

public enum BjSeatStatus : byte
{
    Empty = 0,
    Seated = 1,
    SittingOut = 2,
    BetPlaced = 3,
    Playing = 4,
    Done = 5,
}

[Flags]
public enum BjActions : byte
{
    None = 0,
    Hit = 1,
    Stand = 2,
    Double = 4,
    Split = 8,
    Surrender = 16,
    Insurance = 32,
}

public enum BjActionKind : byte
{
    Hit = 1,
    Stand = 2,
    Double = 3,
    Split = 4,
    Surrender = 5,
    TakeInsurance = 6,
    DeclineInsurance = 7,
}

[Flags]
public enum BjHandFlags : byte
{
    None = 0,
    Doubled = 1,
    FromSplit = 2,
    Stood = 4,
    SplitAces = 8,
    Surrendered = 16,
    Natural = 32,
    Busted = 64,
}

public enum BjOutcome : byte
{
    Pending = 0,
    Win = 1,
    Lose = 2,
    Push = 3,
    Blackjack = 4,
    Surrender = 5,
}

public enum InsuranceState : byte
{
    None = 0,
    Undecided = 1,
    Taken = 2,
    Declined = 3,
}

public sealed class BjHandView
{
    public long Bet;
    public BjHandFlags Flags;
    public BjOutcome Outcome;

    /// <summary>Chips returned to the player at settlement (stake included).</summary>
    public long Payout;

    public List<Card> Cards = [];

    public bool IsFinished => (Flags & (BjHandFlags.Stood | BjHandFlags.Busted | BjHandFlags.Surrendered | BjHandFlags.Natural)) != 0;
}

public sealed class BjSeatView
{
    public byte Seat;
    public BjSeatStatus Status;
    public long Bankroll;
    public long Bet;
    public long Insurance;
    public InsuranceState InsuranceState;
    public long RoundNet;
    public List<BjHandView> Hands = [];

    public long ChipsInPlay => Hands.Sum(h => h.Bet) + Insurance;
}

/// <summary>Public snapshot of a blackjack table.</summary>
public sealed class BlackjackView
{
    public BjPhase Phase;
    public long Round;
    public long TurnId;
    public byte ActiveSeat = 255;
    public byte ActiveHand;
    public BjActions Allowed;
    public byte DeadlineSec = 255;
    public List<Card> Dealer = [];
    public List<BjSeatView> Seats = [];
    public byte[]? Commit;
    public FairReveal? Reveal;

    public BjSeatView? Seat(int seat) => Seats.FirstOrDefault(s => s.Seat == seat);

    public void Write(ByteWriter w)
    {
        w.U8((byte)Phase);
        w.VarU(Round);
        w.VarU(TurnId);
        w.U8(ActiveSeat);
        w.U8(ActiveHand);
        w.U8((byte)Allowed);
        w.U8(DeadlineSec);
        w.Cards(Dealer);
        w.U8(Seats.Count);
        foreach (var s in Seats)
        {
            w.U8(s.Seat);
            w.U8((byte)s.Status);
            w.VarU(s.Bankroll);
            w.VarU(s.Bet);
            w.VarU(s.Insurance);
            w.U8((byte)s.InsuranceState);
            w.VarS(s.RoundNet);
            w.U8(s.Hands.Count);
            foreach (var h in s.Hands)
            {
                w.VarU(h.Bet);
                w.U8((byte)h.Flags);
                w.U8((byte)h.Outcome);
                w.VarU(h.Payout);
                w.Cards(h.Cards);
            }
        }
        var flags = (Commit != null ? 1 : 0) | (Reveal != null ? 2 : 0);
        w.U8(flags);
        if (Commit != null) w.Bytes(Commit.AsSpan(0, Fair.CommitSize));
        Reveal?.Write(w);
    }

    public static BlackjackView Read(ByteReader r)
    {
        var v = new BlackjackView
        {
            Phase = (BjPhase)r.U8(),
            Round = r.VarL(),
            TurnId = r.VarL(),
            ActiveSeat = r.U8(),
            ActiveHand = r.U8(),
            Allowed = (BjActions)r.U8(),
            DeadlineSec = r.U8(),
            Dealer = r.Cards(),
        };
        var n = r.U8();
        for (var i = 0; i < n; i++)
        {
            var s = new BjSeatView
            {
                Seat = r.U8(),
                Status = (BjSeatStatus)r.U8(),
                Bankroll = r.VarL(),
                Bet = r.VarL(),
                Insurance = r.VarL(),
                InsuranceState = (InsuranceState)r.U8(),
                RoundNet = r.VarS(),
            };
            var hands = r.U8();
            for (var h = 0; h < hands; h++)
                s.Hands.Add(new BjHandView { Bet = r.VarL(), Flags = (BjHandFlags)r.U8(), Outcome = (BjOutcome)r.U8(), Payout = r.VarL(), Cards = r.Cards() });
            v.Seats.Add(s);
        }
        var flags = r.U8();
        if ((flags & 1) != 0) v.Commit = r.Fixed(Fair.CommitSize);
        if ((flags & 2) != 0) v.Reveal = FairReveal.Read(r);
        return v;
    }
}
