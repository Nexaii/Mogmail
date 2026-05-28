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
            ImGui.Spacing();

            if (Theme.DrawSectionHeader("Notifications")) DrawGiftEchoSection();
            ImGui.Spacing();

            if (Theme.DrawSectionHeader("Archive")) DrawArchiveSection();
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

    private static void DrawGiftEchoSection()
    {
        var enabled = Plugin.Config.EnableGiftEcho;
        if (SettingsRows.Checkbox("Echo gift sender on Take", ref enabled, "When you claim attachments from a Purchases & Rewards gift letter, print the original gift sender and the items to local Echo chat."))
        {
            Plugin.Config.EnableGiftEcho = enabled;
            Plugin.Config.Save();
        }
    }

    private static void DrawArchiveSection()
    {
        var enabled = Plugin.Config.EnableArchive;
        if (SettingsRows.Checkbox("Enable archive", ref enabled, "Record received letters (header, attachments, gil, full body when read) per character. Local files only."))
        {
            var wasEnabled = Plugin.Config.EnableArchive;
            Plugin.Config.EnableArchive = enabled;
            Plugin.Config.Save();
            if (wasEnabled && !enabled)
                Plugin.Instance.CloseArchiveWindow();
        }

        if (Plugin.Config.EnableArchive)
        {
            if (SettingsRows.PrimaryButton("Open Archive", "Open the archive viewer window."))
                Plugin.Instance.OpenArchiveWindow();
        }
    }

    private static void DrawDeletionSection()
    {
        var confirm = Plugin.Config.ConfirmBeforeDelete;
        if (SettingsRows.Checkbox("Confirm before delete", ref confirm, "Show a confirm dialog before bulk delete and Take + Delete."))
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
    }

}
