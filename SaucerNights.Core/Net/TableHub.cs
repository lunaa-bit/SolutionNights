using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Bots;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;
using SaucerNights.Core.Wire;

namespace SaucerNights.Core.Net;

/// <summary>What the hub needs from the game: who we are and a way to post a line to the chosen channel.</summary>
public interface IChatLink
{
    /// <summary>True when logged in and able to talk on the channel.</summary>
    bool Ready { get; }

    /// <summary>"Name@World" of the local character.</summary>
    string SelfId { get; }

    void Send(string line);
}

/// <summary>An offline table against computer players, connected through an in-memory linkshell.</summary>
public sealed class PracticeTable : IDisposable
{
    public const string DealerId = "Dealer@Practice";
    private static readonly string[] BotNames = ["Aria Moonwhisper", "Bram Ironforge", "Cyra Tidewell", "Dax Emberlight", "Elin Frostvale", "Fenn Brightwater", "Gwyn Starfall", "Hollis Duskwind"];

    private readonly LoopbackNetwork _net;
    private readonly List<(ClientSession Client, IBot Bot)> _bots = [];

    public PracticeTable(GameKind kind, int bots, string playerId, IClock clock, BlackjackRules? bj = null, PokerRules? poker = null)
    {
        ITableGame game = kind == GameKind.Blackjack
            ? new BlackjackEngine(bj ?? new BlackjackRules { ProvablyFair = true })
            : new PokerEngine((poker ?? new PokerRules()).WithHostPlays(false));
        Host = new HostSession(game, new HostOptions
        {
            HostId = DealerId,
            HostName = "Practice dealer",
            Title = kind == GameKind.Blackjack ? "Practice blackjack" : "Practice hold'em",
            AutoApproveRebuys = true,
        }, clock);
        _net = new LoopbackNetwork(Host, TimeSpan.FromMilliseconds(120));

        Me = new ClientSession(Host.Info(clock.Now), playerId, clock);
        _net.Add(Me);
        Me.Join(kind == GameKind.Blackjack ? game.MaxSeats / 2 : 0);

        var seed = Environment.TickCount;
        var maxBots = Math.Min(bots, game.MaxSeats - 1);
        for (var i = 0; i < maxBots; i++)
        {
            var c = new ClientSession(Host.Info(clock.Now), $"{BotNames[i % BotNames.Length]}@Bot", clock);
            _net.Add(c);
            c.Join();
            IBot bot = kind == GameKind.Blackjack ? new BlackjackBot(seed + i) : new PokerBot(seed + i);
            _bots.Add((c, bot));
        }
    }

    public HostSession Host { get; }

    public ClientSession Me { get; }

    public void Tick(DateTime now)
    {
        _net.Tick(now);
        foreach (var (client, bot) in _bots) bot.Tick(client, now);
    }

    public void Dispose()
    {
        Host.Dispose();
        Me.Dispose();
        foreach (var (c, _) in _bots) c.Dispose();
    }
}

public static class RulesExtensions
{
    public static PokerRules WithHostPlays(this PokerRules r, bool hostPlays)
    {
        var copy = r.Normalized();
        copy.HostPlays = hostPlays;
        return copy;
    }
}

/// <summary>
/// Everything the plugin runs: the lobby, at most one table (hosted, joined or practice), and the chat pump that
/// decodes incoming lines and paces outgoing ones.
/// </summary>
public sealed class TableHub : IDisposable
{
    private readonly IChatLink _link;
    private readonly IClock _clock;
    private readonly FrameAssembler _assembler = new();
    private readonly Queue<Frame> _farewells = new();
    private HostSession? _closing;

    public TableHub(IChatLink link, IClock clock)
    {
        _link = link;
        _clock = clock;
    }

    public Lobby Lobby { get; } = new();

    public ChatOutbox Outbox { get; } = new();

    public HostSession? Host { get; private set; }

    /// <summary>The table we're looking at: our own (as host), a joined one, or practice.</summary>
    public ClientSession? Table => Practice?.Me ?? Client;

    public ClientSession? Client { get; private set; }

    public PracticeTable? Practice { get; private set; }

    public bool IsHosting => Host != null;

    public bool IsPractice => Practice != null;

    public int CorruptLines => _assembler.Corrupt;

    public DateTime LastLineHeard { get; private set; }

    public event Action<string>? Notice;

    private void Say(string text) => Notice?.Invoke(text);

    // ───────────── Inbound ─────────────

    /// <summary>A protocol line arrived on the channel from <paramref name="sender"/> ("Name@World").</summary>
    public void OnChatLine(string sender, string text)
    {
        var now = _clock.Now;
        if (string.Equals(sender, _link.SelfId, StringComparison.OrdinalIgnoreCase)) return; // our own echo
        var frame = _assembler.Accept(sender, text, now);
        if (frame == null) return;
        LastLineHeard = now;

        if (frame.Type == MessageType.Announce && Messages.Parse(frame) is AnnounceMsg a) Lobby.OnAnnounce(sender, frame, a, now);
        if (frame.Type == MessageType.Closed) Lobby.OnClosed(sender, frame.TableId);

        if (Host != null) Host.OnFrame(sender, frame, now);
        else if (Client != null && Practice == null) Client.OnFrame(sender, frame, now);
    }

    // ───────────── Pump ─────────────

    public void Update()
    {
        var now = _clock.Now;
        Practice?.Tick(now);

        if (Host != null)
        {
            Host.Tick(now);
            // The host's own view and moves bypass the channel.
            if (Client != null)
            {
                Client.Tick(now);
                while (Client.NextFrame(now) is { } up) Host.OnFrame(_link.SelfId, up, now);
                while (Host.NextLocalFrame(now) is { } down) Client.OnFrame(_link.SelfId, down, now);
            }
        }
        else
        {
            Client?.Tick(now);
        }

        if (_link.Ready)
        {
            var sources = new List<Func<DateTime, Frame?>> { _ => _farewells.Count > 0 ? _farewells.Dequeue() : null };
            if (_closing != null) sources.Add(_closing.NextChatFrame);
            if (Host != null) sources.Add(Host.NextChatFrame);
            else if (Client != null && Practice == null) sources.Add(Client.NextFrame);
            sources.Add(Lobby.NextFrame);
            var line = Outbox.Next(now, sources);
            if (line != null) _link.Send(line);
        }

        if (_closing is { Finished: true } && Outbox.Backlog == 0)
        {
            _closing.Dispose();
            _closing = null;
        }
    }

    // ───────────── Commands ─────────────

    public bool Busy => Host != null || Client != null || Practice != null;

    public void HostTable(ITableGame game, HostOptions options)
    {
        LeaveTable();
        options.HostId = _link.SelfId;
        Host = new HostSession(game, options, _clock);
        Host.Notice += Say;
        Client = new ClientSession(Host.Info(_clock.Now), _link.SelfId, _clock);
        Client.Notice += Say;
        if (game is PokerEngine { Rules.HostPlays: true }) Client.Join(0);
        Say($"Hosting \"{options.Title}\". Players on your linkshell can now find it in their lobby.");
    }

    public void JoinTable(TableInfo table, bool spectateOnly)
    {
        LeaveTable();
        Client = new ClientSession(table, _link.SelfId, _clock);
        Client.Notice += Say;
        if (!spectateOnly) Client.Join();
        Client.RequestResync(ResyncNeed.Roster | ResyncNeed.State);
    }

    public void StartPractice(GameKind kind, int bots, BlackjackRules? bj = null, PokerRules? poker = null)
    {
        LeaveTable();
        var id = string.IsNullOrEmpty(_link.SelfId) ? "You@Practice" : _link.SelfId;
        Practice = new PracticeTable(kind, bots, id, _clock, bj, poker);
        Practice.Me.Notice += Say;
    }

    /// <summary>Leaves whatever table is open (closing it if we host it).</summary>
    public void LeaveTable()
    {
        if (Practice != null)
        {
            Practice.Dispose();
            Practice = null;
        }
        if (Host != null)
        {
            // The closed host keeps talking until its "table closed" line is out.
            Host.Close();
            _closing?.Dispose();
            _closing = Host;
            Host = null;
            Client?.Dispose();
            Client = null;
            return;
        }
        if (Client != null)
        {
            Client.Leave();
            while (Client.NextFrame(_clock.Now) is { } f) _farewells.Enqueue(f);
            Client.Dispose();
            Client = null;
        }
    }

    /// <summary>Hosts that are still announcing that they closed.</summary>
    public bool Closing => _closing != null;

    public void Dispose()
    {
        Practice?.Dispose();
        Host?.Dispose();
        _closing?.Dispose();
        Client?.Dispose();
    }
}
