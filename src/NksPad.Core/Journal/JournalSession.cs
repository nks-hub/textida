using NksPad.Core.Documents;

namespace NksPad.Core.Journal;

/// <summary>
/// The journal of one running editor: records every change of the tracked documents and the session state, adopts the work
/// of crashed earlier runs at start, and compacts itself. The journal lives in its own directory and never writes a user file.
/// A disabled session (no usable directory) tracks nothing and promises nothing: <see cref="IsActive"/> is false.
/// </summary>
public sealed class JournalSession : IDisposable
{
    private const int SeedChunk = 1 << 20;
    private readonly object _gate = new();
    private readonly string? _dir;
    private readonly string? _id;
    private readonly JournalOptions _options;
    private readonly Dictionary<Document, DocTracker> _trackers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Guid, DocTracker> _byId = [];
    private readonly Timer? _compactTimer;
    private FileStream? _lock;
    private JournalWriter? _writer;
    private long _lengthAfterCompaction;
    private Exception? _broken;
    private bool _disposed;

    private JournalSession(string? dir, JournalOptions options)
    {
        _dir = dir;
        _options = options;
        Recovery = new RecoveryResult();
        if (dir is null) return;
        _id = Guid.NewGuid().ToString("N");
        _lock = OpenLock(LockPathOf(JournalPathOf(dir, _id)))!;
        _writer = new JournalWriter(JournalPathOf(dir, _id), options);
        _compactTimer = new Timer(_ => MaybeCompact(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    /// <summary>What earlier runs left behind, adopted into this session. Documents with a rebuilt <see cref="Document"/> are already tracked.</summary>
    public RecoveryResult Recovery { get; private set; }
    public bool IsActive => _writer is { Fault: null } && _broken is null;
    public string? JournalPath => _writer?.Path;
    public Exception? Fault => _broken ?? _writer?.Fault;

    private static string JournalPathOf(string dir, string id) => Path.Combine(dir, $"session-{id}.njl");
    private static string LockPathOf(string journal) => Path.ChangeExtension(journal, ".lock");

    private static FileStream? OpenLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts a journal in <paramref name="directory"/> (null disables journaling). Journals of runs that are no longer alive are
    /// recovered, their work is copied into the new journal and only then are they deleted, so a crash during start loses nothing.
    /// </summary>
    public static JournalSession Start(string? directory, Func<string, IBackendFactory?> backends, JournalOptions? options = null)
    {
        options ??= new();
        if (directory is null) return new JournalSession(null, options);
        Directory.CreateDirectory(directory);
        foreach (var stale in Directory.GetFiles(directory, "session-*.njl.tmp")) TryDelete(stale);

        var session = new JournalSession(directory, options);
        var combined = new RecoveryResult();
        var seen = new HashSet<Guid>();
        var done = new List<(string Journal, FileStream Lock)>();
        var orphans = Directory.GetFiles(directory, "session-*.njl")
            .Where(p => !string.Equals(p, session.JournalPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc);
        foreach (var path in orphans)
        {
            var lockStream = OpenLock(LockPathOf(path));
            if (lockStream is null) continue;
            var result = JournalRecovery.Recover(path, backends);
            combined.Notes.AddRange(result.Notes.Select(n => $"{Path.GetFileName(path)}: {n}"));
            if (result.Unreadable)
            {
                lockStream.Dispose();
                continue;
            }
            combined.Records += result.Records;
            if (result.LastRecord is { } t && (combined.LastRecord is null || t > combined.LastRecord)) combined.LastRecord = t;
            foreach (var doc in result.Documents)
            {
                if (!seen.Add(doc.DocId))
                {
                    doc.Document?.Dispose();
                    continue;
                }
                if (doc.Status == RecoveryStatus.Missing && doc.PendingRecords == 0) continue;
                combined.Documents.Add(doc);
                foreach (var r in doc.Records) session._writer!.Append(r.Type, r.Payload, r.TimestampMs);
            }
            if (combined.Session is null && result.Session is not null)
            {
                combined.Session = result.Session;
                session._writer!.Append(result.SessionRecord!.Value.Type, result.SessionRecord.Value.Payload, result.SessionRecord.Value.TimestampMs);
            }
            done.Add((path, lockStream));
        }

        session._writer!.Flush();
        bool adopted = session._writer.Fault is null;
        if (!adopted) combined.Notes.Add("The new journal could not be written; the old journals were kept.");
        foreach (var (path, lockStream) in done)
        {
            if (adopted) TryDelete(path);
            lockStream.Dispose();
        }

        foreach (var doc in combined.Documents.Where(d => d.Document is not null)) session.Attach(doc.Document!, doc.DocId, doc.BaseRevision);
        session.Recovery = combined;
        session._lengthAfterCompaction = session._writer.Length;
        return session;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ---- tracking ----

    /// <summary>
    /// Starts journaling a document that has no changes yet. A named document is recorded as file path plus fingerprint only
    /// (nothing of its content is copied); an unnamed one as its initial bytes. Returns the id the journal knows it by.
    /// </summary>
    public Guid Track(Document doc)
    {
        if (doc.Revision != 0 || (doc.FilePath is not null && doc.IsModified)) throw new ArgumentException("Only an unchanged document can be tracked.", nameof(doc));
        var id = Guid.NewGuid();
        if (_writer is null) return id;
        lock (_gate)
        {
            if (_disposed) return id;
            var named = doc.FilePath is not null && doc.Fingerprint is not null;
            Append(RecordType.DocOpen, JournalRecords.Encode(new OpenRecord(id, named ? doc.FilePath : null, named ? doc.Fingerprint : null,
                DocFormat.Of(doc.Format), doc.Capabilities.Name, 0, named ? 0 : doc.ByteLength)));
            if (!named)
            {
                var buf = new byte[(int)Math.Min(SeedChunk, Math.Max(1, doc.ByteLength))];
                for (long at = 0; at < doc.ByteLength;)
                {
                    int n = doc.Read(at, buf);
                    Append(RecordType.DocSeed, JournalRecords.Encode(new SeedRecord(id, at, buf[..n])));
                    at += n;
                }
            }
        }
        Attach(doc, id, 0);
        return id;
    }

    private void Attach(Document doc, Guid id, long baseRevision)
    {
        var tracker = new DocTracker(this, doc, id, baseRevision);
        lock (_gate)
        {
            _trackers[doc] = tracker;
            _byId[id] = tracker;
        }
        tracker.Start();
    }

    /// <summary>The id of a tracked document in the journal and the session state.</summary>
    public Guid? IdOf(Document doc)
    {
        lock (_gate) return _trackers.TryGetValue(doc, out var t) ? t.Id : null;
    }

    /// <summary>Records that the document was closed; its work is no longer recovered.</summary>
    public void Untrack(Document doc)
    {
        DocTracker? tracker;
        lock (_gate)
        {
            if (!_trackers.Remove(doc, out tracker)) return;
            _byId.Remove(tracker.Id);
        }
        tracker.Stop();
        lock (_gate) Append(RecordType.DocClose, JournalRecords.EncodeClose(tracker.Id));
    }

    /// <summary>Stores the tab layout. Revisions in the model are those of the live documents.</summary>
    public void UpdateSession(SessionModel model)
    {
        lock (_gate)
        {
            var mapped = new SessionModel([.. model.Groups.Select(g => g with
            {
                Tabs = [.. g.Tabs.Select(t => _byId.TryGetValue(t.DocId, out var tr) ? t with { Revision = t.Revision + tr.BaseRevision } : t)],
            })], model.ActiveGroup);
            Append(RecordType.SessionState, JournalRecords.Encode(mapped));
        }
    }

    internal void Append(RecordType type, byte[] payload)
    {
        lock (_gate)
        {
            if (_disposed || _broken is not null) return;
            _writer?.Append(type, payload);
        }
    }

    /// <summary>A change could not be recorded; the journal no longer matches the document, so it stops promising recovery.</summary>
    private void Break(Exception e)
    {
        lock (_gate) _broken ??= e;
    }

    // ---- durability ----

    /// <summary>Writes and fsyncs everything recorded so far.</summary>
    public void Flush()
    {
        lock (_gate) _writer?.Flush();
    }

    private void MaybeCompact()
    {
        var w = _writer;
        if (w is null || w.Fault is not null) return;
        long len = w.Length;
        if (len > _options.CompactThresholdBytes && len > 2 * _lengthAfterCompaction) Compact();
    }

    /// <summary>Rewrites the journal without closed documents and without history older than each document's last save.</summary>
    public void Compact()
    {
        lock (_gate)
        {
            if (_writer is not { Fault: null } old || _disposed || _dir is null) return;
            old.Flush();
            var records = new List<JournalRecord>();
            using (var reader = new JournalReader(old.Path))
                while (reader.TryRead(out var r)) records.Add(r);
            var plan = JournalCompactor.Plan(records);

            string path = old.Path, tmp = path + ".tmp";
            var next = new JournalWriter(tmp, _options);
            foreach (var doc in plan.Docs)
                foreach (var r in doc.All) next.Append(r.Type, r.Payload, r.TimestampMs);
            if (plan.Session is { } s) next.Append(s.Type, s.Payload, s.TimestampMs);
            next.Flush();
            if (next.Fault is not null)
            {
                next.Dispose();
                TryDelete(tmp);
                return;
            }
            old.Dispose();
            File.Move(tmp, path, overwrite: true);
            next.Path = path;
            _writer = next;
            _lengthAfterCompaction = next.Length;
        }
    }

    public void Dispose()
    {
        DocTracker[] trackers;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            trackers = [.. _trackers.Values];
            _trackers.Clear();
            _byId.Clear();
        }
        _compactTimer?.Dispose();
        foreach (var t in trackers) t.Stop();
        _writer?.Dispose();
        _lock?.Dispose();
    }

    /// <summary>Disposes the session and deletes its journal; used when nothing is left to recover.</summary>
    public void Discard()
    {
        var path = JournalPath;
        Dispose();
        if (path is not null) TryDelete(path);
    }

    /// <summary>Follows the changes of one document and turns them into records.</summary>
    private sealed class DocTracker(JournalSession owner, Document doc, Guid id, long baseRevision)
    {
        private int _undo = doc.UndoDepth, _redo = doc.RedoDepth;
        private int _floor;
        private bool _diverged, _decision;
        private readonly List<bool> _redoOk = [.. Enumerable.Repeat(true, doc.RedoDepth)];

        public Guid Id => id;
        public long BaseRevision => baseRevision;

        public void Start()
        {
            doc.Changed += OnChanged;
            doc.Saving += OnSaving;
            doc.Saved += OnSaved;
        }

        public void Stop()
        {
            doc.Changed -= OnChanged;
            doc.Saving -= OnSaving;
            doc.Saved -= OnSaved;
        }

        private void OnChanged(object? sender, DocumentChangedEventArgs e)
        {
            try
            {
                var c = e.Change;
                int undoNow = doc.UndoDepth, redoNow = doc.RedoDepth;
                long revision = c.Revision + baseRevision;
                switch (c.Kind)
                {
                    case ChangeKind.Edit or ChangeKind.Backend:
                        _redoOk.Clear();
                        owner.Append(RecordType.DocEdit, JournalRecords.Encode(Edit(c, c.Kind, undoNow > _undo)));
                        break;
                    case ChangeKind.Undo:
                        if (undoNow < _undo)
                        {
                            _decision = !_diverged && undoNow >= _floor;
                            _redoOk.Add(_decision);
                            _diverged |= !_decision;
                        }
                        Replayable(RecordType.DocUndo, c, revision);
                        break;
                    case ChangeKind.Redo:
                        if (redoNow < _redo)
                        {
                            bool listed = _redoOk.Count > 0 && _redoOk[^1];
                            if (_redoOk.Count > 0) _redoOk.RemoveAt(_redoOk.Count - 1);
                            _decision = !_diverged && listed;
                            _diverged |= !_decision;
                        }
                        Replayable(RecordType.DocRedo, c, revision);
                        break;
                }
                _undo = undoNow;
                _redo = redoNow;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OverflowException or OutOfMemoryException or NotSupportedException)
            {
                owner.Break(ex);
            }
        }

        private void Replayable(RecordType type, ChangeSet c, long revision)
        {
            if (_decision) owner.Append(type, JournalRecords.Encode(new UndoRedoRecord(id, revision)));
            else owner.Append(RecordType.DocEdit, JournalRecords.Encode(Edit(c, ChangeKind.Edit, true)));
        }

        private EditRecord Edit(ChangeSet c, ChangeKind kind, bool isNew)
        {
            var edits = new ChangeEdit[c.Edits.Count];
            long shift = 0;
            for (int i = 0; i < edits.Length; i++)
            {
                var s = c.Edits[i];
                var inserted = new byte[checked((int)s.Inserted)];
                if (inserted.Length > 0) doc.Read(s.Start + shift, inserted);
                shift += s.Inserted - s.Deleted;
                edits[i] = new(s.Start, s.Deleted, inserted);
            }
            return new EditRecord(id, c.Revision + baseRevision, kind, c.Source, isNew, edits);
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            owner.Append(RecordType.DocSaveIntent, JournalRecords.Encode(new SaveIntentRecord(id, doc.Revision + baseRevision, e.Target, e.Size, e.Sha256)));
            owner.Flush();
        }

        private void OnSaved(object? sender, EventArgs e)
        {
            if (doc.FilePath is not { } path || doc.Fingerprint is not { } print) return;
            owner.Append(RecordType.DocSave, JournalRecords.Encode(new SaveRecord(id, doc.Revision + baseRevision, path, print)));
            _floor = doc.UndoDepth;
            _diverged = false;
            _redoOk.Clear();
            _redoOk.AddRange(Enumerable.Repeat(false, doc.RedoDepth));
        }
    }
}
