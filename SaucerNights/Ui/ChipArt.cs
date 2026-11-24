using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SaucerNights.Ui;

/// <summary>Casino chips: denominations, colours, single chips and perspective stacks.</summary>
public static class ChipArt
{
    public readonly record struct Denom(long Value, string Label, Vector4 Body, Vector4 Stripe, Vector4 Ink);

    public static readonly Denom[] Denoms =
    [
        new(1, "1", Pal.Hex(0xEDEDED), Pal.Hex(0x2F6FDB), Pal.Hex(0x1F2B44)),
        new(5, "5", Pal.Hex(0xC9302C), Pal.Hex(0xF4F1EA), Pal.Hex(0xFFFFFF)),
        new(25, "25", Pal.Hex(0x1E8C4C), Pal.Hex(0xF4F1EA), Pal.Hex(0xFFFFFF)),
        new(100, "100", Pal.Hex(0x22252B), Pal.Hex(0xE8E2D2), Pal.Hex(0xFFFFFF)),
        new(500, "500", Pal.Hex(0x6B36AB), Pal.Hex(0xF4F1EA), Pal.Hex(0xFFFFFF)),
        new(1_000, "1K", Pal.Hex(0xE2AE3A), Pal.Hex(0x3A2A0B), Pal.Hex(0x2A1E06)),
        new(5_000, "5K", Pal.Hex(0xD3457B), Pal.Hex(0xF4F1EA), Pal.Hex(0xFFFFFF)),
        new(25_000, "25K", Pal.Hex(0x179F9C), Pal.Hex(0xF4F1EA), Pal.Hex(0xFFFFFF)),
        new(100_000, "100K", Pal.Hex(0xE2702A), Pal.Hex(0xF4F1EA), Pal.Hex(0xFFFFFF)),
        new(1_000_000, "1M", Pal.Hex(0xB9C0CB), Pal.Hex(0x2E333B), Pal.Hex(0x1E2227)),
    ];

    private static readonly Vector2[] Unit = Enumerable.Range(0, 28).Select(i => new Vector2(MathF.Cos(i * MathF.Tau / 28), MathF.Sin(i * MathF.Tau / 28))).ToArray();
    private static readonly Vector2[] Buf = new Vector2[64];

    public static Denom For(long value)
    {
        var d = Denoms[0];
        foreach (var x in Denoms)
            if (x.Value <= value) d = x;
        return d;
    }

    /// <summary>Greedy break-down of an amount into chips (most valuable first).</summary>
    public static List<(Denom Denom, int Count)> Breakdown(long amount, int maxKinds = 4)
    {
        var result = new List<(Denom, int)>();
        for (var i = Denoms.Length - 1; i >= 0 && amount > 0; i--)
        {
            var d = Denoms[i];
            var n = amount / d.Value;
            if (n <= 0) continue;
            result.Add((d, (int)Math.Min(n, 999)));
            amount -= n * d.Value;
            if (result.Count == maxKinds) break;
        }
        return result;
    }

    /// <summary>A chip seen from above, with edge spots and a printed value.</summary>
    public static void Top(ImDrawListPtr dl, Vector2 c, float r, Denom d, float alpha = 1f, bool label = true)
    {
        dl.AddCircleFilled(c + new Vector2(0, r * 0.12f), r * 1.02f, Pal.U(Pal.Black, 0.45f * alpha), 32);
        dl.AddCircleFilled(c, r, Pal.U(d.Body, alpha), 32);
        var stripe = Pal.U(d.Stripe, alpha);
        for (var i = 0; i < 6; i++)
        {
            var a = i * MathF.Tau / 6 + 0.26f;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            var perp = new Vector2(-dir.Y, dir.X);
            var o = c + dir * r * 0.83f;
            dl.AddQuadFilled(o + perp * r * 0.16f - dir * r * 0.15f, o + perp * r * 0.16f + dir * r * 0.16f,
                o - perp * r * 0.16f + dir * r * 0.16f, o - perp * r * 0.16f - dir * r * 0.15f, stripe);
        }
        dl.AddCircle(c, r * 0.66f, Pal.U(d.Stripe, 0.55f * alpha), 32, Math.Max(1, r * 0.06f));
        dl.AddCircleFilled(c, r * 0.58f, Pal.U(Pal.Lighten(d.Body, 0.08f), alpha), 32);
        dl.AddCircle(c, r, Pal.U(Pal.Darken(d.Body, 0.35f), alpha), 32, Math.Max(1, r * 0.05f));
        if (label && r > UiEnv.S(8))
            Gfx.TextCentered(dl, FontRole.Heading, r * 0.78f, c + new Vector2(0, r * 0.02f), Pal.U(d.Ink, alpha), d.Label);
    }

    private static void EllipseFill(ImDrawListPtr dl, Vector2 c, float rx, float ry, uint col)
    {
        for (var i = 0; i < Unit.Length; i++) Buf[i] = c + new Vector2(Unit[i].X * rx, Unit[i].Y * ry);
        Gfx.FillConvex(dl, Buf.AsSpan(0, Unit.Length), col);
    }

    /// <summary>One column of chips in 3/4 view, bottom chip resting at <paramref name="baseCenter"/>.</summary>
    public static void Column(ImDrawListPtr dl, Vector2 baseCenter, float r, Denom d, int count, float alpha = 1f)
    {
        var ry = r * 0.45f;
        var th = r * 0.2f;
        count = Math.Clamp(count, 1, 14);
        EllipseFill(dl, baseCenter + new Vector2(r * 0.08f, ry * 0.35f), r * 1.08f, ry * 1.1f, Pal.U(Pal.Black, 0.35f * alpha));
        var side = Pal.Darken(d.Body, 0.25f);
        for (var i = 0; i < count; i++)
        {
            var c = baseCenter - new Vector2(0, i * th);
            // Edge band.
            dl.AddRectFilled(c - new Vector2(r, th), c + new Vector2(r, 0), Pal.U(side, alpha));
            EllipseFill(dl, c, r, ry, Pal.U(side, alpha));
            for (var k = -2; k <= 2; k++)
            {
                var x = c.X + k * r * 0.42f;
                dl.AddRectFilled(new Vector2(x - r * 0.08f, c.Y - th + 1), new Vector2(x + r * 0.08f, c.Y + ry * (1 - MathF.Abs(k) * 0.18f) - th * 0.3f), Pal.U(d.Stripe, 0.85f * alpha));
            }
            // Top face.
            var top = c - new Vector2(0, th);
            EllipseFill(dl, top, r, ry, Pal.U(d.Body, alpha));
            if (i == count - 1)
            {
                for (var k = 0; k < 6; k++)
                {
                    var a = k * MathF.Tau / 6 + 0.3f;
                    var p = top + new Vector2(MathF.Cos(a) * r * 0.8f, MathF.Sin(a) * ry * 0.8f);
                    EllipseFill(dl, p, r * 0.13f, ry * 0.15f, Pal.U(d.Stripe, alpha));
                }
                EllipseFill(dl, top, r * 0.55f, ry * 0.55f, Pal.U(Pal.Lighten(d.Body, 0.1f), alpha));
                for (var k = 0; k < Unit.Length; k++) Buf[k] = top + new Vector2(Unit[k].X * r * 0.55f, Unit[k].Y * ry * 0.55f);
                Gfx.Outline(dl, Buf.AsSpan(0, Unit.Length), Pal.U(d.Stripe, 0.6f * alpha), 1f);
            }
        }
    }

    /// <summary>
    /// Draws chips worth <paramref name="amount"/> as side-by-side columns centred on <paramref name="baseCenter"/>.
    /// Returns the drawn width.
    /// </summary>
    public static float Stack(ImDrawListPtr dl, Vector2 baseCenter, long amount, float r, float alpha = 1f, int maxKinds = 3)
    {
        if (amount <= 0) return 0;
        var parts = Breakdown(amount, maxKinds);
        var gap = r * 2.15f;
        var width = gap * (parts.Count - 1);
        for (var i = 0; i < parts.Count; i++)
        {
            var (d, n) = parts[parts.Count - 1 - i];
            Column(dl, baseCenter + new Vector2(-width / 2 + i * gap, 0), r, d, Math.Min(n, 10), alpha);
        }
        return width + r * 2;
    }

    /// <summary>Height a stack of this amount reaches above its base.</summary>
    public static float StackHeight(long amount, float r)
    {
        var tallest = Breakdown(amount, 3).Select(p => Math.Min(p.Count, 10)).DefaultIfEmpty(0).Max();
        return tallest * r * 0.2f + r * 0.5f;
    }

    /// <summary>Amount pill: a dark rounded label with a mini chip.</summary>
    public static void Pill(ImDrawListPtr dl, Vector2 center, long amount, float size, float alpha = 1f, Vector4? accent = null)
    {
        var text = Gfx.Chips(amount);
        var ts = Gfx.TextSize(FontRole.Body, size, text);
        var h = size * 1.45f;
        var w = ts.X + h * 1.35f;
        var min = center - new Vector2(w / 2, h / 2);
        var max = center + new Vector2(w / 2, h / 2);
        dl.AddRectFilled(min, max, Pal.U(Pal.Hex(0x0B0E12), 0.82f * alpha), h / 2);
        dl.AddRect(min, max, Pal.U(accent ?? Pal.Gold, 0.45f * alpha), h / 2, ImDrawFlags.None, 1f);
        Top(dl, new Vector2(min.X + h / 2, center.Y), h * 0.34f, For(amount), alpha, label: false);
        dl.AddText(UiEnv.Font(FontRole.Body, size), size, new Vector2(min.X + h * 0.95f, center.Y - ts.Y / 2), Pal.U(Pal.Text, alpha), text);
    }
}
