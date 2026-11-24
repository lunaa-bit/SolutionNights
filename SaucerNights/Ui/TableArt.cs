using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;

namespace SaucerNights.Ui;

/// <summary>Felt, rails and table furniture.</summary>
public static class TableArt
{
    private static readonly Vector2[] A = new Vector2[400];
    private static readonly Vector2[] B = new Vector2[400];

    private static float S(float v) => UiEnv.S(v);

    public static (Vector4 Center, Vector4 Edge, Vector4 Print) Felt(FeltStyle s) => s switch
    {
        FeltStyle.Sapphire => (Pal.Hex(0x2A62A6), Pal.Hex(0x0B2246), Pal.Hex(0xD7E6FF)),
        FeltStyle.Crimson => (Pal.Hex(0x9B2738), Pal.Hex(0x3A0A13), Pal.Hex(0xFFE0E4)),
        FeltStyle.Amethyst => (Pal.Hex(0x6A35A0), Pal.Hex(0x22103A), Pal.Hex(0xEDE0FF)),
        FeltStyle.Onyx => (Pal.Hex(0x3B414B), Pal.Hex(0x101216), Pal.Hex(0xE6E8EC)),
        _ => (Pal.Hex(0x23875A), Pal.Hex(0x093A24), Pal.Hex(0xDDF5E6)),
    };

    private static readonly Vector4 WoodLight = Pal.Hex(0x6B4428);
    private static readonly Vector4 WoodDark = Pal.Hex(0x2A170C);
    private static readonly Vector4 Trim = Pal.Hex(0xC9A24A);

    /// <summary>Dark vignette behind the table.</summary>
    public static void Backdrop(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        dl.AddRectFilled(min, max, Pal.U(Pal.Hex(0x090B0F)));
        var c = (min + max) / 2;
        var n = Gfx.Ellipse(A, c, (max - min) * 0.62f, 48);
        Gfx.FillRadial(dl, A.AsSpan(0, n), c, Pal.U(Pal.Hex(0x1A212B)), Pal.U(Pal.Hex(0x090B0F)));
    }

    /// <summary>Oval poker table. Returns the felt's inner rectangle.</summary>
    public static (Vector2 Min, Vector2 Max) PokerTable(ImDrawListPtr dl, Vector2 c, Vector2 half, FeltStyle style)
    {
        var rail = Math.Min(half.X, half.Y) * 0.15f;
        var oMin = c - half;
        var oMax = c + half;

        // Shadow under the whole table.
        var n = Gfx.Stadium(A, oMin + new Vector2(0, S(14)), oMax + new Vector2(0, S(22)));
        Gfx.FillRadial(dl, A.AsSpan(0, n), c + new Vector2(0, S(18)), Pal.U(Pal.Black, 0.55f), Pal.U(Pal.Black, 0f));

        // Rail.
        n = Gfx.Stadium(A, oMin, oMax);
        Gfx.FillVertical(dl, A.AsSpan(0, n), WoodLight, WoodDark);
        Gfx.Outline(dl, A.AsSpan(0, n), Pal.U(Pal.Black, 0.6f), S(2));
        var m = Gfx.Stadium(B, oMin + new Vector2(rail * 0.18f), oMax - new Vector2(rail * 0.18f));
        Gfx.Outline(dl, B.AsSpan(0, m), Pal.U(Pal.Hex(0x8A5A36), 0.6f), S(1.5f));

        // Gold trim and felt.
        var fMin = oMin + new Vector2(rail);
        var fMax = oMax - new Vector2(rail);
        m = Gfx.Stadium(B, fMin - new Vector2(S(3)), fMax + new Vector2(S(3)));
        Gfx.FillConvex(dl, B.AsSpan(0, m), Pal.U(Pal.Hex(0x0A0806)));
        Gfx.Outline(dl, B.AsSpan(0, m), Pal.U(Trim, 0.9f), S(1.6f));
        Felted(dl, fMin, fMax, style);
        return (fMin, fMax);
    }

    private static void Felted(ImDrawListPtr dl, Vector2 fMin, Vector2 fMax, FeltStyle style)
    {
        var (center, edge, print) = Felt(style);
        var c = (fMin + fMax) / 2;
        var n = Gfx.Stadium(A, fMin, fMax);
        Gfx.FillRadial(dl, A.AsSpan(0, n), c - new Vector2(0, (fMax.Y - fMin.Y) * 0.08f), Pal.U(center), Pal.U(edge));
        // Inner shading near the rail.
        for (var i = 0; i < 4; i++)
        {
            var k = Gfx.Stadium(B, fMin + new Vector2(i * S(3)), fMax - new Vector2(i * S(3)));
            Gfx.Outline(dl, B.AsSpan(0, k), Pal.U(Pal.Black, 0.22f - i * 0.05f), S(3));
        }
        // Betting line.
        var inset = (fMax - fMin) * 0.11f;
        var bl = Gfx.Stadium(B, fMin + inset, fMax - inset);
        Gfx.Outline(dl, B.AsSpan(0, bl), Pal.U(print, 0.16f), S(1.5f));
    }

    /// <summary>Wordmark printed on the felt.</summary>
    public static void FeltMark(ImDrawListPtr dl, Vector2 c, float size, FeltStyle style, string text)
    {
        var print = Felt(style).Print;
        var ts = Gfx.TextSize(FontRole.Heading, size, text);
        var x = c.X - (ts.X + size * 1.2f) / 2;
        CardArt.Clover(dl, new Vector2(x + size * 0.45f, c.Y), size * 0.95f, Pal.A(print, 0.12f));
        Gfx.Text(dl, FontRole.Heading, size, new Vector2(x + size * 1.2f, c.Y - ts.Y / 2), Pal.U(print, 0.14f), text);
    }

    /// <summary>
    /// Half-moon blackjack table hanging from the dealer's edge at <paramref name="top"/>. Returns nothing; seats are
    /// placed along the same ellipse by the caller.
    /// </summary>
    public static void BlackjackTable(ImDrawListPtr dl, Vector2 top, Vector2 radii, FeltStyle style, string pays, string rule, string insurance)
    {
        var rail = radii.Y * 0.075f;
        var outer = radii + new Vector2(rail);

        // Table shadow.
        var n = Gfx.Ellipse(A, top + new Vector2(0, S(16)), outer, 80, 0, MathF.PI);
        Gfx.FillRadial(dl, A.AsSpan(0, n), top + new Vector2(0, S(16)), Pal.U(Pal.Black, 0.5f), Pal.U(Pal.Black, 0));

        // Rail (half ring).
        n = Gfx.Ellipse(A, top, outer, 80, 0, MathF.PI);
        Gfx.FillVertical(dl, A.AsSpan(0, n), WoodLight, WoodDark);
        Gfx.Outline(dl, A.AsSpan(0, n), Pal.U(Pal.Black, 0.6f), S(2), closed: false);

        // Felt.
        var (center, edge, print) = Felt(style);
        n = Gfx.Ellipse(A, top, radii + new Vector2(S(3)), 80, 0, MathF.PI);
        Gfx.FillConvex(dl, A.AsSpan(0, n), Pal.U(Pal.Hex(0x0A0806)));
        Gfx.Outline(dl, A.AsSpan(0, n), Pal.U(Trim, 0.9f), S(1.6f), closed: false);
        n = Gfx.Ellipse(A, top, radii, 80, 0, MathF.PI);
        Gfx.FillRadial(dl, A.AsSpan(0, n), top + new Vector2(0, radii.Y * 0.35f), Pal.U(center), Pal.U(edge));
        for (var i = 0; i < 4; i++)
        {
            var k = Gfx.Ellipse(B, top, radii - new Vector2(i * S(3)), 80, 0.02f, MathF.PI - 0.02f);
            Gfx.Outline(dl, B.AsSpan(0, k), Pal.U(Pal.Black, 0.22f - i * 0.05f), S(3), closed: false);
        }

        // Dealer's edge (straight back rail).
        var backMin = new Vector2(top.X - outer.X, top.Y - rail * 0.9f);
        var backMax = new Vector2(top.X + outer.X, top.Y + S(2));
        Gfx.RoundedGradient(dl, backMin, backMax, S(4), WoodLight, WoodDark);
        dl.AddLine(new Vector2(backMin.X, backMax.Y), backMax with { X = backMax.X }, Pal.U(Trim, 0.8f), S(1.5f));

        // Printing, on gentle arcs centred well above the table.
        var arcC = top - new Vector2(0, radii.Y * 1.6f);
        float R(float depth) => radii.Y * 1.6f + radii.Y * depth;
        var ink = Pal.U(print, 0.5f);
        var paysSize = Math.Max(S(14), radii.Y * 0.058f);
        Gfx.TextOnArc(dl, FontRole.Heading, paysSize, arcC, R(0.45f), MathF.PI / 2, pays, Pal.U(Pal.GoldLight, 0.7f), 1.18f);
        Gfx.TextOnArc(dl, FontRole.Body, Math.Max(S(12), radii.Y * 0.034f), arcC, R(0.51f), MathF.PI / 2, rule, ink, 1.04f);

        // Insurance band.
        var insSize = Math.Max(S(11), radii.Y * 0.03f);
        var span = Gfx.ArcSpan(FontRole.Body, insSize, R(0.61f), insurance, 1.2f) / 2 + 0.06f;
        var k0 = Gfx.Ellipse(A, arcC, new Vector2(R(0.575f)), 48, MathF.PI / 2 - span, MathF.PI / 2 + span);
        var k1 = Gfx.Ellipse(B, arcC, new Vector2(R(0.645f)), 48, MathF.PI / 2 - span, MathF.PI / 2 + span);
        Gfx.Outline(dl, A.AsSpan(0, k0), Pal.U(print, 0.3f), S(1.2f), closed: false);
        Gfx.Outline(dl, B.AsSpan(0, k1), Pal.U(print, 0.3f), S(1.2f), closed: false);
        Gfx.TextOnArc(dl, FontRole.Body, insSize, arcC, R(0.61f), MathF.PI / 2, insurance, ink, 1.2f);
    }

    /// <summary>Dealer's chip tray.</summary>
    public static void ChipTray(ImDrawListPtr dl, Vector2 center, Vector2 size)
    {
        var min = center - size / 2;
        var max = center + size / 2;
        Gfx.Shadow(dl, min, max, S(6), S(6), 0.4f, new Vector2(0, S(3)));
        Gfx.RoundedGradient(dl, min, max, S(6), Pal.Hex(0x1A1D22), Pal.Hex(0x0B0C0F));
        dl.AddRect(min, max, Pal.U(Pal.Hex(0x3A3F48)), S(6), ImDrawFlags.None, S(1.2f));
        var cols = 8;
        var cw = (size.X - S(12)) / cols;
        for (var i = 0; i < cols; i++)
        {
            var d = ChipArt.Denoms[Math.Min(ChipArt.Denoms.Length - 1, cols - 1 - i)];
            var x = min.X + S(6) + cw * (i + 0.5f);
            var colMin = new Vector2(x - cw * 0.42f, min.Y + S(5));
            var colMax = new Vector2(x + cw * 0.42f, max.Y - S(5));
            dl.AddRectFilled(colMin, colMax, Pal.U(Pal.Darken(d.Body, 0.25f)), S(3));
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
        Gfx.Shadow(dl, min, max, S(6), S(6), 0.45f, new Vector2(0, S(3)));
        CardArt.Draw(dl, center + new Vector2(-size.X * 0.08f, -size.Y * 0.12f), cardHeight * 0.7f, Core.Cards.Card.Hidden);
        Gfx.RoundedGradient(dl, min + new Vector2(0, size.Y * 0.25f), max, S(6), Pal.Hex(0x2B2F36), Pal.Hex(0x121418));
        dl.AddRect(min + new Vector2(0, size.Y * 0.25f), max, Pal.U(Pal.Hex(0x4A505A)), S(6), ImDrawFlags.None, S(1.2f));
        dl.AddLine(new Vector2(min.X + S(6), max.Y - S(6)), new Vector2(max.X - S(6), max.Y - S(6)), Pal.U(Trim, 0.6f), S(1.2f));
    }

    /// <summary>Discard tray with a few face-down cards.</summary>
    public static void Discard(ImDrawListPtr dl, Vector2 center, float cardHeight)
    {
        var size = new Vector2(cardHeight * 0.8f, cardHeight * 0.55f);
        var min = center - size / 2;
        var max = center + size / 2;
        Gfx.Shadow(dl, min, max, S(5), S(5), 0.35f, new Vector2(0, S(3)));
        Gfx.RoundedGradient(dl, min, max, S(5), Pal.Hex(0x2B2F36, 0.85f), Pal.Hex(0x121418, 0.85f));
        dl.AddRect(min, max, Pal.U(Pal.Hex(0x4A505A), 0.8f), S(5));
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

/// <summary>Player name plates.</summary>
public static class SeatArt
{
    private static float S(float v) => UiEnv.S(v);

    public static Vector2 Size => new(S(172), S(52));

    public static void Plate(ImDrawListPtr dl, Vector2 center, SeatInfo s, double now)
    {
        var size = Size;
        var min = center - size / 2;
        var max = center + size / 2;
        var r = S(12);

        if (s.Active)
        {
            var pulse = 0.55f + 0.45f * MathF.Sin((float)now * 4.2f);
            Gfx.Glow(dl, min, max, r, Pal.Gold, S(12), 0.7f + 0.5f * pulse);
        }
        Gfx.Shadow(dl, min, max, r, S(8), 0.5f, new Vector2(0, S(3)));
        Gfx.RoundedGradient(dl, min, max, r, Pal.Hex(0x222A35, 0.96f), Pal.Hex(0x12171D, 0.96f));
        var border = s.Active ? Pal.Gold : s.IsMe ? Pal.A(Pal.Gold, 0.65f) : Pal.Hex(0x394452);
        dl.AddRect(min, max, Pal.U(border), r, ImDrawFlags.None, s.Active || s.IsMe ? S(1.6f) : S(1));

        // Avatar.
        var ar = size.Y * 0.5f;
        var ac = new Vector2(min.X + ar * 0.55f, center.Y);
        var tint = Pal.FromName(s.Name);
        dl.AddCircleFilled(ac + new Vector2(0, S(2)), ar + S(1), Pal.U(Pal.Black, 0.45f), 32);
        dl.AddCircleFilled(ac, ar, Pal.U(Pal.Darken(tint, 0.45f)), 32);
        dl.AddCircleFilled(ac - new Vector2(ar * 0.15f, ar * 0.2f), ar * 0.75f, Pal.U(Pal.Darken(tint, 0.2f), 0.9f), 32);
        dl.AddCircle(ac, ar, Pal.U(s.IsMe ? Pal.Gold : Pal.Hex(0x4A5563)), 32, S(1.8f));
        Gfx.TextCentered(dl, FontRole.Heading, ar * 0.95f, ac + new Vector2(0, S(1)), Pal.U(Pal.White, 0.95f), Initials(s.Name), 0.6f);

        if (s.Active && s.TimerFraction is > 0 and <= 1)
        {
            var col = s.TimerFraction > 0.5f ? Pal.Green : s.TimerFraction > 0.25f ? Pal.Gold : Pal.Red;
            dl.AddCircle(ac, ar + S(4), Pal.U(Pal.Black, 0.5f), 40, S(3.5f));
            Gfx.Arc(dl, ac, ar + S(4), s.TimerFraction, Pal.U(col), S(3.5f));
        }

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
        ChipArt.Top(dl, new Vector2(x + S(6), max.Y - S(15)), S(6), ChipArt.For(Math.Max(1, s.Stack)), 1, false);
        dl.AddText(UiEnv.Font(FontRole.Body, S(15)), S(15), new Vector2(x + S(16), max.Y - S(24)), Pal.U(Pal.GoldLight), s.Stack.ToString("N0"));

        if (s.Dim) dl.AddRectFilled(min, max, Pal.U(Pal.Black, 0.45f), r);

        if (!string.IsNullOrEmpty(s.Badge))
        {
            var fs = S(12);
            var ts = Gfx.TextSize(FontRole.Body, fs, s.Badge);
            var bc = new Vector2(center.X + S(16), min.Y - S(2));
            var bMin = bc - new Vector2(ts.X / 2 + S(8), ts.Y / 2 + S(3));
            var bMax = bc + new Vector2(ts.X / 2 + S(8), ts.Y / 2 + S(3));
            dl.AddRectFilled(bMin, bMax, Pal.U(Pal.Mix(s.BadgeColor, Pal.Black, 0.35f), 0.95f), S(10));
            dl.AddRect(bMin, bMax, Pal.U(s.BadgeColor, 0.8f), S(10));
            dl.AddText(UiEnv.Font(FontRole.Body, fs), fs, bc - ts / 2, Pal.U(Pal.White), s.Badge);
        }
    }

    /// <summary>A faint plate for an empty seat; returns true when clicked (if clickable).</summary>
    public static bool Empty(ImDrawListPtr dl, Vector2 center, string label, bool clickable, string id)
    {
        var size = Size * new Vector2(0.78f, 0.8f);
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
        dl.AddRectFilled(min, max, Pal.U(Pal.Black, hovered ? 0.45f : 0.28f), size.Y / 2);
        dl.AddRect(min, max, Pal.U(hovered ? Pal.Gold : Pal.Muted, hovered ? 0.8f : 0.3f), size.Y / 2, ImDrawFlags.None, S(1.2f));
        Gfx.TextCentered(dl, FontRole.Body, S(14), center, Pal.U(hovered ? Pal.GoldLight : Pal.Muted, 0.85f), label);
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
