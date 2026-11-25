using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Roulette;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Ui;

/// <summary>The bottom bar: chips to bet with, the moves available right now, and seat controls.</summary>
public sealed class ActionBar
{
    private long _bet;
    private long _lastBet;
    private long _betRound = -1;
    private long _raiseTo;
    private long _raiseKey = -1;
    private bool _confirmFold;
    private bool _preFold;
    private long _preFoldTurn = -1;

    private bool _hints;

    private static float S(float v) => UiEnv.S(v);

    private string? K(string key) => _hints ? key : null;

    public static float Height => S(112);

    public void Reset()
    {
        _bet = 0;
        _betRound = -1;
        _raiseKey = -1;
        _confirmFold = false;
        _preFold = false;
    }

    public void Draw(Vector2 min, Vector2 max, ClientSession s, GameApp app, bool focused)
    {
        _hints = app.Settings.KeyboardShortcuts;
        var dl = ImGui.GetWindowDrawList();
        W.Glass(dl, min, max, S(16));

        var pad = S(16);
        var inner = new Vector2(min.X + pad, min.Y + S(14));
        var innerMax = new Vector2(max.X - pad, max.Y - S(12));

        // Left: the player's chips.
        var leftW = S(176);
        DrawStack(dl, inner, s, app);

        // Right: seat controls.
        var rightW = S(132);
        SeatControls(s, new Vector2(innerMax.X - rightW, inner.Y), rightW);

        var midP = new Vector2(inner.X + leftW, inner.Y);
        var midW = innerMax.X - rightW - S(14) - midP.X;
        ImGui.SetCursorScreenPos(midP);
        if (!s.IsSeated)
        {
            Spectator(s, app, midW);
            return;
        }
        if (s.Blackjack is { } bj) Blackjack(s, bj, app, midP, midW, focused);
        else if (s.Poker is { } pk) Poker(s, pk, app, midP, midW, focused);
        else if (s.Roulette is { } rl) Roulette(s, rl, midP, midW, focused);
    }

    private static void DrawStack(ImDrawListPtr dl, Vector2 p, ClientSession s, GameApp app)
    {
        long? stack = null;
        if (s.IsSeated)
            stack = s.Blackjack?.Seat(s.MySeat)?.Bankroll ?? s.Poker?.Seat(s.MySeat)?.Stack ?? s.Roulette?.Seat(s.MySeat)?.Bankroll;
        var dealer = string.Equals(s.Table.HostId, s.SelfId, StringComparison.OrdinalIgnoreCase);
        Gfx.TextSpaced(dl, FontRole.Body, S(11.5f), p, Pal.U(Pal.Accent, 0.85f), s.IsSeated ? "YOUR CHIPS" : dealer ? "DEALING" : "WATCHING", S(1.2f));
        if (stack is { } v)
        {
            ChipArt.Top(dl, p + new Vector2(S(14), S(40)), S(13), ChipArt.For(Math.Max(1, v)), 1, false);
            dl.AddText(UiEnv.Font(FontRole.Heading, S(32)), S(32), p + new Vector2(S(34), S(22)), Pal.U(Pal.GoldLight), v.ToString("N0"));
            var name = SeatArt.ShortName(s.SelfId);
            dl.AddText(UiEnv.Font(FontRole.Body, S(13)), S(13), p + new Vector2(0, S(62)), Pal.U(Pal.Muted), $"{name}  ·  seat {s.MySeat + 1}");
        }
        else
        {
            var seated = s.Roster.Entries.Count;
            dl.AddText(UiEnv.Font(FontRole.Heading, S(28)), S(28), p + new Vector2(0, S(20)), Pal.U(Pal.Text), $"{seated}/{s.Table.SeatsMax}");
            dl.AddText(UiEnv.Font(FontRole.Body, S(13)), S(13), p + new Vector2(0, S(54)), Pal.U(Pal.Muted), seated == 1 ? "player seated" : "players seated");
        }
    }

    private static void SeatControls(ClientSession s, Vector2 p, float width)
    {
        var bh = S(36);
        if (!s.IsSeated) return;
        var sittingOut = s.Blackjack?.Seat(s.MySeat)?.Status == BjSeatStatus.SittingOut
                         || s.Poker?.Seat(s.MySeat)?.Status == PokerSeatStatus.SittingOut
                         || s.Roulette?.Seat(s.MySeat)?.Status == RouletteSeatStatus.SittingOut;
        ImGui.SetCursorScreenPos(p);
        if (W.Button(sittingOut ? "I'm back##sitin" : "Sit out##sitout", new Vector2(width, bh), sittingOut ? Tone.Success : Tone.Neutral, icon: sittingOut ? Icon.Play : Icon.Pause))
            s.SitOut(!sittingOut);
        var rs = s.Roulette?.Seat(s.MySeat);
        var stack = s.Blackjack?.Seat(s.MySeat)?.Bankroll ?? s.Poker?.Seat(s.MySeat)?.Stack ?? (rs != null ? rs.Bankroll + rs.TotalBet : 0);
        var starting = s.Table.BlackjackRules?.StartingStack ?? s.Table.PokerRules?.StartingStack ?? s.Table.RouletteRules?.StartingStack ?? 0;
        var between = s.Blackjack is { Phase: BjPhase.Betting or BjPhase.Waiting or BjPhase.Settled } || s.Poker is { Phase: PokerPhase.Waiting or PokerPhase.Showdown or PokerPhase.Starting }
                      || s.Roulette is { Phase: RoulettePhase.Waiting or RoulettePhase.Settled } || (s.Roulette is { Phase: RoulettePhase.Betting } && rs is { TotalBet: 0 });
        var pending = s.RebuyPending;
        var canRebuy = stack < starting && between && !pending;
        ImGui.SetCursorScreenPos(p + new Vector2(0, bh + S(8)));
        if (W.Button(pending ? "Asked host...##rebuy" : "Rebuy##rebuy", new Vector2(width, bh), Tone.Neutral, canRebuy, icon: pending ? Icon.Users : Icon.Plus))
            s.RequestRebuy();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(pending ? "Waiting for the host to approve your rebuy."
                : stack >= starting ? "You have at least the starting stack."
                : !between ? "Rebuy between hands."
                : $"Ask the host to top you back up to {starting:N0}.");
        }
    }

    private static void Spectator(ClientSession s, GameApp app, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        string title, sub;
        switch (s.Status)
        {
            case ClientStatus.Joining:
                title = "Taking a seat...";
                sub = "Waiting for the host to answer on the linkshell.";
                break;
            case ClientStatus.Rejected:
                title = "Couldn't sit down";
                sub = s.RejectedFor switch
                {
                    RejectReason.Full => "The table is full. You can keep watching.",
                    RejectReason.Kicked => "The host removed you from this table.",
                    RejectReason.BadVersion => "Your plugin version doesn't match the host's.",
                    _ => "The host didn't answer.",
                };
                break;
            case ClientStatus.Closed:
                title = "Table closed";
                sub = s.ClosedText ?? "The host closed this table.";
                break;
            default:
                title = s.Table.HostId == s.SelfId ? "You're the dealer" : "You're watching";
                sub = s.Table.HostId == s.SelfId
                    ? "Players join from their lobby."
                    : "Click an open seat (or Take a seat) to play. Watching is free.";
                break;
        }
        dl.AddText(UiEnv.Font(FontRole.Heading, S(26)), S(26), p, Pal.U(Pal.Text), title);
        dl.AddText(UiEnv.Font(FontRole.Body, S(14)), S(14), p + new Vector2(0, S(34)), Pal.U(Pal.Muted), sub);
        var canJoin = s.Status is ClientStatus.Spectating or ClientStatus.Left or ClientStatus.Rejected && s.Table.HostId != s.SelfId
                      && s.RejectedFor != RejectReason.Kicked && s.Status != ClientStatus.Closed;
        if (canJoin && s.Table.HostId != s.SelfId)
        {
            ImGui.SetCursorScreenPos(new Vector2(p.X + width - S(170), p.Y + S(10)));
            if (W.Button("Take a seat##join", new Vector2(S(170), S(48)), Tone.Primary, s.Table.SeatsTaken < s.Table.SeatsMax, icon: Icon.Users, fontSize: S(17)))
                s.Join();
        }
        else if (app.Hub.Host is { } host && s.Table.HostId == s.SelfId)
        {
            // The dealer runs the table from here too.
            var (label, canStart, _) = HostControls.Start(host);
            var bw = Math.Min(S(170), (width - S(10)) / 3);
            ImGui.SetCursorScreenPos(new Vector2(p.X + width - bw * 2 - S(10), p.Y + S(8)));
            if (W.Button($"{label}##dockstart", new Vector2(bw, S(52)), Tone.Primary, canStart && !host.Game.Paused, icon: Icon.Play)) host.StartNow();
            ImGui.SameLine(0, S(10));
            if (W.Button(host.Game.Paused ? "Resume##dockpause" : "Pause##dockpause", new Vector2(bw, S(52)), host.Game.Paused ? Tone.Success : Tone.Neutral,
                    icon: host.Game.Paused ? Icon.Play : Icon.Pause))
                host.SetPaused(!host.Game.Paused);
        }
    }

    // ───────────── Blackjack ─────────────

    private void Blackjack(ClientSession s, BlackjackView v, GameApp app, Vector2 p, float width, bool focused)
    {
        var rules = s.Table.BlackjackRules ?? new BlackjackRules();
        var me = v.Seat(s.MySeat);
        if (me == null) return;
        var dl = ImGui.GetWindowDrawList();
        var bh = S(56);

        if (v.Phase == BjPhase.Betting && me.Status != BjSeatStatus.SittingOut)
        {
            if (_betRound != v.Round)
            {
                _betRound = v.Round;
                _bet = Math.Clamp(_lastBet > 0 ? _lastBet : rules.MinBet, rules.MinBet, Math.Max(rules.MinBet, Math.Min(rules.MaxBet, me.Bankroll)));
            }
            // Chip palette.
            var chips = ChipArt.Denoms.Where(d => d.Value <= rules.MaxBet && d.Value <= Math.Max(me.Bankroll, rules.MinBet) && d.Value * 200 >= rules.MinBet).TakeLast(6).ToList();
            var r = S(21);
            var x = p.X + r;
            foreach (var d in chips)
            {
                var c = new Vector2(x, p.Y + S(28));
                ImGui.SetCursorScreenPos(c - new Vector2(r));
                var clicked = ImGui.InvisibleButton($"##chip{d.Value}", new Vector2(r * 2));
                var hovered = ImGui.IsItemHovered();
                if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (clicked) _bet = Math.Min(Math.Min(rules.MaxBet, me.Bankroll), _bet + d.Value);
                if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) _bet = Math.Max(0, _bet - d.Value);
                ChipArt.Top(dl, c - new Vector2(0, hovered ? S(3) : 0), r, d);
                x += r * 2 + S(8);
            }
            dl.AddText(UiEnv.Font(FontRole.Body, S(12)), S(12), new Vector2(p.X, p.Y + S(58)), Pal.U(Pal.Muted), "Click chips to add, right-click to remove");

            var bx = x + S(8);
            ImGui.SetCursorScreenPos(new Vector2(bx, p.Y + S(6)));
            if (W.Button("Clear##bjclear", new Vector2(S(70), S(32)), Tone.Neutral, _bet > 0)) _bet = 0;
            ImGui.SetCursorScreenPos(new Vector2(bx, p.Y + S(42)));
            if (W.Button("x2##bjdouble", new Vector2(S(70), S(32)), Tone.Neutral, _bet > 0 && _bet * 2 <= Math.Min(rules.MaxBet, me.Bankroll))) _bet *= 2;

            var placeW = Math.Max(S(170), p.X + width - (bx + S(80)));
            ImGui.SetCursorScreenPos(new Vector2(p.X + width - placeW, p.Y + S(6)));
            var valid = _bet >= rules.MinBet && _bet <= rules.MaxBet && _bet <= me.Bankroll;
            var placed = me.Bet > 0;
            var label = placed ? (me.Bet == _bet ? $"Bet in: {me.Bet:N0}##bjplace" : $"Change bet to {_bet:N0}##bjplace") : $"Bet {_bet:N0}##bjplace";
            if (W.Button(label, new Vector2(placeW, S(68)), placed && me.Bet == _bet ? Tone.Success : Tone.Gold, valid && !(placed && me.Bet == _bet), K("Enter"),
                    $"Limits {rules.MinBet:N0} - {rules.MaxBet:N0}", font: FontRole.Heading, fontSize: S(24))
                || (focused && valid && ImGui.IsKeyPressed(ImGuiKey.Enter) && !(placed && me.Bet == _bet)))
            {
                _lastBet = _bet;
                s.PlaceBet(_bet);
            }
            return;
        }

        if (v.Phase == BjPhase.Insurance && me.InsuranceState == InsuranceState.Undecided)
        {
            var cost = me.Hands.Count > 0 ? me.Hands[0].Bet / 2 : 0;
            dl.AddText(UiEnv.Font(FontRole.Heading, S(22)), S(22), p, Pal.U(Pal.Text), "Dealer shows an ace");
            dl.AddText(UiEnv.Font(FontRole.Body, S(14)), S(14), p + new Vector2(0, S(30)), Pal.U(Pal.Muted), $"Insurance costs {cost:N0} and pays 2 to 1 if the dealer has blackjack.");
            var bw = S(170);
            ImGui.SetCursorScreenPos(new Vector2(p.X + width - bw * 2 - S(10), p.Y + S(8)));
            if (W.Button($"Insure ({cost:N0})##ins", new Vector2(bw, bh), Tone.Info, true, K("I")) || (focused && ImGui.IsKeyPressed(ImGuiKey.I)))
                s.Act((byte)BjActionKind.TakeInsurance);
            ImGui.SameLine(0, S(10));
            if (W.Button("No thanks##noins", new Vector2(bw, bh), Tone.Neutral, true, K("N")) || (focused && ImGui.IsKeyPressed(ImGuiKey.N)))
                s.Act((byte)BjActionKind.DeclineInsurance);
            return;
        }

        if (v.Phase == BjPhase.PlayerTurns && v.ActiveSeat == s.MySeat)
        {
            var hand = me.Hands[Math.Min(v.ActiveHand, me.Hands.Count - 1)];
            var a = v.Allowed;
            var waiting = s.AwaitingHost;
            var buttons = new (string Label, BjActions Flag, BjActionKind Kind, Tone Tone, string Key, ImGuiKey ImKey, string? Sub)[]
            {
                ("Hit", BjActions.Hit, BjActionKind.Hit, Tone.Success, "H", ImGuiKey.H, null),
                ("Stand", BjActions.Stand, BjActionKind.Stand, Tone.Danger, "S", ImGuiKey.S, null),
                ("Double", BjActions.Double, BjActionKind.Double, Tone.Gold, "D", ImGuiKey.D, $"+{hand.Bet:N0}"),
                ("Split", BjActions.Split, BjActionKind.Split, Tone.Info, "P", ImGuiKey.P, $"+{hand.Bet:N0}"),
                ("Surrender", BjActions.Surrender, BjActionKind.Surrender, Tone.Neutral, "R", ImGuiKey.R, $"get {hand.Bet / 2:N0}"),
            };
            var bw = (width - S(10) * (buttons.Length - 1)) / buttons.Length;
            ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + S(6)));
            foreach (var b in buttons)
            {
                var enabled = a.HasFlag(b.Flag) && !waiting;
                if (W.Button($"{b.Label}##bj{b.Label}", new Vector2(bw, S(72)), b.Tone, enabled, K(b.Key), enabled ? b.Sub : null, font: FontRole.Heading, fontSize: S(24))
                    || (enabled && focused && ImGui.IsKeyPressed(b.ImKey)))
                    s.Act((byte)b.Kind, 0, v.ActiveHand);
                ImGui.SameLine(0, S(10));
            }
            return;
        }

        // Nothing to do: say what's happening.
        var status = v.Phase switch
        {
            BjPhase.Betting => ("Sitting out", "Press \"I'm back\" to play the next round."),
            BjPhase.Waiting => ("Waiting for the next round", "The dealer opens betting when players are ready."),
            BjPhase.Insurance => ("Insurance", "Waiting for other players to decide."),
            BjPhase.PlayerTurns => ($"{SeatArt.ShortName(s.NameAt(v.ActiveSeat))} is playing", me.Hands.Count > 0 ? $"Your hand: {BjMath.Describe(me.Hands[0].Cards)}" : "You're not in this round."),
            BjPhase.DealerTurn => ("Dealer's turn", ""),
            _ => me.RoundNet switch
            {
                > 0 => ($"You won {me.RoundNet:N0}", "Next round starting shortly."),
                < 0 => ($"You lost {-me.RoundNet:N0}", "Next round starting shortly."),
                _ => (me.Hands.Count > 0 ? "Push" : "Round over", "Next round starting shortly."),
            },
        };
        dl.AddText(UiEnv.Font(FontRole.Heading, S(26)), S(26), p + new Vector2(0, S(4)), Pal.U(Pal.Text), status.Item1);
        dl.AddText(UiEnv.Font(FontRole.Body, S(14)), S(14), p + new Vector2(0, S(40)), Pal.U(Pal.Muted), status.Item2);
    }

    // ───────────── Roulette ─────────────

    /// <summary>The player's bets in progress, owned by the roulette scene.</summary>
    public RouletteDraft? RouletteDraft { get; set; }

    private void Roulette(ClientSession s, RouletteView v, Vector2 p, float width, bool focused)
    {
        var rules = s.Table.RouletteRules ?? new RouletteRules();
        var me = v.Seat(s.MySeat);
        var d = RouletteDraft;
        if (me == null || d == null) return;
        var dl = ImGui.GetWindowDrawList();

        if (v.Phase == RoulettePhase.Betting && me.Status != RouletteSeatStatus.SittingOut)
        {
            // Chip to bet with.
            var chips = new List<ChipArt.Denom> { ChipArt.For(rules.MinBet) with { Value = rules.MinBet, Label = Gfx.Chips(rules.MinBet) } };
            chips.AddRange(ChipArt.Denoms.Where(c => c.Value > rules.MinBet && c.Value <= rules.MaxBet).Take(5));
            if (!chips.Any(c => c.Value == d.Chip)) d.Chip = chips[0].Value;
            var r = S(19);
            var x = p.X + r;
            foreach (var c in chips)
            {
                var cc = new Vector2(x, p.Y + S(27));
                ImGui.SetCursorScreenPos(cc - new Vector2(r));
                if (ImGui.InvisibleButton($"##rchip{c.Value}", new Vector2(r * 2))) d.Chip = c.Value;
                var hovered = ImGui.IsItemHovered();
                if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                var sel = d.Chip == c.Value;
                var lift = sel ? S(5) : hovered ? S(2) : 0;
                if (sel) Gfx.GlowCircle(dl, cc - new Vector2(0, lift), r, Pal.Accent, S(9), 1f);
                ChipArt.Top(dl, cc - new Vector2(0, lift), r, c);
                if (sel) dl.AddCircle(cc - new Vector2(0, lift), r + S(2.5f), Pal.U(Pal.Accent), 32, S(2));
                x += r * 2 + S(7);
            }
            dl.AddText(UiEnv.Font(FontRole.Body, S(12)), S(12), new Vector2(p.X, p.Y + S(58)), Pal.U(Pal.Muted), SeatArt.Fit("Pick a chip, then click the board", x - p.X - S(4), S(12)));

            var bx = x + S(4);
            var budget = Math.Min(rules.TableMax, me.Bankroll + me.Bets.Sum(b => b.Amount));
            ImGui.SetCursorScreenPos(new Vector2(bx, p.Y + S(2)));
            if (W.Button("Clear##rclear", new Vector2(S(86), S(30)), Tone.Neutral, d.Total > 0)) d.Bets.Clear();
            ImGui.SetCursorScreenPos(new Vector2(bx, p.Y + S(38)));
            var repeat = d.Last.Where(b => b.Amount >= rules.MinBet).ToList();
            if (W.Button("Repeat##rrepeat", new Vector2(S(86), S(30)), Tone.Neutral, repeat.Count > 0 && repeat.Sum(b => b.Amount) <= budget))
            {
                d.Bets.Clear();
                foreach (var (i, a) in repeat) d.Bets[i] = a;
            }
            if (ImGui.IsItemHovered() && repeat.Count > 0) ImGui.SetTooltip($"Put last spin's bets back: {repeat.Sum(b => b.Amount):N0}");

            var placed = me.Bets;
            var placedTotal = placed.Sum(b => b.Amount);
            var differs = d.Differs(placed);
            string label;
            Tone tone;
            bool enabled;
            if (!differs && placed.Count > 0) (label, tone, enabled) = ($"Bets in: {placedTotal:N0}", Tone.Success, true);
            else if (d.Total == 0 && placed.Count > 0) (label, tone, enabled) = ("Take bets back", Tone.Neutral, true);
            else (label, tone, enabled) = (placed.Count > 0 ? $"Update: {d.Total:N0}" : $"Bet {d.Total:N0}", Tone.Gold, d.Total > 0 && d.Total <= budget);
            var placeW = Math.Max(S(170), p.X + width - (bx + S(96)));
            ImGui.SetCursorScreenPos(new Vector2(p.X + width - placeW, p.Y + S(2)));
            var spots = d.Bets.Count(b => b.Value > 0);
            if (W.Button($"{label}##rplace", new Vector2(placeW, S(70)), tone, enabled && !s.AwaitingHost, K("Enter"),
                    $"{spots} of {RouletteBets.MaxSpots} spots  ·  table max {rules.TableMax:N0}", font: FontRole.Heading, fontSize: S(24))
                || (focused && enabled && !s.AwaitingHost && ImGui.IsKeyPressed(ImGuiKey.Enter)))
            {
                if (differs) s.PlaceRouletteBets(d.List());
            }
            return;
        }

        var onTable = me.Bets.Sum(b => b.Amount);
        var status = v.Phase switch
        {
            RoulettePhase.Betting => ("Sitting out", "Press \"I'm back\" to play the next spin."),
            RoulettePhase.Waiting => ("Waiting for the next spin", "Betting opens when the dealer is ready."),
            RoulettePhase.Spinning => ("No more bets", onTable > 0 ? $"You have {onTable:N0} on {me.Bets.Count} spot{(me.Bets.Count == 1 ? "" : "s")}." : "You sat this spin out."),
            _ => me.RoundNet switch
            {
                > 0 => ($"You won {me.RoundNet:N0}", $"{v.Result} {(v.Result == 0 ? "green" : RouletteArt.IsRed(v.Result) ? "red" : "black")}. Next spin shortly."),
                < 0 => ($"You lost {-me.RoundNet:N0}", $"{v.Result} {(v.Result == 0 ? "green" : RouletteArt.IsRed(v.Result) ? "red" : "black")}. Next spin shortly."),
                _ => (v.Result <= 36 ? $"{v.Result} {(v.Result == 0 ? "green" : RouletteArt.IsRed(v.Result) ? "red" : "black")}" : "Spin over", "Next spin shortly."),
            },
        };
        dl.AddText(UiEnv.Font(FontRole.Heading, S(26)), S(26), p + new Vector2(0, S(4)), Pal.U(Pal.Text), status.Item1);
        dl.AddText(UiEnv.Font(FontRole.Body, S(14)), S(14), p + new Vector2(0, S(40)), Pal.U(Pal.Muted), status.Item2);
    }

    // ───────────── Poker ─────────────

    private void Poker(ClientSession s, PokerView v, GameApp app, Vector2 p, float width, bool focused)
    {
        var me = v.Seat(s.MySeat);
        if (me == null) return;
        var dl = ImGui.GetWindowDrawList();
        var myTurn = v.BettingRound && v.ActiveSeat == s.MySeat && !s.AwaitingHost;
        var a = v.Allowed;
        var toCall = Math.Max(0, v.CurrentBet - me.Bet);
        var maxTo = me.Bet + me.Stack;

        // Pre-action: check/fold as soon as it's our turn.
        if (myTurn && _preFold && _preFoldTurn != v.TurnId)
        {
            _preFoldTurn = v.TurnId;
            _preFold = false;
            s.Act((byte)(a.HasFlag(PokerActions.Check) ? PokerActionKind.Check : PokerActionKind.Fold));
            return;
        }

        if (!myTurn)
        {
            _confirmFold = false;
            string title, sub;
            if (me.InHand && v.BettingRound)
            {
                title = $"{SeatArt.ShortName(s.NameAt(v.ActiveSeat))} to act";
                sub = s.HoleHand == v.Hand && s.Hole.Length == 2 ? $"You hold {PokerScene.Preflop(s.Hole)}" : "";
                ImGui.SetCursorScreenPos(new Vector2(p.X + width - S(190), p.Y + S(14)));
                W.Toggle("Check / fold##pre", ref _preFold, null);
            }
            else
            {
                (title, sub) = v.Phase switch
                {
                    PokerPhase.Showdown => me.Won > 0 ? ($"You won {me.Won:N0}", "Next hand starting shortly.") : ("Showdown", "Next hand starting shortly."),
                    PokerPhase.Starting => ("Shuffling", "Adding your seed to the shuffle..."),
                    PokerPhase.Waiting => (v.Seats.Count < 2 ? "Waiting for players" : "Next hand soon", "Hands start when two or more players are seated."),
                    _ => me.Status switch
                    {
                        PokerSeatStatus.Folded => ("Folded", "Waiting for the next hand."),
                        PokerSeatStatus.SittingOut => ("Sitting out", "Press \"I'm back\" to be dealt in."),
                        PokerSeatStatus.AllIn => ("All-in", "Good luck!"),
                        _ => ("Next hand", "You'll be dealt in next hand."),
                    },
                };
            }
            // Once the hand is over, anyone still holding unseen cards may turn them over.
            if (s.CanShowCards)
            {
                sub = me.Status == PokerSeatStatus.Folded ? "Show the table what you folded?" : "Show the table what you won with?";
                ImGui.SetCursorScreenPos(new Vector2(p.X + width - S(200), p.Y + S(6)));
                if (W.Button("Show cards##show", new Vector2(S(200), S(60)), Tone.Primary, !s.AwaitingHost, K("S"), PokerScene.Preflop(s.Hole), font: FontRole.Heading, fontSize: S(22))
                    || (focused && !s.AwaitingHost && ImGui.IsKeyPressed(ImGuiKey.S)))
                    s.ShowCards();
            }
            else if (v.Phase == PokerPhase.Showdown && me.Shown.Count == 2 && me.Status == PokerSeatStatus.Folded)
            {
                sub = "You showed your cards.";
            }
            dl.AddText(UiEnv.Font(FontRole.Heading, S(26)), S(26), p + new Vector2(0, S(4)), Pal.U(Pal.Text), title);
            dl.AddText(UiEnv.Font(FontRole.Body, S(14)), S(14), p + new Vector2(0, S(40)), Pal.U(Pal.Muted), sub);
            return;
        }

        // Raise sizing.
        var key = v.Hand * 1000 + v.TurnId;
        var minTo = Math.Min(v.MinRaiseTo, maxTo);
        if (_raiseKey != key)
        {
            _raiseKey = key;
            _raiseTo = minTo;
        }
        _raiseTo = Math.Clamp(_raiseTo, minTo, maxTo);
        var potAfterCall = v.PotTotal + v.Seats.Sum(x => x.Bet) + toCall;
        long PotTo(double f) => Math.Clamp(v.CurrentBet + (long)(potAfterCall * f), minTo, maxTo);

        var canRaise = (a & (PokerActions.Bet | PokerActions.Raise)) != 0 && maxTo > v.CurrentBet;
        // Leave room for the sizing presets and slider; the three buttons share what's left.
        var sizing = canRaise ? Math.Clamp(width * 0.36f, S(190), S(300)) : 0;
        var btnW = Math.Clamp((width - sizing - S(36)) / 2.8f, S(96), S(150));
        var y = p.Y + S(2);

        if (_confirmFold)
        {
            dl.AddText(UiEnv.Font(FontRole.Heading, S(22)), S(22), p, Pal.U(Pal.Text), "Fold when checking is free?");
            ImGui.SetCursorScreenPos(new Vector2(p.X + width - btnW * 2 - S(10), y + S(6)));
            if (W.Button("Check##cf", new Vector2(btnW, S(60)), Tone.Success, true, K("C"))) { s.Act((byte)PokerActionKind.Check); _confirmFold = false; }
            ImGui.SameLine(0, S(10));
            if (W.Button("Fold anyway##ff", new Vector2(btnW, S(60)), Tone.Danger, true)) { s.Act((byte)PokerActionKind.Fold); _confirmFold = false; }
            return;
        }

        // Main buttons.
        ImGui.SetCursorScreenPos(new Vector2(p.X, y));
        if (W.Button("Fold##fold", new Vector2(btnW * 0.8f, S(76)), Tone.Danger, a.HasFlag(PokerActions.Fold), K("F"), font: FontRole.Heading, fontSize: S(24))
            || (focused && ImGui.IsKeyPressed(ImGuiKey.F)))
        {
            if (a.HasFlag(PokerActions.Check) && app.Settings.ConfirmFoldWhenCheckIsFree) _confirmFold = true;
            else s.Act((byte)PokerActionKind.Fold);
        }
        ImGui.SameLine(0, S(10));
        var callLabel = a.HasFlag(PokerActions.Check) ? "Check##call" : toCall >= me.Stack ? "Call all-in##call" : "Call##call";
        if (W.Button(callLabel, new Vector2(btnW, S(76)), Tone.Success, (a & (PokerActions.Check | PokerActions.Call)) != 0, K("C"),
                a.HasFlag(PokerActions.Call) ? Math.Min(toCall, me.Stack).ToString("N0") : null, font: FontRole.Heading, fontSize: S(24))
            || (focused && ImGui.IsKeyPressed(ImGuiKey.C)))
            s.Act((byte)(a.HasFlag(PokerActions.Check) ? PokerActionKind.Check : a.HasFlag(PokerActions.Call) ? PokerActionKind.Call : PokerActionKind.AllIn));
        ImGui.SameLine(0, S(10));

        var raiseX = ImGui.GetCursorScreenPos().X;
        var allIn = _raiseTo >= maxTo;
        var raiseLabel = allIn ? "All-in##raise" : v.CurrentBet == 0 ? "Bet##raise" : "Raise to##raise";
        if (W.Button(raiseLabel, new Vector2(btnW, S(76)), Tone.Gold, canRaise || a.HasFlag(PokerActions.AllIn), K("R"), _raiseTo.ToString("N0"), font: FontRole.Heading, fontSize: S(24))
            || (focused && ImGui.IsKeyPressed(ImGuiKey.R) && canRaise))
            s.Act((byte)(allIn ? PokerActionKind.AllIn : PokerActionKind.BetOrRaise), _raiseTo);

        // Sizing controls.
        var sx = raiseX + btnW + S(16);
        var sw = p.X + width - sx;
        if (sw < S(150) || !canRaise) return;
        var presets = new (string, long)[] { ("Min", minTo), ("½", PotTo(0.5)), ("¾", PotTo(0.75)), ("Pot", PotTo(1)), ("Max", maxTo) };
        var pw = (sw - S(6) * (presets.Length - 1)) / presets.Length;
        ImGui.SetCursorScreenPos(new Vector2(sx, y));
        foreach (var (label, amount) in presets)
        {
            if (W.Button($"{label}##preset{label}", new Vector2(pw, S(30)), _raiseTo == amount ? Tone.Primary : Tone.Neutral)) _raiseTo = amount;
            ImGui.SameLine(0, S(6));
        }
        ImGui.SetCursorScreenPos(new Vector2(sx, y + S(40)));
        W.SliderL("##raiseslider", ref _raiseTo, minTo, maxTo, x => x.ToString("N0"), sw, Math.Max(1, v.SmallBlind));
        if (_raiseTo > maxTo - Math.Max(1, v.SmallBlind) / 2) _raiseTo = Math.Min(_raiseTo, maxTo);
    }
}
