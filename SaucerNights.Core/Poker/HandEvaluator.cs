using SaucerNights.Core.Cards;

namespace SaucerNights.Core.Poker;

public enum HandCategory : byte
{
    HighCard = 0,
    OnePair = 1,
    TwoPair = 2,
    ThreeOfAKind = 3,
    Straight = 4,
    Flush = 5,
    FullHouse = 6,
    FourOfAKind = 7,
    StraightFlush = 8,
}

/// <summary>A hand's strength. Higher <see cref="Score"/> wins; equal scores split.</summary>
public readonly record struct HandValue(int Score, HandCategory Category, Card[] Best)
{
    public int Rank(int i) => (Score >> (16 - 4 * i)) & 0xF;

    public string Name => HandEvaluator.Describe(this);
}

/// <summary>Best five-card poker hand out of five to seven cards.</summary>
public static class HandEvaluator
{
    public static HandValue Evaluate(IReadOnlyList<Card> cards)
    {
        var visible = cards.Where(c => !c.IsHidden).ToList();
        if (visible.Count == 0) return new HandValue(0, HandCategory.HighCard, []);

        Span<int> counts = stackalloc int[15];
        Span<int> suitCounts = stackalloc int[4];
        Span<int> suitMask = stackalloc int[4];
        var rankMask = 0;
        foreach (var c in visible)
        {
            counts[c.Rank]++;
            suitCounts[(int)c.Suit]++;
            suitMask[(int)c.Suit] |= 1 << c.Rank;
            rankMask |= 1 << c.Rank;
        }

        var flushSuit = -1;
        for (var s = 0; s < 4; s++)
            if (suitCounts[s] >= 5) flushSuit = s;

        if (flushSuit >= 0)
        {
            var sfTop = StraightTop(suitMask[flushSuit]);
            if (sfTop > 0)
                return Make(HandCategory.StraightFlush, StraightRanks(sfTop), visible, (Suit)flushSuit);
        }

        var quads = RanksWithCount(counts, 4);
        if (quads.Count > 0)
        {
            var q = quads[0];
            var kicker = HighestExcept(counts, 1, q);
            return Make(HandCategory.FourOfAKind, [q, q, q, q, .. kicker], visible);
        }

        var trips = RanksWithCount(counts, 3);
        var pairs = RanksWithCount(counts, 2);
        if (trips.Count > 0 && (trips.Count > 1 || pairs.Count > 0))
        {
            var t = trips[0];
            var p = Math.Max(trips.Count > 1 ? trips[1] : 0, pairs.Count > 0 ? pairs[0] : 0);
            return Make(HandCategory.FullHouse, [t, t, t, p, p], visible);
        }

        if (flushSuit >= 0)
        {
            var ranks = new List<int>();
            for (var r = 14; r >= 2 && ranks.Count < 5; r--)
                if ((suitMask[flushSuit] & (1 << r)) != 0) ranks.Add(r);
            return Make(HandCategory.Flush, ranks.ToArray(), visible, (Suit)flushSuit);
        }

        var top = StraightTop(rankMask);
        if (top > 0) return Make(HandCategory.Straight, StraightRanks(top), visible);

        if (trips.Count > 0)
        {
            var t = trips[0];
            return Make(HandCategory.ThreeOfAKind, [t, t, t, .. HighestExcept(counts, 2, t)], visible);
        }

        if (pairs.Count >= 2)
        {
            int a = pairs[0], b = pairs[1];
            return Make(HandCategory.TwoPair, [a, a, b, b, .. HighestExcept(counts, 1, a, b)], visible);
        }

        if (pairs.Count == 1)
        {
            var p = pairs[0];
            return Make(HandCategory.OnePair, [p, p, .. HighestExcept(counts, 3, p)], visible);
        }

        return Make(HandCategory.HighCard, HighestExcept(counts, 5), visible);
    }

    /// <summary>Highest straight in a rank bitmask (bit n = rank n, aces also count low), or 0.</summary>
    private static int StraightTop(int mask)
    {
        if ((mask & (1 << 14)) != 0) mask |= 1 << 1;
        for (var top = 14; top >= 5; top--)
        {
            var need = 0b11111 << (top - 4);
            if ((mask & need) == need) return top;
        }
        return 0;
    }

    private static int[] StraightRanks(int top) =>
        top == 5 ? [5, 4, 3, 2, 14] : [top, top - 1, top - 2, top - 3, top - 4];

    private static List<int> RanksWithCount(Span<int> counts, int n)
    {
        var list = new List<int>();
        for (var r = 14; r >= 2; r--)
            if (counts[r] == n) list.Add(r);
        // Trips/quads also contain a usable pair for full houses; handled by the caller.
        return list;
    }

    private static int[] HighestExcept(Span<int> counts, int howMany, params int[] exclude)
    {
        var list = new List<int>(howMany);
        for (var r = 14; r >= 2 && list.Count < howMany; r--)
            if (counts[r] > 0 && Array.IndexOf(exclude, r) < 0) list.Add(r);
        return list.ToArray();
    }

    private static HandValue Make(HandCategory cat, int[] ranks, List<Card> cards, Suit? suit = null)
    {
        var score = (int)cat << 20;
        // Ace-low straights score as five-high.
        var scoreRanks = cat is HandCategory.Straight or HandCategory.StraightFlush && ranks[0] == 5 ? [5, 4, 3, 2, 1] : ranks;
        for (var i = 0; i < 5; i++)
            score |= (i < scoreRanks.Length ? scoreRanks[i] : 0) << (16 - 4 * i);

        var used = new bool[cards.Count];
        var best = new List<Card>(5);
        foreach (var r in ranks)
        {
            for (var i = 0; i < cards.Count; i++)
            {
                if (used[i] || cards[i].Rank != r || (suit != null && cards[i].Suit != suit)) continue;
                used[i] = true;
                best.Add(cards[i]);
                break;
            }
        }
        return new HandValue(score, cat, best.ToArray());
    }

    public static string Describe(HandValue v)
    {
        string R(int i) => new Card((byte)(Math.Clamp(v.Rank(i), 2, 14) - 2)).RankName;
        string Rs(int i) => new Card((byte)(Math.Clamp(v.Rank(i), 2, 14) - 2)).RankNamePlural;
        return v.Category switch
        {
            HandCategory.StraightFlush when v.Rank(0) == 14 => "Royal Flush",
            HandCategory.StraightFlush => $"Straight Flush, {R(0)} high",
            HandCategory.FourOfAKind => $"Four of a Kind, {Rs(0)}",
            HandCategory.FullHouse => $"Full House, {Rs(0)} over {Rs(3)}",
            HandCategory.Flush => $"Flush, {R(0)} high",
            HandCategory.Straight => $"Straight, {R(0)} high",
            HandCategory.ThreeOfAKind => $"Three of a Kind, {Rs(0)}",
            HandCategory.TwoPair => $"Two Pair, {Rs(0)} and {Rs(2)}",
            HandCategory.OnePair => $"Pair of {Rs(0)}",
            _ => $"{R(0)} high",
        };
    }

    public static string CategoryName(HandCategory c) => c switch
    {
        HandCategory.StraightFlush => "Straight Flush",
        HandCategory.FourOfAKind => "Four of a Kind",
        HandCategory.FullHouse => "Full House",
        HandCategory.Flush => "Flush",
        HandCategory.Straight => "Straight",
        HandCategory.ThreeOfAKind => "Three of a Kind",
        HandCategory.TwoPair => "Two Pair",
        HandCategory.OnePair => "Pair",
        _ => "High Card",
    };
}
