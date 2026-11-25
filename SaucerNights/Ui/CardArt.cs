using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Cards;

namespace SaucerNights.Ui;

/// <summary>Vector-drawn playing cards: no textures, crisp at any size.</summary>
public static class CardArt
{
    public const float Aspect = 0.714f;

    public static CardBackStyle Back = CardBackStyle.Neon;
    public static bool FourColor;

    private static readonly Vector4 Face = Pal.Hex(0xFBFCFF);
    private static readonly Vector4 FaceShade = Pal.Hex(0xE2E7F3);
    private static readonly Vector4 Edge = Pal.Hex(0xAEB8D2);
    private static readonly Vector4 RedInk = Pal.Hex(0xD61C4B);
    private static readonly Vector4 BlackInk = Pal.Hex(0x111729);
    private static readonly Vector4 GreenInk = Pal.Hex(0x0C8A55);
    private static readonly Vector4 BlueInk = Pal.Hex(0x1D5ED2);

    public static Vector2 Size(float height) => new(height * Aspect, height);

    public static Vector4 Ink(Suit s) => FourColor
        ? s switch { Suit.Clubs => GreenInk, Suit.Diamonds => BlueInk, Suit.Hearts => RedInk, _ => BlackInk }
        : s is Suit.Diamonds or Suit.Hearts ? RedInk : BlackInk;

    private static (Vector4 A, Vector4 B, Vector4 Trim) BackColors() => Back switch
    {
        CardBackStyle.Neon => (Pal.Hex(0x10285A), Pal.Hex(0x050A1F), Pal.Accent),
        CardBackStyle.Ultraviolet => (Pal.Hex(0x3C1570), Pal.Hex(0x10052A), Pal.Magenta),
        CardBackStyle.Midnight => (Pal.Hex(0x23356B), Pal.Hex(0x0E1633), Pal.Hex(0xC9D3E8)),
        CardBackStyle.Emerald => (Pal.Hex(0x1A6B47), Pal.Hex(0x0A3020), Pal.Hex(0xE6C16A)),
        CardBackStyle.Gilded => (Pal.Hex(0x2A2116), Pal.Hex(0x120D08), Pal.Hex(0xE8B04A)),
        _ => (Pal.Hex(0x9A2335), Pal.Hex(0x4E0E19), Pal.Hex(0xE6C16A)),
    };

    /// <summary>
    /// Draws a card centred at <paramref name="center"/>. <paramref name="flip"/> runs 0 (back) → 1 (face) and squeezes
    /// the card horizontally through the middle, like turning it over. <paramref name="shown"/> is how much of the
    /// card's width is left showing when the next card in a fan covers its right side: only what fits in that strip is
    /// drawn, so nothing peeks out half cut.
    /// </summary>
    public static void Draw(ImDrawListPtr dl, Vector2 center, float height, Card card, float flip = 1f, float alpha = 1f,
        bool highlight = false, bool dim = false, float lift = 0, float shown = 1f)
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
        if (highlight) Gfx.Glow(dl, min, max, r, Pal.Gold, height * 0.1f, alpha * 1.2f);

        if (faceUp) DrawFace(dl, min, max, r, card, alpha, squeeze, shown);
        else DrawBack(dl, min, max, r, alpha, squeeze);

        if (highlight) dl.AddRect(min, max, Pal.U(Pal.Gold, alpha), r, ImDrawFlags.None, Math.Max(1.5f, height * 0.025f));
        if (dim) dl.AddRectFilled(min, max, Pal.U(Pal.Black, 0.48f * alpha), r);
    }

    private static void DrawFace(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, Card card, float alpha, float squeeze, float shown)
    {
        Gfx.RoundedGradient(dl, min, max, r, Pal.A(Face, alpha), Pal.A(FaceShade, alpha));
        dl.AddRect(min, max, Pal.U(Edge, alpha), r, ImDrawFlags.None, 1f);
        if (squeeze < 0.55f) return; // too thin mid-flip for detail

        // Lay the face out at full width, then squeeze it about the middle while the card turns over.
        var h = max.Y - min.Y;
        var w = h * Aspect;
        var mid = (min.X + max.X) / 2;
        min = new Vector2(mid - w / 2, min.Y);
        max = new Vector2(mid + w / 2, max.Y);
        var start = Gfx.Mark(dl);
        var ink = Pal.A(Ink(card.Suit), alpha);
        var center = (min + max) / 2;

        if (h < TinyBelow)
        {
            // Too small for two corners: one big index and a pip, so the card still reads at a glance.
            Index(dl, min, w, h, card, ink, true);
            var spot = new Vector2(min.X + w * 0.7f, min.Y + h * 0.72f);
            if (shown >= 0.9f)
            {
                if (card.Rank is >= 11 and <= 13) Medallion(dl, spot, h * 0.17f, h * 0.2f, card, alpha);
                else Suits.Draw(dl, card.Suit, spot, h * 0.34f, ink);
            }
        }
        else
        {
            // Rank over suit in the top-left corner, and the same turned upside down in the bottom-right.
            Index(dl, min, w, h, card, ink, false);
            if (shown >= 0.999f)
            {
                var turned = Gfx.Mark(dl);
                Index(dl, min, w, h, card, ink, false);
                Gfx.Turn(dl, turned, min, max);
            }

            if (card.Rank is >= 11 and <= 13)
            {
                if (shown >= CourtShown) Court(dl, min, max, h, card, ink, alpha);
            }
            else
            {
                // Suit shapes reach out to 0.47 of their size either side of the middle.
                var size = h * (card.Rank == 14 ? 0.4f : 0.36f);
                if (shown >= 0.5f + size * 0.47f / w + 0.02f) Suits.Draw(dl, card.Suit, center, size, ink);
            }
        }

        if (squeeze < 0.999f) Gfx.SqueezeX(dl, start, mid, squeeze);
    }

    /// <summary>Cards shorter than this many pixels get the single big corner.</summary>
    private const float TinyBelow = 56f;

    /// <summary>
    /// Rank over suit in the top-left corner. It stays inside the strip a fanned blackjack hand leaves showing (30% of
    /// the width), lines the caps up whatever the rank, and leaves room under the rank for the tail of a Q.
    /// </summary>
    private static void Index(ImDrawListPtr dl, Vector2 min, float w, float h, Card card, Vector4 ink, bool tiny)
    {
        var rank = card.RankText;
        var size = h * (tiny ? 0.36f : 0.25f);
        var font = UiEnv.Font(FontRole.Card, size);
        var caps = Gfx.Ink(font, size, "K");
        var (a, b) = Gfx.Ink(font, size, rank);
        var cx = min.X + w * (tiny ? 0.27f : 0.16f);
        var pos = new Vector2(MathF.Round(cx - (a.X + b.X) / 2), MathF.Round(min.Y + h * (tiny ? 0.07f : 0.065f) - caps.Min.Y));
        var mark = Gfx.Mark(dl);
        dl.AddText(font, size, pos, Pal.U(ink), rank);
        var widest = w * (tiny ? 0.44f : 0.25f);
        if (b.X - a.X > widest) Gfx.SqueezeX(dl, mark, cx, widest / (b.X - a.X));

        var baseline = pos.Y + caps.Max.Y;
        var tail = Math.Max(0, Gfx.Ink(font, size, "Q").Max.Y - caps.Max.Y);
        var s = h * (tiny ? 0.2f : 0.13f);
        Suits.Draw(dl, card.Suit, new Vector2(cx, baseline + Math.Max(h * 0.04f, tail + h * 0.022f) + s / 2), s, ink);
    }

    /// <summary>The share of the width a court card's medallion needs showing before it is drawn.</summary>
    private const float CourtShown = 0.78f;

    /// <summary>
    /// Court cards: a night-blue hexagon medallion in the middle (the Solution Nine motif) with a neon rim in the suit
    /// colour and the letter in gold, a gold headpiece for the rank on top and the suit underneath. Everything stays in
    /// the band between the two corner indices.
    /// </summary>
    private static void Court(ImDrawListPtr dl, Vector2 min, Vector2 max, float h, Card card, Vector4 ink, float alpha)
    {
        var c = (min + max) / 2;
        var rad = h * 0.19f;
        var flat = rad * 0.866f;
        Medallion(dl, c, rad, h * 0.24f, card, alpha);
        Headpiece(dl, card.Rank, new Vector2(c.X, c.Y - flat - h * 0.062f), h * 0.074f, Pal.A(Pal.Gold, alpha), Pal.A(Pal.Hex(0xA8741E), alpha));
        Suits.Draw(dl, card.Suit, new Vector2(c.X, c.Y + flat + h * 0.075f), h * 0.1f, ink);
    }

    /// <summary>The hexagon medallion with the court letter, centred on <paramref name="c"/>.</summary>
    private static void Medallion(ImDrawListPtr dl, Vector2 c, float rad, float letterSize, Card card, float alpha)
    {
        Span<Vector2> hex = stackalloc Vector2[6];
        Gfx.Hexagon(hex, c + new Vector2(0, rad * 0.05f), rad, 0);
        Gfx.FillConvex(dl, hex, Pal.U(Pal.Black, 0.22f * alpha));
        Gfx.Hexagon(hex, c, rad, 0);
        Gfx.FillVertical(dl, hex, Pal.A(Pal.Hex(0x1B2A60), alpha), Pal.A(Pal.Hex(0x080D26), alpha));
        Gfx.Outline(dl, hex, Pal.U(NeonInk(card.Suit), alpha), Math.Max(1.2f, rad * 0.068f));
        if (rad > 9)
        {
            Gfx.Hexagon(hex, c, rad * 0.84f, 0);
            Gfx.Outline(dl, hex, Pal.U(Pal.Gold, 0.45f * alpha), Math.Max(1, rad * 0.032f));
        }

        var font = UiEnv.Font(FontRole.Card, letterSize);
        var caps = Gfx.Ink(font, letterSize, "K");
        var (a, b) = Gfx.Ink(font, letterSize, card.RankText);
        var capH = caps.Max.Y - caps.Min.Y;
        var pos = new Vector2(MathF.Round(c.X - (a.X + b.X) / 2), MathF.Round(c.Y - capH / 2 - caps.Min.Y));
        dl.AddText(font, letterSize, pos, Pal.U(Pal.GoldLight, alpha), card.RankText);
    }

    /// <summary>Bright suit colours for the night medallion.</summary>
    private static Vector4 NeonInk(Suit s) => FourColor
        ? s switch { Suit.Clubs => Pal.Hex(0x3DFFA8), Suit.Diamonds => Pal.Hex(0x5AA8FF), Suit.Hearts => Pal.Hex(0xFF4F7A), _ => Pal.Accent }
        : s is Suit.Diamonds or Suit.Hearts ? Pal.Hex(0xFF4F7A) : Pal.Accent;

    /// <summary>
    /// A gold headpiece above the medallion, different for each rank: a three-point crown with orbs for the king, a
    /// five-point tiara for the queen and a band with a single gem for the jack. Drawn centred on <paramref name="c"/>,
    /// <paramref name="s"/> wide either side.
    /// </summary>
    private static void Headpiece(ImDrawListPtr dl, int rank, Vector2 c, float s, Vector4 gold, Vector4 deep)
    {
        var u = Pal.U(gold);
        var band0 = c + new Vector2(-s, s * 0.2f);
        var band1 = c + new Vector2(s, s * 0.55f);
        dl.AddRectFilled(band0, band1, u, s * 0.08f);
        dl.AddLine(new Vector2(band0.X, band1.Y), band1, Pal.U(deep), Math.Max(1, s * 0.12f));
        var seg = Math.Clamp((int)(s * 0.6f), 8, 16);
        switch (rank)
        {
            case 13:
            {
                float[] xs = [-0.82f, 0, 0.82f];
                float[] hs = [0.55f, 0.85f, 0.55f];
                for (var i = 0; i < 3; i++)
                {
                    var tip = c + new Vector2(xs[i] * s, s * 0.2f - hs[i] * s);
                    dl.AddTriangleFilled(c + new Vector2((xs[i] - 0.32f) * s, s * 0.22f), tip, c + new Vector2((xs[i] + 0.32f) * s, s * 0.22f), u);
                    dl.AddCircleFilled(tip, s * 0.14f, u, seg);
                }
                dl.AddCircleFilled(c + new Vector2(0, s * 0.37f), s * 0.11f, Pal.U(deep), seg);
                break;
            }
            case 12:
            {
                float[] xs = [-0.8f, -0.4f, 0, 0.4f, 0.8f];
                float[] hs = [0.32f, 0.5f, 0.72f, 0.5f, 0.32f];
                for (var i = 0; i < 5; i++)
                {
                    var tip = c + new Vector2(xs[i] * s, s * 0.2f - hs[i] * s);
                    dl.AddTriangleFilled(c + new Vector2((xs[i] - 0.2f) * s, s * 0.22f), tip, c + new Vector2((xs[i] + 0.2f) * s, s * 0.22f), u);
                }
                dl.AddCircleFilled(c + new Vector2(0, s * -0.52f), s * 0.12f, u, seg);
                dl.AddCircleFilled(c + new Vector2(0, s * 0.37f), s * 0.1f, Pal.U(deep), seg);
                break;
            }
            default:
            {
                // A gem on the band, with a small point either side.
                dl.AddQuadFilled(c + new Vector2(0, -s * 0.62f), c + new Vector2(s * 0.3f, -s * 0.12f), c + new Vector2(0, s * 0.3f), c + new Vector2(-s * 0.3f, -s * 0.12f), u);
                dl.AddQuadFilled(c + new Vector2(0, -s * 0.38f), c + new Vector2(s * 0.14f, -s * 0.12f), c + new Vector2(0, s * 0.12f), c + new Vector2(-s * 0.14f, -s * 0.12f), Pal.U(deep));
                dl.AddTriangleFilled(c + new Vector2(-s * 0.95f, s * 0.22f), c + new Vector2(-s * 0.75f, -s * 0.12f), c + new Vector2(-s * 0.55f, s * 0.22f), u);
                dl.AddTriangleFilled(c + new Vector2(s * 0.55f, s * 0.22f), c + new Vector2(s * 0.75f, -s * 0.12f), c + new Vector2(s * 0.95f, s * 0.22f), u);
                break;
            }
        }
    }

    private static void DrawBack(ImDrawListPtr dl, Vector2 min, Vector2 max, float r, float alpha, float squeeze)
    {
        var (a, b, trim) = BackColors();
        var neon = Back is CardBackStyle.Neon or CardBackStyle.Ultraviolet;
        Gfx.RoundedGradient(dl, min, max, r, Pal.A(a, alpha), Pal.A(b, alpha));
        var h = max.Y - min.Y;
        var w = max.X - min.X;
        dl.AddRect(min, max, Pal.U(neon ? Pal.Hex(0xDDE6FF) : Pal.White, 0.9f * alpha), r, ImDrawFlags.None, Math.Max(1.5f, h * 0.035f));
        if (squeeze < 0.3f) return;

        var inset = h * 0.075f;
        var iMin = min + new Vector2(inset);
        var iMax = max - new Vector2(inset);
        if (iMax.X <= iMin.X) return;
        dl.AddRect(iMin, iMax, Pal.U(trim, 0.85f * alpha), r * 0.5f, ImDrawFlags.None, Math.Max(1, h * 0.012f));

        if (neon)
        {
            // Hex lattice with a soft glow from the centre.
            var cc = (iMin + iMax) / 2;
            var gn = Gfx.Ellipse(Glow, cc, (iMax - iMin) * 0.6f, 24);
            Gfx.FillRadial(dl, Glow.AsSpan(0, gn), cc, Pal.U(trim, 0.22f * alpha), Pal.U(trim, 0f));
            Gfx.HexGrid(dl, iMin, iMax, h * 0.065f, Pal.U(trim, 0.3f * alpha), 1f);
        }
        else
        {
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
        }

        // Emblem.
        var c = (min + max) / 2;
        var er = Math.Min(w, h) * 0.26f;
        Span<Vector2> hex = stackalloc Vector2[6];
        Gfx.Hexagon(hex, c, er * 1.15f);
        Gfx.FillConvex(dl, hex, Pal.U(b, alpha));
        Logo.Mark(dl, c, er * 1.9f, Pal.A(trim, alpha));
    }

    private static readonly Vector2[] Glow = new Vector2[32];

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
