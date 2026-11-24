using System.Security.Cryptography;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Crypto;

/// <summary>
/// An ephemeral P-256 key pair. The host and each player agree on a shared key (ECDH) over the public channel, which
/// the host uses to hide that player's hole cards from everyone else reading the linkshell.
/// </summary>
public sealed class SessionKeys : IDisposable
{
    public const int PublicKeySize = 64;

    private static readonly byte[] Label = "SaucerNights/ecdh/v1"u8.ToArray();
    private readonly ECDiffieHellman _ecdh;

    public SessionKeys()
    {
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = _ecdh.ExportParameters(false);
        PublicKey = [.. p.Q.X!, .. p.Q.Y!];
    }

    /// <summary>Uncompressed public point, X ‖ Y (32 bytes each).</summary>
    public byte[] PublicKey { get; }

    /// <summary>Derives the 32-byte shared key with a peer, or null when the peer key is malformed.</summary>
    public byte[]? TryDerive(ReadOnlySpan<byte> peerPublicKey)
    {
        if (peerPublicKey.Length != PublicKeySize) return null;
        try
        {
            using var peer = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = peerPublicKey[..32].ToArray(), Y = peerPublicKey[32..].ToArray() },
            });
            return _ecdh.DeriveKeyFromHash(peer.PublicKey, HashAlgorithmName.SHA256, Label, null);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException or ArgumentException)
        {
            // Not a point on the curve.
            return null;
        }
    }

    public void Dispose() => _ecdh.Dispose();
}

/// <summary>
/// Seals a few card bytes for one seat: XOR with an HMAC-derived key stream plus a 2-byte tag so a wrong key is
/// detected. Every (table, hand, seat) uses a fresh stream, so the same key is never reused.
/// </summary>
public static class CardSeal
{
    private static readonly byte[] StreamLabel = "SaucerNights/hole/v1"u8.ToArray();
    private static readonly byte[] TagLabel = "SaucerNights/tag/v1"u8.ToArray();

    public static byte[] Seal(byte[] key, ushort tableId, long hand, int seat, ReadOnlySpan<byte> cards)
    {
        var stream = Derive(key, StreamLabel, tableId, hand, seat, []);
        var sealedBytes = new byte[cards.Length + 2];
        for (var i = 0; i < cards.Length; i++) sealedBytes[i] = (byte)(cards[i] ^ stream[i]);
        var tag = Derive(key, TagLabel, tableId, hand, seat, cards);
        sealedBytes[cards.Length] = tag[0];
        sealedBytes[cards.Length + 1] = tag[1];
        return sealedBytes;
    }

    public static bool TryOpen(byte[] key, ushort tableId, long hand, int seat, ReadOnlySpan<byte> sealedBytes, out byte[] cards)
    {
        cards = [];
        if (sealedBytes.Length < 3) return false;
        var n = sealedBytes.Length - 2;
        var stream = Derive(key, StreamLabel, tableId, hand, seat, []);
        var plain = new byte[n];
        for (var i = 0; i < n; i++) plain[i] = (byte)(sealedBytes[i] ^ stream[i]);
        var tag = Derive(key, TagLabel, tableId, hand, seat, plain);
        if (tag[0] != sealedBytes[n] || tag[1] != sealedBytes[n + 1]) return false;
        cards = plain;
        return true;
    }

    private static byte[] Derive(byte[] key, byte[] label, ushort tableId, long hand, int seat, ReadOnlySpan<byte> extra)
    {
        var w = new ByteWriter();
        w.Bytes(label);
        w.U16(tableId);
        w.VarU((ulong)hand);
        w.U8(seat);
        w.Bytes(extra);
        return HMACSHA256.HashData(key, w.ToArray());
    }
}
