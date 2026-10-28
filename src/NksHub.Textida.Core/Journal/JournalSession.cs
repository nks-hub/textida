using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Journal;

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
    private Func<string, IBackendFactory?>? _backends;
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
    /// With <paramref name="recovery"/> a document whose changes are large is not replayed but reported as <see cref="RecoveryStatus.Pending"/>;
    /// <see cref="RecoverPending"/> replays it, <see cref="DiscardPending"/> throws it away.
    /// </summary>
    public static JournalSession Start(string? directory, Func<string, IBackendFactory?> backends, JournalOptions? options = null, RecoveryOptions? recovery = null)
    {
        options ??= new();
        if (directory is null) return new JournalSession(null, options);
        Directory.CreateDirectory(directory);
        foreach (var stale in Directory.GetFiles(directory, "session-*.njl.tmp")) TryDelete(stale);

        var session = new JournalSession(directory, options) { _backends = backends };
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
            var result = JournalRecovery.Recover(path, backends, recovery);
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

        JournalPayloads.Sweep(directory, session._id!, combined.Documents.Select(d => d.DocId).ToHashSet());
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

    // ---- changes that wait for a decision ----

    /// <summary>The documents of <see cref="Recovery"/> whose changes wait for <see cref="RecoverPending"/> or <see cref="DiscardPending"/>; a copy, safe to read while another thread settles one.</summary>
    public IReadOnlyList<RecoveredDocument> Pending
    {
        get
        {
            lock (_gate) return [.. Recovery.Documents.Where(d => d.Status == RecoveryStatus.Pending)];
        }
    }

    /// <summary>
    /// Replays the changes of a document that start left as <see cref="RecoveryStatus.Pending"/> and starts tracking the rebuilt document. It can take
    /// minutes for gigabytes of changes, so call it off the UI thread. The result replaces the entry in <see cref="Recovery"/>; when the replay fails
    /// (<see cref="RecoveryStatus.Failed"/>, <see cref="RecoveryStatus.Conflict"/>, <see cref="RecoveryStatus.Missing"/>) nothing is applied and the records stay in the journal.
    /// </summary>
    public RecoveredDocument RecoverPending(Guid docId)
    {
        if (_dir is null || _backends is not { } backends) throw new InvalidOperationException("This journal has nothing that waits.");
        RecoveredDocument pending;
        lock (_gate) pending = Recovery.Documents.Find(d => d.DocId == docId && d.Status == RecoveryStatus.Pending) ?? throw new InvalidOperationException("No change of this document waits for a decision.");
        var log = new JournalCompactor.DocLog { Open = pending.Records[0], Rest = pending.Records.GetRange(1, pending.Records.Count - 1) };
        var rebuilt = JournalRecovery.ReplayDocument(log, backends, JournalPayloads.DirectoryOf(_dir));
        lock (_gate)
        {
            int at = Recovery.Documents.IndexOf(pending);
            if (at < 0)
            {
                rebuilt.Document?.Dispose(); // discarded while it was replayed
                return rebuilt;
            }
            Recovery.Documents[at] = rebuilt;
        }
        if (rebuilt.Document is not null) Attach(rebuilt.Document, rebuilt.DocId, rebuilt.BaseRevision);
        return rebuilt;
    }

    /// <summary>
    /// Throws away the changes of a document that start could not or would not replay (pending, failed, in conflict or missing): the close is made durable
    /// first and then the payload files of the changes go. The file of the document is not touched. False when no such document is listed.
    /// </summary>
    public bool DiscardPending(Guid docId)
    {
        lock (_gate)
        {
            int at = Recovery.Documents.FindIndex(d => d.DocId == docId && d.Document is null);
            if (at < 0) return false;
            Recovery.Documents.RemoveAt(at);
            Append(RecordType.DocClose, JournalRecords.EncodeClose(docId));
        }
        if (_dir is null) return true;
        Flush();
        JournalPayloads.DeleteAllOf(_dir, docId);
        return true;
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
                doc.Codec.Name, doc.Capabilities.Name, 0, named ? 0 : doc.ByteLength)));
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
        if (_dir is not null) doc.Backend.UseDurableSpill(JournalPayloads.DirectoryOf(_dir), JournalPayloads.Prefix(_id!, id));
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
        Append(RecordType.DocClose, JournalRecords.EncodeClose(tracker.Id), tracker.Id);
        if (_dir is null) return;
        Flush(); // the close is durable before the data of its large changes goes
        tracker.CloseBlob();
        JournalPayloads.DeleteAllOf(_dir, tracker.Id);
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

    /// <summary>A record of a document: it waits behind an earlier record of the same document that waits for its payload, and never behind one of another document.</summary>
    internal void Append(RecordType type, byte[] payload, Guid document)
    {
        lock (_gate)
        {
            if (_disposed || _broken is not null) return;
            _writer?.AppendKeyed(type, payload, document);
        }
    }

    /// <summary>A record of a document whose payload (<paramref name="build"/>) is made when <paramref name="gate"/> has ended; see <see cref="JournalWriter.AppendDeferred"/>.</summary>
    internal void AppendDeferred(RecordType type, Func<byte[]> build, Task gate, Guid document)
    {
        lock (_gate)
        {
            if (_disposed || _broken is not null) return;
            _writer?.AppendDeferred(type, build, gate, document);
        }
    }

    /// <summary>A change could not be recorded; the journal no longer matches the document, so it stops promising recovery.</summary>
    private void Break(Exception e)
    {
        lock (_gate) _broken ??= e;
    }

    // ---- durability ----

    /// <summary>
    /// Writes and fsyncs everything recorded so far, and waits for the payloads that records wait for. The gate of the session is not held meanwhile:
    /// the editing goes on and its records queue up behind the flush.
    /// </summary>
    public void Flush()
    {
        JournalWriter? writer;
        lock (_gate) writer = _writer;
        writer?.Flush();
    }

    private void MaybeCompact()
    {
        var w = _writer;
        if (w is null || w.Fault is not null || w.HasWaiting) return; // a compaction would wait for the payloads, and hold back the editing meanwhile
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
            JournalPayloads.Sweep(_dir, _id!, plan.Docs.Select(d => JournalRecords.DocOf(d.Open)!.Value).ToHashSet());
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
        foreach (var t in trackers) t.CloseBlob();
        _lock?.Dispose();
    }

    /// <summary>Disposes the session and deletes its journal; used when nothing is left to recover.</summary>
    public void Discard()
    {
        var path = JournalPath;
        Dispose();
        if (path is not null) TryDelete(path);
        if (_dir is not null && _id is not null) JournalPayloads.Sweep(_dir, _id, new HashSet<Guid>());
    }

    /// <summary>Follows the changes of one document and turns them into records.</summary>
    private sealed class DocTracker(JournalSession owner, Document doc, Guid id, long baseRevision)
    {
        private int _undo = doc.UndoDepth, _redo = doc.RedoDepth;
        private int _floor;
        private bool _diverged, _decision;
        private readonly List<bool> _redoOk = [.. Enumerable.Repeat(true, doc.RedoDepth)];
        private readonly List<(long Revision, string File)> _refs = [];
        private PayloadBlob? _blob;
        private int _blobCount;
        /// <summary>Inserted bytes written into records of this document since its last save.</summary>
        private long _inlineBytes;

        public Guid Id => id;
        public long BaseRevision => baseRevision;

        public void Start()
        {
            doc.Changed += OnChanged;
            doc.Saving += OnSaving;
            doc.Saved += OnSaved;
            doc.Moved += OnMoved;
        }

        public void Stop()
        {
            doc.Changed -= OnChanged;
            doc.Saving -= OnSaving;
            doc.Saved -= OnSaved;
            doc.Moved -= OnMoved;
        }

        private void Append(RecordType type, byte[] payload) => owner.Append(type, payload, id);

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
                        AppendEdit(c, c.Kind, undoNow > _undo, e);
                        break;
                    case ChangeKind.View:
                        Append(RecordType.DocCodec, JournalRecords.Encode(new CodecRecord(id, revision, doc.Codec.Name)));
                        break;
                    case ChangeKind.Undo:
                        if (undoNow < _undo)
                        {
                            _decision = !_diverged && undoNow >= _floor && !doc.EditsDuringSave; // a replay starts from the saved file, whose history this undo may reach below
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
                            _decision = !_diverged && listed && !doc.EditsDuringSave;
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
            if (_decision) Append(type, JournalRecords.Encode(new UndoRedoRecord(id, revision)));
            else AppendEdit(c, ChangeKind.Edit, true);
        }

        /// <summary>The blocks and the edit list of one record that are still being made durable and checked; the record is written when they are.</summary>
        private sealed class Waiting
        {
            public readonly List<(int Index, Block Block)> Blocks = [];
            public Task? List;
            public bool Any => Blocks.Count > 0 || List is not null;
        }

        /// <summary>A reference to a block and the work that makes the block durable and gives the checksum of every byte of it; the work of all the blocks of a record runs on one thread of its own.</summary>
        private sealed record Block(PayloadRef Reference, Func<uint> Run);

        private Task<T> Work<T>(Func<T> work) => Task.Factory.StartNew(() =>
        {
            owner._options.BeforePayloadWork?.Invoke();
            return work();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        /// <summary>
        /// Records an edit. When it refers to large blocks the record waits for them to be durable and checked, and the writer goes on with the records of other documents meanwhile;
        /// the later records of this document queue behind it.
        /// </summary>
        private void AppendEdit(ChangeSet c, ChangeKind kind, bool isNew, DocumentChangedEventArgs? args = null)
        {
            var waiting = new Waiting();
            var record = Edit(c, kind, isNew, waiting, args);
            if (!waiting.Any)
            {
                Append(RecordType.DocEdit, JournalRecords.Encode(record));
                return;
            }
            var blocks = waiting.Blocks;
            var checksums = blocks.Count > 0 ? Work(() => blocks.Select(b => b.Block.Run()).ToArray()) : null;
            var gate = Task.WhenAll(checksums ?? Task.CompletedTask, waiting.List ?? Task.CompletedTask);
            owner.AppendDeferred(RecordType.DocEdit, () =>
            {
                var edits = record.Edits.ToArray();
                for (int k = 0; k < blocks.Count; k++)
                    edits[blocks[k].Index] = edits[blocks[k].Index] with { External = blocks[k].Block.Reference with { FullCrc = checksums!.Result[k] } };
                return JournalRecords.Encode(record with { Edits = edits });
            }, gate, id);
        }

        private EditRecord Edit(ChangeSet c, ChangeKind kind, bool isNew, Waiting waiting, DocumentChangedEventArgs? args = null)
        {
            if (NeedsList(c))
            {
                var list = WriteList(c, out var synced);
                waiting.List = synced;
                _refs.Add((c.Revision + baseRevision, list.File));
                return new EditRecord(id, c.Revision + baseRevision, kind, c.Source, isNew, [], doc.Codec.Name, list);
            }
            var edits = new ChangeEdit[c.Edits.Count];
            long shift = 0;
            for (int i = 0; i < edits.Length; i++)
            {
                var s = c.Edits[i];
                if (s.Pattern is { } pattern)
                {
                    edits[i] = new(s.Start, s.Deleted, pattern, s.Inserted);
                    shift += s.Inserted - s.Deleted;
                    continue;
                }
                // past the inline budget of the document every edit that is more than a keystroke goes to a payload file too, so the records a recovery has to hold stay bounded
                if (s.Inserted > JournalPlaceholders.MaxInlineEditBytes || (s.Inserted > JournalPlaceholders.SmallEditBytes && _inlineBytes + s.Inserted > JournalPlaceholders.MaxInlineDocumentBytes))
                {
                    var block = Externalize(s.Start + shift, s.Inserted);
                    waiting.Blocks.Add((i, block));
                    _refs.Add((c.Revision + baseRevision, block.Reference.File));
                    shift += s.Inserted - s.Deleted;
                    edits[i] = new(s.Start, s.Deleted, [], 0, block.Reference);
                    continue;
                }
                // the step has the inserted bytes already; reading them back from the text costs a lookup for every edit
                byte[] inserted;
                if (args is not null && doc.TryGetInserted(args, i, out var kept)) inserted = kept.ToArray();
                else
                {
                    inserted = new byte[checked((int)s.Inserted)];
                    if (inserted.Length > 0) doc.Read(s.Start + shift, inserted);
                }
                _inlineBytes += inserted.Length;
                shift += s.Inserted - s.Deleted;
                edits[i] = new(s.Start, s.Deleted, inserted);
            }
            return new EditRecord(id, c.Revision + baseRevision, kind, c.Source, isNew, edits, doc.Codec.Name);
        }

        /// <summary>True for a change set with so many edits, or so many inline bytes, that its record would be large (Replace All). A fill is one edit and keeps its pattern.</summary>
        private static bool NeedsList(ChangeSet c)
        {
            if (c.Edits.Count > JournalPlaceholders.MaxInlineEditCount) return !c.Edits.Any(s => s.Pattern is not null);
            long inline = 0;
            foreach (var s in c.Edits)
            {
                if (s.Pattern is not null) return false;
                if (s.Inserted <= JournalPlaceholders.MaxInlineEditBytes) inline += s.Inserted;
            }
            return inline > JournalPlaceholders.MaxInlineRecordBytes;
        }

        /// <summary>
        /// Writes the edits of a change set to the payload file of this document, one entry after the other, through a buffer of
        /// <see cref="SeedChunk"/> bytes: neither the edits nor their text are ever in memory as a whole. The file is synced, on a thread of its own, before the record that names it reaches the disk.
        /// </summary>
        private EditListRef WriteList(ChangeSet c, out Task synced)
        {
            var blob = _blob ??= new PayloadBlob(JournalPayloads.DirectoryOf(owner._dir!), $"{JournalPayloads.Prefix(owner._id!, id)}-blob-{_blobCount++}.payload");
            long at = blob.Length;
            var buffer = new byte[SeedChunk];
            int used = 0;
            uint crc = Crc32.Initial;
            long shift = 0, inserted = 0, deleted = 0;
            void Drain()
            {
                blob.Append(buffer.AsSpan(0, used));
                crc = Crc32.Update(crc, buffer.AsSpan(0, used));
                used = 0;
            }
            foreach (var s in c.Edits)
            {
                if (used + JournalPayloads.ListEntryHeaderBytes > buffer.Length) Drain();
                var head = buffer.AsSpan(used, JournalPayloads.ListEntryHeaderBytes);
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(head, s.Start);
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(head[8..], s.Deleted);
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(head[16..], s.Inserted);
                used += JournalPayloads.ListEntryHeaderBytes;
                for (long done = 0; done < s.Inserted;)
                {
                    if (used == buffer.Length) Drain();
                    int want = (int)Math.Min(buffer.Length - used, s.Inserted - done);
                    int n = doc.Read(s.Start + shift + done, buffer.AsSpan(used, want));
                    if (n == 0) throw new EndOfStreamException("The document ended inside a large change.");
                    used += n;
                    done += n;
                }
                shift += s.Inserted - s.Deleted;
                inserted += s.Inserted;
                deleted += s.Deleted;
            }
            Drain();
            synced = Work(() => SyncBlob(blob));
            return new(blob.Name, at, blob.Length - at, c.Edits.Count, inserted, deleted, Crc32.Finish(crc));
        }

        /// <summary>The file is gone with its document when a sync finds it closed; nothing is left to make durable then.</summary>
        private static uint SyncBlob(PayloadBlob blob, uint result = 0)
        {
            try { blob.Sync(); }
            catch (ObjectDisposedException) { }
            return result;
        }

        /// <summary>
        /// The block of a large edit: the durable file the backend holds it in when there is one, else a copy in the payload file of this document.
        /// Either way the file is synced, and the checksum of every byte of the block is made, when the returned work runs (on a thread of its own); the record that names the block is written when both are done.
        /// </summary>
        private Block Externalize(long offset, long length)
        {
            int sample = (int)Math.Min(JournalPayloads.SampleBytes, length);
            var head = new byte[sample];
            var tail = new byte[sample];
            doc.Read(offset, head);
            doc.Read(offset + length - sample, tail);
            uint headCrc = JournalPayloads.SampleCrc(head), tailCrc = JournalPayloads.SampleCrc(tail);
            string dir = JournalPayloads.DirectoryOf(owner._dir!);
            if (doc.Backend.TryGetDurableBytes(offset, length, out var durable))
                return new(new PayloadRef(durable.File, durable.Offset, length, headCrc, tailCrc), () =>
                {
                    uint whole = JournalPayloads.BlockCrc(dir, durable.File, durable.Offset, length);
                    try { durable.Sync(); }
                    catch (ObjectDisposedException) { } // the file is gone with its document
                    return whole;
                });
            var blob = _blob ??= new PayloadBlob(dir, $"{JournalPayloads.Prefix(owner._id!, id)}-blob-{_blobCount++}.payload");
            long at = blob.Length;
            var buffer = new byte[(int)Math.Min(SeedChunk, length)];
            uint crc = Crc32.Initial;
            for (long done = 0; done < length;)
            {
                int n = doc.Read(offset + done, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - done)));
                if (n == 0) throw new EndOfStreamException("The document ended inside a large change.");
                blob.Append(buffer.AsSpan(0, n));
                crc = Crc32.Update(crc, buffer.AsSpan(0, n));
                done += n;
            }
            uint full = Crc32.Finish(crc);
            return new(new PayloadRef(blob.Name, at, length, headCrc, tailCrc), () => SyncBlob(blob, full));
        }

        public void CloseBlob()
        {
            _blob?.Dispose();
            _blob = null;
        }

        /// <summary>After a save only the files that records newer than the save refer to, and the one the backend writes to now, are still needed.</summary>
        private void DropOldPayloads(long savedRevision)
        {
            if (owner._dir is not { } dir) return;
            _refs.RemoveAll(r => r.Revision <= savedRevision);
            var keep = _refs.Select(r => r.File).ToHashSet();
            if (doc.Backend.DurableFile is { } current) keep.Add(current);
            if (_blob is { } blob && !keep.Contains(blob.Name)) CloseBlob();
            JournalPayloads.DeleteOf(dir, owner._id!, id, keep);
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            long revision = e.Revision >= 0 ? e.Revision : doc.Revision;
            Append(RecordType.DocSaveIntent, JournalRecords.Encode(new SaveIntentRecord(id, revision + baseRevision, e.Target, e.Size, e.Sha256, doc.Codec.Name)));
            owner.Flush();
        }

        private void OnMoved(object? sender, EventArgs e)
        {
            if (doc.FilePath is { } path) Append(RecordType.DocMove, JournalRecords.Encode(new MoveRecord(id, path)));
        }

        private void OnSaved(object? sender, EventArgs e)
        {
            if (doc.FilePath is not { } path || doc.Fingerprint is not { } print) return;
            Append(RecordType.DocSave, JournalRecords.Encode(new SaveRecord(id, doc.SavedRevision + baseRevision, path, print, doc.Codec.Name)));
            _floor = doc.SavedUndoDepth;
            _inlineBytes = 0;
            _undo = doc.UndoDepth; // a save over the mapped file drops the undo steps of fills
            _diverged = false;
            _redoOk.Clear();
            _redoOk.AddRange(Enumerable.Repeat(false, doc.RedoDepth));
            owner.Flush(); // the save is durable before the data older records refer to goes
            DropOldPayloads(doc.SavedRevision + baseRevision);
        }
    }
}
