using Avalonia.Threading;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Editing;

internal sealed partial class PagedTextView
{
    private readonly bool observeColumnIndex = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE"));
    private int columnIndexWork;
    private bool columnIndexFailed;

    internal readonly record struct ReadinessViewport(long FirstLine, int VisibleLines, double Horizontal,
        double TextWidth, double Height);

    internal readonly record struct Readiness(bool SameDocument, bool CurrentCache, bool Modified, bool Detour,
        int VisibleRows, int CachedRows, int LongRows, int RequiredColumnIndexes, int CompleteColumnIndexes,
        int WindowRows, int CachedWindows, bool ColumnTracking, int ColumnWork, bool ColumnFailed,
        bool WindowsPending, bool CaretMovePending, bool IndexTimerPending)
    {
        public bool Ready => SameDocument && CurrentCache && !Modified && !Detour && VisibleRows > 0
            && CachedRows == VisibleRows && CompleteColumnIndexes == RequiredColumnIndexes
            && ColumnTracking && ColumnWork == 0 && !ColumnFailed
            && !WindowsPending && !CaretMovePending && !IndexTimerPending;
    }

    internal ReadinessViewport CaptureReadinessViewport()
        => new(TopLine, _canvas.VisibleLines, _hScroll, _canvas.TextWidth, _canvas.Bounds.Height);

    internal Readiness CaptureReadiness(Document document, PagedBackend backend)
    {
        Dispatcher.UIThread.VerifyAccess();
        long first = TopLine;
        long end = Math.Min(document.LineIndexState.Lines, first + Math.Max(1L, (long)_canvas.VisibleLines + 1));
        int visible = 0, cached = 0, longRows = 0, required = 0, complete = 0, windowRows = 0, cachedWindows = 0;
        double cell = _canvas.CharWidth;
        bool current = _linesRevision == document.Revision && cell > 0 && double.IsFinite(cell);
        bool currentWindows = true;
        for (long line = first; line < end; line++)
        {
            visible++;
            if (!current || !_lines.TryGetValue(line, out var view)) continue;
            cached++;
            if (!view.Truncated) continue;
            longRows++;
            bool window = _hScroll / cell >= view.Display.Length;
            if (window)
            {
                windowRows++;
                long column = (long)Math.Floor(_hScroll / cell);
                if (_windows.TryGetValue(view.Start, out var known) && known.Revision == document.Revision
                    && known.Column == column && known.Columns == _canvas.WindowColumns) cachedWindows++;
                else currentWindows = false;
            }
            // A decoded prefix starts no column job. Do not create one just to establish readiness.
            if (window || _indexing.Contains(view.Start))
            {
                required++;
                if (backend.Paged.Source.TryGetLineColumns(view.Start, out _)) complete++;
            }
        }
        return new(ReferenceEquals(Doc, document) && ReferenceEquals(document.Backend, backend), current && currentWindows,
            backend.Paged.IsModified, _detour is not null, visible, cached, longRows, required, complete, windowRows,
            cachedWindows, observeColumnIndex, columnIndexWork,
            columnIndexFailed, WindowsPending, CaretMovePending, _indexTimer.IsEnabled);
    }

    // EOF can be published before the worker returns or its posted invalidation runs.
    internal sealed class ColumnIndexWork
    {
        private readonly PagedTextView owner;
        private bool taskCompleted, callbackCompleted, released;
        private bool callbackExpected;

        internal ColumnIndexWork(PagedTextView owner)
        {
            Dispatcher.UIThread.VerifyAccess();
            this.owner = owner;
            owner.columnIndexWork++;
        }

        internal void PostCallback()
        {
            callbackExpected = true;
            Dispatcher.UIThread.Post(() =>
            {
                try { owner._canvas.InvalidateVisual(); }
                finally { callbackCompleted = true; Release(); }
            });
        }

        internal void Observe(Task task)
        {
            _ = task.ContinueWith(completed =>
            {
                bool failed = completed.IsFaulted;
                _ = completed.Exception;
                Dispatcher.UIThread.Post(() =>
                {
                    taskCompleted = true;
                    owner.columnIndexFailed |= failed;
                    Release();
                });
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void Release()
        {
            if (released || !taskCompleted || callbackExpected && !callbackCompleted) return;
            released = true;
            owner.columnIndexWork--;
        }
    }

    /// <summary>Indexes the original long line on the thread pool; it does not start for an edited line or a detour cut.</summary>
    private void BuildColumnIndexInBackground(long lineStart)
    {
        if (Paged.IsModified || _detour is { } d && d.Cuts.Contains(lineStart) || !_indexing.Add(lineStart)) return;
        var source = Paged.Source;
        var token = (_indexCts ??= new()).Token;
        var observation = observeColumnIndex ? new ColumnIndexWork(this) : null;
        Task task = Task.Run(() =>
        {
            try { source.ReadColumns(lineStart, long.MaxValue, 0, token); }
            catch (OperationCanceledException) { return; }
            if (observation is not null) observation.PostCallback();
            else Dispatcher.UIThread.Post(_canvas.InvalidateVisual);
        });
        observation?.Observe(task);
    }
}
