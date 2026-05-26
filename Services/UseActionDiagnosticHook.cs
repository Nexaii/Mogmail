using System;
#if DEBUG
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mogmail.Constants;
#endif

namespace Mogmail.Services;

public sealed unsafe class UseActionDiagnosticHook : IDisposable
{
#if DEBUG
    private static readonly long[] SampleOffsetsMs = { 500, 1000, 2000, 5000 };

    private readonly Hook<ActionManager.Delegates.UseAction>? _hook;
    private readonly List<PendingSample> _pendingSamples = new();

    private sealed class PendingSample
    {
        public long FireMs;
        public ActionType ActionType;
        public uint ActionId;
        public bool ReturnValue;
        public int NextOffsetIdx;
    }
#endif

    public UseActionDiagnosticHook()
    {
#if DEBUG
        var address = (nint)ActionManager.MemberFunctionPointers.UseAction;
        if (address == 0)
        {
            MogLog.Warning("[Mogmail] UseAction address not resolved. Diagnostic hook disabled.");
            return;
        }
        _hook = Plugin.GameInteropProvider.HookFromAddress<ActionManager.Delegates.UseAction>(address, Detour);
        _hook.Enable();
        Plugin.Framework.Update += Tick;
        MogLog.Information($"[Mogmail] UseAction diagnostic hook installed at 0x{address:X}.");
#endif
    }

    public void Dispose()
    {
#if DEBUG
        Plugin.Framework.Update -= Tick;
        _hook?.Disable();
        _hook?.Dispose();
        _pendingSamples.Clear();
#endif
    }

#if DEBUG
    private bool Detour(ActionManager* mgr, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        var rc = _hook!.Original(mgr, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);

        if (actionType != ActionType.Item) return rc;

        var now = Environment.TickCount64;
        MogLog.Information(
            $"[Mogmail][diag] UseAction fire type={actionType} id={actionId} extra={extraParam:X} mode={mode} rc={rc} anim={mgr->AnimationLock:F2}s queued={mgr->ActionQueued} casting={Plugin.Condition[ConditionFlag.Casting]} addons=[{ListBlockingAddons()}]");

        _pendingSamples.Add(new PendingSample
        {
            FireMs = now,
            ActionType = actionType,
            ActionId = actionId,
            ReturnValue = rc,
            NextOffsetIdx = 0,
        });

        return rc;
    }

    private void Tick(IFramework framework)
    {
        if (_pendingSamples.Count == 0) return;
        var now = Environment.TickCount64;

        for (var i = _pendingSamples.Count - 1; i >= 0; i--)
        {
            var s = _pendingSamples[i];
            var dueOffset = SampleOffsetsMs[s.NextOffsetIdx];
            if (now - s.FireMs < dueOffset) continue;

            LogSample(s, now);
            s.NextOffsetIdx++;
            if (s.NextOffsetIdx >= SampleOffsetsMs.Length)
                _pendingSamples.RemoveAt(i);
        }
    }

    private static void LogSample(PendingSample s, long now)
    {
        var elapsed = now - s.FireMs;
        var am = ActionManager.Instance();
        var anim = am != null ? am->AnimationLock : -1f;
        var queued = am != null && am->ActionQueued;
        var status = am != null ? am->GetActionStatus(s.ActionType, s.ActionId) : 0u;
        var casting = Plugin.Condition[ConditionFlag.Casting];
        var occupied = Plugin.Condition[ConditionFlag.OccupiedInEvent];
        var addons = ListBlockingAddons();

        MogLog.Information(
            $"[Mogmail][diag] +{elapsed}ms type={s.ActionType} id={s.ActionId} rc0={s.ReturnValue} anim={anim:F2}s queued={queued} casting={casting} occupied={occupied} status={status} addons=[{addons}]");
    }

    private static string ListBlockingAddons()
    {
        var hits = new List<string>();
        foreach (var name in AddonNames.Blocking)
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name, 1);
            if (addon != null && addon->IsVisible) hits.Add(name);
        }
        return string.Join(",", hits);
    }
#endif
}
