using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using Mogmail.Models;

namespace Mogmail.Services;

public sealed class GiftEchoService
{
    private static readonly Regex[] Templates =
    [
        new(@"^Enclosed is the following gift from your friend, (?<sender>.+?):", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"^Enclosed is the following gift from your free company member, (?<sender>.+?):", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"^Enclosed is the following gift from (?<sender>.+?):", RegexOptions.Compiled | RegexOptions.CultureInvariant),
    ];

    public void TryEmit(LetterSnapshot snapshot)
    {
        if (!Plugin.Config.EnableGiftEcho) return;
        if (string.IsNullOrEmpty(snapshot.Preview)) return;
        if (snapshot.Attachments.Count == 0 && snapshot.Gil == 0) return;

        if (!TryParseSender(snapshot.Preview, out var giftSender)) return;

        var line = BuildEchoLine(giftSender, snapshot);
        Plugin.Chat.Print(new Dalamud.Game.Text.XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = line,
        });
    }

    private static bool TryParseSender(string preview, out string sender)
    {
        foreach (var template in Templates)
        {
            var match = template.Match(preview);
            if (!match.Success) continue;
            sender = match.Groups["sender"].Value.Trim();
            if (sender.Length > 0) return true;
        }
        sender = "";
        return false;
    }

    private static string BuildEchoLine(string sender, LetterSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append("[Mogmail] Gift from ");
        sb.Append(sender);
        sb.Append(": ");

        var parts = new List<string>();
        foreach (var attachment in snapshot.Attachments)
        {
            var name = LookupItemName(attachment.ItemId);
            parts.Add(attachment.Count > 1 ? $"{name} x{attachment.Count}" : name);
        }
        if (snapshot.Gil > 0)
            parts.Add($"{snapshot.Gil:N0} gil");

        sb.Append(string.Join(", ", parts));
        return sb.ToString();
    }

    private static string LookupItemName(uint itemId) => ItemNameResolver.Resolve(itemId);
}
