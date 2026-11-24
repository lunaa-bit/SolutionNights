using System.Buffers.Binary;
using System.Security.Cryptography;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Fairness;

/// <summary>
/// Deterministic random stream: HMAC-SHA256(key, counter) blocks. Anyone holding the key can reproduce it.
/// </summary>
public sealed class FairRng
{
    private readonly byte[] _key;
    private readonly byte[] _block = new byte[32];
    private ulong _counter;
    private int _pos = 32;

    public FairRng(ReadOnlySpan<byte> key) => _key = key.ToArray();

    private void Refill()
    {
        Span<byte> c = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(c, _counter++);
        HMACSHA256.HashData(_key, c, _block);
        _pos = 0;
    }

    public uint NextUInt32()
    {
        if (_pos > 28) Refill();
        var v = BinaryPrimitives.ReadUInt32LittleEndian(_block.AsSpan(_pos));
        _pos += 4;
        return v;
    }

    /// <summary>Uniform integer in [0, maxExclusive) using rejection sampling (no modulo bias).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 1) return 0;
        var max = (ulong)maxExclusive;
        var limit = (1UL << 32) - (1UL << 32) % max;
        while (true)
        {
            ulong v = NextUInt32();
            if (v < limit) return (int)(v % max);
        }
    }
}

/// <summary>A player's contribution to a shuffle.</summary>
public readonly record struct SeatSeed(int Seat, byte[] Seed);

/// <summary>What the host publishes after a round so players can re-create and check the shuffle.</summary>
public sealed class FairReveal
{
    public byte[] ServerSeed = [];
    public List<SeatSeed> ClientSeeds = [];

    /// <summary>How many cards were drawn from the shuffled deck this round.</summary>
    public int Drawn;

    public void Write(ByteWriter w)
    {
        w.Bytes(ServerSeed.AsSpan(0, Fair.ServerSeedSize));
        w.U8(ClientSeeds.Count);
        foreach (var s in ClientSeeds)
        {
            w.U8(s.Seat);
            w.Bytes(s.Seed.AsSpan(0, Fair.ClientSeedSize));
        }
        w.VarU((ulong)Drawn);
    }

    public static FairReveal Read(ByteReader r)
    {
        var rev = new FairReveal { ServerSeed = r.Fixed(Fair.ServerSeedSize) };
        var n = r.U8();
        for (var i = 0; i < n; i++)
            rev.ClientSeeds.Add(new SeatSeed(r.U8(), r.Fixed(Fair.ClientSeedSize)));
        rev.Drawn = (int)r.VarU();
        return rev;
    }
}

/// <summary>
/// Provably fair shuffles. Before a round the host publishes <see cref="Commit"/> of a secret server seed. Players then
/// add their own random seeds. The deck is derived from both, and the server seed is revealed afterwards, so the host
/// cannot pick the deck order and players can check every card.
/// </summary>
public static class Fair
{
    public const int ServerSeedSize = 16;
    public const int ClientSeedSize = 8;
    public const int CommitSize = 16;

    private static readonly byte[] CommitLabel = "SaucerNights/commit/v1"u8.ToArray();
    private static readonly byte[] DeckLabel = "SaucerNights/deck/v1"u8.ToArray();

    public static byte[] NewServerSeed() => RandomNumberGenerator.GetBytes(ServerSeedSize);

    public static byte[] NewClientSeed() => RandomNumberGenerator.GetBytes(ClientSeedSize);

    public static byte[] Commit(ReadOnlySpan<byte> serverSeed)
    {
        var buf = new byte[CommitLabel.Length + serverSeed.Length];
        CommitLabel.CopyTo(buf, 0);
        serverSeed.CopyTo(buf.AsSpan(CommitLabel.Length));
        return SHA256.HashData(buf).AsSpan(0, CommitSize).ToArray();
    }

    public static bool CheckCommit(ReadOnlySpan<byte> serverSeed, ReadOnlySpan<byte> commit) =>
        commit.Length == CommitSize && CryptographicOperations.FixedTimeEquals(Commit(serverSeed), commit);

    /// <summary>Key for the round's shuffle. Client seeds are used in seat order, whatever order they arrived in.</summary>
    public static byte[] DeckKey(ReadOnlySpan<byte> serverSeed, ushort tableId, long round, IEnumerable<SeatSeed> clientSeeds)
    {
        var w = new ByteWriter();
        w.Bytes(DeckLabel);
        w.U16(tableId);
        w.VarU((ulong)round);
        foreach (var s in clientSeeds.OrderBy(s => s.Seat))
        {
            w.U8(s.Seat);
            w.Blob(s.Seed);
        }
        return HMACSHA256.HashData(serverSeed, w.ToArray());
    }

    /// <summary>Key for an extra shoe appended when a round runs out of cards (very rare with one deck).</summary>
    public static byte[] ExtensionKey(byte[] deckKey, int index)
    {
        Span<byte> data = stackalloc byte[8];
        "ext\0"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data[4..], index);
        return HMACSHA256.HashData(deckKey, data);
    }

    /// <summary>Fisher–Yates shuffle of <paramref name="decks"/> decks driven by <see cref="FairRng"/>.</summary>
    public static Card[] Shuffle(ReadOnlySpan<byte> deckKey, int decks)
    {
        var cards = Decks.Ordered(decks);
        var rng = new FairRng(deckKey);
        for (var i = cards.Length - 1; i > 0; i--)
        {
            var j = rng.NextInt(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        return cards;
    }

    public static Card[] Deck(FairReveal reveal, ushort tableId, long round, int decks) =>
        Shuffle(DeckKey(reveal.ServerSeed, tableId, round, reveal.ClientSeeds), decks);

    public static Card[] Deck(byte[] serverSeed, IEnumerable<SeatSeed> clientSeeds, ushort tableId, long round, int decks) =>
        Shuffle(DeckKey(serverSeed, tableId, round, clientSeeds), decks);
}
