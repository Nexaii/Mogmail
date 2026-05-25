using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mogmail.Services;

public sealed unsafe class PopConfirmAutoclicker : IDisposable
{
    private const string SelectYesNoAddon = "SelectYesNo";
    private const int YesCallbackCase = 0;
    private const int PostUseActionWindowMs = 1000;

    private static readonly string[] DestructiveAddonGuard =
    {
        "SalvageDialog",
    };

    public PopConfirmAutoclicker()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, SelectYesNoAddon, OnSelectYesNoSetup);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(OnSelectYesNoSetup);
    }

    private void OnSelectYesNoSetup(AddonEvent type, AddonArgs args)
    {
        try
        {
            if (!Plugin.Instance.PopQueue.IsInPostUseActionWindow(PostUseActionWindowMs))
            {
                MogLog.Information("[Mogmail] pop autoclicker: skipped, outside post-UseAction window.");
                return;
            }

            if (TryFindVisibleDestructiveAddon(out var blockingAddon))
            {
                MogLog.Warning($"[Mogmail] pop autoclicker: skipped, destructive addon visible: {blockingAddon}.");
                return;
            }

            var atk = (AtkUnitBase*)args.Addon.Address;
            if (atk == null) return;

            FireYesCallback(atk);
            Plugin.Instance.PopQueue.NotifyConfirmFired();
            MogLog.Information("[Mogmail] pop autoclicker: auto-confirmed SelectYesNo.");
        }
        catch (Exception ex)
        {
            try { MogLog.Error($"[Mogmail] PopConfirmAutoclicker exception: {ex}"); } catch { }
        }
    }

    private static bool TryFindVisibleDestructiveAddon(out string addonName)
    {
        foreach (var name in DestructiveAddonGuard)
        {
            var addon = Plugin.GameGui.GetAddonByName(name);
            if (addon.IsNull) continue;
            if (addon.IsVisible)
            {
                addonName = name;
                return true;
            }
        }
        addonName = string.Empty;
        return false;
    }

    private static void FireYesCallback(AtkUnitBase* atk)
    {
        var value = stackalloc AtkValue[1];
        value[0].Type = AtkValueType.Int;
        value[0].Int = YesCallbackCase;
        atk->FireCallback(1, value, true);
    }
}
