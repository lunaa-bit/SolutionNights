using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Blackjack;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Ui;

/// <summary>Draws a blackjack table from the latest snapshot, animating every change.</summary>
public sealed class BlackjackScene
{
    private readonly TableAnimator _a = new();
    private readonly Dictionary<int, double> _betShowAt = new();
    private readonly Dictionary<string, int> _occurrence = new();
    private BlackjackView? _prev;
    private FeltStyle _felt;
    private Vector2 _min;
    private Vector2 _max;
    private long _resultsRound = -1;
    private double _resultsAt = double.MaxValue;

    public TableAnimator Anim => _a;

    private static float S(float v) => UiEnv.S(v);

    public void Reset()
    {
        _a.Reset();
        _prev = null;
        _resultsRound = -1;
        _resultsAt = double.MaxValue;
        _betShowAt.Clear();
    }

    public void Draw(ImDrawListPtr dl, Vector2 min, Vector2 max, ClientSession s, GameApp app, double now)
    {
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var rules = s.Table.BlackjackRules ?? new BlackjackRules();
        var v = s.Blackjack;
        _a.Speed = app.Settings.AnimationSpeed;
        _felt = app.Settings.Felt;
        _min = min;
        _max = max;

        var top = new Vector2((min.X + max.X) / 2, min.Y + h * 0.08f);
        var radii = new Vector2(Math.Min(w * 0.47f, h * 1.2f), h * 0.79f);
        var ch = Math.Clamp(Math.Min(h * 0.155f, w * 0.085f), S(58), S(150));
        var cw = ch * CardArt.Aspect;
        var shoe = top + new Vector2(radii.X * 0.62f, radii.Y * 0.13f);
        var discard = top + new Vector2(-radii.X * 0.62f, radii.Y * 0.13f);
        var tray = top + new Vector2(0, radii.Y * 0.06f);
        _a.Discard = discard;
        _a.BannerY = 0.37f;

        TableArt.BlackjackTable(dl, top, radii, app.Settings.Felt,
            $"BLACKJACK PAYS {rules.PayoutText.ToUpperInvariant()}",
            rules.DealerHitsSoft17 ? "Dealer hits soft 17" : "Dealer must stand on all 17s",
            rules.Insurance ? "INSURANCE PAYS 2 TO 1" : "NO INSURANCE", now);
        TableArt.ChipTray(dl, tray, new Vector2(radii.X * 0.4f, radii.Y * 0.075f));
        TableArt.Shoe(dl, shoe, ch);
        TableArt.Discard(dl, discard, ch);

        _a.BeginFrame();
        _a.DrawFeltLayer(dl, now);

        if (v == null)
        {
            Gfx.TextGlow(dl, FontRole.Heading, S(28), top + new Vector2(0, radii.Y * 0.5f), Pal.Accent, "CONNECTING TO THE TABLE...", 0.8f, 0.8f);
            _a.EndFrame(now);
            return;
        }

        Diff(v, s, now, top, radii, ch, tray);

        // Seats.
        var n = rules.MaxSeats;
        for (var i = 0; i < n; i++)
        {
            var theta = Theta(i, n);
            DrawSeat(dl, s, v, rules, i, theta, top, radii, ch, cw, shoe, tray, now, app.Clock.Now);
        }

        // Dealer.
        DrawDealer(dl, v, top, radii, ch, cw, shoe, now);

        // Centre messages.
        var msgPos = top + new Vector2(0, radii.Y * (v.Dealer.Count > 0 ? 0.62f : 0.27f));
        if (v.Phase == BjPhase.Betting)
        {
            var left = s.SecondsLeft(app.Clock.Now);
            Gfx.TextGlow(dl, FontRole.Heading, S(30), msgPos, Pal.Gold, "PLACE YOUR BETS", 1f, 0.95f);
            if (!double.IsNaN(left))
                Gfx.TextCentered(dl, FontRole.Body, S(16), msgPos + new Vector2(0, S(26)), Pal.U(Pal.Text, 0.75f), $"Dealing in {Math.Ceiling(left):0}s");
            else
                Gfx.TextCentered(dl, FontRole.Body, S(16), msgPos + new Vector2(0, S(26)), Pal.U(Pal.Text, 0.6f), "The deal starts after the first bet");
        }
        else if (v.Phase == BjPhase.Waiting)
        {
            Gfx.TextGlow(dl, FontRole.Heading, S(26), msgPos, TableArt.Felt(app.Settings.Felt).Neon, s.Table.Flags.HasFlag(Core.Protocol.TableFlags.Paused) ? "TABLE PAUSED" : "WAITING FOR PLAYERS", 0.7f, 0.75f);
        }
        else if (v.Phase == BjPhase.Insurance)
        {
            Gfx.TextGlow(dl, FontRole.Heading, S(26), msgPos, Pal.Gold, "INSURANCE?", 1f, 0.95f);
        }

        _a.EndFrame(now);
        _a.DrawOverlay(dl, min, max, now);
        _prev = v;
    }

    /// <summary>Seat angle on the table's ellipse: seat 1 ("first base") is on the dealer's left, i.e. screen right.</summary>
    private static float Theta(int i, int n) => n == 1 ? MathF.PI / 2 : MathF.PI * (0.12f + 0.76f * i / (n - 1));

    private Vector2 OnEllipse(Vector2 top, Vector2 radii, float f, float theta) =>
        top + new Vector2(MathF.Cos(theta) * radii.X * f, MathF.Sin(theta) * radii.Y * f);

    private (Vector2 Circle, Vector2 Plate, Vector2 Cards, float CircleR) SeatGeometry(Vector2 top, Vector2 radii, float theta, float ch)
    {
        var circle = OnEllipse(top, radii, 0.79f, theta);
        var plate = OnEllipse(top, radii, 0.99f, theta) + new Vector2(0, S(8));
        var half = SeatArt.Size.X / 2 + S(18);
        plate.X = Math.Clamp(plate.X, _min.X + half, _max.X - half);
        var toward = Vector2.Normalize(top + new Vector2(0, radii.Y * 0.3f) - circle);
        var circleR = ch * 0.4f;
        var cards = circle + toward * (circleR + ch * 0.72f);
        return (circle, plate, cards, circleR);
    }

    private void DrawSeat(ImDrawListPtr dl, ClientSession s, BlackjackView v, BlackjackRules rules, int i, float theta,
        Vector2 top, Vector2 radii, float ch, float cw, Vector2 shoe, Vector2 tray, double now, DateTime clockNow)
    {
        var (circle, plate, cardsBase, circleR) = SeatGeometry(top, radii, theta, ch);
        var sv = v.Seat(i);
        var isMe = s.IsSeated && s.MySeat == i;
        var (_, _, print, neon) = TableArt.Felt(_felt);

        // Betting spot: a hexagon ring, lit for the local player.
        Span<Vector2> hex = stackalloc Vector2[6];
        Gfx.Hexagon(hex, circle, circleR, 0);
        Gfx.FillConvex(dl, hex, Pal.U(Pal.Black, 0.16f));
        if (isMe) Gfx.NeonOutline(dl, hex, Pal.A(Pal.Gold, 0.85f), S(1.8f), 0.8f);
        else Gfx.Outline(dl, hex, Pal.U(neon, 0.4f), S(1.4f));
        Gfx.Hexagon(hex, circle, circleR * 0.8f, 0);
        Gfx.Outline(dl, hex, Pal.U(print, 0.1f), S(1));

        if (sv == null)
        {
            var canSit = s.Status is ClientStatus.Spectating or ClientStatus.Left && !IsHost(s) && !s.Table.Flags.HasFlag(Core.Protocol.TableFlags.Paused) && s.Table.SeatsTaken < s.Table.SeatsMax;
            if (SeatArt.Empty(dl, plate, canSit ? "Sit here" : "Open seat", canSit, $"##sit{i}") && canSit) s.Join(i);
            return;
        }

        var name = s.NameAt(i);
        var hands = sv.Hands;
        var multi = hands.Count > 1;
        var hch = multi ? ch * 0.8f : ch;
        var hcw = hch * CardArt.Aspect;
        var spacing = hcw * 1.75f;
        var resultsShown = v.Phase == BjPhase.Settled && now >= _resultsAt;
        var active = v.Phase == BjPhase.PlayerTurns && v.ActiveSeat == i;

        // Chips in the circle.
        if (v.Phase == BjPhase.Betting)
        {
            if (sv.Bet > 0 && now >= _betShowAt.GetValueOrDefault(i))
                ChipArt.Stack(dl, circle + new Vector2(0, circleR * 0.35f), sv.Bet, circleR * 0.42f);
        }
        else
        {
            for (var k = 0; k < hands.Count; k++)
            {
                var hd = hands[k];
                var amount = resultsShown && now >= _resultsAt + 0.6 ? hd.Payout : hd.Bet;
                if (resultsShown && hd.Payout == 0) amount = 0;
                var hx = multi ? (k - (hands.Count - 1) / 2f) * Math.Min(spacing, circleR * 1.2f) : 0;
                if (amount > 0) ChipArt.Stack(dl, circle + new Vector2(hx, circleR * 0.35f), amount, circleR * (multi ? 0.34f : 0.42f));
            }
            if (sv.Insurance > 0)
                ChipArt.Stack(dl, circle + Vector2.Normalize(top - circle) * circleR * 2.1f, sv.Insurance, circleR * 0.3f, 1f, 2);
        }

        // Cards.
        _occurrence.Clear();
        for (var k = 0; k < hands.Count; k++)
        {
            var hd = hands[k];
            var hx = (k - (hands.Count - 1) / 2f) * spacing;
            var basePos = cardsBase + new Vector2(hx, 0);
            var handActive = active && v.ActiveHand == k;
            var lost = resultsShown && hd.Outcome is BjOutcome.Lose or BjOutcome.Surrender;
            for (var j = 0; j < hd.Cards.Count; j++)
            {
                var card = hd.Cards[j];
                var key = $"s{i}:{card.Code}";
                var occ = _occurrence.GetValueOrDefault(key);
                _occurrence[key] = occ + 1;
                var target = basePos + new Vector2(j * hcw * 0.3f, -j * hch * 0.14f);
                var pose = _a.Card($"{key}:{occ}", card, target, hch, shoe, now);
                if (!pose.Visible) continue;
                CardArt.Draw(dl, pose.Pos, hch, card, pose.Flip, 1f, handActive && j == hd.Cards.Count - 1 && pose.Arrived, lost);
            }

            // Total / outcome badge under the hand.
            var badgePos = basePos + new Vector2(hcw * 0.15f * Math.Max(0, hd.Cards.Count - 1), hch * 0.62f);
            if (resultsShown) OutcomeBadge(dl, badgePos, hd, now);
            else if (hd.Cards.Count >= 2) TotalBadge(dl, badgePos, hd, handActive);
        }

        // Plate.
        var frac = TimerFraction(s, rules, v, clockNow);
        var flags = s.Roster.BySeat(i)?.Flags ?? Core.Protocol.RosterFlags.None;
        var between = v.Phase is BjPhase.Betting or BjPhase.Waiting or BjPhase.Settled;
        var badge = sv.Status switch
        {
            _ when flags.HasFlag(Core.Protocol.RosterFlags.RebuyPending) && between => ("REBUY ASKED", Pal.Magenta),
            _ when sv.Bankroll < rules.MinBet && between && sv.Bet == 0 => ("OUT OF CHIPS", Pal.Red),
            BjSeatStatus.SittingOut => ("SITTING OUT", Pal.Muted),
            BjSeatStatus.BetPlaced when v.Phase == BjPhase.Betting => ("BET IN", Pal.Green),
            _ when v.Phase == BjPhase.Insurance && sv.InsuranceState == InsuranceState.Taken => ("INSURED", Pal.Blue),
            _ => ((string?)null, Pal.Muted),
        };
        var insuranceTurn = v.Phase == BjPhase.Insurance && sv.InsuranceState == InsuranceState.Undecided;
        var entry = s.Roster.BySeat(i);
        SeatArt.Plate(dl, plate, new SeatInfo(name, _a.Count($"bank{i}", sv.Bankroll, now), isMe, active || insuranceTurn, frac,
            badge.Item1, badge.Item2, sv.Status == BjSeatStatus.SittingOut, entry?.Flags.HasFlag(Core.Protocol.RosterFlags.Host) == true, false), now);
    }

    private static bool IsHost(ClientSession s) => string.Equals(s.Table.HostId, s.SelfId, StringComparison.OrdinalIgnoreCase);

    private static float TimerFraction(ClientSession s, BlackjackRules rules, BlackjackView v, DateTime clockNow)
    {
        var left = s.SecondsLeft(clockNow);
        if (double.IsNaN(left)) return 0;
        var total = v.Phase == BjPhase.Insurance ? Math.Min(15, (int)rules.TurnSeconds) : rules.TurnSeconds;
        return (float)Math.Clamp(left / total, 0, 1);
    }

    private static void TotalBadge(ImDrawListPtr dl, Vector2 pos, BjHandView hd, bool active)
    {
        string text;
        Vector4 col;
        if (hd.Flags.HasFlag(BjHandFlags.Natural))
            (text, col) = ("BLACKJACK", Pal.Gold);
        else if (hd.Flags.HasFlag(BjHandFlags.Busted))
            (text, col) = ($"BUST {BjMath.Total(hd.Cards)}", Pal.Red);
        else if (hd.Flags.HasFlag(BjHandFlags.Surrendered))
            (text, col) = ("SURRENDERED", Pal.Muted);
        else
            (text, col) = (BjMath.Describe(hd.Cards), active ? Pal.Gold : Pal.Text);
        Badge(dl, pos, text, col, active);
    }

    private static void OutcomeBadge(ImDrawListPtr dl, Vector2 pos, BjHandView hd, double now)
    {
        var (text, col) = hd.Outcome switch
        {
            BjOutcome.Blackjack => ($"BLACKJACK +{Gfx.Chips(hd.Payout - hd.Bet)}", Pal.Gold),
            BjOutcome.Win => ($"WIN +{Gfx.Chips(hd.Payout - hd.Bet)}", Pal.Green),
            BjOutcome.Push => ("PUSH", Pal.Blue),
            BjOutcome.Surrender => ($"SURRENDER -{Gfx.Chips(hd.Bet - hd.Payout)}", Pal.Muted),
            _ => (hd.Flags.HasFlag(BjHandFlags.Busted) ? "BUST" : "LOSE", Pal.Red),
        };
        Badge(dl, pos, text, col, hd.Outcome is BjOutcome.Win or BjOutcome.Blackjack);
    }

    private static void Badge(ImDrawListPtr dl, Vector2 c, string text, Vector4 col, bool strong)
    {
        var fs = S(13.5f);
        var ts = Gfx.TextSize(FontRole.Body, fs, text);
        var min = c - new Vector2(ts.X / 2 + S(9), ts.Y / 2 + S(3));
        var max = c + new Vector2(ts.X / 2 + S(9), ts.Y / 2 + S(3));
        Gfx.Shadow(dl, min, max, S(4), S(4), 0.4f, new Vector2(0, S(2)));
        var cut = (max.Y - min.Y) * 0.32f;
        Gfx.ChamferPanel(dl, min, max, cut, strong ? Pal.Mix(col, Pal.Black, 0.3f) : Pal.Hex(0x0E1530, 0.95f), strong ? Pal.Mix(col, Pal.Black, 0.5f) : Pal.Hex(0x080C1E, 0.95f),
            Pal.A(col, strong ? 1f : 0.7f), S(1.2f), all: true);
        if (strong) Gfx.ChamferGlow(dl, min, max, cut, col, S(6), 0.6f, all: true);
        dl.AddText(UiEnv.Font(FontRole.Body, fs), fs, c - ts / 2, Pal.U(strong ? Pal.White : Pal.Mix(col, Pal.White, 0.4f)), text);
    }

    private void DrawDealer(ImDrawListPtr dl, BlackjackView v, Vector2 top, Vector2 radii, float ch, float cw, Vector2 shoe, double now)
    {
        var c = top + new Vector2(0, radii.Y * 0.225f);
        var n = v.Dealer.Count;
        var gap = cw * 0.62f;
        // Dealer draws at settlement come one at a time.
        var dealerGap = v.Phase == BjPhase.Settled ? 0.55 : 0.13;
        for (var j = 0; j < n; j++)
        {
            var target = c + new Vector2((j - (n - 1) / 2f) * gap, 0);
            var pose = _a.Card($"d{j}", v.Dealer[j], target, ch, shoe, now, j >= 2 ? dealerGap : 0.13);
            if (!pose.Visible) continue;
            CardArt.Draw(dl, pose.Pos, ch, v.Dealer[j], pose.Flip);
        }
        if (n >= 2 && now >= _a.QuietAt(now) - 0.36)
        {
            var visible = v.Dealer.Where(x => !x.IsHidden).ToList();
            var total = BjMath.Total(visible);
            var text = v.Dealer.Any(x => x.IsHidden) ? $"Dealer shows {total}"
                : BjMath.IsNatural(v.Dealer) ? "Dealer: BLACKJACK"
                : total > 21 ? $"Dealer busts ({total})" : $"Dealer: {total}";
            var col = total > 21 ? Pal.Green : BjMath.IsNatural(v.Dealer) ? Pal.Red : Pal.Text;
            Badge(dl, c + new Vector2(0, ch * 0.64f), text, col, false);
        }
    }

    /// <summary>Spots changes between snapshots and schedules chips, banners and confetti.</summary>
    private void Diff(BlackjackView v, ClientSession s, double now, Vector2 top, Vector2 radii, float ch, Vector2 tray)
    {
        var n = (s.Table.BlackjackRules ?? new BlackjackRules()).MaxSeats;

        if (_prev != null && v.Phase == BjPhase.Betting && _prev.Round == v.Round)
        {
            foreach (var sv in v.Seats)
            {
                var before = _prev.Seat(sv.Seat)?.Bet ?? 0;
                if (sv.Bet <= before) continue;
                var g = SeatGeometry(top, radii, Theta(sv.Seat, n), ch);
                _a.Chips(g.Plate, g.Circle + new Vector2(0, g.CircleR * 0.35f), sv.Bet - before, g.CircleR * 0.42f, now);
                _betShowAt[sv.Seat] = now + 0.55 / Math.Max(0.25f, _a.Speed);
            }
        }

        if (v.Phase == BjPhase.Settled && v.Round != _resultsRound)
        {
            _resultsRound = v.Round;
            // The dealer's cards are queued during this frame's draw; wait for them before paying out.
            _resultsAt = _a.QuietAt(now) + 0.55 * Math.Max(0, v.Dealer.Count - 2) + 0.4;
            foreach (var sv in v.Seats.Where(x => x.Hands.Count > 0))
            {
                var g = SeatGeometry(top, radii, Theta(sv.Seat, n), ch);
                var stackPos = g.Circle + new Vector2(0, g.CircleR * 0.35f);
                foreach (var hd in sv.Hands)
                {
                    if (hd.Payout > hd.Bet) _a.Chips(tray, stackPos, hd.Payout - hd.Bet, g.CircleR * 0.42f, _resultsAt);
                    else if (hd.Payout == 0) _a.Chips(stackPos, tray, hd.Bet, g.CircleR * 0.42f, _resultsAt);
                }
                if (sv.RoundNet > 0) _a.Float(g.Plate - new Vector2(0, S(40)), $"+{sv.RoundNet:N0}", Pal.GoldLight, _resultsAt + 0.3);

                if (!s.IsSeated || sv.Seat != s.MySeat) continue;
                var natural = sv.Hands.Any(x => x.Outcome == BjOutcome.Blackjack);
                if (natural)
                {
                    _a.Announce("BLACKJACK!", $"+{sv.RoundNet:N0} chips", Pal.Gold, _resultsAt);
                    _a.Confetti(g.Plate - new Vector2(0, S(60)), 90, _resultsAt);
                }
                else if (sv.RoundNet > 0)
                {
                    _a.Announce("YOU WIN", $"+{sv.RoundNet:N0} chips", Pal.Gold, _resultsAt);
                    _a.Confetti(g.Plate - new Vector2(0, S(60)), 45, _resultsAt, 0.8f);
                }
                else if (sv.RoundNet == 0 && sv.Hands.All(x => x.Outcome == BjOutcome.Push))
                    _a.Announce("PUSH", "Your bet is returned", Pal.Blue, _resultsAt, 1.8);
                else if (BjMath.Total(v.Dealer) > 21)
                    _a.Announce("DEALER BUSTS", "", Pal.Green, _resultsAt, 1.6);
            }
        }
    }
}
