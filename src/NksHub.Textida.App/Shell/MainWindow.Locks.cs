using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Files;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// File locks (P5.4.5): a file that another program holds with a sharing lock, or that another window of the editor holds, opens read only with a bar that says so.
/// The bar offers a retry and the explicit concurrent-write mode, in which every save still checks the file on disk first and asks when it changed.
/// </summary>
internal sealed partial class MainWindow
{
    private void AcquireLock(OpenDocument od, bool concurrentWrite = false)
    {
        od.Lock?.Dispose();
        od.Lock = null;
        if (od.IsResults || od.Path is not { } path || !File.Exists(path)) return;
        try { od.Lock = _app.Locks.Acquire(path, concurrentWrite); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the lock folder cannot be written: the file opens as before, and saving still checks the disk */ }
    }

    /// <summary>Makes the views of the document read only or writable by its lock, and shows the bar that explains it.</summary>
    private void ApplyLock(OpenDocument od)
    {
        foreach (var tab in od.Views)
        {
            if (tab.Surface is { } surface) surface.IsReadOnly = od.ReadOnly;
            if (tab.Hex is { } hex) hex.ReadOnly = od.ReadOnly; // typing, delete, paste and undo are refused with a message
            if (tab.Paged is { } paged) paged.ReadOnlyReason = od.IsVirtual && od.ReadOnly ? Loc.Get(K.VirtualReadOnly) : od.LockedReadOnly ? Loc.Get(K.LockSaveRefused) : od.UserReadOnly ? Loc.Get(K.SpecialSaveRefused) : null;
        }
        if (od.Lock is not { State: not LockState.Writable } lck) return;
        string title = od.Title;
        string message = lck.State switch
        {
            LockState.ReadOnlyLockedByProcess => Loc.Get(K.LockSystem, ("title", title)),
            LockState.ReadOnlyOtherInstance => lck.Owner is { } owner
                ? Loc.Get(K.LockInstanceOwner, ("title", title), ("pid", owner.ProcessId))
                : Loc.Get(K.LockInstance, ("title", title)),
            _ => Loc.Get(K.LockConcurrent, ("title", title)),
        };
        IReadOnlyList<(string, Action)> actions = lck.IsReadOnly
            ? [(Loc.Get(K.LockTryAgain), () => RetryLock(od)), (Loc.Get(K.LockEditAnyway), () => ChooseConcurrentWrite(od))]
            : [];
        foreach (var tab in od.Views.ToList()) tab.ShowBar(new InfoBar(message, actions, warning: true, onClose: () => DismissBars(od), helpTopic: "file.locks"));
    }

    private void RetryLock(OpenDocument od)
    {
        AcquireLock(od);
        ApplyLock(od);
        if (!od.LockedReadOnly)
        {
            DismissBars(od);
            SetStatusMessage(Loc.Get(K.LockFree, ("title", od.Title)));
        }
        else SetStatusMessage(Loc.Get(K.LockStill, ("title", od.Title)));
    }

    /// <summary>The user chose to write although the file is locked: no lock is honoured, and every save compares the file on disk with what was opened.</summary>
    internal void ChooseConcurrentWrite(OpenDocument od)
    {
        AcquireLock(od, concurrentWrite: true);
        ApplyLock(od);
        SetStatusMessage(Loc.Get(K.LockConcurrentOn, ("title", od.Title)));
    }
}
