using SaucerNights.Core.Sessions;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Net;

/// <summary>
/// An in-memory stand-in for a linkshell: connects a host and any number of players, with latency and (optionally)
/// the real chat path — rate limiting, text encoding, fragmentation and reassembly. Used by practice tables and tests.
/// </summary>
public sealed class LoopbackNetwork
{
    private sealed class Participant
    {
        public required string Id;
        public ClientSession? Client;
        public readonly ChatOutbox Outbox = new();
        public readonly FrameAssembler Assembler = new();
    }

    private readonly record struct Delivery(DateTime At, string Sender, string? Line, Frame? Frame, string? Target);

    private readonly HostSession _host;
    private readonly List<Participant> _participants = [];
    private readonly Participant _hostP;
    private readonly List<Delivery> _queue = [];
    private readonly Random _jitter;

    public LoopbackNetwork(HostSession host, TimeSpan latency, bool chatEncoding = false, TimeSpan? rateLimit = null, int seed = 1)
    {
        _host = host;
        Latency = latency;
        ChatEncoding = chatEncoding;
        _jitter = new Random(seed);
        _hostP = new Participant { Id = host.HostId };
        _hostP.Outbox.MinInterval = rateLimit ?? TimeSpan.Zero;
        RateLimit = rateLimit ?? TimeSpan.Zero;
    }

    public TimeSpan Latency { get; set; }

    public bool ChatEncoding { get; }

    public TimeSpan RateLimit { get; }

    /// <summary>Longest chat line produced so far (chat encoding mode).</summary>
    public int LongestLine { get; private set; }

    public long LinesSent { get; private set; }

    /// <summary>Fraction (0–1) of lines to drop at random, to test recovery.</summary>
    public double DropRate { get; set; }

    public void Add(ClientSession client)
    {
        var p = new Participant { Id = client.SelfId, Client = client };
        p.Outbox.MinInterval = RateLimit;
        _participants.Add(p);
    }

    public void Remove(ClientSession client) => _participants.RemoveAll(p => p.Client == client);

    private DateTime Arrival(DateTime now) => now + Latency + TimeSpan.FromMilliseconds(_jitter.Next(0, 40));

    public void Tick(DateTime now)
    {
        _host.Tick(now);
        foreach (var p in _participants) p.Client!.Tick(now);

        // Host → everyone.
        if (ChatEncoding)
        {
            var line = _hostP.Outbox.Next(now, [_host.NextChatFrame]);
            if (line != null) Broadcast(now, _hostP.Id, line);
        }
        else
        {
            while (_host.NextChatFrame(now) is { } f)
                foreach (var p in _participants)
                    _queue.Add(new Delivery(Arrival(now), _host.HostId, null, f, p.Id));
        }

        // Players → host (and, in chat mode, everyone else on the channel too).
        foreach (var p in _participants)
        {
            if (ChatEncoding)
            {
                var line = p.Outbox.Next(now, [p.Client!.NextFrame]);
                if (line != null) Broadcast(now, p.Id, line);
            }
            else
            {
                while (p.Client!.NextFrame(now) is { } f) _queue.Add(new Delivery(Arrival(now), p.Id, null, f, _host.HostId));
            }
        }

        // Deliver whatever has arrived, in send order.
        var due = _queue.Where(d => d.At <= now).ToList();
        _queue.RemoveAll(d => d.At <= now);
        foreach (var d in due) Deliver(d, now);
    }

    private void Broadcast(DateTime now, string sender, string line)
    {
        LinesSent++;
        LongestLine = Math.Max(LongestLine, line.Length);
        if (DropRate > 0 && _jitter.NextDouble() < DropRate) return;
        var at = Arrival(now);
        if (!string.Equals(sender, _host.HostId, StringComparison.OrdinalIgnoreCase)) _queue.Add(new Delivery(at, sender, line, null, _host.HostId));
        foreach (var p in _participants.Where(p => p.Id != sender)) _queue.Add(new Delivery(at, sender, line, null, p.Id));
    }

    private void Deliver(Delivery d, DateTime now)
    {
        var toHost = string.Equals(d.Target, _host.HostId, StringComparison.OrdinalIgnoreCase);
        var target = toHost ? _hostP : _participants.FirstOrDefault(p => p.Id == d.Target);
        if (target == null) return;
        var frame = d.Frame ?? target.Assembler.Accept(d.Sender, d.Line!, now);
        if (frame == null) return;
        if (toHost) _host.OnFrame(d.Sender, frame, now);
        else target.Client!.OnFrame(d.Sender, frame, now);
    }
}
