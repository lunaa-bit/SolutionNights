using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;

namespace SaucerNights.Tests;

internal static class Shoe
{
    /// <summary>The given cards first, then the rest of a deck, so draws never run out.</summary>
    public static Card[] Of(string cards)
    {
        var top = Card.ParseMany(cards);
        var rest = Decks.Ordered(2).Where(c => !top.Contains(c));
        return [.. top, .. rest];
    }
}

public class BlackjackEngineTests
{
    private readonly DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private BlackjackEngine Table(string shoe, int players = 1, BlackjackRules? rules = null, long bet = 100)
    {
        var e = new BlackjackEngine(rules ?? new BlackjackRules { AutoDeal = false, MinBet = 10, MaxBet = 1000 });
        for (var i = 0; i < players; i++) e.Seat(i, 1000, _now);
        e.StartNow(_now);
        Assert.Equal(BjPhase.Betting, e.Phase);
        for (var i = 0; i < players; i++) e.OnBet(i, new BetMsg { Round = e.Round, Amount = bet, Seed = new byte[8] }, _now);
        e.DeckOverride = () => Shoe.Of(shoe);
        e.StartNow(_now);
        return e;
    }

    private BlackjackView View(BlackjackEngine e) => e.BuildState(_now).Blackjack!;

    private void Act(BlackjackEngine e, int seat, BjActionKind kind)
    {
        var v = View(e);
        e.OnAction(seat, new ActionMsg { Round = v.Round, TurnId = v.TurnId, Kind = (byte)kind, Hand = v.ActiveHand }, _now);
    }

    [Fact]
    public void StandingOnNineteenBeatsSeventeen()
    {
        var e = Table("Ts 7d 9h Tc");
        Assert.Equal(BjPhase.PlayerTurns, e.Phase);
        var v = View(e);
        Assert.True(v.Dealer[1].IsHidden);
        Assert.Equal(0, v.ActiveSeat);
        Act(e, 0, BjActionKind.Stand);
        v = View(e);
        Assert.Equal(BjPhase.Settled, e.Phase);
        Assert.Equal(BjOutcome.Win, v.Seats[0].Hands[0].Outcome);
        Assert.Equal(1100, v.Seats[0].Bankroll);
        Assert.Equal(-100, e.HouseNet);
    }

    [Fact]
    public void NaturalPaysThreeToTwo()
    {
        var e = Table("As 7d Kh 9c Th");
        var v = View(e);
        Assert.Equal(BjPhase.Settled, e.Phase);
        Assert.Equal(BjOutcome.Blackjack, v.Seats[0].Hands[0].Outcome);
        Assert.Equal(1150, v.Seats[0].Bankroll);
    }

    [Fact]
    public void SixToFiveTablesPaySixToFive()
    {
        var e = Table("As 7d Kh 9c Th", rules: new BlackjackRules { AutoDeal = false, SixToFive = true, MaxBet = 1000 });
        Assert.Equal(1120, View(e).Seats[0].Bankroll);
    }

    [Fact]
    public void InsurancePaysTwoToOneAgainstDealerBlackjack()
    {
        var e = Table("Ts Ad 9h Kc");
        Assert.Equal(BjPhase.Insurance, e.Phase);
        Act(e, 0, BjActionKind.TakeInsurance);
        var v = View(e);
        Assert.Equal(BjPhase.Settled, e.Phase);
        Assert.Equal(BjOutcome.Lose, v.Seats[0].Hands[0].Outcome);
        Assert.Equal(1000, v.Seats[0].Bankroll); // -100 bet, -50 insurance, +150 insurance win
        Assert.Equal(2, v.Dealer.Count);
        Assert.False(v.Dealer[1].IsHidden);
    }

    [Fact]
    public void DealerPeeksUnderATen()
    {
        var e = Table("9s Kd 9h As");
        Assert.Equal(BjPhase.Settled, e.Phase);
        Assert.Equal(900, View(e).Seats[0].Bankroll);
    }

    [Fact]
    public void SplitAcesGetOneCardEach()
    {
        var e = Table("As 9d Ah 7c 5h Kd 2c");
        var v = View(e);
        Assert.True(v.Allowed.HasFlag(BjActions.Split));
        Act(e, 0, BjActionKind.Split);
        v = View(e);
        Assert.Equal(BjPhase.Settled, e.Phase);
        var hands = v.Seats[0].Hands;
        Assert.Equal(2, hands.Count);
        Assert.Equal("As 5h", string.Join(" ", hands[0].Cards));
        Assert.Equal("Ah Kd", string.Join(" ", hands[1].Cards));
        Assert.Equal(BjOutcome.Lose, hands[0].Outcome);
        Assert.Equal(BjOutcome.Win, hands[1].Outcome); // split 21 is not a blackjack: paid 1:1
        Assert.Equal(1000, v.Seats[0].Bankroll);
        Assert.Equal(18, BjMath.Total(v.Dealer));
    }

    [Fact]
    public void DoubleDownTakesOneCard()
    {
        var e = Table("6s 9d 5h 7c Th 8d");
        Act(e, 0, BjActionKind.Double);
        var v = View(e);
        Assert.Equal(BjPhase.Settled, e.Phase);
        Assert.Equal(200, v.Seats[0].Hands[0].Bet);
        Assert.Equal(3, v.Seats[0].Hands[0].Cards.Count);
        Assert.Equal(1200, v.Seats[0].Bankroll);
    }

    [Fact]
    public void SurrenderReturnsHalf()
    {
        var e = Table("Ts 9d 6h 7c");
        Act(e, 0, BjActionKind.Surrender);
        var v = View(e);
        Assert.Equal(950, v.Seats[0].Bankroll);
        Assert.Equal(2, v.Dealer.Count); // nothing live: the dealer doesn't draw
    }

    [Fact]
    public void BustingLosesAndDealerDoesNotDraw()
    {
        var e = Table("Ts 9d 6h 7c Kd");
        Act(e, 0, BjActionKind.Hit);
        var v = View(e);
        Assert.Equal(BjPhase.Settled, e.Phase);
        Assert.Equal(BjOutcome.Lose, v.Seats[0].Hands[0].Outcome);
        Assert.Equal(900, v.Seats[0].Bankroll);
        Assert.Equal(2, v.Dealer.Count);
    }

    [Fact]
    public void TurnsGoInSeatOrderAndTimeoutsStand()
    {
        // p0: T 8, p1: 9 7, dealer 6 T
        var e = Table("Ts 9d 6h 8c 7h Td 5c", players: 2);
        var v = View(e);
        Assert.Equal(0, v.ActiveSeat);
        Act(e, 1, BjActionKind.Hit); // not their turn: ignored
        Assert.Equal(0, View(e).ActiveSeat);

        e.Tick(_now + TimeSpan.FromSeconds(e.Rules.TurnSeconds) + BlackjackEngine.Grace + TimeSpan.FromSeconds(1));
        v = View(e);
        Assert.Equal(1, v.ActiveSeat);
        Assert.True(v.Seats[0].Hands[0].Flags.HasFlag(BjHandFlags.Stood));
    }

    [Fact]
    public void StaleActionsAreIgnored()
    {
        var e = Table("Ts 9d 6h 8c 7h Td");
        var v = View(e);
        var stale = new ActionMsg { Round = v.Round, TurnId = v.TurnId - 1, Kind = (byte)BjActionKind.Hit };
        e.OnAction(0, stale, _now);
        Assert.Equal(2, View(e).Seats[0].Hands[0].Cards.Count);
    }

    [Fact]
    public void LeavingMidRoundForfeitsToTheHouse()
    {
        var e = Table("Ts 9d 6h 8c 7h Td", players: 2);
        var left = e.Unseat(0, _now);
        Assert.Equal(900, left);
        Assert.Equal(100, e.HouseNet);
        Assert.Equal(1, View(e).ActiveSeat);
    }
}

public class PokerEngineTests
{
    private readonly DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private PokerEngine Table(string deck, params long[] stacks)
    {
        var e = new PokerEngine(new PokerRules { SmallBlind = 10, BigBlind = 20, StartingStack = 1000, AutoStart = false, ProvablyFair = false });
        for (var i = 0; i < stacks.Length; i++) e.Seat(i, stacks[i], _now);
        e.DeckOverride = () => Shoe.Of(deck).Distinct().Take(52).ToArray();
        e.StartNow(_now);
        return e;
    }

    private PokerView View(PokerEngine e) => e.BuildState(_now).Poker!;

    private void Act(PokerEngine e, int seat, PokerActionKind kind, long amount = 0)
    {
        var v = View(e);
        Assert.Equal(seat, v.ActiveSeat);
        e.OnAction(seat, new ActionMsg { Round = v.Hand, TurnId = v.TurnId, Kind = (byte)kind, Amount = amount }, _now);
    }

    private static long Total(PokerView v) => v.Seats.Sum(s => s.Stack + s.Bet) + v.Pots.Sum(p => p.Amount);

    [Fact]
    public void BlindsAndActionOrderThreeHanded()
    {
        var e = Table("2c 3c 4c 5c 6c 7c", 1000, 1000, 1000);
        var v = View(e);
        Assert.Equal(PokerPhase.Preflop, v.Phase);
        Assert.Equal(0, v.Button);
        Assert.Equal(1, v.SmallBlindSeat);
        Assert.Equal(2, v.BigBlindSeat);
        Assert.Equal(0, v.ActiveSeat);
        Assert.Equal(10, v.Seat(1)!.Bet);
        Assert.Equal(20, v.Seat(2)!.Bet);
        Assert.Equal(40, v.MinRaiseTo);
        Assert.Equal(3000, Total(v));
    }

    [Fact]
    public void FoldsAroundGiveTheBigBlindThePot()
    {
        var e = Table("2c 3c 4c 5c 6c 7c", 1000, 1000, 1000);
        Act(e, 0, PokerActionKind.Fold);
        Act(e, 1, PokerActionKind.Fold);
        var v = View(e);
        Assert.Equal(PokerPhase.Showdown, v.Phase);
        Assert.Equal(1010, v.Seat(2)!.Stack);
        Assert.Equal(990, v.Seat(1)!.Stack);
        Assert.Empty(v.Seat(2)!.Shown);
    }

    [Fact]
    public void BigBlindGetsTheOption()
    {
        var e = Table("2c 3c 4c 5c 6c 7c", 1000, 1000, 1000);
        Act(e, 0, PokerActionKind.Call);
        Act(e, 1, PokerActionKind.Call);
        var v = View(e);
        Assert.Equal(2, v.ActiveSeat);
        Assert.True(v.Allowed.HasFlag(PokerActions.Check));
        Assert.True(v.Allowed.HasFlag(PokerActions.Raise));
        Act(e, 2, PokerActionKind.Check);
        v = View(e);
        Assert.Equal(PokerPhase.Flop, v.Phase);
        Assert.Equal(3, v.Board.Count);
        Assert.Equal(1, v.ActiveSeat); // first active seat left of the button
        Assert.Equal(60, v.PotTotal);
    }

    [Fact]
    public void HeadsUpButtonPostsSmallBlindAndActsFirstPreflopOnly()
    {
        var e = Table("2c 3c 4c 5c 6c 7c", 1000, 1000);
        var v = View(e);
        Assert.Equal(0, v.Button);
        Assert.Equal(0, v.SmallBlindSeat);
        Assert.Equal(1, v.BigBlindSeat);
        Assert.Equal(0, v.ActiveSeat);
        Act(e, 0, PokerActionKind.Call);
        Act(e, 1, PokerActionKind.Check);
        Assert.Equal(1, View(e).ActiveSeat);
    }

    [Fact]
    public void SidePotsAreSplitCorrectly()
    {
        // Deal order: 1, 2, 0, 1, 2, 0. Seat 0: AA, seat 1: KK, seat 2: 72o.
        var e = Table("Kc 7c Ac Kd 2d Ad 4h 9h 5s 3c 4s Jd 4d 8s", 100, 300, 1000);
        Act(e, 0, PokerActionKind.AllIn);
        Act(e, 1, PokerActionKind.AllIn);
        Act(e, 2, PokerActionKind.Call);
        var v = View(e);
        Assert.Equal(PokerPhase.Showdown, v.Phase);
        Assert.Equal(5, v.Board.Count);
        Assert.Equal(2, v.Pots.Count);
        Assert.Equal(300, v.Pots[0].Amount);
        Assert.Equal(400, v.Pots[1].Amount);
        Assert.Equal(300, v.Seat(0)!.Stack);
        Assert.Equal(400, v.Seat(1)!.Stack);
        Assert.Equal(700, v.Seat(2)!.Stack);
        Assert.Equal(1400, v.Seats.Sum(s => s.Stack));
        Assert.All(v.Seats, s => Assert.Equal(2, s.Shown.Count));
    }

    [Fact]
    public void UncalledBetIsReturned()
    {
        var e = Table("2c 3c 4c 5c 6c 7c", 1000, 1000, 1000);
        Act(e, 0, PokerActionKind.BetOrRaise, 100);
        Act(e, 1, PokerActionKind.Fold);
        Act(e, 2, PokerActionKind.Fold);
        var v = View(e);
        Assert.Equal(1030, v.Seat(0)!.Stack);
        Assert.Equal(3000, v.Seats.Sum(s => s.Stack));
    }

    [Fact]
    public void SplitPotOddChipGoesLeftOfTheButton()
    {
        var e = new PokerEngine(new PokerRules { SmallBlind = 5, BigBlind = 10, StartingStack = 1000, AutoStart = false, ProvablyFair = false });
        for (var i = 0; i < 3; i++) e.Seat(i, 1000, _now);
        // Holes are irrelevant: the board is a royal flush.
        e.DeckOverride = () => Shoe.Of("2c 3d 4h 5c 6d 7h 2h As Ks Qs 2s Js 3s Ts").Distinct().Take(52).ToArray();
        e.StartNow(_now);
        Act(e, 0, PokerActionKind.Call);
        Act(e, 1, PokerActionKind.Fold);
        Act(e, 2, PokerActionKind.Check);
        for (var street = 0; street < 3; street++)
        {
            Act(e, 2, PokerActionKind.Check);
            Act(e, 0, PokerActionKind.Check);
        }
        var v = View(e);
        Assert.Equal(PokerPhase.Showdown, v.Phase);
        Assert.Equal(1003, v.Seat(2)!.Stack);
        Assert.Equal(1002, v.Seat(0)!.Stack);
        Assert.Equal(995, v.Seat(1)!.Stack);
    }

    [Fact]
    public void IncompleteAllInRaiseDoesNotReopenBetting()
    {
        var e = Table("2c 3c 4c 5c 6c 7c", 1000, 150, 1000);
        Act(e, 0, PokerActionKind.BetOrRaise, 100); // full raise of 80
        Act(e, 1, PokerActionKind.AllIn);             // to 150: only 50 more, incomplete
        var v = View(e);
        Assert.Equal(2, v.ActiveSeat);
        Assert.True(v.Allowed.HasFlag(PokerActions.Raise)); // BB hasn't acted yet
        Act(e, 2, PokerActionKind.Call);
        v = View(e);
        Assert.Equal(0, v.ActiveSeat);
        Assert.True(v.Allowed.HasFlag(PokerActions.Call));
        Assert.False(v.Allowed.HasFlag(PokerActions.Raise));
    }

    [Fact]
    public void RaisesBelowTheMinimumAreRejected()
    {
        var e = Table("2c 3c 4c 5c 6c 7c", 1000, 1000, 1000);
        var before = View(e).TurnId;
        Act(e, 0, PokerActionKind.BetOrRaise, 30); // min raise is to 40
        var v = View(e);
        Assert.Equal(before, v.TurnId);
        Assert.Equal(0, v.ActiveSeat);
    }

    [Fact]
    public void TimeoutChecksOrFoldsThenSitsOut()
    {
        var e = Table("2c 3c 4c 5c 6c 7c", 1000, 1000, 1000);
        var late = _now + TimeSpan.FromSeconds(e.Rules.TurnSeconds) + PokerEngine.Grace + TimeSpan.FromSeconds(1);
        e.Tick(late);
        var v = e.BuildState(late).Poker!;
        Assert.Equal(PokerSeatStatus.Folded, v.Seat(0)!.Status);
        Assert.Equal(1, v.ActiveSeat);
    }

    [Fact]
    public void LeavingFoldsAndKeepsChipsConserved()
    {
        var e = Table("2c 3c 4c 5c 6c 7c", 1000, 1000, 1000);
        Act(e, 0, PokerActionKind.BetOrRaise, 100);
        var left = e.Unseat(0, _now); // walks away with 900, the 100 stays in
        Assert.Equal(900, left);
        Act(e, 1, PokerActionKind.Fold);
        var v = View(e);
        Assert.Equal(PokerPhase.Showdown, v.Phase);
        Assert.Equal(1110, v.Seat(2)!.Stack); // 20 own + 10 SB + 100 forfeited
        Assert.Equal(3000, left + v.Seats.Where(s => s.Seat != 0).Sum(s => s.Stack));
    }
}
