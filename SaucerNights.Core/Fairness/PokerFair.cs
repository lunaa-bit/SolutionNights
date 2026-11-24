using System.Security.Cryptography;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Fairness;

/// <summary>
/// Provably fair hold'em that keeps unshown hands secret.
/// <para>
/// A plain "reveal the seed" scheme lets anyone rebuild the whole deck afterwards, folded hands included. Here the
/// host instead commits, before any player seed arrives, to a secret assignment of deck positions: which position of
/// the (not yet known) shuffled deck becomes the flop, the turn, the river and each seat's hole cards. The deck itself
/// comes from the server seed and the players' seeds as usual. Afterwards the host opens only the entries for cards
/// that are public (the board that was dealt and the hands that were shown); every other entry stays a salted hash.
/// The host could not aim cards at anyone because it fixed the positions before the deck existed, and unshown cards
/// cannot be worked out because their positions are never published.
/// </para>
/// </summary>
public static class PokerFair
{
    public const int MaskSeedSize = 16;
    public const int SaltSize = 8;
    public const int LeafHashSize = 10;
    public const int BoardLeaves = 3;
    public const int MaxSeats = 13;

    public const int FlopLeaf = 0;
    public const int TurnLeaf = 1;
    public const int RiverLeaf = 2;

    private static readonly byte[] CommitLabel = "SaucerNights/poker-commit/v2"u8.ToArray();
    private static readonly byte[] LeafLabel = "SaucerNights/poker-leaf/v2"u8.ToArray();
    private static readonly byte[] PermLabel = "SaucerNights/poker-perm/v2"u8.ToArray();
    private static readonly byte[] SaltLabel = "SaucerNights/poker-salt/v2"u8.ToArray();

    public static int SeatLeaf(int seat) => BoardLeaves + seat;

    public static int LeafCount(int seats) => BoardLeaves + seats;

    /// <summary>How many deck positions a leaf covers: three for the flop, one for turn and river, two per seat.</summary>
    public static int LeafSize(int leaf) => leaf == FlopLeaf ? 3 : leaf < BoardLeaves ? 1 : 2;

    public static byte[] NewMaskSeed() => RandomNumberGenerator.GetBytes(MaskSeedSize);

    public static byte[] LeafHash(ushort tableId, long hand, int leaf, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> positions)
    {
        var w = new ByteWriter();
        w.Bytes(LeafLabel);
        w.U16(tableId);
        w.VarU((ulong)hand);
        w.U8(leaf);
        w.Bytes(salt);
        w.Bytes(positions);
        return SHA256.HashData(w.Span).AsSpan(0, LeafHashSize).ToArray();
    }

    public static byte[] Commit(ReadOnlySpan<byte> serverSeed, IEnumerable<byte[]> leafHashes)
    {
        var w = new ByteWriter();
        w.Bytes(CommitLabel);
        w.Bytes(serverSeed);
        foreach (var h in leafHashes) w.Bytes(h);
        return SHA256.HashData(w.Span).AsSpan(0, Fair.CommitSize).ToArray();
    }

    /// <summary>The host's secret for one hand: which deck position each leaf uses, and the salts hiding them.</summary>
    public sealed class Mask
    {
        private readonly byte[][] _positions;
        private readonly byte[][] _salts;
        private readonly byte[][] _hashes;

        public Mask(ReadOnlySpan<byte> maskSeed, ushort tableId, long hand, int seats)
        {
            seats = Math.Clamp(seats, 2, MaxSeats);
            var seed = maskSeed.ToArray();
            var rng = new FairRng(HMACSHA256.HashData(seed, PermLabel));
            var perm = new byte[52];
            for (var i = 0; i < 52; i++) perm[i] = (byte)i;
            for (var i = 51; i > 0; i--)
            {
                var j = rng.NextInt(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }

            var n = LeafCount(seats);
            _positions = new byte[n][];
            _salts = new byte[n][];
            _hashes = new byte[n][];
            var next = 0;
            for (var leaf = 0; leaf < n; leaf++)
            {
                _positions[leaf] = perm.AsSpan(next, LeafSize(leaf)).ToArray();
                next += LeafSize(leaf);
                _salts[leaf] = HMACSHA256.HashData(seed, (byte[])[.. SaltLabel, (byte)leaf]).AsSpan(0, SaltSize).ToArray();
                _hashes[leaf] = LeafHash(tableId, hand, leaf, _salts[leaf], _positions[leaf]);
            }
        }

        public int Leaves => _positions.Length;

        public byte[] Commit(ReadOnlySpan<byte> serverSeed) => PokerFair.Commit(serverSeed, _hashes);

        /// <summary>The cards a leaf deals from the shuffled deck.</summary>
        public Card[] Cards(Card[] deck, int leaf) => _positions[leaf].Select(p => deck[p]).ToArray();

        public PokerLeaf Leaf(int leaf, bool open) => open
            ? new PokerLeaf { Open = true, Salt = _salts[leaf], Positions = _positions[leaf] }
            : new PokerLeaf { Hash = _hashes[leaf] };
    }
}

/// <summary>One entry of a hold'em reveal: opened (salt and deck positions) or still hidden (hash only).</summary>
public sealed class PokerLeaf
{
    public bool Open;
    public byte[] Salt = [];
    public byte[] Positions = [];
    public byte[] Hash = [];
}

/// <summary>What the host publishes at the end of a hold'em hand.</summary>
public sealed class PokerReveal
{
    public byte[] ServerSeed = [];
    public List<SeatSeed> ClientSeeds = [];
    public List<PokerLeaf> Leaves = [];

    public int OpenCount => Leaves.Count(l => l.Open);

    public void Write(ByteWriter w)
    {
        w.Bytes(ServerSeed.AsSpan(0, Fair.ServerSeedSize));
        w.U8(ClientSeeds.Count);
        foreach (var s in ClientSeeds)
        {
            w.U8(s.Seat);
            w.Bytes(s.Seed.AsSpan(0, Fair.ClientSeedSize));
        }
        w.U8(Leaves.Count);
        var mask = 0;
        for (var i = 0; i < Leaves.Count; i++)
            if (Leaves[i].Open) mask |= 1 << i;
        w.U16(mask);
        for (var i = 0; i < Leaves.Count; i++)
        {
            var l = Leaves[i];
            if (l.Open)
            {
                w.Bytes(l.Salt.AsSpan(0, PokerFair.SaltSize));
                w.Bytes(l.Positions.AsSpan(0, PokerFair.LeafSize(i)));
            }
            else
            {
                w.Bytes(l.Hash.AsSpan(0, PokerFair.LeafHashSize));
            }
        }
    }

    public static PokerReveal Read(ByteReader r)
    {
        var rev = new PokerReveal { ServerSeed = r.Fixed(Fair.ServerSeedSize) };
        var seeds = r.U8();
        for (var i = 0; i < seeds; i++)
            rev.ClientSeeds.Add(new SeatSeed(r.U8(), r.Fixed(Fair.ClientSeedSize)));
        var n = r.U8();
        if (n > PokerFair.LeafCount(PokerFair.MaxSeats)) throw new ProtocolException("Too many reveal entries.");
        var mask = r.U16();
        for (var i = 0; i < n; i++)
        {
            rev.Leaves.Add((mask & (1 << i)) != 0
                ? new PokerLeaf { Open = true, Salt = r.Fixed(PokerFair.SaltSize), Positions = r.Fixed(PokerFair.LeafSize(i)) }
                : new PokerLeaf { Hash = r.Fixed(PokerFair.LeafHashSize) });
        }
        return rev;
    }
}
