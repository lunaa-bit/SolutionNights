using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;

namespace SaucerNights.Ui;

/// <summary>Felt, rails, the city behind the table, and table furniture.</summary>
public static class TableArt
{
    private static readonly Vector2[] A = new Vector2[400];
    private static readonly Vector2[] B = new Vector2[400];

    private static float S(float v) => UiEnv.S(v);

    /// <summary>Felt centre and edge colours, the printing colour and the rail's neon.</summary>
    public static (Vector4 Center, Vector4 Edge, Vector4 Print, Vector4 Neon) Felt(FeltStyle s) => s switch
    {
        FeltStyle.Ultraviolet => (Pal.Hex(0x4B2384), Pal.Hex(0x12072A), Pal.Hex(0xF3D9FF), Pal.Magenta),
        FeltStyle.Gilded => (Pal.Hex(0x3B2C12), Pal.Hex(0x0E0A05), Pal.Hex(0xFFE9B8), Pal.Gold),
        FeltStyle.Emerald => (Pal.Hex(0x1C7A4E), Pal.Hex(0x05291A), Pal.Hex(0xDDF8E8), Pal.Green),
        FeltStyle.Crimson => (Pal.Hex(0x84203A), Pal.Hex(0x22060E), Pal.Hex(0xFFE0E6), Pal.Red),
        _ => (Pal.Hex(0x0F5373), Pal.Hex(0x04101F), Pal.Hex(0xD3F6FF), Pal.Accent),
    };

    private static readonly Vector4 RailTop = Pal.Hex(0x232B4D);
    private static readonly Vector4 RailBottom = Pal.Hex(0x080B19);

    /// <summary>Night sky over Solution Nine: gradient, glow, a distant skyline and a light grid floor.</summary>
    public static void Backdrop(ImDrawListPtr dl, Vector2 min, Vector2 max, FeltStyle style = FeltStyle.Neon, double time = 0, float city = 1f)
    {
        var neon = Felt(style).Neon;
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        dl.AddRectFilledMultiColor(min, max, Pal.U(Pal.Hex(0x0B1030)), Pal.U(Pal.Hex(0x0B1030)), Pal.U(Pal.Hex(0x04050D)), Pal.U(Pal.Hex(0x04050D)));
        var horizon = min.Y + h * 0.3f;
        // City glow along the horizon.
        var c = new Vector2((min.X + max.X) / 2, horizon);
        var n = Gfx.Ellipse(A, c, new Vector2(w * 0.7f, h * 0.32f), 48);
        Gfx.FillRadial(dl, A.AsSpan(0, n), c, Pal.U(Pal.Mix(neon, Pal.Violet, 0.5f), 0.16f), Pal.U(Pal.Violet, 0f));
        dl.PushClipRect(min, max, true);
        Gfx.Skyline(dl, min.X, max.X, horizon, Math.Min(h * 0.2f, S(140)), 9, 0.4f * city, time);
        Gfx.Skyline(dl, min.X, max.X, horizon + S(4), Math.Min(h * 0.12f, S(84)), 21, 0.7f * city, time);
        dl.PopClipRect();
        dl.AddRectFilledMultiColor(new Vector2(min.X, horizon), max, Pal.U(Pal.Hex(0x070A1C)), Pal.U(Pal.Hex(0x070A1C)), Pal.U(Pal.Hex(0x030409)), Pal.U(Pal.Hex(0x030409)));
        Gfx.NeonLine(dl, new Vector2(min.X, horizon), new Vector2(max.X, horizon), Pal.A(neon, 0.22f), 1f, 0.6f);
        // Perspective floor grid fading towards the viewer.
        var vp = new Vector2(c.X, horizon - h * 0.08f);
        for (var i = -12; i <= 12; i++)
        {
            var bx = c.X + i * w * 0.11f;
            var a = 0.05f * (1 - Math.Abs(i) / 13f);
            var t = (horizon - vp.Y) / (max.Y - vp.Y);
            dl.AddLine(Vector2.Lerp(vp, new Vector2(bx, max.Y), t), new Vector2(bx, max.Y), Pal.U(neon, a), 1f);
        }
        for (var k = 1; k <= 9; k++)
        {
            var t = MathF.Pow(k / 9f, 2.2f);
            var y = horizon + (max.Y - horizon) * t;
            dl.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), Pal.U(neon, 0.025f + 0.04f * t), 1f);
        }
    }

    /// <summary>Oval poker table. Returns the felt's inner rectangle.</summary>
    public static (Vector2 Min, Vector2 Max) PokerTable(ImDrawListPtr dl, Vector2 c, Vector2 half, FeltStyle style, double time = 0)
    {
        var (_, _, _, neon) = Felt(style);
        var rail = Math.Min(half.X, half.Y) * 0.13f;
        var oMin = c - half;
        var oMax = c + half;

        // Shadow and under-glow.
        var n = Gfx.Stadium(A, oMin + new Vector2(0, S(14)), oMax + new Vector2(0, S(24)));
        Gfx.FillRadial(dl, A.AsSpan(0, n), c + new Vector2(0, S(18)), Pal.U(Pal.Black, 0.6f), Pal.U(Pal.Black, 0f));
        n = Gfx.Stadium(A, oMin - new Vector2(S(6)), oMax + new Vector2(S(6)));
        Gfx.NeonOutline(dl, A.AsSpan(0, n), Pal.A(neon, 0.18f), S(4), 1.4f);

        // Rail: dark glass with an outer magenta hairline and an inner neon tube.
        n = Gfx.Stadium(A, oMin, oMax);
        Gfx.FillVertical(dl, A.AsSpan(0, n), RailTop, RailBottom);
        Gfx.Outline(dl, A.AsSpan(0, n), Pal.U(Pal.Magenta, 0.55f), S(1.2f));
        var m = Gfx.Stadium(B, oMin + new Vector2(rail * 0.3f), oMax - new Vector2(rail * 0.3f));
        Gfx.Outline(dl, B.AsSpan(0, m), Pal.U(Pal.White, 0.06f), S(1.5f));

        var fMin = oMin + new Vector2(rail);
        var fMax = oMax - new Vector2(rail);
        m = Gfx.Stadium(B, fMin - new Vector2(S(3)), fMax + new Vector2(S(3)));
        Gfx.FillConvex(dl, B.AsSpan(0, m), Pal.U(Pal.Hex(0x02030A)));
        var pulse = 0.85f + 0.15f * MathF.Sin((float)time * 1.3f);
        Gfx.NeonOutline(dl, B.AsSpan(0, m), Pal.A(neon, pulse), S(1.8f), 1f);
        Felted(dl, fMin, fMax, style);
        return (fMin, fMax);
    }

    private static void Felted(ImDrawListPtr dl, Vector2 fMin, Vector2 fMax, FeltStyle style)
    {
        var (center, edge, print, neon) = Felt(style);
        var c = (fMin + fMax) / 2;
        var n = Gfx.Stadium(A, fMin, fMax);
        Gfx.FillRadial(dl, A.AsSpan(0, n), c - new Vector2(0, (fMax.Y - fMin.Y) * 0.08f), Pal.U(center), Pal.U(edge));
        // Hex lattice, strongest in the middle.
        var hexMin = fMin + (fMax - fMin) * 0.12f;
        var hexMax = fMax - (fMax - fMin) * 0.12f;
        Gfx.HexGrid(dl, hexMin, hexMax, Math.Max(S(14), (fMax.Y - fMin.Y) * 0.06f), Pal.U(print, 0.035f));
        for (var i = 0; i < 4; i++)
        {
            var k = Gfx.Stadium(B, fMin + new Vector2(i * S(3)), fMax - new Vector2(i * S(3)));
            Gfx.Outline(dl, B.AsSpan(0, k), Pal.U(Pal.Black, 0.28f - i * 0.06f), S(3));
        }
        // Betting line.
        var inset = (fMax - fMin) * 0.11f;
        var bl = Gfx.Stadium(B, fMin + inset, fMax - inset);
        Gfx.Outline(dl, B.AsSpan(0, bl), Pal.U(neon, 0.22f), S(1.4f));
    }

    /// <summary>Wordmark printed on the felt.</summary>
    public static void FeltMark(ImDrawListPtr dl, Vector2 c, float size, FeltStyle style, string text)
    {
        var (_, _, print, neon) = Felt(style);
        var spacing = size * 0.18f;
        var ts = Gfx.TextSpacedSize(FontRole.Heading, size, text, spacing);
        var x = c.X - (ts.X + size * 1.3f) / 2;
        Logo.Mark(dl, new Vector2(x + size * 0.5f, c.Y), size * 1.05f, Pal.A(neon, 0.2f), Pal.A(print, 0.12f));
        Gfx.TextSpaced(dl, FontRole.Heading, size, new Vector2(x + size * 1.3f, c.Y - ts.Y / 2), Pal.U(print, 0.16f), text, spacing);
    }

    /// <summary>
    /// Half-moon blackjack table hanging from the dealer's edge at <paramref name="top"/>. Seats are placed along the
    /// same ellipse by the caller.
    /// </summary>
    public static void BlackjackTable(ImDrawListPtr dl, Vector2 top, Vector2 radii, FeltStyle style, string pays, string rule, string insurance, double time = 0)
    {
        var (center, edge, print, neon) = Felt(style);
        var rail = radii.Y * 0.07f;
        var outer = radii + new Vector2(rail);

        // Shadow and under-glow.
        var n = Gfx.Ellipse(A, top + new Vector2(0, S(16)), outer, 80, 0, MathF.PI);
        Gfx.FillRadial(dl, A.AsSpan(0, n), top + new Vector2(0, S(16)), Pal.U(Pal.Black, 0.55f), Pal.U(Pal.Black, 0));
        n = Gfx.Ellipse(A, top, outer + new Vector2(S(6)), 80, 0, MathF.PI);
        Gfx.NeonOutline(dl, A.AsSpan(0, n), Pal.A(neon, 0.18f), S(4), 1.4f, closed: false);

        // Rail (half ring).
        n = Gfx.Ellipse(A, top, outer, 80, 0, MathF.PI);
        Gfx.FillVertical(dl, A.AsSpan(0, n), RailTop, RailBottom);
        Gfx.Outline(dl, A.AsSpan(0, n), Pal.U(Pal.Magenta, 0.55f), S(1.2f), closed: false);

        // Felt.
        n = Gfx.Ellipse(A, top, radii + new Vector2(S(3)), 80, 0, MathF.PI);
        Gfx.FillConvex(dl, A.AsSpan(0, n), Pal.U(Pal.Hex(0x02030A)));
        var pulse = 0.85f + 0.15f * MathF.Sin((float)time * 1.3f);
        Gfx.NeonOutline(dl, A.AsSpan(0, n), Pal.A(neon, pulse), S(1.8f), 1f, closed: false);
        n = Gfx.Ellipse(A, top, radii, 80, 0, MathF.PI);
        Gfx.FillRadial(dl, A.AsSpan(0, n), top + new Vector2(0, radii.Y * 0.35f), Pal.U(center), Pal.U(edge));
        var hexMin = top - new Vector2(radii.X * 0.8f, 0);
        var hexMax = top + new Vector2(radii.X * 0.8f, radii.Y * 0.85f);
        dl.PushClipRect(hexMin, hexMax, true);
        Gfx.HexGrid(dl, hexMin, hexMax, Math.Max(S(14), radii.Y * 0.05f), Pal.U(print, 0.03f));
        dl.PopClipRect();
        for (var i = 0; i < 4; i++)
        {
            var k = Gfx.Ellipse(B, top, radii - new Vector2(i * S(3)), 80, 0.02f, MathF.PI - 0.02f);
            Gfx.Outline(dl, B.AsSpan(0, k), Pal.U(Pal.Black, 0.28f - i * 0.06f), S(3), closed: false);
        }

        // Dealer's edge: a straight glass bar with a neon underline.
        var backMin = new Vector2(top.X - outer.X, top.Y - rail * 0.7f);
        var backMax = new Vector2(top.X + outer.X, top.Y + S(2));
        var cut = (backMax.Y - backMin.Y) * 0.9f;
        Gfx.ChamferPanel(dl, backMin, backMax, cut, RailTop, RailBottom, Pal.A(Pal.Magenta, 0.45f), S(1.2f), all: true);
        Gfx.NeonLine(dl, new Vector2(backMin.X + cut, backMax.Y), new Vector2(backMax.X - cut, backMax.Y), Pal.A(neon, 0.85f), S(1.5f), 0.8f);

        // Printing, on gentle arcs centred well above the table.
        var arcC = top - new Vector2(0, radii.Y * 1.6f);
        float R(float depth) => radii.Y * 1.6f + radii.Y * depth;
        var ink = Pal.U(print, 0.34f);
        var paysSize = Math.Max(S(14), radii.Y * 0.058f);
        Gfx.TextOnArc(dl, FontRole.Heading, paysSize, arcC, R(0.45f), MathF.PI / 2, pays, Pal.U(Pal.Lighten(neon, 0.3f), 0.42f), 1.18f);
        Gfx.TextOnArc(dl, FontRole.Body, Math.Max(S(12), radii.Y * 0.034f), arcC, R(0.51f), MathF.PI / 2, rule, ink, 1.04f);

        // Insurance band.
        var insSize = Math.Max(S(11), radii.Y * 0.03f);
        var span = Gfx.ArcSpan(FontRole.Body, insSize, R(0.61f), insurance, 1.2f) / 2 + 0.06f;
        var k0 = Gfx.Ellipse(A, arcC, new Vector2(R(0.575f)), 48, MathF.PI / 2 - span, MathF.PI / 2 + span);
        var k1 = Gfx.Ellipse(B, arcC, new Vector2(R(0.645f)), 48, MathF.PI / 2 - span, MathF.PI / 2 + span);
        Gfx.Outline(dl, A.AsSpan(0, k0), Pal.U(neon, 0.3f), S(1.2f), closed: false);
        Gfx.Outline(dl, B.AsSpan(0, k1), Pal.U(neon, 0.3f), S(1.2f), closed: false);
        Gfx.TextOnArc(dl, FontRole.Body, insSize, arcC, R(0.61f), MathF.PI / 2, insurance, ink, 1.2f);
    }

    /// <summary>Dealer's chip tray.</summary>
    public static void ChipTray(ImDrawListPtr dl, Vector2 center, Vector2 size)
    {
        var min = center - size / 2;
        var max = center + size / 2;
        Gfx.Shadow(dl, min, max, S(4), S(6), 0.45f, new Vector2(0, S(3)));
        Gfx.ChamferPanel(dl, min, max, S(6), Pal.Hex(0x161D38), Pal.Hex(0x070A16), Pal.A(Pal.Accent, 0.35f), S(1.2f), all: true);
        var cols = 8;
        var cw = (size.X - S(12)) / cols;
        for (var i = 0; i < cols; i++)
        {
            var d = ChipArt.Denoms[Math.Min(ChipArt.Denoms.Length - 1, cols - 1 - i)];
            var x = min.X + S(6) + cw * (i + 0.5f);
            var colMin = new Vector2(x - cw * 0.42f, min.Y + S(5));
            var colMax = new Vector2(x + cw * 0.42f, max.Y - S(5));
            dl.AddRectFilled(colMin, colMax, Pal.U(Pal.Darken(d.Body, 0.25f)), S(2));
            for (var y = colMin.Y + S(3); y < colMax.Y - S(2); y += S(4))
                dl.AddLine(new Vector2(colMin.X + S(1), y), new Vector2(colMax.X - S(1), y), Pal.U(d.Body, 0.9f), S(1.6f));
            dl.AddRectFilledMultiColor(colMin, new Vector2(colMax.X, colMin.Y + (colMax.Y - colMin.Y) * 0.4f),
                Pal.U(Pal.White, 0.12f), Pal.U(Pal.White, 0.12f), Pal.U(Pal.White, 0f), Pal.U(Pal.White, 0f));
        }
    }

    /// <summary>The card shoe (or deck) with a card back showing.</summary>
    public static void Shoe(ImDrawListPtr dl, Vector2 center, float cardHeight)
    {
        var size = new Vector2(cardHeight * 0.95f, cardHeight * 0.62f);
        var min = center - size / 2;
        var max = center + size / 2;
        Gfx.Shadow(dl, min, max, S(4), S(6), 0.45f, new Vector2(0, S(3)));
        CardArt.Draw(dl, center + new Vector2(-size.X * 0.08f, -size.Y * 0.12f), cardHeight * 0.7f, Core.Cards.Card.Hidden);
        var bMin = min + new Vector2(0, size.Y * 0.25f);
        Gfx.ChamferPanel(dl, bMin, max, S(7), Pal.Hex(0x1A2240), Pal.Hex(0x080B18), Pal.A(Pal.Accent, 0.4f), S(1.2f));
        Gfx.NeonLine(dl, new Vector2(min.X + S(10), max.Y - S(6)), new Vector2(max.X - S(10), max.Y - S(6)), Pal.A(Pal.Accent, 0.7f), S(1.2f), 0.6f);
    }

    /// <summary>Discard tray.</summary>
    public static void Discard(ImDrawListPtr dl, Vector2 center, float cardHeight)
    {
        var size = new Vector2(cardHeight * 0.8f, cardHeight * 0.55f);
        var min = center - size / 2;
        var max = center + size / 2;
        Gfx.Shadow(dl, min, max, S(4), S(5), 0.35f, new Vector2(0, S(3)));
        Gfx.ChamferPanel(dl, min, max, S(7), Pal.Hex(0x1A2240, 0.85f), Pal.Hex(0x080B18, 0.85f), Pal.A(Pal.Magenta, 0.35f), S(1.2f));
        // A few spent cards lying in the tray.
        var ch = size.Y * 0.78f;
        for (var i = 0; i < 3; i++)
            CardArt.Draw(dl, center + new Vector2((i - 1) * ch * 0.16f, -i * S(1.5f)), ch, Core.Cards.Card.Hidden, 0, 0.85f);
    }
}

/// <summary>The Solution Nights mark: a neon hexagon holding a diamond, with four sparks.</summary>
public static class Logo
{
    public static void Mark(ImDrawListPtr dl, Vector2 c, float size, Vector4 neon, Vector4? fill = null)
    {
        Span<Vector2> hex = stackalloc Vector2[6];
        var r = size * 0.5f;
        Gfx.Hexagon(hex, c, r);
        if (fill is { } f) Gfx.FillConvex(dl, hex, Pal.U(f));
        Gfx.NeonOutline(dl, hex, neon, Math.Max(1f, size * 0.045f), neon.W > 0.5f ? 1f : 0.5f);
        var d = r * 0.52f;
        var col = Pal.U(Pal.Lighten(neon, 0.35f));
        dl.AddQuadFilled(c + new Vector2(0, -d), c + new Vector2(d * 0.68f, 0), c + new Vector2(0, d), c + new Vector2(-d * 0.68f, 0), col);
        dl.AddQuadFilled(c + new Vector2(0, -d * 0.45f), c + new Vector2(d * 0.3f, 0), c + new Vector2(0, d * 0.45f), c + new Vector2(-d * 0.3f, 0), Pal.U(Pal.White, 0.55f * neon.W));
        for (var i = 0; i < 4; i++)
        {
            var a = MathF.PI / 4 + i * MathF.PI / 2;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            dl.AddLine(c + dir * r * 0.62f, c + dir * r * 0.78f, Pal.U(neon, 0.8f * neon.W), Math.Max(1f, size * 0.03f));
        }
    }

    /// <summary>
    /// The full brand: mark, "SOLUTION NIGHTS" and a tagline, on one line when it fits and stacked when it doesn't.
    /// </summary>
    public static void Wordmark(ImDrawListPtr dl, Vector2 pos, float height, float maxWidth, string? tagline = "CARDS OVER YOUR LINKSHELL")
    {
        var mark = height * 1.05f;
        Mark(dl, pos + new Vector2(mark / 2, height / 2), mark, Pal.Accent, Pal.Hex(0x0B1530));
        var x = pos.X + mark + height * 0.3f;
        var avail = maxWidth - (x - pos.X);
        var size = height * 0.62f;
        var spacing = size * 0.06f;
        var a = Gfx.TextSpacedSize(FontRole.Heading, size, "SOLUTION", spacing).X;
        var b = Gfx.TextSpacedSize(FontRole.Heading, size, "NIGHTS", spacing).X;
        var tagSize = height * 0.25f;
        if (a + b + size * 0.25f <= avail)
        {
            var top = pos.Y + (tagline != null ? 0 : height * 0.2f);
            Gfx.TextSpaced(dl, FontRole.Heading, size, new Vector2(x, top - size * 0.05f), Pal.U(Pal.Text), "SOLUTION", spacing);
            Gfx.TextSpaced(dl, FontRole.Heading, size, new Vector2(x + a + size * 0.25f, top - size * 0.05f), Pal.U(Pal.Accent), "NIGHTS", spacing);
            if (tagline != null) Tagline(dl, new Vector2(x + 1, top + size * 0.95f), tagSize, avail, tagline);
            return;
        }
        // Stacked: SOLUTION over NIGHTS, beside the mark; the tagline runs underneath.
        size = height * 0.5f;
        spacing = size * 0.1f;
        var widest = Gfx.TextSpacedSize(FontRole.Heading, size, "SOLUTION", spacing).X;
        if (widest > avail) (size, spacing) = (size * avail / widest, spacing * avail / widest);
        Gfx.TextSpaced(dl, FontRole.Heading, size, new Vector2(x, pos.Y + height * 0.02f), Pal.U(Pal.Text), "SOLUTION", spacing);
        Gfx.TextSpaced(dl, FontRole.Heading, size, new Vector2(x, pos.Y + height * 0.5f), Pal.U(Pal.Accent), "NIGHTS", spacing);
        if (tagline != null) Tagline(dl, new Vector2(pos.X + 1, pos.Y + height + height * 0.22f), tagSize, maxWidth, tagline);
    }

    private static void Tagline(ImDrawListPtr dl, Vector2 p, float size, float avail, string text)
    {
        var spacing = size * 0.12f;
        var w = Gfx.TextSpacedSize(FontRole.Body, size, text, spacing).X;
        if (w > avail) (size, spacing) = (size * avail / w, spacing * avail / w);
        Gfx.TextSpaced(dl, FontRole.Body, size, p, Pal.U(Pal.Magenta, 0.9f), text, spacing);
    }
}

public readonly record struct SeatInfo(
    string Name,
    long Stack,
    bool IsMe,
    bool Active,
    float TimerFraction,
    string? Badge,
    Vector4 BadgeColor,
    bool Dim,
    bool IsHost,
    bool Away);

/// <summary>Player name plates: angular glass with a hexagon avatar and a turn timer bar.</summary>
public static class SeatArt
{
    private static float S(float v) => UiEnv.S(v);

    public static Vector2 Size => new(S(176), S(54));

    public static void Plate(ImDrawListPtr dl, Vector2 center, SeatInfo s, double now, float width = 0)
    {
        var size = width > 0 ? new Vector2(width, Size.Y) : Size;
        var min = center - size / 2;
        var max = center + size / 2;
        var cut = S(12);
        var accent = s.IsMe ? Pal.Gold : Pal.Accent;

        if (s.Active)
        {
            var pulse = 0.55f + 0.45f * MathF.Sin((float)now * 4.2f);
            Gfx.ChamferGlow(dl, min, max, cut, Pal.Accent, S(14), 0.8f + 0.5f * pulse);
        }
        Gfx.Shadow(dl, min, max, S(4), S(8), 0.55f, new Vector2(0, S(3)));
        Gfx.ChamferPanel(dl, min, max, cut, Pal.Hex(0x18224A, 0.96f), Pal.Hex(0x0A0F24, 0.96f));
        var border = s.Active ? Pal.Accent : s.IsMe ? Pal.A(Pal.Gold, 0.7f) : Pal.Hex(0x2C3A6A);
        Gfx.ChamferOutline(dl, min, max, cut, Pal.U(border), s.Active || s.IsMe ? S(1.6f) : S(1));
        // Accent along the cut corner.
        Gfx.NeonLine(dl, new Vector2(min.X, min.Y + cut), new Vector2(min.X + cut, min.Y), Pal.A(accent, s.Active ? 1f : 0.7f), S(1.6f), s.Active ? 1f : 0.4f);

        // Hexagon avatar.
        var ar = size.Y * 0.52f;
        var ac = new Vector2(min.X + ar * 0.62f + S(2), center.Y);
        var tint = Pal.FromName(s.Name);
        Span<Vector2> hex = stackalloc Vector2[6];
        Gfx.Hexagon(hex, ac + new Vector2(0, S(2)), ar + S(1));
        Gfx.FillConvex(dl, hex, Pal.U(Pal.Black, 0.5f));
        Gfx.Hexagon(hex, ac, ar);
        Gfx.FillVertical(dl, hex, Pal.Darken(tint, 0.15f), Pal.Darken(tint, 0.6f));
        Gfx.Outline(dl, hex, Pal.U(s.IsMe ? Pal.Gold : s.Active ? Pal.Accent : Pal.Hex(0x4A5A8C)), S(1.8f));
        Gfx.TextCentered(dl, FontRole.Heading, ar * 0.9f, ac + new Vector2(0, S(1)), Pal.U(Pal.White, 0.95f), Initials(s.Name), 0.6f);

        // Name and chips.
        var x = ac.X + ar + S(9);
        var maxW = max.X - x - S(8);
        var name = Fit(ShortName(s.Name), maxW - (s.IsHost ? S(16) : 0), S(15));
        dl.AddText(UiEnv.Font(FontRole.Body, S(15)), S(15), new Vector2(x, min.Y + S(7)), Pal.U(s.Away ? Pal.Muted : Pal.Text), name);
        if (s.IsHost)
        {
            var nw = Gfx.TextSize(FontRole.Body, S(15), name).X;
            Icons.Draw(dl, Icon.Crown, new Vector2(x + nw + S(10), min.Y + S(15)), S(13), Pal.Gold);
        }
        ChipArt.Top(dl, new Vector2(x + S(6), max.Y - S(16)), S(6), ChipArt.For(Math.Max(1, s.Stack)), 1, false);
        dl.AddText(UiEnv.Font(FontRole.Body, S(15)), S(15), new Vector2(x + S(16), max.Y - S(25)), Pal.U(s.Stack > 0 ? Pal.GoldLight : Pal.Muted), s.Stack.ToString("N0"));

        // Turn timer: a bar along the bottom edge.
        if (s.Active && s.TimerFraction is > 0 and <= 1)
        {
            var col = s.TimerFraction > 0.5f ? Pal.Accent : s.TimerFraction > 0.25f ? Pal.Gold : Pal.Red;
            var y = max.Y - S(2.5f);
            var x0 = min.X + S(4);
            var x1 = max.X - cut - S(2);
            dl.AddLine(new Vector2(x0, y), new Vector2(x1, y), Pal.U(Pal.Black, 0.5f), S(3));
            Gfx.NeonLine(dl, new Vector2(x0, y), new Vector2(x0 + (x1 - x0) * s.TimerFraction, y), col, S(2.5f), 0.8f);
        }

        if (s.Dim) Gfx.ChamferPanel(dl, min, max, cut, Pal.A(Pal.Black, 0.48f), Pal.A(Pal.Black, 0.48f));

        if (!string.IsNullOrEmpty(s.Badge))
        {
            var fs = S(12);
            var ts = Gfx.TextSize(FontRole.Body, fs, s.Badge);
            var bc = new Vector2(center.X + S(16), min.Y - S(2));
            var bMin = bc - new Vector2(ts.X / 2 + S(9), ts.Y / 2 + S(3));
            var bMax = bc + new Vector2(ts.X / 2 + S(9), ts.Y / 2 + S(3));
            Gfx.ChamferPanel(dl, bMin, bMax, (bMax.Y - bMin.Y) * 0.32f, Pal.Mix(s.BadgeColor, Pal.Black, 0.45f), Pal.Mix(s.BadgeColor, Pal.Black, 0.65f),
                Pal.A(s.BadgeColor, 0.9f), 1f, all: true);
            dl.AddText(UiEnv.Font(FontRole.Body, fs), fs, bc - ts / 2, Pal.U(Pal.White), s.Badge);
        }
    }

    /// <summary>A faint plate for an empty seat; returns true when clicked (if clickable).</summary>
    public static bool Empty(ImDrawListPtr dl, Vector2 center, string label, bool clickable, string id, float width = 0)
    {
        var size = new Vector2((width > 0 ? width : Size.X) * 0.78f, Size.Y * 0.8f);
        var min = center - size / 2;
        var max = center + size / 2;
        var hovered = false;
        var clicked = false;
        if (clickable)
        {
            ImGui.SetCursorScreenPos(min);
            clicked = ImGui.InvisibleButton(id, size);
            hovered = ImGui.IsItemHovered();
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        var cut = S(10);
        Gfx.ChamferPanel(dl, min, max, cut, Pal.A(Pal.Black, hovered ? 0.5f : 0.32f), Pal.A(Pal.Black, hovered ? 0.5f : 0.32f));
        Gfx.ChamferOutline(dl, min, max, cut, Pal.U(hovered ? Pal.Accent : Pal.Muted, hovered ? 0.85f : 0.3f), S(1.2f));
        if (hovered) Gfx.ChamferGlow(dl, min, max, cut, Pal.Accent, S(8), 0.7f);
        var text = label.ToUpperInvariant();
        var fs = S(12.5f);
        var ts = Gfx.TextSpacedSize(FontRole.Body, fs, text, S(1));
        Gfx.TextSpaced(dl, FontRole.Body, fs, center - ts / 2, Pal.U(hovered ? Pal.AccentLight : Pal.Muted, 0.85f), text, S(1));
        return clicked;
    }

    public static string ShortName(string id)
    {
        var at = id.IndexOf('@');
        return at > 0 ? id[..at] : id;
    }

    public static string Initials(string id)
    {
        var parts = ShortName(id).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}",
        };
    }

    public static string Fit(string text, float width, float size)
    {
        if (Gfx.TextSize(FontRole.Body, size, text).X <= width) return text;
        for (var n = text.Length - 1; n > 1; n--)
        {
            var t = text[..n].TrimEnd() + "...";
            if (Gfx.TextSize(FontRole.Body, size, t).X <= width) return t;
        }
        return text[..1];
    }
}
