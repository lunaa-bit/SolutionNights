using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Ui;

/// <summary>The lobby window: find tables, host one, practise, settings and help.</summary>
public sealed class LobbyView
{
    private static readonly (string Name, Icon Icon)[] Pages =
    [
        ("Play", Icon.Spade),
        ("Host", Icon.Crown),
        ("Practice", Icon.Robot),
        ("Settings", Icon.Gear),
        ("About", Icon.Info),
    ];

    private readonly Dictionary<int, float> _navHover = new();
    private int _page;
    private int _hostGame;
    private int _practiceGame;
    private float _navSlide = -1;

    private static float S(float v) => UiEnv.S(v);

    public int Page
    {
        get => _page;
        set => _page = Math.Clamp(value, 0, Pages.Length - 1);
    }

    public void Draw(GameApp app, Action openTable)
    {
        var min = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        var max = min + size;
        var dl = ImGui.GetWindowDrawList();
        if (!app.Settings.IntroDone)
        {
            Intro(app, min, max);
            return;
        }

        // The city behind everything, dimmed so pages stay readable.
        TableArt.Backdrop(dl, min, max, FeltStyle.Neon, UiEnv.Now, 0.8f);
        dl.AddRectFilledMultiColor(min, max, Pal.U(Pal.Black, 0.25f), Pal.U(Pal.Black, 0.45f), Pal.U(Pal.Black, 0.55f), Pal.U(Pal.Black, 0.35f));

        // Navigation rail: a glass panel.
        var m = S(10);
        var navW = S(236);
        var navMin = min + new Vector2(m);
        var navMax = new Vector2(min.X + m + navW, max.Y - m);
        W.Glass(dl, navMin, navMax, S(16));
        Logo.Wordmark(dl, navMin + new Vector2(S(18), S(18)), S(42), navW - S(34));
        var y = navMin.Y + S(104);
        if (_navSlide < 0) _navSlide = _page;
        _navSlide = Ease.Approach(_navSlide, _page, 18);
        var itemH = S(42);
        var itemW = navW - S(24);
        var x0 = navMin.X + S(12);
        var selMin = new Vector2(x0, y + _navSlide * (itemH + S(4)));
        var selMax = selMin + new Vector2(itemW, itemH);
        dl.AddRectFilledMultiColor(selMin, selMax, Pal.U(Pal.Accent, 0.22f), Pal.U(Pal.Accent, 0.02f), Pal.U(Pal.Accent, 0.02f), Pal.U(Pal.Accent, 0.22f));
        Gfx.ChamferOutline(dl, selMin, selMax, S(10), Pal.U(Pal.Accent, 0.45f), 1f);
        Gfx.NeonLine(dl, selMin + new Vector2(0, S(10)), new Vector2(selMin.X, selMax.Y - S(7)), Pal.Accent, S(2.5f), 0.9f);
        Span<Vector2> hex = stackalloc Vector2[6];
        for (var i = 0; i < Pages.Length; i++)
        {
            var p = new Vector2(x0, y + i * (itemH + S(4)));
            ImGui.SetCursorScreenPos(p);
            if (ImGui.InvisibleButton($"##nav{i}", new Vector2(itemW, itemH))) _page = i;
            var hovered = ImGui.IsItemHovered();
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            _navHover.TryGetValue(i, out var h);
            h = Ease.Approach(h, hovered && i != _page ? 1 : 0, 16);
            _navHover[i] = h;
            if (h > 0.01f) Gfx.ChamferPanel(dl, p, p + new Vector2(itemW, itemH), S(10), Pal.A(Pal.Panel3, h * 0.7f), Pal.A(Pal.Panel3, h * 0.4f));
            var sel = i == _page;
            var col = sel ? Pal.AccentLight : Pal.Mix(Pal.Muted, Pal.Text, h);
            var ic = p + new Vector2(S(24), itemH / 2);
            Gfx.Hexagon(hex, ic, S(14));
            Gfx.FillConvex(dl, hex, Pal.U(sel ? Pal.Hex(0x10305A) : Pal.Hex(0x0C1330), 0.9f));
            Gfx.Outline(dl, hex, Pal.U(sel ? Pal.Accent : Pal.Line, sel ? 0.9f : 0.8f), S(1.2f));
            Icons.Draw(dl, Pages[i].Icon, ic, S(15), col);
            dl.AddText(UiEnv.Font(FontRole.Body, S(16)), S(16), p + new Vector2(S(48), (itemH - S(16)) / 2 - S(1)), Pal.U(sel ? Pal.Text : col), Pages[i].Name);
        }

        // Footer: the way back to a table, then channel status.
        var fy = navMax.Y - S(14);
        fy -= S(46);
        Status(dl, app, new Vector2(x0, fy), itemW);
        if (app.Hub.Table != null || app.Hub.Closing)
        {
            fy -= S(52);
            ImGui.SetCursorScreenPos(new Vector2(x0, fy));
            if (W.Button("Back to table##back", new Vector2(itemW, S(42)), Tone.Primary, app.Hub.Table != null, icon: Icon.Play)) openTable();
        }

        // Page.
        var pageMin = new Vector2(navMax.X + S(18), min.Y + S(14));
        ImGui.SetCursorScreenPos(pageMin + new Vector2(S(6), S(6)));
        if (ImGui.BeginChild("##page", max - pageMin - new Vector2(S(14), S(14)), false))
        {
            switch (_page)
            {
                case 0: Play(app, openTable); break;
                case 1: Host(app, openTable); break;
                case 2: Practice(app); break;
                case 3: Settings(app); break;
                default: About(); break;
            }
            ImGui.Dummy(new Vector2(1, S(10)));
        }
        ImGui.EndChild();
    }

    private static void Status(ImDrawListPtr dl, GameApp app, Vector2 p, float w)
    {
        var h = S(44);
        Gfx.ChamferPanel(dl, p, p + new Vector2(w, h), S(9), Pal.Hex(0x121A3A, 0.95f), Pal.Hex(0x0A0F24, 0.95f), Pal.Line);
        var ok = app.Bridge.LoggedIn;
        var dot = ok ? Pal.Green : Pal.Muted;
        dl.AddCircleFilled(p + new Vector2(S(14), h / 2), S(4), Pal.U(dot), 12);
        if (ok) Gfx.GlowCircle(dl, p + new Vector2(S(14), h / 2), S(4), dot, S(4), 0.6f);
        var label = SeatArt.Fit(app.ChannelLabel, w - S(34), S(14));
        dl.AddText(UiEnv.Font(FontRole.Body, S(14)), S(14), p + new Vector2(S(26), S(5)), Pal.U(Pal.Text), label);
        var sub = ok ? SeatArt.Fit(SeatArt.ShortName(app.Bridge.SelfId), w - S(34), S(12)) : "Not logged in";
        dl.AddText(UiEnv.Font(FontRole.Body, S(12)), S(12), p + new Vector2(S(26), S(24)), Pal.U(Pal.Muted), sub);
    }

    private static void PageTitle(string title, string sub)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var fs = S(34);
        var ts = Gfx.TextSize(FontRole.Heading, fs, title.ToUpperInvariant());
        Gfx.TextSpaced(dl, FontRole.Heading, fs, p, Pal.U(Pal.Text), title.ToUpperInvariant(), S(1.5f));
        var ly = p.Y + ts.Y + S(4);
        Gfx.NeonLine(dl, new Vector2(p.X, ly), new Vector2(p.X + S(46), ly), Pal.Accent, S(2), 0.8f);
        dl.AddLine(new Vector2(p.X + S(52), ly), new Vector2(p.X + S(60), ly), Pal.U(Pal.Magenta, 0.9f), S(2));
        ImGui.Dummy(new Vector2(ts.X, ts.Y + S(12)));
        W.Muted(sub);
        ImGui.Dummy(new Vector2(1, S(10)));
    }

    // ───────────── Play ─────────────

    private void Play(GameApp app, Action openTable)
    {
        var now = app.Clock.Now;
        var tables = app.Hub.Lobby.Tables(now);
        // Opening an empty lobby asks hosts to announce (at most every two minutes).
        if (tables.Count == 0 && app.CanUseChannel() == null && !app.Hub.Busy && (now - app.Hub.Lobby.LastRefresh).TotalSeconds > 120)
            app.Hub.Lobby.Refresh(now);
        var w = W.Avail();
        var start = ImGui.GetCursorScreenPos();
        PageTitle("Find a table", $"Tables hosted on {app.ChannelLabel}. Everyone at a table needs Solution Nights and the same linkshell.");
        var after = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(start.X + w - S(140), start.Y + S(4)));
        var canScan = app.CanUseChannel() == null;
        var scanning = (now - app.Hub.Lobby.LastRefresh).TotalSeconds < 4;
        if (W.Button(scanning ? "Scanning...##scan" : "Scan##scan", new Vector2(S(140), S(36)), Tone.Neutral, canScan && !scanning, icon: Icon.Refresh))
            app.Hub.Lobby.Refresh(now);
        ImGui.SetCursorScreenPos(after);

        if (app.CanUseChannel() is { } problem)
        {
            W.Panel(null, () =>
            {
                ImGui.TextColored(Pal.Orange, problem);
                W.Muted("You can still try a practice table against computer players.");
            }, Pal.Orange);
        }

        if (tables.Count == 0)
        {
            EmptyState(app);
            return;
        }

        foreach (var t in tables) TableCard(app, t, openTable);
    }

    private void EmptyState(GameApp app)
    {
        var dl = ImGui.GetWindowDrawList();
        var w = W.Avail();
        var p = ImGui.GetCursorScreenPos() + new Vector2(0, S(20));
        var c = new Vector2(p.X + w / 2, p.Y + S(70));
        var cards = Card.ParseMany("Qh Ks As");
        for (var i = 0; i < 3; i++)
            CardArt.Draw(dl, c + new Vector2((i - 1) * S(54), MathF.Abs(i - 1) * S(8)), S(96), cards[i], 1, 1, i == 2, false, 0, i < 2 ? S(54) / CardArt.Size(S(96)).X : 1f);
        Gfx.TextGlow(dl, FontRole.Heading, S(26), c + new Vector2(0, S(92)), Pal.Accent, "NO TABLES YET", 0.6f);
        Gfx.TextCentered(dl, FontRole.Body, S(15), c + new Vector2(0, S(122)), Pal.U(Pal.Muted), "Open tables announce themselves about once a minute.");
        Gfx.TextCentered(dl, FontRole.Body, S(15), c + new Vector2(0, S(142)), Pal.U(Pal.Muted), "Press Scan to ask now, host your own, or practise against bots.");
        ImGui.SetCursorScreenPos(new Vector2(c.X - S(205), c.Y + S(172)));
        if (W.Button("Host a table##hostcta", new Vector2(S(200), S(44)), Tone.Primary, icon: Icon.Crown)) _page = 1;
        ImGui.SameLine(0, S(10));
        if (W.Button("Practice##practicecta", new Vector2(S(200), S(44)), Tone.Neutral, icon: Icon.Robot)) _page = 2;
    }

    private static void TableCard(GameApp app, TableInfo t, Action openTable)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = W.Avail();
        var h = S(92);
        var current = app.Hub.Table?.Table.TableId == t.TableId && app.Hub.Table.Table.HostId == t.HostId;
        var neon = t.Game switch { GameKind.Blackjack => Pal.Accent, GameKind.Roulette => Pal.Gold, _ => Pal.Magenta };
        var pMax = p + new Vector2(w, h);
        Gfx.Shadow(dl, p, pMax, S(4), S(6), 0.4f, new Vector2(0, S(2)));
        Gfx.ChamferPanel(dl, p, pMax, S(14), Pal.Hex(0x131B3C, 0.96f), Pal.Hex(0x0B1028, 0.96f), current ? Pal.Gold : Pal.Line, current ? S(1.5f) : S(1));
        Gfx.NeonLine(dl, new Vector2(p.X, p.Y + S(14)), new Vector2(p.X + S(14), p.Y), neon, S(1.6f), 0.6f);
        dl.AddRectFilledMultiColor(new Vector2(p.X + S(14), p.Y), new Vector2(p.X + S(160), p.Y + S(1.5f)), Pal.U(neon), Pal.U(neon, 0), Pal.U(neon, 0), Pal.U(neon));

        // Game badge.
        var bc = p + new Vector2(S(46), h / 2);
        Span<Vector2> hex = stackalloc Vector2[6];
        Gfx.Hexagon(hex, bc, S(31));
        Gfx.FillVertical(dl, hex, Pal.Darken(neon, 0.6f), Pal.Darken(neon, 0.85f));
        Gfx.NeonOutline(dl, hex, neon, S(1.4f), 0.7f);
        if (t.Game == GameKind.Blackjack)
        {
            CardArt.Draw(dl, bc + new Vector2(-S(7), S(1)), S(34), Card.Parse("Ad"), 1, 1, false, false, 0, 0.5f);
            CardArt.Draw(dl, bc + new Vector2(S(7), S(-1)), S(34), Card.Parse("Ks"));
        }
        else if (t.Game == GameKind.Roulette)
        {
            RouletteArt.Wheel(dl, bc, S(22), (float)UiEnv.Now * 0.4f, Pal.Gold);
        }
        else
        {
            ChipArt.Top(dl, bc, S(16), ChipArt.Denoms[3], 1, false);
            Icons.Draw(dl, Icon.Spade, bc, S(18), Pal.White);
        }

        var x = p.X + S(92);
        dl.AddText(UiEnv.Font(FontRole.Heading, S(22)), S(22), new Vector2(x, p.Y + S(12)), Pal.U(Pal.Text), t.Title);
        var gameName = t.Game switch { GameKind.Blackjack => "Blackjack", GameKind.Roulette => "European Roulette", _ => "No-Limit Hold'em" };
        var stakes = t.Game == GameKind.Poker ? $"Blinds {t.StakesText}" : $"Bets {t.StakesText}";
        dl.AddText(UiEnv.Font(FontRole.Body, S(14)), S(14), new Vector2(x, p.Y + S(40)), Pal.U(Pal.Muted), $"{gameName}  ·  {stakes}  ·  Dealer {SeatArt.ShortName(t.HostId)}");

        ImGui.SetCursorScreenPos(new Vector2(x, p.Y + S(62)));
        W.SeatDots(t.SeatsTaken, t.SeatsMax);
        ImGui.SameLine(0, S(10));
        var pillY = p.Y + S(59);
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, pillY));
        if (t.Flags.HasFlag(TableFlags.ProvablyFair)) { W.Pill("Provably fair", Pal.Accent); ImGui.SameLine(0, S(6)); }
        if (t.Game == GameKind.Poker && t.Flags.HasFlag(TableFlags.HostPlays))
        {
            W.Pill("Dealer plays", Pal.Orange);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The host also has a seat. Their game deals the cards, so it knows every hand. Only join if you trust them.");
            ImGui.SameLine(0, S(6));
        }
        if (t.Flags.HasFlag(TableFlags.InProgress)) { W.Pill("In play", Pal.Green); ImGui.SameLine(0, S(6)); }
        if (t.Flags.HasFlag(TableFlags.Paused)) { W.Pill("Paused", Pal.Orange); ImGui.SameLine(0, S(6)); }
        if (!t.Compatible) { W.Pill("Different version", Pal.Red); ImGui.SameLine(0, S(6)); }
        ImGui.NewLine();

        var bw = S(110);
        ImGui.SetCursorScreenPos(new Vector2(p.X + w - bw * 2 - S(26), p.Y + (h - S(40)) / 2));
        var open = t.SeatsTaken < t.SeatsMax && t.Compatible;
        if (current)
        {
            ImGui.SetCursorScreenPos(new Vector2(p.X + w - bw - S(16), p.Y + (h - S(40)) / 2));
            if (W.Button($"Open##open{t.TableId}", new Vector2(bw, S(40)), Tone.Primary)) openTable();
        }
        else
        {
            var free = !app.Hub.IsHosting;
            if (W.Button($"Watch##watch{t.TableId}", new Vector2(bw, S(40)), Tone.Neutral, t.Compatible && free, icon: Icon.Eye)) app.Join(t, true);
            ImGui.SameLine(0, S(8));
            if (W.Button($"Join##join{t.TableId}", new Vector2(bw, S(40)), Tone.Primary, open && free, icon: Icon.Users)) app.Join(t, false);
            if (!free && ImGui.IsItemHovered()) ImGui.SetTooltip("Close the table you're hosting first.");
        }
        ImGui.SetCursorScreenPos(p + new Vector2(0, h + S(10)));
        ImGui.Dummy(new Vector2(w, 0));
    }

    // ───────────── Host ─────────────

    private void Host(GameApp app, Action openTable)
    {
        PageTitle("Host a table", "You deal. Players on your linkshell see the table in their lobby and join with one click. Buying back in always needs your approval.");
        var settings = app.Settings;
        var w = W.Avail();
        GameTiles(ref _hostGame, w);
        ImGui.Dummy(new Vector2(1, S(8)));

        var title = settings.TableTitle;
        W.Caption("Table name");
        ImGui.SetNextItemWidth(w);
        if (ImGui.InputText("##title", ref title, 32))
        {
            settings.TableTitle = title;
            app.Save();
        }
        if (title.Length == 0)
        {
            var r = ImGui.GetItemRectMin();
            ImGui.GetWindowDrawList().AddText(r + new Vector2(S(10), S(4)), Pal.U(Pal.Faint), $"{SeatArt.ShortName(app.Bridge.SelfId)}'s {(_hostGame switch { 0 => "Blackjack", 1 => "Hold'em", _ => "Roulette" })}");
        }
        ImGui.Dummy(new Vector2(1, S(6)));

        var changed = _hostGame switch
        {
            0 => BlackjackRulesEditor(settings.Blackjack),
            1 => PokerRulesEditor(settings.Poker),
            _ => RouletteRulesEditor(settings.Roulette),
        };
        if (changed) app.Save();

        var problem = app.CanUseChannel();
        var busy = app.Hub.Busy;
        ImGui.Dummy(new Vector2(1, S(4)));
        if (W.Button(busy ? "Leave your current table first##open" : $"Open table on {ChatChannels.Short(settings.Channel)}##open", new Vector2(w, S(54)), Tone.Primary,
                problem == null && !busy, icon: Icon.Play, font: FontRole.Heading, fontSize: S(22)))
        {
            switch (_hostGame)
            {
                case 0: app.HostBlackjack(); break;
                case 1: app.HostPoker(); break;
                default: app.HostRoulette(); break;
            }
        }
        W.Muted(problem ?? $"Table messages are posted to {app.ChannelLabel} at most once every {settings.SendIntervalMs / 1000.0:0.0}s and hidden for players using the plugin.");
        if (busy && app.Hub.Table != null)
        {
            if (W.Button("Go to my table##goto", new Vector2(S(180), S(34)))) openTable();
        }
    }

    private sealed record GameTile(string Title, string Line1, string Line2, FeltStyle Felt, int Art);

    private static readonly GameTile[] Games =
    [
        new("BLACKJACK", "You're the house.", "Up to 7 players.", FeltStyle.Neon, 0),
        new("TEXAS HOLD'EM", "No-limit, side pots,", "2 to 9 players.", FeltStyle.Ultraviolet, 1),
        new("ROULETTE", "Single zero, every bet,", "up to 8 players.", FeltStyle.Gilded, 2),
    ];

    private static GameKind KindOf(int tile) => tile switch { 0 => GameKind.Blackjack, 1 => GameKind.Poker, _ => GameKind.Roulette };

    private static void GameTiles(ref int selected, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var count = Games.Length;
        var gap = S(12);
        var tw = (width - gap * (count - 1)) / count;
        var h = count > 2 ? S(142) : S(118);
        Span<Vector2> shape = stackalloc Vector2[8];
        for (var i = 0; i < count; i++)
        {
            var g = Games[i];
            var tp = p + new Vector2(i * (tw + gap), 0);
            ImGui.SetCursorScreenPos(tp);
            if (ImGui.InvisibleButton($"##game{i}", new Vector2(tw, h))) selected = i;
            var hovered = ImGui.IsItemHovered();
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var sel = selected == i;
            var (feltC, feltE, _, neon) = TableArt.Felt(g.Felt);
            var tMax = tp + new Vector2(tw, h);
            Gfx.Shadow(dl, tp, tMax, S(4), S(8), 0.45f, new Vector2(0, S(3)));
            if (sel) Gfx.ChamferGlow(dl, tp, tMax, S(16), neon, S(12), 0.9f);
            var n = Gfx.Chamfer(shape, tp, tMax, S(16));
            Gfx.FillRadial(dl, shape[..n], tp + new Vector2(tw * 0.7f, h * 0.35f), Pal.U(feltC), Pal.U(feltE));
            dl.PushClipRect(tp, tMax, true);
            Gfx.Skyline(dl, tp.X + tw * 0.3f, tMax.X, tMax.Y, h * 0.5f, 3 + i * 5, 0.45f, UiEnv.Now);
            Gfx.HexGrid(dl, tp, tMax, S(16), Pal.U(neon, 0.05f));

            // Art in the lower right, sized to the tile.
            var a = Math.Min(h * 0.5f, tw * 0.3f);
            var art = new Vector2(tMax.X - a * 0.95f, tp.Y + h * 0.62f);
            switch (g.Art)
            {
                case 0:
                    CardArt.Draw(dl, art + new Vector2(-a * 0.22f, a * 0.05f), a, Card.Parse("As"), 1, 1, false, false, 0, 0.44f / CardArt.Aspect);
                    CardArt.Draw(dl, art + new Vector2(a * 0.22f, -a * 0.03f), a, Card.Parse("Kh"), 1, 1, sel);
                    break;
                case 1:
                    ChipArt.Stack(dl, art + new Vector2(a * 0.36f, a * 0.42f), 1600, a * 0.18f);
                    CardArt.Draw(dl, art + new Vector2(-a * 0.3f, -a * 0.05f), a * 0.95f, Card.Parse("Qd"), 1, 1, false, false, 0, 0.35f / (0.95f * CardArt.Aspect));
                    CardArt.Draw(dl, art + new Vector2(a * 0.05f, -a * 0.1f), a * 0.95f, Card.Parse("Qc"), 1, 1, sel);
                    break;
                default:
                    RouletteArt.Wheel(dl, art + new Vector2(0, a * 0.08f), a * 0.62f, (float)UiEnv.Now * 0.3f, neon, (float)-UiEnv.Now * 0.9f, 0.74f);
                    break;
            }
            dl.PopClipRect();
            Gfx.ChamferOutline(dl, tp, tMax, S(16), Pal.U(sel ? neon : hovered ? Pal.Muted : Pal.Line), sel ? S(2) : S(1));

            var fs = S(26);
            var tsz = Gfx.TextSize(FontRole.Heading, fs, g.Title);
            if (tsz.X > tw - S(36)) fs *= (tw - S(36)) / tsz.X;
            dl.AddText(UiEnv.Font(FontRole.Heading, fs), fs, tp + new Vector2(S(18), S(18)), Pal.U(Pal.White), g.Title);
            dl.AddText(UiEnv.Font(FontRole.Body, S(13.5f)), S(13.5f), tp + new Vector2(S(18), S(52)), Pal.U(Pal.White, 0.75f), g.Line1);
            dl.AddText(UiEnv.Font(FontRole.Body, S(13.5f)), S(13.5f), tp + new Vector2(S(18), S(70)), Pal.U(Pal.White, 0.75f), g.Line2);
        }
        ImGui.SetCursorScreenPos(p + new Vector2(0, h));
        ImGui.Dummy(new Vector2(width, S(4)));
    }

    private static bool Byte(string label, ref byte value, int min, int max, string format = "%d") =>
        W.Slider(label, ref value, min, max, x => format.Replace("%d", x.ToString()));

    private static bool Toggle(string label, ref bool value, string? help = null) => W.Toggle(label, ref value, help);

    private static bool BlackjackRulesEditor(Core.Blackjack.BlackjackRules r)
    {
        var changed = false;
        W.Panel("Stakes", () =>
        {
            var half = (W.Avail() - S(12)) / 2;
            ImGui.BeginGroup();
            W.Caption("Minimum bet");
            changed |= W.Stepper("minbet", ref r.MinBet, 1, 1_000_000, Step(r.MinBet), half);
            ImGui.EndGroup();
            ImGui.SameLine(0, S(12));
            ImGui.BeginGroup();
            W.Caption("Maximum bet");
            changed |= W.Stepper("maxbet", ref r.MaxBet, r.MinBet, 100_000_000, Step(r.MaxBet), half);
            ImGui.EndGroup();
            W.Caption("Starting chips");
            changed |= W.Stepper("bjstack", ref r.StartingStack, r.MinBet, 1_000_000_000, Step(r.StartingStack));
        });
        W.Panel("Table", () =>
        {
            W.Caption("Seats");
            changed |= Byte("##bjseats", ref r.MaxSeats, 1, 7, "%d seats");
            W.Caption("Decks in the shoe");
            changed |= Byte("##decks", ref r.Decks, 1, 8, "%d decks");
            W.Caption("Time to act");
            changed |= Byte("##bjturn", ref r.TurnSeconds, 10, 90, "%d seconds");
            W.Caption("Betting window after the first bet");
            changed |= Byte("##betsec", ref r.BetSeconds, 5, 60, "%d seconds");
        });
        W.Panel("House rules", () =>
        {
            changed |= Toggle("Blackjack pays 6 to 5##sixfive", ref r.SixToFive, "Off pays the classic 3 to 2.");
            changed |= Toggle("Dealer hits soft 17##h17", ref r.DealerHitsSoft17);
            changed |= Toggle("Double on any two cards##dbl", ref r.DoubleAnyTwo, "Off: only on 9, 10 or 11.");
            changed |= Toggle("Double after split##das", ref r.DoubleAfterSplit);
            changed |= Toggle("Late surrender##sur", ref r.LateSurrender);
            changed |= Toggle("Insurance##ins", ref r.Insurance);
            changed |= Toggle("Re-split aces##rsa", ref r.ResplitAces);
            changed |= Toggle("Hit split aces##hsa", ref r.HitSplitAces);
            W.Caption("Most hands after splitting");
            changed |= Byte("##maxhands", ref r.MaxHands, 2, 4, "%d hands");
        });
        W.Panel("Dealing", () =>
        {
            changed |= Toggle("Provably fair shuffles##bjfair", ref r.ProvablyFair, "Players add a seed to every shoe and check each round's cards afterwards.");
            changed |= Toggle("Deal automatically##autodeal", ref r.AutoDeal, "Open betting by itself after every round.");
        });
        return changed;
    }

    private static bool PokerRulesEditor(Core.Poker.PokerRules r)
    {
        var changed = false;
        W.Panel("Stakes", () =>
        {
            var third = (W.Avail() - S(24)) / 3;
            ImGui.BeginGroup();
            W.Caption("Small blind");
            changed |= W.Stepper("sb", ref r.SmallBlind, 1, 1_000_000, Step(r.SmallBlind), third);
            ImGui.EndGroup();
            ImGui.SameLine(0, S(12));
            ImGui.BeginGroup();
            W.Caption("Big blind");
            changed |= W.Stepper("bb", ref r.BigBlind, r.SmallBlind, 2_000_000, Step(r.BigBlind), third);
            ImGui.EndGroup();
            ImGui.SameLine(0, S(12));
            ImGui.BeginGroup();
            W.Caption("Ante");
            changed |= W.Stepper("ante", ref r.Ante, 0, r.BigBlind, Math.Max(1, Step(r.Ante)), third);
            ImGui.EndGroup();
            W.Caption("Starting stack");
            changed |= W.Stepper("pkstack", ref r.StartingStack, r.BigBlind * 2, 1_000_000_000, Step(r.StartingStack));
            W.Muted($"That's {r.StartingStack / Math.Max(1, r.BigBlind):N0} big blinds.");
        });
        W.Panel("Table", () =>
        {
            W.Caption("Seats");
            changed |= Byte("##pkseats", ref r.MaxSeats, 2, 9, "%d seats");
            W.Caption("Time to act");
            changed |= Byte("##pkturn", ref r.TurnSeconds, 10, 90, "%d seconds");
            changed |= Toggle("I play too##hostplays", ref r.HostPlays, "Take a seat as well as dealing. Your client sees every card, so only do this with friends who trust you. Players see a \"Dealer plays\" warning before they join.");
            changed |= Toggle("Start hands automatically##autostart", ref r.AutoStart);
            changed |= Toggle("Provably fair shuffles##pkfair", ref r.ProvablyFair, "Players check every deck after the hand, so the deal can't be stacked. Folded hands stay secret.");
        });
        return changed;
    }

    private static bool RouletteRulesEditor(Core.Roulette.RouletteRules r)
    {
        var changed = false;
        W.Panel("Stakes", () =>
        {
            var half = (W.Avail() - S(12)) / 2;
            ImGui.BeginGroup();
            W.Caption("Minimum per bet");
            changed |= W.Stepper("rlmin", ref r.MinBet, 1, 1_000_000, Step(r.MinBet), half);
            ImGui.EndGroup();
            ImGui.SameLine(0, S(12));
            ImGui.BeginGroup();
            W.Caption("Maximum per bet");
            changed |= W.Stepper("rlmax", ref r.MaxBet, r.MinBet, 100_000_000, Step(r.MaxBet), half);
            ImGui.EndGroup();
            ImGui.BeginGroup();
            W.Caption("Table maximum per spin");
            changed |= W.Stepper("rltable", ref r.TableMax, r.MaxBet, 1_000_000_000, Step(r.TableMax), half);
            ImGui.EndGroup();
            ImGui.SameLine(0, S(12));
            ImGui.BeginGroup();
            W.Caption("Starting chips");
            changed |= W.Stepper("rlstack", ref r.StartingStack, r.MinBet, 1_000_000_000, Step(r.StartingStack), half);
            ImGui.EndGroup();
            W.Muted("Every bet on the layout pays the standard odds: 35 to 1 on a single number down to even money.");
        });
        W.Panel("Table", () =>
        {
            W.Caption("Seats");
            changed |= Byte("##rlseats", ref r.MaxSeats, 1, 8, "%d seats");
            W.Caption("Betting window after the first bet");
            changed |= Byte("##rlbetsec", ref r.BetSeconds, 5, 60, "%d seconds");
        });
        W.Panel("Dealing", () =>
        {
            changed |= Toggle("Provably fair spins##rlfair", ref r.ProvablyFair, "Players add a seed to every spin and check the result afterwards.");
            changed |= Toggle("Spin automatically##autospin", ref r.AutoSpin, "Reopen betting by itself after every spin.");
        });
        return changed;
    }

    private static long Step(long value) => value switch
    {
        < 10 => 1,
        < 100 => 5,
        < 1_000 => 10,
        < 10_000 => 100,
        < 100_000 => 1_000,
        _ => 10_000,
    };

    // ───────────── Practice ─────────────

    private void Practice(GameApp app)
    {
        PageTitle("Practice", "Play against computer players. Nothing is sent to chat, and the chips aren't real.");
        var w = W.Avail();
        GameTiles(ref _practiceGame, w);
        ImGui.Dummy(new Vector2(1, S(8)));
        W.Panel("Opponents", () =>
        {
            var bots = app.Settings.PracticeBots;
            if (W.Slider("##bots", ref bots, 1, 6, x => x == 1 ? "1 player" : $"{x} players"))
            {
                app.Settings.PracticeBots = bots;
                app.Save();
            }
            W.Muted(_practiceGame switch
            {
                0 => "Bots play basic strategy. Your table rules from the Host page are used.",
                1 => "Bots mix a preflop chart with odds-based play. Blinds and stacks come from the Host page.",
                _ => "Bots spread a few bets each spin. Limits and stacks come from the Host page.",
            });
        });
        ImGui.Dummy(new Vector2(1, S(4)));
        if (W.Button("Start practice##startpractice", new Vector2(w, S(54)), Tone.Primary, !app.Hub.IsHosting, icon: Icon.Play, font: FontRole.Heading, fontSize: S(22)))
            app.Practice(KindOf(_practiceGame));
        if (app.Hub.IsHosting) W.Muted("Close the table you're hosting first.");
    }

    // ───────────── Settings ─────────────

    private static void Settings(GameApp app)
    {
        PageTitle("Settings", "Saved automatically.");
        var s = app.Settings;
        var changed = false;

        W.Panel("Interface", () =>
        {
            W.Caption("Size");
            var scale = s.UiScale;
            if (W.SliderF("##uiscale", ref scale, App.Settings.MinScale, App.Settings.MaxScale, 0.05f, x => $"{x * 100:0}%"))
            {
                s.UiScale = scale;
                changed = true;
            }
            W.Muted("Scales every Solution Nights window, on top of Dalamud's own global font scale.");
            ImGui.Dummy(new Vector2(1, S(2)));
            if (W.Button("Show the welcome screen##intro", new Vector2(S(220), S(32)), Tone.Neutral, icon: Icon.Info))
            {
                s.IntroDone = false;
                changed = true;
            }
        });

        W.Panel("Linkshell", () =>
        {
            W.Caption("Channel used for games");
            var locked = app.Hub.Busy && !app.Hub.IsPractice;
            ImGui.BeginDisabled(locked);
            ImGui.SetNextItemWidth(W.Avail());
            if (ImGui.BeginCombo("##channel", app.ChannelLabel))
            {
                foreach (var ch in ChatChannels.All)
                {
                    var name = app.Bridge.ChannelName(ch);
                    var label = string.IsNullOrWhiteSpace(name) ? ChatChannels.Short(ch) : $"{ChatChannels.Short(ch)}  ·  {name}";
                    if (ImGui.Selectable(label, ch == s.Channel))
                    {
                        s.Channel = ch;
                        changed = true;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
            W.Muted(locked
                ? "Leave your table to change the channel."
                : "Pick the slot this linkshell sits in on your own list. Everyone at a table must be in the same linkshell, but it can be a different number for each player. A cross-world linkshell lets friends on other worlds join.");
            ImGui.Dummy(new Vector2(1, S(4)));
            var hide = s.HideProtocolLines;
            if (W.Toggle("Hide game messages in chat##hide", ref hide, "Solution Nights lines are hidden from your chat log. Members without the plugin will still see them."))
            {
                s.HideProtocolLines = hide;
                changed = true;
            }
            W.Caption("Message pacing");
            var pace = s.SendIntervalMs / 1000f;
            if (W.SliderF("##pace", ref pace, 0.8f, 3f, 0.1f, x => $"{x:0.0} s"))
            {
                s.SendIntervalMs = (int)(pace * 1000);
                changed = true;
            }
            W.Muted("Slower is gentler on the game's chat spam limits; 1.2 s works well.");
        });

        W.Panel("Table look", () =>
        {
            W.Caption("Felt");
            var dl = ImGui.GetWindowDrawList();
            Span<Vector2> hex = stackalloc Vector2[6];
            foreach (var f in Looks.Felts)
            {
                var p = ImGui.GetCursorScreenPos();
                var size = S(46);
                if (ImGui.InvisibleButton($"##felt{f}", new Vector2(size))) { s.Felt = f; changed = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(Looks.Name(f));
                var (c, e, _, neon) = TableArt.Felt(f);
                Gfx.Hexagon(hex, p + new Vector2(size / 2), size / 2 - S(2));
                Gfx.FillRadial(dl, hex, p + new Vector2(size / 2), Pal.U(c), Pal.U(e));
                if (f == s.Felt) Gfx.NeonOutline(dl, hex, neon, S(2), 1f);
                else Gfx.Outline(dl, hex, Pal.U(neon, 0.45f), S(1));
                ImGui.SameLine(0, S(10));
            }
            ImGui.NewLine();
            ImGui.Dummy(new Vector2(1, S(4)));
            W.Caption("Card backs");
            var old = CardArt.Back;
            foreach (var b in Looks.Backs)
            {
                var p = ImGui.GetCursorScreenPos();
                var size = new Vector2(S(44), S(62));
                if (ImGui.InvisibleButton($"##back{b}", size)) { s.CardBack = b; changed = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(Looks.Name(b));
                CardArt.Back = b;
                CardArt.Draw(dl, p + size / 2, size.Y, Card.Hidden, 0, 1, b == s.CardBack);
                ImGui.SameLine(0, S(12));
            }
            CardArt.Back = old;
            ImGui.NewLine();
            ImGui.Dummy(new Vector2(1, S(6)));
            var four = s.FourColorDeck;
            if (W.Toggle("Four-colour deck##four", ref four, "Clubs green and diamonds blue, so suits are easier to tell apart."))
            {
                s.FourColorDeck = four;
                changed = true;
            }
            var strength = s.ShowHandStrength;
            if (W.Toggle("Show my hand strength##strength", ref strength))
            {
                s.ShowHandStrength = strength;
                changed = true;
            }
            W.Caption("Animation speed");
            var speed = s.AnimationSpeed;
            if (W.SliderF("##speed", ref speed, 0.5f, 2f, 0.1f, x => $"{x:0.0}x"))
            {
                s.AnimationSpeed = speed;
                changed = true;
            }
        });

        W.Panel("Sounds", () =>
        {
            var turn = s.TurnSound;
            if (W.Toggle("Play a sound when it's my turn##turnsound", ref turn)) { s.TurnSound = turn; changed = true; }
            changed |= SoundPicker(app, "turnse", s.TurnSoundEffect, v => s.TurnSoundEffect = v);
            var win = s.WinSound;
            if (W.Toggle("Play a sound when I win##winsound", ref win)) { s.WinSound = win; changed = true; }
            changed |= SoundPicker(app, "winse", s.WinSoundEffect, v => s.WinSoundEffect = v);
        });

        W.Panel("Gameplay", () =>
        {
            var confirm = s.ConfirmFoldWhenCheckIsFree;
            if (W.Toggle("Ask before folding when I could check##conf", ref confirm)) { s.ConfirmFoldWhenCheckIsFree = confirm; changed = true; }
            var keys = s.KeyboardShortcuts;
            if (W.Toggle("Keyboard shortcuts in the table window##keys", ref keys, "H/S/D/P/R for blackjack, F/C/R for poker. The game also receives these keys, so S may move your character."))
            {
                s.KeyboardShortcuts = keys;
                changed = true;
            }
            var open = s.OpenTableOnJoin;
            if (W.Toggle("Open the table window when I join##openjoin", ref open)) { s.OpenTableOnJoin = open; changed = true; }
        });

        if (changed) app.Save();
    }

    private static bool SoundPicker(GameApp app, string id, int value, Action<int> set)
    {
        var v = value;
        var changed = false;
        var p = ImGui.GetCursorScreenPos();
        var w = W.Avail();
        if (W.Slider($"##{id}", ref v, 1, 16, x => $"<se.{x}>", w - S(84)))
        {
            set(v);
            changed = true;
        }
        var after = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(p.X + w - S(74), p.Y));
        if (W.Button($"Test##{id}t", new Vector2(S(74), S(28)), Tone.Neutral, app.Bridge.LoggedIn)) app.Bridge.PlaySound(v);
        ImGui.SetCursorScreenPos(after);
        return changed;
    }

    // ───────────── About ─────────────

    private static void About()
    {
        PageTitle("How it works", "Solution Nights runs card games over a linkshell everyone at the table shares.");
        W.Panel("The table", () =>
        {
            W.Muted("One player hosts and deals. Their game keeps the cards and chips and posts short coded messages to the linkshell; " +
                    "everyone else's plugin reads them and draws the table. Your moves go back the same way. Messages are paced to stay well inside the game's chat limits.");
        });
        W.Panel("Privacy and fairness", () =>
        {
            W.Muted("Hole cards are encrypted for their owner (P-256 key exchange), so other linkshell members can't read them. " +
                    "With provably fair shuffles, the host commits to each deck before the deal, players mix in their own random seeds, and every client checks the cards afterwards.");
            W.Muted("The host's game necessarily knows every card, so play with people you trust.");
        });
        W.Panel("Chips", () =>
        {
            W.Muted("Chips are play money kept by the host. Solution Nights never trades gil or items. Buying back in needs the host's approval, and leaving keeps your chips for when you return. If your group settles up afterwards, the host's ledger shows who is up or down. " +
                    "Please respect Square Enix's terms of service.");
        }, Pal.Orange);
        W.Panel("Commands", () =>
        {
            Row("/snights", "Open the lobby");
            Row("/snights table", "Open the table window");
            Row("/snights host bj | poker | roulette", "Open a table with your saved rules");
            Row("/snights practice bj | poker | roulette", "Start a practice table");
            Row("/snights leave", "Leave or close the current table");
        });
        W.Panel("Keyboard (turn on in Settings)", () =>
        {
            Row("H  S  D  P  R", "Hit, stand, double, split, surrender");
            Row("I  N", "Take or decline insurance");
            Row("Enter", "Place your blackjack or roulette bets");
            Row("F  C  R", "Fold, check or call, bet or raise");
        });
    }

    // ───────────── Welcome ─────────────

    private bool _showLinkshells;

    /// <summary>First run: a short welcome that asks which linkshell to play on (and how big the windows should be).</summary>
    private void Intro(GameApp app, Vector2 min, Vector2 max)
    {
        var dl = ImGui.GetWindowDrawList();
        var s = app.Settings;
        TableArt.Backdrop(dl, min, max, FeltStyle.Neon, UiEnv.Now, 0.6f);
        var w = max.X - min.X;
        var colW = Math.Min(S(780), w - S(40));
        var x = min.X + (w - colW) / 2;
        // Darken behind the column so text over the city stays easy to read.
        dl.AddRectFilledMultiColor(new Vector2(x - S(60), min.Y), new Vector2(x, max.Y), Pal.U(Pal.Black, 0), Pal.U(Pal.Black, 0.35f), Pal.U(Pal.Black, 0.35f), Pal.U(Pal.Black, 0));
        dl.AddRectFilled(new Vector2(x, min.Y), new Vector2(x + colW, max.Y), Pal.U(Pal.Black, 0.35f));
        dl.AddRectFilledMultiColor(new Vector2(x + colW, min.Y), new Vector2(x + colW + S(60), max.Y), Pal.U(Pal.Black, 0.35f), Pal.U(Pal.Black, 0), Pal.U(Pal.Black, 0), Pal.U(Pal.Black, 0.35f));

        ImGui.SetCursorScreenPos(new Vector2(x, min.Y));
        if (!ImGui.BeginChild("##intro", new Vector2(colW, max.Y - min.Y), false))
        {
            ImGui.EndChild();
            return;
        }
        var cw0 = W.Avail();
        var left = ImGui.GetCursorScreenPos().X;
        var top = ImGui.GetCursorScreenPos().Y + S(16);
        dl = ImGui.GetWindowDrawList();

        // Brand.
        var markH = S(54);
        var brandW = Math.Min(cw0, S(470));
        Logo.Wordmark(dl, new Vector2(left + (cw0 - brandW) / 2, top), markH, brandW);
        var y = top + markH + S(20);
        Gfx.TextCentered(dl, FontRole.Body, S(16), new Vector2(left + cw0 / 2, y), Pal.U(Pal.Text, 0.9f),
            "Blackjack and hold'em with friends, dealt over a linkshell.");
        y += S(24);

        // Step 1: the linkshell.
        ImGui.SetCursorScreenPos(new Vector2(left, y));
        ImGui.BeginGroup();
        ImGui.PushItemWidth(cw0);
        var changed = false;
        Step(dl, colW, "1", "Pick your linkshell", () =>
        {
            W.Muted("Choose the slot your card-night linkshell sits in on your own list. Everyone at a table must be in the same linkshell, but its number can be different for each player.");
            ImGui.Dummy(new Vector2(1, S(4)));
            var cw = W.Avail();
            var cols = cw > S(560) ? 4 : 2;
            var gap = S(8);
            var tileW = (cw - gap * (cols - 1)) / cols;
            for (var i = 0; i < 8; i++)
            {
                if (i % cols != 0) ImGui.SameLine(0, gap);
                var ch = ChatChannel.Cwls1 + i;
                if (ChannelTile(app, ch, new Vector2(tileW, S(52)))) { s.Channel = ch; changed = true; }
            }
            ImGui.Dummy(new Vector2(1, S(2)));
            var other = !ChatChannels.IsCrossWorld(s.Channel);
            if (other) _showLinkshells = true;
            if (!_showLinkshells)
            {
                if (W.Button("Use a world linkshell or party instead##more", new Vector2(S(290), S(28)), Tone.Ghost)) _showLinkshells = true;
            }
            else
            {
                W.Caption("Linkshells on your world");
                var smallW = (cw - gap * 8) / 9;
                for (var i = 0; i < 9; i++)
                {
                    if (i > 0) ImGui.SameLine(0, gap);
                    var ch = i < 8 ? ChatChannel.Ls1 + i : ChatChannel.Party;
                    if (ChannelTile(app, ch, new Vector2(smallW, S(34)), compact: true)) { s.Channel = ch; changed = true; }
                }
                W.Muted("World linkshells and party only reach players on your own world (or in your party).");
            }
        });

        // Step 2: size.
        Step(dl, colW, "2", "Choose a size", () =>
        {
            var scale = s.UiScale;
            if (W.SliderF("##introscale", ref scale, App.Settings.MinScale, App.Settings.MaxScale, 0.05f, v => $"{v * 100:0}%"))
            {
                s.UiScale = scale;
                changed = true;
            }
            W.Muted("Makes every Solution Nights window bigger or smaller. You can change it later in Settings.");
        });

        // Go.
        ImGui.Dummy(new Vector2(1, S(4)));
        var bw = (cw0 - S(12)) / 2;
        if (W.Button("Let's play##introgo", new Vector2(bw, S(52)), Tone.Primary, icon: Icon.Play, font: FontRole.Heading, fontSize: S(22)))
        {
            s.IntroDone = true;
            _page = 0;
            changed = true;
        }
        ImGui.SameLine(0, S(12));
        if (W.Button("Practise against bots first##introbots", new Vector2(bw, S(52)), Tone.Neutral, icon: Icon.Robot))
        {
            s.IntroDone = true;
            _page = 2;
            changed = true;
        }
        ImGui.Dummy(new Vector2(1, S(6)));
        W.Muted($"Playing on {app.ChannelLabel}. Everything here can be changed later in Settings.");
        ImGui.PopItemWidth();
        ImGui.EndGroup();
        ImGui.Dummy(new Vector2(1, S(20)));
        if (changed) app.Save();
        ImGui.EndChild();
    }

    /// <summary>A numbered step panel on the welcome screen.</summary>
    private static void Step(ImDrawListPtr dl, float width, string number, string title, Action body)
    {
        W.Panel(null, () =>
        {
            var p = ImGui.GetCursorScreenPos();
            Span<Vector2> hex = stackalloc Vector2[6];
            var c = p + new Vector2(S(13), S(13));
            Gfx.Hexagon(hex, c, S(13));
            Gfx.FillConvex(ImGui.GetWindowDrawList(), hex, Pal.U(Pal.Hex(0x0B1530)));
            Gfx.NeonOutline(ImGui.GetWindowDrawList(), hex, Pal.Accent, S(1.4f), 0.7f);
            Gfx.TextCentered(ImGui.GetWindowDrawList(), FontRole.Heading, S(17), c + new Vector2(0, S(1)), Pal.U(Pal.AccentLight), number);
            ImGui.SetCursorScreenPos(p + new Vector2(S(36), S(1)));
            W.Heading(title.ToUpperInvariant(), 22);
            ImGui.SetCursorScreenPos(new Vector2(p.X, ImGui.GetCursorScreenPos().Y + S(2)));
            body();
        }, Pal.Accent, 18);
    }

    /// <summary>A selectable linkshell slot showing the linkshell's name when the game knows it.</summary>
    private static bool ChannelTile(GameApp app, ChatChannel ch, Vector2 size, bool compact = false)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##ch{ch}", size);
        var hovered = ImGui.IsItemHovered();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var sel = app.Settings.Channel == ch;
        var max = p + size;
        var cut = Math.Min(S(10), size.Y * 0.3f);
        if (sel) Gfx.ChamferGlow(dl, p, max, cut, Pal.Accent, S(8), 0.9f);
        Gfx.ChamferPanel(dl, p, max, cut, sel ? Pal.Hex(0x14345A) : hovered ? Pal.Hex(0x172146) : Pal.Hex(0x111936),
            sel ? Pal.Hex(0x0B1E3A) : Pal.Hex(0x0B1028), sel ? Pal.Accent : hovered ? Pal.A(Pal.Accent, 0.5f) : Pal.Line, sel ? S(1.5f) : 1f);
        var name = app.Bridge.ChannelName(ch);
        var label = ChatChannels.Short(ch);
        if (compact || string.IsNullOrWhiteSpace(name))
        {
            Gfx.TextCentered(dl, FontRole.Body, S(compact ? 13 : 15), (p + max) / 2, Pal.U(sel ? Pal.AccentLight : Pal.Text), compact && ch != ChatChannel.Party ? $"LS{ChatChannels.Number(ch)}" : label);
            if (!compact && app.Bridge.LoggedIn)
                Gfx.TextCentered(dl, FontRole.Body, S(11.5f), (p + max) / 2 + new Vector2(0, S(14)), Pal.U(Pal.Faint), "empty slot");
        }
        else
        {
            dl.AddText(UiEnv.Font(FontRole.Body, S(12)), S(12), p + new Vector2(S(12), S(8)), Pal.U(sel ? Pal.Accent : Pal.Muted), label.ToUpperInvariant());
            var fit = SeatArt.Fit(name, size.X - S(22), S(15));
            dl.AddText(UiEnv.Font(FontRole.Body, S(15)), S(15), p + new Vector2(S(12), S(25)), Pal.U(sel ? Pal.White : Pal.Text), fit);
        }
        if (sel) W.Diamond(dl, new Vector2(max.X - S(12), p.Y + S(12)), S(4), Pal.Accent);
        return clicked;
    }


    private static void Row(string key, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Pal.AccentLight);
        ImGui.TextUnformatted(key);
        ImGui.PopStyleColor();
        ImGui.SameLine(S(230));
        ImGui.PushStyleColor(ImGuiCol.Text, Pal.Muted);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }
}
