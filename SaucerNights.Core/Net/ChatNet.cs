using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Net;

/// <summary>
/// Paces outgoing chat lines. Every line from this client (host frames, player moves, fragments) shares one budget so
/// the game's chat spam protection is never tripped.
/// </summary>
public sealed class ChatOutbox
{
    private readonly Queue<string> _lines = new();
    private DateTime _nextAllowed;
    private byte _fragCounter;

    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(1200);

    public int MaxLineChars { get; set; } = FrameCodec.DefaultMaxLineChars;

    public long Sent { get; private set; }

    public DateTime LastSent { get; private set; }

    /// <summary>Lines already encoded and waiting for their slot (fragments of a long frame).</summary>
    public int Backlog => _lines.Count;

    public void Reset()
    {
        _lines.Clear();
        _nextAllowed = default;
    }

    /// <summary>Returns the next line to send now, or null if it's too soon or nothing is waiting.</summary>
    public string? Next(DateTime now, IEnumerable<Func<DateTime, Frame?>> sources)
    {
        if (now < _nextAllowed) return null;
        if (_lines.Count == 0)
        {
            foreach (var source in sources)
            {
                var frame = source(now);
                if (frame == null) continue;
                foreach (var l in FrameCodec.ToLines(frame, ref _fragCounter, MaxLineChars)) _lines.Enqueue(l);
                break;
            }
        }
        if (_lines.Count == 0) return null;
        _nextAllowed = now + MinInterval;
        LastSent = now;
        Sent++;
        return _lines.Dequeue();
    }
}

/// <summary>Tables heard announcing themselves on the channel.</summary>
public sealed class Lobby
{
    private readonly Dictionary<(string Host, ushort Table), TableInfo> _tables = new();
    private DateTime _lastDiscover = DateTime.MinValue;
    private bool _discoverPending;

    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(150);

    public void OnAnnounce(string sender, Frame f, AnnounceMsg a, DateTime now) =>
        _tables[(sender.ToLowerInvariant(), f.TableId)] = TableInfo.From(a, f.TableId, sender, now);

    public void OnClosed(string sender, ushort tableId) => _tables.Remove((sender.ToLowerInvariant(), tableId));

    public IReadOnlyList<TableInfo> Tables(DateTime now) =>
        _tables.Values.Where(t => now - t.LastSeen < StaleAfter).OrderByDescending(t => t.LastSeen).ToList();

    /// <summary>Asks hosts on the channel to announce (at most every 10 s).</summary>
    public bool Refresh(DateTime now)
    {
        if (now - _lastDiscover < TimeSpan.FromSeconds(10)) return false;
        _discoverPending = true;
        return true;
    }

    public DateTime LastRefresh => _lastDiscover;

    public Frame? NextFrame(DateTime now)
    {
        if (!_discoverPending) return null;
        _discoverPending = false;
        _lastDiscover = now;
        return new DiscoverMsg().ToFrame(0, 0);
    }

    public void Clear() => _tables.Clear();
}
