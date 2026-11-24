using System.Numerics;
using Dalamud.Bindings.ImGui;
using SaucerNights.Core.Cards;
using SaucerNights.Ui;

namespace SaucerNights.Preview;

internal static class Gallery
{
    public static void Draw()
    {
        ImGui.SetNextWindowPos(new Vector2(0, 0));
        ImGui.SetNextWindowSize(new Vector2(1280, 820));
        ImGui.Begin("Gallery", ImGuiWindowFlags.NoDecoration);
        var dl = ImGui.GetWindowDrawList();
        var felt = new Vector2[200];
        var n = Gfx.Stadium(felt, new Vector2(20, 20), new Vector2(1260, 800));
        Gfx.FillRadial(dl, felt.AsSpan(0, n), new Vector2(640, 380), Pal.U(Pal.Hex(0x1E7A4C)), Pal.U(Pal.Hex(0x0B3F27)));
        var x = 90f;
        foreach (var c in Card.ParseMany("As Kh Qd Jc Ts 9h 8d 7c 2s Ad"))
        {
            CardArt.Draw(dl, new Vector2(x, 140), 150, c);
            x += 118;
        }
        x = 90;
        foreach (var c in Card.ParseMany("Ah 5c 3d"))
        {
            CardArt.Draw(dl, new Vector2(x, 330), 96, c);
            x += 78;
        }
        CardArt.Draw(dl, new Vector2(x, 330), 96, Card.Hidden);
        CardArt.Draw(dl, new Vector2(x + 80, 330), 96, Card.Parse("Kd"), flip: 0.3f);
        CardArt.Draw(dl, new Vector2(x + 160, 330), 96, Card.Parse("Kd"), flip: 0.7f);
        CardArt.Draw(dl, new Vector2(x + 240, 330), 96, Card.Parse("Qs"), highlight: true);
        CardArt.Draw(dl, new Vector2(x + 320, 330), 96, Card.Parse("2c"), dim: true);
        CardArt.Draw(dl, new Vector2(x + 420, 330), 60, Card.Parse("Th"));
        CardArt.Draw(dl, new Vector2(x + 470, 330), 60, Card.Hidden);
        x = 80;
        foreach (var d in ChipArt.Denoms)
        {
            ChipArt.Top(dl, new Vector2(x, 480), 28, d);
            x += 72;
        }
        ChipArt.Stack(dl, new Vector2(150, 640), 1375, 22);
        ChipArt.Stack(dl, new Vector2(400, 640), 250, 22);
        ChipArt.Stack(dl, new Vector2(650, 640), 48_500, 22);
        ChipArt.Pill(dl, new Vector2(900, 620), 12_345, 17);
        ChipArt.Pill(dl, new Vector2(900, 670), 250, 17);
        CardArt.Clover(dl, new Vector2(1100, 640), 80, Pal.Gold);
        CardArt.Clover(dl, new Vector2(1000, 560), 40, Pal.GoldLight);
        CardArt.Clover(dl, new Vector2(1060, 560), 20, Pal.GoldLight);
        Gfx.TextCentered(dl, FontRole.Display, 54, new Vector2(1000, 760), Pal.U(Pal.GoldLight), "BLACKJACK!", 0.6f);
        ImGui.End();
    }
}
