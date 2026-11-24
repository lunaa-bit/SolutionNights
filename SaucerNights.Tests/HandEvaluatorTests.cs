using SaucerNights.Core.Cards;
using SaucerNights.Core.Poker;

namespace SaucerNights.Tests;

public class HandEvaluatorTests
{
    private static HandValue E(string cards) => HandEvaluator.Evaluate(Card.ParseMany(cards));

    [Theory]
    [InlineData("As Ks Qs Js Ts 2d 3c", HandCategory.StraightFlush, "Royal Flush")]
    [InlineData("9h 8h 7h 6h 5h Kc Kd", HandCategory.StraightFlush, "Straight Flush, Nine high")]
    [InlineData("Ad 2d 3d 4d 5d 9c 9s", HandCategory.StraightFlush, "Straight Flush, Five high")]
    [InlineData("7c 7d 7h 7s Kd 2c 3c", HandCategory.FourOfAKind, "Four of a Kind, Sevens")]
    [InlineData("Kc Kd Kh 7s 7d 2c 3c", HandCategory.FullHouse, "Full House, Kings over Sevens")]
    [InlineData("Kc Kd Kh 7s 7d 7c 3c", HandCategory.FullHouse, "Full House, Kings over Sevens")]
    [InlineData("Ah 9h 6h 3h 2h Kc Kd", HandCategory.Flush, "Flush, Ace high")]
    [InlineData("Ac 2d 3h 4s 5c 9d Jc", HandCategory.Straight, "Straight, Five high")]
    [InlineData("Tc Jd Qh Ks Ac 2d 2c", HandCategory.Straight, "Straight, Ace high")]
    [InlineData("Qc Qd Qh 9s 2c 4d 6h", HandCategory.ThreeOfAKind, "Three of a Kind, Queens")]
    [InlineData("Jc Jd 4h 4s 2c 9d Ac", HandCategory.TwoPair, "Two Pair, Jacks and Fours")]
    [InlineData("6c 6d 4h 9s 2c Jd Ac", HandCategory.OnePair, "Pair of Sixes")]
    [InlineData("Ac Jd 9h 7s 5c 3d 2h", HandCategory.HighCard, "Ace high")]
    public void ClassifiesAndNames(string cards, HandCategory category, string name)
    {
        var v = E(cards);
        Assert.Equal(category, v.Category);
        Assert.Equal(name, v.Name);
        Assert.Equal(5, v.Best.Length);
        Assert.Equal(5, v.Best.Distinct().Count());
    }

    [Theory]
    [InlineData("As Ad Kc 7h 2d 3s 9c", "Ks Kd Ac 7h 2d 3s 9c")] // aces beat kings
    [InlineData("Ah Ad Kc Qh 2d 3s 9c", "Ac As Jc Qh 2d 3s 9c")] // kicker K beats J
    [InlineData("2c 3d 4h 5s 6c Kd Kh", "Ac 2d 3h 4s 5c Kd Kh")] // six-high straight beats the wheel
    [InlineData("Ah 2h 3h 4h 9h Kc Kd", "Kh Qh Jh 9h 8h Kc Kd")] // flush ace high beats king high
    [InlineData("Kc Kd Kh 2s 2d 5c 9c", "Qc Qd Qh As Ad 5c 9c")] // boat KKK22 beats QQQAA
    [InlineData("Jc Jd 4h 4s Ac 2d 3c", "Jh Js 4c 4d Kc 2h 3h")] // two pair kicker
    public void FirstBeatsSecond(string a, string b) => Assert.True(E(a).Score > E(b).Score);

    [Fact]
    public void BoardPlaysIsASplit()
    {
        const string board = "Ac Kd Qh Js Tc";
        Assert.Equal(E(board + " 2c 3d").Score, E(board + " 4h 5s").Score);
    }

    [Fact]
    public void FlushBestCardsAreTheFlushSuit()
    {
        var v = E("Ah 9h 6h 3h 2h Ac Kh");
        Assert.All(v.Best, c => Assert.Equal(Suit.Hearts, c.Suit));
        Assert.Contains(Card.Parse("Kh"), v.Best);
        Assert.DoesNotContain(Card.Parse("2h"), v.Best);
    }
}
