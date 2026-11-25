using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.App;
using SaucerNights.Core.Cards;
using SaucerNights.Ui;

namespace SaucerNights.Preview;

/// <summary>Every card face and back, at table sizes and fanned the way the tables lay them out.</summary>
internal static class CardSheet
{
    private static float S(float v) => v * UiEnv.Scale;

    private static void Sheet(PreviewHost host, string path, string id, Action<ImDrawListPtr> draw)
    {
        host.Frame(() =>
        {
            Theme.Push();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(host.Width, host.Height));
            ImGui.Begin(id, ImGuiWindowFlags.NoDecoration);
            Theme.AfterBegin();
            var dl = ImGui.GetWindowDrawList();
            TableArt.Backdrop(dl, Vector2.Zero, new Vector2(host.Width, host.Height), FeltStyle.Neon, 3, 0.5f);
            draw(dl);
            ImGui.End();
            Theme.Pop();
        });
        host.Save(path);
    }

    private static readonly string[] Ranks = ["A", "K", "Q", "J", "T", "9", "8", "7", "6", "5", "4", "3", "2"];
    private static readonly string[] SuitCodes = ["s", "h", "d", "c"];

    /// <summary>All 52 faces at a mid table size, with the backs and four-colour suits underneath.</summary>
    public static void Deck(PreviewHost host, string path)
    {
        Sheet(host, path, "Deck", dl =>
        {
            var h = S(118);
            var step = S(98);
            var x0 = (host.Width - step * 12) / 2;
            for (var r = 0; r < 4; r++)
            for (var k = 0; k < 13; k++)
                CardArt.Draw(dl, new Vector2(x0 + k * step, S(84) + r * S(138)), h, Card.Parse(Ranks[k] + SuitCodes[r]));

            var y = S(84) + 4 * S(138) + S(14);
            var saved = CardArt.Back;
            var backs = Enum.GetValues<CardBackStyle>();
            for (var i = 0; i < backs.Length; i++)
            {
                CardArt.Back = backs[i];
                CardArt.Draw(dl, new Vector2(x0 + i * step, y), h, Card.Hidden);
            }
            CardArt.Back = saved;
            CardArt.FourColor = true;
            string[] four = ["Ac", "Td", "Kh", "Qs", "Jd", "7c"];
            for (var i = 0; i < four.Length; i++)
                CardArt.Draw(dl, new Vector2(x0 + (i + 7) * step, y), h, Card.Parse(four[i]));
            CardArt.FourColor = false;
        });
    }

    /// <summary>Every court card at three sizes, the compact sizes, four-colour suits, and courts in fanned hands.</summary>
    public static void Courts(PreviewHost host, string path)
    {
        Sheet(host, path, "Courts", dl =>
        {
            string[] codes = ["Ks", "Qs", "Js", "Kh", "Qh", "Jh", "Kd", "Qd", "Jd", "Kc", "Qc", "Jc"];
            void Row(float y, float hh, float x0, IEnumerable<string> cs, bool four = false)
            {
                var h = S(hh);
                var w = h * CardArt.Aspect;
                var x = S(x0) + w / 2;
                CardArt.FourColor = four;
                foreach (var c in cs)
                {
                    CardArt.Draw(dl, new Vector2(x, S(y) + h / 2), h, Card.Parse(c));
                    x += w + Math.Max(S(5), w * 0.1f);
                }
                CardArt.FourColor = false;
            }

            Row(16, 132, 24, codes.Take(6));
            Row(16, 132, 690, ["Kh", "Qd", "Jc", "Ks"], true);
            Row(166, 96, 24, codes.Skip(6));
            Row(166, 96, 520, ["Ah", "Ts", "7d", "2c"]);
            Row(280, 70, 24, codes);
            Row(368, 50, 24, codes);
            Row(368, 38, 760, codes.Take(8));

            // A blackjack fan and a dealer row with courts in them.
            var hb = S(120);
            var wb = hb * CardArt.Aspect;
            var fan = Card.ParseMany("Kd Qs 2h Jc");
            for (var j = 0; j < fan.Count; j++)
                CardArt.Draw(dl, new Vector2(S(90) + j * wb * 0.3f, S(620) - j * hb * 0.14f), hb, fan[j], 1, 1, j == fan.Count - 1, false, 0, j < fan.Count - 1 ? 0.3f : 1f);
            var dealer = Card.ParseMany("Qh Kc 9d");
            for (var j = 0; j < dealer.Count; j++)
                CardArt.Draw(dl, new Vector2(S(420) + j * wb * 0.8f, S(600)), hb, dealer[j], 1, 1, false, false, 0, j < dealer.Count - 1 ? 0.8f : 1f);
            var hole = Card.ParseMany("Ks Kh");
            for (var j = 0; j < 2; j++)
                CardArt.Draw(dl, new Vector2(S(720) + j * wb * 0.8f, S(600) + (j == 0 ? 1 : -1) * S(2)), hb, hole[j], 1, 1, true, false, S(5), j == 0 ? 0.8f : 1f);
            var lost = Card.ParseMany("Jd Qc");
            for (var j = 0; j < 2; j++)
                CardArt.Draw(dl, new Vector2(S(950) + j * wb * 0.8f, S(600)), hb, lost[j], 1, 1, false, true, 0, j == 0 ? 0.8f : 1f);
            CardArt.Draw(dl, new Vector2(S(1180), S(600)), hb, Card.Parse("Kh"), 0.72f);
        });
    }

    /// <summary>The awkward cards (10s, courts, aces) from the largest to the smallest size the tables use.</summary>
    public static void Sizes(PreviewHost host, string path)
    {
        Sheet(host, path, "Sizes", dl =>
        {
            float[] heights = [150, 112, 84, 64, 48, 36, 30];
            string[] cards = ["Th", "Ks", "Qd", "Jc", "As", "8d", "Tc", "?"];
            var y0 = S(14);
            for (var row = 0; row < heights.Length; row++)
            {
                var h = S(heights[row]);
                var w = h * CardArt.Aspect;
                var gap = Math.Max(S(4), w * 0.08f);
                var y = y0 + h / 2;
                y0 += h + S(16);
                var col = new[] { S(24) };
                for (var i = 0; i < cards.Length; i++)
                {
                    var c = cards[i] == "?" ? Card.Hidden : Card.Parse(cards[i]);
                    CardArt.Draw(dl, new Vector2(col[0] + w / 2 + i * (w + gap), y), h, c);
                }
                Gfx.Text(dl, FontRole.Body, S(13), new Vector2(col[0] + cards.Length * (w + gap) + S(4), y - S(8)), Pal.U(Pal.Muted), $"{heights[row]:0}");
            }
        });
    }

    /// <summary>Hands fanned exactly as the tables fan them, plus highlight, dim and mid-flip states.</summary>
    public static void Hands(PreviewHost host, string path)
    {
        Sheet(host, path, "Hands", dl =>
        {
            // Blackjack: each card steps right 30% of a width and up 14% of a height.
            void Bj(Vector2 at, float h, string codes, bool hl = false, bool dim = false)
            {
                var cs = Card.ParseMany(codes);
                var w = h * CardArt.Aspect;
                for (var j = 0; j < cs.Count; j++)
                    CardArt.Draw(dl, at + new Vector2(j * w * 0.3f, -j * h * 0.14f), h, cs[j], 1, 1, hl && j == cs.Count - 1, dim, 0, j < cs.Count - 1 ? 0.3f : 1f);
            }

            // Poker hole cards: big (mine / shown) and small (others).
            void Pk(Vector2 at, float h, string codes, bool big, bool hl = false, bool dim = false, bool me = true)
            {
                var cs = codes == "??" ? new List<Card> { Card.Hidden, Card.Hidden } : Card.ParseMany(codes);
                var w = h * CardArt.Aspect;
                var spread = me && big ? w * 0.8f : big ? w * 0.6f : w * 0.34f;
                for (var j = 0; j < cs.Count; j++)
                    CardArt.Draw(dl, at + new Vector2((j - 0.5f) * spread, (j == 0 ? 1 : -1) * S(2)), h, cs[j], 1, 1, hl, dim, 0, j == 0 ? spread / w : 1f);
            }

            // Dealer: cards 62% of a width apart.
            void Dealer(Vector2 at, float h, string codes)
            {
                var cs = codes.Split(' ').Select(x => x == "??" ? Card.Hidden : Card.Parse(x)).ToList();
                var w = h * CardArt.Aspect;
                var gap = cs.Count <= 4 ? w * 0.8f : w * 0.62f;
                for (var j = 0; j < cs.Count; j++)
                    CardArt.Draw(dl, at + new Vector2((j - (cs.Count - 1) / 2f) * gap, 0), h, cs[j], 1, 1, false, false, 0, j + 1 < cs.Count ? gap / w : 1f);
            }

            Bj(new Vector2(S(80), S(190)), S(150), "Th 2c Qd 4s 10s".Replace("10", "T"), true);
            Bj(new Vector2(S(400), S(190)), S(120), "Ac Td Kh Js");
            Bj(new Vector2(S(640), S(190)), S(96), "9d Ts 2h 3c Qc", false, true);
            Bj(new Vector2(S(860), S(190)), S(76), "Th Tc Td Ts");
            Bj(new Vector2(S(1060), S(190)), S(58), "Ks Qh Jd Tc 9s");

            Dealer(new Vector2(S(200), S(400)), S(130), "Kh ??");
            Dealer(new Vector2(S(480), S(400)), S(130), "Th Ad 9c Qs");
            Dealer(new Vector2(S(800), S(400)), S(90), "Jc Th 2d 3s 4h");
            Dealer(new Vector2(S(1100), S(400)), S(62), "Qd Td Ks");

            Pk(new Vector2(S(120), S(600)), S(134), "Th Ts", true, true);
            Pk(new Vector2(S(330), S(600)), S(100), "Kd Qc", true, false, true);
            Pk(new Vector2(S(500), S(600)), S(76), "Js Tc", true);
            Pk(new Vector2(S(640), S(600)), S(56), "Ah Kh", true, false, false, false);
            Pk(new Vector2(S(760), S(600)), S(42), "Tc 9c", true, false, false, false);
            Pk(new Vector2(S(860), S(600)), S(34), "??", false);
            Pk(new Vector2(S(940), S(600)), S(31), "??", false);

            // Flip states and the table-side shoe.
            float[] flips = [0.05f, 0.25f, 0.45f, 0.55f, 0.75f, 0.95f];
            for (var i = 0; i < flips.Length; i++)
                CardArt.Draw(dl, new Vector2(S(90) + i * S(90), S(780)), S(100), Card.Parse("Qh"), flips[i]);
            TableArt.Shoe(dl, new Vector2(S(760), S(780)), S(110));
            TableArt.Discard(dl, new Vector2(S(920), S(780)), S(110));
            string[] log = ["Th", "As", "Qd", "7c"];
            for (var i = 0; i < log.Length; i++)
                TableLog.MiniCard(dl, new Vector2(S(1040) + i * S(58), S(740)), Card.Parse(log[i]), S(13));
            for (var i = 0; i < log.Length; i++)
                TableLog.MiniCard(dl, new Vector2(S(1040) + i * S(66), S(790)), Card.Parse(log[i]), S(17));
        });
    }
}
