using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SaucerNights.Ui;

public enum Tone
{
    Neutral,
    Primary,
    Success,
    Danger,
    Info,
    Ghost,
}

public enum Icon
{
    Spade,
    Heart,
    Diamond,
    Club,
    Gear,
    Info,
    Refresh,
    Users,
    Crown,
    Robot,
    Exit,
    Chat,
    Shield,
    Eye,
    Play,
    Pause,
    List,
    Copy,
    Plus,
    Minus,
    Close,
    Check,
    Panel,
    Bolt,
}

/// <summary>Custom-drawn widgets with eased hover/press states.</summary>
public static class W
{
    private static readonly Dictionary<uint, float> Hover = new();
    private static readonly Dictionary<uint, float> Switches = new();

    private static float S(float v) => UiEnv.S(v);

    private static float HoverAnim(uint id, bool hovered)
    {
        Hover.TryGetValue(id, out var v);
        v = Ease.Approach(v, hovered ? 1 : 0, 16);
        Hover[id] = v;
        return v;
    }

    public static (Vector4 Top, Vector4 Bottom, Vector4 Text, Vector4 Border) ToneColors(Tone tone) => tone switch
    {
        Tone.Primary => (Pal.Hex(0xF3C969), Pal.Hex(0xC98E2C), Pal.Hex(0x22180A), Pal.Hex(0xFFE3A3)),
        Tone.Success => (Pal.Hex(0x34B979), Pal.Hex(0x1F8553), Pal.White, Pal.Hex(0x67E0A6)),
        Tone.Danger => (Pal.Hex(0xE2575B), Pal.Hex(0xA8303A), Pal.White, Pal.Hex(0xFF8A8D)),
        Tone.Info => (Pal.Hex(0x4C8FD6), Pal.Hex(0x2C5E9E), Pal.White, Pal.Hex(0x86BDF5)),
        Tone.Ghost => (Pal.A(Pal.Panel2, 0f), Pal.A(Pal.Panel2, 0f), Pal.Muted, Pal.A(Pal.Line, 0f)),
        _ => (Pal.Hex(0x2A333F), Pal.Hex(0x1E252E), Pal.Text, Pal.Hex(0x3A4553)),
    };

    /// <summary>
    /// A rounded gradient button. <paramref name="hint"/> is a keyboard key shown in the corner; <paramref name="sub"/>
    /// is a second, smaller line (e.g. an amount).
    /// </summary>
    public static bool Button(string label, Vector2 size, Tone tone = Tone.Neutral, bool enabled = true, string? hint = null,
        string? sub = null, Icon? icon = null, FontRole font = FontRole.Body, float fontSize = 0)
    {
        var id = ImGui.GetID(label);
        var visible = label.Split("##")[0];
        var p = ImGui.GetCursorScreenPos();
        if (size.X <= 0) size.X = Gfx.TextSize(FontRole.Body, ImGui.GetFontSize(), visible).X + S(28) + (icon != null ? S(22) : 0);
        if (size.Y <= 0) size.Y = ImGui.GetFrameHeight() + S(4);
        var clicked = ImGui.InvisibleButton(label, size) && enabled;
        var hovered = ImGui.IsItemHovered() && enabled;
        var held = ImGui.IsItemActive() && enabled;
        var h = HoverAnim(id, hovered);
        var dl = ImGui.GetWindowDrawList();
        var (top, bottom, text, border) = ToneColors(tone);
        var r = Math.Min(S(9), size.Y / 2);

        if (!enabled)
        {
            top = Pal.Mix(top, Pal.Panel2, 0.75f);
            bottom = Pal.Mix(bottom, Pal.Panel2, 0.75f);
            text = Pal.A(Pal.Muted, 0.6f);
            border = Pal.Line;
        }
        else if (tone == Tone.Ghost)
        {
            top = bottom = Pal.A(Pal.Panel3, h * 0.9f);
            text = Pal.Mix(Pal.Muted, Pal.Text, h);
        }

        var lift = held ? 0 : -h * S(1.5f);
        var min = p + new Vector2(0, lift);
        var max = p + size + new Vector2(0, lift);
        if (enabled && tone != Tone.Ghost) Gfx.Shadow(dl, min, max, r, S(4) + h * S(4), 0.35f, new Vector2(0, S(2)));
        if (held)
        {
            top = Pal.Darken(top, 0.12f);
            bottom = Pal.Darken(bottom, 0.12f);
        }
        else
        {
            top = Pal.Lighten(top, 0.1f * h);
            bottom = Pal.Lighten(bottom, 0.06f * h);
        }
        Gfx.RoundedGradient(dl, min, max, r, top, bottom);
        if (tone != Tone.Ghost || h > 0.01f)
        {
            dl.AddRect(min, max, Pal.U(border, enabled ? 0.55f + 0.45f * h : 0.5f), r, ImDrawFlags.None, 1f);
            // Top highlight line.
            dl.AddLine(new Vector2(min.X + r, min.Y + 1), new Vector2(max.X - r, min.Y + 1), Pal.U(Pal.White, enabled ? 0.10f + 0.08f * h : 0.04f));
        }

        var fs = fontSize > 0 ? fontSize : ImGui.GetFontSize();
        var ts = Gfx.TextSize(font, fs, visible);
        var subSize = fs * 0.78f;
        var subTs = sub != null ? Gfx.TextSize(FontRole.Body, subSize, sub) : Vector2.Zero;
        var iconW = icon != null ? fs * 1.05f + S(6) : 0;
        var contentW = iconW + Math.Max(ts.X, subTs.X);
        var x = (min.X + max.X) / 2 - contentW / 2;
        var cy = (min.Y + max.Y) / 2;
        var tu = Pal.U(text);
        if (icon is { } ic) Icons.Draw(dl, ic, new Vector2(x + fs * 0.5f, cy), fs * 0.95f, text);
        x += iconW;
        if (sub == null)
        {
            dl.AddText(UiEnv.Font(font), fs, new Vector2(x + (contentW - iconW - ts.X) / 2, cy - ts.Y / 2), tu, visible);
        }
        else
        {
            var total = ts.Y + subTs.Y * 0.9f;
            dl.AddText(UiEnv.Font(font), fs, new Vector2(x + (contentW - iconW - ts.X) / 2, cy - total / 2), tu, visible);
            dl.AddText(UiEnv.Font(FontRole.Body, subSize), subSize, new Vector2(x + (contentW - iconW - subTs.X) / 2, cy - total / 2 + ts.Y * 0.95f), Pal.U(text, 0.8f), sub);
        }
        if (hint != null && enabled)
        {
            var hs = Gfx.TextSize(FontRole.Body, S(11), hint);
            var hp = new Vector2(max.X - hs.X - S(7), min.Y + S(4));
            dl.AddRectFilled(hp - new Vector2(S(3), S(1)), hp + hs + new Vector2(S(3), S(1)), Pal.U(Pal.Black, 0.25f), S(3));
            dl.AddText(UiEnv.Font(FontRole.Body, S(11)), S(11), hp, Pal.U(text, 0.7f), hint);
        }
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    /// <summary>Square icon button with a tooltip.</summary>
    public static bool IconButton(string id, Icon icon, float size, string tooltip, Tone tone = Tone.Ghost, bool enabled = true, bool active = false)
    {
        var p = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size)) && enabled;
        var hovered = ImGui.IsItemHovered();
        var h = HoverAnim(ImGui.GetID(id), hovered && enabled);
        var dl = ImGui.GetWindowDrawList();
        var r = S(8);
        var (top, bottom, text, _) = ToneColors(tone);
        if (tone == Tone.Ghost || active)
        {
            var bg = active ? Pal.A(Pal.Gold, 0.16f + 0.1f * h) : Pal.A(Pal.Panel3, h);
            dl.AddRectFilled(p, p + new Vector2(size), Pal.U(bg), r);
            if (active) dl.AddRect(p, p + new Vector2(size), Pal.U(Pal.Gold, 0.5f), r);
            text = active ? Pal.GoldLight : Pal.Mix(Pal.Muted, Pal.Text, h);
        }
        else
        {
            Gfx.RoundedGradient(dl, p, p + new Vector2(size), r, Pal.Lighten(top, 0.1f * h), bottom);
        }
        if (!enabled) text = Pal.A(Pal.Muted, 0.4f);
        Icons.Draw(dl, icon, p + new Vector2(size / 2), size * 0.5f, text);
        if (hovered && tooltip.Length > 0) ImGui.SetTooltip(tooltip);
        if (hovered && enabled) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    /// <summary>A small coloured label with a dot.</summary>
    public static void Pill(string text, Vector4 color, bool dot = true)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var fs = S(13);
        var ts = Gfx.TextSize(FontRole.Body, fs, text);
        var h = ts.Y + S(8);
        var w = ts.X + S(dot ? 24 : 16);
        dl.AddRectFilled(p, p + new Vector2(w, h), Pal.U(color, 0.14f), h / 2);
        dl.AddRect(p, p + new Vector2(w, h), Pal.U(color, 0.4f), h / 2);
        var x = p.X + S(8);
        if (dot)
        {
            dl.AddCircleFilled(new Vector2(x + S(3), p.Y + h / 2), S(3.2f), Pal.U(color));
            x += S(10);
        }
        dl.AddText(UiEnv.Font(FontRole.Body, fs), fs, new Vector2(x, p.Y + (h - ts.Y) / 2), Pal.U(Pal.Mix(color, Pal.White, 0.55f)), text);
        ImGui.Dummy(new Vector2(w, h));
    }

    public static float PillWidth(string text, bool dot = true) => Gfx.TextSize(FontRole.Body, S(13), text).X + S(dot ? 24 : 16);

    /// <summary>An animated on/off switch with a label and optional description.</summary>
    public static bool Toggle(string label, ref bool value, string? description = null)
    {
        var id = ImGui.GetID(label);
        var visible = label.Split("##")[0];
        var p = ImGui.GetCursorScreenPos();
        var h = S(20);
        var w = S(36);
        var textW = Gfx.TextSize(FontRole.Body, ImGui.GetFontSize(), visible).X;
        var clicked = ImGui.InvisibleButton(label, new Vector2(w + S(10) + textW, h));
        if (clicked) value = !value;
        var hovered = ImGui.IsItemHovered();
        Switches.TryGetValue(id, out var k);
        k = Ease.Approach(k, value ? 1 : 0, 18);
        Switches[id] = k;
        var dl = ImGui.GetWindowDrawList();
        var track = Pal.Mix(Pal.Panel3, Pal.Gold, k);
        dl.AddRectFilled(p, p + new Vector2(w, h), Pal.U(hovered ? Pal.Lighten(track, 0.08f) : track), h / 2);
        var knob = new Vector2(p.X + h / 2 + (w - h) * k, p.Y + h / 2);
        dl.AddCircleFilled(knob + new Vector2(0, S(1)), h / 2 - S(2), Pal.U(Pal.Black, 0.3f), 20);
        dl.AddCircleFilled(knob, h / 2 - S(2.5f), Pal.U(Pal.Mix(Pal.Muted, Pal.White, k)), 20);
        dl.AddText(new Vector2(p.X + w + S(10), p.Y + (h - ImGui.GetTextLineHeight()) / 2), Pal.U(Pal.Text), visible);
        if (description != null)
        {
            ImGui.SetCursorScreenPos(new Vector2(p.X + w + S(10), p.Y + h + S(1)));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(S(160), ImGui.GetContentRegionAvail().X - S(8)));
            ImGui.PushStyleColor(ImGuiCol.Text, Pal.Muted);
            ImGui.TextWrapped(description);
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
            ImGui.SetCursorScreenPos(new Vector2(p.X, ImGui.GetCursorScreenPos().Y + S(4)));
        }
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    /// <summary>Segmented control; returns true when the selection changed.</summary>
    public static bool Segmented(string id, string[] options, ref int index, float width = 0)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        if (width <= 0) width = ImGui.GetContentRegionAvail().X;
        var h = ImGui.GetFrameHeight() + S(2);
        var segW = width / options.Length;
        dl.AddRectFilled(p, p + new Vector2(width, h), Pal.U(Pal.Panel), h / 2.6f);
        dl.AddRect(p, p + new Vector2(width, h), Pal.U(Pal.Line), h / 2.6f);
        var key = ImGui.GetID(id);
        Switches.TryGetValue(key, out var slide);
        slide = Switches.ContainsKey(key) ? Ease.Approach(slide, index, 16) : index;
        Switches[key] = slide;
        var sMin = p + new Vector2(segW * slide + S(3), S(3));
        Gfx.RoundedGradient(dl, sMin, sMin + new Vector2(segW - S(6), h - S(6)), h / 3, Pal.Hex(0x343F4D), Pal.Hex(0x2A333F));
        dl.AddRect(sMin, sMin + new Vector2(segW - S(6), h - S(6)), Pal.U(Pal.Gold, 0.45f), h / 3);
        var changed = false;
        for (var i = 0; i < options.Length; i++)
        {
            ImGui.SetCursorScreenPos(p + new Vector2(segW * i, 0));
            if (ImGui.InvisibleButton($"{id}##{i}", new Vector2(segW, h)) && index != i)
            {
                index = i;
                changed = true;
            }
            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var col = i == index ? Pal.Text : Pal.Muted;
            Gfx.TextCentered(dl, FontRole.Body, ImGui.GetFontSize(), p + new Vector2(segW * (i + 0.5f), h / 2), Pal.U(col), options[i]);
        }
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(width, h));
        return changed;
    }

    /// <summary>A slim slider with a round knob and the value shown on the right. Returns true while it changes.</summary>
    public static bool SliderL(string id, ref long value, long min, long max, Func<long, string> label, float width = 0, long step = 1)
    {
        if (width <= 0) width = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var h = S(28);
        var text = label(value);
        var labelW = Math.Max(S(96), Gfx.TextSize(FontRole.Body, ImGui.GetFontSize(), text).X + S(18));
        var trackW = width - labelW - S(10);
        var knobR = S(9);
        ImGui.InvisibleButton(id, new Vector2(trackW, h));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var changed = false;
        if (active && max > min)
        {
            var t = Math.Clamp((ImGui.GetMousePos().X - p.X - knobR) / Math.Max(1, trackW - knobR * 2), 0, 1);
            var v = min + (long)Math.Round(t * (max - min) / step) * step;
            v = Math.Clamp(v, min, max);
            if (v != value)
            {
                value = v;
                changed = true;
            }
        }
        if (hovered && max > min)
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0)
            {
                var v = Math.Clamp(value + (long)Math.Sign(wheel) * step, min, max);
                changed |= v != value;
                value = v;
            }
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        var frac = max > min ? (float)(value - min) / (max - min) : 0;
        var cy = p.Y + h / 2;
        var x0 = p.X + knobR;
        var x1 = p.X + trackW - knobR;
        var kx = x0 + (x1 - x0) * frac;
        dl.AddRectFilled(new Vector2(x0, cy - S(3)), new Vector2(x1, cy + S(3)), Pal.U(Pal.Panel3), S(3));
        dl.AddRectFilledMultiColor(new Vector2(x0, cy - S(3)), new Vector2(kx, cy + S(3)), Pal.U(Pal.GoldDark), Pal.U(Pal.Gold), Pal.U(Pal.Gold), Pal.U(Pal.GoldDark));
        var hv = HoverAnim(ImGui.GetID(id), hovered || active);
        if (hv > 0.01f) dl.AddCircleFilled(new Vector2(kx, cy), knobR + S(5) * hv, Pal.U(Pal.Gold, 0.15f * hv), 24);
        dl.AddCircleFilled(new Vector2(kx, cy + S(1.5f)), knobR, Pal.U(Pal.Black, 0.4f), 24);
        dl.AddCircleFilled(new Vector2(kx, cy), knobR, Pal.U(Pal.Hex(0xF4EEDD)), 24);
        dl.AddCircle(new Vector2(kx, cy), knobR, Pal.U(Pal.Gold), 24, S(2));

        var lMin = new Vector2(p.X + trackW + S(10), p.Y);
        var lMax = new Vector2(p.X + width, p.Y + h);
        dl.AddRectFilled(lMin, lMax, Pal.U(Pal.Panel2), S(8));
        dl.AddRect(lMin, lMax, Pal.U(Pal.Line), S(8));
        Gfx.TextCentered(dl, FontRole.Body, ImGui.GetFontSize(), (lMin + lMax) / 2, Pal.U(Pal.Text), text);
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(width, h));
        return changed;
    }

    public static bool Slider(string id, ref int value, int min, int max, Func<int, string> label, float width = 0)
    {
        long v = value;
        if (!SliderL(id, ref v, min, max, x => label((int)x), width)) return false;
        value = (int)v;
        return true;
    }

    public static bool Slider(string id, ref byte value, int min, int max, Func<int, string> label, float width = 0)
    {
        long v = value;
        if (!SliderL(id, ref v, min, max, x => label((int)x), width)) return false;
        value = (byte)v;
        return true;
    }

    /// <summary>Float slider over [min, max] in <paramref name="steps"/> increments.</summary>
    public static bool SliderF(string id, ref float value, float min, float max, float step, Func<float, string> label, float width = 0)
    {
        var steps = (long)Math.Round((max - min) / step);
        long v = (long)Math.Round((value - min) / step);
        if (!SliderL(id, ref v, 0, steps, x => label(min + x * step), width)) return false;
        value = min + v * step;
        return true;
    }

    /// <summary>"−  value  +" amount editor; returns true when changed.</summary>
    public static bool Stepper(string id, ref long value, long min, long max, long step, float width = 0, string suffix = "")
    {
        if (width <= 0) width = ImGui.GetContentRegionAvail().X;
        var h = ImGui.GetFrameHeight() + S(2);
        var changed = false;
        ImGui.PushID(id);
        if (IconButton("minus", Icon.Minus, h, "", Tone.Neutral, value > min))
        {
            value = Math.Max(min, value - step);
            changed = true;
        }
        ImGui.SameLine(0, S(4));
        ImGui.SetNextItemWidth(width - h * 2 - S(8));
        var v = (int)Math.Clamp(value, int.MinValue, int.MaxValue);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(S(10), (h - ImGui.GetTextLineHeight()) / 2));
        if (ImGui.InputInt("##v", ref v, 0, 0, suffix.Length > 0 ? $"%d {suffix}" : "%d"))
        {
            value = Math.Clamp(v, min, max);
            changed = true;
        }
        ImGui.PopStyleVar();
        ImGui.SameLine(0, S(4));
        if (IconButton("plus", Icon.Plus, h, "", Tone.Neutral, value < max))
        {
            value = Math.Min(max, value + step);
            changed = true;
        }
        ImGui.PopID();
        return changed;
    }

    /// <summary>Upper-case caption.</summary>
    public static void Caption(string text)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var fs = S(12);
        dl.AddText(UiEnv.Font(FontRole.Body, fs), fs, p, Pal.U(Pal.Muted), text.ToUpperInvariant());
        ImGui.Dummy(new Vector2(Gfx.TextSize(FontRole.Body, fs, text.ToUpperInvariant()).X, fs + S(2)));
    }

    public static void Muted(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Pal.Muted);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public static void Heading(string text, float size = 26)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var fs = S(size);
        var ts = Gfx.TextSize(FontRole.Heading, fs, text);
        dl.AddText(UiEnv.Font(FontRole.Heading, fs), fs, p, Pal.U(Pal.Text), text);
        ImGui.Dummy(new Vector2(ts.X, ts.Y));
    }

    /// <summary>A rounded panel behind <paramref name="content"/>. Don't nest panels.</summary>
    public static void Panel(string? title, Action content, Vector4? accent = null, float padding = 16)
    {
        var pad = S(padding);
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var min = ImGui.GetCursorScreenPos();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.Dummy(new Vector2(1, pad - ImGui.GetStyle().ItemSpacing.Y));
        ImGui.Indent(pad);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - pad * 2);
        ImGui.PushItemWidth(width - pad * 2);
        if (title != null)
        {
            Caption(title);
            ImGui.Dummy(new Vector2(1, S(2)));
        }
        try
        {
            content();
        }
        finally
        {
            ImGui.PopItemWidth();
            ImGui.PopTextWrapPos();
            ImGui.Unindent(pad);
            ImGui.Dummy(new Vector2(1, pad - ImGui.GetStyle().ItemSpacing.Y));
            var max = new Vector2(min.X + width, ImGui.GetCursorScreenPos().Y);
            dl.ChannelsSetCurrent(0);
            Gfx.RoundedGradient(dl, min, max, S(12), Pal.Hex(0x18202A), Pal.Hex(0x131920));
            dl.AddRect(min, max, Pal.U(Pal.Line), S(12));
            var a = accent ?? Pal.Gold;
            dl.AddRectFilledMultiColor(min + new Vector2(S(14), 0), new Vector2(min.X + Math.Min(width - S(14), S(120)), min.Y + S(2)),
                Pal.U(a), Pal.U(a with { W = 0 }), Pal.U(a with { W = 0 }), Pal.U(a));
            dl.ChannelsMerge();
        }
        ImGui.Dummy(new Vector2(1, S(4)));
    }

    /// <summary>Row of seat dots: filled for taken seats.</summary>
    public static void SeatDots(int taken, int max)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var r = S(4.5f);
        var gap = S(13);
        for (var i = 0; i < max; i++)
        {
            var c = p + new Vector2(r + i * gap, ImGui.GetTextLineHeight() / 2);
            if (i < taken) dl.AddCircleFilled(c, r, Pal.U(Pal.Gold), 16);
            else dl.AddCircle(c, r - 0.5f, Pal.U(Pal.Muted, 0.6f), 16, 1.2f);
        }
        ImGui.Dummy(new Vector2(max * gap, ImGui.GetTextLineHeight()));
    }
}

/// <summary>Small vector icons (no icon font needed).</summary>
public static class Icons
{
    public static void Draw(ImDrawListPtr dl, Icon icon, Vector2 c, float s, Vector4 color)
    {
        var col = Pal.U(color);
        var t = Math.Max(1.2f, s * 0.11f);
        switch (icon)
        {
            case Icon.Spade: Suits.Draw(dl, Core.Cards.Suit.Spades, c, s, color); break;
            case Icon.Heart: Suits.Draw(dl, Core.Cards.Suit.Hearts, c, s, color); break;
            case Icon.Diamond: Suits.Draw(dl, Core.Cards.Suit.Diamonds, c, s, color); break;
            case Icon.Club: Suits.Draw(dl, Core.Cards.Suit.Clubs, c, s, color); break;
            case Icon.Gear:
            {
                for (var i = 0; i < 8; i++)
                {
                    var a = i * MathF.Tau / 8;
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    dl.AddLine(c + d * s * 0.28f, c + d * s * 0.5f, col, s * 0.16f);
                }
                dl.AddCircle(c, s * 0.3f, col, 20, t * 1.3f);
                dl.AddCircleFilled(c, s * 0.1f, col, 12);
                break;
            }
            case Icon.Info:
                dl.AddCircle(c, s * 0.45f, col, 24, t);
                dl.AddCircleFilled(c + new Vector2(0, -s * 0.2f), t * 0.8f, col, 8);
                dl.AddLine(c + new Vector2(0, -s * 0.05f), c + new Vector2(0, s * 0.24f), col, t * 1.2f);
                break;
            case Icon.Refresh:
                dl.PathClear();
                dl.PathArcTo(c, s * 0.36f, -MathF.PI * 0.35f, MathF.PI * 1.35f, 20);
                dl.PathStroke(col, ImDrawFlags.None, t * 1.2f);
                {
                    var tip = c + new Vector2(MathF.Cos(-MathF.PI * 0.35f), MathF.Sin(-MathF.PI * 0.35f)) * s * 0.36f;
                    dl.AddTriangleFilled(tip + new Vector2(s * 0.18f, -s * 0.02f), tip + new Vector2(-s * 0.08f, -s * 0.16f), tip + new Vector2(-s * 0.02f, s * 0.14f), col);
                }
                break;
            case Icon.Users:
                dl.AddCircleFilled(c + new Vector2(-s * 0.14f, -s * 0.16f), s * 0.15f, col, 14);
                dl.PathClear();
                dl.PathArcTo(c + new Vector2(-s * 0.14f, s * 0.32f), s * 0.28f, MathF.PI, MathF.Tau, 14);
                dl.PathFillConvex(col);
                dl.AddCircleFilled(c + new Vector2(s * 0.2f, -s * 0.12f), s * 0.12f, Pal.U(color, 0.75f), 14);
                dl.PathClear();
                dl.PathArcTo(c + new Vector2(s * 0.24f, s * 0.32f), s * 0.22f, MathF.PI, MathF.Tau, 14);
                dl.PathFillConvex(Pal.U(color, 0.75f));
                break;
            case Icon.Crown:
            {
                var b = c + new Vector2(0, s * 0.18f);
                dl.AddRectFilled(b + new Vector2(-s * 0.4f, 0), b + new Vector2(s * 0.4f, s * 0.14f), col);
                dl.AddTriangleFilled(b + new Vector2(-s * 0.4f, 0), b + new Vector2(-s * 0.42f, -s * 0.48f), b + new Vector2(-s * 0.1f, 0), col);
                dl.AddTriangleFilled(b + new Vector2(-s * 0.18f, 0), b + new Vector2(0, -s * 0.56f), b + new Vector2(s * 0.18f, 0), col);
                dl.AddTriangleFilled(b + new Vector2(s * 0.1f, 0), b + new Vector2(s * 0.42f, -s * 0.48f), b + new Vector2(s * 0.4f, 0), col);
                break;
            }
            case Icon.Robot:
                dl.AddRectFilled(c + new Vector2(-s * 0.36f, -s * 0.22f), c + new Vector2(s * 0.36f, s * 0.34f), col, s * 0.1f);
                dl.AddLine(c + new Vector2(0, -s * 0.22f), c + new Vector2(0, -s * 0.42f), col, t);
                dl.AddCircleFilled(c + new Vector2(0, -s * 0.44f), t * 1.1f, col, 8);
                dl.AddCircleFilled(c + new Vector2(-s * 0.15f, s * 0.03f), s * 0.07f, Pal.U(Pal.Bg), 10);
                dl.AddCircleFilled(c + new Vector2(s * 0.15f, s * 0.03f), s * 0.07f, Pal.U(Pal.Bg), 10);
                break;
            case Icon.Exit:
                dl.AddRect(c + new Vector2(-s * 0.4f, -s * 0.4f), c + new Vector2(s * 0.1f, s * 0.4f), col, s * 0.06f, ImDrawFlags.None, t);
                dl.AddLine(c + new Vector2(-s * 0.1f, 0), c + new Vector2(s * 0.42f, 0), col, t * 1.2f);
                dl.AddTriangleFilled(c + new Vector2(s * 0.48f, 0), c + new Vector2(s * 0.28f, -s * 0.16f), c + new Vector2(s * 0.28f, s * 0.16f), col);
                break;
            case Icon.Chat:
                dl.AddRectFilled(c + new Vector2(-s * 0.42f, -s * 0.34f), c + new Vector2(s * 0.42f, s * 0.2f), col, s * 0.12f);
                dl.AddTriangleFilled(c + new Vector2(-s * 0.2f, s * 0.18f), c + new Vector2(-s * 0.26f, s * 0.42f), c + new Vector2(s * 0.04f, s * 0.18f), col);
                break;
            case Icon.Shield:
                dl.AddQuadFilled(c + new Vector2(-s * 0.36f, -s * 0.36f), c + new Vector2(s * 0.36f, -s * 0.36f), c + new Vector2(s * 0.3f, s * 0.12f), c + new Vector2(-s * 0.3f, s * 0.12f), col);
                dl.AddTriangleFilled(c + new Vector2(-s * 0.3f, s * 0.1f), c + new Vector2(s * 0.3f, s * 0.1f), c + new Vector2(0, s * 0.48f), col);
                dl.AddPolyline(ref Check(c, s)[0], 3, Pal.U(Pal.Bg), ImDrawFlags.None, t * 1.2f);
                break;
            case Icon.Check:
                dl.AddPolyline(ref Check(c, s * 1.4f)[0], 3, col, ImDrawFlags.None, t * 1.4f);
                break;
            case Icon.Eye:
                dl.PathClear();
                dl.PathArcTo(c + new Vector2(0, s * 0.35f), s * 0.58f, -MathF.PI * 0.82f, -MathF.PI * 0.18f, 16);
                dl.PathArcTo(c - new Vector2(0, s * 0.35f), s * 0.58f, MathF.PI * 0.18f, MathF.PI * 0.82f, 16);
                dl.PathStroke(col, ImDrawFlags.Closed, t);
                dl.AddCircleFilled(c, s * 0.14f, col, 12);
                break;
            case Icon.Play:
                dl.AddTriangleFilled(c + new Vector2(-s * 0.26f, -s * 0.36f), c + new Vector2(s * 0.38f, 0), c + new Vector2(-s * 0.26f, s * 0.36f), col);
                break;
            case Icon.Pause:
                dl.AddRectFilled(c + new Vector2(-s * 0.3f, -s * 0.34f), c + new Vector2(-s * 0.08f, s * 0.34f), col, s * 0.04f);
                dl.AddRectFilled(c + new Vector2(s * 0.08f, -s * 0.34f), c + new Vector2(s * 0.3f, s * 0.34f), col, s * 0.04f);
                break;
            case Icon.List:
                for (var i = -1; i <= 1; i++)
                {
                    dl.AddCircleFilled(c + new Vector2(-s * 0.32f, i * s * 0.26f), t * 0.9f, col, 8);
                    dl.AddLine(c + new Vector2(-s * 0.16f, i * s * 0.26f), c + new Vector2(s * 0.4f, i * s * 0.26f), col, t);
                }
                break;
            case Icon.Copy:
                dl.AddRect(c + new Vector2(-s * 0.36f, -s * 0.24f), c + new Vector2(s * 0.16f, s * 0.4f), col, s * 0.06f, ImDrawFlags.None, t);
                dl.AddRect(c + new Vector2(-s * 0.14f, -s * 0.42f), c + new Vector2(s * 0.38f, s * 0.2f), col, s * 0.06f, ImDrawFlags.None, t);
                break;
            case Icon.Plus:
                dl.AddLine(c - new Vector2(s * 0.32f, 0), c + new Vector2(s * 0.32f, 0), col, t * 1.3f);
                dl.AddLine(c - new Vector2(0, s * 0.32f), c + new Vector2(0, s * 0.32f), col, t * 1.3f);
                break;
            case Icon.Minus:
                dl.AddLine(c - new Vector2(s * 0.32f, 0), c + new Vector2(s * 0.32f, 0), col, t * 1.3f);
                break;
            case Icon.Close:
                dl.AddLine(c - new Vector2(s * 0.28f), c + new Vector2(s * 0.28f), col, t * 1.3f);
                dl.AddLine(c + new Vector2(-s * 0.28f, s * 0.28f), c + new Vector2(s * 0.28f, -s * 0.28f), col, t * 1.3f);
                break;
            case Icon.Panel:
                dl.AddRect(c - new Vector2(s * 0.42f, s * 0.34f), c + new Vector2(s * 0.42f, s * 0.34f), col, s * 0.08f, ImDrawFlags.None, t);
                dl.AddRectFilled(c + new Vector2(s * 0.1f, -s * 0.34f), c + new Vector2(s * 0.42f, s * 0.34f), col, s * 0.08f);
                break;
            case Icon.Bolt:
                dl.AddTriangleFilled(c + new Vector2(s * 0.12f, -s * 0.5f), c + new Vector2(-s * 0.3f, s * 0.08f), c + new Vector2(s * 0.02f, s * 0.08f), col);
                dl.AddTriangleFilled(c + new Vector2(-s * 0.02f, -s * 0.08f), c + new Vector2(s * 0.3f, -s * 0.08f), c + new Vector2(-s * 0.12f, s * 0.5f), col);
                break;
        }
    }

    private static Vector2[] Check(Vector2 c, float s) =>
        [c + new Vector2(-s * 0.18f, -s * 0.02f), c + new Vector2(-s * 0.04f, s * 0.13f), c + new Vector2(s * 0.2f, -s * 0.16f)];
}
