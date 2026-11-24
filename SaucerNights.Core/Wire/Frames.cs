namespace SaucerNights.Core.Wire;

/// <summary>One protocol message as it travels: table, sender sequence number, type and payload.</summary>
public sealed record Frame(ushort TableId, uint Seq, Protocol.MessageType Type, byte[] Payload);

/// <summary>
/// Turns frames into chat lines and back. A line is <c>SN1~</c> followed by <see cref="ChatBase64"/> of
/// <c>[u16 table][varint seq][u8 type][payload][u16 crc]</c>. Frames too long for one line are split into fragments.
/// </summary>
public static class FrameCodec
{
    public const string Prefix = "SN1~";

    /// <summary>
    /// Longest line we send. The game's limit is 500 bytes for the whole chat input, which also holds the channel
    /// command (e.g. "/cwlinkshell1 "), so stay well under it.
    /// </summary>
    public const int DefaultMaxLineChars = 440;

    public static bool LooksLikeProtocol(string text) => text.StartsWith("SN", StringComparison.Ordinal) && text.Length > 4 && text[3] == '~';

    public static byte[] ToBytes(Frame f)
    {
        var w = new ByteWriter();
        w.U16(f.TableId);
        w.VarU(f.Seq);
        w.U8((byte)f.Type);
        w.Bytes(f.Payload);
        w.U16(Crc16.Compute(w.Span));
        return w.ToArray();
    }

    public static bool TryFromBytes(byte[] bytes, out Frame frame)
    {
        frame = null!;
        if (bytes.Length < 6) return false;
        var crc = (ushort)(bytes[^2] | bytes[^1] << 8);
        if (Crc16.Compute(bytes.AsSpan(0, bytes.Length - 2)) != crc) return false;
        try
        {
            var r = new ByteReader(bytes, 0, bytes.Length - 2);
            var table = r.U16();
            var seq = (uint)r.VarU();
            var type = (Protocol.MessageType)r.U8();
            frame = new Frame(table, seq, type, r.Rest());
            return true;
        }
        catch (ProtocolException)
        {
            return false;
        }
    }

    public static string ToLine(byte[] frameBytes) => Prefix + ChatBase64.Encode(frameBytes);

    /// <summary>Encodes a frame as one line, or several fragment lines when it does not fit.</summary>
    public static List<string> ToLines(Frame f, ref byte fragCounter, int maxLineChars = DefaultMaxLineChars)
    {
        var bytes = ToBytes(f);
        var line = ToLine(bytes);
        if (line.Length <= maxLineChars) return [line];

        // Fragment frame overhead: table(2) + seq(1) + type(1) + fragId/index/count(3) + crc(2) = 9 bytes.
        var chunk = (maxLineChars - Prefix.Length) * 3 / 4 - 9;
        var count = (bytes.Length + chunk - 1) / chunk;
        if (count > 255) throw new InvalidOperationException("Frame is too large to send.");
        var fragId = ++fragCounter;
        var lines = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var w = new ByteWriter();
            w.U8(fragId);
            w.U8(i);
            w.U8(count);
            var start = i * chunk;
            w.Bytes(bytes.AsSpan(start, Math.Min(chunk, bytes.Length - start)));
            lines.Add(ToLine(ToBytes(new Frame(f.TableId, 0, Protocol.MessageType.Fragment, w.ToArray()))));
        }
        return lines;
    }

    public static bool TryParseLine(string line, out Frame frame)
    {
        frame = null!;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        return ChatBase64.TryDecode(line.AsSpan(Prefix.Length).Trim(), out var bytes) && TryFromBytes(bytes, out frame);
    }
}

/// <summary>Decodes incoming lines per sender and stitches fragments back together.</summary>
public sealed class FrameAssembler
{
    private sealed class Partial
    {
        public byte[]?[] Parts = [];
        public DateTime Started;
    }

    private readonly Dictionary<(string Sender, ushort Table, byte Id), Partial> _partials = new();

    /// <summary>Count of lines that looked like ours but failed to decode (e.g. mangled by a chat filter).</summary>
    public int Corrupt { get; private set; }

    public Frame? Accept(string sender, string line, DateTime now)
    {
        if (!FrameCodec.TryParseLine(line, out var frame))
        {
            Corrupt++;
            return null;
        }
        if (frame.Type != Protocol.MessageType.Fragment) return frame;
        if (frame.Payload.Length < 4) return null;

        var id = frame.Payload[0];
        int index = frame.Payload[1], count = frame.Payload[2];
        if (count == 0 || index >= count) return null;

        Expire(now);
        var key = (sender, frame.TableId, id);
        if (!_partials.TryGetValue(key, out var p) || p.Parts.Length != count)
            _partials[key] = p = new Partial { Parts = new byte[]?[count], Started = now };
        p.Parts[index] = frame.Payload[3..];
        if (p.Parts.Any(x => x == null)) return null;

        _partials.Remove(key);
        var whole = p.Parts.SelectMany(x => x!).ToArray();
        if (FrameCodec.TryFromBytes(whole, out var inner)) return inner;
        Corrupt++;
        return null;
    }

    private void Expire(DateTime now)
    {
        foreach (var k in _partials.Where(kv => now - kv.Value.Started > TimeSpan.FromSeconds(60)).Select(kv => kv.Key).ToList())
            _partials.Remove(k);
    }
}
