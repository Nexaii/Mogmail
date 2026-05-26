using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mogmail.Models;
using Mogmail.Services;

namespace Mogmail.Managers;

public sealed unsafe class ClaimQueueManager : IDisposable
{
    private const int TransferGateIndex = 136;
    private const long SessionTimeoutMs = 5 * 60 * 1000;
    private const long PerCallSafetyTimeoutMs = 8000;
    private const long DetailAckTimeoutMs = 5000;
    private const long TakeAckTimeoutMs = 10000;
    private const long RejectionBackoffMs = 3000;
    private const int LetterAttachmentSlotCount = 5;

    private enum TakePhase { ReadyForDetail, AwaitingDetailAck, AwaitingTakeAck }

    public enum RunState { Idle, Running }

    public RunState State { get; private set; } = RunState.Idle;
    public ClaimAction CurrentAction { get; private set; }
    public string CurrentLabel { get; private set; } = "";
    public int Processed { get; private set; }
    public int Skipped { get; private set; }
    public int Total { get; private set; }

    private readonly Queue<int> _takeIndices = new();
    private Func<int, bool>? _deletePredicate;
    private readonly HashSet<(ulong ContentId, uint Timestamp)> _deleteBlacklist = new();
    private int _deleteBudget;

    private long _lastInvokeMs;
    private long _runStartMs;
    private bool _firstCall;

    private TakePhase _takePhase = TakePhase.ReadyForDetail;
    private int _pendingTakeIdx = -1;
    private long _detailRequestedMs;
    private long _takeRequestedMs;
    private (ulong ContentId, uint Timestamp) _pendingTakeKey;
#pragma warning disable CS0414
    private uint _pendingTakeGilPre;
    private readonly uint[] _pendingTakeSlotsPre = new uint[LetterAttachmentSlotCount];
    private int _lastLoggedAttachmentDelta = -1;
#pragma warning restore CS0414
    private long _rejectionBackoffUntilMs;

    private bool _autoDeleteAfterTake;
    private readonly HashSet<(ulong ContentId, uint Timestamp)> _autoDeleteKeys = [];
    private bool _autoPopPending;

    public ClaimQueueManager()
    {
        Plugin.Framework.Update += Tick;
        Plugin.Instance.MailRejectionWatcher.RejectionDetected += OnServerRejection;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= Tick;
        Plugin.Instance.MailRejectionWatcher.RejectionDetected -= OnServerRejection;
        _takeIndices.Clear();
        _deletePredicate = null;
        _deleteBlacklist.Clear();
        State = RunState.Idle;
    }

    public bool IsIdle => State == RunState.Idle;

    public void StartTake(string actionLabel, IEnumerable<int> indices)
    {
        if (State != RunState.Idle) return;
        CurrentAction = ClaimAction.Take;
        CurrentLabel = actionLabel;
        _takeIndices.Clear();
        foreach (var i in indices) _takeIndices.Enqueue(i);
        Total = _takeIndices.Count;
        ResetRunState();
        _autoPopPending = Plugin.Config.AutoPopAfterTake;
        MogLog.Information($"[Mogmail] {actionLabel} started. {Total} letters in batch.");
    }

    public void StartTakeAndDelete(string actionLabel, IEnumerable<int> indices)
    {
        if (State != RunState.Idle) return;
        StartTake(actionLabel, indices);
        if (State != RunState.Running) return;
        _autoDeleteAfterTake = true;
        MogLog.Information($"[Mogmail] {actionLabel}: auto-delete chain armed.");
    }

    public void StartDelete(ClaimAction action, string actionLabel, Func<int, bool> predicate, int budget)
    {
        if (State != RunState.Idle) return;
        if (action == ClaimAction.Take) return;
        CurrentAction = action;
        CurrentLabel = actionLabel;
        _deletePredicate = predicate;
        _deleteBlacklist.Clear();
        _deleteBudget = budget;
        Total = budget;
        ResetRunState();
        MogLog.Information($"[Mogmail] {actionLabel} started. {Total} letters in batch.");
    }

    private void ResetRunState()
    {
        Processed = 0;
        Skipped = 0;
        _lastInvokeMs = 0;
        _runStartMs = Environment.TickCount64;
        _firstCall = true;
        _takePhase = TakePhase.ReadyForDetail;
        _pendingTakeIdx = -1;
        _detailRequestedMs = 0;
        _takeRequestedMs = 0;
        _pendingTakeKey = (0, 0);
        _pendingTakeGilPre = 0;
        Array.Clear(_pendingTakeSlotsPre);
        _rejectionBackoffUntilMs = 0;
        _lastLoggedAttachmentDelta = -1;
        _autoDeleteAfterTake = false;
        _autoDeleteKeys.Clear();
        State = RunState.Running;
    }

    public void Abort(string reason)
    {
        if (State == RunState.Idle) return;
        MogLog.Information($"[Mogmail] {CurrentLabel} aborted: {reason}. processed={Processed} skipped={Skipped}");
        Plugin.Chat.Print($"[Mogmail] aborted ({reason}). {Processed} done, {Skipped} skipped.");
        _autoPopPending = false;
        FinishRun();
    }

    private void FinishRun()
    {
        State = RunState.Idle;
        _takeIndices.Clear();
        _deletePredicate = null;
        _deleteBlacklist.Clear();
    }

    private void Tick(IFramework framework)
    {
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            try { MogLog.Error($"[Mogmail] ClaimQueue tick exception: {ex}"); } catch { }
            try { Abort("tick exception"); } catch { State = RunState.Idle; }
        }
    }

    private void TickCore()
    {
        if (State != RunState.Running) return;

        if (!GatesValid(out var reason))
        {
            Abort(reason);
            return;
        }

        if (CurrentAction == ClaimAction.Take)
        {
            TickTake();
            return;
        }

        if (NotReadyYet()) return;
        TickDelete();
    }

    private void TickTake()
    {
        switch (_takePhase)
        {
            case TakePhase.AwaitingDetailAck:
                TickAwaitingDetailAck();
                return;
            case TakePhase.AwaitingTakeAck:
                TickAwaitingTakeAck();
                return;
        }

        if (NotReadyYet()) return;

        if (_takeIndices.Count == 0)
        {
            CompleteRun();
            return;
        }

        var idx = _takeIndices.Dequeue();
        BeginTake(idx);
    }

    private void TickAwaitingDetailAck()
    {
        var mailbox = Plugin.Instance.Mailbox;
        if (mailbox.PendingDetailIndex < 0)
        {
            InvokeTake(_pendingTakeIdx);
            return;
        }

        if (Environment.TickCount64 - _detailRequestedMs > DetailAckTimeoutMs)
        {
            MogLog.Warning($"[Mogmail] detail ack timeout for idx {_pendingTakeIdx}. Skipping letter.");
            Skipped++;
            ResetTakePhase();
        }
    }

    private void TickAwaitingTakeAck()
    {
        var mailbox = Plugin.Instance.Mailbox;
        var liveIdx = ResolveLetterByKey(_pendingTakeKey);

        if (liveIdx < 0)
        {
            TraceTake($"target {_pendingTakeKey.ContentId:X}/{_pendingTakeKey.Timestamp} no longer in cache, counting as processed");
            Processed++;
            ResetTakePhase();
            return;
        }

        LogAttachmentDelta(liveIdx);

        if (!mailbox.LetterHasUnclaimedAttachments(liveIdx))
        {
            TraceTake($"ack received idx={liveIdx} elapsed={Environment.TickCount64 - _takeRequestedMs}ms");
            Processed++;
            if (_autoDeleteAfterTake) _autoDeleteKeys.Add(_pendingTakeKey);
            ResetTakePhase();
            return;
        }

        if (Environment.TickCount64 - _takeRequestedMs > TakeAckTimeoutMs)
        {
            MogLog.Warning($"[Mogmail] take ack timeout idx={liveIdx} ({_pendingTakeKey.ContentId:X}/{_pendingTakeKey.Timestamp}) elapsed={Environment.TickCount64 - _takeRequestedMs}ms. Skipping letter.");
            DumpFinalSlotState(liveIdx);
            Skipped++;
            ResetTakePhase();
        }
    }

    private void LogAttachmentDelta(int liveIdx)
    {
#if DEBUG
        Span<uint> live = stackalloc uint[LetterAttachmentSlotCount];
        if (!Plugin.Instance.Mailbox.SnapshotAttachmentState(liveIdx, out var gilLive, live)) return;

        var clearedCount = 0;
        for (var i = 0; i < LetterAttachmentSlotCount; i++)
            if (_pendingTakeSlotsPre[i] != 0 && live[i] == 0) clearedCount++;
        var gilCleared = _pendingTakeGilPre != 0 && gilLive == 0 ? 1 : 0;
        var delta = clearedCount + gilCleared;
        if (delta == _lastLoggedAttachmentDelta) return;
        _lastLoggedAttachmentDelta = delta;

        MogLog.Information(
            $"[Mogmail][trace] phase=AwaitingTakeAck idx={liveIdx} cleared_slots={clearedCount}/{NonZeroCount(_pendingTakeSlotsPre)} gil_cleared={(gilCleared == 1 ? "yes" : "no")} elapsed={Environment.TickCount64 - _takeRequestedMs}ms");
#endif
    }

    private void DumpFinalSlotState(int liveIdx)
    {
#if DEBUG
        Span<uint> live = stackalloc uint[LetterAttachmentSlotCount];
        if (!Plugin.Instance.Mailbox.SnapshotAttachmentState(liveIdx, out var gilLive, live)) return;
        MogLog.Information(
            $"[Mogmail][trace] final state idx={liveIdx} pre_slots=[{_pendingTakeSlotsPre[0]},{_pendingTakeSlotsPre[1]},{_pendingTakeSlotsPre[2]},{_pendingTakeSlotsPre[3]},{_pendingTakeSlotsPre[4]}] live_slots=[{live[0]},{live[1]},{live[2]},{live[3]},{live[4]}] gil_pre={_pendingTakeGilPre} gil_live={gilLive}");
#endif
    }

#if DEBUG
    private static int NonZeroCount(uint[] arr)
    {
        var n = 0;
        for (var i = 0; i < arr.Length; i++) if (arr[i] != 0) n++;
        return n;
    }
#endif

    private int ResolveLetterByKey((ulong ContentId, uint Timestamp) key)
    {
        var mailbox = Plugin.Instance.Mailbox;
        var count = (int)mailbox.LoadedLetterCount;
        for (var i = 0; i < count; i++)
        {
            if (mailbox.GetSenderContentId(i) == key.ContentId
                && mailbox.GetLetterTimestamp(i) == key.Timestamp)
                return i;
        }
        return -1;
    }

    private void ResetTakePhase()
    {
        _pendingTakeIdx = -1;
        _pendingTakeKey = (0, 0);
        _takePhase = TakePhase.ReadyForDetail;
    }

    private void BeginTake(int idx)
    {
        var mailbox = Plugin.Instance.Mailbox;
        if (!mailbox.CanRequestLetterDetail)
        {
            InvokeTake(idx);
            return;
        }

        if (mailbox.IsLetterReadFlag(idx))
        {
            InvokeTake(idx);
            return;
        }

        if (mailbox.PendingDetailIndex >= 0)
        {
            MogLog.Information("[Mogmail] another detail request pending, deferring take.");
            _takeIndices.Enqueue(idx);
            return;
        }

        if (!mailbox.RequestLetterDetail(idx))
        {
            MogLog.Warning($"[Mogmail] RequestLetterDetail({idx}) returned false. Falling through to direct take.");
            InvokeTake(idx);
            return;
        }

        _lastInvokeMs = Environment.TickCount64;
        _firstCall = false;
        _pendingTakeIdx = idx;
        _detailRequestedMs = _lastInvokeMs;
        _takePhase = TakePhase.AwaitingDetailAck;
    }

    private void TickDelete()
    {
        if (Processed + Skipped >= _deleteBudget)
        {
            CompleteRun();
            return;
        }

        var idx = FindNextDeleteTarget();
        if (idx < 0)
        {
            CompleteRun();
            return;
        }

        InvokeDelete(idx);
    }

    private int FindNextDeleteTarget()
    {
        if (_deletePredicate == null) return -1;
        var mailbox = Plugin.Instance.Mailbox;
        var count = (int)mailbox.LoadedLetterCount;
        for (var i = 0; i < count; i++)
        {
            var cid = mailbox.GetSenderContentId(i);
            var ts = mailbox.GetLetterTimestamp(i);
            if (_deleteBlacklist.Contains((cid, ts))) continue;
            if (mailbox.LetterHasUnclaimedAttachments(i)) continue;
            if (!_deletePredicate(i)) continue;
            return i;
        }
        return -1;
    }

    private void CompleteRun()
    {
        MogLog.Information($"[Mogmail] {CurrentLabel} done. processed={Processed} skipped={Skipped}");
        Plugin.Chat.Print($"[Mogmail] {CurrentLabel}: {Processed} done, {Skipped} skipped.");

        var shouldChainDelete = CurrentAction == ClaimAction.Take
            && _autoDeleteAfterTake
            && _autoDeleteKeys.Count > 0;
        HashSet<(ulong, uint)>? chainKeys = null;
        string? chainLabel = null;
        if (shouldChainDelete)
        {
            chainKeys = [.._autoDeleteKeys];
            chainLabel = $"{CurrentLabel} -> Delete (Taken)";
        }

        var firePopNow = _autoPopPending && !shouldChainDelete;
        if (firePopNow) _autoPopPending = false;

        FinishRun();

        if (shouldChainDelete) FireAutoDeleteChain(chainLabel!, chainKeys!);
        else if (firePopNow) Plugin.Instance.PopQueue.Arm("after take");
    }

    private void FireAutoDeleteChain(string label, HashSet<(ulong, uint)> keys)
    {
        var mailbox = Plugin.Instance.Mailbox;
        bool Predicate(int i)
        {
            var cid = mailbox.GetSenderContentId(i);
            var ts = mailbox.GetLetterTimestamp(i);
            return keys.Contains((cid, ts));
        }
        var budget = keys.Count + 2;
        StartDelete(ClaimAction.DeleteAll, label, Predicate, budget);
    }

    private bool GatesValid(out string reason)
    {
        reason = "";
        if (!Plugin.ClientState.IsLoggedIn) { reason = "logged out"; return false; }
        if (!Plugin.Instance.Mailbox.IsAvailable) { reason = "mailbox proxy unavailable"; return false; }
        if (!Plugin.Instance.Mailbox.IsMailboxOpen) { reason = "mailbox closed"; return false; }
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) { reason = "zoning"; return false; }
        if (Plugin.Condition[ConditionFlag.BetweenAreas51]) { reason = "zoning"; return false; }
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]) { reason = "cutscene"; return false; }
        if (Environment.TickCount64 - _runStartMs > SessionTimeoutMs) { reason = "session timeout"; return false; }
        return true;
    }

    private bool NotReadyYet()
    {
        var now = Environment.TickCount64;

        if (_rejectionBackoffUntilMs != 0 && now < _rejectionBackoffUntilMs) return true;

        if (_firstCall) return false;

        if (now - _lastInvokeMs < Plugin.Config.MinFloorMs) return true;

        if (CurrentAction == ClaimAction.Take)
        {
            var stage = AtkStage.Instance();
            if (stage == null) return true;
            var numArr = stage->GetNumberArrayData(NumberArrayType.Letter);
            if (numArr == null) return true;
            if (numArr->IntArray[TransferGateIndex] != 0)
            {
                if (now - _lastInvokeMs > PerCallSafetyTimeoutMs) return false;
                return true;
            }
            return false;
        }

        var proxy = InfoProxyLetter.Instance();
        if (proxy == null) return true;
        var busyByte = *((byte*)proxy + 0x76DB);
        if (busyByte != 0)
        {
            if (now - _lastInvokeMs > PerCallSafetyTimeoutMs) return false;
            return true;
        }
        return false;
    }

    private void InvokeTake(int idx)
    {
        _lastInvokeMs = Environment.TickCount64;
        _firstCall = false;

        var proxy = InfoProxyLetter.Instance();
        if (proxy == null) { Skipped++; ResetTakePhase(); return; }
        if (idx < 0 || idx >= proxy->EntryCount) { Skipped++; ResetTakePhase(); return; }

        var mailbox = Plugin.Instance.Mailbox;
        if (!mailbox.LetterHasUnclaimedAttachments(idx))
        {
            TraceTake($"target idx={idx} already empty, counting as processed");
            Processed++;
            ResetTakePhase();
            return;
        }

        _pendingTakeIdx = idx;
        _pendingTakeKey = (mailbox.GetSenderContentId(idx), mailbox.GetLetterTimestamp(idx));
        _takeRequestedMs = _lastInvokeMs;
        _takePhase = TakePhase.AwaitingTakeAck;
        _lastLoggedAttachmentDelta = -1;

        Array.Clear(_pendingTakeSlotsPre);
        mailbox.SnapshotAttachmentState(idx, out _pendingTakeGilPre, _pendingTakeSlotsPre);
        TraceTake($"begin idx={idx} cid=0x{_pendingTakeKey.ContentId:X} ts={_pendingTakeKey.Timestamp} slots=[{_pendingTakeSlotsPre[0]},{_pendingTakeSlotsPre[1]},{_pendingTakeSlotsPre[2]},{_pendingTakeSlotsPre[3]},{_pendingTakeSlotsPre[4]}] gil={_pendingTakeGilPre}");

        if (!proxy->TakeAttachments((uint)idx, -1))
        {
            MogLog.Warning($"[Mogmail] TakeAttachments({idx}) returned false. Skipping letter.");
            Skipped++;
            ResetTakePhase();
            return;
        }

        TraceTake($"TakeAttachments({idx}) returned true, awaiting server ack");
    }

    private void OnServerRejection(string message)
    {
        if (State != RunState.Running || _takePhase != TakePhase.AwaitingTakeAck) return;

        MogLog.Warning($"[Mogmail] server rejection mid-take: \"{message}\" idx={_pendingTakeIdx}. Skipping letter and backing off {RejectionBackoffMs}ms.");
        Skipped++;
        _rejectionBackoffUntilMs = Environment.TickCount64 + RejectionBackoffMs;
        ResetTakePhase();
    }

    private static void TraceTake(string line)
    {
#if DEBUG
        MogLog.Information($"[Mogmail][trace] {line}");
#else
        _ = line;
#endif
    }

    private void InvokeDelete(int idx)
    {
        _lastInvokeMs = Environment.TickCount64;
        _firstCall = false;

        var proxy = InfoProxyLetter.Instance();
        if (proxy == null) { Skipped++; return; }
        if (idx < 0 || idx >= proxy->EntryCount) { Skipped++; return; }

        var mailbox = Plugin.Instance.Mailbox;
        var cid = mailbox.GetSenderContentId(idx);
        var ts = mailbox.GetLetterTimestamp(idx);

        _deleteBlacklist.Add((cid, ts));

        var deleted = proxy->DeleteLetter((uint)idx);
        if (deleted) Processed++; else Skipped++;
    }
}
