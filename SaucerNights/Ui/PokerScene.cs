using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Cards;
using SaucerNights.Core.Poker;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Ui;

/// <summary>Draws a hold'em table with the local player at the bottom, animating bets, pots and the board.</summary>
public sealed class PokerScene
{
    private readonly TableAnimator _a = new();
    private readonly Dictionary<int, double> _betShowAt = new();
    private PokerView? _prev;
    private long _resultsHand = -1;
    private double _resultsAt = double.MaxValue;
    private double _potShowAt;

    public TableAnimator Anim => _a;

    private static float S(float v) => UiEnv.S(v);

    public void Reset()
    {
        _a.Reset();
        _prev = null;
        _resultsHand = -1;
        _resultsAt = double.MaxValue;
        _betShowAt.Clear();
    }

    private sealed record Geo(Vector2 C, Vector2 Half, Vector2 SeatRadii, float BoardH, float MeH, float OtherH, Vector2 Origin, Vector2 Min, Vector2 Max, int Seats, int Me);

    private static Vector2 SeatPos(Geo g, int seat)
    {
        var k = ((seat - g.Me) % g.Seats + g.Seats) % g.Seats;
        var theta = MathF.PI / 2 + k * MathF.Tau / g.Seats;
        var p = g.C + new Vector2(MathF.Cos(theta) * g.SeatRadii.X, MathF.Sin(theta) * g.SeatRadii.Y);
        // Room for the hexagon avatar, which overhangs the plate's left edge.
        var half = SeatArt.Size.X / 2 + S(16);
        p.X = Math.Clamp(p.X, g.Min.X + half, g.Max.X - half);
        p.Y = Math.Clamp(p.Y, g.Min.Y + SeatArt.Size.Y / 2 + S(16), g.Max.Y - SeatArt.Size.Y / 2 - S(4));
        return p;
    }

    private static Vector2 BetPos(Geo g, int seat) => Vector2.Lerp(SeatPos(g, seat), g.C, 0.45f);

    private static Vector2 PotPos(Geo g) => g.C - new Vector2(0, g.BoardH * 0.98f);

    public void Draw(ImDrawListPtr dl, Vector2 min, Vector2 max, ClientSession s, GameApp app, double now)
    {
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var rules = s.Table.PokerRules ?? new PokerRules();
        var v = s.Poker;
        _a.Speed = app.Settings.AnimationSpeed;

        var c = new Vector2((min.X + max.X) / 2, min.Y + h * 0.47f);
        var halfX = Math.Min(w * 0.38f, h * 0.95f);
        var half = new Vector2(halfX, Math.Min(h * 0.29f, halfX * 0.58f));
        var boardH = Math.Clamp(Math.Min(h * 0.14f, w * 0.075f), S(52), S(124));
        var g = new Geo(c, half, half + new Vector2(S(20), S(40)), boardH, boardH * 1.08f, boardH * 0.6f,
            c - new Vector2(0, half.Y * 0.7f), min, max, rules.MaxSeats, s.IsSeated ? s.MySeat : 0);
        _a.Discard = g.Origin;
        _a.BannerY = 0.25f;

        TableArt.PokerTable(dl, c, half, app.Settings.Felt, now);
        TableArt.FeltMark(dl, c + new Vector2(0, boardH * 0.92f), Math.Min(half.Y * 0.15f, S(24)), app.Settings.Felt, "SOLUTION NIGHTS");

        _a.BeginFrame();
        _a.DrawFeltLayer(dl, now);

        if (v == null)
        {
            Gfx.TextGlow(dl, FontRole.Heading, S(28), c, Pal.Accent, "CONNECTING TO THE TABLE...", 0.8f, 0.8f);
            _a.EndFrame(now);
            return;
        }

        Diff(v, s, g, now);
        DrawBoard(dl, v, g, now);
        DrawPot(dl, v, g, now);

        var showdown = v.Phase == PokerPhase.Showdown && now >= _resultsAt - 0.2;
        var winnerCards = showdown ? WinningCards(v) : [];
        for (var seat = 0; seat < rules.MaxSeats; seat++)
            DrawSeat(dl, s, v, rules, g, seat, now, app.Clock.Now, showdown, winnerCards, app.Settings.ShowHandStrength);

        // Board highlight for the winning five.
        var msg = v.Phase switch
        {
            PokerPhase.Waiting => s.Table.Flags.HasFlag(TableFlags.Paused) ? "TABLE PAUSED" : v.Seats.Count < 2 ? "WAITING FOR PLAYERS" : "NEXT HAND SOON",
            PokerPhase.Starting => "SHUFFLING...",
            _ => null,
        };
        if (msg != null) Gfx.TextGlow(dl, FontRole.Heading, S(24), c - new Vector2(0, S(6)), TableArt.Felt(app.Settings.Felt).Neon, msg, 0.7f, 0.75f);

        _a.EndFrame(now);
        _a.DrawOverlay(dl, min, max, now);
        _prev = v;
    }

    private void DrawBoard(ImDrawListPtr dl, PokerView v, Geo g, double now)
    {
        var bw = g.BoardH * CardArt.Aspect;
        var showdownCards = v.Phase == PokerPhase.Showdown && now >= _resultsAt - 0.2 ? WinningCards(v) : [];
        for (var k = 0; k < 5; k++)
        {
            var pos = g.C + new Vector2((k - 2) * bw * 1.1f, S(4));
            if (k >= v.Board.Count)
            {
                if (v.BettingRound) CardArt.Slot(dl, pos, g.BoardH, Pal.White);
                continue;
            }
            // A run-out deals one street at a time.
            var gap = v.Phase == PokerPhase.Showdown && (_prev?.Board.Count ?? 0) < v.Board.Count ? 0.45 : 0.18;
            var pose = _a.Card($"b{k}", v.Board[k], pos, g.BoardH, g.Origin, now, gap);
            if (!pose.Visible) continue;
            var win = showdownCards.Contains(v.Board[k]);
            CardArt.Draw(dl, pose.Pos, g.BoardH, v.Board[k], pose.Flip, 1f, win, showdownCards.Count > 0 && !win, win ? S(6) : 0);
        }
    }

    private void DrawPot(ImDrawListPtr dl, PokerView v, Geo g, double now)
    {
        var total = v.PotTotal;
        if (total <= 0 || now < _potShowAt) return;
        if (v.Phase == PokerPhase.Showdown && now >= _resultsAt) return;
        var p = PotPos(g);
        ChipArt.Stack(dl, p, total, S(12), 1f, 4);
        ChipArt.Pill(dl, p + new Vector2(0, S(22)), _a.Count("pot", total, now), S(15));
        if (v.Pots.Count > 1)
        {
            var side = string.Join("   ", v.Pots.Skip(1).Select((x, i) => $"Side {i + 1}: {Gfx.Chips(x.Amount)}"));
            Gfx.TextCentered(dl, FontRole.Body, S(12.5f), p + new Vector2(0, S(42)), Pal.U(Pal.Text, 0.7f), $"Main: {Gfx.Chips(v.Pots[0].Amount)}   {side}");
        }
    }

    private void DrawSeat(ImDrawListPtr dl, ClientSession s, PokerView v, PokerRules rules, Geo g, int seat, double now, DateTime clockNow,
        bool showdown, HashSet<Card> winnerCards, bool showStrength)
    {
        var pos = SeatPos(g, seat);
        var sv = v.Seat(seat);
        if (sv == null)
        {
            var isHost = string.Equals(s.Table.HostId, s.SelfId, StringComparison.OrdinalIgnoreCase);
            var canSit = s.Status is ClientStatus.Spectating or ClientStatus.Left && s.Table.SeatsTaken < s.Table.SeatsMax
                         && (!isHost || s.Table.Flags.HasFlag(TableFlags.HostPlays));
            if (SeatArt.Empty(dl, pos, canSit ? "Sit here" : "Open seat", canSit, $"##sit{seat}") && canSit) s.Join(seat);
            return;
        }

        var isMe = s.IsSeated && s.MySeat == seat;
        var toCenter = Vector2.Normalize(g.C - pos);
        var dealt = (v.DealtMask & (1 << seat)) != 0;

        // Bet in front of the player.
        if (sv.Bet > 0 && now >= _betShowAt.GetValueOrDefault(seat))
        {
            var bp = BetPos(g, seat);
            ChipArt.Stack(dl, bp, sv.Bet, S(10), 1f, 2);
            ChipArt.Pill(dl, bp + new Vector2(0, S(19)), sv.Bet, S(13.5f));
        }

        // Dealer button.
        if (v.Button == seat && v.Phase != PokerPhase.Waiting)
        {
            var perp = new Vector2(-toCenter.Y, toCenter.X);
            var bp = pos + toCenter * (EdgeDistance(toCenter) + S(16)) + perp * (SeatArt.Size.X * 0.5f + S(4));
            Span<Vector2> hex = stackalloc Vector2[6];
            Gfx.Hexagon(hex, bp + new Vector2(0, S(2)), S(13));
            Gfx.FillConvex(dl, hex, Pal.U(Pal.Black, 0.5f));
            Gfx.Hexagon(hex, bp, S(12.5f));
            Gfx.FillVertical(dl, hex, Pal.Hex(0xFFFFFF), Pal.Hex(0xD5DEF2));
            Gfx.Outline(dl, hex, Pal.U(Pal.Accent), S(1.6f));
            Gfx.TextCentered(dl, FontRole.Heading, S(15), bp + new Vector2(0, S(1)), Pal.U(Pal.Hex(0x0A1230)), "D");
        }

        // Hole cards.
        var cards = new List<Card>();
        var faceUp = false;
        if (isMe && s.HoleHand == v.Hand && s.Hole.Length == 2 && dealt)
        {
            cards.AddRange(s.Hole);
            faceUp = true;
        }
        else if (sv.Shown.Count == 2)
        {
            cards.AddRange(sv.Shown);
            faceUp = true;
        }
        else if (dealt && sv.InHand && v.Phase != PokerPhase.Waiting)
        {
            cards.Add(Card.Hidden);
            cards.Add(Card.Hidden);
        }
        var folded = sv.Status == PokerSeatStatus.Folded;
        if (folded && !isMe && sv.Shown.Count != 2) cards.Clear();

        var big = isMe || sv.Shown.Count == 2;
        var ch = isMe ? g.MeH : sv.Shown.Count == 2 ? g.OtherH * 1.35f : g.OtherH;
        var cw = ch * CardArt.Aspect;
        var cardsCenter = pos + toCenter * (EdgeDistance(toCenter) + ch * 0.36f);
        if (isMe) cardsCenter = pos - new Vector2(0, SeatArt.Size.Y * 0.5f + ch * 0.5f - S(6));
        var isWinner = showdown && sv.Won > 0;
        for (var j = 0; j < cards.Count; j++)
        {
            var spread = big ? cw * 0.56f : cw * 0.34f;
            var target = cardsCenter + new Vector2((j - 0.5f) * spread * (big ? 1.08f : 1), (j == 0 ? 1 : -1) * S(2));
            var pose = _a.Card($"h{seat}:{j}", cards[j], target, ch, g.Origin, now);
            if (!pose.Visible) continue;
            var win = isWinner && winnerCards.Contains(cards[j]);
            CardArt.Draw(dl, pose.Pos, ch, cards[j], faceUp || cards[j].IsHidden ? pose.Flip : 0, 1f, win, folded || (showdown && sv.Shown.Count == 2 && !isWinner), win ? S(5) : 0);
        }

        // Hand strength / showdown label.
        string? label = null;
        if (faceUp && cards.Count == 2 && !folded)
        {
            if (v.Board.Count >= 3 || showdown) label = HandEvaluator.Evaluate([.. cards, .. v.Board]).Name;
            else if (isMe && showStrength) label = Preflop(cards);
        }
        if (label != null && (isMe ? showStrength || showdown : showdown))
        {
            // Keep the label on the side of the cards away from the name plate.
            var above = isMe || toCenter.Y < -0.3f;
            var lp = above ? cardsCenter - new Vector2(0, ch * 0.5f + S(14)) : cardsCenter + new Vector2(0, ch * 0.5f + S(12));
            var fs = S(13);
            var ts = Gfx.TextSize(FontRole.Body, fs, label);
            var lMin = lp - new Vector2(ts.X / 2 + S(9), ts.Y / 2 + S(3));
            var lMax = lp + new Vector2(ts.X / 2 + S(9), ts.Y / 2 + S(3));
            var lc = isWinner ? Pal.Gold : Pal.Accent;
            Gfx.ChamferPanel(dl, lMin, lMax, (lMax.Y - lMin.Y) * 0.32f, isWinner ? Pal.Mix(Pal.Gold, Pal.Black, 0.35f) : Pal.Hex(0x0E1530, 0.94f),
                isWinner ? Pal.Mix(Pal.Gold, Pal.Black, 0.55f) : Pal.Hex(0x080C1E, 0.94f), Pal.A(lc, isWinner ? 1f : 0.55f), 1f, all: true);
            dl.AddText(UiEnv.Font(FontRole.Body, fs), fs, lp - ts / 2, Pal.U(isWinner ? Pal.White : Pal.AccentLight), label);
        }

        // Plate.
        var active = v.BettingRound && v.ActiveSeat == seat;
        var left = s.SecondsLeft(clockNow);
        var frac = active && !double.IsNaN(left) ? (float)Math.Clamp(left / rules.TurnSeconds, 0, 1) : 0;
        var entry = s.Roster.BySeat(seat);
        var (badge, badgeCol) = Badge(v, sv, seat, entry?.Flags ?? RosterFlags.None);
        SeatArt.Plate(dl, pos, new SeatInfo(s.NameAt(seat), _a.Count($"stack{seat}", sv.Stack, now), isMe, active, frac, badge, badgeCol,
            folded || sv.Status is PokerSeatStatus.SittingOut or PokerSeatStatus.Waiting && v.Phase != PokerPhase.Waiting,
            entry?.Flags.HasFlag(RosterFlags.Host) == true, sv.Status == PokerSeatStatus.SittingOut), now);
    }

    /// <summary>Distance from the plate's centre to its edge along a direction.</summary>
    private static float EdgeDistance(Vector2 dir)
    {
        var half = SeatArt.Size / 2;
        var dx = Math.Abs(dir.X) > 1e-3f ? half.X / Math.Abs(dir.X) : float.MaxValue;
        var dy = Math.Abs(dir.Y) > 1e-3f ? half.Y / Math.Abs(dir.Y) : float.MaxValue;
        return Math.Min(dx, dy);
    }

    private static (string?, Vector4) Badge(PokerView v, PokerSeatView sv, int seat, RosterFlags flags)
    {
        var between = v.Phase is PokerPhase.Waiting or PokerPhase.Starting || !sv.InHand;
        if (flags.HasFlag(RosterFlags.RebuyPending) && between && v.Phase != PokerPhase.Showdown) return ("REBUY ASKED", Pal.Magenta);
        if (sv.Stack == 0 && between && v.Phase != PokerPhase.Showdown) return ("OUT OF CHIPS", Pal.Red);
        if (sv.Status == PokerSeatStatus.SittingOut) return ("SITTING OUT", Pal.Muted);
        if (sv.Status == PokerSeatStatus.Waiting && v.Phase is not PokerPhase.Waiting and not PokerPhase.Starting) return ("NEXT HAND", Pal.Muted);
        if (v.Phase == PokerPhase.Showdown && sv.Won > 0) return ($"WINS {Gfx.Chips(sv.Won)}", Pal.Gold);
        if (sv.Status == PokerSeatStatus.AllIn && v.Phase != PokerPhase.Showdown) return ("ALL-IN", Pal.Red);
        if (v.Phase == PokerPhase.Showdown)
            return sv.Status == PokerSeatStatus.Folded ? (sv.Shown.Count == 2 ? "FOLDED · SHOWS" : "FOLD", Pal.Muted) : (null, Pal.Muted);
        return sv.Last switch
        {
            PokerLast.Fold => ("FOLD", Pal.Muted),
            PokerLast.Check => ("CHECK", Pal.Blue),
            PokerLast.Call => ("CALL", Pal.Green),
            PokerLast.Bet => ("BET", Pal.Orange),
            PokerLast.Raise => ("RAISE", Pal.Orange),
            PokerLast.SmallBlind => ("SMALL BLIND", Pal.Purple),
            PokerLast.BigBlind => ("BIG BLIND", Pal.Purple),
            _ => (null, Pal.Muted),
        };
    }

    public static string Preflop(IReadOnlyList<Card> hole)
    {
        if (hole.Count != 2) return string.Empty;
        var hi = hole[0].Rank >= hole[1].Rank ? hole[0] : hole[1];
        var lo = hole[0].Rank >= hole[1].Rank ? hole[1] : hole[0];
        if (hi.Rank == lo.Rank) return $"Pocket {hi.RankNamePlural}";
        return $"{hi.RankName}-{lo.RankName}{(hi.Suit == lo.Suit ? " suited" : "")}";
    }

    /// <summary>Cards making the winning hand(s) at showdown.</summary>
    private static HashSet<Card> WinningCards(PokerView v)
    {
        var set = new HashSet<Card>();
        foreach (var sv in v.Seats.Where(x => x.Won > 0 && x.Shown.Count == 2))
            foreach (var card in HandEvaluator.Evaluate([.. sv.Shown, .. v.Board]).Best)
                set.Add(card);
        return set;
    }

    private void Diff(PokerView v, ClientSession s, Geo g, double now)
    {
        var speed = Math.Max(0.25f, _a.Speed);
        if (_prev != null && _prev.Hand == v.Hand)
        {
            // New chips in front of a player.
            foreach (var sv in v.Seats)
            {
                var before = _prev.Seat(sv.Seat)?.Bet ?? 0;
                if (sv.Bet > before)
                {
                    _a.Chips(SeatPos(g, sv.Seat), BetPos(g, sv.Seat), sv.Bet - before, S(10), now, 0.45);
                    _betShowAt[sv.Seat] = now + 0.45 / speed;
                }
            }
            // Bets swept into the pot at the end of a street.
            var swept = false;
            foreach (var pv in _prev.Seats.Where(x => x.Bet > 0))
            {
                var after = v.Seat(pv.Seat)?.Bet ?? 0;
                if (after != 0) continue;
                _a.Chips(BetPos(g, pv.Seat), PotPos(g), pv.Bet, S(10), now + 0.1, 0.5);
                swept = true;
            }
            if (swept) _potShowAt = now + 0.6 / speed;
        }

        if (v.Phase == PokerPhase.Showdown && v.Hand != _resultsHand)
        {
            _resultsHand = v.Hand;
            var runout = Math.Max(0, v.Board.Count - (_prev?.Hand == v.Hand ? _prev.Board.Count : v.Board.Count));
            _resultsAt = Math.Max(_a.QuietAt(now), now + runout * 0.45 / speed) + 0.9 / speed;
            var winners = v.Seats.Where(x => x.Won > 0).ToList();
            var delay = 0.0;
            foreach (var wv in winners)
            {
                _a.Chips(PotPos(g), SeatPos(g, wv.Seat), wv.Won, S(11), _resultsAt + delay, 0.65);
                _a.Float(SeatPos(g, wv.Seat) - new Vector2(0, S(46)), $"+{wv.Won:N0}", Pal.GoldLight, _resultsAt + delay + 0.5);
                delay += 0.25;
            }
            var mine = s.IsSeated ? v.Seat(s.MySeat) : null;
            var top = winners.OrderByDescending(x => x.Won).FirstOrDefault();
            if (mine is { Won: > 0 })
            {
                var name = mine.Shown.Count == 2 ? HandEvaluator.Evaluate([.. mine.Shown, .. v.Board]).Name : "Everyone folded";
                _a.Announce("YOU WIN", $"{mine.Won:N0} chips  -  {name}", Pal.Gold, _resultsAt);
                _a.Confetti(SeatPos(g, mine.Seat) - new Vector2(0, S(70)), mine.Won >= v.BigBlind * 20 ? 110 : 55, _resultsAt + 0.2);
            }
            else if (top != null && top.Shown.Count == 2)
            {
                var name = HandEvaluator.Evaluate([.. top.Shown, .. v.Board]).Name;
                _a.Announce($"{SeatArt.ShortName(s.NameAt(top.Seat)).ToUpperInvariant()} WINS", $"{top.Won:N0} chips  -  {name}", Pal.Text, _resultsAt, 2.0);
            }
        }
    }
}
