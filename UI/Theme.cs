using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Mogmail.UI;

public static class Theme
{
    public static readonly Vector4 ColorSubdued = new(0.75f, 0.75f, 0.75f, 1.0f);
    public static readonly Vector4 ColorDanger = new(0.85f, 0.30f, 0.30f, 1.0f);
    public static readonly Vector4 ColorWarning = new(0.95f, 0.70f, 0.30f, 1.0f);
    public static readonly Vector4 ColorSuccess = new(0.50f, 0.78f, 0.50f, 1.0f);
    public static readonly Vector4 ColorNeutral = new(0.92f, 0.92f, 0.92f, 1.0f);
    public static readonly Vector4 ColorAccent = new(0.40f, 0.65f, 0.95f, 1.0f);

    public const float ToolbarBgAlpha = 0.85f;
    public const float TooltipBgAlpha = 0.85f;
    public const float TooltipAnimMs = 150f;
    public const float TooltipExitAnimMs = 100f;
    public const float TooltipWidth = 260f;
    public const float TooltipEdgeOffset = 10f;

    public static bool DrawSectionHeader(string label)
    {
        ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
        return ImGui.CollapsingHeader(label);
    }

    public static void HelperText(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ColorSubdued))
            ImGui.TextWrapped(text);
    }

    public static void SpacingSeparator()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }
}
