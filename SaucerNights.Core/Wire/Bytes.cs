using System.Buffers.Binary;
using System.Text;
using SaucerNights.Core.Cards;

namespace SaucerNights.Core.Wire;

public sealed class ProtocolException(string message) : Exception(message);

/// <summary>Little-endian binary writer with LEB128 varints, used for every protocol message.</summary>
public sealed class ByteWriter
{
    private byte[] _buf = new byte[128];
    private int _len;

    public int Length => _len;

    public ReadOnlySpan<byte> Span => _buf.AsSpan(0, _len);

    private Span<byte> Grow(int n)
    {
        if (_len + n > _buf.Length) Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + n));
        var s = _buf.AsSpan(_len, n);
        _len += n;
        return s;
    }

    public void U8(int v) => Grow(1)[0] = (byte)v;

    public void Bool(bool v) => U8(v ? 1 : 0);

    public void U16(int v) => BinaryPrimitives.WriteUInt16LittleEndian(Grow(2), (ushort)v);

    public void U32(uint v) => BinaryPrimitives.WriteUInt32LittleEndian(Grow(4), v);

    public void VarU(ulong v)
    {
        while (v >= 0x80)
        {
            U8((int)(v & 0x7F) | 0x80);
            v >>= 7;
        }
        U8((int)v);
    }

    public void VarU(long v) => VarU((ulong)Math.Max(0, v));

    public void VarS(long v) => VarU((ulong)((v << 1) ^ (v >> 63)));

    public void Bytes(ReadOnlySpan<byte> data) => data.CopyTo(Grow(data.Length));

    /// <summary>Length-prefixed bytes.</summary>
    public void Blob(ReadOnlySpan<byte> data)
    {
        VarU((ulong)data.Length);
        Bytes(data);
    }

    /// <summary>Length-prefixed UTF-8, cut to <paramref name="maxBytes"/> on a character boundary.</summary>
    public void Str(string? s, int maxBytes = 64)
    {
        s ??= string.Empty;
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length > maxBytes)
        {
            var cut = maxBytes;
            while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;
            bytes = bytes.AsSpan(0, cut).ToArray();
        }
        Blob(bytes);
    }

    public void Cards(IReadOnlyList<Card> cards)
    {
        U8(cards.Count);
        foreach (var c in cards) U8(c.Code);
    }

    public byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();
}

/// <summary>Reader matching <see cref="ByteWriter"/>; throws <see cref="ProtocolException"/> on malformed input.</summary>
public sealed class ByteReader(byte[] data, int offset = 0, int? length = null)
{
    private readonly int _end = offset + (length ?? data.Length - offset);
    private int _pos = offset;

    public bool AtEnd => _pos >= _end;

    public int Remaining => _end - _pos;

    private void Need(int n)
    {
        if (_pos + n > _end) throw new ProtocolException("Message is truncated.");
    }

    public byte U8()
    {
        Need(1);
        return data[_pos++];
    }

    public bool Bool() => U8() != 0;

    public ushort U16()
    {
        Need(2);
        var v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_pos));
        _pos += 2;
        return v;
    }

    public uint U32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_pos));
        _pos += 4;
        return v;
    }

    public ulong VarU()
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var b = U8();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new ProtocolException("Varint is too long.");
    }

    /// <summary>Unsigned varint as a non-negative long (values above <see cref="long.MaxValue"/> are rejected).</summary>
    public long VarL()
    {
        var v = VarU();
        if (v > long.MaxValue) throw new ProtocolException("Number out of range.");
        return (long)v;
    }

    public int VarI()
    {
        var v = VarU();
        if (v > int.MaxValue) throw new ProtocolException("Number out of range.");
        return (int)v;
    }

    public long VarS()
    {
        var u = VarU();
        return (long)(u >> 1) ^ -(long)(u & 1);
    }

    public byte[] Fixed(int n)
    {
        Need(n);
        var b = data.AsSpan(_pos, n).ToArray();
        _pos += n;
        return b;
    }

    public byte[] Blob(int maxLength = 4096)
    {
        var n = VarU();
        if (n > (ulong)maxLength) throw new ProtocolException("Blob is too long.");
        return Fixed((int)n);
    }

    public string Str(int maxBytes = 256) => Encoding.UTF8.GetString(Blob(maxBytes));

    public List<Card> Cards()
    {
        var n = U8();
        var list = new List<Card>(n);
        for (var i = 0; i < n; i++)
        {
            var code = U8();
            if (code > Card.HiddenCode) throw new ProtocolException("Bad card code.");
            list.Add(new Card(code));
        }
        return list;
    }

    public byte[] Rest() => Fixed(Remaining);
}

/// <summary>CRC-16/CCITT-FALSE.</summary>
public static class Crc16
{
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}

/// <summary>
/// Base64-style text encoding with an alphabet chosen to survive FFXIV chat: no vowels (so the profanity filter can't
/// match words), no '&lt;' / '&gt;' (auto-translate / placeholders like &lt;t&gt;), no '/', '*', '%' or quotes.
/// </summary>
public static class ChatBase64
{
    public const string Alphabet = "BCDFGHJKLMNPQRSTVWXZbcdfghjklmnpqrstvwxz0123456789-_.,!=+()[]:;?";

    private static readonly sbyte[] Reverse = BuildReverse();

    private static sbyte[] BuildReverse()
    {
        var r = new sbyte[128];
        Array.Fill(r, (sbyte)-1);
        for (var i = 0; i < Alphabet.Length; i++) r[Alphabet[i]] = (sbyte)i;
        return r;
    }

    public static int EncodedLength(int bytes) => bytes / 3 * 4 + (bytes % 3) switch { 1 => 2, 2 => 3, _ => 0 };

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(EncodedLength(data.Length));
        var i = 0;
        for (; i + 3 <= data.Length; i += 3)
        {
            var v = data[i] << 16 | data[i + 1] << 8 | data[i + 2];
            sb.Append(Alphabet[(v >> 18) & 63]).Append(Alphabet[(v >> 12) & 63]).Append(Alphabet[(v >> 6) & 63]).Append(Alphabet[v & 63]);
        }
        var rem = data.Length - i;
        if (rem == 1)
        {
            var v = data[i] << 16;
            sb.Append(Alphabet[(v >> 18) & 63]).Append(Alphabet[(v >> 12) & 63]);
        }
        else if (rem == 2)
        {
            var v = data[i] << 16 | data[i + 1] << 8;
            sb.Append(Alphabet[(v >> 18) & 63]).Append(Alphabet[(v >> 12) & 63]).Append(Alphabet[(v >> 6) & 63]);
        }
        return sb.ToString();
    }

    public static bool TryDecode(ReadOnlySpan<char> text, out byte[] data)
    {
        data = [];
        if (text.Length % 4 == 1) return false;
        var full = text.Length / 4;
        var rem = text.Length % 4;
        var result = new byte[full * 3 + (rem == 2 ? 1 : rem == 3 ? 2 : 0)];
        var o = 0;
        var i = 0;
        for (var g = 0; g < full; g++, i += 4)
        {
            int a = Val(text[i]), b = Val(text[i + 1]), c = Val(text[i + 2]), d = Val(text[i + 3]);
            if ((a | b | c | d) < 0) return false;
            var v = a << 18 | b << 12 | c << 6 | d;
            result[o++] = (byte)(v >> 16);
            result[o++] = (byte)(v >> 8);
            result[o++] = (byte)v;
        }
        if (rem >= 2)
        {
            int a = Val(text[i]), b = Val(text[i + 1]);
            var c = rem == 3 ? Val(text[i + 2]) : 0;
            if ((a | b | c) < 0) return false;
            var v = a << 18 | b << 12 | c << 6;
            result[o++] = (byte)(v >> 16);
            if (rem == 3) result[o] = (byte)(v >> 8);
        }
        data = result;
        return true;
    }

    private static int Val(char ch) => ch < 128 ? Reverse[ch] : -1;
}
