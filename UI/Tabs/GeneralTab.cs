using System.Numerics;
using Dalamud.Bindings.ImGui;
using Mogmail.UI.Helpers;

namespace Mogmail.UI.Tabs;

public sealed class GeneralTab : ISettingsTab
{
    public string Name => "General";

    public void Draw()
    {
        if (!ImGui.BeginChild("GeneralTabScroll", new Vector2(0, 0), true)) { ImGui.EndChild(); return; }
        try
        {
            if (Theme.DrawSectionHeader("Toolbar")) DrawToolbarSection();
            ImGui.Spacing();

            if (Theme.DrawSectionHeader("Deletion")) DrawDeletionSection();
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private static void DrawToolbarSection()
    {
        var large = Plugin.Config.UseLargeToolbar;
        if (SettingsRows.Checkbox("Larger buttons", ref large, "Switch toolbar buttons to the bigger 32px size."))
        {
            Plugin.Config.UseLargeToolbar = large;
            Plugin.Config.Save();
        }
    }

    private static void DrawDeletionSection()
    {
        var confirm = Plugin.Config.ConfirmBeforeDelete;
        if (SettingsRows.Checkbox("Confirm before delete", ref confirm, "Show a confirm dialog before any bulk delete."))
        {
            Plugin.Config.ConfirmBeforeDelete = confirm;
            Plugin.Config.Save();
        }

        var includeGm = Plugin.Config.IncludeGMInSweeps;
        if (SettingsRows.Checkbox("Include GM letters in sweeps", ref includeGm))
        {
            Plugin.Config.IncludeGMInSweeps = includeGm;
            Plugin.Config.Save();
        }
        Theme.HelperText("GM letters are protected by default. Off keeps them out of every sweep.");
    }

}
