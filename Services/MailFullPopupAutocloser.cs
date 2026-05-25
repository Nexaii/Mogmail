using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mogmail.Services;

public sealed unsafe class MailFullPopupAutocloser : IDisposable
{
    private const string SelectOkAddon = "SelectOk";
    private const int OkCallbackCase = 0;

    public MailFullPopupAutocloser()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, SelectOkAddon, OnSelectOkSetup);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(OnSelectOkSetup);
    }

    private void OnSelectOkSetup(AddonEvent type, AddonArgs args)
    {
        try
        {
            var fromMailbox = Plugin.Instance.Mailbox.IsMailboxOpen;
            var fromPop = !Plugin.Instance.PopQueue.IsIdle;
            if (!fromMailbox && !fromPop) return;

            var addon = (AddonSelectOk*)args.Addon.Address;
            if (addon == null) return;

            var prompt = ReadPrompt(addon);
            FireOkCallback((AtkUnitBase*)addon);
            if (fromPop) Plugin.Instance.PopQueue.NotifyConfirmFired();
            var source = fromMailbox ? "mail-full" : "pop";
            MogLog.Information($"[Mogmail] {source} SelectOk auto-dismissed. Prompt: \"{Truncate(prompt, 140)}\"");
        }
        catch (Exception ex)
        {
            try { MogLog.Error($"[Mogmail] MailFullPopupAutocloser exception: {ex}"); } catch { }
        }
    }

    private static string ReadPrompt(AddonSelectOk* addon)
    {
        var textNode = addon->PromptText;
        if (textNode == null) return "";
        var s = textNode->NodeText.ToString();
        return s ?? "";
    }

    private static void FireOkCallback(AtkUnitBase* atk)
    {
        var value = stackalloc AtkValue[1];
        value[0].Type = AtkValueType.Int;
        value[0].Int = OkCallbackCase;
        atk->FireCallback(1, value, true);
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "...";
    }
}
