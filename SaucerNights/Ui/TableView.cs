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
    private readonly ActionBar _bar = new();
    private readonly SidePanel _side = new();
    private readonly TableLog _log = new();
    private ClientSession? _current;
    private bool _confirmLeave;

    private static float S(float v) => UiEnv.S(v);

    public static float HeaderHeight => S(52);

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
            TableArt.Backdrop(dl, min, max);
            var c = (min + max) / 2;
            CardArt.Clover(dl, c - new Vector2(0, S(70)), S(70), Pal.A(Pal.Gold, 0.5f));
            Gfx.TextCentered(dl, FontRole.Heading, S(30), c + new Vector2(0, S(10)), Pal.U(Pal.Text),
                app.Hub.Closing ? "Closing the table..." : "You're not at a table");
            Gfx.TextCentered(dl, FontRole.Body, S(15), c + new Vector2(0, S(42)), Pal.U(Pal.Muted), "Find, host or practise a game from the lobby.");
            ImGui.SetCursorScreenPos(c + new Vector2(-S(90), S(70)));
            if (W.Button("Open lobby##lobby", new Vector2(S(180), S(42)), Tone.Primary, icon: Icon.Spade)) openLobby();
            return;
        }

        var side = app.Settings.ShowSidePanel ? SidePanel.Width : 0;
        var headerMax = new Vector2(max.X, min.Y + HeaderHeight);
        var sceneMin = new Vector2(min.X, headerMax.Y);
        var sceneMax = new Vector2(max.X - side, max.Y - ActionBar.Height);

        // Scene.
        dl.PushClipRect(sceneMin, sceneMax, true);
        if (s.Table.Game == GameKind.Blackjack) _blackjack.Draw(dl, sceneMin, sceneMax, s, app, now);
        else _poker.Draw(dl, sceneMin, sceneMax, s, app, now);
        Toasts(dl, app, sceneMin, sceneMax);
        dl.PopClipRect();

        Header(dl, app, s, min, headerMax, openLobby);

        var focused = app.Settings.KeyboardShortcuts && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.GetIO().WantTextInput;
        _bar.Draw(new Vector2(min.X, sceneMax.Y), new Vector2(sceneMax.X, max.Y), s, app, focused);

        if (side > 0) _side.Draw(new Vector2(sceneMax.X, headerMax.Y), max, s, app, _log);

        ImGui.SetCursorScreenPos(max);
        ImGui.Dummy(Vector2.Zero);
    }

    private void Header(ImDrawListPtr dl, GameApp app, ClientSession s, Vector2 min, Vector2 max, Action openLobby)
    {
        Gfx.RoundedGradient(dl, min, max, 0, Pal.Hex(0x171D25), Pal.Hex(0x10151B));
        dl.AddLine(new Vector2(min.X, max.Y - 1), new Vector2(max.X, max.Y - 1), Pal.U(Pal.Line));

        var p = min + new Vector2(S(16), 0);
        var cy = (min.Y + max.Y) / 2;
        if (s.Table.Game == GameKind.Blackjack)
        {
            dl.AddRectFilled(new Vector2(p.X, cy - S(14)), new Vector2(p.X + S(28), cy + S(14)), Pal.U(Pal.Gold, 0.18f), S(6));
            Gfx.TextCentered(dl, FontRole.Heading, S(16), new Vector2(p.X + S(14), cy + S(1)), Pal.U(Pal.Gold), "21");
        }
        else
        {
            Icons.Draw(dl, Icon.Spade, new Vector2(p.X + S(14), cy), S(24), Pal.Gold);
        }
        var title = s.Table.Title;
        dl.AddText(UiEnv.Font(FontRole.Heading, S(22)), S(22), new Vector2(p.X + S(38), cy - S(16)), Pal.U(Pal.Text), title);
        var game = s.Table.Game == GameKind.Blackjack ? $"Blackjack  ·  Bets {s.Table.StakesText}" : $"No-Limit Hold'em  ·  Blinds {s.Table.StakesText}";
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
        if (s.Table.Flags.HasFlag(TableFlags.ProvablyFair))
        {
            ImGui.SameLine(0, S(6));
            var fair = s.Fairness.Count == 0 || s.Fairness[^1].Status != Core.Fairness.FairStatus.Failed;
            W.Pill(fair ? "Provably fair" : "Fairness check failed", fair ? Pal.Gold : Pal.Red);
        }
        if (s.Table.Flags.HasFlag(TableFlags.Paused))
        {
            ImGui.SameLine(0, S(6));
            W.Pill("Paused", Pal.Orange);
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
            var inHand = s.IsSeated && (s.Blackjack?.Seat(s.MySeat)?.Hands.Count > 0 || s.Poker?.Seat(s.MySeat)?.InHand == true);
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
            Gfx.Shadow(dl, bMin, bMax, S(12), S(6), 0.5f * alpha, new Vector2(0, S(2)));
            dl.AddRectFilled(bMin, bMax, Pal.U(Pal.Hex(0x161C24), 0.95f * alpha), S(12));
            dl.AddRect(bMin, bMax, Pal.U(Pal.Gold, 0.45f * alpha), S(12));
            dl.AddText(UiEnv.Font(FontRole.Body, fs), fs, c - ts / 2, Pal.U(Pal.Text, alpha), n.Text);
            y = bMax.Y + S(6);
        }
    }
}
