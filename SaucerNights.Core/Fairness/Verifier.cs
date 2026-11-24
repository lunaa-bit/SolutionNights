using System.Security.Cryptography;
using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;

namespace SaucerNights.Core.Fairness;

public enum FairStatus : byte
{
    Verified,
    Failed,
    Unverifiable,
}

public sealed record FairnessResult(GameKind Game, long Round, FairStatus Status, string Summary, DateTime At)
{
    public bool Ok => Status == FairStatus.Verified;
}

/// <summary>
/// Re-creates a finished round's shuffle from the revealed seeds and checks that the host dealt exactly those cards.
/// </summary>
public static class Verifier
{
    private static Card[] DeckFor(FairReveal reveal, ushort tableId, long round, int decks)
    {
        var key = Fair.DeckKey(reveal.ServerSeed, tableId, round, reveal.ClientSeeds);
        var deck = Fair.Shuffle(key, decks);
        var ext = 1;
        while (deck.Length < reveal.Drawn && ext < 8)
            deck = [.. deck, .. Fair.Shuffle(Fair.ExtensionKey(key, ext++), decks)];
        return deck;
    }

    private static string? CheckSeeds(FairReveal reveal, byte[]? commit, int mySeat, byte[]? mySeed)
    {
        if (commit == null) return "The shuffle commitment was not seen before the deal (joined mid-round).";
        if (!Fair.CheckCommit(reveal.ServerSeed, commit)) return "The revealed server seed does not match the commitment.";
        if (mySeed != null && !reveal.ClientSeeds.Any(s => s.Seat == mySeat && s.Seed.AsSpan().SequenceEqual(mySeed)))
            return "Your shuffle seed was not used.";
        return null;
    }

    public static FairnessResult Blackjack(BlackjackView v, byte[]? commit, ushort tableId, int decks, int mySeat, byte[]? mySeed, DateTime now)
    {
        if (v.Reveal is not { } reveal) return new(GameKind.Blackjack, v.Round, FairStatus.Unverifiable, "No shuffle reveal.", now);
        var seedProblem = CheckSeeds(reveal, commit, mySeat, mySeed);
        if (seedProblem != null)
            return new(GameKind.Blackjack, v.Round, commit == null ? FairStatus.Unverifiable : FairStatus.Failed, seedProblem, now);

        var deck = DeckFor(reveal, tableId, v.Round, decks);
        var players = v.Seats.Where(s => s.Hands.Count > 0).OrderBy(s => s.Seat).ToList();
        var n = players.Count;
        if (reveal.Drawn > deck.Length || v.Dealer.Count < 2 || deck.Length < 2 * n + 2)
            return Fail("The reveal does not cover the cards dealt.");

        var dealt = players.SelectMany(p => p.Hands.SelectMany(h => h.Cards)).Concat(v.Dealer).Select(c => c.Code).OrderBy(c => c).ToList();
        var expected = deck.Take(reveal.Drawn).Select(c => c.Code).OrderBy(c => c).ToList();
        if (dealt.Count < expected.Count)
        {
            // Someone left mid-round and their cards went with them: we can only check that what's left is genuine.
            var pool = expected.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            foreach (var c in dealt)
                if (!pool.TryGetValue(c, out var left) || left == 0) return Fail("A card on the table isn't in the shuffled shoe.");
                else pool[c] = left - 1;
            return new(GameKind.Blackjack, v.Round, FairStatus.Unverifiable, "A player left mid-round; the remaining cards are genuine.", now);
        }

        for (var k = 0; k < n; k++)
        {
            var hands = players[k].Hands;
            if (hands[0].Cards.Count == 0 || hands[0].Cards[0] != deck[k]) return Fail($"Seat {players[k].Seat + 1}'s first card is out of order.");
            if (hands.Count == 1 && (hands[0].Cards.Count < 2 || hands[0].Cards[1] != deck[n + 1 + k]))
                return Fail($"Seat {players[k].Seat + 1}'s second card is out of order.");
        }
        if (v.Dealer[0] != deck[n] || v.Dealer[1] != deck[2 * n + 1]) return Fail("The dealer's first two cards are out of order.");

        var extra = v.Dealer.Count - 2;
        for (var i = 0; i < extra; i++)
            if (v.Dealer[2 + i] != deck[reveal.Drawn - extra + i])
                return Fail("The dealer's draws are out of order.");

        if (!dealt.SequenceEqual(expected)) return Fail("The cards on the table don't match the shuffled shoe.");

        return new(GameKind.Blackjack, v.Round, FairStatus.Verified, $"Round {v.Round}: {reveal.Drawn} cards match the committed shuffle.", now);

        FairnessResult Fail(string why) => new(GameKind.Blackjack, v.Round, FairStatus.Failed, why, now);
    }

    public static FairnessResult Poker(PokerView v, byte[]? commit, ushort tableId, int mySeat, byte[]? mySeed, IReadOnlyList<Card> myHole, DateTime now)
    {
        if (v.Reveal is not { } reveal) return new(GameKind.Poker, v.Hand, FairStatus.Unverifiable, "No shuffle reveal.", now);
        if (commit == null)
            return new(GameKind.Poker, v.Hand, FairStatus.Unverifiable, "The shuffle commitment was not seen before the deal (joined mid-hand).", now);

        // The commitment covers the server seed and every entry, opened or not.
        var hashes = reveal.Leaves
            .Select((l, i) => l.Open ? PokerFair.LeafHash(tableId, v.Hand, i, l.Salt, l.Positions) : l.Hash)
            .ToList();
        if (!CryptographicOperations.FixedTimeEquals(PokerFair.Commit(reveal.ServerSeed, hashes), commit))
            return Fail("The reveal does not match the commitment made before the deal.");
        if (mySeed != null && !reveal.ClientSeeds.Any(s => s.Seat == mySeat && s.Seed.AsSpan().SequenceEqual(mySeed)))
            return Fail("Your shuffle seed was not used.");

        var opened = reveal.Leaves.Where(l => l.Open).SelectMany(l => l.Positions).ToList();
        if (opened.Any(p => p >= 52) || opened.Distinct().Count() != opened.Count)
            return Fail("The reveal uses a deck position twice.");

        var deck = Fair.Deck(reveal.ServerSeed, reveal.ClientSeeds, tableId, v.Hand, 1);

        Card[]? CardsOf(int leaf) =>
            leaf < reveal.Leaves.Count && reveal.Leaves[leaf].Open ? reveal.Leaves[leaf].Positions.Select(p => deck[p]).ToArray() : null;

        var board = new List<Card>();
        foreach (var leaf in new[] { PokerFair.FlopLeaf, PokerFair.TurnLeaf, PokerFair.RiverLeaf })
        {
            if (board.Count >= v.Board.Count) break;
            if (CardsOf(leaf) is not { } cards) return Fail("The reveal does not cover the board.");
            board.AddRange(cards);
        }
        if (!board.SequenceEqual(v.Board)) return Fail("The board doesn't match the shuffle.");

        foreach (var s in v.Seats.Where(s => s.Shown.Count == 2))
            if (CardsOf(PokerFair.SeatLeaf(s.Seat)) is not { } cards || !cards.SequenceEqual(s.Shown))
                return Fail($"Seat {s.Seat + 1}'s shown cards don't match the shuffle.");

        if (myHole.Count == 2 && mySeat >= 0 && CardsOf(PokerFair.SeatLeaf(mySeat)) is { } mine && !mine.SequenceEqual(myHole))
            return Fail("Your hole cards don't match the shuffle.");

        return new(GameKind.Poker, v.Hand, FairStatus.Verified, $"Hand {v.Hand}: the board and every shown hand match the committed shuffle.", now);

        FairnessResult Fail(string why) => new(GameKind.Poker, v.Hand, FairStatus.Failed, why, now);
    }
}
