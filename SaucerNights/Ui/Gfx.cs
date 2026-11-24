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

/// <summary>Colour palette and helpers.</summary>
public static class Pal
{
    public static readonly Vector4 Bg = Hex(0x0C0F14);
    public static readonly Vector4 Panel = Hex(0x141920);
    public static readonly Vector4 Panel2 = Hex(0x1B222B);
    public static readonly Vector4 Panel3 = Hex(0x252E39);
    public static readonly Vector4 Line = Hex(0x2C3642);
    public static readonly Vector4 Text = Hex(0xEEF0F4);
    public static readonly Vector4 Muted = Hex(0x98A2AF);
    public static readonly Vector4 Faint = Hex(0x687280);
    public static readonly Vector4 Gold = Hex(0xE8B04A);
    public static readonly Vector4 GoldLight = Hex(0xF8DC96);
    public static readonly Vector4 GoldDark = Hex(0x9C6F1E);
    public static readonly Vector4 Green = Hex(0x3DD68C);
    public static readonly Vector4 Red = Hex(0xF2555A);
    public static readonly Vector4 Blue = Hex(0x5AB0F2);
    public static readonly Vector4 Orange = Hex(0xF59E0B);
    public static readonly Vector4 Purple = Hex(0xA78BFA);
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

    public static string Chips(long v) => v switch
    {
        >= 10_000_000 => $"{v / 1_000_000.0:0.#}M",
        >= 100_000 => $"{v / 1000.0:0.#}K",
        _ => v.ToString("N0"),
    };
}
