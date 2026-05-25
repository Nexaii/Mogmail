using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Mogmail.Models;
using Mogmail.Services;

namespace Mogmail.Managers;

public sealed class ReadAllManager : IDisposable
{
    private const long MinFloorMs = 50;
    private const long DetailAckTimeoutMs = 3000;
    private const long SessionTimeoutMs = 2 * 60 * 1000;

    private enum Phase { ReadyForDetail, AwaitingDetailAck }

    public enum RunState { Idle, Running }

    public RunState State { get; private set; } = RunState.Idle;
    public int Processed { get; private set; }
    public int Skipped { get; private set; }
    public int Total { get; private set; }
    public string CurrentLabel { get; private set; } = "";

    private readonly Queue<int> _readQueue = new();
    private Phase _phase = Phase.ReadyForDetail;
    private int _pendingIdx = -1;
    private long _detailRequestedMs;
    private long _lastInvokeMs;
    private long _runStartMs;
    private bool _firstCall;
    private bool _deleteAfter;

    public ReadAllManager()
    {
        Plugin.Framework.Update += Tick;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= Tick;
        _readQueue.Clear();
        State = RunState.Idle;
    }

    public bool IsIdle => State == RunState.Idle;

    public void Start(string label, bool deleteAfter)
    {
        if (State != RunState.Idle) return;
        if (!Plugin.Instance.ClaimQueue.IsIdle) return;
        if (!Plugin.Instance.PopQueue.IsIdle) return;
        if (!Plugin.Instance.Mailbox.CanRequestLetterDetail)
        {
            Plugin.Chat.Print("[Mogmail] Read All: RequestLetterDetail unavailable, mark-read disabled.");
            return;
        }

        _readQueue.Clear();
        var mailbox = Plugin.Instance.Mailbox;
        var includeGM = Plugin.Config.IncludeGMInSweeps;
        var count = (int)mailbox.LoadedLetterCount;
        for (var i = 0; i < count; i++)
        {
            if (!includeGM && mailbox.IsGMLetter(i)) continue;
            if (mailbox.IsLetterReadFlag(i)) continue;
            _readQueue.Enqueue(i);
        }

        Total = _readQueue.Count;
        Processed = 0;
        Skipped = 0;
        _firstCall = true;
        _phase = Phase.ReadyForDetail;
        _pendingIdx = -1;
        _detailRequestedMs = 0;
        _lastInvokeMs = 0;
        _runStartMs = Environment.TickCount64;
        _deleteAfter = deleteAfter;
        CurrentLabel = label;
        State = RunState.Running;

        MogLog.Information($"[Mogmail] {label} started. {Total} unread letters.");
        if (Total == 0 && !deleteAfter)
        {
            Plugin.Chat.Print("[Mogmail] Read All: nothing to mark.");
            FinishRun();
        }
    }

    public void Abort(string reason)
    {
        if (State == RunState.Idle) return;
        MogLog.Information($"[Mogmail] {CurrentLabel} aborted: {reason}. processed={Processed} skipped={Skipped}");
        Plugin.Chat.Print($"[Mogmail] {CurrentLabel} aborted ({reason}). {Processed} done, {Skipped} skipped.");
        FinishRun();
    }

    private void FinishRun()
    {
        State = RunState.Idle;
        _readQueue.Clear();
        _phase = Phase.ReadyForDetail;
        _pendingIdx = -1;
    }

    private void Tick(IFramework framework)
    {
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            try { MogLog.Error($"[Mogmail] ReadAll tick exception: {ex}"); } catch { }
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

        if (_phase == Phase.AwaitingDetailAck)
        {
            TickAwaitingDetailAck();
            return;
        }

        if (NotReadyYet()) return;

        if (_readQueue.Count == 0)
        {
            Complete();
            return;
        }

        BeginRead(_readQueue.Dequeue());
    }

    private void BeginRead(int idx)
    {
        var mailbox = Plugin.Instance.Mailbox;
        if (idx < 0 || idx >= (int)mailbox.LoadedLetterCount)
        {
            Skipped++;
            return;
        }

        if (mailbox.IsLetterReadFlag(idx))
        {
            Processed++;
            return;
        }

        if (mailbox.PendingDetailIndex >= 0)
        {
            _readQueue.Enqueue(idx);
            return;
        }

        _pendingIdx = idx;

        if (!mailbox.RequestLetterDetail(idx))
        {
            MogLog.Warning($"[Mogmail] Read All: RequestLetterDetail({idx}) returned false. Skipping.");
            Skipped++;
            _pendingIdx = -1;
            return;
        }

        _lastInvokeMs = Environment.TickCount64;
        _detailRequestedMs = _lastInvokeMs;
        _firstCall = false;
        _phase = Phase.AwaitingDetailAck;
    }

    private void TickAwaitingDetailAck()
    {
        var mailbox = Plugin.Instance.Mailbox;
        if (mailbox.PendingDetailIndex < 0)
        {
            Processed++;
            _pendingIdx = -1;
                _phase = Phase.ReadyForDetail;
            return;
        }

        if (Environment.TickCount64 - _detailRequestedMs > DetailAckTimeoutMs)
        {
            MogLog.Warning($"[Mogmail] Read All: detail ack timeout for idx {_pendingIdx}. Skipping.");
            Skipped++;
            _pendingIdx = -1;
                _phase = Phase.ReadyForDetail;
        }
    }

    private bool NotReadyYet()
    {
        if (_firstCall) return false;
        return Environment.TickCount64 - _lastInvokeMs < MinFloorMs;
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

    private void Complete()
    {
        MogLog.Information($"[Mogmail] {CurrentLabel} done. processed={Processed} skipped={Skipped}");
        if (Total > 0)
            Plugin.Chat.Print($"[Mogmail] {CurrentLabel}: {Processed} read, {Skipped} skipped.");

        var chainDelete = _deleteAfter;
        var chainLabel = $"{CurrentLabel} -> Delete (Read & Empty)";
        FinishRun();

        if (chainDelete) FireDeleteChain(chainLabel);
    }

    private static void FireDeleteChain(string label)
    {
        var includeGM = Plugin.Config.IncludeGMInSweeps;
        var mailbox = Plugin.Instance.Mailbox;
        bool Predicate(int i) =>
            (includeGM || !mailbox.IsGMLetter(i))
            && mailbox.IsLetterReadFlag(i)
            && !mailbox.LetterHasUnclaimedAttachments(i);

        var count = (int)mailbox.LoadedLetterCount;
        var targets = 0;
        for (var i = 0; i < count; i++) if (Predicate(i)) targets++;
        if (targets == 0)
        {
            Plugin.Chat.Print($"[Mogmail] {label}: no read & empty letters.");
            return;
        }

        Plugin.Instance.ClaimQueue.StartDelete(ClaimAction.DeleteReadEmpty, label, Predicate, targets + 2);
    }
}
