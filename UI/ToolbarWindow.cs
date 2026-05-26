using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mogmail.Constants;
using Mogmail.Managers;
using Mogmail.Models;

namespace Mogmail.UI;

public sealed class ToolbarWindow : Window
{
    private const float IconicButtonSizeSmall = 28f;
    private const float IconicButtonSizeLarge = 32f;
    private const float Gap = 5f;
    private const float VerticalOffset = 5f;
    private const long OrientationTransitionSuppressMs = 350;

    private static readonly Vector4 ButtonBg = new(0f, 0f, 0f, 0.67f);
    private static readonly Vector4 ButtonHovered = new(0.28f, 0.28f, 0.32f, 0.95f);
    private static readonly Vector4 ButtonActive = new(0.35f, 0.35f, 0.40f, 1.0f);
    private static readonly Vector4 TransparentColor = new(0f, 0f, 0f, 0f);

    private readonly HoverTooltipDrawer _tooltips = new();
    private readonly ConfirmDialog _confirmDialog;
    private readonly Dictionary<string, Vector2> _buttonCenters = new();

    private Vector2 _windowPos;
    private Vector2 _windowSize;
    private static long _suppressTooltipsUntilMs;

    private static float IconicButtonSize() => Plugin.Config.UseLargeToolbar ? IconicButtonSizeLarge : IconicButtonSizeSmall;

    public ToolbarWindow(ConfirmDialog confirmDialog)
        : base("##MogmailToolbar",
            ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoMove)
    {
        _confirmDialog = confirmDialog;
        IsOpen = true;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
    }

    public override unsafe bool DrawConditions()
    {
        var addon = GetLetterListAddon();
        return addon != null && addon->IsVisible;
    }

    public override void PreDraw()
    {
        ApplyWindowPosition();
        PushWindowStyle();
    }

    public override void PostDraw()
    {
        PopWindowStyle();
    }

    private unsafe void ApplyWindowPosition()
    {
        var attach = EffectiveAttach();
        var addon = GetLetterListAddon();
        if (addon == null) return;

        var node = addon->RootNode;
        if (node == null) return;

        var addonX = node->ScreenX;
        var addonY = node->ScreenY;
        var addonW = addon->GetScaledWidth(true);

        var lastWidth = _windowSize.X > 0 ? _windowSize.X : EstimateInitialWidth();
        var pos = attach switch
        {
            ToolbarAttach.SnappedLeft => new Vector2(addonX - lastWidth - Gap, addonY + VerticalOffset),
            ToolbarAttach.SnappedRight => new Vector2(addonX + addonW + Gap, addonY + VerticalOffset),
            _ => new Vector2(addonX - lastWidth - Gap, addonY),
        };

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
    }

    private void PushWindowStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2f, 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, TransparentColor);
        ImGui.PushStyleColor(ImGuiCol.Border, TransparentColor);
    }

    private static void PopWindowStyle()
    {
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(4);
    }

    public override void Draw()
    {
        _windowPos = ImGui.GetWindowPos();
        _windowSize = ImGui.GetWindowSize();

        DrawVertical(ComputeEnabled());
    }

    public void DrawTooltipsOverlay()
    {
        if (Environment.TickCount64 < _suppressTooltipsUntilMs) return;
        foreach (var id in EnumerateButtons())
        {
            var spec = GetButtonSpec(id);
            if (!_buttonCenters.TryGetValue(spec.Key, out var center)) continue;
            var (anchor, pivot, side) = ComputeTooltipAnchor(center);
            _tooltips.DrawTooltip(spec.Key, spec.Label, spec.Description, anchor, pivot, side, Theme.TooltipWidth);
        }
    }

    private (Vector2 Anchor, Vector2 Pivot, TooltipSide Side) ComputeTooltipAnchor(Vector2 buttonCenter)
    {
        var attach = EffectiveAttach();
        return attach == ToolbarAttach.SnappedRight
            ? (new Vector2(_windowPos.X + _windowSize.X + Theme.TooltipEdgeOffset, buttonCenter.Y),
               new Vector2(0f, 0.5f),
               TooltipSide.Right)
            : (new Vector2(_windowPos.X - Theme.TooltipEdgeOffset, buttonCenter.Y),
               new Vector2(1f, 0.5f),
               TooltipSide.Left);
    }

    private void DrawVertical(bool enabled)
    {
        foreach (var id in EnumerateButtons())
            DrawButton(id, enabled);
    }

    private void DrawButton(ButtonId id, bool enabled)
    {
        var spec = GetButtonSpec(id);
        var effective = enabled || spec.AlwaysEnabled;
        using var disabled = ImRaii.Disabled(!effective);

        var startPos = ImGui.GetCursorScreenPos();
        var clicked = DrawIconicButton(spec);
        var rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);

        var center = startPos + ImGui.GetItemRectSize() * 0.5f;
        _buttonCenters[spec.Key] = center;
        _tooltips.TrackHover(spec.Key, ImGui.IsItemHovered());

        if (clicked && effective)
            spec.OnClick();
        else if (rightClicked && effective && spec.OnRightClick != null)
            spec.OnRightClick();
    }

    private static bool DrawIconicButton(ButtonSpec spec)
    {
        var size = IconicButtonSize();
        using (ImRaii.PushColor(ImGuiCol.Button, ButtonBg))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, ButtonHovered))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, ButtonActive))
        using (ImRaii.PushColor(ImGuiCol.Text, spec.Color))
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            return ImGui.Button($"{spec.Icon.ToIconString()}##{spec.Key}", new Vector2(size, size));
        }
    }

    private IEnumerable<ButtonId> EnumerateButtons()
    {
        yield return ButtonId.Take;
        yield return ButtonId.ReadAll;
        yield return ButtonId.AutoPopToggle;
        yield return ButtonId.Delete;
        yield return ButtonId.CycleOrientation;
        yield return ButtonId.Settings;
    }

    private ButtonSpec GetButtonSpec(ButtonId id) => id switch
    {
        ButtonId.Take => new ButtonSpec(
            "btn_take",
            "Take",
            FontAwesomeIcon.EnvelopeOpenText,
            Theme.ColorSuccess,
            "Left-click: Claim attachments. Right-click: Claim then delete.",
            OnTakeClicked,
            OnRightClick: OnTakeAndDeleteClicked),
        ButtonId.ReadAll => new ButtonSpec(
            "btn_readall",
            "Read All",
            FontAwesomeIcon.EnvelopeOpen,
            Theme.ColorAccent,
            "Left-click: Mark all as read. Right-click: Mark all as read then delete.",
            OnReadAllClicked,
            OnRightClick: OnReadAllAndDeleteClicked),
        ButtonId.Delete => new ButtonSpec(
            "btn_delete",
            "Delete",
            FontAwesomeIcon.Times,
            Theme.ColorDanger,
            "Delete all read mail.",
            OnDeleteClicked),
        ButtonId.AutoPopToggle => new ButtonSpec(
            "btn_autopop",
            Plugin.Config.AutoPopAfterTake ? "Auto Pop: On" : "Auto Pop: Off",
            FontAwesomeIcon.BoxOpen,
            Plugin.Config.AutoPopAfterTake ? Theme.ColorSuccess : Theme.ColorSubdued,
            "Toggle auto-pop. When on, registrable items in inventory are used after every Take.",
            OnAutoPopToggleClicked,
            AlwaysEnabled: true),
        ButtonId.CycleOrientation => new ButtonSpec(
            "btn_cycle",
            "Side",
            Plugin.Config.ToolbarAttach == ToolbarAttach.SnappedRight
                ? FontAwesomeIcon.ChevronLeft
                : FontAwesomeIcon.ChevronRight,
            Theme.ColorNeutral,
            "Toggle toolbar side.",
            OnCycleOrientationClicked,
            AlwaysEnabled: true),
        ButtonId.Settings => new ButtonSpec(
            "btn_settings",
            "Settings",
            FontAwesomeIcon.Cog,
            Theme.ColorNeutral,
            "Open settings.",
            OnSettingsClicked,
            AlwaysEnabled: true),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
    };

    private static void SetAttach(ToolbarAttach attach)
    {
        Plugin.Config.ToolbarAttach = attach;
        Plugin.Config.Save();
    }

    private static ToolbarAttach EffectiveAttach() => Plugin.Config.ToolbarAttach;

    private static bool ComputeEnabled()
    {
        if (!Plugin.ClientState.IsLoggedIn) return false;
        if (!Plugin.Instance.Mailbox.IsAvailable) return false;
        if (!Plugin.Instance.ClaimQueue.IsIdle) return false;
        if (!Plugin.Instance.PopQueue.IsIdle) return false;
        if (!Plugin.Instance.ReadAll.IsIdle) return false;
        return true;
    }

    private static IReadOnlyList<int> BuildCandidates(ClaimAction action, bool includeGM)
    {
        const ulong PlayerThreshold = 100_000_000_000UL;
        var mailbox = Plugin.Instance.Mailbox;
        var count = (int)mailbox.LoadedLetterCount;
        var result = new List<int>();
        for (var i = 0; i < count; i++)
        {
            if (!includeGM && mailbox.IsGMLetter(i)) continue;
            switch (action)
            {
                case ClaimAction.Take:
                    if (mailbox.LetterHasUnclaimedAttachments(i)) result.Add(i);
                    break;
                case ClaimAction.DeleteEmpty:
                    if (!mailbox.LetterHasUnclaimedAttachments(i)) result.Add(i);
                    break;
                case ClaimAction.DeleteReadEmpty:
                    if (mailbox.IsLetterReadFlag(i) && !mailbox.LetterHasUnclaimedAttachments(i)) result.Add(i);
                    break;
                case ClaimAction.DeleteSystem:
                    if (mailbox.GetSenderContentId(i) < PlayerThreshold) result.Add(i);
                    break;
                case ClaimAction.DeleteAll:
                    result.Add(i);
                    break;
            }
        }
        return result;
    }

    private static int CountGMInMailbox()
    {
        var mailbox = Plugin.Instance.Mailbox;
        var count = (int)mailbox.LoadedLetterCount;
        var n = 0;
        for (var i = 0; i < count; i++) if (mailbox.IsGMLetter(i)) n++;
        return n;
    }

    private static float EstimateInitialWidth() =>
        (Plugin.Config.UseLargeToolbar ? IconicButtonSizeLarge : 24f) + 20f;

    private static unsafe AtkUnitBase* GetLetterListAddon()
    {
        return Plugin.GameGui.GetAddonByName<AtkUnitBase>(AddonNames.LetterList, 1);
    }

    private static void OnTakeClicked()
    {
        var candidates = BuildCandidates(ClaimAction.Take, includeGM: false);
        if (candidates.Count == 0)
        {
            Plugin.Chat.Print("[Mogmail] no letters with attachments to claim.");
            return;
        }
        Plugin.Instance.ClaimQueue.StartTake("Take Attachment(s)", candidates);
    }

    private static void OnTakeAndDeleteClicked()
    {
        var candidates = BuildCandidates(ClaimAction.Take, includeGM: false);
        if (candidates.Count == 0)
        {
            Plugin.Chat.Print("[Mogmail] no letters with attachments to claim.");
            return;
        }
        Plugin.Instance.ClaimQueue.StartTakeAndDelete("Take + Delete", candidates);
    }

    private static void OnSettingsClicked() => Plugin.Instance.OpenSettings();

    private static void OnReadAllClicked()
    {
        Plugin.Instance.ReadAll.Start("Read All", deleteAfter: false);
    }

    private static void OnReadAllAndDeleteClicked()
    {
        Plugin.Instance.ReadAll.Start("Read All + Delete", deleteAfter: true);
    }

    private static void OnAutoPopToggleClicked()
    {
        Plugin.Config.AutoPopAfterTake = !Plugin.Config.AutoPopAfterTake;
        Plugin.Config.Save();
    }

    private void OnCycleOrientationClicked()
    {
        var next = NextOrientation(Plugin.Config.ToolbarAttach);
        SetAttach(next);
        _tooltips.Reset();
        _suppressTooltipsUntilMs = Environment.TickCount64 + OrientationTransitionSuppressMs;
    }

    private static ToolbarAttach NextOrientation(ToolbarAttach current) => current switch
    {
        ToolbarAttach.SnappedLeft => ToolbarAttach.SnappedRight,
        ToolbarAttach.SnappedRight => ToolbarAttach.SnappedLeft,
        _ => ToolbarAttach.SnappedLeft,
    };

    private void OnDeleteClicked()
    {
        var includeGM = Plugin.Config.IncludeGMInSweeps;

        if (!Plugin.Config.ConfirmBeforeDelete)
        {
            FireDeleteForScope(Plugin.Config.LastDeleteScope, includeGM);
            return;
        }

        _confirmDialog.Show(
            Plugin.Config.LastDeleteScope,
            scope => BuildScopePreview(scope, includeGM),
            scope =>
            {
                Plugin.Config.LastDeleteScope = scope;
                Plugin.Config.Save();
                FireDeleteForScope(scope, includeGM);
            });
    }

    private static ConfirmDialog.ScopePreview BuildScopePreview(ClaimAction scope, bool includeGM)
    {
        var candidates = BuildCandidates(scope, includeGM);
        var mailbox = Plugin.Instance.Mailbox;
        var skipped = candidates.Count(i => mailbox.LetterHasUnclaimedAttachments(i));
        var actualCount = candidates.Count - skipped;
        var samples = candidates.Take(5).Select(i => mailbox.GetSenderName(i)).ToList();
        var protectedGM = includeGM ? 0 : CountGMInMailbox();
        return new ConfirmDialog.ScopePreview(actualCount, samples, skipped, protectedGM);
    }

    private static void FireDeleteForScope(ClaimAction scope, bool includeGM)
    {
        var label = ScopeLabel(scope);
        var preview = BuildScopePreview(scope, includeGM);
        if (preview.LetterCount == 0)
        {
            Plugin.Chat.Print($"[Mogmail] {label}: no matching letters.");
            return;
        }
        var predicate = BuildDeletePredicate(scope, includeGM);
        var budget = preview.LetterCount + 2;
        Plugin.Instance.ClaimQueue.StartDelete(scope, label, predicate, budget);
    }

    private static string ScopeLabel(ClaimAction scope) => scope switch
    {
        ClaimAction.DeleteEmpty => "Delete (Empty)",
        ClaimAction.DeleteReadEmpty => "Delete (Read & Empty)",
        ClaimAction.DeleteSystem => "Delete (System)",
        ClaimAction.DeleteAll => "Delete (All)",
        _ => "Delete",
    };

    private static Func<int, bool> BuildDeletePredicate(ClaimAction action, bool includeGM)
    {
        const ulong PlayerThreshold = 100_000_000_000UL;
        var mailbox = Plugin.Instance.Mailbox;
        return action switch
        {
            ClaimAction.DeleteEmpty => i =>
                (includeGM || !mailbox.IsGMLetter(i))
                && !mailbox.LetterHasUnclaimedAttachments(i),
            ClaimAction.DeleteReadEmpty => i =>
                (includeGM || !mailbox.IsGMLetter(i))
                && mailbox.IsLetterReadFlag(i)
                && !mailbox.LetterHasUnclaimedAttachments(i),
            ClaimAction.DeleteSystem => i =>
                (includeGM || !mailbox.IsGMLetter(i))
                && mailbox.GetSenderContentId(i) < PlayerThreshold,
            ClaimAction.DeleteAll => i =>
                includeGM || !mailbox.IsGMLetter(i),
            _ => _ => false,
        };
    }

    private enum ButtonId { Take, ReadAll, Delete, AutoPopToggle, CycleOrientation, Settings }

    private readonly record struct ButtonSpec(
        string Key,
        string Label,
        FontAwesomeIcon Icon,
        Vector4 Color,
        string Description,
        Action OnClick,
        bool AlwaysEnabled = false,
        Action? OnRightClick = null);
}
