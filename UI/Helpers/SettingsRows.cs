using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Mogmail.UI.Helpers;

public static class SettingsRows
{
    public const float LabelWidth = 180f;
    public const float InputWidthSmall = 100f;
    public const float InputWidthMedium = 150f;
    public const float InputWidthLarge = 220f;

    private static readonly Vector4 DangerHover = new(0.80f, 0.20f, 0.20f, 1.00f);

    public static bool Checkbox(string label, ref bool value, string? tooltip = null)
    {
        var changed = ImGui.Checkbox(label, ref value);
        if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return changed;
    }

    public static bool InputInt(string label, ref int value, int step = 1, int stepFast = 10, float labelWidth = LabelWidth, float inputWidth = InputWidthSmall, string? tooltip = null)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(labelWidth);
        ImGui.SetNextItemWidth(inputWidth);
        var changed = ImGui.InputInt($"##{label}", ref value, step, stepFast);
        if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return changed;
    }

    public static bool PrimaryButton(string label, string? tooltip = null)
    {
        using var color = ImRaii.PushColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.TabHovered]);
        var clicked = ImGui.Button(label);
        if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    public static bool DangerButton(string label, string? tooltip = null)
    {
        using var color = ImRaii.PushColor(ImGuiCol.ButtonHovered, DangerHover)
                                .Push(ImGuiCol.ButtonActive, DangerHover);
        var clicked = ImGui.Button(label);
        if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return clicked;
    }

}
