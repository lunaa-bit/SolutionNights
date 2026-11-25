using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Ui;

/// <summary>The table window's content: header, animated scene, action bar and side panel.</summary>
public sealed class TableView
{
    private readonly BlackjackScene _blackjack = new();
    private readonly PokerScene _poker = new();
    private readonly RouletteScene _roulette = new();
    private readonly ActionBar _bar = new();
    private readonly SidePanel _side = new();
    private readonly TableLog _log = new();
    private ClientSession? _current;
    private bool _confirmLeave;

    private static float S(float v) => UiEnv.S(v);

    public static float HeaderHeight => S(56);

    /// <summary>Space between the window edge and the floating panels.</summary>
    public static float Margin => S(10);

    public void Draw(GameApp app, Action openLobby)
    {
        CardArt.Back = app.Settings.CardBack;
        CardArt.FourColor = app.Settings.FourColorDeck;
        var s = app.Hub.Table;
        if (s != _current)
        {
            _current = s;
            _blackjack.Reset();
            _poker.Reset();
            _roulette.Reset();
            _bar.Reset();
            _log.Attach(s);
            _confirmLeave = false;
        }

        var min = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        var max = min + size;
        var dl = ImGui.GetWindowDrawList();
        var now = UiEnv.Now;

        if (s == null)
        {
            TableArt.Backdrop(dl, min, max, app.Settings.Felt, now);
            var c = (min + max) / 2;
            Logo.Mark(dl, c - new Vector2(0, S(66)), S(84), Pal.Accent, Pal.Hex(0x0B1530));
            Gfx.TextGlow(dl, FontRole.Heading, S(30), c + new Vector2(0, S(10)), Pal.Text,
                app.Hub.Closing ? "CLOSING THE TABLE..." : "YOU'RE NOT AT A TABLE", 0.5f);
            Gfx.TextCentered(dl, FontRole.Body, S(15), c + new Vector2(0, S(42)), Pal.U(Pal.Muted), "Find, host or practise a game from the lobby.");
            ImGui.SetCursorScreenPos(c + new Vector2(-S(90), S(70)));
            if (W.Button("Open lobby##lobby", new Vector2(S(180), S(42)), Tone.Primary, icon: Icon.Spade)) openLobby();
            return;
        }

        // The city fills the window; the header, side panel and action dock float over it as glass.
        TableArt.Backdrop(dl, min, max, app.Settings.Felt, now);
        var m = Margin;
        var side = app.Settings.ShowSidePanel ? SidePanel.Width : 0;
        var headerMin = min + new Vector2(m);
        var headerMax = new Vector2(max.X - m, min.Y + m + HeaderHeight);
        var sideMin = new Vector2(max.X - m - side, headerMax.Y + m);
        var dockMax = new Vector2(side > 0 ? sideMin.X - m : max.X - m, max.Y - m);
        var dockMin = new Vector2(min.X + m, dockMax.Y - ActionBar.Height);
        var sceneMin = new Vector2(min.X, headerMax.Y);
        var sceneMax = new Vector2(side > 0 ? sideMin.X - m * 0.5f : max.X, dockMin.Y);

        // Scene.
        dl.PushClipRect(min, max, true);
        if (s.Table.Game == GameKind.Blackjack) _blackjack.Draw(dl, sceneMin, sceneMax, s, app, now);
        else if (s.Table.Game == GameKind.Roulette) _roulette.Draw(dl, sceneMin, sceneMax, s, app, now);
        else _poker.Draw(dl, sceneMin, sceneMax, s, app, now);
        dl.PopClipRect();

        Header(dl, app, s, headerMin, headerMax, openLobby);

        var focused = app.Settings.KeyboardShortcuts && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.GetIO().WantTextInput;
        _bar.RouletteDraft = _roulette.Draft;
        _bar.Draw(dockMin, dockMax, s, app, focused);

        if (side > 0) _side.Draw(sideMin, new Vector2(max.X - m, max.Y - m), s, app, _log);

        Toasts(dl, app, sceneMin, sceneMax);
        if (app.Hub.Host is { RebuyRequests.Count: > 0 } host) RebuyRequests(dl, host, sceneMin, sceneMax);

        ImGui.SetCursorScreenPos(max);
        ImGui.Dummy(Vector2.Zero);
    }

    private void Header(ImDrawListPtr dl, GameApp app, ClientSession s, Vector2 min, Vector2 max, Action openLobby)
    {
        W.Glass(dl, min, max, S(14));

        var p = min + new Vector2(S(20), 0);
        var cy = (min.Y + max.Y) / 2;
        Span<Vector2> hex = stackalloc Vector2[6];
        Gfx.Hexagon(hex, new Vector2(p.X + S(14), cy), S(16));
        Gfx.FillConvex(dl, hex, Pal.U(Pal.Hex(0x0B1530)));
        Gfx.NeonOutline(dl, hex, Pal.Accent, S(1.4f), 0.6f);
        var ic = new Vector2(p.X + S(14), cy);
        if (s.Table.Game == GameKind.Blackjack)
            Gfx.TextCentered(dl, FontRole.Heading, S(16), ic + new Vector2(0, S(1)), Pal.U(Pal.AccentLight), "21");
        else if (s.Table.Game == GameKind.Roulette)
        {
            dl.AddCircle(ic, S(8), Pal.U(Pal.AccentLight), 24, S(1.5f));
            for (var k = 0; k < 4; k++) dl.AddLine(ic, ic + new Vector2(MathF.Cos(k * MathF.PI / 2 + 0.4f), MathF.Sin(k * MathF.PI / 2 + 0.4f)) * S(8), Pal.U(Pal.AccentLight), S(1.2f));
            dl.AddCircleFilled(ic, S(2.5f), Pal.U(Pal.AccentLight), 12);
        }
        else
            Icons.Draw(dl, Icon.Spade, ic, S(16), Pal.AccentLight);
        var title = s.Table.Title;
        dl.AddText(UiEnv.Font(FontRole.Heading, S(22)), S(22), new Vector2(p.X + S(38), cy - S(16)), Pal.U(Pal.Text), title);
        var game = s.Table.Game switch
        {
            GameKind.Blackjack => $"Blackjack  ·  Bets {s.Table.StakesText}",
            GameKind.Roulette => $"European Roulette  ·  Bets {s.Table.StakesText}",
            _ => $"No-Limit Hold'em  ·  Blinds {s.Table.StakesText}",
        };
        var host = app.Hub.IsPractice ? "Practice table" : s.Table.HostId == s.SelfId ? "You're hosting" : $"Dealer: {SeatArt.ShortName(s.Table.HostId)}";
        dl.AddText(UiEnv.Font(FontRole.Body, S(13)), S(13), new Vector2(p.X + S(38), cy + S(4)), Pal.U(Pal.Muted), $"{game}  ·  {host}");

        // Status pills.
        var tw = Math.Max(Gfx.TextSize(FontRole.Heading, S(22), title).X, Gfx.TextSize(FontRole.Body, S(13), $"{game}  ·  {host}").X);
        ImGui.SetCursorScreenPos(new Vector2(p.X + S(54) + tw, cy - S(11)));
        if (app.Hub.IsPractice) W.Pill("Offline", Pal.Purple);
        else
        {
            var quiet = (app.Clock.Now - s.LastHostFrame).TotalSeconds;
            if (s.Status == ClientStatus.Closed) W.Pill("Closed", Pal.Red);
            else if (s.Table.HostId != s.SelfId && quiet > 75) W.Pill($"Host quiet {quiet:0}s", Pal.Orange);
            else W.Pill(app.ChannelLabel, Pal.Green);
        }
        if (s.Table.Game == GameKind.Poker && s.Table.Flags.HasFlag(TableFlags.HostPlays) && s.Table.HostId != s.SelfId)
        {
            ImGui.SameLine(0, S(6));
            W.Pill("Dealer plays", Pal.Orange);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The host also has a seat. Their game deals the cards, so it knows every hand.");
        }
        if (s.Table.Flags.HasFlag(TableFlags.ProvablyFair))
        {
            ImGui.SameLine(0, S(6));
            var fair = s.Fairness.Count == 0 || s.Fairness[^1].Status != Core.Fairness.FairStatus.Failed;
            W.Pill(fair ? "Provably fair" : "Fairness check failed", fair ? Pal.Accent : Pal.Red);
        }
        if (s.Table.Flags.HasFlag(TableFlags.Paused))
        {
            ImGui.SameLine(0, S(6));
            W.Pill("Paused", Pal.Orange);
        }
        var pillsEnd = ImGui.GetItemRectMax().X;

        // Where the table is: round or hand number and phase, centred when there's room.
        if (PhaseText(s) is { } phase)
        {
            var fs = S(13);
            var spacing = S(1.6f);
            var ts = Gfx.TextSpacedSize(FontRole.Body, fs, phase.Text, spacing);
            var cx = (min.X + max.X) / 2;
            var right = max.X - S(160);
            if (cx - ts.X / 2 < pillsEnd + S(24)) cx = pillsEnd + S(24) + ts.X / 2;
            if (cx + ts.X / 2 < right)
            {
                var pos = new Vector2(cx - ts.X / 2, cy - ts.Y / 2);
                W.Diamond(dl, pos - new Vector2(S(10), -ts.Y / 2), S(3.5f), phase.Color);
                Gfx.TextSpaced(dl, FontRole.Body, fs, pos, Pal.U(phase.Color), phase.Text, spacing);
            }
        }

        // Right-hand buttons.
        var bs = S(34);
        var x = max.X - S(12) - bs;
        ImGui.SetCursorScreenPos(new Vector2(x, cy - bs / 2));
        var leaveTip = app.Hub.IsHosting ? "Close the table" : app.Hub.IsPractice ? "End practice" : "Leave the table";
        if (_confirmLeave)
        {
            var w = S(250);
            ImGui.SetCursorScreenPos(new Vector2(max.X - S(12) - w, cy - S(16)));
            if (W.Button(app.Hub.IsHosting ? "Close table for everyone##leaveyes" : "Leave table##leaveyes", new Vector2(w * 0.62f, S(32)), Tone.Danger))
            {
                _confirmLeave = false;
                app.Leave();
            }
            ImGui.SameLine(0, S(6));
            if (W.Button("Stay##leaveno", new Vector2(w * 0.38f - S(6), S(32)))) _confirmLeave = false;
            return;
        }
        if (W.IconButton("leave", Icon.Exit, bs, leaveTip))
        {
            var inHand = s.IsSeated && (s.Blackjack?.Seat(s.MySeat)?.Hands.Count > 0 || s.Poker?.Seat(s.MySeat)?.InHand == true || s.Roulette?.Seat(s.MySeat)?.Bets.Count > 0);
            if (app.Hub.IsHosting || inHand) _confirmLeave = true;
            else app.Leave();
        }
        ImGui.SetCursorScreenPos(new Vector2(x - bs - S(6), cy - bs / 2));
        if (W.IconButton("panel", Icon.Panel, bs, app.Settings.ShowSidePanel ? "Hide side panel" : "Show side panel", Tone.Ghost, true, app.Settings.ShowSidePanel))
        {
            app.Settings.ShowSidePanel = !app.Settings.ShowSidePanel;
            app.Save();
        }
        ImGui.SetCursorScreenPos(new Vector2(x - (bs + S(6)) * 2, cy - bs / 2));
        if (W.IconButton("lobby", Icon.List, bs, "Lobby")) openLobby();
    }

    private static (string Text, Vector4 Color)? PhaseText(ClientSession s)
    {
        if (s.Blackjack is { } b)
        {
            var phase = b.Phase switch
            {
                Core.Blackjack.BjPhase.Betting => "BETTING",
                Core.Blackjack.BjPhase.Insurance => "INSURANCE",
                Core.Blackjack.BjPhase.PlayerTurns => "PLAYERS",
                Core.Blackjack.BjPhase.DealerTurn => "DEALER",
                Core.Blackjack.BjPhase.Settled => "PAYOUTS",
                _ => "WAITING",
            };
            return (b.Round > 0 ? $"ROUND {b.Round}  ·  {phase}" : phase, b.Phase == Core.Blackjack.BjPhase.Betting ? Pal.Gold : Pal.AccentLight);
        }
        if (s.Roulette is { } r)
        {
            var phase = r.Phase switch
            {
                Core.Roulette.RoulettePhase.Betting => "BETTING",
                Core.Roulette.RoulettePhase.Spinning => "NO MORE BETS",
                Core.Roulette.RoulettePhase.Settled => r.HasResult ? $"{r.Result} {(r.Result == 0 ? "GREEN" : RouletteArt.IsRed(r.Result) ? "RED" : "BLACK")}" : "RESULT",
                _ => "WAITING",
            };
            return (r.Round > 0 ? $"SPIN {r.Round}  ·  {phase}" : phase, r.Phase switch
            {
                Core.Roulette.RoulettePhase.Betting => Pal.Gold,
                Core.Roulette.RoulettePhase.Spinning => Pal.Magenta,
                _ => Pal.AccentLight,
            });
        }
        if (s.Poker is { } p)
        {
            var phase = p.Phase switch
            {
                Core.Poker.PokerPhase.Starting => "SHUFFLING",
                Core.Poker.PokerPhase.Preflop => "PRE-FLOP",
                Core.Poker.PokerPhase.Flop => "FLOP",
                Core.Poker.PokerPhase.Turn => "TURN",
                Core.Poker.PokerPhase.River => "RIVER",
                Core.Poker.PokerPhase.Showdown => "SHOWDOWN",
                _ => "WAITING",
            };
            return (p.Hand > 0 ? $"HAND {p.Hand}  ·  {phase}" : phase, p.Phase == Core.Poker.PokerPhase.Showdown ? Pal.Gold : Pal.AccentLight);
        }
        return null;
    }

    /// <summary>The host's rebuy requests, pinned to the top-right of the table until answered.</summary>
    private static void RebuyRequests(ImDrawListPtr dl, HostSession host, Vector2 min, Vector2 max)
    {
        var w = Math.Min(S(300), (max.X - min.X) * 0.4f);
        var h = S(84);
        var x = max.X - w - S(14);
        var y = min.Y + S(14);
        foreach (var r in host.RebuyRequests.Take(3).ToList())
        {
            var pMin = new Vector2(x, y);
            var pMax = new Vector2(x + w, y + h);
            var pulse = 0.6f + 0.4f * MathF.Sin((float)UiEnv.Now * 3);
            Gfx.ChamferGlow(dl, pMin, pMax, S(12), Pal.Magenta, S(10), 0.6f * pulse);
            Gfx.ChamferPanel(dl, pMin, pMax, S(12), Pal.Hex(0x1C1440, 0.97f), Pal.Hex(0x0C0A24, 0.97f), Pal.A(Pal.Magenta, 0.8f), S(1.2f));
            Gfx.TextSpaced(dl, FontRole.Body, S(11), pMin + new Vector2(S(16), S(9)), Pal.U(Pal.Magenta), "BUY-BACK REQUEST", S(1.2f));
            var name = SeatArt.Fit(SeatArt.ShortName(r.Name), w - S(32), S(16));
            dl.AddText(UiEnv.Font(FontRole.Body, S(16)), S(16), pMin + new Vector2(S(16), S(25)), Pal.U(Pal.Text), name);
            var amount = host.RebuyAmount(r.Id);
            ImGui.SetCursorScreenPos(new Vector2(pMin.X + S(16), pMax.Y - S(34)));
            if (W.Button($"Approve +{Gfx.Chips(amount)}##rbok{r.Id}", new Vector2(w * 0.55f, S(26)), Tone.Success, amount > 0)) host.ApproveRebuy(r.Id);
            ImGui.SameLine(0, S(8));
            if (W.Button($"Decline##rbno{r.Id}", new Vector2(w - w * 0.55f - S(40), S(26)), Tone.Neutral)) host.DenyRebuy(r.Id);
            y += h + S(10);
        }
        if (host.RebuyRequests.Count > 3)
            dl.AddText(UiEnv.Font(FontRole.Body, S(13)), S(13), new Vector2(x + S(6), y), Pal.U(Pal.Muted), $"+{host.RebuyRequests.Count - 3} more in Players");
    }

    private static void Toasts(ImDrawListPtr dl, GameApp app, Vector2 min, Vector2 max)
    {
        var now = app.Clock.Now;
        var recent = app.Notices.Where(n => (now - n.At).TotalSeconds < 6).TakeLast(3).ToList();
        var y = min.Y + S(14);
        foreach (var n in recent)
        {
            var age = (now - n.At).TotalSeconds;
            var alpha = (float)Math.Clamp(Math.Min(age * 4, (6 - age) * 1.5), 0, 1);
            var fs = S(14);
            var ts = Gfx.TextSize(FontRole.Body, fs, n.Text);
            var c = new Vector2((min.X + max.X) / 2, y + ts.Y / 2 + S(7));
            var bMin = c - new Vector2(ts.X / 2 + S(16), ts.Y / 2 + S(7));
            var bMax = c + new Vector2(ts.X / 2 + S(16), ts.Y / 2 + S(7));
            Gfx.Shadow(dl, bMin, bMax, S(4), S(6), 0.5f * alpha, new Vector2(0, S(2)));
            Gfx.ChamferPanel(dl, bMin, bMax, S(9), Pal.Hex(0x131C40, 0.96f * alpha), Pal.Hex(0x0A1028, 0.96f * alpha), Pal.A(Pal.Accent, 0.6f * alpha), 1f);
            dl.AddText(UiEnv.Font(FontRole.Body, fs), fs, c - ts / 2, Pal.U(Pal.Text, alpha), n.Text);
            y = bMax.Y + S(6);
        }
    }
}
