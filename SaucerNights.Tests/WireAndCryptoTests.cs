using SaucerNights.Core.Cards;
using SaucerNights.Core.Crypto;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Wire;

namespace SaucerNights.Tests;

public class CardTests
{
    [Theory]
    [InlineData("As", 14, Suit.Spades)]
    [InlineData("Td", 10, Suit.Diamonds)]
    [InlineData("10h", 10, Suit.Hearts)]
    [InlineData("2c", 2, Suit.Clubs)]
    [InlineData("qh", 12, Suit.Hearts)]
    public void Parses(string text, int rank, Suit suit)
    {
        var c = Card.Parse(text);
        Assert.Equal(rank, c.Rank);
        Assert.Equal(suit, c.Suit);
        Assert.Equal(c, Card.Parse(c.ToString()));
    }

    [Fact]
    public void AllCodesRoundTrip()
    {
        var seen = new HashSet<string>();
        for (byte i = 0; i < 52; i++)
        {
            var c = new Card(i);
            Assert.False(c.IsHidden);
            Assert.True(seen.Add(c.ToString()));
            Assert.Equal(c, Card.Of(c.Rank, c.Suit));
        }
        Assert.True(Card.Hidden.IsHidden);
    }
}

public class ChatBase64Tests
{
    [Fact]
    public void AlphabetIsChatSafe()
    {
        var a = ChatBase64.Alphabet;
        Assert.Equal(64, a.Length);
        Assert.Equal(64, a.Distinct().Count());
        foreach (var ch in a)
        {
            Assert.DoesNotContain(char.ToLowerInvariant(ch), "aeiouy");
            Assert.DoesNotContain(ch, "<>/\\*%\"'~ ");
            Assert.True(ch < 128);
        }
    }

    [Fact]
    public void RoundTripsEveryLength()
    {
        var rng = new Random(7);
        for (var len = 0; len < 200; len++)
        {
            var data = new byte[len];
            rng.NextBytes(data);
            var text = ChatBase64.Encode(data);
            Assert.Equal(ChatBase64.EncodedLength(len), text.Length);
            Assert.True(ChatBase64.TryDecode(text, out var back));
            Assert.Equal(data, back);
        }
    }

    [Fact]
    public void RejectsForeignCharacters()
    {
        Assert.False(ChatBase64.TryDecode("BCD*", out _));
        Assert.False(ChatBase64.TryDecode("BCDFG", out _)); // length % 4 == 1
    }
}

public class FrameTests
{
    [Fact]
    public void FrameRoundTripsThroughALine()
    {
        var msg = new ActionMsg { Round = 12, TurnId = 300, Kind = 4, Amount = 123456, Hand = 1 };
        var line = FrameCodec.ToLine(FrameCodec.ToBytes(msg.ToFrame(4242, 77)));
        Assert.StartsWith(FrameCodec.Prefix, line);
        Assert.True(FrameCodec.TryParseLine(line, out var f));
        Assert.Equal(4242, f.TableId);
        Assert.Equal(77u, f.Seq);
        var back = Assert.IsType<ActionMsg>(Messages.Parse(f));
        Assert.Equal(123456, back.Amount);
        Assert.Equal(300, back.TurnId);
    }

    [Fact]
    public void CorruptionIsDetected()
    {
        var line = FrameCodec.ToLine(FrameCodec.ToBytes(new SitOutMsg { SitOut = true }.ToFrame(9, 1)));
        // What a chat profanity filter would do: mask part of the line.
        var mangled = line[..8] + "***" + line[11..];
        Assert.False(FrameCodec.TryParseLine(mangled, out _));
        var flipped = line[..^2] + (line[^2] == 'B' ? 'C' : 'B') + line[^1];
        Assert.False(FrameCodec.TryParseLine(flipped, out _));
    }

    [Fact]
    public void LongFramesAreFragmentedAndReassembled()
    {
        var payload = new byte[1500];
        new Random(3).NextBytes(payload);
        var frame = new Frame(321, 5, MessageType.State, payload);
        byte counter = 0;
        var lines = FrameCodec.ToLines(frame, ref counter, 440);
        Assert.True(lines.Count > 3);
        Assert.All(lines, l => Assert.True(l.Length <= 440, $"line {l.Length} chars"));

        var asm = new FrameAssembler();
        Frame? result = null;
        // Deliver out of order; only the last piece completes the frame.
        foreach (var l in lines.AsEnumerable().Reverse())
        {
            Assert.Null(result);
            result = asm.Accept("Host@World", l, DateTime.UtcNow);
        }
        Assert.NotNull(result);
        Assert.Equal(payload, result!.Payload);
        Assert.Equal(MessageType.State, result.Type);
    }

    [Fact]
    public void ProtocolLinesAreRecognised()
    {
        Assert.True(FrameCodec.LooksLikeProtocol("SN1~BCDF"));
        Assert.False(FrameCodec.LooksLikeProtocol("hello there"));
        Assert.False(FrameCodec.LooksLikeProtocol("SN"));
    }
}

public class CryptoTests
{
    [Fact]
    public void BothSidesDeriveTheSameKey()
    {
        using var host = new SessionKeys();
        using var player = new SessionKeys();
        Assert.Equal(SessionKeys.PublicKeySize, host.PublicKey.Length);
        var a = host.TryDerive(player.PublicKey);
        var b = player.TryDerive(host.PublicKey);
        Assert.NotNull(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void BadKeysAreRejected()
    {
        using var k = new SessionKeys();
        Assert.Null(k.TryDerive(new byte[64]));
        Assert.Null(k.TryDerive(new byte[10]));
    }

    [Fact]
    public void HoleCardsSealAndOpenOnlyWithTheRightKey()
    {
        using var host = new SessionKeys();
        using var alice = new SessionKeys();
        using var bob = new SessionKeys();
        var aliceKey = host.TryDerive(alice.PublicKey)!;
        var bobKey = host.TryDerive(bob.PublicKey)!;
        byte[] cards = [Card.Parse("As").Code, Card.Parse("Kd").Code];

        var sealedBytes = CardSeal.Seal(aliceKey, 1000, 42, 3, cards);
        Assert.NotEqual(cards, sealedBytes[..2]);
        Assert.True(CardSeal.TryOpen(alice.TryDerive(host.PublicKey)!, 1000, 42, 3, sealedBytes, out var opened));
        Assert.Equal(cards, opened);

        Assert.False(CardSeal.TryOpen(bobKey, 1000, 42, 3, sealedBytes, out _));
        Assert.False(CardSeal.TryOpen(aliceKey, 1000, 43, 3, sealedBytes, out _)); // other hand
        Assert.False(CardSeal.TryOpen(aliceKey, 1000, 42, 4, sealedBytes, out _)); // other seat
    }
}

public class FairnessTests
{
    [Fact]
    public void ShuffleIsAPermutationAndDeterministic()
    {
        var seed = Fair.NewServerSeed();
        var key = Fair.DeckKey(seed, 7, 1, [new SeatSeed(2, [1, 2, 3, 4, 5, 6, 7, 8])]);
        var a = Fair.Shuffle(key, 6);
        var b = Fair.Shuffle(key, 6);
        Assert.Equal(a, b);
        Assert.Equal(312, a.Length);
        Assert.All(a.GroupBy(c => c.Code), g => Assert.Equal(6, g.Count()));
    }

    [Fact]
    public void SeedOrderDoesNotMatterButSeedsDo()
    {
        var seed = Fair.NewServerSeed();
        SeatSeed s1 = new(1, [1, 1, 1, 1, 1, 1, 1, 1]), s2 = new(4, [2, 2, 2, 2, 2, 2, 2, 2]);
        Assert.Equal(Fair.DeckKey(seed, 1, 1, [s1, s2]), Fair.DeckKey(seed, 1, 1, [s2, s1]));
        Assert.NotEqual(Fair.DeckKey(seed, 1, 1, [s1]), Fair.DeckKey(seed, 1, 1, [s1, s2]));
        Assert.NotEqual(Fair.DeckKey(seed, 1, 1, [s1]), Fair.DeckKey(seed, 1, 2, [s1]));
    }

    [Fact]
    public void CommitmentBindsTheSeed()
    {
        var seed = Fair.NewServerSeed();
        var commit = Fair.Commit(seed);
        Assert.True(Fair.CheckCommit(seed, commit));
        seed[0] ^= 1;
        Assert.False(Fair.CheckCommit(seed, commit));
    }

    [Fact]
    public void ShuffleLooksUniform()
    {
        // Position of the ace of spades over many shuffles should be roughly uniform (chi-square, 51 dof).
        const int trials = 26000;
        var counts = new int[52];
        var ace = Card.Parse("As");
        var seed = new byte[16];
        for (var t = 0; t < trials; t++)
        {
            seed[0] = (byte)t;
            seed[1] = (byte)(t >> 8);
            var deck = Fair.Shuffle(Fair.DeckKey(seed, 1, t, []), 1);
            counts[Array.IndexOf(deck, ace)]++;
        }
        var expected = trials / 52.0;
        var chi = counts.Sum(c => (c - expected) * (c - expected) / expected);
        Assert.True(chi < 95, $"chi-square {chi:F1} is too high for a uniform shuffle");
    }

    [Fact]
    public void RngHasNoModuloBias()
    {
        var rng = new FairRng(new byte[32]);
        var counts = new int[3];
        for (var i = 0; i < 30000; i++) counts[rng.NextInt(3)]++;
        Assert.All(counts, c => Assert.InRange(c, 9500, 10500));
    }
}
