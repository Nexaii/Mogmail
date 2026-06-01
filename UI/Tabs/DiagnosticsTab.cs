using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Mogmail.UI.Helpers;

namespace Mogmail.UI.Tabs;

public sealed class DiagnosticsTab : ISettingsTab
{
    private const string ResetPopupId = "##MogmailResetConfirm";

    public string Name => "Diagnostics";

    public void Draw()
    {
        if (Theme.DrawSectionHeader("Plugin log file")) DrawExternalLogSection();
        ImGui.Spacing();

        DrawResetSection();
    }

    private static void DrawExternalLogSection()
    {
        var fileLog = Plugin.Config.EnableExternalLogFile;
        if (SettingsRows.Checkbox("Save plugin log to external file", ref fileLog))
        {
            Plugin.Config.EnableExternalLogFile = fileLog;
            if (fileLog && string.IsNullOrWhiteSpace(Plugin.Config.ExternalLogFilePath))
                Plugin.Config.ExternalLogFilePath = System.IO.Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "Mogmail.log");
            Plugin.Config.Save();
        }

        using (ImRaii.Disabled(!Plugin.Config.EnableExternalLogFile))
        {
            var path = Plugin.Config.ExternalLogFilePath;
            ImGui.SetNextItemWidth(SettingsRows.InputWidthLarge);
            if (ImGui.InputText("##LogFilePath", ref path, 512))
            {
                Plugin.Config.ExternalLogFilePath = path;
                Plugin.Config.Save();
            }
            ImGui.SameLine();
            if (SettingsRows.PrimaryButton("..."))
                OpenLogFilePicker();
        }
    }

    private static void DrawResetSection()
    {
        ImGui.Separator();
        ImGui.Spacing();

        if (SettingsRows.DangerButton("Reset to defaults", "Reset every Mogmail setting to its install default."))
            ImGui.OpenPopup(ResetPopupId);

        if (!ImGui.BeginPopup(ResetPopupId)) return;

        ImGui.TextUnformatted("Reset all Mogmail settings to defaults?");
        ImGui.Spacing();

        if (SettingsRows.DangerButton("Yes, reset"))
        {
            Plugin.Config.ResetToDefaults();
            Plugin.Config.Save();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (SettingsRows.PrimaryButton("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private static void OpenLogFilePicker()
    {
        var current = Plugin.Config.ExternalLogFilePath;
        var startDir = !string.IsNullOrWhiteSpace(current)
            ? System.IO.Path.GetDirectoryName(current) ?? ""
            : Plugin.PluginInterface.GetPluginConfigDirectory();
        var defaultName = !string.IsNullOrWhiteSpace(current)
            ? System.IO.Path.GetFileName(current)
            : "mogmail.log";

        Plugin.FileDialogManager.SaveFileDialog(
            "Save Mogmail log to...",
            ".log,.txt,.*",
            defaultName,
            ".log",
            (ok, picked) =>
            {
                if (!ok || string.IsNullOrWhiteSpace(picked)) return;
                Plugin.Config.ExternalLogFilePath = picked;
                Plugin.Config.Save();
            },
            startDir);
    }
}
