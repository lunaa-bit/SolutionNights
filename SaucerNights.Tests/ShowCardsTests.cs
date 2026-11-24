using SaucerNights.Core.Cards;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Wire;

namespace SaucerNights.Tests;

/// <summary>Showing cards after the hand, and a fairness reveal that keeps unshown hands secret.</summary>
public class ShowCardsTests
{
    private const ushort TableId = 4321;
    private DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PokerView View(PokerEngine e, DateTime now) => e.BuildState(now).Poker!;

    private void Act(PokerEngine e, PokerActionKind kind, long amount = 0)
    {
        var v = View(e, _now);
        e.OnAction(v.ActiveSeat, new ActionMsg { Round = v.Hand, TurnId = v.TurnId, Kind = (byte)kind, Amount = amount }, _now);
    }

    private void Show(PokerEngine e, int seat) =>
        e.OnAction(seat, new ActionMsg { Round = e.Round, Kind = (byte)PokerActionKind.Show }, _now);

    /// <summary>A three-handed provably fair hand, dealt and waiting on the first player.</summary>
    private (PokerEngine Engine, byte[] Commit, Dictionary<int, Card[]> Holes) FairHand()
    {
        var e = new PokerEngine(new PokerRules { SmallBlind = 10, BigBlind = 20, StartingStack = 1000, AutoStart = false, ProvablyFair = true, MaxSeats = 6 }) { TableId = TableId };
        for (var i = 0; i < 3; i++) e.Seat(i, 1000, _now);
        e.StartNow(_now);
        var commit = View(e, _now).Commit!;
        for (var i = 0; i < 3; i++) e.OnReady(i, new ReadyMsg { Hand = e.Round, Seed = Fair.NewClientSeed() }, _now);
        Assert.Equal(PokerPhase.Preflop, e.Phase);
        var holes = e.PrivateCards().ToDictionary(p => p.Seat, p => p.Cards);
        return (e, commit, holes);
    }

    /// <summary>The first player folds and the other two check it down. Returns the folder's seat.</summary>
    private int FoldThenShowdown(PokerEngine e)
    {
        var folder = View(e, _now).ActiveSeat;
        Act(e, PokerActionKind.Fold);
        Act(e, PokerActionKind.Call);
        while (e.Phase != PokerPhase.Showdown) Act(e, PokerActionKind.Check);
        return folder;
    }

    [Fact]
    public void FoldedPlayerCanShowOnceTheHandIsOver()
    {
        var (e, _, holes) = FairHand();
        var folder = View(e, _now).ActiveSeat;
        Show(e, folder);
        Assert.Empty(View(e, _now).Seat(folder)!.Shown); // not while the hand is live

        Act(e, PokerActionKind.Fold);
        Show(e, folder);
        Assert.Empty(View(e, _now).Seat(folder)!.Shown); // still live for the others
        Act(e, PokerActionKind.Fold);

        var v = View(e, _now);
        Assert.Equal(PokerPhase.Showdown, v.Phase);
        Assert.All(v.Seats, s => Assert.Empty(s.Shown));
        Assert.True(e.CanShow(folder));
        e.DrainEvents();

        Show(e, folder);
        v = View(e, _now);
        Assert.Equal(holes[folder], v.Seat(folder)!.Shown);
        Assert.Equal(PokerSeatStatus.Folded, v.Seat(folder)!.Status);
        var ev = Assert.Single(e.DrainEvents(), x => x.Kind == EventKind.ShowCards);
        Assert.Equal(folder, ev.Seat);
        Assert.Equal(holes[folder][0].Code | (holes[folder][1].Code << 8), ev.Amount);
        Assert.False(e.CanShow(folder));

        // Showing twice does nothing.
        Show(e, folder);
        Assert.DoesNotContain(e.DrainEvents(), x => x.Kind == EventKind.ShowCards);
    }

    [Fact]
    public void ShowingKeepsTheHandOnTheTableLongEnoughToSee()
    {
        var (e, _, _) = FairHand();
        var folder = View(e, _now).ActiveSeat;
        Act(e, PokerActionKind.Fold);
        Act(e, PokerActionKind.Fold);
        _now += TimeSpan.FromSeconds(4.5);
        e.Tick(_now);
        Assert.Equal(PokerPhase.Showdown, e.Phase);
        Show(e, folder);
        _now += TimeSpan.FromSeconds(3);
        e.Tick(_now);
        Assert.Equal(PokerPhase.Showdown, e.Phase);
        Assert.Equal(2, View(e, _now).Seat(folder)!.Shown.Count);
        _now += TimeSpan.FromSeconds(1.5);
        e.Tick(_now);
        Assert.NotEqual(PokerPhase.Showdown, e.Phase);
        Assert.False(e.CanShow(folder));
    }

    [Fact]
    public void UncontestedWinnerCanShowToo()
    {
        var (e, _, holes) = FairHand();
        Act(e, PokerActionKind.Fold);
        Act(e, PokerActionKind.Fold);
        var winner = View(e, _now).Seats.Single(s => s.Won > 0).Seat;
        Show(e, winner);
        Assert.Equal(holes[winner], View(e, _now).Seat(winner)!.Shown);
    }

    [Fact]
    public void RevealOpensOnlyPublicCardsAndStillVerifies()
    {
        var (e, commit, holes) = FairHand();
        var folder = FoldThenShowdown(e);

        var v = View(e, _now);
        var reveal = v.Reveal!;
        Assert.Equal(PokerFair.LeafCount(6), reveal.Leaves.Count);
        Assert.True(reveal.Leaves[PokerFair.FlopLeaf].Open && reveal.Leaves[PokerFair.TurnLeaf].Open && reveal.Leaves[PokerFair.RiverLeaf].Open);
        foreach (var s in v.Seats)
            Assert.Equal(s.Seat != folder, reveal.Leaves[PokerFair.SeatLeaf(s.Seat)].Open);

        // Nothing about the folded hand is published beyond a salted hash.
        var hidden = reveal.Leaves[PokerFair.SeatLeaf(folder)];
        Assert.Empty(hidden.Positions);
        Assert.Empty(hidden.Salt);
        Assert.Equal(PokerFair.LeafHashSize, hidden.Hash.Length);

        // The opened positions never point at the folded player's cards.
        var deck = Fair.Deck(reveal.ServerSeed, reveal.ClientSeeds, TableId, v.Hand, 1);
        var published = reveal.Leaves.Where(l => l.Open).SelectMany(l => l.Positions).Select(p => deck[p]).ToList();
        Assert.DoesNotContain(holes[folder][0], published);
        Assert.DoesNotContain(holes[folder][1], published);

        // Survives the wire.
        var w = new ByteWriter();
        v.Write(w);
        var read = PokerView.Read(new ByteReader(w.ToArray()));
        Assert.Equal(FairStatus.Verified, Verifier.Poker(read, commit, TableId, folder, null, holes[folder], _now).Status);
        Assert.Equal(FairStatus.Verified, Verifier.Poker(read, commit, TableId, -1, null, [], _now).Status);

        // The folded player shows: their entry opens and everything still checks out.
        Show(e, folder);
        var after = View(e, _now);
        Assert.True(after.Reveal!.Leaves[PokerFair.SeatLeaf(folder)].Open);
        Assert.Equal(reveal.OpenCount + 1, after.Reveal.OpenCount);
        Assert.Equal(FairStatus.Verified, Verifier.Poker(after, commit, TableId, folder, null, holes[folder], _now).Status);
    }

    [Fact]
    public void TamperedRevealsAreCaught()
    {
        var (e, commit, holes) = FairHand();
        var folder = FoldThenShowdown(e);
        Show(e, folder);

        PokerView Fresh() => View(e, _now);
        FairStatus Check(PokerView view) => Verifier.Poker(view, commit, TableId, -1, null, [], _now).Status;
        Assert.Equal(FairStatus.Verified, Check(Fresh()));

        // A different card on the board.
        var v = Fresh();
        v.Board[4] = holes[folder][0];
        Assert.Equal(FairStatus.Failed, Check(v));

        // A shown hand swapped for other cards.
        v = Fresh();
        v.Seat(folder)!.Shown = [v.Board[0], v.Board[1]];
        Assert.Equal(FairStatus.Failed, Check(v));

        // An opened entry pointed at a different deck position.
        v = Fresh();
        var leaf = v.Reveal!.Leaves[PokerFair.SeatLeaf(folder)];
        leaf.Positions = [(byte)((leaf.Positions[0] + 1) % 52), leaf.Positions[1]];
        Assert.Equal(FairStatus.Failed, Check(v));

        // A hidden entry swapped out.
        v = Fresh();
        var empty = v.Reveal!.Leaves[PokerFair.SeatLeaf(5)];
        Assert.False(empty.Open);
        empty.Hash = new byte[PokerFair.LeafHashSize];
        Assert.Equal(FairStatus.Failed, Check(v));

        // A shown hand whose entry is withheld.
        v = Fresh();
        var mine = v.Reveal!.Leaves[PokerFair.SeatLeaf(folder)];
        v.Reveal.Leaves[PokerFair.SeatLeaf(folder)] = new PokerLeaf { Hash = PokerFair.LeafHash(TableId, v.Hand, PokerFair.SeatLeaf(folder), mine.Salt, mine.Positions) };
        Assert.Equal(FairStatus.Failed, Check(v));

        // A different server seed.
        v = Fresh();
        v.Reveal!.ServerSeed = Fair.NewServerSeed();
        Assert.Equal(FairStatus.Failed, Check(v));

        // The player's own cards differing from what they were dealt.
        v = Fresh();
        Assert.Equal(FairStatus.Failed, Verifier.Poker(v, commit, TableId, folder, null, [v.Board[0], v.Board[1]], _now).Status);
    }

    [Fact]
    public void EveryHandDealsDistinctCards()
    {
        for (var round = 0; round < 40; round++)
        {
            var (e, _, holes) = FairHand();
            Act(e, PokerActionKind.Call);
            Act(e, PokerActionKind.Call);
            while (e.Phase != PokerPhase.Showdown) Act(e, PokerActionKind.Check);
            var cards = holes.Values.SelectMany(c => c).Concat(View(e, _now).Board).ToList();
            Assert.Equal(11, cards.Count);
            Assert.Equal(11, cards.Distinct().Count());
            Assert.DoesNotContain(cards, c => c.IsHidden);
        }
    }
}
