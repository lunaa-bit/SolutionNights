using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.Core.Cards;

namespace SaucerNights.Ui;

/// <summary>Where a tracked card should be drawn this frame.</summary>
public readonly record struct CardPose(Vector2 Pos, float Flip, bool Visible, bool Arrived);

/// <summary>
/// Turns snapshot changes into motion. Cards are tracked by a stable id: a new id is dealt from the shoe (queued so a
/// deal plays one card at a time), a hidden card that becomes visible flips over, a moved card glides, and a card that
/// disappears is swept to the discard pile. Also runs chip flights, banners, confetti, floating text and number tickers.
/// </summary>
public sealed class TableAnimator
{
    private sealed class Tracked
    {
        public Card Card;
        public Vector2 From;
        public Vector2 Target;
        public double Start;
        public double MoveStart;
        public double Duration;
        public double FlipStart = double.NaN;
        public float Height;
        public int Frame;
    }

    private sealed record Ghost(Card Card, Vector2 From, Vector2 To, double Start, float Height);

    private sealed record Flight(Vector2 From, Vector2 To, long Amount, float Radius, double Start, double Duration);

    private sealed class Particle
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Angle;
        public float Spin;
        public Vector4 Color;
        public double Start;
        public double Life;
        public Vector2 Size;
    }

    private sealed record FloatText(Vector2 Pos, string Text, Vector4 Color, double Start, float Size);

    private sealed record Banner(string Title, string Sub, Vector4 Color, double Start, double Duration);

    private sealed class Ticker
    {
        public double From;
        public long To;
        public double Start;
    }

    private static readonly Vector4[] ConfettiColors =
        [Pal.Gold, Pal.GoldLight, Pal.Accent, Pal.AccentLight, Pal.Magenta, Pal.Violet, Pal.White, Pal.Green];

    private readonly Dictionary<string, Tracked> _cards = new();
    private readonly List<Ghost> _ghosts = [];
    private readonly List<Flight> _flights = [];
    private readonly List<Particle> _particles = [];
    private readonly List<FloatText> _floats = [];
    private readonly List<Banner> _banners = [];
    private readonly Dictionary<string, Ticker> _tickers = new();
    private readonly Random _rng = new();
    private double _dealCursor;
    private double _revealCursor;
    private int _frame;

    public float Speed { get; set; } = 1f;

    public Vector2 Discard { get; set; }

    /// <summary>Vertical position of banners as a fraction of the scene height.</summary>
    public float BannerY { get; set; } = 0.42f;

    private double T(double seconds) => seconds / Math.Max(0.25f, Speed);

    public void Reset()
    {
        _cards.Clear();
        _ghosts.Clear();
        _flights.Clear();
        _particles.Clear();
        _floats.Clear();
        _banners.Clear();
        _tickers.Clear();
        _dealCursor = _revealCursor = 0;
    }

    public void BeginFrame() => _frame++;

    /// <summary>When every queued deal and flip will have finished.</summary>
    public double QuietAt(double now) => Math.Max(now, Math.Max(_dealCursor, _revealCursor)) + T(0.35);

    /// <summary>Tracks a card and returns where/how to draw it.</summary>
    public CardPose Card(string id, Card card, Vector2 target, float height, Vector2 origin, double now, double gap = 0.13)
    {
        if (!_cards.TryGetValue(id, out var t))
        {
            var start = Math.Max(now, _dealCursor);
            _dealCursor = start + T(gap);
            t = new Tracked
            {
                Card = card,
                From = origin,
                Target = target,
                Start = start,
                MoveStart = start,
                Duration = T(0.42),
                Height = height,
                FlipStart = card.IsHidden ? double.NaN : start + T(0.16),
            };
            _cards[id] = t;
        }
        else
        {
            if (t.Card.IsHidden && !card.IsHidden)
            {
                var fs = Math.Max(Math.Max(now, t.MoveStart + t.Duration), _revealCursor);
                _revealCursor = fs + T(0.3);
                t.FlipStart = fs;
            }
            else if (!t.Card.IsHidden && card.IsHidden)
            {
                t.FlipStart = double.NaN;
            }
            t.Card = card;
            if (Vector2.DistanceSquared(target, t.Target) > 1)
            {
                t.From = PosOf(t, now);
                t.Target = target;
                t.MoveStart = Math.Max(now, t.Start);
                t.Duration = T(0.32);
            }
            t.Height = height;
        }
        t.Frame = _frame;
        return new CardPose(PosOf(t, now), FlipOf(t, now), now >= t.Start, now >= t.MoveStart + t.Duration);
    }

    private static Vector2 PosOf(Tracked t, double now)
    {
        if (now <= t.MoveStart) return t.From;
        var k = Ease.OutCubic(Ease.Clamp01((now - t.MoveStart) / t.Duration));
        return Vector2.Lerp(t.From, t.Target, k);
    }

    private float FlipOf(Tracked t, double now)
    {
        if (t.Card.IsHidden) return 0;
        if (double.IsNaN(t.FlipStart)) return 1;
        return Ease.InOutCubic(Ease.Clamp01((now - t.FlipStart) / T(0.32)));
    }

    /// <summary>Cards not drawn this frame are swept to the discard point.</summary>
    public void EndFrame(double now)
    {
        foreach (var (id, t) in _cards.Where(kv => kv.Value.Frame != _frame).ToList())
        {
            _cards.Remove(id);
            if (now >= t.Start) _ghosts.Add(new Ghost(t.Card, PosOf(t, now), Discard, now + _rng.NextDouble() * T(0.15), t.Height));
        }
    }

    // ───────────── Effects ─────────────

    public void Chips(Vector2 from, Vector2 to, long amount, float radius, double start, double duration = 0.55)
    {
        if (amount > 0) _flights.Add(new Flight(from, to, amount, radius, start, T(duration)));
    }

    public void Confetti(Vector2 origin, int count, double start, float spread = 1f)
    {
        for (var i = 0; i < count; i++)
        {
            var a = -MathF.PI / 2 + (float)(_rng.NextDouble() - 0.5) * 2.2f;
            var v = (float)(_rng.NextDouble() * 360 + 160) * UiEnv.Scale * spread;
            _particles.Add(new Particle
            {
                Pos = origin,
                Vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * v,
                Angle = (float)(_rng.NextDouble() * MathF.Tau),
                Spin = (float)(_rng.NextDouble() - 0.5) * 14,
                Color = ConfettiColors[_rng.Next(ConfettiColors.Length)],
                Start = start + _rng.NextDouble() * 0.15,
                Life = 1.8 + _rng.NextDouble() * 0.8,
                Size = new Vector2(3 + (float)_rng.NextDouble() * 3, 1.5f + (float)_rng.NextDouble() * 2) * UiEnv.Scale,
            });
        }
    }

    public void Float(Vector2 pos, string text, Vector4 color, double start, float size = 0)
        => _floats.Add(new FloatText(pos, text, color, start, size > 0 ? size : UiEnv.S(22)));

    public void Announce(string title, string sub, Vector4 color, double start, double duration = 2.4)
    {
        _banners.RemoveAll(b => b.Start + b.Duration < start);
        _banners.Add(new Banner(title, sub, color, Math.Max(start, _banners.Count > 0 ? _banners[^1].Start + _banners[^1].Duration * 0.8 : start), T(duration)));
    }

    /// <summary>Animated number that counts towards <paramref name="value"/>.</summary>
    public long Count(string key, long value, double now)
    {
        if (!_tickers.TryGetValue(key, out var t))
        {
            _tickers[key] = new Ticker { From = value, To = value, Start = now };
            return value;
        }
        var current = CurrentTick(t, now);
        if (t.To != value)
        {
            t.From = current;
            t.To = value;
            t.Start = now;
        }
        return (long)Math.Round(CurrentTick(t, now));
    }

    private double CurrentTick(Ticker t, double now)
    {
        var k = Ease.OutCubic(Ease.Clamp01((now - t.Start) / T(0.7)));
        return t.From + (t.To - t.From) * k;
    }

    // ───────────── Drawing ─────────────

    /// <summary>Things that live on the felt (under seats and UI): swept cards and flying chips.</summary>
    public void DrawFeltLayer(ImDrawListPtr dl, double now)
    {
        _ghosts.RemoveAll(g => now > g.Start + T(0.45));
        foreach (var g in _ghosts)
        {
            var k = Ease.Clamp01((now - g.Start) / T(0.45));
            var p = Vector2.Lerp(g.From, g.To, Ease.InOutCubic(k));
            CardArt.Draw(dl, p, g.Height * (1 - 0.35f * k), Core.Cards.Card.Hidden, 0, 1 - k * k);
        }

        _flights.RemoveAll(f => now > f.Start + f.Duration);
        foreach (var f in _flights)
        {
            if (now < f.Start) continue;
            var k = Ease.InOutCubic(Ease.Clamp01((now - f.Start) / f.Duration));
            var p = Vector2.Lerp(f.From, f.To, k) - new Vector2(0, MathF.Sin(k * MathF.PI) * Vector2.Distance(f.From, f.To) * 0.12f);
            ChipArt.Stack(dl, p, f.Amount, f.Radius, 1f, 2);
        }
    }

    /// <summary>Overlay effects: confetti, floating numbers and banners across <paramref name="area"/>.</summary>
    public void DrawOverlay(ImDrawListPtr dl, Vector2 areaMin, Vector2 areaMax, double now)
    {
        var dt = UiEnv.Dt;
        _particles.RemoveAll(p => now > p.Start + p.Life);
        foreach (var p in _particles)
        {
            if (now < p.Start) continue;
            p.Vel += new Vector2(0, 520 * UiEnv.Scale) * dt;
            p.Vel *= 1 - 0.9f * dt;
            p.Pos += p.Vel * dt;
            p.Angle += p.Spin * dt;
            var age = (now - p.Start) / p.Life;
            var alpha = age > 0.7 ? (float)(1 - (age - 0.7) / 0.3) : 1f;
            var wobble = MathF.Abs(MathF.Cos(p.Angle * 1.7f));
            Gfx.RotatedRect(dl, p.Pos, new Vector2(p.Size.X * (0.3f + 0.7f * wobble), p.Size.Y), p.Angle, Pal.U(p.Color, alpha));
        }

        _floats.RemoveAll(f => now > f.Start + T(1.8));
        foreach (var f in _floats)
        {
            if (now < f.Start) continue;
            var k = Ease.Clamp01((now - f.Start) / T(1.8));
            var alpha = k < 0.15f ? k / 0.15f : k > 0.7f ? 1 - (k - 0.7f) / 0.3f : 1;
            var pos = f.Pos - new Vector2(0, Ease.OutCubic(k) * UiEnv.S(46));
            Gfx.TextCentered(dl, FontRole.Heading, f.Size, pos, Pal.U(f.Color, alpha), f.Text, 0.8f);
        }

        _banners.RemoveAll(b => now > b.Start + b.Duration);
        var banner = _banners.FirstOrDefault(b => now >= b.Start);
        if (banner != null) DrawBanner(dl, areaMin, areaMax, banner, now);
    }

    private void DrawBanner(ImDrawListPtr dl, Vector2 min, Vector2 max, Banner b, double now)
    {
        var t = (now - b.Start) / b.Duration;
        var appear = Ease.Clamp01((now - b.Start) / T(0.35));
        var fade = t > 0.82 ? 1 - (float)((t - 0.82) / 0.18) : 1f;
        var scale = 0.6f + 0.4f * Ease.OutBack(appear);
        var alpha = Math.Min(appear * 1.5f, 1) * fade;

        var c = new Vector2((min.X + max.X) / 2, min.Y + (max.Y - min.Y) * BannerY);
        var titleSize = UiEnv.S(58) * scale;
        var ts = Gfx.TextSize(FontRole.Display, titleSize, b.Title);
        var bandH = titleSize * (b.Sub.Length > 0 ? 1.75f : 1.35f);
        var bandW = Math.Min(max.X - min.X, Math.Max(ts.X * 1.6f, UiEnv.S(380)));
        var bMin = new Vector2(c.X - bandW / 2, c.Y - bandH / 2);
        var bMax = new Vector2(c.X + bandW / 2, c.Y + bandH / 2);

        // Band fading out at both ends.
        var edge = bandW * 0.22f;
        var dark = Pal.Hex(0x060A1C, 0.88f * alpha);
        var clear = dark with { W = 0 };
        dl.AddRectFilledMultiColor(bMin, new Vector2(bMin.X + edge, bMax.Y), Pal.U(clear), Pal.U(dark), Pal.U(dark), Pal.U(clear));
        dl.AddRectFilled(new Vector2(bMin.X + edge, bMin.Y), new Vector2(bMax.X - edge, bMax.Y), Pal.U(dark));
        dl.AddRectFilledMultiColor(new Vector2(bMax.X - edge, bMin.Y), bMax, Pal.U(dark), Pal.U(clear), Pal.U(clear), Pal.U(dark));
        var line = Pal.A(b.Color, alpha);
        foreach (var y in new[] { bMin.Y, bMax.Y })
        {
            dl.AddRectFilledMultiColor(new Vector2(bMin.X, y - 1), new Vector2(c.X, y + 1), Pal.U(line with { W = 0 }), Pal.U(line), Pal.U(line), Pal.U(line with { W = 0 }));
            dl.AddRectFilledMultiColor(new Vector2(c.X, y - 1), new Vector2(bMax.X, y + 1), Pal.U(line), Pal.U(line with { W = 0 }), Pal.U(line with { W = 0 }), Pal.U(line));
        }

        var titleY = b.Sub.Length > 0 ? c.Y - bandH * 0.12f : c.Y;
        Gfx.TextGlow(dl, FontRole.Display, titleSize, new Vector2(c.X, titleY), b.Color, b.Title, 1.5f, alpha);
        if (b.Sub.Length > 0)
            Gfx.TextCentered(dl, FontRole.Heading, UiEnv.S(22) * scale, new Vector2(c.X, c.Y + bandH * 0.3f), Pal.U(Pal.Text, 0.92f * alpha), b.Sub);
    }
}
