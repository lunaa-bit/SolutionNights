using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
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
        Gfx.RoundedGradient(dl, min, max, 0, Pal.Hex(0x161C24), Pal.Hex(0x0E1217));
        dl.AddRectFilledMultiColor(min, new Vector2((min.X + max.X) / 2, min.Y + S(1.5f)), Pal.U(Pal.Gold, 0), Pal.U(Pal.Gold, 0.8f), Pal.U(Pal.Gold, 0.8f), Pal.U(Pal.Gold, 0));
        dl.AddRectFilledMultiColor(new Vector2((min.X + max.X) / 2, min.Y), new Vector2(max.X, min.Y + S(1.5f)), Pal.U(Pal.Gold, 0.8f), Pal.U(Pal.Gold, 0), Pal.U(Pal.Gold, 0), Pal.U(Pal.Gold, 0.8f));

        var pad = S(16);
        var inner = new Vector2(min.X + pad, min.Y + S(14));
        var innerMax = new Vector2(max.X - pad, max.Y - S(12));

        // Left: the player's chips.
        var leftW = S(190);
        DrawStack(dl, inner, s, app);

        // Right: seat controls.
        var rightW = S(132);
        SeatControls(s, new Vector2(innerMax.X - rightW, inner.Y), rightW);

        var mid = new Vector2(inner.X + leftW, inner.Y);
        var midW = innerMax.X - rightW - S(14) - mid.X;
        ImGui.SetCursorScreenPos(mid);
        if (!s.IsSeated)
        {
            Spectator(s, app, midW);
            return;
        }
        if (s.Blackjack is { } bj) Blackjack(s, bj, app, mid, midW, focused);
        else if (s.Poker is { } pk) Poker(s, pk, app, mid, midW, focused);
    }

    private static void DrawStack(ImDrawListPtr dl, Vector2 p, ClientSession s, GameApp app)
    {
        long? stack = null;
        if (s.IsSeated)
            stack = s.Blackjack?.Seat(s.MySeat)?.Bankroll ?? s.Poker?.Seat(s.MySeat)?.Stack;
        var dealer = string.Equals(s.Table.HostId, s.SelfId, StringComparison.OrdinalIgnoreCase);
        dl.AddText(UiEnv.Font(FontRole.Body, S(12)), S(12), p, Pal.U(Pal.Muted), s.IsSeated ? "YOUR CHIPS" : dealer ? "DEALING" : "WATCHING");
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
                         || s.Poker?.Seat(s.MySeat)?.Status == PokerSeatStatus.SittingOut;
        ImGui.SetCursorScreenPos(p);
        if (W.Button(sittingOut ? "I'm back##sitin" : "Sit out##sitout", new Vector2(width, bh), sittingOut ? Tone.Success : Tone.Neutral, icon: sittingOut ? Icon.Play : Icon.Pause))
            s.SitOut(!sittingOut);
        var stack = s.Blackjack?.Seat(s.MySeat)?.Bankroll ?? s.Poker?.Seat(s.MySeat)?.Stack ?? 0;
        var starting = s.Table.BlackjackRules?.StartingStack ?? s.Table.PokerRules?.StartingStack ?? 0;
        var between = s.Blackjack is { Phase: BjPhase.Betting or BjPhase.Waiting or BjPhase.Settled } || s.Poker is { Phase: PokerPhase.Waiting or PokerPhase.Showdown or PokerPhase.Starting };
        var canRebuy = stack < starting && between;
        ImGui.SetCursorScreenPos(p + new Vector2(0, bh + S(8)));
        if (W.Button("Rebuy##rebuy", new Vector2(width, bh), Tone.Neutral, canRebuy, icon: Icon.Plus))
            s.RequestRebuy();
        if (ImGui.IsItemHovered() && !canRebuy) ImGui.SetTooltip(stack >= starting ? "You have at least the starting stack." : "Rebuy between hands.");
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
                    ? "Run the table from the Host tab. Players join from their lobby."
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
            if (W.Button(label, new Vector2(placeW, S(68)), placed && me.Bet == _bet ? Tone.Success : Tone.Primary, valid && !(placed && me.Bet == _bet), K("Enter"),
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
                ("Double", BjActions.Double, BjActionKind.Double, Tone.Primary, "D", ImGuiKey.D, $"+{hand.Bet:N0}"),
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
        var btnW = S(150);
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
        if (W.Button(raiseLabel, new Vector2(btnW, S(76)), Tone.Primary, canRaise || a.HasFlag(PokerActions.AllIn), K("R"), _raiseTo.ToString("N0"), font: FontRole.Heading, fontSize: S(24))
            || (focused && ImGui.IsKeyPressed(ImGuiKey.R) && canRaise))
            s.Act((byte)(allIn ? PokerActionKind.AllIn : PokerActionKind.BetOrRaise), _raiseTo);

        // Sizing controls.
        var sx = raiseX + btnW + S(16);
        var sw = p.X + width - sx;
        if (sw < S(160) || !canRaise) return;
        var presets = new (string, long)[] { ("Min", minTo), ("½", PotTo(0.5)), ("¾", PotTo(0.75)), ("Pot", PotTo(1)), ("Max", maxTo) };
        var pw = (sw - S(6) * (presets.Length - 1)) / presets.Length;
        ImGui.SetCursorScreenPos(new Vector2(sx, y));
        foreach (var (label, amount) in presets)
        {
            if (W.Button($"{label}##preset{label}", new Vector2(pw, S(30)), _raiseTo == amount ? Tone.Info : Tone.Neutral)) _raiseTo = amount;
            ImGui.SameLine(0, S(6));
        }
        ImGui.SetCursorScreenPos(new Vector2(sx, y + S(40)));
        W.SliderL("##raiseslider", ref _raiseTo, minTo, maxTo, x => x.ToString("N0"), sw, Math.Max(1, v.SmallBlind));
        if (_raiseTo > maxTo - Math.Max(1, v.SmallBlind) / 2) _raiseTo = Math.Min(_raiseTo, maxTo);
    }
}
