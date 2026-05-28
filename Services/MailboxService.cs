using System;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mogmail.Constants;
using Mogmail.Models;

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
    private const int InfoProxyEntryCountOffset = 0x10;
    private const int LetterStructSize = 0xE8;
    private const int LetterArrayBaseOffset = 0x30;
    private const int LetterDetailBodyOffset = 0x0C;

    private const string RequestLetterDetailSignature =
        "40 55 41 54 41 57 48 81 EC B0 0F 00 00 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 83 B9 00 76 00 00 00";

    private const string AddDataSignature =
        "53 55 56 57 41 54 48 83 EC 40 48 8B D9 45 8B E0 48 8B 49 08 48 8B EA 48 8B 01 FF 50 40";

    private const string HandleLetterDetailResponseSignature =
        "48 89 5C 24 18 56 41 56 41 57 48 83 EC 30 8B 81 00 76 00 00 48 8B DA 48 8B F1";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte RequestLetterDetailDelegate(InfoProxyLetter* infoProxy, uint letterIndex);

    private delegate void AddDataDelegate(InfoProxyLetter* proxy, byte* packet, uint count);

    private delegate void HandleLetterDetailResponseDelegate(InfoProxyLetter* proxy, byte* packet);

    private readonly RequestLetterDetailDelegate? _requestLetterDetail;
    private readonly Hook<AddDataDelegate>? _addDataHook;
    private readonly Hook<HandleLetterDetailResponseDelegate>? _handleLetterDetailHook;

    public event Action<int>? NewLetterObserved;
    public event Action<string, ulong, uint>? LetterDetailReceived;

    public bool IsMailboxOpen
    {
        get
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(AddonNames.LetterList, 1);
            return addon != null && addon->IsReady && addon->IsVisible;
        }
    }

    public MailboxService()
    {
        if (Plugin.SigScanner.TryScanText(RequestLetterDetailSignature, out var detailAddr))
        {
            _requestLetterDetail = Marshal.GetDelegateForFunctionPointer<RequestLetterDetailDelegate>(detailAddr);
            MogLog.Information($"[Mogmail] RequestLetterDetail resolved at 0x{detailAddr.ToInt64():X}");
        }
        else
        {
            MogLog.Warning("[Mogmail] RequestLetterDetail signature not found. Take path will skip detail-request prefix (vanilla packet order broken).");
        }

        if (Plugin.SigScanner.TryScanText(AddDataSignature, out var addDataAddr))
        {
            _addDataHook = Plugin.GameInteropProvider.HookFromAddress<AddDataDelegate>(addDataAddr, AddDataDetour);
            _addDataHook.Enable();
            MogLog.Information($"[Mogmail] AddData hook installed at 0x{addDataAddr.ToInt64():X}");
        }
        else
        {
            MogLog.Warning("[Mogmail] AddData signature not found. Archive receive tracking will rely on degraded fallback path.");
        }

        if (Plugin.SigScanner.TryScanText(HandleLetterDetailResponseSignature, out var detailRespAddr))
        {
            _handleLetterDetailHook = Plugin.GameInteropProvider.HookFromAddress<HandleLetterDetailResponseDelegate>(detailRespAddr, HandleLetterDetailDetour);
            _handleLetterDetailHook.Enable();
            MogLog.Information($"[Mogmail] HandleLetterDetailResponse hook installed at 0x{detailRespAddr.ToInt64():X}");
        }
        else
        {
            MogLog.Warning("[Mogmail] HandleLetterDetailResponse signature not found. Archive will not capture full message bodies.");
        }
    }

    public void Dispose()
    {
        _addDataHook?.Disable();
        _addDataHook?.Dispose();
        _handleLetterDetailHook?.Disable();
        _handleLetterDetailHook?.Dispose();
    }

    private void AddDataDetour(InfoProxyLetter* proxy, byte* packet, uint count)
    {
        var oldEntryCount = proxy != null ? *(uint*)((byte*)proxy + InfoProxyEntryCountOffset) : 0u;
        _addDataHook!.Original(proxy, packet, count);
        if (proxy == null) return;
        var newEntryCount = *(uint*)((byte*)proxy + InfoProxyEntryCountOffset);
        if (newEntryCount <= oldEntryCount) return;
        var handler = NewLetterObserved;
        if (handler == null) return;
        try
        {
            for (var i = oldEntryCount; i < newEntryCount; i++)
                handler((int)i);
        }
        catch (Exception ex)
        {
            MogLog.Warning($"[Mogmail] NewLetterObserved subscriber threw: {ex.Message}");
        }
    }

    private void HandleLetterDetailDetour(InfoProxyLetter* proxy, byte* packet)
    {
        ulong cid = 0;
        uint timestamp = 0;
        string body = "";
        var captured = false;

        if (proxy != null && packet != null)
        {
            var pendingIdx = *(int*)((byte*)proxy + InfoProxyPendingDetailIndexOffset);
            if (pendingIdx >= 0 && pendingIdx < proxy->EntryCount)
            {
                var letterBase = (byte*)proxy + LetterArrayBaseOffset + LetterStructSize * pendingIdx;
                cid = *(ulong*)letterBase;
                timestamp = *(uint*)(letterBase + 0x08);
                body = ReadNullTerminatedUtf8(packet + LetterDetailBodyOffset);
                captured = true;
            }
        }

        _handleLetterDetailHook!.Original(proxy, packet);

        if (!captured) return;
        var handler = LetterDetailReceived;
        if (handler == null) return;
        try { handler(body, cid, timestamp); }
        catch (Exception ex) { MogLog.Warning($"[Mogmail] LetterDetailReceived subscriber threw: {ex.Message}"); }
    }

    private static string ReadNullTerminatedUtf8(byte* ptr)
    {
        if (ptr == null) return "";
        var buffer = new System.Collections.Generic.List<byte>(256);
        var depth = 0;
        var i = 0;
        while (i < 0x4000)
        {
            var b = ptr[i++];
            if (b == 0) break;
            if (b == 0x02) { depth++; continue; }
            if (b == 0x03 && depth > 0) { depth--; continue; }
            if (depth == 0) buffer.Add(b);
        }
        if (buffer.Count == 0) return "";
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()).Trim();
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

    public uint GetGil(int index)
    {
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return 0;
        return ReadGil(LetterPointer(proxy, index));
    }

    public int GetAttachmentCount(int index)
    {
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return 0;
        return CountAttachments(LetterPointer(proxy, index));
    }

    public bool TrySnapshotLetter(int index, out LetterSnapshot snapshot)
    {
        snapshot = new LetterSnapshot();
        var proxy = InfoProxyLetter.Instance();
        if (proxy == null || index < 0 || index >= proxy->EntryCount) return false;

        var letterPtr = LetterPointer(proxy, index);
        snapshot.SenderContentId = (ulong)proxy->Letters[index].SenderContentId;
        snapshot.Timestamp = (uint)proxy->Letters[index].Timestamp;
        snapshot.Sender = proxy->Letters[index].SenderString;
        snapshot.Preview = proxy->Letters[index].MessagePreviewString;
        snapshot.Category = *(letterPtr + LetterCategoryOffset);
        snapshot.Read = ReadReadFlag(letterPtr);
        snapshot.Gil = ReadGil(letterPtr);
        for (var slot = 0; slot < LetterAttachmentSlotCount; slot++)
        {
            var itemId = ReadAttachmentItemId(letterPtr, slot);
            if (itemId == 0) continue;
            var count = ReadAttachmentCount(letterPtr, slot);
            snapshot.Attachments.Add(new AttachmentSnapshot(itemId, count));
        }
        return true;
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

    private static uint ReadAttachmentCount(byte* letterPtr, int slot)
        => *(uint*)(letterPtr + LetterAttachmentBase + LetterAttachmentStride * slot + 4);

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
}
