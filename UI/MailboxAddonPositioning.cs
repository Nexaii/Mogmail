using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mogmail.Constants;

namespace Mogmail.UI;

internal static unsafe class MailboxAddonPositioning
{
    public static bool TryGetLetterListCenter(out Vector2 center)
    {
        center = default;
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(AddonNames.LetterList, 1);
        if (addon == null) return false;
        if (!addon->IsReady || !addon->IsVisible) return false;
        var node = addon->RootNode;
        if (node == null) return false;
        var width = addon->GetScaledWidth(true);
        var height = addon->GetScaledHeight(true);
        center = new Vector2(node->ScreenX + width * 0.5f, node->ScreenY + height * 0.5f);
        return true;
    }
}
