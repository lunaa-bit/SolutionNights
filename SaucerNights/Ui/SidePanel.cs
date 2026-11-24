using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Fairness;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Ui;

/// <summary>Right-hand panel: table log, linkshell chat, players and ledger, fairness checks and host tools.</summary>
public sealed class SidePanel
{
    private int _tab;
    private string _chat = string.Empty;
    private int _seenLog;
    private int _seenChat;
    private long _adjust = 100;
    private bool _confirmClose;

    private static float S(float v) => UiEnv.S(v);

    public static float Width => S(316);

    public void Draw(Vector2 min, Vector2 max, ClientSession? s, GameApp app, TableLog log)
    {
        var dl = ImGui.GetWindowDrawList();
        Gfx.RoundedGradient(dl, min, max, 0, Pal.Hex(0x12171E), Pal.Hex(0x0E1217));
        dl.AddLine(min, new Vector2(min.X, max.Y), Pal.U(Pal.Line), S(1));

        var hosting = app.Hub.Host != null;
        string[] tabs = hosting ? ["Log", "Chat", "Players", "Fair", "Host"] : ["Log", "Chat", "Players", "Fair"];
        _tab = Math.Min(_tab, tabs.Length - 1);
        var pad = S(12);
        ImGui.SetCursorScreenPos(min + new Vector2(pad, pad));
        W.Segmented("##sidetabs", tabs, ref _tab, max.X - min.X - pad * 2);

        var top = ImGui.GetCursorScreenPos().Y + S(8);
        ImGui.SetCursorScreenPos(new Vector2(min.X + pad, top));
        var size = new Vector2(max.X - min.X - pad * 2, max.Y - top - pad);
        switch (tabs[_tab])
        {
            case "Log": Log(log, size); break;
            case "Chat": Chat(app, size); break;
            case "Players": Players(s, app, size); break;
            case "Fair": Fairness(s, size); break;
            case "Host": Host(app, size); break;
        }
    }

    private void Log(TableLog log, Vector2 size)
    {
        if (!ImGui.BeginChild("##log", size, false))
        {
            ImGui.EndChild();
            return;
        }
        if (log.Lines.Count == 0) W.Muted("Table events will appear here.");
        var width = ImGui.GetContentRegionAvail().X;
        foreach (var line in log.Lines) TableLog.DrawLine(line with { Text = log.Resolve(line.Text) }, width);
        if (log.Lines.Count != _seenLog)
        {
            _seenLog = log.Lines.Count;
            ImGui.SetScrollHereY(1f);
        }
        ImGui.EndChild();
    }

    private void Chat(GameApp app, Vector2 size)
    {
        var inputH = ImGui.GetFrameHeight() + S(8);
        if (ImGui.BeginChild("##chat", size - new Vector2(0, inputH), false))
        {
            if (app.Chat.Count == 0) W.Muted($"Messages on {app.ChannelLabel} show here. Say hi to the table!");
            foreach (var line in app.Chat)
            {
                var name = SeatArt.ShortName(line.Sender);
                ImGui.PushStyleColor(ImGuiCol.Text, Pal.Lighten(Pal.FromName(line.Sender), 0.25f));
                ImGui.TextUnformatted(name);
                ImGui.PopStyleColor();
                ImGui.SameLine(0, S(6));
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
                ImGui.TextWrapped(line.Text);
                ImGui.PopTextWrapPos();
            }
            if (app.Chat.Count != _seenChat)
            {
                _seenChat = app.Chat.Count;
                ImGui.SetScrollHereY(1f);
            }
        }
        ImGui.EndChild();
        ImGui.Dummy(new Vector2(1, S(4)));
        var practice = app.Hub.IsPractice;
        ImGui.SetNextItemWidth(size.X);
        ImGui.BeginDisabled(practice || !app.Bridge.LoggedIn);
        if (ImGui.InputText("##chatin", ref _chat, 400, ImGuiInputTextFlags.EnterReturnsTrue) && _chat.Trim().Length > 0)
        {
            app.Bridge.SendChat(_chat.Trim());
            _chat = string.Empty;
            ImGui.SetKeyboardFocusHere(-1);
        }
        ImGui.EndDisabled();
        if (_chat.Length == 0)
        {
            var r = ImGui.GetItemRectMin();
            ImGui.GetWindowDrawList().AddText(r + new Vector2(S(8), S(4)), Pal.U(Pal.Faint), practice ? "Chat is off in practice" : $"Message {app.ChannelLabel}...");
        }
    }

    private void Players(ClientSession? s, GameApp app, Vector2 size)
    {
        if (!ImGui.BeginChild("##players", size, false))
        {
            ImGui.EndChild();
            return;
        }
        var host = app.Hub.Host;
        if (s != null)
        {
            W.Caption($"At the table  ({s.Roster.Entries.Count}/{s.Table.SeatsMax})");
            foreach (var e in s.Roster.Entries)
            {
                var stack = s.Blackjack?.Seat(e.Seat)?.Bankroll ?? s.Poker?.Seat(e.Seat)?.Stack ?? 0;
                PlayerRow(e, stack, host, s);
            }
            if (s.Roster.Entries.Count == 0) W.Muted("Nobody has sat down yet.");
        }

        if (host != null)
        {
            ImGui.Dummy(new Vector2(1, S(8)));
            if (host.RebuyRequests.Count > 0)
            {
                W.Caption("Rebuy requests");
                foreach (var r in host.RebuyRequests.ToList())
                {
                    ImGui.TextUnformatted(SeatArt.ShortName(r.Name));
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - S(120));
                    if (W.Button($"Approve##ap{r.Id}", new Vector2(S(70), S(26)), Tone.Success)) host.ApproveRebuy(r.Id);
                    ImGui.SameLine(0, S(4));
                    if (W.IconButton($"deny{r.Id}", Icon.Close, S(26), "Deny")) host.DenyRebuy(r.Id);
                }
                ImGui.Dummy(new Vector2(1, S(6)));
            }

            W.Caption("Session ledger");
            W.Muted("Chips bought in vs. held now. Use it to settle up after the game.");
            var ledger = host.Ledger();
            if (ImGui.BeginTable("##ledger", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 2.2f);
                ImGui.TableSetupColumn("In", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("Now", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("Net", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableHeadersRow();
                foreach (var e in ledger)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(e.Seated ? Pal.Text : Pal.Muted, SeatArt.ShortName(e.Name));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(Gfx.Chips(e.BuyIns));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(Gfx.Chips(e.CashedOut + e.Current));
                    ImGui.TableNextColumn();
                    ImGui.TextColored(e.Net > 0 ? Pal.Green : e.Net < 0 ? Pal.Red : Pal.Muted, (e.Net > 0 ? "+" : "") + Gfx.Chips(e.Net));
                }
                if (host.Game.Kind == GameKind.Blackjack)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(Pal.Gold, "House");
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    var house = host.Game.HouseNet;
                    ImGui.TextColored(house > 0 ? Pal.Green : house < 0 ? Pal.Red : Pal.Muted, (house > 0 ? "+" : "") + Gfx.Chips(house));
                }
                ImGui.EndTable();
            }
            if (W.Button("Copy ledger##copyledger", new Vector2(ImGui.GetContentRegionAvail().X, S(30)), Tone.Neutral, ledger.Count > 0, icon: Icon.Copy))
                ImGui.SetClipboardText(LedgerText(ledger, host));
        }
        ImGui.EndChild();
    }

    private void PlayerRow(RosterEntry e, long stack, HostSession? host, ClientSession s)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        var h = S(40);
        dl.AddRectFilled(p, p + new Vector2(w, h), Pal.U(Pal.Panel2), S(8));
        var tint = Pal.FromName(e.Name);
        dl.AddCircleFilled(p + new Vector2(S(20), h / 2), S(13), Pal.U(Pal.Darken(tint, 0.35f)), 24);
        Gfx.TextCentered(dl, FontRole.Heading, S(14), p + new Vector2(S(20), h / 2 + S(1)), Pal.U(Pal.White), SeatArt.Initials(e.Name));
        var name = SeatArt.Fit(SeatArt.ShortName(e.Name), w - S(150), S(15));
        dl.AddText(p + new Vector2(S(40), S(4)), Pal.U(e.Seat == s.MySeat && s.IsSeated ? Pal.GoldLight : Pal.Text), name);
        var sub = $"Seat {e.Seat + 1}" + (e.Flags.HasFlag(RosterFlags.SittingOut) ? "  ·  sitting out" : "") + (e.Flags.HasFlag(RosterFlags.Host) ? "  ·  host" : "");
        dl.AddText(UiEnv.Font(FontRole.Body, S(12)), S(12), p + new Vector2(S(40), S(22)), Pal.U(Pal.Muted), sub);
        var st = Gfx.Chips(stack);
        var sw = Gfx.TextSize(FontRole.Body, S(15), st).X;
        var right = host != null ? S(64) : 0;
        dl.AddText(p + new Vector2(w - sw - S(10) - right, S(11)), Pal.U(Pal.GoldLight), st);
        if (host != null)
        {
            ImGui.SetCursorScreenPos(p + new Vector2(w - S(60), S(7)));
            if (W.IconButton($"adj{e.Seat}", Icon.Plus, S(26), "Add or remove chips")) ImGui.OpenPopup($"adjust{e.Seat}");
            ImGui.SameLine(0, S(4));
            if (W.IconButton($"kick{e.Seat}", Icon.Close, S(26), "Remove from table", Tone.Ghost, e.Seat != s.MySeat || !s.IsSeated)) ImGui.OpenPopup($"kick{e.Seat}");
            if (ImGui.BeginPopup($"adjust{e.Seat}"))
            {
                ImGui.TextUnformatted($"Chips for {SeatArt.ShortName(e.Name)}");
                W.Stepper($"amt{e.Seat}", ref _adjust, 1, 100_000_000, 100, S(220));
                if (W.Button($"Give {_adjust:N0}##give{e.Seat}", new Vector2(S(108), S(30)), Tone.Success)) { host.AdjustChips(e.Seat, _adjust); ImGui.CloseCurrentPopup(); }
                ImGui.SameLine(0, S(4));
                if (W.Button($"Take {_adjust:N0}##take{e.Seat}", new Vector2(S(108), S(30)), Tone.Danger)) { host.AdjustChips(e.Seat, -_adjust); ImGui.CloseCurrentPopup(); }
                ImGui.EndPopup();
            }
            if (ImGui.BeginPopup($"kick{e.Seat}"))
            {
                ImGui.TextUnformatted($"Remove {SeatArt.ShortName(e.Name)}?");
                W.Muted("Their chips are cashed out in the ledger.");
                if (W.Button($"Remove##rm{e.Seat}", new Vector2(S(110), S(30)), Tone.Danger)) { host.Kick(e.Seat, false); ImGui.CloseCurrentPopup(); }
                ImGui.SameLine(0, S(4));
                if (W.Button($"Remove and ban##ban{e.Seat}", new Vector2(S(150), S(30)), Tone.Danger)) { host.Kick(e.Seat, true); ImGui.CloseCurrentPopup(); }
                ImGui.EndPopup();
            }
        }
        ImGui.SetCursorScreenPos(p + new Vector2(0, h + S(6)));
    }

    private static string LedgerText(List<LedgerEntry> ledger, HostSession host)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{host.Options.Title} - ledger {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (var e in ledger)
            sb.AppendLine($"{e.Name}: in {e.BuyIns:N0}, now {e.CashedOut + e.Current:N0}, net {(e.Net > 0 ? "+" : "")}{e.Net:N0}");
        if (host.Game.Kind == GameKind.Blackjack) sb.AppendLine($"House: {(host.Game.HouseNet > 0 ? "+" : "")}{host.Game.HouseNet:N0}");
        return sb.ToString();
    }

    private static void Fairness(ClientSession? s, Vector2 size)
    {
        if (!ImGui.BeginChild("##fair", size, false))
        {
            ImGui.EndChild();
            return;
        }
        var fair = s?.Table.Flags.HasFlag(TableFlags.ProvablyFair) == true;
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        Icons.Draw(dl, Icon.Shield, p + new Vector2(S(14), S(16)), S(26), fair ? Pal.Green : Pal.Muted);
        ImGui.SetCursorScreenPos(p + new Vector2(S(36), 0));
        ImGui.BeginGroup();
        ImGui.TextUnformatted(fair ? "Provably fair shuffles" : "Standard shuffles");
        ImGui.PushStyleColor(ImGuiCol.Text, Pal.Muted);
        ImGui.TextWrapped(fair
            ? "Every deck is locked in (committed) before the deal and mixed with your own random seed. After each round the host reveals the seed and your client re-shuffles and checks every card."
            : "The host shuffles with a secure random generator. Ask the host to enable provably fair shuffles to check every round yourself.");
        ImGui.PopStyleColor();
        ImGui.EndGroup();
        ImGui.Dummy(new Vector2(1, S(8)));

        var commit = s?.Blackjack?.Commit ?? s?.Poker?.Commit;
        if (commit != null)
        {
            W.Caption("This round's commitment");
            ImGui.PushStyleColor(ImGuiCol.Text, Pal.GoldLight);
            ImGui.TextUnformatted(Convert.ToHexString(commit)[..24].ToLowerInvariant() + "...");
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(1, S(6)));
        }

        if (s != null && s.Fairness.Count > 0)
        {
            var ok = s.Fairness.Count(f => f.Ok);
            W.Caption($"Checked rounds  ({ok}/{s.Fairness.Count} verified)");
            foreach (var f in Enumerable.Reverse(s.Fairness))
            {
                var row = ImGui.GetCursorScreenPos();
                var (icon, col) = f.Status switch
                {
                    FairStatus.Verified => (Icon.Check, Pal.Green),
                    FairStatus.Failed => (Icon.Close, Pal.Red),
                    _ => (Icon.Info, Pal.Muted),
                };
                Icons.Draw(ImGui.GetWindowDrawList(), icon, row + new Vector2(S(9), S(10)), S(16), col);
                ImGui.SetCursorScreenPos(row + new Vector2(S(24), 0));
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
                ImGui.PushStyleColor(ImGuiCol.Text, f.Status == FairStatus.Failed ? Pal.Red : f.Ok ? Pal.Text : Pal.Muted);
                ImGui.TextWrapped(f.Summary);
                ImGui.PopStyleColor();
                ImGui.PopTextWrapPos();
            }
        }
        else if (fair)
        {
            W.Muted("Results appear here after each round you watch from the start.");
        }
        ImGui.EndChild();
    }

    private void Host(GameApp app, Vector2 size)
    {
        var host = app.Hub.Host;
        if (host == null) return;
        if (!ImGui.BeginChild("##host", size, false))
        {
            ImGui.EndChild();
            return;
        }
        var w = ImGui.GetContentRegionAvail().X;
        W.Caption("Table");
        ImGui.TextUnformatted(host.Options.Title);
        ImGui.PushStyleColor(ImGuiCol.Text, Pal.Muted);
        ImGui.TextUnformatted($"{(host.Game.Kind == GameKind.Blackjack ? "Blackjack" : "No-Limit Hold'em")} on {app.ChannelLabel}");
        ImGui.TextUnformatted($"Table #{host.TableId:X4}  ·  {host.Players.Count}/{host.Game.MaxSeats} seated");
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(1, S(6)));

        var bj = host.Game as BlackjackEngine;
        var pk = host.Game as PokerEngine;
        var startLabel = bj != null
            ? bj.Phase is BjPhase.Betting ? "Deal now" : "Open betting"
            : pk!.Phase == PokerPhase.Showdown ? "Next hand" : "Start hand";
        var canStart = bj != null
            ? bj.Phase is BjPhase.Waiting or BjPhase.Settled or BjPhase.Betting
            : pk!.Phase is PokerPhase.Waiting or PokerPhase.Starting or PokerPhase.Showdown && host.Players.Count >= 2;
        if (W.Button($"{startLabel}##hoststart", new Vector2(w, S(38)), Tone.Primary, canStart && !host.Game.Paused, icon: Icon.Play)) host.StartNow();
        if (W.Button(host.Game.Paused ? "Resume table##pause" : "Pause after this round##pause", new Vector2(w, S(34)), host.Game.Paused ? Tone.Success : Tone.Neutral, icon: host.Game.Paused ? Icon.Play : Icon.Pause))
            host.SetPaused(!host.Game.Paused);
        W.Muted(bj != null
            ? bj.Rules.AutoDeal ? "Auto-deal is on: betting opens by itself after each round." : "Auto-deal is off: open betting yourself each round."
            : pk!.Rules.AutoStart ? "Hands start automatically while two or more players are seated." : "Start each hand yourself.");

        ImGui.Dummy(new Vector2(1, S(10)));
        W.Caption("Linkshell traffic");
        ImGui.PushStyleColor(ImGuiCol.Text, Pal.Muted);
        ImGui.TextUnformatted($"Lines sent: {app.Hub.Outbox.Sent:N0}   Queued: {app.Hub.Outbox.Backlog}");
        ImGui.TextUnformatted($"Pacing: one line every {app.Settings.SendIntervalMs / 1000.0:0.0}s");
        if (app.Hub.CorruptLines > 0) ImGui.TextColored(Pal.Orange, $"{app.Hub.CorruptLines} unreadable lines (chat filter?)");
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(1, S(10)));
        if (!_confirmClose)
        {
            if (W.Button("Close table##close", new Vector2(w, S(34)), Tone.Danger, icon: Icon.Exit)) _confirmClose = true;
        }
        else
        {
            W.Muted("Close the table for everyone? Chips are cashed out in the ledger.");
            if (W.Button("Close it##closeyes", new Vector2(w / 2 - S(4), S(34)), Tone.Danger))
            {
                _confirmClose = false;
                app.Leave();
            }
            ImGui.SameLine(0, S(8));
            if (W.Button("Keep playing##closeno", new Vector2(w / 2 - S(4), S(34)))) _confirmClose = false;
        }
        ImGui.EndChild();
    }
}
