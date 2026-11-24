using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Cards;

namespace SaucerNights.Ui;

/// <summary>Vector-drawn playing cards: no textures, crisp at any size.</summary>
public static class CardArt
{
    public const float Aspect = 0.714f;

    public static CardBackStyle Back = CardBackStyle.Crimson;
    public static bool FourColor;

    private static readonly Vector4 Face = Pal.Hex(0xFCFAF4);
    private static readonly Vector4 FaceShade = Pal.Hex(0xEFE8D8);
    private static readonly Vector4 Edge = Pal.Hex(0xCFC6B3);
    private static readonly Vector4 RedInk = Pal.Hex(0xC8102E);
    private static readonly Vector4 BlackInk = Pal.Hex(0x17181C);
    private static readonly Vector4 GreenInk = Pal.Hex(0x13844A);
    private static readonly Vector4 BlueInk = Pal.Hex(0x1C5BC4);

    public static Vector2 Size(float height) => new(height * Aspect, height);

    public static Vector4 Ink(Suit s) => FourColor
        ? s switch { Suit.Clubs => GreenInk, Suit.Diamonds => BlueInk, Suit.Hearts => RedInk, _ => BlackInk }
        : s is Suit.Diamonds or Suit.Hearts ? RedInk : BlackInk;

    private static (Vector4 A, Vector4 B, Vector4 Trim) BackColors() => Back switch
    {
        CardBackStyle.Midnight => (Pal.Hex(0x23356B), Pal.Hex(0x0E1633), Pal.Hex(0xC9D3E8)),
        CardBackStyle.Emerald => (Pal.Hex(0x1A6B47), Pal.Hex(0x0A3020), Pal.Hex(0xE6C16A)),
        CardBackStyle.Gilded => (Pal.Hex(0x2A2116), Pal.Hex(0x120D08), Pal.Hex(0xE8B04A)),
        _ => (Pal.Hex(0x9A2335), Pal.Hex(0x4E0E19), Pal.Hex(0xE6C16A)),
    };

    /// <summary>
    /// Draws a card centred at <paramref name="center"/>. <paramref name="flip"/> runs 0 (back) → 1 (face) and squeezes
    /// the card horizontally through the middle, like turning it over.
    /// </summary>
    public static void Draw(ImDrawListPtr dl, Vector2 center, float height, Card card, float flip = 1f, float alpha = 1f,
        bool highlight = false, bool dim = false, float lift = 0)
    {
        if (alpha <= 0.01f) return;
        var faceUp = !card.IsHidden && flip >= 0.5f;
        var squeeze = MathF.Abs(MathF.Cos(MathF.PI * Math.Clamp(flip, 0, 1)));
        if (card.IsHidden) squeeze = 1;
        var w = height * Aspect * Math.Max(0.04f, squeeze);
        var c = center - new Vector2(0, lift);
        var min = c - new Vector2(w / 2, height / 2);
        var max = c + new Vector2(w / 2, height / 2);
        var r = height * 0.07f;

        Gfx.Shadow(dl, min, max, r, height * 0.05f, 0.45f * alpha, new Vector2(height * 0.015f, height * 0.035f + lift * 0.4f));
        if (highlight) Gfx.Glow(dl, min, max, r, Pal.GoldLight, height * 0.09f, alpha);

        if (faceUp) DrawFace(dl, min, max, r, card, alpha, squeeze);
        else DrawBack(dl, min, max, r, alpha, squeeze);

        if (highlight) dl.AddRect(min, max, Pal.U(Pal.Gold, alpha), r, ImDrawFlags.None, Math.Max(1.5f, height * 0.025f));
        if (dim) dl.AddRectFilled(min, max, Pal.U(Pal.Black, 0.48f * alpha), r);
    }

    private static void DrawFace(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, Card card, float alpha, float squeeze)
    {
        Gfx.RoundedGradient(dl, min, max, r, Pal.A(Face, alpha), Pal.A(FaceShade, alpha));
        dl.AddRect(min, max, Pal.U(Edge, alpha), r, ImDrawFlags.None, 1f);
        if (squeeze < 0.55f) return; // too thin mid-flip for detail

        var h = max.Y - min.Y;
        var w = max.X - min.X;
        var ink = Pal.A(Ink(card.Suit), alpha);
        var inkU = Pal.U(ink);

        // Corner index.
        var rankSize = h * 0.26f;
        var rank = card.RankText;
        var rs = Gfx.TextSize(FontRole.Card, rankSize, rank);
        var idxX = min.X + w * 0.17f;
        var rankPos = new Vector2(idxX - rs.X / 2, min.Y + h * 0.045f);
        if (rank == "10") rankPos.X += w * 0.02f;
        Gfx.Text(dl, FontRole.Card, rankSize, rankPos, inkU, rank);
        Suits.Draw(dl, card.Suit, new Vector2(idxX + (rank == "10" ? w * 0.02f : 0), min.Y + h * 0.34f), h * 0.14f, ink);

        // Small mirrored suit in the opposite corner.
        Suits.Draw(dl, card.Suit, new Vector2(max.X - w * 0.17f, max.Y - h * 0.13f), h * 0.12f, Pal.A(ink, 0.85f));

        var center = new Vector2((min.X + max.X) / 2 + w * 0.06f, (min.Y + max.Y) / 2 + h * 0.05f);
        if (card.Rank is >= 11 and <= 13)
        {
            // Court cards: a framed panel with the letter and suit.
            var pMin = new Vector2(min.X + w * 0.36f, min.Y + h * 0.17f);
            var pMax = new Vector2(max.X - w * 0.1f, max.Y - h * 0.24f);
            dl.AddRectFilled(pMin, pMax, Pal.U(Pal.Mix(Face, ink, 0.09f), alpha), r * 0.6f);
            dl.AddRect(pMin, pMax, Pal.U(ink, 0.55f), r * 0.6f, ImDrawFlags.None, Math.Max(1, h * 0.012f));
            var pc = (pMin + pMax) / 2;
            Gfx.TextCentered(dl, FontRole.Card, h * 0.36f, pc - new Vector2(0, h * 0.06f), inkU, card.RankText);
            Suits.Draw(dl, card.Suit, pc + new Vector2(0, h * 0.15f), h * 0.13f, ink);
            Crown(dl, new Vector2(pc.X, pMin.Y + h * 0.035f), h * 0.075f, Pal.A(Pal.Gold, alpha));
        }
        else if (card.Rank == 14)
        {
            Suits.Draw(dl, card.Suit, center, h * 0.44f, ink);
            dl.AddCircle(center, h * 0.3f, Pal.U(ink, 0.25f), 40, Math.Max(1, h * 0.01f));
        }
        else
        {
            Suits.Draw(dl, card.Suit, center, h * 0.4f, ink);
        }
    }

    private static void Crown(ImDrawListPtr dl, Vector2 c, float s, Vector4 col)
    {
        var u = Pal.U(col);
        var b0 = c + new Vector2(-s, s * 0.45f);
        var b1 = c + new Vector2(s, s * 0.45f);
        dl.AddRectFilled(b0 - new Vector2(0, s * 0.25f), b1, u);
        dl.AddTriangleFilled(c + new Vector2(-s, s * 0.25f), c + new Vector2(-s * 0.85f, -s * 0.5f), c + new Vector2(-s * 0.35f, s * 0.25f), u);
        dl.AddTriangleFilled(c + new Vector2(-s * 0.4f, s * 0.25f), c + new Vector2(0, -s * 0.75f), c + new Vector2(s * 0.4f, s * 0.25f), u);
        dl.AddTriangleFilled(c + new Vector2(s * 0.35f, s * 0.25f), c + new Vector2(s * 0.85f, -s * 0.5f), c + new Vector2(s, s * 0.25f), u);
    }

    private static void DrawBack(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, float alpha, float squeeze)
    {
        var (a, b, trim) = BackColors();
        Gfx.RoundedGradient(dl, min, max, r, Pal.A(a, alpha), Pal.A(b, alpha));
        var h = max.Y - min.Y;
        var w = max.X - min.X;
        dl.AddRect(min, max, Pal.U(Pal.White, 0.9f * alpha), r, ImDrawFlags.None, Math.Max(1.5f, h * 0.035f));
        if (squeeze < 0.3f) return;

        var inset = h * 0.075f;
        var iMin = min + new Vector2(inset);
        var iMax = max - new Vector2(inset);
        if (iMax.X <= iMin.X) return;
        dl.AddRect(iMin, iMax, Pal.U(trim, 0.85f * alpha), r * 0.5f, ImDrawFlags.None, Math.Max(1, h * 0.012f));

        // Diamond lattice.
        dl.PushClipRect(iMin, iMax, true);
        var step = h * 0.11f;
        var latt = Pal.U(trim, 0.22f * alpha);
        for (var x = iMin.X - h; x < iMax.X + h; x += step)
        {
            dl.AddLine(new Vector2(x, iMin.Y), new Vector2(x + h, iMax.Y + (h - (iMax.Y - iMin.Y))), latt, 1f);
            dl.AddLine(new Vector2(x + h, iMin.Y), new Vector2(x, iMax.Y + (h - (iMax.Y - iMin.Y))), latt, 1f);
        }
        dl.PopClipRect();

        // Clover emblem.
        var c = (min + max) / 2;
        var er = Math.Min(w, h) * 0.24f;
        dl.AddCircleFilled(c, er, Pal.U(b, alpha), 32);
        dl.AddCircle(c, er, Pal.U(trim, 0.9f * alpha), 32, Math.Max(1, h * 0.014f));
        Clover(dl, c, er * 1.05f, Pal.A(trim, alpha));
    }

    /// <summary>Four-leaf clover — the Saucer Nights mark.</summary>
    public static void Clover(ImDrawListPtr dl, Vector2 c, float size, Vector4 col)
    {
        var u = Pal.U(col);
        var lr = size * 0.15f;
        var d = size * 0.23f;
        var seg = Math.Clamp((int)(size * 0.5f), 10, 28);
        for (var k = 0; k < 4; k++)
        {
            var a = MathF.PI / 4 + k * MathF.PI / 2;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            var perp = new Vector2(-dir.Y, dir.X);
            var lc = c + dir * d;
            // A heart pointing at the centre.
            dl.AddCircleFilled(lc + dir * lr * 0.35f + perp * lr * 0.74f, lr, u, seg);
            dl.AddCircleFilled(lc + dir * lr * 0.35f - perp * lr * 0.74f, lr, u, seg);
            dl.AddTriangleFilled(c + dir * size * 0.03f, lc + dir * lr * 0.2f + perp * lr * 1.66f, lc + dir * lr * 0.2f - perp * lr * 1.66f, u);
        }
        dl.AddBezierCubic(c, c + new Vector2(size * 0.04f, size * 0.22f), c + new Vector2(size * 0.2f, size * 0.32f), c + new Vector2(size * 0.16f, size * 0.5f), u, Math.Max(1.2f, size * 0.06f));
    }

    /// <summary>Faint outline marking where a card will go.</summary>
    public static void Slot(ImDrawListPtr dl, Vector2 center, float height, Vector4 tint)
    {
        var w = height * Aspect;
        var min = center - new Vector2(w / 2, height / 2);
        var max = center + new Vector2(w / 2, height / 2);
        dl.AddRect(min, max, Pal.U(tint, 0.35f), height * 0.07f, ImDrawFlags.None, Math.Max(1f, height * 0.015f));
    }
}

/// <summary>Suit symbols built from circles and triangles.</summary>
public static class Suits
{
    public static void Draw(ImDrawListPtr dl, Suit suit, Vector2 c, float s, Vector4 color)
    {
        var col = Pal.U(color);
        var seg = Math.Clamp((int)(s * 0.8f), 12, 40);
        switch (suit)
        {
            case Suit.Diamonds:
                dl.AddQuadFilled(c + new Vector2(0, -s * 0.5f), c + new Vector2(s * 0.37f, 0), c + new Vector2(0, s * 0.5f), c + new Vector2(-s * 0.37f, 0), col);
                break;
            case Suit.Hearts:
            {
                var r = s * 0.255f;
                dl.AddCircleFilled(c + new Vector2(-s * 0.215f, -s * 0.16f), r, col, seg);
                dl.AddCircleFilled(c + new Vector2(s * 0.215f, -s * 0.16f), r, col, seg);
                dl.AddTriangleFilled(c + new Vector2(-s * 0.462f, -s * 0.07f), c + new Vector2(s * 0.462f, -s * 0.07f), c + new Vector2(0, s * 0.46f), col);
                dl.AddTriangleFilled(c + new Vector2(-s * 0.2f, -s * 0.2f), c + new Vector2(s * 0.2f, -s * 0.2f), c + new Vector2(0, s * 0.1f), col);
                break;
            }
            case Suit.Spades:
            {
                var r = s * 0.24f;
                dl.AddCircleFilled(c + new Vector2(-s * 0.205f, s * 0.08f), r, col, seg);
                dl.AddCircleFilled(c + new Vector2(s * 0.205f, s * 0.08f), r, col, seg);
                dl.AddTriangleFilled(c + new Vector2(-s * 0.44f, s * 0.0f), c + new Vector2(0, -s * 0.5f), c + new Vector2(s * 0.44f, s * 0.0f), col);
                dl.AddTriangleFilled(c + new Vector2(-s * 0.2f, s * 0.1f), c + new Vector2(0, -s * 0.2f), c + new Vector2(s * 0.2f, s * 0.1f), col);
                Stem(dl, c, s, col);
                break;
            }
            default:
            {
                var r = s * 0.2f;
                dl.AddCircleFilled(c + new Vector2(0, -s * 0.24f), r, col, seg);
                dl.AddCircleFilled(c + new Vector2(-s * 0.225f, s * 0.07f), r, col, seg);
                dl.AddCircleFilled(c + new Vector2(s * 0.225f, s * 0.07f), r, col, seg);
                dl.AddCircleFilled(c + new Vector2(0, -s * 0.02f), s * 0.12f, col, seg);
                Stem(dl, c, s, col);
                break;
            }
        }
    }

    private static void Stem(ImDrawListPtr dl, Vector2 c, float s, uint col) =>
        dl.AddTriangleFilled(c + new Vector2(0, s * 0.06f), c + new Vector2(s * 0.17f, s * 0.5f), c + new Vector2(-s * 0.17f, s * 0.5f), col);
}
