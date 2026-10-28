using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.History;

/// <summary>
/// Feeds a <see cref="FileHistory"/> from a document: the state on disk when the file is attached, every save, and a change made by another program
/// (taken at the moment it is detected, before a later save can overwrite it). The files are read from disk on a worker, one at a time; this holds for a
/// document of any size, since a large file is never copied into memory (the history keeps it as a patch when it can).
/// </summary>
public sealed class HistoryRecorder : IDisposable
{
    private readonly FileHistory _history;
    private readonly Document _doc;
    private Task _queue = Task.CompletedTask;

    /// <summary>Raised on the worker when a revision could not be stored.</summary>
    public event Action<string, HistoryRecordResult>? Failed;

    public HistoryRecorder(FileHistory history, Document doc, Action<string, HistoryRecordResult>? onFailed = null)
    {
        _history = history;
        _doc = doc;
        Failed = onFailed;
        doc.Saved += OnSaved;
        doc.ExternalChangeDetected += OnExternal;
        if (doc.FilePath is not { } path || !File.Exists(path)) return;
        // a file large enough to be kept as patches is copied whole once, which a document that is only read does not need: the state it was opened in is taken at the first edit,
        // when the file on disk is still that state
        if (history.Policy.PatchAboveBytes is { } above && new FileInfo(path).Length > above) doc.Changed += OnFirstChange;
        else Enqueue(path, HistorySource.Initial, onlyIfEmpty: true);
    }

    /// <summary>Completes when everything queued so far is stored.</summary>
    public Task Pending => _queue;

    private void OnFirstChange(object? sender, DocumentChangedEventArgs e)
    {
        _doc.Changed -= OnFirstChange;
        if (_doc.FilePath is { } path && File.Exists(path)) Enqueue(path, HistorySource.Initial, onlyIfEmpty: true);
    }

    private void OnSaved(object? sender, EventArgs e)
    {
        if (_doc.FilePath is { } path) Enqueue(path, HistorySource.Save, false);
    }

    private void OnExternal(object? sender, ExternalChange change)
    {
        if (change.Kind == ExternalChangeKind.Modified) Enqueue(change.Path, HistorySource.External, false);
    }

    private readonly HashSet<(string Path, HistorySource Source)> _waiting = [];

    private void Enqueue(string path, HistorySource source, bool onlyIfEmpty)
    {
        lock (this)
        {
            // a record of the same kind that has not started yet reads the file when it does, so it sees the state this change made: a large file that changes
            // all the time (a log) must not queue a read of the whole file for each change
            if (!_waiting.Add((path, source))) return;
            _queue = _queue.ContinueWith(_ =>
            {
                lock (this) _waiting.Remove((path, source));
                try
                {
                    if (onlyIfEmpty && _history.List(path).Count > 0) return;
                    var result = _history.RecordFile(path, source);
                    if (result.Status is HistoryStatus.Failed or HistoryStatus.TooLarge) Failed?.Invoke(path, result);
                }
                catch (HistoryFormatException e)
                {
                    Failed?.Invoke(path, new HistoryRecordResult(HistoryStatus.Failed, null, e.Message));
                }
            }, TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        _doc.Changed -= OnFirstChange;
        _doc.Saved -= OnSaved;
        _doc.ExternalChangeDetected -= OnExternal;
    }
}
