using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Mogmail.Models;

namespace Mogmail.UI;

public sealed class ConfirmDialog : Window
{
    public readonly record struct ScopePreview(
        int LetterCount,
        IReadOnlyList<string> SampleSenders,
        int SkippedAttachmentCount,
        int GmProtectedCount);

    private static readonly (ClaimAction Scope, string Label)[] ScopeOptions =
    {
        (ClaimAction.DeleteEmpty, "Empty (no attachments)"),
        (ClaimAction.DeleteReadEmpty, "Read and Empty"),
        (ClaimAction.DeleteSystem, "System (purchases, rewards)"),
        (ClaimAction.DeleteAll, "All letters"),
    };

    private ClaimAction _scope = ClaimAction.DeleteReadEmpty;
    private Func<ClaimAction, ScopePreview>? _previewProvider;
    private Action<ClaimAction>? _onConfirm;
    private ScopePreview _preview;

    public ConfirmDialog() : base("Delete##MogmailConfirm",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings)
    {
        Size = new Vector2(460, 0);
        SizeCondition = ImGuiCond.Always;
        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = true;
    }

    public void Show(ClaimAction initialScope, Func<ClaimAction, ScopePreview> previewProvider, Action<ClaimAction> onConfirm)
    {
        _scope = initialScope;
        _previewProvider = previewProvider;
        _onConfirm = onConfirm;
        _preview = previewProvider(initialScope);
        IsOpen = true;
    }

    public override void Draw()
    {
        if (_previewProvider == null)
        {
            IsOpen = false;
            return;
        }

        ImGui.TextUnformatted("Choose scope:");
        ImGui.Spacing();

        var scopeIndex = (int)_scope;
        var changed = false;
        foreach (var (option, label) in ScopeOptions)
        {
            if (ImGui.RadioButton(label, ref scopeIndex, (int)option))
                changed = true;
        }
        if (changed)
        {
            _scope = (ClaimAction)scopeIndex;
            _preview = _previewProvider(_scope);
        }

        ImGui.Spacing();
        Theme.SpacingSeparator();
        ImGui.Spacing();

        ImGui.TextUnformatted($"{_preview.LetterCount} letters will be deleted.");

        if (_preview.SampleSenders.Count > 0)
        {
            ImGui.Spacing();
            Theme.HelperText("Senders:");
            foreach (var sender in _preview.SampleSenders)
                ImGui.BulletText(sender);
            if (_preview.LetterCount > _preview.SampleSenders.Count)
                Theme.HelperText($"and {_preview.LetterCount - _preview.SampleSenders.Count} more.");
        }

        if (_preview.SkippedAttachmentCount > 0)
        {
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorWarning))
                ImGui.TextWrapped($"{_preview.SkippedAttachmentCount} letters have unclaimed attachments and will be skipped. Use Take Attachment(s) first.");
        }

        if (_preview.GmProtectedCount > 0)
        {
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorAccent))
                ImGui.TextWrapped($"{_preview.GmProtectedCount} GM letters are protected and will be kept.");
        }

        Theme.SpacingSeparator();

        var cancelClicked = ImGui.Button("Cancel", new Vector2(120, 28));
        ImGui.SameLine();

        using (ImRaii.Disabled(_preview.LetterCount == 0))
        using (ImRaii.PushColor(ImGuiCol.Button, Theme.ColorDanger))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, Theme.ColorDanger))
        {
            if (ImGui.Button("Delete", new Vector2(160, 28)))
            {
                _onConfirm?.Invoke(_scope);
                IsOpen = false;
            }
        }

        if (cancelClicked || (ImGui.IsKeyPressed(ImGuiKey.Escape) && !ImGui.IsAnyItemActive()))
            IsOpen = false;
    }
}
