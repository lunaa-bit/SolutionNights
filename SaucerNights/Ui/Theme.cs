using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SaucerNights.Ui;

/// <summary>ImGui colours and style for Saucer Nights's windows (pushed in PreDraw, popped in PostDraw).</summary>
public static class Theme
{
    private static int _colors;
    private static int _vars;

    public static void Push()
    {
        var title = Pal.Hex(0x0B0E13);
        (ImGuiCol, Vector4)[] colors =
        [
            (ImGuiCol.WindowBg, Pal.Bg),
            (ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0)),
            (ImGuiCol.PopupBg, Pal.Hex(0x161C24, 0.98f)),
            (ImGuiCol.Border, Pal.Line),
            (ImGuiCol.Separator, Pal.Line),
            (ImGuiCol.TitleBg, title),
            (ImGuiCol.TitleBgActive, Pal.Hex(0x111721)),
            (ImGuiCol.TitleBgCollapsed, title),
            (ImGuiCol.FrameBg, Pal.Hex(0x1A212A)),
            (ImGuiCol.FrameBgHovered, Pal.Hex(0x222B36)),
            (ImGuiCol.FrameBgActive, Pal.Hex(0x28323E)),
            (ImGuiCol.Text, Pal.Text),
            (ImGuiCol.TextDisabled, Pal.Muted),
            (ImGuiCol.Button, Pal.Panel3),
            (ImGuiCol.ButtonHovered, Pal.Lighten(Pal.Panel3, 0.08f)),
            (ImGuiCol.ButtonActive, Pal.A(Pal.Gold, 0.35f)),
            (ImGuiCol.Header, Pal.A(Pal.Gold, 0.14f)),
            (ImGuiCol.HeaderHovered, Pal.A(Pal.Gold, 0.24f)),
            (ImGuiCol.HeaderActive, Pal.A(Pal.Gold, 0.34f)),
            (ImGuiCol.CheckMark, Pal.Gold),
            (ImGuiCol.SliderGrab, Pal.Gold),
            (ImGuiCol.SliderGrabActive, Pal.GoldLight),
            (ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0)),
            (ImGuiCol.ScrollbarGrab, Pal.Panel3),
            (ImGuiCol.ScrollbarGrabHovered, Pal.Lighten(Pal.Panel3, 0.1f)),
            (ImGuiCol.ScrollbarGrabActive, Pal.A(Pal.Gold, 0.5f)),
            (ImGuiCol.TableHeaderBg, Pal.Panel2),
            (ImGuiCol.TableBorderStrong, Pal.Line),
            (ImGuiCol.TableBorderLight, Pal.Line),
            (ImGuiCol.TableRowBgAlt, Pal.A(Pal.Panel2, 0.5f)),
            (ImGuiCol.TextSelectedBg, Pal.A(Pal.Gold, 0.3f)),
            (ImGuiCol.ResizeGrip, new Vector4(0, 0, 0, 0)),
            (ImGuiCol.ResizeGripHovered, Pal.A(Pal.Gold, 0.4f)),
            (ImGuiCol.ResizeGripActive, Pal.Gold),
            (ImGuiCol.NavHighlight, Pal.Gold),
        ];
        foreach (var (col, value) in colors) ImGui.PushStyleColor(col, value);
        _colors = colors.Length;

        var s = UiEnv.Scale;
        (ImGuiStyleVar, float)[] floats =
        [
            (ImGuiStyleVar.WindowRounding, 12 * s),
            (ImGuiStyleVar.ChildRounding, 10 * s),
            (ImGuiStyleVar.PopupRounding, 10 * s),
            (ImGuiStyleVar.FrameRounding, 8 * s),
            (ImGuiStyleVar.GrabRounding, 8 * s),
            (ImGuiStyleVar.ScrollbarRounding, 8 * s),
            (ImGuiStyleVar.ScrollbarSize, 9 * s),
            (ImGuiStyleVar.WindowBorderSize, 1),
            (ImGuiStyleVar.FrameBorderSize, 0),
            (ImGuiStyleVar.PopupBorderSize, 1),
        ];
        foreach (var (v, value) in floats) ImGui.PushStyleVar(v, value);
        (ImGuiStyleVar, Vector2)[] vectors =
        [
            (ImGuiStyleVar.FramePadding, new Vector2(10, 6) * s),
            (ImGuiStyleVar.ItemSpacing, new Vector2(8, 7) * s),
            (ImGuiStyleVar.ItemInnerSpacing, new Vector2(6, 4) * s),
            (ImGuiStyleVar.WindowTitleAlign, new Vector2(0.02f, 0.5f)),
            // Last, so AfterBegin can pop it: the window itself is edge-to-edge, popups and tooltips keep padding.
            (ImGuiStyleVar.WindowPadding, Vector2.Zero),
        ];
        foreach (var (v, value) in vectors) ImGui.PushStyleVar(v, value);
        _vars = floats.Length + vectors.Length;
    }

    /// <summary>Call first thing inside the window: restores padding for child windows, popups and tooltips.</summary>
    public static void AfterBegin()
    {
        ImGui.PopStyleVar();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * UiEnv.Scale);
    }

    public static void Pop()
    {
        ImGui.PopStyleVar(_vars);
        ImGui.PopStyleColor(_colors);
    }
}
