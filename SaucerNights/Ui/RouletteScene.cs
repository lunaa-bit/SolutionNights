using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Protocol;
using SaucerNights.Core.Roulette;
using SaucerNights.Core.Sessions;

namespace SaucerNights.Ui;

/// <summary>The bets the player is building for this spin, shared by the board (clicks) and the dock (chips, place).</summary>
public sealed class RouletteDraft
{
    public readonly Dictionary<byte, long> Bets = new();

    /// <summary>The spin these bets are for.</summary>
    public long Round = -1;

    /// <summary>The chip value a click adds.</summary>
    public long Chip;

    /// <summary>What was placed last spin, for "Repeat".</summary>
    public List<(byte Index, long Amount)> Last = [];

    public long Total => Bets.Values.Sum();

    public List<(byte Index, long Amount)> List() => Bets.Where(b => b.Value > 0).OrderBy(b => b.Key).Select(b => (b.Key, b.Value)).ToList();

    /// <summary>True when the draft differs from the bets the host has for this player.</summary>
    public bool Differs(IReadOnlyList<(byte Index, long Amount)>? placed)
    {
        var mine = Bets.Where(b => b.Value > 0).ToDictionary(b => b.Key, b => b.Value);
        if (placed == null) return mine.Count > 0;
        if (placed.Count != mine.Count) return true;
        return placed.Any(b => !mine.TryGetValue(b.Index, out var a) || a != b.Amount);
    }

    public void Reset(long round)
    {
        Round = round;
        Bets.Clear();
    }
}

/// <summary>Draws a roulette table: the wheel and ball, the betting board with everyone's chips, and the players.</summary>
public sealed class RouletteScene
{
    /// <summary>How long the ball runs before it drops into its pocket (matches the host's spin time).</summary>
    private const double LandAt = 0.84;

    private readonly TableAnimator _a = new();
    private readonly RouletteBoard _board = new();
    private RouletteView? _prev;
    private long _spinRound = -1;
    private double _spinStart = double.MaxValue;
    private long _settledRound = -1;
    private double _resultsAt = double.MaxValue;
    private ulong? _hover;
    private string _placedKey = string.Empty;
    private List<(byte Index, long Amount)> _placedPrev = [];

    public RouletteDraft Draft { get; } = new();

    public TableAnimator Anim => _a;

    private static float S(float v) => UiEnv.S(v);

    public void Reset()
    {
        _a.Reset();
        _prev = null;
        _spinRound = -1;
        _spinStart = double.MaxValue;
        _settledRound = -1;
        _resultsAt = double.MaxValue;
        Draft.Reset(-1);
        Draft.Last = [];
    }

    private sealed record Geo(Vector2 Wheel, float WheelR, Vector2 Min, Vector2 Max, float PlateY, float PlateW, int PerRow, int Seats);

    public void Draw(ImDrawListPtr dl, Vector2 min, Vector2 max, ClientSession s, GameApp app, double now)
    {
        var rules = s.Table.RouletteRules ?? new RouletteRules();
        var v = s.Roulette;
        _a.Speed = app.Settings.AnimationSpeed;
        var (feltColor, _, print, neon) = TableArt.Felt(app.Settings.Felt);
        var w = max.X - min.X;
        var h = max.Y - min.Y;

        // Layout: wheel left, board right, players along the bottom.
        // Players share one row when their plates can shrink to fit, otherwise two.
        var plateH = SeatArt.Size.Y;
        var rowRoom = w - S(40);
        var plateW = Math.Min(SeatArt.Size.X, (rowRoom - S(10) * (rules.MaxSeats - 1)) / rules.MaxSeats);
        int perRow = rules.MaxSeats;
        if (plateW < S(142))
        {
            perRow = (rules.MaxSeats + 1) / 2;
            plateW = Math.Min(SeatArt.Size.X, (rowRoom - S(10) * (perRow - 1)) / perRow);
        }
        var rows = (rules.MaxSeats + perRow - 1) / perRow;
        var stripH = rows * (plateH + S(22)) + S(10);
        var top = min.Y + S(44);
        var bottom = max.Y - stripH;
        var wheelR = Math.Min((bottom - top) * 0.44f, w * 0.165f);
        var wheelC = new Vector2(min.X + S(24) + wheelR * 1.08f, (top + bottom) / 2 + S(8));
        var msgY = Math.Max(top, wheelC.Y - wheelR - S(34));
        _board.Layout(new Vector2(wheelC.X + wheelR * 1.1f + S(26), top + S(34)), new Vector2(max.X - S(22), bottom - S(4)));
        var g = new Geo(wheelC, wheelR, min, max, max.Y - stripH + S(16) + plateH / 2, plateW, perRow, rules.MaxSeats);

        _a.BeginFrame();
        if (v == null)
        {
            RouletteArt.Wheel(dl, wheelC, wheelR, (float)now * 0.25f, neon);
            Gfx.TextGlow(dl, FontRole.Heading, S(28), (min + max) / 2, Pal.Accent, "CONNECTING TO THE TABLE...", 0.8f, 0.8f);
            _a.EndFrame(now);
            return;
        }
        Diff(v, s, g, now, rules, app.Clock.Now);

        // Wheel and ball.
        var wheel = (float)(now * 0.22);
        var spinning = v.Phase == RoulettePhase.Spinning || (v.Phase == RoulettePhase.Settled && now < _resultsAt);
        var result = v.HasResult ? v.Result : v.History.Count > 0 ? v.History[^1] : -1;
        float? ballAngle = null;
        var ballR = 0.72f;
        var landed = false;
        if (result >= 0)
        {
            var t = spinning && v.Round == _spinRound ? (now - _spinStart) / (RouletteRules.SpinSeconds / Math.Max(0.25, _a.Speed)) : 1;
            (ballAngle, ballR, landed) = Ball(t, wheel, result);
        }
        var lit = landed && v.Phase == RoulettePhase.Settled ? result : -1;
        var pulse = 0.5f + 0.5f * MathF.Sin((float)now * 5);
        RouletteArt.Wheel(dl, wheelC, wheelR, wheel, neon, ballAngle, ballR, lit, lit >= 0 ? 0.6f + 0.4f * pulse : 0);

        // History above the board.
        History(dl, v, now);

        // Board, with the bet under the mouse when the player can bet.
        var canBet = s.IsSeated && v.Phase == RoulettePhase.Betting && v.Seat(s.MySeat) is { Status: not RouletteSeatStatus.SittingOut };
        if (Draft.Round != v.Round)
        {
            // Remember last spin's bets for "Repeat" (the new snapshot has already cleared them).
            if (Draft.Round >= 0 && _prev?.Round == Draft.Round && _prev.Seat(s.MySeat) is { Bets.Count: > 0 } prevMe)
                Draft.Last = prevMe.Bets.Select(b => (b.Index, b.Amount)).ToList();
            Draft.Reset(v.Round);
            // Start from whatever the host already holds for us this spin.
            if (v.Seat(s.MySeat) is { } me) foreach (var b in me.Bets) Draft.Bets[b.Index] = b.Amount;
        }
        // While the player hasn't changed anything locally, the draft follows what the host holds.
        if (v.Seat(s.MySeat) is { } mine)
        {
            var key = string.Join(",", mine.Bets.Select(b => $"{b.Index}:{b.Amount}"));
            if (key != _placedKey)
            {
                if (!Draft.Differs(_placedPrev))
                {
                    Draft.Bets.Clear();
                    foreach (var b in mine.Bets) Draft.Bets[b.Index] = b.Amount;
                }
                _placedKey = key;
                _placedPrev = mine.Bets.ToList();
            }
        }
        _hover = null;
        if (canBet)
        {
            ImGui.SetCursorScreenPos(_board.Min);
            ImGui.InvisibleButton("##rouletteboard", _board.Max - _board.Min);
            if (ImGui.IsItemHovered() && _board.HitTest(ImGui.GetMousePos()) is { } hit && RouletteBets.Find(hit.Mask) is var idx and >= 0)
            {
                _hover = hit.Mask;
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                var bet = RouletteBets.All[idx];
                var have = Draft.Bets.GetValueOrDefault((byte)idx);
                ImGui.SetTooltip($"{bet.Name}  ·  pays {RouletteBets.Multiplier(idx)} to 1" + (have > 0 ? $"\nYour bet: {have:N0}" : "") + "\nClick to add a chip, right-click to take one off");
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) AddChip((byte)idx, rules, s, v);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) RemoveChip((byte)idx);
            }
        }
        _board.Draw(dl, neon, print, feltColor, lit, _hover, pulse);

        // Chips on the board: other players first, then ours on top.
        DrawBets(dl, s, v, now, lit);

        // Players.
        for (var i = 0; i < rules.MaxSeats; i++) DrawSeat(dl, s, v, g, i, now, app.Clock.Now, rules);

        // Messages over the wheel.
        string? msg = v.Phase switch
        {
            RoulettePhase.Waiting => s.Table.Flags.HasFlag(TableFlags.Paused) ? "TABLE PAUSED" : "WAITING FOR PLAYERS",
            RoulettePhase.Spinning => "NO MORE BETS",
            _ => null,
        };
        if (v.Phase == RoulettePhase.Betting)
        {
            var left = s.SecondsLeft(app.Clock.Now);
            Gfx.TextGlow(dl, FontRole.Heading, S(26), new Vector2(wheelC.X, msgY - S(10)), Pal.Gold, "PLACE YOUR BETS", 1f, 0.95f);
            Gfx.TextCentered(dl, FontRole.Body, S(14), new Vector2(wheelC.X, msgY + S(14)), Pal.U(Pal.Text, 0.75f),
                double.IsNaN(left) ? "The wheel spins after the first bet" : $"Spinning in {Math.Ceiling(left):0}s");
        }
        else if (msg != null)
        {
            Gfx.TextGlow(dl, FontRole.Heading, S(24), new Vector2(wheelC.X, msgY - S(6)), msg == "NO MORE BETS" ? Pal.Magenta : neon, msg, 0.8f, 0.85f);
        }
        if (landed && result >= 0 && v.Phase == RoulettePhase.Settled)
        {
            var c = new Vector2(wheelC.X, msgY - S(4));
            RouletteArt.NumberBadge(dl, c - new Vector2(S(52), 0), S(17), result, 1, true);
            var label = result == 0 ? "ZERO" : $"{(RouletteArt.IsRed(result) ? "RED" : "BLACK")}  ·  {(result % 2 == 0 ? "EVEN" : "ODD")}";
            Gfx.TextSpaced(dl, FontRole.Heading, S(20), c - new Vector2(S(28), S(11)), Pal.U(Pal.Text), label, S(1.2f));
        }

        _a.EndFrame(now);
        _a.DrawFeltLayer(dl, now);
        _a.DrawOverlay(dl, min, max, now);
        _prev = v;
    }

    /// <summary>Ball position: running round the track against the wheel, then dropping into the result's pocket.</summary>
    private static (float Angle, float Radius, bool Landed) Ball(double t, float wheel, int result)
    {
        var target = RouletteArt.PocketAngle(result, wheel);
        var u = (float)Math.Clamp(t / LandAt, 0, 1);
        if (u >= 1) return (target, 0.72f, true);
        var offset = -MathF.Pow(1 - u, 3) * MathF.PI * 16;
        var radius = u < 0.55f ? 0.93f : 0.93f - (u - 0.55f) / 0.45f * 0.21f + 0.035f * MathF.Sin(u * 46) * (1 - u);
        return (target + offset, radius, false);
    }

    private void AddChip(byte idx, RouletteRules rules, ClientSession s, RouletteView v)
    {
        var me = v.Seat(s.MySeat);
        if (me == null || Draft.Chip <= 0) return;
        var have = Draft.Bets.GetValueOrDefault(idx);
        if (have == 0 && Draft.Bets.Count(b => b.Value > 0) >= RouletteBets.MaxSpots) return;
        var placed = me.Bets.Sum(b => b.Amount);
        var budget = Math.Min(rules.TableMax, me.Bankroll + placed);
        var add = Math.Min(Draft.Chip, rules.MaxBet - have);
        add = Math.Min(add, budget - Draft.Total);
        if (add <= 0) return;
        Draft.Bets[idx] = Math.Max(have + add, rules.MinBet);
        if (Draft.Total > budget) Draft.Bets[idx] = have;
    }

    private void RemoveChip(byte idx)
    {
        if (!Draft.Bets.TryGetValue(idx, out var have)) return;
        var left = have - Math.Max(1, Draft.Chip);
        if (left <= 0) Draft.Bets.Remove(idx);
        else Draft.Bets[idx] = left;
    }

    private void History(ImDrawListPtr dl, RouletteView v, double now)
    {
        var r = Math.Min(S(14), _board.Cell * 0.3f);
        var y = _board.Min.Y - S(24) - r;
        var x = _board.Max.X + S(4) - r;
        var shown = v.History.TakeLast(12).ToList();
        if (v.Phase == RoulettePhase.Spinning && shown.Count > 0 && shown[^1] == v.Result) shown.RemoveAt(shown.Count - 1);
        for (var i = shown.Count - 1; i >= 0; i--)
        {
            var newest = i == shown.Count - 1;
            RouletteArt.NumberBadge(dl, new Vector2(x, y), newest ? r * 1.15f : r, shown[i], newest ? 1f : 0.75f, newest);
            x -= r * 2.3f;
        }
        if (shown.Count > 0)
        {
            var label = "LAST SPINS";
            var ts = Gfx.TextSpacedSize(FontRole.Body, S(11), label, S(1.2f));
            Gfx.TextSpaced(dl, FontRole.Body, S(11), new Vector2(x + r - ts.X, y - ts.Y / 2), Pal.U(Pal.Muted), label, S(1.2f));
        }
    }

    private void DrawBets(ImDrawListPtr dl, ClientSession s, RouletteView v, double now, int lit)
    {
        var r = _board.Cell * 0.26f;
        var showResults = lit >= 0;
        foreach (var seat in v.Seats.OrderBy(x => s.IsSeated && x.Seat == s.MySeat ? 1 : 0))
        {
            var mine = s.IsSeated && seat.Seat == s.MySeat;
            if (mine && v.Phase == RoulettePhase.Betting) continue; // the draft below shows ours
            var tint = Pal.FromName(s.NameAt(seat.Seat));
            var off = mine ? Vector2.Zero : new Vector2((seat.Seat % 3 - 1) * r * 0.35f, -(seat.Seat % 2) * r * 0.3f);
            foreach (var b in seat.Bets)
            {
                var spot = _board.SpotOf(RouletteBets.All[b.Index].Mask) + off;
                var lost = showResults && !RouletteBets.Covers(b.Index, lit);
                var alpha = lost ? 0.35f : 1f;
                Chip(dl, spot, mine ? r : r * 0.82f, b.Amount, mine ? Pal.Gold : tint, alpha, mine);
            }
        }
        if (s.IsSeated && v.Phase == RoulettePhase.Betting)
        {
            var placed = v.Seat(s.MySeat)?.Bets;
            foreach (var (idx, amount) in Draft.Bets)
            {
                if (amount <= 0) continue;
                var spot = _board.SpotOf(RouletteBets.All[idx].Mask);
                var confirmed = placed?.Any(b => b.Index == idx && b.Amount == amount) == true;
                Chip(dl, spot, r, amount, confirmed ? Pal.Gold : Pal.Accent, confirmed ? 1f : 0.8f, true, !confirmed);
            }
        }
    }

    /// <summary>A chip on the board with an owner's ring and, for the local player, the amount.</summary>
    private static void Chip(ImDrawListPtr dl, Vector2 c, float r, long amount, Vector4 ring, float alpha, bool label, bool draft = false)
    {
        ChipArt.Top(dl, c, r, ChipArt.For(amount), alpha, false);
        dl.AddCircle(c, r + S(1.5f), Pal.U(ring, alpha), 24, S(draft ? 1.2f : 2f));
        if (draft) Gfx.GlowCircle(dl, c, r, Pal.Accent, S(5), 0.6f * alpha);
        if (label)
        {
            var text = Gfx.Chips(amount);
            var fs = Math.Max(S(10), r * 0.85f);
            var ts = Gfx.TextSize(FontRole.Heading, fs, text);
            var bc = c + new Vector2(0, r + ts.Y * 0.3f);
            dl.AddRectFilled(bc - new Vector2(ts.X / 2 + S(3), ts.Y / 2), bc + new Vector2(ts.X / 2 + S(3), ts.Y / 2), Pal.U(Pal.Hex(0x060A1C), 0.85f * alpha), S(2));
            Gfx.TextCentered(dl, FontRole.Heading, fs, bc, Pal.U(Pal.GoldLight, alpha), text);
        }
    }

    private Vector2 PlatePos(Geo g, int seat)
    {
        var row = seat / g.PerRow;
        var inRow = Math.Min(g.PerRow, g.Seats - row * g.PerRow);
        var col = seat % g.PerRow;
        var gap = S(10);
        var rowW = inRow * g.PlateW + (inRow - 1) * gap;
        var x = (g.Min.X + g.Max.X) / 2 - rowW / 2 + col * (g.PlateW + gap) + g.PlateW / 2;
        return new Vector2(x, g.PlateY + row * (SeatArt.Size.Y + S(22)));
    }

    private void DrawSeat(ImDrawListPtr dl, ClientSession s, RouletteView v, Geo g, int seat, double now, DateTime clockNow, RouletteRules rules)
    {
        var pos = PlatePos(g, seat);
        var sv = v.Seat(seat);
        if (sv == null)
        {
            var canSit = s.Status is ClientStatus.Spectating or ClientStatus.Left && s.Table.HostId != s.SelfId && s.Table.SeatsTaken < s.Table.SeatsMax;
            if (SeatArt.Empty(dl, pos, canSit ? "Sit here" : "Open seat", canSit, $"##rsit{seat}", g.PlateW) && canSit) s.Join(seat);
            return;
        }
        var isMe = s.IsSeated && s.MySeat == seat;
        var flags = s.Roster.BySeat(seat)?.Flags ?? RosterFlags.None;
        var onTable = sv.Bets.Sum(b => b.Amount);
        (string?, Vector4) badge = sv.Status == RouletteSeatStatus.SittingOut ? ("SITTING OUT", Pal.Muted)
            : flags.HasFlag(RosterFlags.RebuyPending) ? ("REBUY ASKED", Pal.Magenta)
            : v.Phase == RoulettePhase.Settled && sv.RoundNet > 0 ? ($"WINS {Gfx.Chips(sv.RoundNet)}", Pal.Gold)
            : v.Phase == RoulettePhase.Settled && sv.RoundNet < 0 ? ($"LOST {Gfx.Chips(-sv.RoundNet)}", Pal.Red)
            : onTable > 0 ? ($"BET {Gfx.Chips(onTable)}", Pal.Accent)
            : sv.Bankroll < rules.MinBet ? ("OUT OF CHIPS", Pal.Red)
            : (null, Pal.Muted);
        SeatArt.Plate(dl, pos, new SeatInfo(s.NameAt(seat), _a.Count($"bank{seat}", sv.Bankroll, now), isMe, false, 0, badge.Item1, badge.Item2,
            sv.Status == RouletteSeatStatus.SittingOut, flags.HasFlag(RosterFlags.Host), false), now, g.PlateW);
    }

    /// <summary>Spots changes between snapshots: the spin starting, and the payout once the ball has landed.</summary>
    private void Diff(RouletteView v, ClientSession s, Geo g, double now, RouletteRules rules, DateTime clockNow)
    {
        var speed = Math.Max(0.25, _a.Speed);
        if (v.Phase == RoulettePhase.Spinning && v.Round != _spinRound)
        {
            _spinRound = v.Round;
            // Joining part-way through a spin: start the ball where it would be.
            var left = s.SecondsLeft(clockNow) is var l && !double.IsNaN(l) ? l : RouletteRules.SpinSeconds;
            _spinStart = now - Math.Max(0, RouletteRules.SpinSeconds - left) / speed;
        }
        if (v.Phase == RoulettePhase.Settled && v.Round != _settledRound && v.HasResult)
        {
            _settledRound = v.Round;
            var landing = v.Round == _spinRound ? _spinStart + RouletteRules.SpinSeconds * LandAt / speed : now;
            _resultsAt = Math.Max(now, landing) + 0.35;
            var house = g.Wheel;
            foreach (var seat in v.Seats)
            {
                var plate = PlatePos(g, seat.Seat);
                foreach (var b in seat.Bets)
                {
                    var spot = _board.SpotOf(RouletteBets.All[b.Index].Mask);
                    if (RouletteBets.Covers(b.Index, v.Result))
                        _a.Chips(house, spot, b.Amount * RouletteBets.Multiplier(b.Index), _board.Cell * 0.22f, _resultsAt, 0.6);
                    else
                        _a.Chips(spot, house, b.Amount, _board.Cell * 0.22f, _resultsAt + 0.1, 0.55);
                }
                if (seat.RoundNet > 0)
                {
                    foreach (var b in seat.Bets.Where(b => RouletteBets.Covers(b.Index, v.Result)))
                        _a.Chips(_board.SpotOf(RouletteBets.All[b.Index].Mask), plate, b.Amount * (RouletteBets.Multiplier(b.Index) + 1), _board.Cell * 0.22f, _resultsAt + 1.1, 0.6);
                    _a.Float(plate - new Vector2(0, S(42)), $"+{seat.RoundNet:N0}", Pal.GoldLight, _resultsAt + 1.4);
                }
                if (!s.IsSeated || seat.Seat != s.MySeat) continue;
                if (seat.RoundNet > 0)
                {
                    _a.Announce("YOU WIN", $"{seat.RoundNet:N0} chips on {v.Result}", Pal.Gold, _resultsAt + 0.2);
                    _a.Confetti(plate - new Vector2(0, S(60)), seat.RoundNet >= rules.MinBet * 30 ? 110 : 55, _resultsAt + 0.4);
                }
            }
        }
    }
}
