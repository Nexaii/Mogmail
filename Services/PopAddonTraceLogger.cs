using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mogmail.Services;

public sealed unsafe class PopAddonTraceLogger : IDisposable
{
    private static readonly string[] TrackedAddons =
    {
        "SelectYesno",
        "SelectOk",
        "SelectString",
        "JournalResult",
    };

    public PopAddonTraceLogger()
    {
        foreach (var name in TrackedAddons)
        {
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, name, OnSetup);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, name, OnFinalize);
        }
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(OnSetup);
        Plugin.AddonLifecycle.UnregisterListener(OnFinalize);
    }

    private static void OnSetup(AddonEvent type, AddonArgs args)
    {
        if (!ShouldTrace()) return;
        var prompt = ReadPrompt(args);
        MogLog.Information($"[Mogmail][diag] addon PostSetup {args.AddonName} prompt=\"{Truncate(prompt, 120)}\"");
    }

    private static void OnFinalize(AddonEvent type, AddonArgs args)
    {
        if (!ShouldTrace()) return;
        MogLog.Information($"[Mogmail][diag] addon PreFinalize {args.AddonName}");
    }

    private static bool ShouldTrace() => Plugin.Config.VerboseTakeDiagnostics;

    private static string ReadPrompt(AddonArgs args)
    {
        var addr = args.Addon.Address;
        if (addr == nint.Zero) return "";

        switch (args.AddonName)
        {
            case "SelectYesno":
            {
                var addon = (AddonSelectYesno*)addr;
                if (addon == null || addon->PromptText == null) return "";
                return addon->PromptText->NodeText.ToString() ?? "";
            }
            case "SelectOk":
            {
                var addon = (AddonSelectOk*)addr;
                if (addon == null || addon->PromptText == null) return "";
                return addon->PromptText->NodeText.ToString() ?? "";
            }
            default:
                return "";
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "...";
    }
}
