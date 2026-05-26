using System;
using System.Runtime.InteropServices;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Mogmail.Constants;

namespace Mogmail.Services;

public sealed unsafe class MailboxService : IDisposable
{
    private const int LetterAttachmentBase = 0x0C;
    private const int LetterAttachmentStride = 0x14;
    private const int LetterAttachmentSlotCount = 5;
    private const int LetterGilOffset = 0x74;
    private const int LetterReadOffset = 0x84;
    private const int LetterCategoryOffset = 0x85;

    private const int InfoProxyPendingDetailIndexOffset = 0x7600;
    private const string RequestLetterDetailSignature =
        "40 55 41 54 41 57 48 81 EC B0 0F 00 00 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 83 B9 00 76 00 00 00";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte RequestLetterDetailDelegate(InfoProxyLetter* infoProxy, uint letterIndex);

    private readonly RequestLetterDetailDelegate? _requestLetterDetail;

    public bool IsMailboxOpen { get; private set; }

    public MailboxService()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonNames.LetterList, OnPostSetup);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonNames.LetterList, OnPreFinalize);

        if (Plugin.SigScanner.TryScanText(RequestLetterDetailSignature, out var detailAddr))
        {
            _requestLetterDetail = Marshal.GetDelegateForFunctionPointer<RequestLetterDetailDelegate>(detailAddr);
            MogLog.Information($"[Mogmail] RequestLetterDetail resolved at 0x{detailAddr.ToInt64():X}");
        }
        else
        {
            MogLog.Warning("[Mogmail] RequestLetterDetail signature not found. Take path will skip detail-request prefix (vanilla packet order broken).");
        }
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(OnPostSetup);
        Plugin.AddonLifecycle.UnregisterListener(OnPreFinalize);
        IsMailboxOpen = false;
    }

    public bool IsAvailable => InfoProxyLetter.Instance() != null;

    public uint LoadedLetterCount
    {
        get
        {
            var p = InfoProxyLetter.Instance();
            return p != null ? p->EntryCount : 0u;
        }
    }

    public bool LetterHasUnclaimedAttachments(int index)
    {
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return false;
        var letterPtr = LetterPointer(proxy, index);
        if (ReadGil(letterPtr) != 0) return true;
        for (var slot = 0; slot < LetterAttachmentSlotCount; slot++)
            if (ReadAttachmentItemId(letterPtr, slot) != 0) return true;
        return false;
    }

    public bool SnapshotAttachmentState(int index, out uint gil, Span<uint> slotItemIds)
    {
        gil = 0;
        for (var i = 0; i < slotItemIds.Length; i++) slotItemIds[i] = 0;

        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return false;

        var letterPtr = LetterPointer(proxy, index);
        gil = ReadGil(letterPtr);
        var n = System.Math.Min(slotItemIds.Length, LetterAttachmentSlotCount);
        for (var slot = 0; slot < n; slot++)
            slotItemIds[slot] = ReadAttachmentItemId(letterPtr, slot);
        return true;
    }

    public byte GetLetterCategory(int index)
    {
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return 0;
        return *(LetterPointer(proxy, index) + LetterCategoryOffset);
    }

    public bool IsGMLetter(int index) => GetLetterCategory(index) == 2;

    public bool IsLetterReadFlag(int index)
    {
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return false;
        return ReadReadFlag(LetterPointer(proxy, index));
    }

    public ulong GetSenderContentId(int index)
    {
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return 0;
        return (ulong)proxy->Letters[index].SenderContentId;
    }

    public uint GetLetterTimestamp(int index)
    {
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return 0;
        return (uint)proxy->Letters[index].Timestamp;
    }

    public string GetSenderName(int index)
    {
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return "";
        return proxy->Letters[index].SenderString;
    }

    public bool CanRequestLetterDetail => _requestLetterDetail != null;

    public bool RequestLetterDetail(int index)
    {
        if (_requestLetterDetail == null) return false;
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return false;
        return _requestLetterDetail(proxy, (uint)index) != 0;
    }

    public int PendingDetailIndex
    {
        get
        {
            var proxy = InfoProxyLetter.Instance();
            if (proxy == null) return -1;
            return *(int*)((byte*)proxy + InfoProxyPendingDetailIndexOffset);
        }
    }

    private static byte* LetterPointer(InfoProxyLetter* proxy, int index)
        => (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref proxy->Letters[index]);

    private static uint ReadAttachmentItemId(byte* letterPtr, int slot)
        => *(uint*)(letterPtr + LetterAttachmentBase + LetterAttachmentStride * slot);

    private static uint ReadGil(byte* letterPtr)
        => *(uint*)(letterPtr + LetterGilOffset);

    private static bool ReadReadFlag(byte* letterPtr)
        => *(letterPtr + LetterReadOffset) != 0;

    private static int CountAttachments(byte* letterPtr)
    {
        var n = 0;
        for (var slot = 0; slot < LetterAttachmentSlotCount; slot++)
            if (ReadAttachmentItemId(letterPtr, slot) != 0) n++;
        return n;
    }

    private void OnPostSetup(AddonEvent type, AddonArgs args) => IsMailboxOpen = true;
    private void OnPreFinalize(AddonEvent type, AddonArgs args) => IsMailboxOpen = false;
}
