using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SaucerNights.Ui;

/// <summary>
/// The European roulette wheel and betting board, drawn from scratch. Bets are identified by the set of numbers they
/// cover (bit n = number n), so the board doesn't need to know how the engine indexes them.
/// </summary>
public static class RouletteArt
{
    public static readonly int[] WheelOrder =
        [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26];

    private static readonly HashSet<int> Reds = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];

    public static readonly Vector4 RedPocket = Pal.Hex(0xD11F45);
    public static readonly Vector4 BlackPocket = Pal.Hex(0x111528);
    public static readonly Vector4 GreenPocket = Pal.Hex(0x10A36A);

    private static float S(float v) => UiEnv.S(v);

    public static bool IsRed(int n) => Reds.Contains(n);

    public static Vector4 NumberColor(int n) => n == 0 ? GreenPocket : IsRed(n) ? RedPocket : BlackPocket;

    public static ulong Bit(int n) => 1UL << n;

    public static ulong Mask(params int[] numbers) => numbers.Aggregate(0UL, (m, n) => m | Bit(n));

    public static ulong Mask(IEnumerable<int> numbers) => numbers.Aggregate(0UL, (m, n) => m | Bit(n));

    public static IEnumerable<int> Numbers(ulong mask)
    {
        for (var n = 0; n <= 36; n++)
            if ((mask & Bit(n)) != 0) yield return n;
    }

    // ───────────── Wheel ─────────────

    /// <summary>Angle (radians, screen space) of a pocket's centre when the wheel is turned by <paramref name="wheel"/>.</summary>
    public static float PocketAngle(int number, float wheel)
    {
        var i = Array.IndexOf(WheelOrder, number);
        return wheel + i * MathF.Tau / 37 - MathF.PI / 2;
    }

    /// <summary>
    /// Draws the wheel. <paramref name="wheel"/> turns the pockets; the ball (if any) sits at <paramref name="ballAngle"/>
    /// on a track <paramref name="ballRadius"/> (0..1 of the wheel's radius). <paramref name="lit"/> glows one pocket.
    /// </summary>
    public static void Wheel(ImDrawListPtr dl, Vector2 c, float r, float wheel, Vector4 neon, float? ballAngle = null, float ballRadius = 0.8f, int lit = -1, float litStrength = 0)
    {
        // Bowl and rim.
        Gfx.GlowCircle(dl, c, r * 1.02f, neon, r * 0.12f, 0.7f);
        dl.AddCircleFilled(c + new Vector2(0, r * 0.05f), r * 1.04f, Pal.U(Pal.Black, 0.55f), 96);
        dl.AddCircleFilled(c, r, Pal.U(Pal.Hex(0x161D3E)), 96);
        Ring(dl, c, r, r * 0.86f, Pal.Hex(0x232D5C), Pal.Hex(0x0B0F24));
        dl.AddCircle(c, r, Pal.U(Pal.Magenta, 0.6f), 96, S(1.4f));
        dl.AddCircle(c, r * 0.86f, Pal.U(neon, 0.9f), 96, S(1.6f));
        // Ball track markers.
        for (var i = 0; i < 8; i++)
        {
            var a = wheel * 0 + i * MathF.Tau / 8 + MathF.PI / 8;
            var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
            W.Diamond(dl, c + d * r * 0.93f, r * 0.018f, Pal.A(neon, 0.6f));
        }

        // Pockets.
        var outer = r * 0.84f;
        var inner = r * 0.6f;
        var step = MathF.Tau / 37;
        for (var i = 0; i < 37; i++)
        {
            var n = WheelOrder[i];
            var a0 = wheel + (i - 0.5f) * step - MathF.PI / 2;
            var a1 = a0 + step;
            var col = NumberColor(n);
            if (n == lit && litStrength > 0) col = Pal.Lighten(col, 0.2f * litStrength);
            Sector(dl, c, inner, outer, a0, a1, col);
            if (n == lit && litStrength > 0)
            {
                // Gold edge around the winning pocket.
                Span<Vector2> edge = [c + Dir(a0) * inner, c + Dir(a0) * outer, c + Dir((a0 + a1) / 2) * outer, c + Dir(a1) * outer, c + Dir(a1) * inner];
                Gfx.NeonOutline(dl, edge, Pal.A(Pal.Gold, litStrength), Math.Max(1.5f, r * 0.01f), litStrength);
            }
            dl.AddLine(c + Dir(a0) * inner, c + Dir(a0) * outer, Pal.U(Pal.Hex(0xC9D6FF), 0.35f), Math.Max(1f, r * 0.006f));
            // Number, upright, on the outer half of the pocket (too small to read on a tiny wheel).
            if (r < S(60)) continue;
            var am = (a0 + a1) / 2;
            var fs = Math.Max(S(8), r * 0.075f);
            Gfx.TextCentered(dl, FontRole.Heading, fs, c + Dir(am) * (outer - r * 0.065f), Pal.U(Pal.White, 0.92f), n.ToString());
        }
        dl.AddCircle(c, outer, Pal.U(Pal.Hex(0xC9D6FF), 0.5f), 96, Math.Max(1f, r * 0.008f));
        // Pocket floor ring (where the ball rests) and the cone.
        Ring(dl, c, inner, r * 0.5f, Pal.Hex(0x1B2550), Pal.Hex(0x0D1230));
        dl.AddCircle(c, inner, Pal.U(neon, 0.7f), 96, S(1.2f));
        var cone = r * 0.5f;
        var n2 = Gfx.Ellipse(Buf, c, new Vector2(cone), 48);
        Gfx.FillRadial(dl, Buf.AsSpan(0, n2), c - new Vector2(cone * 0.25f), Pal.U(Pal.Hex(0x2A3770)), Pal.U(Pal.Hex(0x0B1028)));
        // Spokes turning with the wheel.
        for (var k = 0; k < 4; k++)
        {
            var a = wheel + k * MathF.PI / 2;
            Gfx.NeonLine(dl, c + Dir(a) * cone * 0.25f, c + Dir(a) * cone * 0.95f, Pal.A(neon, 0.75f), Math.Max(1f, r * 0.014f), 0.6f);
            dl.AddCircleFilled(c + Dir(a) * cone * 0.95f, r * 0.022f, Pal.U(Pal.Lighten(neon, 0.4f)), 12);
        }
        Logo.Mark(dl, c, cone * 0.62f, neon, Pal.Hex(0x0B1530));

        if (ballAngle is { } ba)
        {
            var bp = c + Dir(ba) * r * ballRadius;
            var br = Math.Max(S(3), r * 0.032f);
            dl.AddCircleFilled(bp + new Vector2(br * 0.3f, br * 0.5f), br, Pal.U(Pal.Black, 0.5f), 16);
            dl.AddCircleFilled(bp, br, Pal.U(Pal.Hex(0xF2F5FF)), 16);
            dl.AddCircleFilled(bp - new Vector2(br * 0.3f), br * 0.4f, Pal.U(Pal.White), 12);
        }
    }

    private static readonly Vector2[] Buf = new Vector2[128];

    private static Vector2 Dir(float a) => new(MathF.Cos(a), MathF.Sin(a));

    private static void Sector(ImDrawListPtr dl, Vector2 c, float r0, float r1, float a0, float a1, Vector4 col)
    {
        var u = Pal.U(col);
        const int seg = 3;
        for (var k = 0; k < seg; k++)
        {
            var b0 = a0 + (a1 - a0) * k / seg;
            var b1 = a0 + (a1 - a0) * (k + 1) / seg;
            dl.AddQuadFilled(c + Dir(b0) * r0, c + Dir(b0) * r1, c + Dir(b1) * r1, c + Dir(b1) * r0, u);
        }
    }

    private static void Ring(ImDrawListPtr dl, Vector2 c, float r0, float r1, Vector4 outerCol, Vector4 innerCol)
    {
        const int seg = 72;
        var uo = Pal.U(outerCol);
        var ui = Pal.U(innerCol);
        var uv = ImGui.GetFontTexUvWhitePixel();
        for (var k = 0; k < seg; k++)
        {
            var a0 = k * MathF.Tau / seg;
            var a1 = (k + 1) * MathF.Tau / seg;
            dl.PrimReserve(6, 4);
            var b = dl.VtxCurrentIdx;
            dl.PrimWriteVtx(c + Dir(a0) * r0, uv, uo);
            dl.PrimWriteVtx(c + Dir(a1) * r0, uv, uo);
            dl.PrimWriteVtx(c + Dir(a1) * r1, uv, ui);
            dl.PrimWriteVtx(c + Dir(a0) * r1, uv, ui);
            dl.PrimWriteIdx((ushort)b);
            dl.PrimWriteIdx((ushort)(b + 1));
            dl.PrimWriteIdx((ushort)(b + 2));
            dl.PrimWriteIdx((ushort)b);
            dl.PrimWriteIdx((ushort)(b + 2));
            dl.PrimWriteIdx((ushort)(b + 3));
        }
    }

    /// <summary>A number in a coloured hexagon (results, history).</summary>
    public static void NumberBadge(ImDrawListPtr dl, Vector2 c, float r, int n, float alpha = 1f, bool glow = false)
    {
        Span<Vector2> hex = stackalloc Vector2[6];
        Gfx.Hexagon(hex, c, r);
        var col = NumberColor(n);
        if (glow) Gfx.GlowCircle(dl, c, r, Pal.Lighten(col, 0.3f), r * 0.6f, alpha);
        Gfx.FillVertical(dl, hex, Pal.A(Pal.Lighten(col, 0.15f), alpha), Pal.A(Pal.Darken(col, 0.25f), alpha));
        Gfx.Outline(dl, hex, Pal.U(Pal.Hex(0xDDE6FF), 0.6f * alpha), Math.Max(1f, r * 0.08f));
        Gfx.TextCentered(dl, FontRole.Heading, r * 1.05f, c + new Vector2(0, r * 0.04f), Pal.U(Pal.White, alpha), n.ToString());
    }
}

/// <summary>
/// The betting layout: zero, the 3 x 12 number grid, "2 to 1" columns, dozens and the even-money boxes. Knows where
/// every bet sits and which bet a point on the board means.
/// </summary>
public sealed class RouletteBoard
{
    public Vector2 Min;
    public Vector2 Max;

    public float Cell;
    public float RowH;
    public Vector2 Grid;

    private static float S(float v) => UiEnv.S(v);

    public static readonly (string Label, int[] Numbers)[] Dozens =
    [
        ("1st 12", Enumerable.Range(1, 12).ToArray()),
        ("2nd 12", Enumerable.Range(13, 12).ToArray()),
        ("3rd 12", Enumerable.Range(25, 12).ToArray()),
    ];

    public static readonly (string Label, int[] Numbers)[] EvenMoney =
    [
        ("1-18", Enumerable.Range(1, 18).ToArray()),
        ("EVEN", Enumerable.Range(1, 36).Where(n => n % 2 == 0).ToArray()),
        ("RED", Enumerable.Range(1, 36).Where(RouletteArt.IsRed).ToArray()),
        ("BLACK", Enumerable.Range(1, 36).Where(n => !RouletteArt.IsRed(n)).ToArray()),
        ("ODD", Enumerable.Range(1, 36).Where(n => n % 2 == 1).ToArray()),
        ("19-36", Enumerable.Range(19, 18).ToArray()),
    ];

    /// <summary>Column bets, top row first (3, 6, ... 36), as they sit on the board.</summary>
    public static readonly int[][] Columns =
    [
        Enumerable.Range(0, 12).Select(c => 3 * c + 3).ToArray(),
        Enumerable.Range(0, 12).Select(c => 3 * c + 2).ToArray(),
        Enumerable.Range(0, 12).Select(c => 3 * c + 1).ToArray(),
    ];

    /// <summary>Fits the board into a rectangle (keeping its proportions) and returns its size.</summary>
    public Vector2 Layout(Vector2 areaMin, Vector2 areaMax)
    {
        // 14 cells across (zero, 12 columns, "2 to 1"); 3 rows plus dozens and even-money rows of 0.8.
        var w = areaMax.X - areaMin.X;
        var h = areaMax.Y - areaMin.Y;
        Cell = Math.Min(w / 14f, h / (3 + 0.8f * 2) / 1.18f * 1f);
        RowH = Cell * 1.18f;
        var size = new Vector2(Cell * 14, RowH * 3 + RowH * 0.8f * 2);
        Min = areaMin + (areaMax - areaMin - size) / 2;
        Max = Min + size;
        Grid = Min + new Vector2(Cell, 0);
        return size;
    }

    public Vector2 CellCenter(int n)
    {
        if (n == 0) return new Vector2(Min.X + Cell / 2, Min.Y + RowH * 1.5f);
        var c = (n - 1) / 3;
        var r = 2 - (n - 1) % 3;
        return Grid + new Vector2((c + 0.5f) * Cell, (r + 0.5f) * RowH);
    }

    private (Vector2 Min, Vector2 Max) DozenRect(int i) =>
        (Grid + new Vector2(i * 4 * Cell, 3 * RowH), Grid + new Vector2((i + 1) * 4 * Cell, 3.8f * RowH));

    private (Vector2 Min, Vector2 Max) EvenRect(int i) =>
        (Grid + new Vector2(i * 2 * Cell, 3.8f * RowH), Grid + new Vector2((i + 1) * 2 * Cell, 4.6f * RowH));

    private (Vector2 Min, Vector2 Max) ColumnRect(int row) =>
        (Grid + new Vector2(12 * Cell, row * RowH), Grid + new Vector2(13 * Cell, (row + 1) * RowH));

    /// <summary>Where a bet's chips sit.</summary>
    public Vector2 SpotOf(ulong mask)
    {
        var nums = RouletteArt.Numbers(mask).ToArray();
        if (nums.Length == 0) return (Min + Max) / 2;
        for (var i = 0; i < 3; i++)
            if (mask == RouletteArt.Mask(Dozens[i].Numbers)) { var r = DozenRect(i); return (r.Min + r.Max) / 2; }
        for (var i = 0; i < 6; i++)
            if (mask == RouletteArt.Mask(EvenMoney[i].Numbers)) { var r = EvenRect(i); return (r.Min + r.Max) / 2; }
        for (var i = 0; i < 3; i++)
            if (mask == RouletteArt.Mask(Columns[i])) { var r = ColumnRect(i); return (r.Min + r.Max) / 2; }
        if (nums.Contains(0) && nums.Length > 1)
        {
            // 0/1, 0/2, 0/3 sit on the zero line; trios and the first four on its corners.
            var others = nums.Where(n => n != 0).Select(CellCenter).ToArray();
            var y = others.Average(p => p.Y);
            if (nums.Length == 4) y = Grid.Y + 3 * RowH;
            return new Vector2(Grid.X, y);
        }
        var centers = nums.Select(CellCenter).ToArray();
        var avg = new Vector2(centers.Average(p => p.X), centers.Average(p => p.Y));
        // Streets and six lines sit on the board's bottom edge.
        if (nums.Length is 3 or 6) avg.Y = Grid.Y + 3 * RowH;
        return avg;
    }

    /// <summary>The bet a point on the board means (numbers covered and where its chips sit), or null.</summary>
    public (ulong Mask, Vector2 Spot)? HitTest(Vector2 p)
    {
        if (p.X < Min.X || p.Y < Min.Y || p.X > Max.X || p.Y > Max.Y) return null;
        var gx = (p.X - Grid.X) / Cell;
        var gy = (p.Y - Grid.Y) / RowH;
        const float edge = 0.22f;

        // Outside bets.
        if (gy >= 3.8f && gx >= 0 && gx < 12) return Hit(RouletteArt.Mask(EvenMoney[Math.Min(5, (int)(gx / 2))].Numbers));
        if (gy >= 3f && gx >= 0 && gx < 12)
        {
            // Just under the grid still counts as a street / six line.
            if (gy < 3f + edge * 0.8f) return Inside(gx, 3f - 0.001f, edge, true);
            return Hit(RouletteArt.Mask(Dozens[Math.Min(2, (int)(gx / 4))].Numbers));
        }
        if (gx >= 12 && gy < 3) return Hit(RouletteArt.Mask(Columns[Math.Min(2, (int)gy)]));
        if (gx < 0)
        {
            if (gy >= 3) return null;
            // Zero, or the line between zero and the first column.
            if (gx > -edge) return ZeroLine(gy, edge);
            return Hit(RouletteArt.Bit(0));
        }
        if (gy >= 3) return null;
        if (gx < edge) return ZeroLine(gy, edge);
        return Inside(gx, gy, edge, false);
    }

    private (ulong, Vector2)? Hit(ulong mask) => (mask, SpotOf(mask));

    private (ulong, Vector2)? ZeroLine(float gy, float edge)
    {
        // Bottom-left corner: the first four; row boundaries: trios; otherwise a split with zero.
        if (gy > 3 - edge) return Hit(RouletteArt.Mask(0, 1, 2, 3));
        var row = Math.Clamp((int)gy, 0, 2);
        var fy = gy - row;
        int Num(int r) => 3 - r;
        if (fy < edge && row > 0) return Hit(RouletteArt.Mask(0, Num(row - 1), Num(row)));
        if (fy > 1 - edge && row < 2) return Hit(RouletteArt.Mask(0, Num(row), Num(row + 1)));
        return Hit(RouletteArt.Mask(0, Num(row)));
    }

    private (ulong, Vector2)? Inside(float gx, float gy, float edge, bool bottom)
    {
        var col = Math.Clamp((int)gx, 0, 11);
        var row = Math.Clamp((int)gy, 0, 2);
        var fx = gx - col;
        var fy = gy - row;
        int N(int c, int r) => 3 * c + (3 - r);
        var nearRight = fx > 1 - edge && col < 11;
        var nearLeft = fx < edge && col > 0;
        if (nearLeft)
        {
            col--;
            nearRight = true;
        }
        var nearBottomEdge = bottom || (row == 2 && fy > 1 - edge);
        var nearDown = !nearBottomEdge && fy > 1 - edge && row < 2;
        var nearUp = !nearBottomEdge && fy < edge && row > 0;
        if (nearUp)
        {
            row--;
            nearDown = true;
        }
        if (nearBottomEdge)
        {
            var street = new[] { N(col, 0), N(col, 1), N(col, 2) };
            if (nearRight) return Hit(RouletteArt.Mask(street.Concat([N(col + 1, 0), N(col + 1, 1), N(col + 1, 2)])));
            return Hit(RouletteArt.Mask(street));
        }
        if (nearRight && nearDown) return Hit(RouletteArt.Mask(N(col, row), N(col + 1, row), N(col, row + 1), N(col + 1, row + 1)));
        if (nearRight) return Hit(RouletteArt.Mask(N(col, row), N(col + 1, row)));
        if (nearDown) return Hit(RouletteArt.Mask(N(col, row), N(col, row + 1)));
        return Hit(RouletteArt.Bit(N(col, row)));
    }

    /// <summary>Draws the layout. <paramref name="result"/> (or -1) lights the winning number and the bets it pays.</summary>
    public void Draw(ImDrawListPtr dl, Vector4 neon, Vector4 print, Vector4 felt, int result, ulong? hover, float pulse)
    {
        var line = Pal.U(print, 0.42f);
        var t = Math.Max(1f, S(1.2f));
        // Felt.
        Gfx.Shadow(dl, Min, Max, S(4), S(10), 0.5f, new Vector2(0, S(4)));
        Gfx.ChamferPanel(dl, Min - new Vector2(S(8)), Max + new Vector2(S(8)), S(16), Pal.A(Pal.Mix(felt, Pal.Bg, 0.35f), 0.96f), Pal.A(Pal.Mix(felt, Pal.Bg, 0.75f), 0.96f), Pal.A(neon, 0.8f), S(1.4f));
        Gfx.HexGrid(dl, Min, Max, Cell * 0.32f, Pal.U(print, 0.03f));

        // Zero: a pointed cell on the left.
        var z0 = Min;
        var z1 = new Vector2(Grid.X, Grid.Y + 3 * RowH);
        Span<Vector2> zero = [new(z0.X + Cell * 0.45f, z0.Y), new(z1.X, z0.Y), new(z1.X, z1.Y), new(z0.X + Cell * 0.45f, z1.Y), new(z0.X, (z0.Y + z1.Y) / 2)];
        Gfx.FillConvex(dl, zero, Pal.U(RouletteArt.GreenPocket, result == 0 ? 0.95f : 0.8f));
        Gfx.Outline(dl, zero, line, t);
        Gfx.TextCentered(dl, FontRole.Heading, Cell * 0.5f, new Vector2((z0.X + z1.X) / 2 + Cell * 0.08f, (z0.Y + z1.Y) / 2), Pal.U(Pal.White), "0");

        // Numbers.
        for (var n = 1; n <= 36; n++)
        {
            var c = CellCenter(n);
            var half = new Vector2(Cell, RowH) / 2;
            var won = n == result;
            var col = RouletteArt.NumberColor(n);
            dl.AddRectFilled(c - half, c + half, Pal.U(Pal.Mix(col, Pal.Hex(0x0B1530), 0.2f), 0.9f));
            if (won)
            {
                dl.AddRectFilled(c - half, c + half, Pal.U(Pal.White, 0.18f + 0.12f * pulse));
                Gfx.Glow(dl, c - half, c + half, 0, Pal.Gold, Cell * 0.2f, 0.9f);
            }
            dl.AddRect(c - half, c + half, line, 0, ImDrawFlags.None, t);
            // Numbers sit in a small oval like a printed layout.
            Gfx.TextCentered(dl, FontRole.Heading, Cell * 0.42f, c + new Vector2(0, S(1)), Pal.U(Pal.White, 0.95f), n.ToString());
        }

        // Columns.
        for (var i = 0; i < 3; i++)
        {
            var (a, b) = ColumnRect(i);
            var lit = result > 0 && Columns[i].Contains(result);
            Box(dl, a, b, "2 TO 1", lit, line, t, print, pulse);
        }
        // Dozens and even money.
        for (var i = 0; i < 3; i++)
        {
            var (a, b) = DozenRect(i);
            Box(dl, a, b, Dozens[i].Label.ToUpperInvariant(), result > 0 && Dozens[i].Numbers.Contains(result), line, t, print, pulse);
        }
        for (var i = 0; i < 6; i++)
        {
            var (a, b) = EvenRect(i);
            var lit = result > 0 && EvenMoney[i].Numbers.Contains(result);
            if (i is 2 or 3)
            {
                Box(dl, a, b, "", lit, line, t, print, pulse);
                var cc = (a + b) / 2;
                var d = (b.Y - a.Y) * 0.32f;
                dl.AddQuadFilled(cc + new Vector2(0, -d), cc + new Vector2(d * 1.5f, 0), cc + new Vector2(0, d), cc + new Vector2(-d * 1.5f, 0), Pal.U(i == 2 ? RouletteArt.RedPocket : RouletteArt.BlackPocket));
                dl.AddQuad(cc + new Vector2(0, -d), cc + new Vector2(d * 1.5f, 0), cc + new Vector2(0, d), cc + new Vector2(-d * 1.5f, 0), Pal.U(print, 0.6f), t);
            }
            else
            {
                Box(dl, a, b, EvenMoney[i].Label, lit, line, t, print, pulse);
            }
        }

        // The bet under the mouse.
        if (hover is { } h)
        {
            foreach (var n in RouletteArt.Numbers(h))
            {
                if (n == 0) continue;
                var c = CellCenter(n);
                var half = new Vector2(Cell, RowH) / 2;
                dl.AddRectFilled(c - half, c + half, Pal.U(neon, 0.16f));
            }
            var spot = SpotOf(h);
            dl.AddCircle(spot, Cell * 0.26f, Pal.U(neon, 0.9f), 24, S(1.5f));
        }
    }

    private void Box(ImDrawListPtr dl, Vector2 a, Vector2 b, string label, bool lit, uint line, float t, Vector4 print, float pulse)
    {
        if (lit)
        {
            dl.AddRectFilled(a, b, Pal.U(Pal.Gold, 0.14f + 0.08f * pulse));
            Gfx.Glow(dl, a, b, 0, Pal.Gold, Cell * 0.15f, 0.6f);
        }
        dl.AddRect(a, b, line, 0, ImDrawFlags.None, t);
        if (label.Length > 0)
        {
            var fs = Math.Min(Cell * 0.34f, (b.Y - a.Y) * 0.5f);
            var ts = Gfx.TextSpacedSize(FontRole.Heading, fs, label, fs * 0.08f);
            if (ts.X > (b.X - a.X) * 0.86f)
            {
                fs *= (b.X - a.X) * 0.86f / ts.X;
                ts = Gfx.TextSpacedSize(FontRole.Heading, fs, label, fs * 0.08f);
            }
            Gfx.TextSpaced(dl, FontRole.Heading, fs, (a + b) / 2 - ts / 2, Pal.U(lit ? Pal.GoldLight : print, lit ? 1f : 0.85f), label, fs * 0.08f);
        }
    }
}
