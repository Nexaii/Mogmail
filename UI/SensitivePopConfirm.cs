using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Mogmail.Services;

namespace Mogmail.UI;

public sealed class SensitivePopConfirm : Window
{
    private Action? _onYes;
    private Action? _onNo;
    private string[] _itemNames = Array.Empty<string>();

    public SensitivePopConfirm() : base("Sensitive Pop##MogmailSensitivePop",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings)
    {
        Size = new Vector2(480, 0);
        SizeCondition = ImGuiCond.Always;
        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = true;
    }

    public void Show(IReadOnlyList<uint> eligibleRawItemIds, Action onYes, Action onNo)
    {
        _itemNames = eligibleRawItemIds
            .Select(raw => ItemRegistryClassifier.SensitiveItemName(GetBaseId(raw)))
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        _onYes = onYes;
        _onNo = onNo;
        IsOpen = true;
    }

    public override void Draw()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorWarning))
            ImGui.TextUnformatted($"{_itemNames.Length} sensitive item(s) detected in inventory:");

        ImGui.Spacing();
        foreach (var name in _itemNames)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextWrapped(name);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Pop will use at most 1 of these per session. These changes are irreversible. Continue?");
        ImGui.Spacing();
        Theme.HelperText("Pick No to run the pop without using any sensitive item.");
        Theme.SpacingSeparator();

        var no = ImGui.Button("No", new Vector2(140, 28));
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, Theme.ColorDanger))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Theme.ColorDanger))
        {
            if (ImGui.Button("Yes, allow up to 1", new Vector2(240, 28)))
            {
                _onYes?.Invoke();
                IsOpen = false;
                _onYes = null;
                _onNo = null;
                return;
            }
        }

        if (no || (ImGui.IsKeyPressed(ImGuiKey.Escape) && !ImGui.IsAnyItemActive()))
        {
            _onNo?.Invoke();
            IsOpen = false;
            _onYes = null;
            _onNo = null;
        }
    }

    private static uint GetBaseId(uint rawItemId)
    {
        var (baseId, _) = ItemUtil.GetBaseId(rawItemId);
        return baseId;
    }
}
