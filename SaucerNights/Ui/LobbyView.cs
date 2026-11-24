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
        var navW = S(226);

        // Navigation rail.
        Gfx.RoundedGradient(dl, min, new Vector2(min.X + navW, max.Y), 0, Pal.Hex(0x141A22), Pal.Hex(0x0E1217));
        dl.AddLine(new Vector2(min.X + navW, min.Y), new Vector2(min.X + navW, max.Y), Pal.U(Pal.Line));
        Brand(dl, min + new Vector2(S(18), S(18)), navW - S(18));
        var y = min.Y + S(96);
        if (_navSlide < 0) _navSlide = _page;
        _navSlide = Ease.Approach(_navSlide, _page, 18);
        var itemH = S(40);
        var selMin = new Vector2(min.X + S(10), y + _navSlide * (itemH + S(4)));
        dl.AddRectFilled(selMin, selMin + new Vector2(navW - S(20), itemH), Pal.U(Pal.Gold, 0.12f), S(10));
        dl.AddRect(selMin, selMin + new Vector2(navW - S(20), itemH), Pal.U(Pal.Gold, 0.35f), S(10));
        dl.AddRectFilled(selMin + new Vector2(0, S(10)), selMin + new Vector2(S(3), itemH - S(10)), Pal.U(Pal.Gold), S(2));
        for (var i = 0; i < Pages.Length; i++)
        {
            var p = new Vector2(min.X + S(10), y + i * (itemH + S(4)));
            ImGui.SetCursorScreenPos(p);
            if (ImGui.InvisibleButton($"##nav{i}", new Vector2(navW - S(20), itemH))) _page = i;
            var hovered = ImGui.IsItemHovered();
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            _navHover.TryGetValue(i, out var h);
            h = Ease.Approach(h, hovered && i != _page ? 1 : 0, 16);
            _navHover[i] = h;
            if (h > 0.01f) dl.AddRectFilled(p, p + new Vector2(navW - S(20), itemH), Pal.U(Pal.Panel3, h * 0.8f), S(10));
            var col = i == _page ? Pal.GoldLight : Pal.Mix(Pal.Muted, Pal.Text, h);
            Icons.Draw(dl, Pages[i].Icon, p + new Vector2(S(24), itemH / 2), S(18), col);
            dl.AddText(UiEnv.Font(FontRole.Body, S(16)), S(16), p + new Vector2(S(46), (itemH - S(16)) / 2 - S(1)), Pal.U(i == _page ? Pal.Text : col), Pages[i].Name);
        }

        // Footer: channel status and the way back to a table.
        var fy = max.Y - S(16);
        if (app.Hub.Table != null || app.Hub.Closing)
        {
            fy -= S(42);
            ImGui.SetCursorScreenPos(new Vector2(min.X + S(14), fy));
            if (W.Button("Back to table##back", new Vector2(navW - S(28), S(40)), Tone.Primary, app.Hub.Table != null, icon: Icon.Play)) openTable();
            fy -= S(10);
        }
        fy -= S(44);
        Status(dl, app, new Vector2(min.X + S(14), fy), navW - S(28));

        // Page.
        var pageMin = new Vector2(min.X + navW, min.Y);
        dl.AddRectFilled(pageMin, max, Pal.U(Pal.Bg));
        ImGui.SetCursorScreenPos(pageMin + new Vector2(S(24), S(20)));
        if (ImGui.BeginChild("##page", max - pageMin - new Vector2(S(36), S(24)), false))
        {
            switch (_page)
            {
                case 0: Play(app, openTable); break;
                case 1: Host(app, openTable); break;
                case 2: Practice(app); break;
                case 3: Settings(app); break;
                default: About(); break;
            }
        }
        ImGui.EndChild();
    }

    private static void Brand(ImDrawListPtr dl, Vector2 p, float width)
    {
        // Three fanned cards behind the clover mark.
        var c = p + new Vector2(S(24), S(28));
        CardArt.Draw(dl, c + new Vector2(-S(9), S(3)), S(38), Card.Parse("Ah"), 1, 0.9f);
        CardArt.Draw(dl, c + new Vector2(S(9), S(1)), S(38), Card.Parse("As"), 1, 1f);
        dl.AddCircleFilled(c + new Vector2(S(16), S(17)), S(12), Pal.U(Pal.Hex(0x15573A)), 24);
        dl.AddCircle(c + new Vector2(S(16), S(17)), S(12), Pal.U(Pal.Gold), 24, S(1.5f));
        CardArt.Clover(dl, c + new Vector2(S(16), S(16)), S(19), Pal.GoldLight);
        // Shrink the wordmark if the font runs wide.
        var size = S(23);
        var avail = width - S(56);
        var tw = Gfx.TextSize(FontRole.Heading, size, "Saucer Nights").X;
        if (tw > avail) size *= avail / tw;
        dl.AddText(UiEnv.Font(FontRole.Heading, size), size, p + new Vector2(S(52), S(8) + (S(23) - size) / 2), Pal.U(Pal.Text), "Saucer Nights");
        dl.AddText(UiEnv.Font(FontRole.Body, S(12.5f)), S(12.5f), p + new Vector2(S(53), S(36)), Pal.U(Pal.Muted), "Cards over your linkshell");
    }

    private static void Status(ImDrawListPtr dl, GameApp app, Vector2 p, float w)
    {
        var h = S(44);
        dl.AddRectFilled(p, p + new Vector2(w, h), Pal.U(Pal.Panel2), S(10));
        dl.AddRect(p, p + new Vector2(w, h), Pal.U(Pal.Line), S(10));
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
        W.Heading(title, 30);
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
        var w = ImGui.GetContentRegionAvail().X;
        var start = ImGui.GetCursorScreenPos();
        PageTitle("Find a table", $"Tables hosted on {app.ChannelLabel}. Everyone at a table needs Saucer Nights and the same linkshell.");
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
        var w = ImGui.GetContentRegionAvail().X;
        var p = ImGui.GetCursorScreenPos() + new Vector2(0, S(20));
        var c = new Vector2(p.X + w / 2, p.Y + S(70));
        var cards = Card.ParseMany("Qh Ks As");
        for (var i = 0; i < 3; i++)
            CardArt.Draw(dl, c + new Vector2((i - 1) * S(44), MathF.Abs(i - 1) * S(8)), S(96), cards[i], 1, 1, i == 2);
        Gfx.TextCentered(dl, FontRole.Heading, S(26), c + new Vector2(0, S(92)), Pal.U(Pal.Text), "No tables yet");
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
        var w = ImGui.GetContentRegionAvail().X;
        var h = S(92);
        var current = app.Hub.Table?.Table.TableId == t.TableId && app.Hub.Table.Table.HostId == t.HostId;
        Gfx.Shadow(dl, p, p + new Vector2(w, h), S(12), S(6), 0.35f, new Vector2(0, S(2)));
        Gfx.RoundedGradient(dl, p, p + new Vector2(w, h), S(12), Pal.Hex(0x1A222C), Pal.Hex(0x141A21));
        dl.AddRect(p, p + new Vector2(w, h), Pal.U(current ? Pal.Gold : Pal.Line), S(12), ImDrawFlags.None, current ? S(1.5f) : S(1));

        // Game badge.
        var bc = p + new Vector2(S(46), h / 2);
        var felt = t.Game == GameKind.Blackjack ? Pal.Hex(0x1E7A4C) : Pal.Hex(0x2A62A6);
        dl.AddCircleFilled(bc, S(30), Pal.U(Pal.Darken(felt, 0.2f)), 40);
        dl.AddCircle(bc, S(30), Pal.U(Pal.Gold, 0.7f), 40, S(1.5f));
        if (t.Game == GameKind.Blackjack)
        {
            CardArt.Draw(dl, bc + new Vector2(-S(7), S(1)), S(34), Card.Parse("Ad"));
            CardArt.Draw(dl, bc + new Vector2(S(7), S(-1)), S(34), Card.Parse("Ks"));
        }
        else
        {
            ChipArt.Top(dl, bc, S(16), ChipArt.Denoms[3], 1, false);
            Icons.Draw(dl, Icon.Spade, bc, S(18), Pal.White);
        }

        var x = p.X + S(92);
        dl.AddText(UiEnv.Font(FontRole.Heading, S(22)), S(22), new Vector2(x, p.Y + S(12)), Pal.U(Pal.Text), t.Title);
        var gameName = t.Game == GameKind.Blackjack ? "Blackjack" : "No-Limit Hold'em";
        var stakes = t.Game == GameKind.Blackjack ? $"Bets {t.StakesText}" : $"Blinds {t.StakesText}";
        dl.AddText(UiEnv.Font(FontRole.Body, S(14)), S(14), new Vector2(x, p.Y + S(40)), Pal.U(Pal.Muted), $"{gameName}  ·  {stakes}  ·  Dealer {SeatArt.ShortName(t.HostId)}");

        ImGui.SetCursorScreenPos(new Vector2(x, p.Y + S(62)));
        W.SeatDots(t.SeatsTaken, t.SeatsMax);
        ImGui.SameLine(0, S(10));
        var pillY = p.Y + S(59);
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, pillY));
        if (t.Flags.HasFlag(TableFlags.ProvablyFair)) { W.Pill("Provably fair", Pal.Gold); ImGui.SameLine(0, S(6)); }
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
        PageTitle("Host a table", "You deal. Players on your linkshell see the table in their lobby and join with one click.");
        var settings = app.Settings;
        var w = ImGui.GetContentRegionAvail().X;
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
            ImGui.GetWindowDrawList().AddText(r + new Vector2(S(10), S(4)), Pal.U(Pal.Faint), $"{SeatArt.ShortName(app.Bridge.SelfId)}'s {(_hostGame == 0 ? "Blackjack" : "Hold'em")}");
        }
        ImGui.Dummy(new Vector2(1, S(6)));

        var changed = _hostGame == 0 ? BlackjackRulesEditor(settings.Blackjack) : PokerRulesEditor(settings.Poker);
        W.Panel("Hosting", () =>
        {
            var v = settings.AutoApproveRebuys;
            if (W.Toggle("Approve rebuys automatically##rebuys", ref v, "Players who run low can top back up to the starting stack. Off: you approve each one."))
            {
                settings.AutoApproveRebuys = v;
                changed = true;
            }
        });
        if (changed) app.Save();

        var problem = app.CanUseChannel();
        var busy = app.Hub.Busy;
        ImGui.Dummy(new Vector2(1, S(4)));
        if (W.Button(busy ? "Leave your current table first##open" : $"Open table on {ChatChannels.Short(settings.Channel)}##open", new Vector2(w, S(54)), Tone.Primary,
                problem == null && !busy, icon: Icon.Play, font: FontRole.Heading, fontSize: S(22)))
        {
            if (_hostGame == 0) app.HostBlackjack();
            else app.HostPoker();
        }
        W.Muted(problem ?? $"Table messages are posted to {app.ChannelLabel} at most once every {settings.SendIntervalMs / 1000.0:0.0}s and hidden for players using the plugin.");
        if (busy && app.Hub.Table != null)
        {
            if (W.Button("Go to my table##goto", new Vector2(S(180), S(34)))) openTable();
        }
    }

    private static void GameTiles(ref int selected, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var h = S(116);
        var tw = (width - S(12)) / 2;
        for (var i = 0; i < 2; i++)
        {
            var tp = p + new Vector2(i * (tw + S(12)), 0);
            ImGui.SetCursorScreenPos(tp);
            if (ImGui.InvisibleButton($"##game{i}", new Vector2(tw, h))) selected = i;
            var hovered = ImGui.IsItemHovered();
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var sel = selected == i;
            var feltC = i == 0 ? Pal.Hex(0x23875A) : Pal.Hex(0x2A62A6);
            var feltE = i == 0 ? Pal.Hex(0x093A24) : Pal.Hex(0x0B2246);
            Gfx.Shadow(dl, tp, tp + new Vector2(tw, h), S(14), S(8), 0.4f, new Vector2(0, S(3)));
            var buf = new Vector2[64];
            var n = Gfx.RoundedRect(buf, tp, tp + new Vector2(tw, h), S(14), 8);
            Gfx.FillRadial(dl, buf.AsSpan(0, n), tp + new Vector2(tw * 0.7f, h * 0.4f), Pal.U(feltC), Pal.U(feltE));
            dl.AddRect(tp, tp + new Vector2(tw, h), Pal.U(sel ? Pal.Gold : hovered ? Pal.Muted : Pal.Line), S(14), ImDrawFlags.None, sel ? S(2) : S(1));
            if (sel) Gfx.Glow(dl, tp, tp + new Vector2(tw, h), S(14), Pal.Gold, S(8), 0.6f);

            var art = tp + new Vector2(tw - S(78), h / 2);
            if (i == 0)
            {
                CardArt.Draw(dl, art + new Vector2(-S(16), S(4)), S(74), Card.Parse("As"), 1, 1, false, false);
                CardArt.Draw(dl, art + new Vector2(S(16), -S(2)), S(74), Card.Parse("Kh"), 1, 1, sel);
            }
            else
            {
                ChipArt.Stack(dl, art + new Vector2(S(26), S(30)), 1600, S(13));
                CardArt.Draw(dl, art + new Vector2(-S(22), -S(4)), S(70), Card.Parse("Qd"));
                CardArt.Draw(dl, art + new Vector2(S(4), -S(8)), S(70), Card.Parse("Qc"), 1, 1, sel);
            }
            dl.AddText(UiEnv.Font(FontRole.Heading, S(26)), S(26), tp + new Vector2(S(18), S(18)), Pal.U(Pal.White), i == 0 ? "Blackjack" : "Texas Hold'em");
            dl.AddText(UiEnv.Font(FontRole.Body, S(13.5f)), S(13.5f), tp + new Vector2(S(18), S(52)), Pal.U(Pal.White, 0.75f), i == 0 ? "You're the house." : "No-limit, side pots,");
            dl.AddText(UiEnv.Font(FontRole.Body, S(13.5f)), S(13.5f), tp + new Vector2(S(18), S(70)), Pal.U(Pal.White, 0.75f), i == 0 ? "Up to 7 players." : "2 to 9 players.");
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
            var half = (ImGui.GetContentRegionAvail().X - S(12)) / 2;
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
            var third = (ImGui.GetContentRegionAvail().X - S(24)) / 3;
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
            changed |= Toggle("I play too##hostplays", ref r.HostPlays, "Take a seat as well as dealing. Your client sees every card, so only do this with friends who trust you.");
            changed |= Toggle("Start hands automatically##autostart", ref r.AutoStart);
            changed |= Toggle("Provably fair shuffles##pkfair", ref r.ProvablyFair, "Lets players verify every deck. The reveal also shows folded hands after each hand.");
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
        var w = ImGui.GetContentRegionAvail().X;
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
            W.Muted(_practiceGame == 0
                ? "Bots play basic strategy. Your table rules from the Host page are used."
                : "Bots mix a preflop chart with odds-based play. Blinds and stacks come from the Host page.");
        });
        ImGui.Dummy(new Vector2(1, S(4)));
        if (W.Button("Start practice##startpractice", new Vector2(w, S(54)), Tone.Primary, !app.Hub.IsHosting, icon: Icon.Play, font: FontRole.Heading, fontSize: S(22)))
            app.Practice(_practiceGame == 0 ? GameKind.Blackjack : GameKind.Poker);
        if (app.Hub.IsHosting) W.Muted("Close the table you're hosting first.");
    }

    // ───────────── Settings ─────────────

    private static void Settings(GameApp app)
    {
        PageTitle("Settings", "Saved automatically.");
        var s = app.Settings;
        var changed = false;

        W.Panel("Linkshell", () =>
        {
            W.Caption("Channel used for games");
            var locked = app.Hub.Busy && !app.Hub.IsPractice;
            ImGui.BeginDisabled(locked);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
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
            if (W.Toggle("Hide game messages in chat##hide", ref hide, "Saucer Nights lines are hidden from your chat log. Members without the plugin will still see them."))
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
            var felts = Enum.GetValues<FeltStyle>();
            var dl = ImGui.GetWindowDrawList();
            foreach (var f in felts)
            {
                var p = ImGui.GetCursorScreenPos();
                var size = S(44);
                if (ImGui.InvisibleButton($"##felt{f}", new Vector2(size))) { s.Felt = f; changed = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(f.ToString());
                var (c, e, _) = TableArt.Felt(f);
                var buf = new Vector2[40];
                var n = Gfx.Ellipse(buf, p + new Vector2(size / 2), new Vector2(size / 2 - S(2)), 32);
                Gfx.FillRadial(dl, buf.AsSpan(0, n), p + new Vector2(size / 2), Pal.U(c), Pal.U(e));
                dl.AddCircle(p + new Vector2(size / 2), size / 2 - S(1), Pal.U(f == s.Felt ? Pal.Gold : Pal.Line), 32, f == s.Felt ? S(2.5f) : S(1));
                ImGui.SameLine(0, S(10));
            }
            ImGui.NewLine();
            ImGui.Dummy(new Vector2(1, S(4)));
            W.Caption("Card backs");
            var backs = Enum.GetValues<CardBackStyle>();
            var old = CardArt.Back;
            foreach (var b in backs)
            {
                var p = ImGui.GetCursorScreenPos();
                var size = new Vector2(S(44), S(62));
                if (ImGui.InvisibleButton($"##back{b}", size)) { s.CardBack = b; changed = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(b.ToString());
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
        var w = ImGui.GetContentRegionAvail().X;
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
        PageTitle("How it works", "Saucer Nights runs card games over a linkshell everyone at the table shares.");
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
            W.Muted("Chips are play money kept by the host. Saucer Nights never trades gil or items. If your group settles up afterwards, the host's ledger shows who is up or down. " +
                    "Please respect Square Enix's terms of service.");
        }, Pal.Orange);
        W.Panel("Commands", () =>
        {
            Row("/saucer", "Open the lobby");
            Row("/saucer table", "Open the table window");
            Row("/saucer host bj | poker", "Open a table with your saved rules");
            Row("/saucer practice bj | poker", "Start a practice table");
            Row("/saucer leave", "Leave or close the current table");
        });
        W.Panel("Keyboard (turn on in Settings)", () =>
        {
            Row("H  S  D  P  R", "Hit, stand, double, split, surrender");
            Row("I  N", "Take or decline insurance");
            Row("F  C  R", "Fold, check or call, bet or raise");
            Row("Enter", "Place your blackjack bet");
        });
    }

    private static void Row(string key, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Pal.GoldLight);
        ImGui.TextUnformatted(key);
        ImGui.PopStyleColor();
        ImGui.SameLine(S(230));
        ImGui.PushStyleColor(ImGuiCol.Text, Pal.Muted);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }
}
