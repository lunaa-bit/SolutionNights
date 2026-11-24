namespace SaucerNights.Core.Cards;

public enum Suit : byte
{
    Clubs = 0,
    Diamonds = 1,
    Hearts = 2,
    Spades = 3,
}

/// <summary>
/// A playing card packed into one byte: <c>suit * 13 + (rank - 2)</c> for 0–51; 52 means "face down".
/// </summary>
public readonly record struct Card(byte Code)
{
    public const byte HiddenCode = 52;

    public static readonly Card Hidden = new(HiddenCode);

    public static Card Of(int rank, Suit suit)
    {
        if (rank is < 2 or > 14) throw new ArgumentOutOfRangeException(nameof(rank));
        return new Card((byte)((int)suit * 13 + (rank - 2)));
    }

    public bool IsHidden => Code >= 52;

    /// <summary>2–14 (11 = Jack, 12 = Queen, 13 = King, 14 = Ace).</summary>
    public int Rank => Code % 13 + 2;

    public Suit Suit => (Suit)(Code / 13);

    public bool IsRed => Suit is Suit.Diamonds or Suit.Hearts;

    /// <summary>Blackjack points with aces counted as 1 (soft totals add 10 separately).</summary>
    public int BlackjackPoints => Rank == 14 ? 1 : Math.Min(Rank, 10);

    public string RankText => Rank switch
    {
        11 => "J",
        12 => "Q",
        13 => "K",
        14 => "A",
        _ => Rank.ToString(),
    };

    public string RankName => Rank switch
    {
        2 => "Two", 3 => "Three", 4 => "Four", 5 => "Five", 6 => "Six", 7 => "Seven", 8 => "Eight",
        9 => "Nine", 10 => "Ten", 11 => "Jack", 12 => "Queen", 13 => "King", _ => "Ace",
    };

    public string RankNamePlural => Rank == 6 ? "Sixes" : RankName + "s";

    public string SuitName => Suit switch
    {
        Suit.Clubs => "Clubs",
        Suit.Diamonds => "Diamonds",
        Suit.Hearts => "Hearts",
        _ => "Spades",
    };

    public char SuitLetter => "cdhs"[(int)Suit];

    public override string ToString() => IsHidden ? "??" : (Rank == 10 ? "T" : RankText) + SuitLetter;

    /// <summary>Parses "As", "Td", "10h", "qc" (case-insensitive) or "??" for a hidden card.</summary>
    public static Card Parse(string text)
    {
        var t = text.Trim();
        if (t == "??") return Hidden;
        if (t.Length is < 2 or > 3) throw new FormatException($"Bad card '{text}'.");
        var suitChar = char.ToLowerInvariant(t[^1]);
        var suitIndex = "cdhs".IndexOf(suitChar);
        if (suitIndex < 0) throw new FormatException($"Bad suit in '{text}'.");
        var r = t[..^1].ToUpperInvariant();
        var rank = r switch
        {
            "A" => 14,
            "K" => 13,
            "Q" => 12,
            "J" => 11,
            "T" or "10" => 10,
            _ when int.TryParse(r, out var n) && n is >= 2 and <= 9 => n,
            _ => throw new FormatException($"Bad rank in '{text}'."),
        };
        return Of(rank, (Suit)suitIndex);
    }

    public static List<Card> ParseMany(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Parse).ToList();
}

public static class Decks
{
    /// <summary>All cards of <paramref name="decks"/> decks, in suit/rank order.</summary>
    public static Card[] Ordered(int decks)
    {
        var cards = new Card[decks * 52];
        for (var d = 0; d < decks; d++)
            for (var c = 0; c < 52; c++)
                cards[d * 52 + c] = new Card((byte)c);
        return cards;
    }
}
