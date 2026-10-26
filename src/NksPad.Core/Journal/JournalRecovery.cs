using NksPad.Core.Documents;

namespace NksPad.Core.Journal;

public enum RecoveryStatus
{
    /// <summary>Unsaved work was rebuilt: same bytes, revision, undo steps and "unsaved" state as before the crash.</summary>
    Restored,
    /// <summary>A named document without unsaved work; opened from its file as it is now.</summary>
    Clean,
    /// <summary>The file changed on disk after the journal was started. Nothing was applied and nothing was written; the journal keeps the edits.</summary>
    Conflict,
    /// <summary>The file is gone and there is unsaved work for it; nothing was applied.</summary>
    Missing,
    /// <summary>The records of the document could not be replayed; see <see cref="RecoveredDocument.Error"/>.</summary>
    Failed,
}

public sealed class RecoveredDocument
{
    public Guid DocId { get; init; }
    public RecoveryStatus Status { get; init; }
    /// <summary>The rebuilt document for <see cref="RecoveryStatus.Restored"/> and <see cref="RecoveryStatus.Clean"/>; the caller owns it.</summary>
    public Document? Document { get; internal set; }
    public string? Path { get; init; }
    public string? Title { get; init; }
    /// <summary>Revision of the journaled history that revision 0 of <see cref="Document"/> corresponds to.</summary>
    public long BaseRevision { get; init; }
    /// <summary>Journal revision of the last replayed change.</summary>
    public long LastRevision { get; init; }
    public int PendingRecords { get; init; }
    public DateTimeOffset? LastChange { get; init; }
    public ExternalChange? Conflict { get; init; }
    public string? Error { get; init; }
    internal List<JournalRecord> Records { get; init; } = [];
}

public sealed class RecoveryResult
{
    public string? SourcePath { get; internal set; }
    public List<RecoveredDocument> Documents { get; } = [];
    /// <summary>Tabs of the last saved session state; selections are mapped to the recovered documents, tabs of unknown documents are dropped.</summary>
    public SessionModel? Session { get; internal set; }
    public DateTimeOffset? LastRecord { get; internal set; }
    public long Records { get; internal set; }
    public JournalTail Tail { get; internal set; }
    public long DiscardedBytes { get; internal set; }
    public List<string> Notes { get; } = [];
    /// <summary>The file is not a readable journal (damaged header or a newer format); it is left untouched.</summary>
    public bool Unreadable { get; internal set; }
    internal JournalRecord? SessionRecord { get; set; }
}

/// <summary>
/// Rebuilds documents from a journal. Named documents are opened from their file and the file must still match the
/// recorded fingerprint, otherwise the result is a conflict and nothing is applied. Nothing here writes to a user file.
/// </summary>
public static class JournalRecovery
{
    public static RecoveryResult Recover(string journalPath, Func<string, IBackendFactory?> backends)
    {
        var result = new RecoveryResult { SourcePath = journalPath };
        var records = new List<JournalRecord>();
        try
        {
            using var reader = new JournalReader(journalPath);
            while (reader.TryRead(out var rec)) records.Add(rec);
            result.Tail = reader.Tail;
            result.DiscardedBytes = reader.TotalLength - reader.ValidLength;
            if (reader.Version != JournalFormat.CurrentVersion) result.Notes.Add($"Journal format version {reader.Version} was migrated to {JournalFormat.CurrentVersion}.");
        }
        catch (JournalFormatException e)
        {
            result.Notes.Add(e.Message);
            result.Unreadable = true;
            return result;
        }
        if (result.DiscardedBytes > 0) result.Notes.Add($"{result.DiscardedBytes} bytes at the end of the journal were incomplete and ignored.");
        result.Records = records.Count;
        if (records.Count > 0) result.LastRecord = DateTimeOffset.FromUnixTimeMilliseconds(records[^1].TimestampMs);

        var plan = JournalCompactor.Plan(records, verifyIntent: intent => FileFingerprint.TryRead(intent.Path));
        foreach (var doc in plan.Docs) result.Documents.Add(Replay(doc, backends));
        result.SessionRecord = plan.Session;
        if (plan.Session is { } s)
        {
            try { result.Session = MapSession(JournalRecords.DecodeSession(s.Payload), result.Documents); }
            catch (Exception e) when (e is JournalFormatException or EndOfStreamException) { result.Notes.Add("The session state could not be read."); }
        }
        return result;
    }

    private static RecoveredDocument Replay(JournalCompactor.DocLog log, Func<string, IBackendFactory?> backends)
    {
        var open = JournalRecords.DecodeOpen(log.Open.Payload);
        var edits = log.Rest.Where(r => r.Type is RecordType.DocEdit or RecordType.DocUndo or RecordType.DocRedo).ToList();
        long lastMs = log.Rest.Count > 0 ? log.Rest[^1].TimestampMs : log.Open.TimestampMs;

        RecoveredDocument Make(RecoveryStatus status, Document? doc = null, ExternalChange? conflict = null, string? error = null, long lastRevision = 0) => new()
        {
            DocId = open.DocId,
            Status = status,
            Document = doc,
            Path = open.Path,
            BaseRevision = open.BaseRevision,
            LastRevision = lastRevision,
            PendingRecords = edits.Count,
            LastChange = lastMs == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastMs),
            Conflict = conflict,
            Error = error,
            Records = log.All,
        };

        var factory = backends(open.Backend);
        if (factory is null) return Make(RecoveryStatus.Failed, error: $"Backend '{open.Backend}' is not available.");
        var options = new DocumentOptions { Format = open.Format.ToEncoding() };
        Document? document = null;
        try
        {
            if (open.Path is { } path)
            {
                try { document = Document.Open(path, factory, options); }
                catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                {
                    return Make(RecoveryStatus.Missing, error: "The file no longer exists.");
                }
                if (open.Fingerprint is { } expected && edits.Count > 0 && !document.Fingerprint!.SameContent(expected))
                {
                    var change = new ExternalChange(open.DocId, 0, path, expected, document.Fingerprint, ExternalChangeKind.Modified);
                    document.Dispose();
                    return Make(RecoveryStatus.Conflict, conflict: change);
                }
            }
            else
            {
                var bytes = AssembleSeeds(log, open);
                document = Document.FromBytes(bytes, factory, options);
            }

            long last = ReplayRecords(document, edits, open.BaseRevision);
            var doc = document;
            document = null;
            return Make(open.Path is null || edits.Count > 0 ? RecoveryStatus.Restored : RecoveryStatus.Clean, doc, lastRevision: last);
        }
        catch (Exception e) when (e is JournalFormatException or InvalidDataException or EndOfStreamException or NotSupportedException or IOException or ArgumentException)
        {
            return Make(RecoveryStatus.Failed, error: e.Message);
        }
        finally
        {
            document?.Dispose();
        }
    }

    private static byte[] AssembleSeeds(JournalCompactor.DocLog log, OpenRecord open)
    {
        if (open.SeedLength > Array.MaxLength) throw new NotSupportedException("An unnamed document over 2 GiB cannot be restored.");
        var bytes = new byte[open.SeedLength];
        long covered = 0;
        foreach (var rec in log.Rest.Where(r => r.Type == RecordType.DocSeed))
        {
            var seed = JournalRecords.DecodeSeed(rec.Payload);
            if (seed.Offset != covered || seed.Offset + seed.Bytes.Length > bytes.Length) throw new InvalidDataException("The initial content of the document is out of order.");
            seed.Bytes.CopyTo(bytes, seed.Offset);
            covered += seed.Bytes.Length;
        }
        if (covered != bytes.Length) throw new InvalidDataException("The initial content of the document was not fully written before the crash.");
        return bytes;
    }

    /// <returns>Journal revision after the last record.</returns>
    private static long ReplayRecords(Document doc, List<JournalRecord> records, long baseRevision)
    {
        int keyCounter = 0;
        foreach (var rec in records)
        {
            if (rec.Type == RecordType.DocEdit)
            {
                var e = JournalRecords.DecodeEdit(rec.Payload);
                string key = (e.NewUndoEntry ? null : doc.TopUndoKey) ?? "j" + ++keyCounter;
                var result = doc.Apply([.. e.Edits.Select(x => new ByteEdit(new ByteRange(x.Start, x.Start + x.Deleted), x.Inserted))],
                    new EditOptions { CoalesceKey = key, Source = e.Source });
                if (!result.Applied || baseRevision + doc.Revision != e.Revision)
                    throw new InvalidDataException($"Replay of revision {e.Revision} ended at {baseRevision + doc.Revision} ({result.Conflict}).");
            }
            else
            {
                var u = JournalRecords.DecodeUndoRedo(rec.Payload);
                if (baseRevision + doc.Revision >= u.Revision) continue;
                if (rec.Type == RecordType.DocUndo) doc.Undo(); else doc.Redo();
                if (baseRevision + doc.Revision < u.Revision) throw new InvalidDataException($"Replay of {rec.Type} for revision {u.Revision} ended at {baseRevision + doc.Revision}.");
            }
        }
        return baseRevision + doc.Revision;
    }

    private static SessionModel MapSession(SessionModel model, List<RecoveredDocument> docs)
    {
        var byId = docs.ToDictionary(d => d.DocId);
        var groups = new List<SessionGroup>();
        foreach (var g in model.Groups)
        {
            var tabs = new List<SessionTab>();
            foreach (var t in g.Tabs)
            {
                if (!byId.TryGetValue(t.DocId, out var d)) continue;
                var doc = d.Document;
                long rel = t.Revision - d.BaseRevision;
                var selections = new List<ByteRange>();
                if (doc is not null && rel >= 0 && rel <= doc.Revision)
                    foreach (var s in t.Selections)
                    {
                        var mapped = doc.MapRange(s, rel);
                        if (mapped.Ok && mapped.Range.End <= doc.ByteLength) selections.Add(mapped.Range);
                    }
                tabs.Add(new(t.DocId, t.Title, doc?.Revision ?? 0, selections));
            }
            groups.Add(new(tabs, g.ActiveDocId is { } a && tabs.Any(t => t.DocId == a) ? a : tabs.FirstOrDefault()?.DocId));
        }
        return new SessionModel(groups, Math.Clamp(model.ActiveGroup, 0, Math.Max(0, groups.Count - 1)));
    }
}

/// <summary>
/// Reduces a journal to what recovery needs: closed documents disappear, everything before a document's last save is replaced
/// by a new start record pointing at the saved file, only the newest session state stays.
/// </summary>
internal static class JournalCompactor
{
    public sealed class DocLog
    {
        public JournalRecord Open;
        /// <summary>Everything after <see cref="Open"/>, seeds included.</summary>
        public List<JournalRecord> Rest = [];
        public bool Closed;
        public List<JournalRecord> All => [Open, .. Rest];
    }

    public sealed class CompactionPlan(List<DocLog> docs, JournalRecord? session)
    {
        public List<DocLog> Docs { get; } = docs;
        public JournalRecord? Session { get; } = session;
    }

    /// <param name="verifyIntent">Reads the file named by a save intent. An intent after the last save whose file has the intended hash counts as a save: the process died after the swap but before the save record was written.</param>
    public static CompactionPlan Plan(IEnumerable<JournalRecord> records, Func<SaveIntentRecord, FileFingerprint?>? verifyIntent = null)
    {
        var order = new List<Guid>();
        var logs = new Dictionary<Guid, DocLog>();
        JournalRecord? session = null;
        foreach (var rec in records)
        {
            try
            {
                if (rec.Type == RecordType.SessionState) { session = rec; continue; }
                var id = JournalRecords.DocOf(rec)!.Value;
                if (rec.Type == RecordType.DocOpen)
                {
                    if (logs.ContainsKey(id)) continue;
                    logs[id] = new DocLog { Open = rec };
                    order.Add(id);
                }
                else if (logs.TryGetValue(id, out var log))
                {
                    if (rec.Type == RecordType.DocClose) log.Closed = true;
                    else log.Rest.Add(rec);
                }
            }
            catch (EndOfStreamException)
            {
                // a record with a valid checksum but a short payload cannot belong to any document
            }
        }

        var docs = new List<DocLog>();
        foreach (var id in order)
        {
            var log = logs[id];
            if (log.Closed) continue;
            int save = log.Rest.FindLastIndex(r => r.Type == RecordType.DocSave);
            SaveRecord? point = save >= 0 ? JournalRecords.DecodeSave(log.Rest[save].Payload) : null;
            int intentAt = verifyIntent is null ? -1 : log.Rest.FindLastIndex(r => r.Type == RecordType.DocSaveIntent);
            if (intentAt > save)
            {
                var intent = JournalRecords.DecodeSaveIntent(log.Rest[intentAt].Payload);
                if (verifyIntent!(intent) is { } actual && actual.Size == intent.Size && actual.Sha256 == intent.Sha256)
                {
                    save = intentAt;
                    point = new SaveRecord(intent.DocId, intent.Revision, intent.Path, actual);
                }
            }
            if (point is not null)
            {
                var o = JournalRecords.DecodeOpen(log.Open.Payload);
                var open = o with { Path = point.Path, Fingerprint = point.Fingerprint, BaseRevision = point.Revision, SeedLength = 0 };
                log.Open = new JournalRecord(0, log.Rest[save].TimestampMs, RecordType.DocOpen, JournalRecords.Encode(open));
                log.Rest = log.Rest.GetRange(save + 1, log.Rest.Count - save - 1);
            }
            docs.Add(log);
        }
        return new CompactionPlan(docs, session);
    }
}
