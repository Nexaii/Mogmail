using System;
using Dalamud.Plugin.Ipc;
using Mogmail.Models;
using Mogmail.Services;

namespace Mogmail.IPC;

public sealed class IPCProvider : IDisposable
{
    private readonly ICallGateProvider<bool> _isAvailable;
    private readonly ICallGateProvider<bool> _isBusy;
    private readonly ICallGateProvider<bool> _isMailboxOpen;
    private readonly ICallGateProvider<int>  _letterCount;

    private readonly ICallGateProvider<bool> _claimAll;
    private readonly ICallGateProvider<bool> _claimAndDelete;
    private readonly ICallGateProvider<bool> _readAll;
    private readonly ICallGateProvider<bool> _readAllAndDelete;
    private readonly ICallGateProvider<bool> _deleteReadEmpty;
    private readonly ICallGateProvider<bool, bool> _pop;
    private readonly ICallGateProvider<bool> _stop;

    public IPCProvider()
    {
        var pi = Plugin.PluginInterface;

        _isAvailable      = pi.GetIpcProvider<bool>(IPCNames.IsAvailable);
        _isBusy           = pi.GetIpcProvider<bool>(IPCNames.IsBusy);
        _isMailboxOpen    = pi.GetIpcProvider<bool>(IPCNames.IsMailboxOpen);
        _letterCount      = pi.GetIpcProvider<int>(IPCNames.LetterCount);
        _claimAll         = pi.GetIpcProvider<bool>(IPCNames.ClaimAll);
        _claimAndDelete   = pi.GetIpcProvider<bool>(IPCNames.ClaimAndDelete);
        _readAll          = pi.GetIpcProvider<bool>(IPCNames.ReadAll);
        _readAllAndDelete = pi.GetIpcProvider<bool>(IPCNames.ReadAllAndDelete);
        _deleteReadEmpty  = pi.GetIpcProvider<bool>(IPCNames.DeleteReadEmpty);
        _pop              = pi.GetIpcProvider<bool, bool>(IPCNames.Pop);
        _stop             = pi.GetIpcProvider<bool>(IPCNames.Stop);

        _isAvailable.RegisterFunc(IsAvailable);
        _isBusy.RegisterFunc(IsBusy);
        _isMailboxOpen.RegisterFunc(IsMailboxOpen);
        _letterCount.RegisterFunc(LetterCount);
        _claimAll.RegisterFunc(ClaimAll);
        _claimAndDelete.RegisterFunc(ClaimAndDelete);
        _readAll.RegisterFunc(ReadAll);
        _readAllAndDelete.RegisterFunc(ReadAllAndDelete);
        _deleteReadEmpty.RegisterFunc(DeleteReadEmpty);
        _pop.RegisterFunc(Pop);
        _stop.RegisterFunc(Stop);
    }

    public void Dispose()
    {
        _isAvailable.UnregisterFunc();
        _isBusy.UnregisterFunc();
        _isMailboxOpen.UnregisterFunc();
        _letterCount.UnregisterFunc();
        _claimAll.UnregisterFunc();
        _claimAndDelete.UnregisterFunc();
        _readAll.UnregisterFunc();
        _readAllAndDelete.UnregisterFunc();
        _deleteReadEmpty.UnregisterFunc();
        _pop.UnregisterFunc();
        _stop.UnregisterFunc();
    }

    private static bool IsAvailable()
        => Plugin.ClientState.IsLoggedIn && Plugin.Instance.Mailbox.IsAvailable;

    private static bool IsBusy()
        => !Plugin.Instance.ClaimQueue.IsIdle
        || !Plugin.Instance.ReadAll.IsIdle
        || !Plugin.Instance.PopQueue.IsIdle;

    private static bool IsMailboxOpen()
        => Plugin.Instance.Mailbox.IsMailboxOpen;

    private static int LetterCount()
        => (int)Plugin.Instance.Mailbox.LoadedLetterCount;

    private static bool ClaimAll()
    {
        if (!CanAcceptMailboxAction()) return false;
        var candidates = CollectTakeCandidates();
        if (candidates.Count == 0) return false;
        Plugin.Instance.ClaimQueue.StartTake("IPC ClaimAll", candidates);
        return !Plugin.Instance.ClaimQueue.IsIdle;
    }

    private static bool ClaimAndDelete()
    {
        if (!CanAcceptMailboxAction()) return false;
        var candidates = CollectTakeCandidates();
        if (candidates.Count == 0) return false;
        Plugin.Instance.ClaimQueue.StartTakeAndDelete("IPC ClaimAndDelete", candidates);
        return !Plugin.Instance.ClaimQueue.IsIdle;
    }

    private static bool ReadAll()
    {
        if (!CanAcceptMailboxAction()) return false;
        Plugin.Instance.ReadAll.Start("IPC ReadAll", deleteAfter: false);
        return !Plugin.Instance.ReadAll.IsIdle;
    }

    private static bool ReadAllAndDelete()
    {
        if (!CanAcceptMailboxAction()) return false;
        Plugin.Instance.ReadAll.Start("IPC ReadAllAndDelete", deleteAfter: true);
        return !Plugin.Instance.ReadAll.IsIdle;
    }

    private static bool DeleteReadEmpty()
    {
        if (!CanAcceptMailboxAction()) return false;
        var includeGM = Plugin.Config.IncludeGMInSweeps;
        var mailbox = Plugin.Instance.Mailbox;
        bool Predicate(int i) =>
            (includeGM || !mailbox.IsGMLetter(i))
            && mailbox.IsLetterReadFlag(i)
            && !mailbox.LetterHasUnclaimedAttachments(i);

        var count = (int)mailbox.LoadedLetterCount;
        var targets = 0;
        for (var i = 0; i < count; i++) if (Predicate(i)) targets++;
        if (targets == 0) return false;

        Plugin.Instance.ClaimQueue.StartDelete(ClaimAction.DeleteReadEmpty, "IPC DeleteReadEmpty", Predicate, targets + 2);
        return !Plugin.Instance.ClaimQueue.IsIdle;
    }

    private static bool Pop(bool allowSensitive)
    {
        if (!IsAvailable()) return false;
        if (IsBusy()) return false;
        Plugin.Instance.PopQueue.Start(allowSensitive);
        return !Plugin.Instance.PopQueue.IsIdle;
    }

    private static bool Stop()
    {
        var changed = false;
        if (!Plugin.Instance.PopQueue.IsIdle) { Plugin.Instance.PopQueue.Abort("IPC Stop"); changed = true; }
        if (Plugin.Instance.PopQueue.IsArmed) { Plugin.Instance.PopQueue.Disarm("IPC Stop"); changed = true; }
        if (!Plugin.Instance.ClaimQueue.IsIdle) { Plugin.Instance.ClaimQueue.Abort("IPC Stop"); changed = true; }
        if (!Plugin.Instance.ReadAll.IsIdle) { Plugin.Instance.ReadAll.Abort("IPC Stop"); changed = true; }
        return changed;
    }

    private static bool CanAcceptMailboxAction()
    {
        if (!IsAvailable()) return false;
        if (IsBusy()) return false;
        if (!IsMailboxOpen()) return false;
        return true;
    }

    private static System.Collections.Generic.List<int> CollectTakeCandidates()
    {
        var mailbox = Plugin.Instance.Mailbox;
        var includeGM = Plugin.Config.IncludeGMInSweeps;
        var count = (int)mailbox.LoadedLetterCount;
        var result = new System.Collections.Generic.List<int>();
        for (var i = 0; i < count; i++)
        {
            if (!includeGM && mailbox.IsGMLetter(i)) continue;
            if (mailbox.LetterHasUnclaimedAttachments(i)) result.Add(i);
        }
        return result;
    }
}
