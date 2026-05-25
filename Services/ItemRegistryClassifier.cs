using Dalamud.Utility;
using Lumina.Excel.Sheets;

namespace Mogmail.Services;

public enum PopCategory
{
    Minion = 0,
    Barding = 1,
    Mount = 2,
    SecretRecipeBook = 3,
    UnlockLink = 4,
    TripleTriadCard = 5,
    FolkloreTome = 6,
    FieldNotes = 7,
    Ornament = 8,
    OrchestrionRoll = 9,
    FramersKit = 10,
    Glasses = 11,
    CompanySealVouchers = 12,
    OccultRecords = 13,
    SoulShards = 14,
    StarContributorCertificate = 15,
}

public static class ItemRegistryClassifier
{
    public const uint FantasiaItemId = 7596;

    public static bool IsSensitive(uint baseItemId) => SensitiveItemRegistry.TryGetCategory(baseItemId, out _);

    public static bool TryGetSensitiveCategory(uint baseItemId, out SensitiveCategory category) =>
        SensitiveItemRegistry.TryGetCategory(baseItemId, out category);

    public static string SensitiveItemName(uint baseItemId) => SensitiveItemRegistry.GetName(baseItemId);

    public static bool IsBlacklistedFromPop(uint baseItemId) => PopBlacklistRegistry.Contains(baseItemId);

    public static bool TryClassify(uint rawItemId, out PopCategory category, out uint baseItemId)
    {
        category = default;
        baseItemId = 0;
        var (b, _) = ItemUtil.GetBaseId(rawItemId);
        baseItemId = b;
        var sheet = Plugin.Data.GetExcelSheet<Item>();
        if (!sheet.TryGetRow(b, out var item)) return false;
        if (item.ItemAction.RowId == 0) return false;
        return TryMapActionType(item.ItemAction.Value.Action.RowId, out category);
    }

    public static bool IsAlreadyUnlocked(uint baseItemId)
    {
        var sheet = Plugin.Data.GetExcelSheet<Item>();
        if (!sheet.TryGetRow(baseItemId, out var item)) return false;
        if (!Plugin.UnlockState.IsItemUnlockable(item)) return false;
        return Plugin.UnlockState.IsItemUnlocked(item);
    }

    public static string CategoryLabel(PopCategory category) => category switch
    {
        PopCategory.Minion => "Minions",
        PopCategory.Barding => "Bardings",
        PopCategory.Mount => "Mounts",
        PopCategory.SecretRecipeBook => "Master Recipe Books",
        PopCategory.UnlockLink => "Hairstyles / Emotes / Riding Maps (UnlockLink)",
        PopCategory.TripleTriadCard => "Triple Triad Cards",
        PopCategory.FolkloreTome => "Folklore Tomes",
        PopCategory.FieldNotes => "Bozjan Field Notes",
        PopCategory.Ornament => "Fashion Accessories",
        PopCategory.OrchestrionRoll => "Orchestrion Rolls",
        PopCategory.FramersKit => "Framer's Kits",
        PopCategory.Glasses => "Glasses",
        PopCategory.CompanySealVouchers => "Company Seal Vouchers",
        PopCategory.OccultRecords => "Occult Records",
        PopCategory.SoulShards => "Phantom Soul Shards",
        PopCategory.StarContributorCertificate => "Star Contributor Certificates",
        _ => category.ToString(),
    };

    private static bool TryMapActionType(uint actionType, out PopCategory category)
    {
        switch (actionType)
        {
            case 853: category = PopCategory.Minion; return true;
            case 1013: category = PopCategory.Barding; return true;
            case 1322: category = PopCategory.Mount; return true;
            case 2136: category = PopCategory.SecretRecipeBook; return true;
            case 2633: category = PopCategory.UnlockLink; return true;
            case 3357: category = PopCategory.TripleTriadCard; return true;
            case 4107: category = PopCategory.FolkloreTome; return true;
            case 19743: category = PopCategory.FieldNotes; return true;
            case 20086: category = PopCategory.Ornament; return true;
            case 25183: category = PopCategory.OrchestrionRoll; return true;
            case 29459: category = PopCategory.FramersKit; return true;
            case 37312: category = PopCategory.Glasses; return true;
            case 41120: category = PopCategory.CompanySealVouchers; return true;
            case 43141: category = PopCategory.OccultRecords; return true;
            case 43142: category = PopCategory.SoulShards; return true;
            case 45189: category = PopCategory.StarContributorCertificate; return true;
            default: category = default; return false;
        }
    }
}
