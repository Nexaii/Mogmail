using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Mogmail.UI;

public sealed class TakeDeleteConfirmDialog : Window
{
    private int _letterCount;
    private Action? _onConfirm;
    private bool _dontAskAgain;
    private int _centerFramesRemaining;

    public TakeDeleteConfirmDialog() : base("Take + Delete##MogmailTakeDeleteConfirm",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings)
    {
        Size = new Vector2(420, 0);
        SizeCondition = ImGuiCond.Always;
        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = true;
    }

    public void Show(int letterCount, Action onConfirm)
    {
        _letterCount = letterCount;
        _onConfirm = onConfirm;
        _dontAskAgain = false;
        _centerFramesRemaining = 2;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        if (_centerFramesRemaining <= 0) return;
        if (MailboxAddonPositioning.TryGetLetterListCenter(out var center))
            ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        _centerFramesRemaining--;
    }

    public override void Draw()
    {
        if (_onConfirm == null)
        {
            IsOpen = false;
            return;
        }

        ImGui.TextWrapped($"Claim attachments from {_letterCount} letter(s) and then delete them?");

        ImGui.Spacing();
        Theme.SpacingSeparator();

        var cancelClicked = ImGui.Button("Cancel", new Vector2(120, 28));
        ImGui.SameLine();

        using (ImRaii.PushColor(ImGuiCol.Button, Theme.ColorDanger))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Theme.ColorDanger))
        {
            if (ImGui.Button("Confirm", new Vector2(160, 28)))
            {
                if (_dontAskAgain)
                {
                    Plugin.Config.ConfirmBeforeDelete = false;
                    Plugin.Config.Save();
                }
                var callback = _onConfirm;
                IsOpen = false;
                callback.Invoke();
                return;
            }
        }

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
            ImGui.Checkbox("Don't ask again", ref _dontAskAgain);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Re-enable in Settings > Confirm before delete.");

        if (cancelClicked || (ImGui.IsKeyPressed(ImGuiKey.Escape) && !ImGui.IsAnyItemActive()))
            IsOpen = false;
    }
}
