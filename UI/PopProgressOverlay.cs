using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mogmail.Constants;

namespace Mogmail.UI;

public sealed class PopProgressOverlay : Window
{
    private const float VerticalGap = 4f;

    public PopProgressOverlay() : base("##MogmailPopProgress",
        ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoSavedSettings)
    {
        IsOpen = true;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
    }

    public override unsafe bool DrawConditions()
    {
        var pop = Plugin.Instance.PopQueue;
        if (pop.IsIdle && !pop.IsArmed) return false;
        var addon = GetLetterListAddon();
        return addon == null || !addon->IsVisible;
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowBgAlpha(0.85f);
        var savedX = Plugin.Config.PopOverlayPosX;
        var savedY = Plugin.Config.PopOverlayPosY;
        if (savedX >= 0 && savedY >= 0)
        {
            ImGui.SetNextWindowPos(new Vector2(savedX, savedY), ImGuiCond.FirstUseEver);
            return;
        }
        ImGui.SetNextWindowPos(GetDefaultPosition(), ImGuiCond.FirstUseEver);
    }

    public override void Draw()
    {
        var pop = Plugin.Instance.PopQueue;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);

        ImGui.AlignTextToFramePadding();
        if (pop.IsArmed && pop.IsIdle)
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Pop armed");
        else
            ImGui.TextColored(ImGuiColors.HealerGreen, $"Pop {pop.Processed}/{pop.Total}");

        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.DalamudRed);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.DalamudRed);
        if (ImGui.Button("Stop"))
        {
            if (pop.IsArmed) pop.Disarm("user cancelled");
            if (!pop.IsIdle) pop.Abort("user cancelled");
        }
        ImGui.PopStyleColor(2);

        ImGui.PopStyleVar(2);
    }

    public override void PostDraw()
    {
        var pos = ImGui.GetWindowPos();
        if (Math.Abs(pos.X - Plugin.Config.PopOverlayPosX) > 0.5f
            || Math.Abs(pos.Y - Plugin.Config.PopOverlayPosY) > 0.5f)
        {
            Plugin.Config.PopOverlayPosX = pos.X;
            Plugin.Config.PopOverlayPosY = pos.Y;
            Plugin.Config.Save();
        }
    }

    private static unsafe Vector2 GetDefaultPosition()
    {
        var addon = GetLetterListAddon();
        if (addon != null)
        {
            var node = addon->RootNode;
            if (node != null)
            {
                var winSize = ImGui.GetWindowSize();
                var h = winSize.Y > 0 ? winSize.Y : 36f;
                return new Vector2(node->ScreenX + 5f, node->ScreenY - h - VerticalGap);
            }
        }
        var vp = ImGui.GetMainViewport();
        return new Vector2(vp.Pos.X + (vp.Size.X - 220f) * 0.5f, vp.Pos.Y + 80f);
    }

    private static unsafe AtkUnitBase* GetLetterListAddon()
    {
        return Plugin.GameGui.GetAddonByName<AtkUnitBase>(AddonNames.LetterList, 1);
    }
}
