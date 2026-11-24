using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Net;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.App;

/// <summary>What the UI needs from the game client (implemented by the plugin, faked by the preview tool).</summary>
public interface IGameBridge
{
    bool LoggedIn { get; }

    /// <summary>"Name@World".</summary>
    string SelfId { get; }

    string SelfName { get; }

    /// <summary>The linkshell's own name for a channel, if known (e.g. "Casino Night").</summary>
    string ChannelName(ChatChannel channel);

    /// <summary>Posts the player's own chat message to the configured channel.</summary>
    void SendChat(string text);

    /// <summary>Plays one of the game's &lt;se.1&gt;–&lt;se.16&gt; sound effects.</summary>
    void PlaySound(int effect);
}

public sealed record ChatLine(DateTime At, string Sender, string Text);

public sealed record NoticeLine(DateTime At, string Text);

/// <summary>The plugin's state shared by every window: settings, the table hub, chat and notices.</summary>
public sealed class GameApp : IDisposable
{
    private readonly Action _save;
    private bool _wasMyTurn;
    private long _lastWinRound = -1;

    public GameApp(IGameBridge bridge, Settings settings, Action save, IChatLink link, IClock clock)
    {
        Bridge = bridge;
        Settings = settings;
        _save = save;
        Clock = clock;
        Hub = new TableHub(link, clock);
        Hub.Outbox.MinInterval = TimeSpan.FromMilliseconds(Math.Clamp(settings.SendIntervalMs, 600, 5000));
        Hub.Notice += Notify;
    }

    public IGameBridge Bridge { get; }

    public Settings Settings { get; }

    public IClock Clock { get; }

    public TableHub Hub { get; }

    public List<ChatLine> Chat { get; } = [];

    public List<NoticeLine> Notices { get; } = [];

    /// <summary>Raised when a table should be shown (hosting, joining or practice started).</summary>
    public event Action? TableOpened;

    public string ChannelLabel
    {
        get
        {
            var name = Bridge.ChannelName(Settings.Channel);
            return string.IsNullOrWhiteSpace(name) ? ChatChannels.Short(Settings.Channel) : $"{ChatChannels.Short(Settings.Channel)} · {name}";
        }
    }

    public void Save()
    {
        Hub.Outbox.MinInterval = TimeSpan.FromMilliseconds(Math.Clamp(Settings.SendIntervalMs, 600, 5000));
        _save();
    }

    public void Notify(string text)
    {
        Notices.Add(new NoticeLine(Clock.Now, text));
        if (Notices.Count > 100) Notices.RemoveAt(0);
    }

    /// <summary>A normal (non-protocol) chat line on the game channel, shown in the table's chat tab.</summary>
    public void OnChannelChat(string sender, string text)
    {
        Chat.Add(new ChatLine(Clock.Now, sender, text));
        if (Chat.Count > 200) Chat.RemoveAt(0);
    }

    public void Update()
    {
        Hub.Update();
        Alerts();
    }

    /// <summary>Plays the "your turn" sound once when it becomes the player's turn.</summary>
    private void Alerts()
    {
        var t = Hub.Table;
        var mine = false;
        if (t is { IsSeated: true })
        {
            if (t.Blackjack is { } b)
            {
                var seat = b.Seat(t.MySeat);
                mine = (b.Phase == BjPhase.PlayerTurns && b.ActiveSeat == t.MySeat)
                       || (b.Phase == BjPhase.Insurance && seat?.InsuranceState == InsuranceState.Undecided);
                if (b.Phase == BjPhase.Settled && seat is { RoundNet: > 0 } && _lastWinRound != b.Round)
                {
                    _lastWinRound = b.Round;
                    if (Settings.WinSound) Bridge.PlaySound(Settings.WinSoundEffect);
                }
            }
            else if (t.Poker is { } p)
            {
                mine = p.BettingRound && p.ActiveSeat == t.MySeat;
                if (p.Phase == PokerPhase.Showdown && p.Seat(t.MySeat) is { Won: > 0 } && _lastWinRound != p.Hand)
                {
                    _lastWinRound = p.Hand;
                    if (Settings.WinSound) Bridge.PlaySound(Settings.WinSoundEffect);
                }
            }
        }
        if (mine && !_wasMyTurn && Settings.TurnSound && !Hub.IsPractice) Bridge.PlaySound(Settings.TurnSoundEffect);
        _wasMyTurn = mine;
    }

    // ───────────── Commands ─────────────

    public string? CanUseChannel()
    {
        if (!Bridge.LoggedIn) return "Log in to a character first.";
        return null;
    }

    /// <summary>Starting anything else would close the table we're hosting, so refuse until it's closed.</summary>
    private bool HostingBlocks()
    {
        if (!Hub.IsHosting) return false;
        Notify("You're hosting a table. Close it first (Host tab, or /snights leave).");
        return true;
    }

    public void HostBlackjack()
    {
        if (HostingBlocks()) return;
        var rules = Settings.Blackjack.Normalized();
        Hub.HostTable(new BlackjackEngine(rules), Options("Blackjack"));
        TableOpened?.Invoke();
    }

    public void HostPoker()
    {
        if (HostingBlocks()) return;
        var rules = Settings.Poker.Normalized();
        Hub.HostTable(new PokerEngine(rules), Options("Hold'em"));
        TableOpened?.Invoke();
    }

    private HostOptions Options(string game) => new()
    {
        HostName = Bridge.SelfName,
        Title = string.IsNullOrWhiteSpace(Settings.TableTitle) ? $"{Bridge.SelfName}'s {game}" : Settings.TableTitle.Trim(),
        AutoApproveRebuys = Settings.AutoApproveRebuys,
    };

    public void Join(TableInfo table, bool spectate)
    {
        if (HostingBlocks()) return;
        Hub.JoinTable(table, spectate);
        TableOpened?.Invoke();
    }

    public void Practice(GameKind kind)
    {
        if (HostingBlocks()) return;
        var poker = Settings.Poker.Normalized();
        poker.AutoStart = true;
        Hub.StartPractice(kind, Math.Clamp(Settings.PracticeBots, 1, 6), Tweak(Settings.Blackjack.Normalized()), poker);
        TableOpened?.Invoke();
    }

    private static BlackjackRules Tweak(BlackjackRules r)
    {
        r.AutoDeal = true;
        return r;
    }

    public void Leave() => Hub.LeaveTable();

    public void Dispose() => Hub.Dispose();
}
