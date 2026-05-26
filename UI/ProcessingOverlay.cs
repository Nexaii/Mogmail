using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mogmail.Constants;

namespace Mogmail.UI;

public sealed class ProcessingOverlay : Window
{
    private const float VerticalGap = 4f;

    public ProcessingOverlay() : base("##MogmailProcessing",
        ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoSavedSettings)
    {
        IsOpen = true;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
    }

    public override unsafe bool DrawConditions()
    {
        if (!IsAnyQueueRunning()) return false;
        var addon = GetLetterListAddon();
        return addon != null && addon->IsVisible;
    }

    public override unsafe void PreDraw()
    {
        var addon = GetLetterListAddon();
        if (addon == null) return;
        var node = addon->RootNode;
        if (node == null) return;

        var winSize = ImGui.GetWindowSize();
        var h = winSize.Y > 0 ? winSize.Y : 36f;
        ImGui.SetNextWindowPos(new Vector2(node->ScreenX + 5f, node->ScreenY - h - VerticalGap), ImGuiCond.Always);
    }

    public override void Draw()
    {
        var (label, processed, total, cancel) = GetActiveSnapshot();

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.HealerGreen, $"{label} {processed}/{total}");
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.DalamudRed);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.DalamudRed);
        if (ImGui.Button("Stop")) cancel();
        ImGui.PopStyleColor(2);

        ImGui.PopStyleVar(2);
    }

    private static bool IsAnyQueueRunning() =>
        !Plugin.Instance.ClaimQueue.IsIdle
        || !Plugin.Instance.ReadAll.IsIdle
        || !Plugin.Instance.PopQueue.IsIdle
        || Plugin.Instance.PopQueue.IsArmed;

    private static (string Label, int Processed, int Total, Action Cancel) GetActiveSnapshot()
    {
        var claim = Plugin.Instance.ClaimQueue;
        if (!claim.IsIdle)
            return (claim.CurrentLabel, claim.Processed, claim.Total, () => claim.Abort("user cancelled"));

        var read = Plugin.Instance.ReadAll;
        if (!read.IsIdle)
            return (read.CurrentLabel, read.Processed, read.Total, () => read.Abort("user cancelled"));

        var pop = Plugin.Instance.PopQueue;
        if (pop.IsArmed && pop.IsIdle)
            return ("Pop armed", 0, 0, () => pop.Disarm("user cancelled"));
        return ("Pop", pop.Processed, pop.Total, () => pop.Abort("user cancelled"));
    }

    private static unsafe AtkUnitBase* GetLetterListAddon()
    {
        return Plugin.GameGui.GetAddonByName<AtkUnitBase>(AddonNames.LetterList, 1);
    }
}
