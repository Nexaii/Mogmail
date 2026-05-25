using System;
using System.Collections.Generic;
using Dalamud.Game;
using Lumina.Excel.Sheets;

namespace Mogmail.Services;

public enum SensitiveCategory
{
    Fantasia,
    MsqProgression,
    OneHerosJourney,
    OneRetainersJourney,
}

public static class SensitiveItemRegistry
{
    private const uint FantasiaBaseId = 7596;

    private static readonly string[] MsqSuffixes =
    {
        " A Realm Reborn",
        " Heavensward",
        " Stormblood",
        " Shadowbringers",
        " Endwalker",
    };

    private static readonly string[] HerosJourneySuffixes =
    {
        " Journey IV",
        " Journey V",
        " Journey VI",
        " Journey I-IV",
        " Journey I-V",
        " Journey I-VI",
    };

    private static readonly Lazy<IReadOnlyDictionary<uint, Entry>> Cache = new(BuildCache);

    public readonly record struct Entry(SensitiveCategory Category, string Name);

    public static bool TryGetCategory(uint baseItemId, out SensitiveCategory category)
    {
        if (Cache.Value.TryGetValue(baseItemId, out var entry))
        {
            category = entry.Category;
            return true;
        }
        category = default;
        return false;
    }

    public static string GetName(uint baseItemId) =>
        Cache.Value.TryGetValue(baseItemId, out var entry) ? entry.Name : $"item#{baseItemId}";

    private static IReadOnlyDictionary<uint, Entry> BuildCache()
    {
        var map = new Dictionary<uint, Entry>();
        var sheet = Plugin.Data.GetExcelSheet<Item>(ClientLanguage.English);
        if (sheet == null)
        {
            MogLog.Warning("[Mogmail] sensitive registry: English Item sheet unavailable. No items will be flagged sensitive.");
            return map;
        }

        foreach (var row in sheet)
        {
            var rowId = row.RowId;
            if (rowId == 0) continue;

            if (rowId == FantasiaBaseId)
            {
                map[rowId] = new Entry(SensitiveCategory.Fantasia, "Phial of Fantasia");
                continue;
            }

            var name = row.Name.ExtractText();
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.StartsWith("Tales of Adventure: ", StringComparison.Ordinal)) continue;

            if (TryMatchMsq(name)) { map[rowId] = new Entry(SensitiveCategory.MsqProgression, name); continue; }
            if (TryMatchRetainer(name)) { map[rowId] = new Entry(SensitiveCategory.OneRetainersJourney, name); continue; }
            if (TryMatchHerosJourney(name)) { map[rowId] = new Entry(SensitiveCategory.OneHerosJourney, name); continue; }
        }

        if (!map.ContainsKey(FantasiaBaseId))
            MogLog.Warning($"[Mogmail] sensitive registry: Fantasia row {FantasiaBaseId} missing from Item sheet. Fantasia protection disabled.");
        if (map.Count < 2)
            MogLog.Warning($"[Mogmail] sensitive registry: only {map.Count} entries built. Tales of Adventure patterns may have changed.");

        return map;
    }

    private static bool TryMatchMsq(string name)
    {
        foreach (var suffix in MsqSuffixes)
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool TryMatchRetainer(string name)
    {
        if (!name.StartsWith("Tales of Adventure: One Retainer's Journey ", StringComparison.Ordinal)) return false;
        foreach (var suffix in HerosJourneySuffixes)
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool TryMatchHerosJourney(string name)
    {
        if (!name.StartsWith("Tales of Adventure: One ", StringComparison.Ordinal)) return false;
        if (name.StartsWith("Tales of Adventure: One Retainer's Journey ", StringComparison.Ordinal)) return false;
        foreach (var suffix in HerosJourneySuffixes)
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return true;
        return false;
    }
}
