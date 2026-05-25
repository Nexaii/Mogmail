using System;
using System.Collections.Generic;
using Dalamud.Game;
using Lumina.Excel.Sheets;

namespace Mogmail.Services;

public static class PopBlacklistRegistry
{
    private static readonly Lazy<IReadOnlySet<uint>> Cache = new(BuildCache);

    public static bool Contains(uint baseItemId) => Cache.Value.Contains(baseItemId);

    private static IReadOnlySet<uint> BuildCache()
    {
        var set = new HashSet<uint>();
        var sheet = Plugin.Data.GetExcelSheet<Item>(ClientLanguage.English);
        if (sheet == null)
        {
            MogLog.Warning("[Mogmail] pop blacklist: English Item sheet unavailable. Blacklist is empty.");
            return set;
        }

        foreach (var row in sheet)
        {
            if (row.RowId == 0) continue;
            var name = row.Name.ExtractText();
            if (string.IsNullOrEmpty(name)) continue;
            if (IsAetheryteTicket(name)) set.Add(row.RowId);
        }

        if (set.Count == 0)
            MogLog.Warning("[Mogmail] pop blacklist: zero Aetheryte Tickets identified. Pattern may have changed.");

        return set;
    }

    private static bool IsAetheryteTicket(string name)
    {
        if (name.Equals("Aetheryte Ticket", StringComparison.Ordinal)) return true;
        return name.EndsWith(" Aetheryte Ticket", StringComparison.Ordinal);
    }
}
