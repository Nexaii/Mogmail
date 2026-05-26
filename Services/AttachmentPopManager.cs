using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Mogmail.Constants;

namespace Mogmail.Services;

public sealed unsafe class AttachmentPopManager : IDisposable
{
    private const long MinFloorMs = 250;
    private const long UseAckTimeoutMs = 12000;
    private const int MaxPerSession = 100;
    private const uint UseActionExtraParam = 65535;
    private const long StatusReadyWaitMaxMs = 18000;
    private const long PostConfirmGraceMs = 800;
    private const int MaxUseActionRetries = 3;
    private const float AnimationLockEpsilon = 0.05f;

    private const long ArmStillnessMs = 400;
    private const float ArmStillnessEpsilon = 0.05f;
    private const long ArmTimeoutMs = 60_000;
    private const long MailboxCloseThrottleMs = 750;
    private const int ConsecutiveSkipAbortThreshold = 5;
    private const long PostSuccessCooldownMs = 3000;
    private const long SilentFailureRefireMs = 5000;
    private const int MaxSilentRefires = 3;

    private static readonly InventoryType[] PlayerBags =
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    };

    public enum RunState { Idle, Running }
    private enum Phase { ReadyToUse, AwaitingUseAck }

    public RunState State { get; private set; } = RunState.Idle;
    public int Processed { get; private set; }
    public int Skipped { get; private set; }
    public int Total { get; private set; }

    private readonly Queue<uint> _itemQueue = new();
    private uint _pendingUseItemId;
    private int _pendingPreCount;
    private long _useRequestedMs;
    private long _lastInvokeMs;
    private bool _firstCall;
    private Phase _phase = Phase.ReadyToUse;

    private bool _armed;
    private string _armReason = "";
    private long _armStartMs;
    private long _armStillSinceMs;
    private Vector3 _armLastPos;
    private long _lastMailboxCloseAttemptMs;
    private int _consecutiveSkips;
    private uint _waitingItemId;
    private long _waitingSinceMs;
    private uint _waitingLastStatus;
    private int _waitingPreCount;
    private bool _waitingPreUnlocked;
    private uint _waitingBaseId;
    private long _postConfirmGraceUntilMs;
    private readonly Dictionary<uint, int> _useActionRetries = new();
    private uint _pendingBaseItemId;
    private bool _pendingWasUnlocked;
#if DEBUG
    private long _lastDiagLogMs;
#endif
    private long _lastSuccessMs;
    private long _lastUseActionTrueFireMs;
    private uint _lastUseActionTrueItemId;
    private int _silentRefireCount;

    public AttachmentPopManager()
    {
        Plugin.Framework.Update += Tick;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= Tick;
        _itemQueue.Clear();
        State = RunState.Idle;
    }

    public bool IsIdle => State == RunState.Idle && !_armed;

    public bool IsArmed => _armed;

    public bool IsInPostUseActionWindow(int windowMs)
    {
        if (_lastUseActionTrueFireMs <= 0) return false;
        return Environment.TickCount64 - _lastUseActionTrueFireMs < windowMs;
    }

    public void NotifyConfirmFired()
    {
        _postConfirmGraceUntilMs = Environment.TickCount64 + PostConfirmGraceMs;
    }

    public void Arm(string reason)
    {
        if (State != RunState.Idle) return;
        if (_armed) return;
        _armed = true;
        _armReason = reason;
        _armStartMs = Environment.TickCount64;
        _armStillSinceMs = 0;
        _armLastPos = Plugin.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
        MogLog.Information($"[Mogmail] pop armed ({reason}). Waiting for mailbox close + stillness.");
    }

    public void Disarm(string reason)
    {
        if (!_armed) return;
        _armed = false;
        _armReason = "";
        _armStartMs = 0;
        _armStillSinceMs = 0;
        MogLog.Information($"[Mogmail] pop disarmed: {reason}.");
    }

    public void Start(bool allowSensitive = false)
    {
        if (State != RunState.Idle)
        {
            Plugin.Chat.Print("[Mogmail] pop already running.");
            return;
        }

        if (!Plugin.ClientState.IsLoggedIn)
        {
            Plugin.Chat.Print("[Mogmail] pop requires login.");
            return;
        }

        var items = ScanInventoryForRegistrableItems(allowSensitive, out var skippedUnlocked, out var skippedDisabled);
        if (items.Count == 0)
        {
            var detail = (skippedUnlocked, skippedDisabled) switch
            {
                (0, 0) => "no registrable items found in inventory.",
                (_, 0) => $"all {skippedUnlocked} registrable items are already unlocked.",
                (0, _) => $"all {skippedDisabled} registrable items are in disabled categories.",
                _ => $"{skippedUnlocked} already unlocked, {skippedDisabled} in disabled categories.",
            };
            Plugin.Chat.Print($"[Mogmail] pop: {detail}");
            return;
        }
        if (skippedUnlocked > 0 || skippedDisabled > 0)
            MogLog.Information($"[Mogmail] pop scan: {items.Count} queued, {skippedUnlocked} already unlocked, {skippedDisabled} category-disabled.");

        _itemQueue.Clear();
        foreach (var id in items) _itemQueue.Enqueue(id);
        Total = _itemQueue.Count;
        Processed = 0;
        Skipped = 0;
        _firstCall = true;
        _phase = Phase.ReadyToUse;
        _lastInvokeMs = 0;
        _pendingUseItemId = 0;
        _consecutiveSkips = 0;
        ResetWaitingState();
        State = RunState.Running;

        MogLog.Information($"[Mogmail] pop started. {Total} items.");
        Plugin.Chat.Print($"[Mogmail] pop started. {Total} items.");
    }

    public void Abort(string reason)
    {
        if (State == RunState.Idle) return;
        MogLog.Information($"[Mogmail] pop aborted: {reason}. processed={Processed} skipped={Skipped}");
        Plugin.Chat.Print($"[Mogmail] pop aborted ({reason}). {Processed} done, {Skipped} skipped.");
        FinishRun();
    }

    private void FinishRun()
    {
        State = RunState.Idle;
        _itemQueue.Clear();
        _pendingUseItemId = 0;
        _pendingBaseItemId = 0;
        _pendingWasUnlocked = false;
        _phase = Phase.ReadyToUse;
        _useActionRetries.Clear();
        _lastSuccessMs = 0;
        _lastUseActionTrueFireMs = 0;
        _lastUseActionTrueItemId = 0;
        _silentRefireCount = 0;
        ResetWaitingState();
    }

    private void Tick(IFramework framework)
    {
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            try { MogLog.Error($"[Mogmail] pop tick exception: {ex}"); } catch { }
            try
            {
                if (State != RunState.Idle) Abort("tick exception");
                if (_armed) Disarm("tick exception");
            }
            catch
            {
                State = RunState.Idle;
                _armed = false;
            }
        }
    }

    private void TickCore()
    {
        if (_armed && State == RunState.Idle)
        {
            TickArmed();
            return;
        }

        if (State != RunState.Running) return;

        if (!GatesValid(out var reason))
        {
            Abort(reason);
            return;
        }

        if (_phase == Phase.AwaitingUseAck)
        {
            TickAwaitingUseAck();
            return;
        }

        if (NotReadyYet()) return;

        if (_itemQueue.Count == 0 || Processed + Skipped >= MaxPerSession)
        {
            Complete();
            return;
        }

        if (!TryAdvanceHead()) return;

        var head = _itemQueue.Peek();
        if (InvokeUse(head))
        {
            _itemQueue.Dequeue();
            _useActionRetries.Remove(head);
        }
        else
        {
            HandleUseActionFailure(head);
        }
    }

    private void HandleUseActionFailure(uint itemId)
    {
        _useActionRetries.TryGetValue(itemId, out var n);
        n++;
        _useActionRetries[itemId] = n;
        if (n >= MaxUseActionRetries)
        {
            _itemQueue.Dequeue();
            _useActionRetries.Remove(itemId);
            MogLog.Warning($"[Mogmail] pop skip {GetItemName(itemId)} (#{itemId}) after {MaxUseActionRetries} UseAction failures.");
            Skipped++;
            _consecutiveSkips++;
            CheckConsecutiveSkipAbort();
            return;
        }
        _postConfirmGraceUntilMs = Environment.TickCount64 + PostConfirmGraceMs;
    }

    private bool TryAdvanceHead()
    {
        var head = _itemQueue.Peek();
        EnsureHeadSnapshot(head);

        if (DetectPassiveHeadCompletion(head, out var passiveReason))
        {
            _itemQueue.Dequeue();
            _useActionRetries.Remove(head);
            MogLog.Information($"[Mogmail] pop ok {GetItemName(head)} (#{head}): {passiveReason} (no ack phase).");
            Processed++;
            _consecutiveSkips = 0;
            _lastSuccessMs = Environment.TickCount64;
            _silentRefireCount = 0;
            ResetWaitingState();
            return false;
        }

        var am = ActionManager.Instance();
        if (am == null)
        {
            Abort("action manager unavailable");
            return false;
        }

        var now = Environment.TickCount64;
        if (now < _postConfirmGraceUntilMs)
        {
            TraceDiag(() => $"hold {GetItemName(head)} (#{head}): post-confirm grace {_postConfirmGraceUntilMs - now}ms");
            return false;
        }

        if (_lastSuccessMs > 0 && now - _lastSuccessMs < PostSuccessCooldownMs)
        {
            TraceDiag(() => $"hold {GetItemName(head)} (#{head}): post-success cooldown {PostSuccessCooldownMs - (now - _lastSuccessMs)}ms");
            return false;
        }

        if (IsBlockingAddonVisible(out var blockerName))
        {
            HoldOnHead(head, $"addon {blockerName} visible");
            return false;
        }

        if (am->AnimationLock > AnimationLockEpsilon)
        {
            HoldOnHead(head, $"AnimationLock={am->AnimationLock:F2}s");
            return false;
        }

        if (am->ActionQueued)
        {
            HoldOnHead(head, $"ActionQueued={am->QueuedActionId}");
            return false;
        }

        if (Plugin.Condition[ConditionFlag.Casting])
        {
            HoldOnHead(head, "ConditionFlag.Casting");
            return false;
        }

        if (Plugin.Condition[ConditionFlag.OccupiedInEvent])
        {
            HoldOnHead(head, "ConditionFlag.OccupiedInEvent");
            return false;
        }

        var status = am->GetActionStatus(ActionType.Item, head);
        if (status == 0)
            return true;

        if (_waitingItemId != head)
        {
            _waitingItemId = head;
            _waitingSinceMs = now;
            _waitingLastStatus = status;
            MogLog.Information($"[Mogmail] pop wait {GetItemName(head)} (#{head}): GetActionStatus={status}");
        }
        else if (status != _waitingLastStatus)
        {
            _waitingLastStatus = status;
            MogLog.Information($"[Mogmail] pop wait {GetItemName(head)} (#{head}): GetActionStatus={status}");
        }

        if (now - _waitingSinceMs > StatusReadyWaitMaxMs)
        {
            _itemQueue.Dequeue();
            MogLog.Warning($"[Mogmail] pop skip {GetItemName(head)} (#{head}) after {StatusReadyWaitMaxMs}ms: GetActionStatus={status}");
            Skipped++;
            _consecutiveSkips++;
            ResetWaitingState();
            CheckConsecutiveSkipAbort();
        }

        return false;
    }

    private void EnsureHeadSnapshot(uint head)
    {
        if (_waitingItemId == head && _waitingPreCount > 0) return;
        _waitingPreCount = GetItemCount(head);
        var (baseId, _) = ItemUtil.GetBaseId(head);
        _waitingBaseId = baseId;
        _waitingPreUnlocked = IsBaseUnlockedNow(baseId);
    }

    private bool DetectPassiveHeadCompletion(uint head, out string reason)
    {
        reason = "";
        if (_waitingPreCount <= 0) return false;

        var live = GetItemCount(head);
        if (live < _waitingPreCount)
        {
            reason = $"inventory drop {_waitingPreCount}->{live}";
            return true;
        }

        if (_waitingBaseId != 0 && !_waitingPreUnlocked && IsBaseUnlockedNow(_waitingBaseId))
        {
            reason = "unlock-state flipped";
            return true;
        }

        return false;
    }

    private void ResetWaitingState()
    {
        _waitingItemId = 0;
        _waitingSinceMs = 0;
        _waitingLastStatus = 0;
        _waitingPreCount = 0;
        _waitingPreUnlocked = false;
        _waitingBaseId = 0;
    }

    private void HoldOnHead(uint head, string reason)
    {
        var now = Environment.TickCount64;
        if (_waitingItemId != head)
        {
            _waitingItemId = head;
            _waitingSinceMs = now;
            _waitingLastStatus = 0;
            MogLog.Information($"[Mogmail] pop wait {GetItemName(head)} (#{head}): {reason}");
        }

        if (now - _waitingSinceMs > StatusReadyWaitMaxMs)
        {
            _itemQueue.Dequeue();
            MogLog.Warning($"[Mogmail] pop skip {GetItemName(head)} (#{head}) after {StatusReadyWaitMaxMs}ms: {reason}");
            Skipped++;
            _consecutiveSkips++;
            ResetWaitingState();
            CheckConsecutiveSkipAbort();
        }
    }

    private static bool IsBlockingAddonVisible(out string name)
    {
        foreach (var addonName in AddonNames.Blocking)
        {
            var ptr = Plugin.GameGui.GetAddonByName<AtkUnitBase>(addonName, 1);
            if (ptr != null && ptr->IsVisible)
            {
                name = addonName;
                return true;
            }
        }
        name = "";
        return false;
    }

    private void TickAwaitingUseAck()
    {
        var liveCount = GetItemCount(_pendingUseItemId);
        if (liveCount < _pendingPreCount)
        {
            CompleteAckSuccess("inventory drop");
            return;
        }

        if (_pendingBaseItemId != 0 && !_pendingWasUnlocked && IsBaseUnlockedNow(_pendingBaseItemId))
        {
            CompleteAckSuccess("unlock-state flipped");
            return;
        }

        TraceAckTick();

        var elapsed = Environment.TickCount64 - _useRequestedMs;

        if (elapsed > SilentFailureRefireMs
            && _lastUseActionTrueItemId == _pendingUseItemId
            && _silentRefireCount < MaxSilentRefires)
        {
            var refireName = GetItemName(_pendingUseItemId);
            _silentRefireCount++;
            MogLog.Information($"[Mogmail] pop silent refire {refireName} (#{_pendingUseItemId}) attempt {_silentRefireCount}/{MaxSilentRefires} after {elapsed}ms.");
            var am = ActionManager.Instance();
            if (am != null)
            {
                var extra = ResolveExtraParam(_pendingUseItemId);
                am->UseAction(ActionType.Item, _pendingUseItemId, extraParam: extra);
                _useRequestedMs = Environment.TickCount64;
                _lastUseActionTrueFireMs = _useRequestedMs;
            }
            return;
        }

        if (elapsed > UseAckTimeoutMs)
        {
            var name = GetItemName(_pendingUseItemId);
            MogLog.Warning($"[Mogmail] pop ack timeout for {name} (#{_pendingUseItemId}) after {UseAckTimeoutMs}ms refires={_silentRefireCount}. liveCount={liveCount} pre={_pendingPreCount}. Skipping.");
            Skipped++;
            _consecutiveSkips++;
            _phase = Phase.ReadyToUse;
            _pendingUseItemId = 0;
            _pendingBaseItemId = 0;
            _pendingWasUnlocked = false;
            _silentRefireCount = 0;

            CheckConsecutiveSkipAbort();
        }
    }

    private void CompleteAckSuccess(string reason)
    {
        var name = GetItemName(_pendingUseItemId);
        TraceDiag(() => $"ack ok {name} (#{_pendingUseItemId}): {reason} in {Environment.TickCount64 - _useRequestedMs}ms");
        Processed++;
        _consecutiveSkips = 0;
        _phase = Phase.ReadyToUse;
        _pendingUseItemId = 0;
        _pendingBaseItemId = 0;
        _pendingWasUnlocked = false;
        _lastSuccessMs = Environment.TickCount64;
        _silentRefireCount = 0;
    }

    private static bool IsBaseUnlockedNow(uint baseId)
    {
        var sheet = Plugin.Data.GetExcelSheet<Item>();
        if (!sheet.TryGetRow(baseId, out var item)) return false;
        if (!Plugin.UnlockState.IsItemUnlockable(item)) return false;
        return Plugin.UnlockState.IsItemUnlocked(item);
    }

    private void TraceAckTick()
    {
#if DEBUG
        var now = Environment.TickCount64;
        if (now - _lastDiagLogMs < 500) return;
        _lastDiagLogMs = now;
        var am = ActionManager.Instance();
        var lockSec = am != null ? am->AnimationLock : -1f;
        MogLog.Information($"[Mogmail][trace] ack-wait #{_pendingUseItemId} elapsed={now - _useRequestedMs}ms anim={lockSec:F2}s");
#endif
    }

    private static void TraceDiag(Func<string> msg)
    {
#if DEBUG
        MogLog.Information($"[Mogmail][trace] {msg()}");
#endif
    }

    private void CheckConsecutiveSkipAbort()
    {
        if (_consecutiveSkips >= ConsecutiveSkipAbortThreshold)
            Abort($"{ConsecutiveSkipAbortThreshold} consecutive skips");
    }

    private bool InvokeUse(uint itemId)
    {
        _lastInvokeMs = Environment.TickCount64;
        _firstCall = false;

        var name = GetItemName(itemId);

        _pendingPreCount = GetItemCount(itemId);
        if (_pendingPreCount == 0)
        {
            MogLog.Information($"[Mogmail] pop skip {name} (#{itemId}): not in inventory.");
            Skipped++;
            _consecutiveSkips++;
            CheckConsecutiveSkipAbort();
            return true;
        }

        var am = ActionManager.Instance();
        if (am == null)
        {
            MogLog.Warning("[Mogmail] ActionManager unavailable. Aborting pop.");
            Abort("action manager unavailable");
            return true;
        }

        var extra = ResolveExtraParam(itemId);
        TraceDiag(() => $"fire {name} (#{itemId}) extra=0x{extra:X} anim={am->AnimationLock:F2}s queued={am->ActionQueued}");
        MogLog.Information($"[Mogmail] pop use {name} (#{itemId}).");
        var ok = am->UseAction(ActionType.Item, itemId, extraParam: extra);
        if (!ok)
        {
            MogLog.Information($"[Mogmail] pop UseAction returned false for {name} (#{itemId}).");
            _lastUseActionTrueItemId = 0;
            return false;
        }

        _lastUseActionTrueFireMs = _lastInvokeMs;
        _lastUseActionTrueItemId = itemId;

        var (baseId, _) = ItemUtil.GetBaseId(itemId);
        _pendingUseItemId = itemId;
        _pendingBaseItemId = baseId;
        _pendingWasUnlocked = IsBaseUnlockedNow(baseId);
        _useRequestedMs = _lastInvokeMs;
        _phase = Phase.AwaitingUseAck;
        return true;
    }

    private static string GetItemName(uint rawItemId)
    {
        var (baseId, _) = ItemUtil.GetBaseId(rawItemId);
        var sheet = Plugin.Data.GetExcelSheet<Item>();
        if (!sheet.TryGetRow(baseId, out var item)) return $"item#{rawItemId}";
        var name = item.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? $"item#{rawItemId}" : name;
    }

    private void TickArmed()
    {
        var now = Environment.TickCount64;
        if (now - _armStartMs > ArmTimeoutMs)
        {
            Disarm("arm timeout");
            return;
        }

        if (!ArmGatesValid(out var reason, out var stillnessOk))
        {
            _armStillSinceMs = 0;
            _armLastPos = Plugin.ObjectTable.LocalPlayer?.Position ?? _armLastPos;
#if DEBUG
            MogLog.Information($"[Mogmail] pop arm gate fail: {reason}");
#endif
            return;
        }

        if (!stillnessOk) return;

        _armed = false;
        var reasonCopy = _armReason;
        _armReason = "";
        _armStartMs = 0;
        _armStillSinceMs = 0;
        MogLog.Information($"[Mogmail] pop arming gates cleared ({reasonCopy}). Starting pop.");
        Start();
    }

    private bool ArmGatesValid(out string reason, out bool stillnessOk)
    {
        reason = "";
        stillnessOk = false;

        if (!Plugin.ClientState.IsLoggedIn) { reason = "logged out"; return false; }
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) { reason = "zoning"; return false; }
        if (Plugin.Condition[ConditionFlag.BetweenAreas51]) { reason = "zoning"; return false; }
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]) { reason = "cutscene"; return false; }
        if (Plugin.Condition[ConditionFlag.InCombat]) { reason = "in combat"; return false; }
        if (Plugin.Condition[ConditionFlag.Unconscious]) { reason = "unconscious"; return false; }

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null) { reason = "no local player"; return false; }
        if (player.IsDead) { reason = "dead"; return false; }

        if (Plugin.Instance.Mailbox.IsMailboxOpen)
        {
            TryCloseMailbox();
            reason = "mailbox open";
            return false;
        }

        var pos = player.Position;
        var moved = Vector3.Distance(pos, _armLastPos) > ArmStillnessEpsilon;
        _armLastPos = pos;

        var now = Environment.TickCount64;
        if (moved)
        {
            _armStillSinceMs = now;
            reason = "moving";
            return true;
        }

        if (_armStillSinceMs == 0)
        {
            _armStillSinceMs = now;
            return true;
        }

        if (now - _armStillSinceMs >= ArmStillnessMs)
            stillnessOk = true;
        return true;
    }

    private void TryCloseMailbox()
    {
        var now = Environment.TickCount64;
        if (now - _lastMailboxCloseAttemptMs < MailboxCloseThrottleMs) return;
        _lastMailboxCloseAttemptMs = now;

        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(AddonNames.LetterList, 1);
        if (addon == null) return;
        if (!addon->IsVisible) return;
        addon->Close(true);
        MogLog.Information("[Mogmail] pop arm: requested LetterList close.");
    }

    private bool NotReadyYet()
    {
        if (_firstCall) return false;
        return Environment.TickCount64 - _lastInvokeMs < MinFloorMs;
    }

    private static bool GatesValid(out string reason)
    {
        reason = "";
        if (!Plugin.ClientState.IsLoggedIn) { reason = "logged out"; return false; }
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) { reason = "zoning"; return false; }
        if (Plugin.Condition[ConditionFlag.BetweenAreas51]) { reason = "zoning"; return false; }
        if (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]) { reason = "cutscene"; return false; }
        if (Plugin.Condition[ConditionFlag.InCombat]) { reason = "in combat"; return false; }
        return true;
    }

    private void Complete()
    {
        MogLog.Information($"[Mogmail] pop done. processed={Processed} skipped={Skipped}");
        Plugin.Chat.Print($"[Mogmail] pop: {Processed} done, {Skipped} skipped.");
        FinishRun();
    }

    private static List<uint> ScanInventoryForRegistrableItems(bool allowSensitive, out int skippedUnlocked, out int skippedDisabled)
    {
        skippedUnlocked = 0;
        skippedDisabled = 0;
        var result = new List<uint>();
        uint sensitivePicked = 0;
        var mgr = InventoryManager.Instance();
        if (mgr == null) return result;

        var seen = new HashSet<uint>();
        foreach (var bagType in PlayerBags)
        {
            var c = mgr->GetInventoryContainer(bagType);
            if (c == null) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var rawItemId = c->Items[i].ItemId;
                if (rawItemId == 0) continue;
                if (!seen.Add(rawItemId)) continue;
                var bId = GetBaseId(rawItemId);
                if (ItemRegistryClassifier.IsBlacklistedFromPop(bId)) continue;
                if (ItemRegistryClassifier.TryGetSensitiveCategory(bId, out var sensCat))
                {
                    if (!allowSensitive) { skippedDisabled++; continue; }
                    if (!IsSensitiveCategoryAllowed(sensCat)) { skippedDisabled++; continue; }
                    if (sensitivePicked != 0) { skippedDisabled++; continue; }
                    sensitivePicked = rawItemId;
                    result.Add(rawItemId);
                    continue;
                }
                if (!ItemRegistryClassifier.TryClassify(rawItemId, out var category, out var baseId)) continue;
                if (!Plugin.Config.IsPopCategoryEnabled(category)) { skippedDisabled++; continue; }
                if (ItemRegistryClassifier.IsAlreadyUnlocked(baseId)) { skippedUnlocked++; continue; }
                result.Add(rawItemId);
            }
        }
        return result;
    }

    private static uint GetBaseId(uint rawItemId)
    {
        var (baseId, _) = Dalamud.Utility.ItemUtil.GetBaseId(rawItemId);
        return baseId;
    }

    public static bool InventoryContainsSensitive()
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return false;
        foreach (var bagType in PlayerBags)
        {
            var c = mgr->GetInventoryContainer(bagType);
            if (c == null) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var rawItemId = c->Items[i].ItemId;
                if (rawItemId == 0) continue;
                if (ItemRegistryClassifier.IsSensitive(GetBaseId(rawItemId))) return true;
            }
        }
        return false;
    }

    public static List<uint> CollectAllowedSensitiveInInventory()
    {
        var result = new List<uint>();
        var mgr = InventoryManager.Instance();
        if (mgr == null) return result;
        var seen = new HashSet<uint>();
        foreach (var bagType in PlayerBags)
        {
            var c = mgr->GetInventoryContainer(bagType);
            if (c == null) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var rawItemId = c->Items[i].ItemId;
                if (rawItemId == 0) continue;
                if (!seen.Add(rawItemId)) continue;
                var bId = GetBaseId(rawItemId);
                if (!ItemRegistryClassifier.TryGetSensitiveCategory(bId, out var cat)) continue;
                if (!IsSensitiveCategoryAllowed(cat)) continue;
                result.Add(rawItemId);
            }
        }
        return result;
    }

    private static bool IsSensitiveCategoryAllowed(SensitiveCategory category) => category switch
    {
        SensitiveCategory.Fantasia => Plugin.Config.AllowFantasiaInPop,
        SensitiveCategory.MsqProgression => Plugin.Config.AllowMsqProgressionInPop,
        SensitiveCategory.OneHerosJourney => Plugin.Config.AllowOneHerosJourneyInPop,
        SensitiveCategory.OneRetainersJourney => Plugin.Config.AllowOneRetainersJourneyInPop,
        _ => false,
    };

    private static uint ResolveExtraParam(uint rawItemId)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return UseActionExtraParam;

        foreach (var bagType in PlayerBags)
        {
            var c = mgr->GetInventoryContainer(bagType);
            if (c == null) continue;
            for (var i = 0; i < c->Size; i++)
            {
                if (c->Items[i].ItemId != rawItemId) continue;
                return ((uint)bagType << 16) | (uint)i;
            }
        }
        return UseActionExtraParam;
    }

    private static int GetItemCount(uint rawItemId)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return 0;

        var total = 0;
        foreach (var bagType in PlayerBags)
        {
            var c = mgr->GetInventoryContainer(bagType);
            if (c == null) continue;
            for (var i = 0; i < c->Size; i++)
            {
                if (c->Items[i].ItemId == rawItemId)
                    total += (int)c->Items[i].Quantity;
            }
        }
        return total;
    }
}
