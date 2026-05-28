using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Mogmail.Services;
using Mogmail.UI.Helpers;

namespace Mogmail.UI.Tabs;

public sealed class PopTab : ISettingsTab
{
    private static readonly PopCategory[] AllCategories = (PopCategory[])Enum.GetValues(typeof(PopCategory));

    private static readonly (string Label, Func<bool> Get, Action<bool> Set)[] SensitiveToggles =
    {
        ("Allow: Phial of Fantasia",            () => Plugin.Config.AllowFantasiaInPop,            v => Plugin.Config.AllowFantasiaInPop = v),
        ("Allow: Main Scenario Progression",    () => Plugin.Config.AllowMsqProgressionInPop,      v => Plugin.Config.AllowMsqProgressionInPop = v),
        ("Allow: One Hero's Journey",           () => Plugin.Config.AllowOneHerosJourneyInPop,     v => Plugin.Config.AllowOneHerosJourneyInPop = v),
        ("Allow: One Retainer's Journey",       () => Plugin.Config.AllowOneRetainersJourneyInPop, v => Plugin.Config.AllowOneRetainersJourneyInPop = v),
    };

    public string Name => "Pop";

    public void Draw()
    {
        if (!ImGui.BeginChild("PopTabScroll", new Vector2(0, 0), true)) { ImGui.EndChild(); return; }
        try
        {
            if (Theme.DrawSectionHeader("Auto Pop")) DrawAutoPopSection();
            ImGui.Spacing();

            var enabledCount = CountEnabled();
            if (Theme.DrawSectionHeader($"Pop Categories [{enabledCount} of {AllCategories.Length} enabled]##popcat")) DrawCategoriesSection();
            ImGui.Spacing();

            if (Theme.DrawSectionHeader("Sensitive items")) DrawSensitiveSection();
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private static void DrawAutoPopSection()
    {
        var autoPop = Plugin.Config.AutoPopAfterTake;
        if (SettingsRows.Checkbox("Enable Auto Pop items after use of Take.", ref autoPop))
        {
            Plugin.Config.AutoPopAfterTake = autoPop;
            Plugin.Config.Save();
        }
    }

    private static void DrawCategoriesSection()
    {
        if (SettingsRows.PrimaryButton("Enable all"))
        {
            Plugin.Config.PopCategoryMask = ulong.MaxValue;
            Plugin.Config.Save();
        }
        ImGui.SameLine();
        if (SettingsRows.PrimaryButton("Disable all"))
        {
            Plugin.Config.PopCategoryMask = 0UL;
            Plugin.Config.Save();
        }

        ImGui.Spacing();
        DrawCategoryGrid();
    }

    private static void DrawSensitiveSection()
    {
        Theme.HelperText("Opt-in. One per Pop run.");
        ImGui.Spacing();

        var flags = ImGuiTableFlags.Borders
                  | ImGuiTableFlags.RowBg
                  | ImGuiTableFlags.NoSavedSettings
                  | ImGuiTableFlags.SizingStretchSame;

        using var table = ImRaii.Table("MogmailSensitiveItems", 1, flags);
        if (!table) return;

        for (var i = 0; i < SensitiveToggles.Length; i++)
        {
            var entry = SensitiveToggles[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var value = entry.Get();
            if (ImGui.Checkbox($"{entry.Label}##sens_{i}", ref value))
            {
                entry.Set(value);
                Plugin.Config.Save();
            }
        }
    }

    private static int CountEnabled()
    {
        var n = 0;
        foreach (var c in AllCategories)
            if (Plugin.Config.IsPopCategoryEnabled(c)) n++;
        return n;
    }

    private static void DrawCategoryGrid()
    {
        var flags = ImGuiTableFlags.Borders
                  | ImGuiTableFlags.RowBg
                  | ImGuiTableFlags.SizingStretchSame
                  | ImGuiTableFlags.NoSavedSettings
                  | ImGuiTableFlags.PadOuterX;

        using var table = ImRaii.Table("MogmailPopCategoryGrid", 2, flags);
        if (!table) return;

        foreach (var category in AllCategories)
        {
            ImGui.TableNextColumn();
            var enabled = Plugin.Config.IsPopCategoryEnabled(category);
            var label = ItemRegistryClassifier.CategoryLabel(category);
            if (ImGui.Checkbox(label + $"##cat_{(int)category}", ref enabled))
            {
                Plugin.Config.SetPopCategoryEnabled(category, enabled);
                Plugin.Config.Save();
            }
        }
    }
}
