using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Bots;
using SaucerNights.Core.Net;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;
using SaucerNights.Ui;

namespace SaucerNights.Preview;

/// <summary>
/// Renders the plugin's windows offline. Usage: SaucerNights.Preview.exe [outDir] [scenario...]
/// Scenarios: gallery, lobby, host, settings, practice, bj-bet, bj-turn, bj-settle, pk-turn, pk-showdown (default: all).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "shots");
        var only = args.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(outDir);
        bool Want(string name) => only.Count == 0 || only.Contains(name);

        var host = new PreviewHost(1320, 860);
        if (Want("gallery"))
        {
            host.Frame(Gallery.Draw);
            host.Save(Path.Combine(outDir, "gallery.png"));
        }
        foreach (var (name, run) in Scenarios.All)
        {
            if (!Want(name)) continue;
            try
            {
                run(host, Path.Combine(outDir, name + ".png"));
                Console.WriteLine($"{name}: ok");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{name}: FAILED {ex}");
            }
        }
        return 0;
    }
}

internal sealed class FakeBridge : IGameBridge
{
    public bool LoggedIn => true;

    public string SelfId => "Kiri Valentine@Ultros";

    public string SelfName => "Kiri Valentine";

    public string ChannelName(ChatChannel channel) => channel == ChatChannel.Cwls1 ? "Casino Night" : channel == ChatChannel.Cwls2 ? "Moogle Mafia" : "";

    public void SendChat(string text) { }

    public void PlaySound(int effect) { }
}

internal sealed class FakeLink : IChatLink
{
    public bool Ready => true;

    public string SelfId => "Kiri Valentine@Ultros";

    public List<string> Sent { get; } = [];

    public void Send(string line) => Sent.Add(line);
}

/// <summary>Builds app states and renders the real windows.</summary>
internal static class Scenarios
{
    public static readonly (string Name, Action<PreviewHost, string> Run)[] All =
    [
        ("lobby", Lobby),
        ("lobby-empty", LobbyEmpty),
        ("host", HostPage),
        ("host-poker", HostPoker),
        ("settings", SettingsPage),
        ("practice", PracticePage),
        ("bj-bet", BlackjackBet),
        ("bj-turn", BlackjackTurn),
        ("bj-settle", BlackjackSettle),
        ("pk-turn", PokerTurn),
        ("pk-showdown", PokerShowdown),
        ("bj-host", BlackjackHostView),
        ("pk-show", PokerShowCards),
        ("icon", Icon),
    ];

    private static (GameApp App, ManualClock Clock) NewApp()
    {
        var clock = new ManualClock(new DateTime(2026, 11, 21, 20, 0, 0, DateTimeKind.Utc));
        var app = new GameApp(new FakeBridge(), new Settings(), () => { }, new FakeLink(), clock);
        return (app, clock);
    }

    private static void Window(string title, Vector2 pos, Vector2 size, Action body, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        Theme.Push();
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.Begin(title, flags | ImGuiWindowFlags.NoCollapse);
        Theme.AfterBegin();
        body();
        ImGui.End();
        Theme.Pop();
    }

    private static void LobbyFrame(PreviewHost host, GameApp app, LobbyView view) =>
        Window("Saucer Nights##lobby", new Vector2(20, 20), new Vector2(980, 700), () => view.Draw(app, () => { }), ImGuiWindowFlags.NoScrollbar);

    private static void TableFrame(PreviewHost host, GameApp app, TableView view) =>
        Window("Saucer Nights - Table##table", new Vector2(10, 10), new Vector2(1300, 840), () => view.Draw(app, () => { }), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

    /// <summary>Runs the app for a while (clock and UI time together), drawing frames so animations progress.</summary>
    private static void Run(PreviewHost host, GameApp app, ManualClock clock, Action frame, double seconds, Func<bool>? until = null, Action? each = null)
    {
        const double dt = 1 / 20.0;
        for (var t = 0.0; t < seconds; t += dt)
        {
            clock.Advance(TimeSpan.FromSeconds(dt));
            app.Update();
            each?.Invoke();
            host.Frame(frame, (float)dt, render: false);
            if (until != null && until()) return;
        }
        if (until != null) throw new TimeoutException("State not reached.");
    }

    private static void Announce(GameApp app, ManualClock clock, string host, string title, GameKind game, byte taken, byte max, TableFlags flags, byte[] rules)
    {
        var msg = new AnnounceMsg { Game = game, Title = title, HostName = host, SeatsTaken = taken, SeatsMax = max, Flags = flags, Rules = rules };
        app.Hub.Lobby.OnAnnounce(host, new Core.Wire.Frame((ushort)(title.Length * 977), 1, MessageType.Announce, []), msg, clock.Now);
    }

    private static void Lobby(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        Announce(app, clock, "Moira Starling@Gilgamesh", "Golden Saucer Night", GameKind.Blackjack, 4, 6, TableFlags.Open | TableFlags.ProvablyFair | TableFlags.InProgress,
            new BlackjackRules { MinBet = 50, MaxBet = 5000 }.ToBytes());
        Announce(app, clock, "Tobias Wren@Ultros", "High Rollers Hold'em", GameKind.Poker, 7, 8, TableFlags.Open | TableFlags.InProgress,
            new PokerRules { SmallBlind = 500, BigBlind = 1000 }.ToBytes());
        Announce(app, clock, "Lyse Hext@Balmung", "Friday Night Cards", GameKind.Poker, 2, 6, TableFlags.Open | TableFlags.ProvablyFair,
            new PokerRules { SmallBlind = 10, BigBlind = 20 }.ToBytes());
        var view = new LobbyView();
        for (var i = 0; i < 10; i++) host.Frame(() => LobbyFrame(host, app, view), 0.05f, render: i == 9);
        host.Save(path);
    }

    private static void LobbyEmpty(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        var view = new LobbyView();
        for (var i = 0; i < 10; i++) host.Frame(() => LobbyFrame(host, app, view), 0.05f, render: i == 9);
        host.Save(path);
    }

    private static void HostPage(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        var view = new LobbyView { Page = 1 };
        for (var i = 0; i < 10; i++) host.Frame(() => LobbyFrame(host, app, view), 0.05f, render: i == 9);
        host.Save(path);
    }

    private static void HostPoker(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        var view = new LobbyView { Page = 1 };
        // Click the poker tile by drawing once and selecting it through the public page API isn't possible; use reflection.
        typeof(LobbyView).GetField("_hostGame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(view, 1);
        for (var i = 0; i < 10; i++) host.Frame(() => LobbyFrame(host, app, view), 0.05f, render: i == 9);
        host.Save(path);
    }

    private static void SettingsPage(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        var view = new LobbyView { Page = 3 };
        for (var i = 0; i < 10; i++) host.Frame(() => LobbyFrame(host, app, view), 0.05f, render: i == 9);
        host.Save(path);
    }

    private static void PracticePage(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        var view = new LobbyView { Page = 2 };
        for (var i = 0; i < 10; i++) host.Frame(() => LobbyFrame(host, app, view), 0.05f, render: i == 9);
        host.Save(path);
    }

    private static void BlackjackBet(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Settings.PracticeBots = 4;
        app.Practice(GameKind.Blackjack);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(host, app, view), 30,
            () => app.Hub.Table?.Blackjack is { Phase: BjPhase.Betting } v && v.Seats.Count(x => x.Bet > 0) >= 2);
        Run(host, app, clock, () => TableFrame(host, app, view), 0.8);
        host.Frame(() => TableFrame(host, app, view), 0.05f);
        host.Save(path);
    }

    private static void PlayUntilMyTurn(PreviewHost host, GameApp app, ManualClock clock, TableView view, Func<BlackjackView, bool> extra)
    {
        long betRound = -1;
        Run(host, app, clock, () => TableFrame(host, app, view), 300, () =>
        {
            var s = app.Hub.Table!;
            return s.Blackjack is { Phase: BjPhase.PlayerTurns } v && v.ActiveSeat == s.MySeat && extra(v);
        }, () =>
        {
            var s = app.Hub.Table!;
            if (s.Blackjack is { Phase: BjPhase.Betting } v && v.Round != betRound && s.IsSeated)
            {
                betRound = v.Round;
                s.PlaceBet(100);
            }
            if (s.Blackjack is { Phase: BjPhase.PlayerTurns } p && p.ActiveSeat == s.MySeat && !extra(p) && !s.AwaitingHost)
                s.Act((byte)BjActionKind.Stand, 0, p.ActiveHand);
            if (s.Blackjack is { Phase: BjPhase.Insurance } i && i.Seat(s.MySeat)?.InsuranceState == InsuranceState.Undecided)
                s.Act((byte)BjActionKind.DeclineInsurance);
        });
    }

    private static void BlackjackTurn(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Practice(GameKind.Blackjack);
        var view = new TableView();
        PlayUntilMyTurn(host, app, clock, view, v => v.Allowed.HasFlag(BjActions.Double));
        Run(host, app, clock, () => TableFrame(host, app, view), 1.5);
        host.Frame(() => TableFrame(host, app, view), 0.05f);
        host.Save(path);
    }

    private static void BlackjackSettle(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Practice(GameKind.Blackjack);
        var view = new TableView();
        long betRound = -1;
        // Play until a round where we win, then capture the payout moment.
        Run(host, app, clock, () => TableFrame(host, app, view), 600, () =>
        {
            var s = app.Hub.Table!;
            return s.Blackjack is { Phase: BjPhase.Settled } v && v.Seat(s.MySeat) is { RoundNet: > 0 };
        }, () =>
        {
            var s = app.Hub.Table!;
            if (s.Blackjack is { Phase: BjPhase.Betting } v && v.Round != betRound && s.IsSeated)
            {
                betRound = v.Round;
                s.PlaceBet(150);
            }
            if (s.Blackjack is { Phase: BjPhase.PlayerTurns } p && p.ActiveSeat == s.MySeat && !s.AwaitingHost)
                s.Act((byte)BlackjackBot.Decide(p.Seat(s.MySeat)!.Hands[p.ActiveHand].Cards, p.Dealer[0], p.Allowed), 0, p.ActiveHand);
            if (s.Blackjack is { Phase: BjPhase.Insurance } i && i.Seat(s.MySeat)?.InsuranceState == InsuranceState.Undecided)
                s.Act((byte)BjActionKind.DeclineInsurance);
        });
        Run(host, app, clock, () => TableFrame(host, app, view), 3.4);
        host.Frame(() => TableFrame(host, app, view), 0.05f);
        host.Save(path);
    }

    private static void BlackjackHostView(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.HostBlackjack();
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(host, app, view), 2);
        host.Frame(() => TableFrame(host, app, view), 0.05f);
        host.Save(path);
    }

    private static void PokerTurn(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Settings.PracticeBots = 5;
        app.Practice(GameKind.Poker);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(host, app, view), 600, () =>
        {
            var s = app.Hub.Table!;
            return s.Poker is { Phase: PokerPhase.Flop } v && v.ActiveSeat == s.MySeat;
        }, () =>
        {
            var s = app.Hub.Table!;
            if (s.Poker is { } v && v.BettingRound && v.ActiveSeat == s.MySeat && v.Phase == PokerPhase.Preflop && !s.AwaitingHost)
                s.Act((byte)(v.Allowed.HasFlag(PokerActions.Check) ? PokerActionKind.Check : PokerActionKind.Call));
        });
        Run(host, app, clock, () => TableFrame(host, app, view), 1.5);
        host.Frame(() => TableFrame(host, app, view), 0.05f);
        host.Save(path);
    }

    private static void PokerShowdown(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Settings.PracticeBots = 4;
        app.Practice(GameKind.Poker);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(host, app, view), 900, () =>
        {
            var s = app.Hub.Table!;
            return s.Poker is { Phase: PokerPhase.Showdown } v && v.Seats.Count(x => x.Shown.Count == 2) >= 2 && v.Seat(s.MySeat)?.Shown.Count == 2;
        }, () =>
        {
            var s = app.Hub.Table!;
            if (s.Poker is { } v && v.BettingRound && v.ActiveSeat == s.MySeat && !s.AwaitingHost)
                s.Act((byte)(v.Allowed.HasFlag(PokerActions.Check) ? PokerActionKind.Check : PokerActionKind.Call));
        });
        Run(host, app, clock, () => TableFrame(host, app, view), 2.6);
        host.Frame(() => TableFrame(host, app, view), 0.05f);
        host.Save(path);
    }

    /// <summary>The 512×512 plugin icon (images/icon.png): two aces and the clover chip on felt.</summary>
    private static void Icon(PreviewHost host, string path)
    {
        const float n = 512;
        host.Frame(() =>
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0));
            ImGui.SetNextWindowSize(new Vector2(n, n));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
            ImGui.Begin("Icon", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground);
            var dl = ImGui.GetWindowDrawList();
            var shape = new Vector2[64];
            var count = Gfx.RoundedRect(shape, new Vector2(8, 8), new Vector2(n - 8, n - 8), 96, 12);
            Gfx.FillRadial(dl, shape.AsSpan(0, count), new Vector2(n * 0.5f, n * 0.42f), Pal.U(Pal.Hex(0x1E7A4C)), Pal.U(Pal.Hex(0x093220)));
            dl.AddRect(new Vector2(8, 8), new Vector2(n - 8, n - 8), Pal.U(Pal.Gold), 96, ImDrawFlags.None, 8);
            CardArt.Draw(dl, new Vector2(n * 0.37f, n * 0.47f), 300, SaucerNights.Core.Cards.Card.Parse("Ah"), 1, 1f);
            CardArt.Draw(dl, new Vector2(n * 0.60f, n * 0.44f), 300, SaucerNights.Core.Cards.Card.Parse("As"), 1, 1f);
            var chip = new Vector2(n * 0.71f, n * 0.72f);
            dl.AddCircleFilled(chip + new Vector2(0, 6), 92, Pal.U(Pal.Black, 0.35f), 64);
            dl.AddCircleFilled(chip, 90, Pal.U(Pal.Hex(0x15573A)), 64);
            dl.AddCircle(chip, 90, Pal.U(Pal.Gold), 64, 10);
            dl.AddCircle(chip, 70, Pal.U(Pal.GoldLight, 0.55f), 64, 3);
            CardArt.Clover(dl, chip + new Vector2(0, -2), 104, Pal.GoldLight);
            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        });
        host.SaveCrop(path, (int)n, (int)n);
    }

    /// <summary>The player folded; the hand has ended and the "Show cards" button is offered.</summary>
    private static void PokerShowCards(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Settings.PracticeBots = 4;
        app.Practice(GameKind.Poker);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(host, app, view), 900, () => app.Hub.Table is { CanShowCards: true } t && t.Poker!.Seat(t.MySeat)!.Status == PokerSeatStatus.Folded, () =>
        {
            var s = app.Hub.Table!;
            if (s.Poker is { } v && v.BettingRound && v.ActiveSeat == s.MySeat && !s.AwaitingHost)
                s.Act((byte)PokerActionKind.Fold);
        });
        Run(host, app, clock, () => TableFrame(host, app, view), 1.2);
        host.Frame(() => TableFrame(host, app, view), 0.05f);
        host.Save(path);
        app.Hub.Table!.ShowCards();
        Run(host, app, clock, () => TableFrame(host, app, view), 1.5);
        host.Frame(() => TableFrame(host, app, view), 0.05f);
        host.Save(path.Replace(".png", "-shown.png"));
    }
}

/// <summary>An ImGui context with fonts and the software renderer.</summary>
internal sealed unsafe class PreviewHost
{
    private readonly SoftwareRenderer _renderer = new();
    private uint[]? _last;

    public PreviewHost(int width, int height)
    {
        Width = width;
        Height = height;
        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.IniFilename = null;
        io.DisplaySize = new Vector2(width, height);
        io.DeltaTime = 1f / 60f;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        var fonts = io.Fonts;
        fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 17f);
        var small = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 13f);
        var headingS = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\bahnschrift.ttf", 22f);
        var heading = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\bahnschrift.ttf", 34f);
        var display = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\bahnschrift.ttf", 64f);
        var cardS = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\georgiab.ttf", 24f);
        var card = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\georgiab.ttf", 48f);
        fonts.Build();
        UiEnv.FontProvider = (role, size) => role switch
        {
            FontRole.Body when size > 0 && size <= 14.5f => small,
            FontRole.Heading when size > 40 => display,
            FontRole.Heading when size > 0 && size <= 23 => headingS,
            FontRole.Heading => heading,
            FontRole.Display => display,
            FontRole.Card when size > 0 && size <= 25 => cardS,
            FontRole.Card => card,
            _ => (ImFontPtr?)null,
        };
        for (var i = 0; i < fonts.Textures.Size; i++)
        {
            byte* pixels;
            int w, h;
            ImGui.GetTexDataAsRGBA32(fonts, i, &pixels, &w, &h);
            var data = new uint[w * h];
            for (var p = 0; p < data.Length; p++) data[p] = ((uint*)pixels)[p];
            var id = (ulong)(i + 1);
            fonts.Textures.Ref<ImFontAtlasTexture>(i).TexID = new ImTextureID(id);
            _renderer.AddTexture(id, w, h, data);
        }
        ImGui.StyleColorsDark();
        io.MousePos = new Vector2(-1000, -1000);
    }

    public int Width { get; }

    public int Height { get; }

    public void Frame(Action draw, float dt = 1f / 60f, bool render = true)
    {
        var io = ImGui.GetIO();
        io.DeltaTime = dt;
        ImGui.NewFrame();
        draw();
        ImGui.Render();
        if (render) _last = _renderer.Render(ImGui.GetDrawData(), Width, Height, 0xFF101214);
    }

    public void Save(string path) => SoftwareRenderer.SavePng(path, _last!, Width, Height);

    /// <summary>Saves the top-left corner of the last frame.</summary>
    public void SaveCrop(string path, int width, int height)
    {
        var crop = new uint[width * height];
        for (var y = 0; y < height; y++) Array.Copy(_last!, y * Width, crop, y * width, width);
        SoftwareRenderer.SavePng(path, crop, width, height);
    }
}
