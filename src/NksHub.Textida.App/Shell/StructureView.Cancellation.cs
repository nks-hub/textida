using System.Diagnostics;
using Avalonia.Threading;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureView
{
    private bool observeCancellation;
    private CancellationCapture? cancellationCapture;
    private List<(Session Owner, Task Worker)>? cancellationCleanups;

    internal readonly record struct CancellationObservation(Guid Nonce, Guid DocumentId, long Revision, long Generation,
        long RequestedQpc, long WorkerStoppedQpc, bool WorkerWasActive, bool WorkerCompleted, bool WorkerSucceeded,
        bool WorkerOnDispatcher, int RequestedThreadId, int WorkerThreadId);

    private sealed class CancellationCapture(Session owner, Task work)
    {
        private readonly object gate = new();
        private readonly Guid nonce = Guid.NewGuid();
        private Task finalWork = work;
        private long tailVersion;
        private long requestedQpc, workerStoppedQpc;
        private bool workerWasActive, workerCompleted, workerSucceeded, workerOnDispatcher;
        private int requestedThreadId, workerThreadId;
        public Session Owner { get; } = owner;
        private readonly record struct Completion(CancellationCapture Capture, long Version);

        public void Requested()
        {
            requestedThreadId = Environment.CurrentManagedThreadId;
            requestedQpc = Stopwatch.GetTimestamp();
            workerWasActive = !finalWork.IsCompleted;
        }

        public void ObserveCompletion()
        {
            // Queue and cleanup work must finish first; scheduler delay only increases this bound.
            Task observedWork;
            long version;
            lock (gate) { observedWork = finalWork; version = tailVersion; }
            _ = observedWork.ContinueWith(static (finished, state) =>
            {
                long stopped = Stopwatch.GetTimestamp();
                var completion = (Completion)state!;
                var capture = completion.Capture;
                lock (capture.gate)
                {
                    if (completion.Version != capture.tailVersion || !ReferenceEquals(finished, capture.finalWork)
                        || capture.workerCompleted) return;
                    capture.workerStoppedQpc = stopped;
                    capture.workerCompleted = finished.IsCompleted;
                    capture.workerSucceeded = finished.Status == TaskStatus.RanToCompletion;
                    capture.workerOnDispatcher = Dispatcher.UIThread.CheckAccess();
                    capture.workerThreadId = Environment.CurrentManagedThreadId;
                }
            }, new Completion(this, version), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        public void Extend(Task next)
        {
            lock (gate)
            {
                // A completed cleanup can yield the same Task again; version every extension.
                tailVersion++;
                finalWork = next;
                workerStoppedQpc = 0; workerCompleted = workerSucceeded = false;
                workerOnDispatcher = false; workerThreadId = 0;
            }
            ObserveCompletion();
        }

        public CancellationObservation Snapshot()
        {
            lock (gate)
                return new(nonce, Owner.Source.DocumentId, Owner.Source.Revision, Owner.Generation, requestedQpc,
                    workerStoppedQpc, workerWasActive, workerCompleted, workerSucceeded, workerOnDispatcher,
                    requestedThreadId, workerThreadId);
        }
    }

    internal void EnableCancellationObservation()
    {
        observeCancellation = true;
        cancellationCleanups ??= [];
    }

    private void ObserveCancellationCleanup(Session owner, Task worker)
    {
        if (!observeCancellation || !ReferenceEquals(session, owner)) return;
        cancellationCleanups!.RemoveAll(entry => entry.Worker.IsCompleted || !ReferenceEquals(entry.Owner, owner));
        cancellationCleanups.Add((owner, worker));
        if (cancellationCapture is { } capture && ReferenceEquals(capture.Owner, owner))
            capture.Extend(CancellationTail(owner));
    }

    private Task CancellationTail(Session owner)
    {
        var cleanups = cancellationCleanups!.Where(entry => ReferenceEquals(entry.Owner, owner) && !entry.Worker.IsCompleted)
            .Select(entry => entry.Worker).ToArray();
        return cleanups.Length == 0 ? work : Task.WhenAll([work, .. cleanups]);
    }
    internal long? SourceGeneration => session?.Generation;
    internal Avalonia.Controls.Button CancelButton => cancel;
    internal CancellationObservation? ObservedCancellation
    {
        get
        {
            if (cancellationCapture is not { } capture || disposed || !ReferenceEquals(session, capture.Owner)
                || capture.Owner.Generation != generation || source() is not { } latest || !SameSource(latest, capture.Owner.Source)) return null;
            return capture.Snapshot();
        }
    }

    private void CancelWork()
    {
        if (session is { } owner)
        {
            CancellationCapture? capture = observeCancellation ? new(owner, CancellationTail(owner)) : null;
            cancellationCapture = capture;
            capture?.Requested();
            owner.Cancellation.Cancel();
            capture?.ObserveCompletion();
        }
        SetStatus(K.StructureCancelled);
        UpdateButtons();
    }
}
