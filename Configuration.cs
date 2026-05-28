using Dalamud.Configuration;
using Mogmail.Models;
using Mogmail.Services;

namespace Mogmail;

public enum ButtonDisplayMode { Iconic, Text }
public enum ToolbarLayout { Vertical, Horizontal }
public enum ToolbarAttach { SnappedLeft, SnappedRight, SnappedTop, Free }

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public int BulkSessionCap { get; set; } = 50;
    public int MinFloorMs { get; set; } = 200;

    public ButtonDisplayMode ButtonDisplayMode { get; set; } = ButtonDisplayMode.Iconic;
    public ToolbarLayout ToolbarLayout { get; set; } = ToolbarLayout.Vertical;
    public ToolbarAttach ToolbarAttach { get; set; } = ToolbarAttach.SnappedLeft;

    public bool ConfirmBeforeDelete { get; set; } = true;
    public bool IncludeGMInSweeps { get; set; } = false;

    public ClaimAction LastDeleteScope { get; set; } = ClaimAction.DeleteReadEmpty;

    public bool UseLargeToolbar { get; set; } = false;

    public bool AutoPopAfterTake { get; set; } = false;

    public ulong PopCategoryMask { get; set; } = ulong.MaxValue;

    public bool AllowFantasiaInPop { get; set; } = false;
    public bool AllowMsqProgressionInPop { get; set; } = false;
    public bool AllowOneHerosJourneyInPop { get; set; } = false;
    public bool AllowOneRetainersJourneyInPop { get; set; } = false;

    public bool EnableExternalLogFile { get; set; } = false;
    public string ExternalLogFilePath { get; set; } = "";

    public bool EnableGiftEcho { get; set; } = false;
    public bool EnableArchive { get; set; } = false;

    public float PopOverlayPosX { get; set; } = -1f;
    public float PopOverlayPosY { get; set; } = -1f;

    public bool IsPopCategoryEnabled(PopCategory category)
    {
        var bit = 1UL << (int)category;
        return (PopCategoryMask & bit) != 0UL;
    }

    public void SetPopCategoryEnabled(PopCategory category, bool enabled)
    {
        var bit = 1UL << (int)category;
        if (enabled) PopCategoryMask |= bit;
        else PopCategoryMask &= ~bit;
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    public void ResetToDefaults()
    {
        BulkSessionCap = 50;
        MinFloorMs = 200;

        ButtonDisplayMode = ButtonDisplayMode.Iconic;
        ToolbarLayout = ToolbarLayout.Vertical;
        ToolbarAttach = ToolbarAttach.SnappedLeft;

        ConfirmBeforeDelete = true;
        IncludeGMInSweeps = false;

        LastDeleteScope = ClaimAction.DeleteReadEmpty;

        UseLargeToolbar = false;
        AutoPopAfterTake = false;

        PopCategoryMask = ulong.MaxValue;
        AllowFantasiaInPop = false;
        AllowMsqProgressionInPop = false;
        AllowOneHerosJourneyInPop = false;
        AllowOneRetainersJourneyInPop = false;

        EnableExternalLogFile = false;
        ExternalLogFilePath = "";

        EnableGiftEcho = false;
        EnableArchive = false;
    }
}
