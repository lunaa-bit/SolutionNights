using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SaucerNights.Ui;

public enum FontRole
{
    Body,
    Heading,
    Display,
    Card,
}

/// <summary>Host-provided environment: UI scale and fonts (Dalamud in game, TTFs in the preview tool).</summary>
public static class UiEnv
{
    public static float Scale = 1f;

    /// <summary>
    /// Returns the best font for a role drawn at a pixel size, or null to fall back to the current font. Fonts are baked
    /// at a few sizes because text drawn far below its baked size loses thin strokes.
    /// </summary>
    public static Func<FontRole, float, ImFontPtr?> FontProvider = (_, _) => null;

    public static ImFontPtr Font(FontRole role, float size = 0) => FontProvider(role, size) ?? ImGui.GetFont();

    public static float S(float v) => v * Scale;

    public static double Now => ImGui.GetTime();

    public static float Dt => Math.Clamp(ImGui.GetIO().DeltaTime, 0.0001f, 0.1f);
}

/// <summary>
/// Colour palette: Solution Nine after dark. Deep indigo glass, cyan and magenta neon, gold kept for money.
/// </summary>
public static class Pal
{
    public static readonly Vector4 Bg = Hex(0x070A16);
    public static readonly Vector4 Panel = Hex(0x0C1124);
    public static readonly Vector4 Panel2 = Hex(0x121A32);
    public static readonly Vector4 Panel3 = Hex(0x1B2444);
    public static readonly Vector4 Line = Hex(0x263258);
    public static readonly Vector4 Text = Hex(0xEAF1FF);
    public static readonly Vector4 Muted = Hex(0x93A1C8);
    public static readonly Vector4 Faint = Hex(0x5E6A93);

    /// <summary>The interface accent: Solution Nine cyan.</summary>
    public static readonly Vector4 Accent = Hex(0x3DE0FF);
    public static readonly Vector4 AccentLight = Hex(0xB4F3FF);
    public static readonly Vector4 AccentDeep = Hex(0x0C7FB0);
    public static readonly Vector4 Magenta = Hex(0xFF4FCB);
    public static readonly Vector4 Violet = Hex(0x8C6CFF);

    /// <summary>Money: chips, stacks, winnings.</summary>
    public static readonly Vector4 Gold = Hex(0xFFC857);
    public static readonly Vector4 GoldLight = Hex(0xFFE6A6);
    public static readonly Vector4 GoldDark = Hex(0xB27A16);
    public static readonly Vector4 Green = Hex(0x35E6A1);
    public static readonly Vector4 Red = Hex(0xFF4D6D);
    public static readonly Vector4 Blue = Hex(0x56A8FF);
    public static readonly Vector4 Orange = Hex(0xFF9F45);
    public static readonly Vector4 Purple = Hex(0xA98BFF);
    public static readonly Vector4 White = new(1, 1, 1, 1);
    public static readonly Vector4 Black = new(0, 0, 0, 1);

    public static Vector4 Hex(uint rgb, float a = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    public static uint U(Vector4 c) =>
        (uint)Math.Clamp((int)(c.X * 255 + 0.5f), 0, 255)
        | (uint)Math.Clamp((int)(c.Y * 255 + 0.5f), 0, 255) << 8
        | (uint)Math.Clamp((int)(c.Z * 255 + 0.5f), 0, 255) << 16
        | (uint)Math.Clamp((int)(c.W * 255 + 0.5f), 0, 255) << 24;

    public static uint U(Vector4 c, float alpha) => U(c with { W = c.W * alpha });

    public static Vector4 A(Vector4 c, float alpha) => c with { W = c.W * alpha };

    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, Math.Clamp(t, 0, 1));

    public static Vector4 Lighten(Vector4 c, float t) => new(c.X + (1 - c.X) * t, c.Y + (1 - c.Y) * t, c.Z + (1 - c.Z) * t, c.W);

    public static Vector4 Darken(Vector4 c, float t) => new(c.X * (1 - t), c.Y * (1 - t), c.Z * (1 - t), c.W);

    /// <summary>A pleasant colour derived from a name (avatars).</summary>
    public static Vector4 FromName(string name)
    {
        var h = 0u;
        foreach (var ch in name) h = h * 31 + ch;
        var hue = h % 360 / 360f;
        return Hsv(hue, 0.55f, 0.78f);
    }

    public static Vector4 Hsv(float h, float s, float v)
    {
        var i = (int)(h * 6);
        var f = h * 6 - i;
        float p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        var (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return new Vector4(r, g, b, 1);
    }
}

/// <summary>Easing curves.</summary>
public static class Ease
{
    public static float Clamp01(double t) => (float)Math.Clamp(t, 0, 1);

    public static float OutCubic(float t) => 1 - MathF.Pow(1 - t, 3);

    public static float InOutCubic(float t) => t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;

    public static float OutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }

    public static float OutQuad(float t) => 1 - (1 - t) * (1 - t);

    /// <summary>Exponential approach to a target, frame-rate independent.</summary>
    public static float Approach(float current, float target, float speed) =>
        current + (target - current) * (1 - MathF.Exp(-speed * UiEnv.Dt));
}

/// <summary>Draw-list helpers: gradients, rounded shapes, shadows, glows and text.</summary>
public static class Gfx
{
    private static readonly Vector2[] Buf = new Vector2[512];

    public static float S(float v) => UiEnv.S(v);

    // ───────────── Shapes ─────────────

    public static int Ellipse(Span<Vector2> buf, Vector2 c, Vector2 r, int n, float a0 = 0, float a1 = MathF.PI * 2)
    {
        var closed = Math.Abs(a1 - a0 - MathF.PI * 2) < 1e-4f;
        var count = closed ? n : n + 1;
        for (var i = 0; i < count; i++)
        {
            var a = a0 + (a1 - a0) * i / n;
            buf[i] = c + new Vector2(MathF.Cos(a) * r.X, MathF.Sin(a) * r.Y);
        }
        return count;
    }

    public static int RoundedRect(Span<Vector2> buf, Vector2 min, Vector2 max, float r, int seg = 6)
    {
        r = Math.Min(r, Math.Min((max.X - min.X) / 2, (max.Y - min.Y) / 2));
        var n = 0;
        Span<Vector2> centers = [new(max.X - r, min.Y + r), new(max.X - r, max.Y - r), new(min.X + r, max.Y - r), new(min.X + r, min.Y + r)];
        for (var k = 0; k < 4; k++)
        {
            var start = -MathF.PI / 2 + k * MathF.PI / 2;
            for (var i = 0; i <= seg; i++)
            {
                var a = start + MathF.PI / 2 * i / seg;
                buf[n++] = centers[k] + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            }
        }
        return n;
    }

    /// <summary>A stadium (pill) shape filling the rectangle.</summary>
    public static int Stadium(Span<Vector2> buf, Vector2 min, Vector2 max, int seg = 28) =>
        RoundedRect(buf, min, max, Math.Min(max.X - min.X, max.Y - min.Y) / 2, seg);

    /// <summary>Fan-fills a convex polygon with a colour at <paramref name="center"/> blending to the edge colour.</summary>
    public static void FillRadial(ImDrawListPtr dl, ReadOnlySpan<Vector2> pts, Vector2 center, uint centerCol, uint edgeCol)
    {
        var n = pts.Length;
        if (n < 3) return;
        var uv = ImGui.GetFontTexUvWhitePixel();
        dl.PrimReserve(n * 3, n + 1);
        var b = dl.VtxCurrentIdx;
        dl.PrimWriteVtx(center, uv, centerCol);
        for (var i = 0; i < n; i++) dl.PrimWriteVtx(pts[i], uv, edgeCol);
        for (var i = 0; i < n; i++)
        {
            dl.PrimWriteIdx((ushort)b);
            dl.PrimWriteIdx((ushort)(b + 1 + i));
            dl.PrimWriteIdx((ushort)(b + 1 + (i + 1) % n));
        }
    }

    /// <summary>Fills a convex polygon with a vertical gradient.</summary>
    public static void FillVertical(ImDrawListPtr dl, ReadOnlySpan<Vector2> pts, Vector4 top, Vector4 bottom)
    {
        var n = pts.Length;
        if (n < 3) return;
        float y0 = float.MaxValue, y1 = float.MinValue;
        var c = Vector2.Zero;
        foreach (var p in pts)
        {
            y0 = Math.Min(y0, p.Y);
            y1 = Math.Max(y1, p.Y);
            c += p;
        }
        c /= n;
        var h = Math.Max(1, y1 - y0);
        var uv = ImGui.GetFontTexUvWhitePixel();
        dl.PrimReserve(n * 3, n + 1);
        var b = dl.VtxCurrentIdx;
        dl.PrimWriteVtx(c, uv, Pal.U(Vector4.Lerp(top, bottom, (c.Y - y0) / h)));
        for (var i = 0; i < n; i++) dl.PrimWriteVtx(pts[i], uv, Pal.U(Vector4.Lerp(top, bottom, (pts[i].Y - y0) / h)));
        for (var i = 0; i < n; i++)
        {
            dl.PrimWriteIdx((ushort)b);
            dl.PrimWriteIdx((ushort)(b + 1 + i));
            dl.PrimWriteIdx((ushort)(b + 1 + (i + 1) % n));
        }
    }

    public static void Outline(ImDrawListPtr dl, ReadOnlySpan<Vector2> pts, uint col, float thickness, bool closed = true)
    {
        if (pts.Length < 2) return;
        dl.AddPolyline(ref MemoryMarshalRef(pts), pts.Length, col, closed ? ImDrawFlags.Closed : ImDrawFlags.None, thickness);
    }

    public static void FillConvex(ImDrawListPtr dl, ReadOnlySpan<Vector2> pts, uint col)
    {
        if (pts.Length < 3) return;
        dl.AddConvexPolyFilled(ref MemoryMarshalRef(pts), pts.Length, col);
    }

    private static ref Vector2 MemoryMarshalRef(ReadOnlySpan<Vector2> s) => ref System.Runtime.InteropServices.MemoryMarshal.GetReference(s);

    /// <summary>Rounded rectangle with a vertical gradient (anti-aliased edge).</summary>
    public static void RoundedGradient(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, Vector4 top, Vector4 bottom)
    {
        var n = RoundedRect(Buf, min, max, r, r > S(8) ? 8 : 5);
        FillVertical(dl, Buf.AsSpan(0, n), top, bottom);
        Outline(dl, Buf.AsSpan(0, n), Pal.U(Vector4.Lerp(top, bottom, 0.5f), 0.9f), 1f);
    }

    /// <summary>Soft drop shadow made of stacked translucent rounded rectangles.</summary>
    public static void Shadow(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, float spread, float alpha = 0.35f, Vector2 offset = default)
    {
        const int layers = 5;
        for (var i = layers; i >= 1; i--)
        {
            var g = spread * i / layers;
            dl.AddRectFilled(min - new Vector2(g) + offset, max + new Vector2(g) + offset, Pal.U(Pal.Black, alpha / layers * (1.2f - i / (float)layers)), r + g);
        }
    }

    /// <summary>Glow around a rectangle.</summary>
    public static void Glow(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, Vector4 color, float spread, float strength = 1f)
    {
        const int layers = 6;
        for (var i = layers; i >= 1; i--)
        {
            var g = spread * i / layers;
            var a = strength * 0.22f * (1 - (i - 1) / (float)layers);
            dl.AddRect(min - new Vector2(g), max + new Vector2(g), Pal.U(color, a), r + g, ImDrawFlags.None, spread / layers * 1.6f);
        }
    }

    public static void GlowCircle(ImDrawListPtr dl, Vector2 c, float radius, Vector4 color, float spread, float strength = 1f)
    {
        const int layers = 6;
        for (var i = layers; i >= 1; i--)
        {
            var g = spread * i / layers;
            var a = strength * 0.22f * (1 - (i - 1) / (float)layers);
            dl.AddCircle(c, radius + g, Pal.U(color, a), 48, spread / layers * 1.6f);
        }
    }

    /// <summary>Progress arc (e.g. a turn timer), clockwise from the top.</summary>
    public static void Arc(ImDrawListPtr dl, Vector2 c, float radius, float fraction, uint col, float thickness)
    {
        if (fraction <= 0) return;
        var a0 = -MathF.PI / 2;
        var a1 = a0 + MathF.PI * 2 * Math.Clamp(fraction, 0, 1);
        dl.PathClear();
        dl.PathArcTo(c, radius, a0, a1, Math.Max(8, (int)(48 * fraction)));
        dl.PathStroke(col, ImDrawFlags.None, thickness);
    }

    /// <summary>A rotated filled rectangle (confetti).</summary>
    public static void RotatedRect(ImDrawListPtr dl, Vector2 c, Vector2 half, float angle, uint col)
    {
        var (s, co) = MathF.SinCos(angle);
        Vector2 R(float x, float y) => c + new Vector2(x * co - y * s, x * s + y * co);
        dl.AddQuadFilled(R(-half.X, -half.Y), R(half.X, -half.Y), R(half.X, half.Y), R(-half.X, half.Y), col);
    }

    // ───────────── Text ─────────────

    public static Vector2 TextSize(ImFontPtr font, float size, string text)
    {
        return ImGui.CalcTextSizeA(font, size, float.MaxValue, 0, text, out _);
    }

    public static Vector2 TextSize(FontRole role, float size, string text) => TextSize(UiEnv.Font(role, size), size, text);

    public static void Text(ImDrawListPtr dl, FontRole role, float size, Vector2 pos, uint col, string text) =>
        dl.AddText(UiEnv.Font(role, size), size, pos, col, text);

    /// <summary>Text centred on a point, with an optional soft shadow.</summary>
    public static Vector2 TextCentered(ImDrawListPtr dl, FontRole role, float size, Vector2 center, uint col, string text, float shadow = 0)
    {
        var font = UiEnv.Font(role, size);
        var sz = TextSize(font, size, text);
        var p = center - sz / 2;
        if (shadow > 0)
        {
            var sc = Pal.U(Pal.Black, shadow * ((col >> 24) / 255f));
            dl.AddText(font, size, p + new Vector2(0, Math.Max(1, size * 0.06f)), sc, text);
        }
        dl.AddText(font, size, p, col, text);
        return sz;
    }

    /// <summary>Text with a dark outline, readable on any background.</summary>
    public static void TextOutlined(ImDrawListPtr dl, FontRole role, float size, Vector2 pos, uint col, uint outline, string text, float width = 1.5f)
    {
        var font = UiEnv.Font(role, size);
        for (var i = 0; i < 8; i++)
        {
            var a = i * MathF.PI / 4;
            dl.AddText(font, size, pos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * width, outline, text);
        }
        dl.AddText(font, size, pos, col, text);
    }

    /// <summary>Letters placed along a circular arc (glyphs stay upright), for felt printing.</summary>
    public static void TextOnArc(ImDrawListPtr dl, FontRole role, float size, Vector2 center, float radius, float centerAngle, string text, uint col, float letterSpacing = 1.15f)
    {
        var font = UiEnv.Font(role, size);
        var widths = text.Select(ch => TextSize(font, size, ch.ToString()).X * letterSpacing).ToArray();
        var total = widths.Sum();
        // Angles grow clockwise on screen; walk from the left end (larger angle when the arc hangs below the centre).
        var angle = centerAngle + total / 2 / radius;
        for (var i = 0; i < text.Length; i++)
        {
            var a = angle - widths[i] / 2 / radius;
            var p = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            var sz = TextSize(font, size, text[i].ToString());
            dl.AddText(font, size, p - sz / 2, col, text[i].ToString());
            angle -= widths[i] / radius;
        }
    }

    /// <summary>Arc length (radians) a string occupies on a circle of the given radius.</summary>
    public static float ArcSpan(FontRole role, float size, float radius, string text, float letterSpacing = 1.15f) =>
        text.Sum(ch => TextSize(role, size, ch.ToString()).X * letterSpacing) / radius;

    // ───────────── Solution Nine shapes ─────────────

    /// <summary>
    /// A rectangle with its top-left and bottom-right corners cut at 45 degrees (the angular Solution Nine panel). With
    /// <paramref name="all"/> every corner is cut.
    /// </summary>
    public static int Chamfer(Span<Vector2> buf, Vector2 min, Vector2 max, float cut, bool all = false)
    {
        cut = Math.Max(0, Math.Min(cut, Math.Min((max.X - min.X) / 2, (max.Y - min.Y) / 2)));
        var n = 0;
        buf[n++] = new Vector2(min.X + cut, min.Y);
        if (all)
        {
            buf[n++] = new Vector2(max.X - cut, min.Y);
            buf[n++] = new Vector2(max.X, min.Y + cut);
        }
        else
        {
            buf[n++] = new Vector2(max.X, min.Y);
        }
        buf[n++] = new Vector2(max.X, max.Y - cut);
        buf[n++] = new Vector2(max.X - cut, max.Y);
        if (all)
        {
            buf[n++] = new Vector2(min.X + cut, max.Y);
            buf[n++] = new Vector2(min.X, max.Y - cut);
        }
        else
        {
            buf[n++] = new Vector2(min.X, max.Y);
        }
        buf[n++] = new Vector2(min.X, min.Y + cut);
        return n;
    }

    /// <summary>Chamfered panel with a vertical gradient and an optional border.</summary>
    public static void ChamferPanel(ImDrawListPtr dl, Vector2 min, Vector2 max, float cut, Vector4 top, Vector4 bottom, Vector4? border = null,
        float thickness = 1f, bool all = false)
    {
        Span<Vector2> pts = stackalloc Vector2[8];
        var n = Chamfer(pts, min, max, cut, all);
        FillVertical(dl, pts[..n], top, bottom);
        if (border is { } b) Outline(dl, pts[..n], Pal.U(b), thickness);
    }

    /// <summary>Outline of a chamfered rectangle.</summary>
    public static void ChamferOutline(ImDrawListPtr dl, Vector2 min, Vector2 max, float cut, uint col, float thickness, bool all = false)
    {
        Span<Vector2> pts = stackalloc Vector2[8];
        var n = Chamfer(pts, min, max, cut, all);
        Outline(dl, pts[..n], col, thickness);
    }

    /// <summary>Soft neon glow around a chamfered rectangle (stacked translucent outlines).</summary>
    public static void ChamferGlow(ImDrawListPtr dl, Vector2 min, Vector2 max, float cut, Vector4 color, float spread, float strength = 1f, bool all = false)
    {
        const int layers = 5;
        Span<Vector2> pts = stackalloc Vector2[8];
        for (var i = layers; i >= 1; i--)
        {
            var g = spread * i / layers;
            var n = Chamfer(pts, min - new Vector2(g), max + new Vector2(g), cut + g * 0.41f, all);
            Outline(dl, pts[..n], Pal.U(color, strength * 0.2f * (1 - (i - 1) / (float)layers)), spread / layers * 1.7f);
        }
    }

    /// <summary>A line drawn as a neon tube: a bright core with a soft halo.</summary>
    public static void NeonLine(ImDrawListPtr dl, Vector2 a, Vector2 b, Vector4 color, float thickness, float glow = 1f)
    {
        if (glow > 0)
        {
            dl.AddLine(a, b, Pal.U(color, 0.08f * glow), thickness * 6);
            dl.AddLine(a, b, Pal.U(color, 0.16f * glow), thickness * 3);
        }
        dl.AddLine(a, b, Pal.U(color), thickness);
        dl.AddLine(a, b, Pal.U(Pal.Lighten(color, 0.6f), 0.8f * color.W), Math.Max(1f, thickness * 0.4f));
    }

    /// <summary>A closed or open polyline drawn as a neon tube.</summary>
    public static void NeonOutline(ImDrawListPtr dl, ReadOnlySpan<Vector2> pts, Vector4 color, float thickness, float glow = 1f, bool closed = true)
    {
        if (glow > 0)
        {
            Outline(dl, pts, Pal.U(color, 0.07f * glow), thickness * 7, closed);
            Outline(dl, pts, Pal.U(color, 0.14f * glow), thickness * 3.2f, closed);
        }
        Outline(dl, pts, Pal.U(color), thickness, closed);
        Outline(dl, pts, Pal.U(Pal.Lighten(color, 0.65f), 0.75f * color.W), Math.Max(1f, thickness * 0.4f), closed);
    }

    /// <summary>A regular hexagon (pointy top) around <paramref name="c"/>.</summary>
    public static int Hexagon(Span<Vector2> buf, Vector2 c, float r, float rotation = -MathF.PI / 2)
    {
        for (var i = 0; i < 6; i++)
        {
            var a = rotation + i * MathF.Tau / 6;
            buf[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
        }
        return 6;
    }

    /// <summary>A faint hexagon lattice over a rectangle (clipped), for felts and backdrops.</summary>
    public static void HexGrid(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, uint col, float thickness = 1f)
    {
        if (r < 3) return;
        dl.PushClipRect(min, max, true);
        var w = MathF.Sqrt(3) * r;
        var rowH = r * 1.5f;
        var row = 0;
        Span<Vector2> hex = stackalloc Vector2[6];
        for (var y = min.Y - r; y < max.Y + r; y += rowH, row++)
        {
            var x0 = min.X - w + (row % 2 == 1 ? w / 2 : 0);
            for (var x = x0; x < max.X + w; x += w)
            {
                // Three edges per cell draw the whole lattice once.
                Hexagon(hex, new Vector2(x, y), r);
                dl.AddLine(hex[3], hex[4], col, thickness);
                dl.AddLine(hex[4], hex[5], col, thickness);
                dl.AddLine(hex[5], hex[0], col, thickness);
            }
        }
        dl.PopClipRect();
    }

    /// <summary>
    /// A night skyline standing on <paramref name="baseY"/>: towers of varied height with lit windows and neon roof
    /// lines. Deterministic for a given seed, so it never flickers.
    /// </summary>
    public static void Skyline(ImDrawListPtr dl, float x0, float x1, float baseY, float height, int seed, float alpha = 1f, double time = 0)
    {
        if (x1 <= x0 || height <= 4) return;
        var rng = new Random(seed);
        var x = x0 - rng.NextSingle() * height * 0.3f;
        var body = Pal.U(Pal.Hex(0x0A0F22), alpha);
        var edge = Pal.U(Pal.Hex(0x1A2448), alpha);
        Vector4[] lights = [Pal.Accent, Pal.Magenta, Pal.Gold, Pal.Violet];
        while (x < x1)
        {
            var w = height * (0.12f + rng.NextSingle() * 0.22f);
            var h = height * (0.25f + MathF.Pow(rng.NextSingle(), 1.6f) * 0.75f);
            var min = new Vector2(x, baseY - h);
            var max = new Vector2(x + w, baseY);
            dl.AddRectFilled(min, max, body);
            dl.AddLine(min, new Vector2(min.X, max.Y), edge, 1f);
            var light = lights[rng.Next(lights.Length)];
            var blink = rng.NextSingle() * 6;
            if (rng.NextSingle() < 0.25f)
            {
                // A spire with a beacon.
                var sx = min.X + w * 0.5f;
                dl.AddTriangleFilled(new Vector2(min.X + w * 0.3f, min.Y), new Vector2(sx, min.Y - h * 0.18f), new Vector2(min.X + w * 0.7f, min.Y), body);
                var beacon = 0.45f + 0.55f * MathF.Max(0, MathF.Sin((float)time * 1.7f + blink));
                dl.AddCircleFilled(new Vector2(sx, min.Y - h * 0.18f), Math.Max(1.2f, w * 0.05f), Pal.U(Pal.Red, alpha * beacon), 8);
            }
            else
            {
                dl.AddLine(min, new Vector2(max.X, min.Y), Pal.U(light, 0.75f * alpha), Math.Max(1f, height * 0.012f));
            }
            var cell = Math.Max(3f, height * 0.045f);
            for (var wy = min.Y + cell; wy < max.Y - cell * 0.5f; wy += cell * 1.6f)
            {
                for (var wx = min.X + cell * 0.6f; wx < max.X - cell * 0.6f; wx += cell * 1.3f)
                {
                    if (rng.NextSingle() > 0.3f) continue;
                    var lc = rng.NextSingle() < 0.7f ? light : Pal.Hex(0xFFF2D0);
                    dl.AddRectFilled(new Vector2(wx, wy), new Vector2(wx + cell * 0.6f, wy + cell * 0.45f), Pal.U(lc, (0.22f + rng.NextSingle() * 0.4f) * alpha));
                }
            }
            x += w + height * (rng.NextSingle() < 0.3f ? 0.04f : 0.005f);
        }
    }

    /// <summary>Text with extra space between letters (captions, felt printing). Returns the drawn size.</summary>
    public static Vector2 TextSpaced(ImDrawListPtr dl, FontRole role, float size, Vector2 pos, uint col, string text, float spacing)
    {
        var font = UiEnv.Font(role, size);
        var x = pos.X;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            dl.AddText(font, size, new Vector2(x, pos.Y), col, s);
            x += TextSize(font, size, s).X + spacing;
        }
        return new Vector2(x - pos.X - (text.Length > 0 ? spacing : 0), TextSize(font, size, text).Y);
    }

    public static Vector2 TextSpacedSize(FontRole role, float size, string text, float spacing)
    {
        var font = UiEnv.Font(role, size);
        var w = text.Sum(ch => TextSize(font, size, ch.ToString()).X) + spacing * Math.Max(0, text.Length - 1);
        return new Vector2(w, TextSize(font, size, text).Y);
    }

    /// <summary>Neon text: soft offset copies under a crisp, slightly lightened top layer.</summary>
    public static Vector2 TextGlow(ImDrawListPtr dl, FontRole role, float size, Vector2 center, Vector4 col, string text, float glow = 1f, float alpha = 1f)
    {
        var font = UiEnv.Font(role, size);
        var sz = TextSize(font, size, text);
        var p = center - sz / 2;
        var r = Math.Max(1.5f, size * 0.06f);
        for (var i = 0; i < 8; i++)
        {
            var a = i * MathF.PI / 4;
            dl.AddText(font, size, p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r, Pal.U(col, 0.13f * glow * alpha), text);
        }
        dl.AddText(font, size, p, Pal.U(Pal.Lighten(col, 0.45f), alpha), text);
        return sz;
    }

    public static string Chips(long v) => v switch
    {
        >= 10_000_000 => $"{v / 1_000_000.0:0.#}M",
        >= 100_000 => $"{v / 1000.0:0.#}K",
        _ => v.ToString("N0"),
    };
}
