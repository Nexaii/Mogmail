using System;
using Lumina.Excel.Sheets;

namespace Mogmail.Services;

public static class ItemNameResolver
{
    private const uint HighQualityOffset = 1_000_000;
    private const uint CollectableOffset = 500_000;

    public static string Resolve(uint itemId)
    {
        var (baseId, suffix) = Normalize(itemId);
        try
        {
            var sheet = Plugin.Data.GetExcelSheet<Item>();
            if (sheet == null) return Fallback(itemId);
            if (!sheet.TryGetRow(baseId, out var row)) return Fallback(itemId);
            var name = row.Name.ExtractText();
            if (string.IsNullOrEmpty(name)) return Fallback(itemId);
            return suffix.Length == 0 ? name : $"{name} {suffix}";
        }
        catch
        {
            return Fallback(itemId);
        }
    }

    private static (uint BaseId, string Suffix) Normalize(uint raw)
    {
        if (raw >= HighQualityOffset) return (raw - HighQualityOffset, "(HQ)");
        if (raw >= CollectableOffset) return (raw - CollectableOffset, "(Collectable)");
        return (raw, "");
    }

    private static string Fallback(uint itemId) => $"Item#{itemId}";
}
