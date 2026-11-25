using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SaucerNights.Ui;

/// <summary>ImGui colours and style for Solution Nights's windows (pushed in PreDraw, popped in PostDraw).</summary>
public static class Theme
{
    private static int _colors;
    private static int _vars;

    public static void Push()
    {
        var title = Pal.Hex(0x060914);
        (ImGuiCol, Vector4)[] colors =
        [
            (ImGuiCol.WindowBg, Pal.Bg),
            (ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0)),
            (ImGuiCol.PopupBg, Pal.Hex(0x0E1428, 0.98f)),
            (ImGuiCol.Border, Pal.A(Pal.Accent, 0.28f)),
            (ImGuiCol.BorderShadow, new Vector4(0, 0, 0, 0)),
            (ImGuiCol.Separator, Pal.Line),
            (ImGuiCol.TitleBg, title),
            (ImGuiCol.TitleBgActive, Pal.Hex(0x0B1230)),
            (ImGuiCol.TitleBgCollapsed, title),
            (ImGuiCol.FrameBg, Pal.Hex(0x10172E)),
            (ImGuiCol.FrameBgHovered, Pal.Hex(0x172140)),
            (ImGuiCol.FrameBgActive, Pal.Hex(0x1C284E)),
            (ImGuiCol.Text, Pal.Text),
            (ImGuiCol.TextDisabled, Pal.Muted),
            (ImGuiCol.Button, Pal.Panel3),
            (ImGuiCol.ButtonHovered, Pal.Lighten(Pal.Panel3, 0.08f)),
            (ImGuiCol.ButtonActive, Pal.A(Pal.Accent, 0.35f)),
            (ImGuiCol.Header, Pal.A(Pal.Accent, 0.14f)),
            (ImGuiCol.HeaderHovered, Pal.A(Pal.Accent, 0.22f)),
            (ImGuiCol.HeaderActive, Pal.A(Pal.Accent, 0.32f)),
            (ImGuiCol.CheckMark, Pal.Accent),
            (ImGuiCol.SliderGrab, Pal.Accent),
            (ImGuiCol.SliderGrabActive, Pal.AccentLight),
            (ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0)),
            (ImGuiCol.ScrollbarGrab, Pal.Panel3),
            (ImGuiCol.ScrollbarGrabHovered, Pal.Lighten(Pal.Panel3, 0.1f)),
            (ImGuiCol.ScrollbarGrabActive, Pal.A(Pal.Accent, 0.5f)),
            (ImGuiCol.TableHeaderBg, Pal.Panel2),
            (ImGuiCol.TableBorderStrong, Pal.Line),
            (ImGuiCol.TableBorderLight, Pal.Line),
            (ImGuiCol.TableRowBgAlt, Pal.A(Pal.Panel2, 0.5f)),
            (ImGuiCol.TextSelectedBg, Pal.A(Pal.Accent, 0.3f)),
            (ImGuiCol.ResizeGrip, new Vector4(0, 0, 0, 0)),
            (ImGuiCol.ResizeGripHovered, Pal.A(Pal.Accent, 0.4f)),
            (ImGuiCol.ResizeGripActive, Pal.Accent),
            (ImGuiCol.NavHighlight, Pal.Accent),
        ];
        foreach (var (col, value) in colors) ImGui.PushStyleColor(col, value);
        _colors = colors.Length;

        var s = UiEnv.Scale;
        (ImGuiStyleVar, float)[] floats =
        [
            (ImGuiStyleVar.WindowRounding, 6 * s),
            (ImGuiStyleVar.ChildRounding, 4 * s),
            (ImGuiStyleVar.PopupRounding, 4 * s),
            (ImGuiStyleVar.FrameRounding, 3 * s),
            (ImGuiStyleVar.GrabRounding, 3 * s),
            (ImGuiStyleVar.ScrollbarRounding, 3 * s),
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
