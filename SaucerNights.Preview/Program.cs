using System.Diagnostics;
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
/// Renders the plugin's windows offline.
/// Usage: SaucerNights.Preview.exe [outDir] [--scale=1.25] [scenario...]
/// Scenarios: gallery, intro, lobby, host, settings, practice, bj-bet, bj-turn, bj-settle, pk-turn, pk-showdown,
/// bj-host, rebuy-host, pk-show, icon (default: all). "soak[=N]" plays N long bot sessions (default 12), drawing
/// every frame and saving a snapshot every 20 seconds of table time, plus contact sheets.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var plain = args.Where(a => !a.StartsWith("--")).ToList();
        var outDir = plain.Count > 0 ? plain[0] : Path.Combine(AppContext.BaseDirectory, "shots");
        var only = plain.Skip(1).ToList();
        var scaleArg = args.FirstOrDefault(a => a.StartsWith("--scale="));
        var scale = scaleArg != null ? float.Parse(scaleArg["--scale=".Length..], System.Globalization.CultureInfo.InvariantCulture) : 1f;
        Directory.CreateDirectory(outDir);
        Scenarios.Scale = scale;

        var host = new PreviewHost((int)(1320 * scale), (int)(860 * scale), scale);
        var soak = only.FirstOrDefault(o => o.StartsWith("soak", StringComparison.OrdinalIgnoreCase));
        if (soak != null)
        {
            var n = soak.Contains('=') ? int.Parse(soak[(soak.IndexOf('=') + 1)..]) : 12;
            return Soak.Run(host, Path.Combine(outDir, $"soak-{scale * 100:0}"), n);
        }

        var want = only.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Want(string name) => want.Count == 0 || want.Contains(name);
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

    public string ChannelName(ChatChannel channel) => channel switch
    {
        ChatChannel.Cwls1 => "Casino Night",
        ChatChannel.Cwls2 => "Moogle Mafia",
        ChatChannel.Cwls4 => "Solution Nine Socials",
        _ => "",
    };

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
    public static float Scale = 1f;

    public static readonly (string Name, Action<PreviewHost, string> Run)[] All =
    [
        ("intro", Intro),
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
        ("rebuy-host", RebuyHost),
        ("pk-show", PokerShowCards),
        ("icon", Icon),
        ("roulette-art", RouletteArtShot),
        ("rl-bet", RouletteBet),
        ("rl-spin", RouletteSpin),
        ("rl-settle", RouletteSettle),
        ("rl-host", RouletteHost),
    ];

    private static readonly (byte Index, long Amount)[] DemoBets = [(17, 25), (124, 10), (151, 100), (146, 50), (138, 25)];

    private static (GameApp App, ManualClock Clock, TableView View) RoulettePractice(PreviewHost host, int bots)
    {
        var (app, clock) = NewApp();
        app.Settings.PracticeBots = bots;
        app.Settings.Felt = FeltStyle.Neon;
        app.Practice(GameKind.Roulette);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(app, view), 60, () => app.Hub.Table?.Roulette is { Phase: Core.Roulette.RoulettePhase.Betting } v && v.Seats.Count(x => x.Bets.Count > 0) >= 2);
        return (app, clock, view);
    }

    /// <summary>Betting is open: a few bots have chips down and so do we.</summary>
    private static void RouletteBet(PreviewHost host, string path)
    {
        var (app, clock, view) = RoulettePractice(host, 4);
        app.Hub.Table!.PlaceRouletteBets(DemoBets);
        Run(host, app, clock, () => TableFrame(app, view), 2.5);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    /// <summary>No more bets: the ball is running round the wheel.</summary>
    private static void RouletteSpin(PreviewHost host, string path)
    {
        var (app, clock, view) = RoulettePractice(host, 4);
        app.Hub.Table!.PlaceRouletteBets(DemoBets);
        Run(host, app, clock, () => TableFrame(app, view), 90, () => app.Hub.Table?.Roulette is { Phase: Core.Roulette.RoulettePhase.Spinning });
        Run(host, app, clock, () => TableFrame(app, view), 2.6);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    /// <summary>A spin we won: the result lit up and the payout on its way.</summary>
    private static void RouletteSettle(PreviewHost host, string path)
    {
        var (app, clock, view) = RoulettePractice(host, 3);
        long betRound = -1;
        Run(host, app, clock, () => TableFrame(app, view), 900, () =>
        {
            var s = app.Hub.Table!;
            return s.Roulette is { Phase: Core.Roulette.RoulettePhase.Settled } v && v.Seat(s.MySeat) is { RoundNet: > 0 };
        }, () =>
        {
            var s = app.Hub.Table!;
            if (s.Roulette is { Phase: Core.Roulette.RoulettePhase.Betting } v && v.Round != betRound && !s.AwaitingHost)
            {
                betRound = v.Round;
                s.PlaceRouletteBets([(145, 50), (146, 50), (17, 10)]);
            }
        });
        Run(host, app, clock, () => TableFrame(app, view), 2.2);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    /// <summary>Hosting roulette: three players have sat down.</summary>
    private static void RouletteHost(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.HostRoulette();
        var hs = app.Hub.Host!;
        uint seq = 1;
        using var keys = new Core.Crypto.SessionKeys();
        string[] players = ["Lyra Moonfall@Ultros", "Bram Ironside@Gilgamesh", "Cyra Tidewell@Balmung"];
        for (var i = 0; i < players.Length; i++)
            hs.OnFrame(players[i], new JoinMsg { Nonce = (uint)(i + 7), PublicKey = keys.PublicKey, PreferredSeat = (byte)i }.ToFrame(hs.TableId, seq++), clock.Now);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(app, view), 3);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    public static (GameApp App, ManualClock Clock) NewApp(bool introDone = true)
    {
        var clock = new ManualClock(new DateTime(2026, 11, 21, 20, 0, 0, DateTimeKind.Utc));
        var app = new GameApp(new FakeBridge(), new Settings { IntroDone = introDone, UiScale = Scale }, () => { }, new FakeLink(), clock);
        return (app, clock);
    }

    private static void Window(string title, Vector2 pos, Vector2 size, Action body, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        Theme.Push();
        ImGui.SetNextWindowPos(pos * Scale);
        ImGui.SetNextWindowSize(size * Scale);
        ImGui.Begin(title, flags | ImGuiWindowFlags.NoCollapse);
        Theme.AfterBegin();
        body();
        ImGui.End();
        Theme.Pop();
    }

    public static void LobbyFrame(GameApp app, LobbyView view) =>
        Window("Solution Nights##lobby", new Vector2(20, 20), new Vector2(980, 720), () => view.Draw(app, () => { }), ImGuiWindowFlags.NoScrollbar);

    public static void TableFrame(GameApp app, TableView view) =>
        Window("Solution Nights - Table##table", new Vector2(10, 10), new Vector2(1300, 840), () => view.Draw(app, () => { }), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

    /// <summary>Runs the app for a while (clock and UI time together), drawing frames so animations progress.</summary>
    public static void Run(PreviewHost host, GameApp app, ManualClock clock, Action frame, double seconds, Func<bool>? until = null, Action? each = null)
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

    private static void LobbyShot(PreviewHost host, string path, GameApp app, LobbyView view)
    {
        for (var i = 0; i < 10; i++) host.Frame(() => LobbyFrame(app, view), 0.05f, render: i == 9);
        host.Save(path);
    }

    private static void Intro(PreviewHost host, string path)
    {
        var (app, _) = NewApp(introDone: false);
        app.Settings.Channel = ChatChannel.Cwls1;
        LobbyShot(host, path, app, new LobbyView());
    }

    private static void Lobby(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        Announce(app, clock, "Moira Starling@Gilgamesh", "Neon Stakes Blackjack", GameKind.Blackjack, 4, 6, TableFlags.Open | TableFlags.ProvablyFair | TableFlags.InProgress,
            new BlackjackRules { MinBet = 50, MaxBet = 5000 }.ToBytes());
        Announce(app, clock, "Tobias Wren@Ultros", "High Rollers Hold'em", GameKind.Poker, 7, 8, TableFlags.Open | TableFlags.InProgress,
            new PokerRules { SmallBlind = 500, BigBlind = 1000 }.ToBytes());
        Announce(app, clock, "Lyse Hext@Balmung", "Friday Night Cards", GameKind.Poker, 2, 6, TableFlags.Open | TableFlags.ProvablyFair,
            new PokerRules { SmallBlind = 10, BigBlind = 20 }.ToBytes());
        LobbyShot(host, path, app, new LobbyView());
    }

    private static void LobbyEmpty(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        LobbyShot(host, path, app, new LobbyView());
    }

    private static void HostPage(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        LobbyShot(host, path, app, new LobbyView { Page = 1 });
    }

    private static void HostPoker(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        var view = new LobbyView { Page = 1 };
        typeof(LobbyView).GetField("_hostGame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(view, 1);
        LobbyShot(host, path, app, view);
    }

    private static void SettingsPage(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        LobbyShot(host, path, app, new LobbyView { Page = 3 });
    }

    private static void PracticePage(PreviewHost host, string path)
    {
        var (app, _) = NewApp();
        LobbyShot(host, path, app, new LobbyView { Page = 2 });
    }

    private static void BlackjackBet(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Settings.PracticeBots = 4;
        app.Practice(GameKind.Blackjack);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(app, view), 30,
            () => app.Hub.Table?.Blackjack is { Phase: BjPhase.Betting } v && v.Seats.Count(x => x.Bet > 0) >= 2);
        Run(host, app, clock, () => TableFrame(app, view), 0.8);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    private static void PlayUntilMyTurn(PreviewHost host, GameApp app, ManualClock clock, TableView view, Func<BlackjackView, bool> extra)
    {
        long betRound = -1;
        Run(host, app, clock, () => TableFrame(app, view), 300, () =>
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
        Run(host, app, clock, () => TableFrame(app, view), 1.5);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    private static void BlackjackSettle(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Practice(GameKind.Blackjack);
        var view = new TableView();
        long betRound = -1;
        Run(host, app, clock, () => TableFrame(app, view), 600, () =>
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
        Run(host, app, clock, () => TableFrame(app, view), 3.4);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    private static void BlackjackHostView(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.HostBlackjack();
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(app, view), 2);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    /// <summary>Hosting blackjack: three players sit down over "chat", one runs dry and asks to buy back in.</summary>
    private static void RebuyHost(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.HostBlackjack();
        var hs = app.Hub.Host!;
        uint seq = 1;
        void Send(string who, IMessage msg) => hs.OnFrame(who, msg.ToFrame(hs.TableId, seq++), clock.Now);
        string[] players = ["Lyra Moonfall@Ultros", "Bram Ironside@Gilgamesh", "Cyra Tidewell@Balmung"];
        using var keys = new Core.Crypto.SessionKeys();
        for (var i = 0; i < players.Length; i++)
            Send(players[i], new JoinMsg { Nonce = (uint)(i + 7), PublicKey = keys.PublicKey, PreferredSeat = (byte)(i * 2) });
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(app, view), 2);
        hs.AdjustChips(hs.Player(players[0])!.Seat, -hs.Game.StackOf(hs.Player(players[0])!.Seat));
        Send(players[0], new RebuyMsg());
        Run(host, app, clock, () => TableFrame(app, view), 1.5);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    private static void PokerTurn(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Settings.PracticeBots = 5;
        app.Practice(GameKind.Poker);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(app, view), 600, () =>
        {
            var s = app.Hub.Table!;
            return s.Poker is { Phase: PokerPhase.Flop } v && v.ActiveSeat == s.MySeat;
        }, () =>
        {
            var s = app.Hub.Table!;
            if (s.Poker is { } v && v.BettingRound && v.ActiveSeat == s.MySeat && v.Phase == PokerPhase.Preflop && !s.AwaitingHost)
                s.Act((byte)(v.Allowed.HasFlag(PokerActions.Check) ? PokerActionKind.Check : PokerActionKind.Call));
        });
        Run(host, app, clock, () => TableFrame(app, view), 1.5);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    private static void PokerShowdown(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Settings.PracticeBots = 4;
        app.Practice(GameKind.Poker);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(app, view), 900, () =>
        {
            var s = app.Hub.Table!;
            return s.Poker is { Phase: PokerPhase.Showdown } v && v.Seats.Count(x => x.Shown.Count == 2) >= 2 && v.Seat(s.MySeat)?.Shown.Count == 2;
        }, () =>
        {
            var s = app.Hub.Table!;
            if (s.Poker is { } v && v.BettingRound && v.ActiveSeat == s.MySeat && !s.AwaitingHost)
                s.Act((byte)(v.Allowed.HasFlag(PokerActions.Check) ? PokerActionKind.Check : PokerActionKind.Call));
        });
        Run(host, app, clock, () => TableFrame(app, view), 2.6);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
    }

    /// <summary>The 512x512 plugin icon (images/icon.png): two aces over the city, with the neon mark.</summary>
    private static void Icon(PreviewHost host, string path)
    {
        const float n = 512;
        var old = UiEnv.Scale;
        UiEnv.Scale = 1;
        host.Frame(() =>
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0));
            ImGui.SetNextWindowSize(new Vector2(n, n));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
            ImGui.Begin("Icon", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground);
            var dl = ImGui.GetWindowDrawList();
            var min = new Vector2(8, 8);
            var max = new Vector2(n - 8, n - 8);
            var shape = new Vector2[64];
            var count = Gfx.RoundedRect(shape, min, max, 96, 12);
            Gfx.FillVertical(dl, shape.AsSpan(0, count), Pal.Hex(0x14205A), Pal.Hex(0x05060F));
            dl.PushClipRect(min + new Vector2(20), max - new Vector2(20), true);
            var glow = Gfx.Ellipse(shape, new Vector2(n / 2, n * 0.62f), new Vector2(n * 0.5f, n * 0.3f), 48);
            Gfx.FillRadial(dl, shape.AsSpan(0, glow), new Vector2(n / 2, n * 0.62f), Pal.U(Pal.Violet, 0.35f), Pal.U(Pal.Violet, 0f));
            Gfx.Skyline(dl, 0, n, n * 0.8f, 150, 11, 0.9f);
            dl.PopClipRect();
            count = Gfx.RoundedRect(shape, min, max, 96, 12);
            Gfx.NeonOutline(dl, shape.AsSpan(0, count), Pal.Accent, 6, 1.2f);
            CardArt.Draw(dl, new Vector2(n * 0.38f, n * 0.44f), 270, SaucerNights.Core.Cards.Card.Parse("Ah"), 1, 1f);
            CardArt.Draw(dl, new Vector2(n * 0.6f, n * 0.41f), 270, SaucerNights.Core.Cards.Card.Parse("As"), 1, 1f);
            var c = new Vector2(n * 0.7f, n * 0.71f);
            var hex = new Vector2[6];
            Gfx.Hexagon(hex, c + new Vector2(0, 8), 92);
            Gfx.FillConvex(dl, hex, Pal.U(Pal.Black, 0.45f));
            Logo.Mark(dl, c, 180, Pal.Accent, Pal.Hex(0x0B1530));
            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
        });
        UiEnv.Scale = old;
        host.SaveCrop(path, (int)n, (int)n);
    }

    /// <summary>The wheel (ball dropping) and the board with a few bets, for tuning the roulette art.</summary>
    private static void RouletteArtShot(PreviewHost host, string path)
    {
        var board = new RouletteBoard();
        host.Frame(() =>
        {
            Theme.Push();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(host.Width, host.Height));
            ImGui.Begin("RouletteArt", ImGuiWindowFlags.NoDecoration);
            Theme.AfterBegin();
            var dl = ImGui.GetWindowDrawList();
            var min = Vector2.Zero;
            var max = new Vector2(host.Width, host.Height);
            TableArt.Backdrop(dl, min, max, FeltStyle.Neon, 3);
            var r = host.Height * 0.3f;
            RouletteArt.Wheel(dl, new Vector2(host.Width * 0.22f, host.Height * 0.42f), r, 0.7f, Pal.Accent, 2.1f, 0.74f, 17, 1);
            board.Layout(new Vector2(host.Width * 0.42f, host.Height * 0.12f), new Vector2(host.Width - 30 * Scale, host.Height * 0.8f));
            board.Draw(dl, Pal.Accent, Pal.Hex(0xD3F6FF), Pal.Hex(0x0F5373), 17, RouletteArt.Mask(17, 20), 0.5f);
            foreach (var (m, amount) in new (ulong, long)[] { (RouletteArt.Bit(17), 25), (RouletteArt.Mask(13, 14, 16, 17), 10), (RouletteArt.Mask(RouletteBoard.EvenMoney[2].Numbers), 100), (RouletteArt.Mask(0, 1, 2, 3), 5), (RouletteArt.Mask(RouletteBoard.Dozens[1].Numbers), 50), (RouletteArt.Mask(22, 23, 24, 25, 26, 27), 25) })
                ChipArt.Top(dl, board.SpotOf(m), board.Cell * 0.27f, ChipArt.For(amount));
            for (var i = 0; i < 10; i++) RouletteArt.NumberBadge(dl, new Vector2(host.Width * 0.45f + i * 40 * Scale, host.Height * 0.9f), 16 * Scale, RouletteArt.WheelOrder[i * 3], 1f, i == 9);
            ImGui.End();
            Theme.Pop();
        });
        host.Save(path);
    }

    /// <summary>The player folded; the hand has ended and the "Show cards" button is offered.</summary>
    private static void PokerShowCards(PreviewHost host, string path)
    {
        var (app, clock) = NewApp();
        app.Settings.PracticeBots = 4;
        app.Practice(GameKind.Poker);
        var view = new TableView();
        Run(host, app, clock, () => TableFrame(app, view), 900, () => app.Hub.Table is { CanShowCards: true } t && t.Poker!.Seat(t.MySeat)!.Status == PokerSeatStatus.Folded, () =>
        {
            var s = app.Hub.Table!;
            if (s.Poker is { } v && v.BettingRound && v.ActiveSeat == s.MySeat && !s.AwaitingHost)
                s.Act((byte)PokerActionKind.Fold);
        });
        Run(host, app, clock, () => TableFrame(app, view), 1.2);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path);
        app.Hub.Table!.ShowCards();
        Run(host, app, clock, () => TableFrame(app, view), 1.5);
        host.Frame(() => TableFrame(app, view), 0.05f);
        host.Save(path.Replace(".png", "-shown.png"));
    }
}

/// <summary>
/// Long practice sessions with a bot in the player's own seat, drawing every frame of the real table UI. Checks that
/// nothing throws, chips are conserved and play keeps moving, and saves snapshots plus 3x3 contact sheets.
/// </summary>
internal static class Soak
{
    public static int Run(PreviewHost host, string dir, int sessions)
    {
        Directory.CreateDirectory(dir);
        var shots = new List<uint[]>();
        var failures = 0;
        var totalRounds = 0L;
        var sw = Stopwatch.StartNew();
        FeltStyle[] felts = Looks.Felts;
        CardBackStyle[] backs = Looks.Backs;
        for (var i = 0; i < sessions; i++)
        {
            var kind = (i % 3) switch { 0 => GameKind.Poker, 1 => GameKind.Blackjack, _ => GameKind.Roulette };
            var rng = new Random(1000 + i);
            var (app, clock) = Scenarios.NewApp();
            app.Settings.PracticeBots = (kind == GameKind.Poker ? new[] { 5, 1, 3, 6, 2, 4 } : kind == GameKind.Blackjack ? new[] { 4, 6, 2, 5, 1, 3 } : new[] { 3, 5, 1, 6, 2, 4 })[i / 3 % 6];
            app.Settings.Felt = felts[i / 2 % felts.Length];
            app.Settings.CardBack = backs[i % backs.Length];
            app.Settings.ShowSidePanel = rng.Next(3) != 0;
            app.Settings.FourColorDeck = rng.Next(4) == 0;
            app.Practice(kind);
            var practice = app.Hub.Practice!;
            IBot me = kind switch
            {
                GameKind.Blackjack => new BlackjackBot(77 + i, 0.4, 1.2),
                GameKind.Roulette => new RouletteBot(77 + i, 0.5, 3),
                _ => new PokerBot(77 + i, 0.4, 1.2),
            };
            var view = new TableView();
            var minutes = 6 + rng.Next(5);
            var violations = 0;
            var nextShot = 15.0;
            var t = 0.0;
            var lastRound = -1L;
            var lastChange = 0.0;
            var longestWait = 0.0;
            try
            {
                for (; t < minutes * 60; t += 0.1)
                {
                    clock.Advance(TimeSpan.FromSeconds(0.1));
                    app.Update();
                    me.Tick(app.Hub.Table!, clock.Now);
                    var render = t >= nextShot;
                    host.Frame(() => Scenarios.TableFrame(app, view), 0.1f, render);
                    if (render)
                    {
                        nextShot += 20;
                        var file = Path.Combine(dir, $"s{i:00}-{kind.ToString().ToLowerInvariant()}-{(int)t:0000}.png");
                        host.Save(file);
                        shots.Add(Third(host.Last, host.Width, host.Height));
                    }
                    if (!Conserved(practice.Host)) violations++;
                    if (practice.Host.Game.Round != lastRound)
                    {
                        lastRound = practice.Host.Game.Round;
                        lastChange = t;
                    }
                    longestWait = Math.Max(longestWait, t - lastChange);
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"session {i} ({kind}) threw at {t:0.0}s: {ex}");
            }
            var rounds = practice.Host.Game.Round;
            totalRounds += rounds;
            // A stall is two and a half minutes without a new round or hand.
            var ok = violations == 0 && longestWait < 150;
            if (!ok) failures++;
            Console.WriteLine($"session {i:00}: {kind,-9} bots={app.Settings.PracticeBots} {minutes}m rounds={rounds} longest gap={longestWait:0}s violations={violations} {(ok ? "ok" : "PROBLEM")}");
            app.Leave();
            app.Dispose();
        }
        ContactSheets(dir, shots, host.Width, host.Height);
        Console.WriteLine($"soak: {sessions} sessions, {totalRounds} rounds/hands, {shots.Count} snapshots, {failures} problems, {sw.Elapsed.TotalSeconds:0}s");
        return failures == 0 ? 0 : 1;
    }

    private static bool Conserved(HostSession host)
    {
        var net = host.Ledger().Sum(e => e.Net);
        return host.Game switch
        {
            BlackjackEngine bj => bj.Phase is not (BjPhase.Settled or BjPhase.Betting or BjPhase.Waiting) || net + bj.HouseNet == 0,
            PokerEngine pk => pk.Phase is not (PokerPhase.Waiting or PokerPhase.Showdown) || net == 0,
            Core.Roulette.RouletteEngine rl => net + rl.HouseNet + rl.ChipsOnTable == 0,
            _ => true,
        };
    }

    /// <summary>A frame at a third of its size (3x3 box filter).</summary>
    private static uint[] Third(uint[] src, int w, int h)
    {
        var tw = w / 3;
        var th = h / 3;
        var dst = new uint[tw * th];
        for (var y = 0; y < th; y++)
        for (var x = 0; x < tw; x++)
        {
            uint r = 0, g = 0, b = 0;
            for (var dy = 0; dy < 3; dy++)
            for (var dx = 0; dx < 3; dx++)
            {
                var c = src[(y * 3 + dy) * w + x * 3 + dx];
                r += c & 0xFF;
                g += (c >> 8) & 0xFF;
                b += (c >> 16) & 0xFF;
            }
            dst[y * tw + x] = 0xFF000000 | (b / 9) << 16 | (g / 9) << 8 | r / 9;
        }
        return dst;
    }

    /// <summary>3x3 grids of snapshots at a third of their size, for reviewing many frames at once.</summary>
    private static void ContactSheets(string dir, List<uint[]> shots, int w, int h)
    {
        var tw = w / 3;
        var th = h / 3;
        for (var sheet = 0; sheet * 9 < shots.Count; sheet++)
        {
            var px = new uint[w * h];
            Array.Fill(px, 0xFF101214);
            for (var k = 0; k < 9 && sheet * 9 + k < shots.Count; k++)
            {
                var thumb = shots[sheet * 9 + k];
                var ox = k % 3 * tw;
                var oy = k / 3 * th;
                for (var y = 0; y < th; y++) Array.Copy(thumb, y * tw, px, (oy + y) * w + ox, tw);
            }
            SoftwareRenderer.SavePng(Path.Combine(dir, $"sheet-{sheet:00}.png"), px, w, h);
        }
    }
}

/// <summary>An ImGui context with fonts and the software renderer.</summary>
internal sealed unsafe class PreviewHost
{
    private readonly SoftwareRenderer _renderer = new();
    private uint[]? _last;

    public PreviewHost(int width, int height, float scale = 1f)
    {
        Width = width;
        Height = height;
        UiEnv.Scale = scale;
        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.IniFilename = null;
        io.DisplaySize = new Vector2(width, height);
        io.DeltaTime = 1f / 60f;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        var fonts = io.Fonts;
        fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 17f * scale);
        var small = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 13f * scale);
        var headingS = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\bahnschrift.ttf", 22f * scale);
        var heading = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\bahnschrift.ttf", 34f * scale);
        var display = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\bahnschrift.ttf", 64f * scale);
        var cardS = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\georgiab.ttf", 24f * scale);
        var card = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\georgiab.ttf", 48f * scale);
        fonts.Build();
        UiEnv.FontProvider = (role, size) =>
        {
            var s = UiEnv.Scale;
            return role switch
            {
                FontRole.Body when size > 0 && size <= 14.5f * s => small,
                FontRole.Heading when size > 40 * s => display,
                FontRole.Heading when size > 0 && size <= 23 * s => headingS,
                FontRole.Heading => heading,
                FontRole.Display => display,
                FontRole.Card when size > 0 && size <= 25 * s => cardS,
                FontRole.Card => card,
                _ => (ImFontPtr?)null,
            };
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

    public uint[] Last => _last!;

    public void Save(string path) => SoftwareRenderer.SavePng(path, _last!, Width, Height);

    /// <summary>Saves the top-left corner of the last frame.</summary>
    public void SaveCrop(string path, int width, int height)
    {
        var crop = new uint[width * height];
        for (var y = 0; y < height; y++) Array.Copy(_last!, y * Width, crop, y * width, width);
        SoftwareRenderer.SavePng(path, crop, width, height);
    }
}
