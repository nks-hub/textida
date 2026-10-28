using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.Core.Journal;

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
    /// <summary>
    /// The changes are larger than <see cref="RecoveryOptions.DeferAboveBytes"/>, so they were not replayed: nothing is opened or read, the records stay in the journal
    /// and <see cref="JournalSession.RecoverPending"/> or <see cref="JournalSession.DiscardPending"/> settles them. <see cref="RecoveredDocument.PendingBytes"/> says how large they are.
    /// </summary>
    Pending,
}

/// <summary>Choices of a recovery that are not about one journal.</summary>
public sealed class RecoveryOptions
{
    /// <summary>Changes of one document (bytes inserted and removed, copied from the journal or its payload files) above which the document is left as <see cref="RecoveryStatus.Pending"/>; 0 replays everything.</summary>
    public long DeferAboveBytes { get; init; }

    /// <summary>
    /// A file longer than this is not hashed while its document is rebuilt (<see cref="DocumentOptions.HashInBackgroundAboveBytes"/>), so a crash in front of a file of tens of GiB does not make the
    /// recovery read all of it first. The file still has to be the one the journal recorded: a different size is a <see cref="RecoveryStatus.Conflict"/> at once, the same size and write time are taken
    /// for the same file and the pass over the file compares its hash with the recorded one later (a difference raises <see cref="Document.ExternalChangeDetected"/>, and a save that is not an
    /// explicit overwrite returns it), and a different write time with the same size, where only the content can tell a touched file from a changed one, waits for the pass.
    /// The default hashes every file before the document is rebuilt.
    /// </summary>
    public long HashInBackgroundAboveBytes { get; init; } = long.MaxValue;

    /// <summary>Test seam: the passes of <see cref="HashInBackgroundAboveBytes"/> wait for this task before they read a file.</summary>
    internal Task? HashPassGate { get; init; }
}

public sealed class RecoveredDocument
{
    public Guid DocId { get; init; }
    public RecoveryStatus Status { get; init; }
    /// <summary>The rebuilt document for <see cref="RecoveryStatus.Restored"/> and <see cref="RecoveryStatus.Clean"/>; the caller owns it.</summary>
    public Document? Document { get; internal set; }
    /// <summary>Bytes of changes that wait for a decision; only set for <see cref="RecoveryStatus.Pending"/>.</summary>
    public long PendingBytes { get; init; }
    /// <summary>Name of the backend the document was journaled with. A backend bound to the thread that creates it (a text control) must be replayed on the thread that uses it.</summary>
    public string Backend { get; init; } = "";
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
    public static RecoveryResult Recover(string journalPath, Func<string, IBackendFactory?> backends, RecoveryOptions? options = null)
    {
        options ??= new();
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

        var plan = JournalCompactor.Plan(records, verifyIntent: VerifyIntent);
        string payloads = JournalPayloads.DirectoryOf(Path.GetDirectoryName(Path.GetFullPath(journalPath))!);
        foreach (var doc in plan.Docs) result.Documents.Add(Replay(doc, backends, payloads, options));
        result.SessionRecord = plan.Session;
        if (plan.Session is { } s)
        {
            try { result.Session = MapSession(JournalRecords.DecodeSession(s.Payload), result.Documents); }
            catch (Exception e) when (e is JournalFormatException or EndOfStreamException) { result.Notes.Add("The session state could not be read."); }
        }
        return result;
    }

    /// <summary>
    /// The file a save intent names, as the intent can vouch for it. The length is read first: hashing a file of tens of GiB to learn that it is not the
    /// saved one would take minutes. A streamed save is checked by the whole file; an in-place save by the written ranges (after an unfinished write of
    /// the patch journal is completed or undone, which opening the file would do anyway): the hash of an in-place intent is returned as written when
    /// the ranges match and empty when they do not.
    /// </summary>
    private static FileFingerprint? VerifyIntent(SaveIntentRecord intent)
    {
        bool inPlace = InPlaceIntent.IsInPlace(intent.Sha256);
        if (inPlace && File.Exists(intent.Path)) InPlacePatch.Recover(intent.Path);
        if (new FileInfo(intent.Path) is not { Exists: true } info || info.Length != intent.Size) return null;
        if (!inPlace) return FileFingerprint.TryRead(intent.Path);
        return FileFingerprint.TryReadQuick(intent.Path) is { } quick ? quick with { Sha256 = InPlaceIntent.Matches(intent.Path, intent.Sha256) ? intent.Sha256 : "" } : null;
    }

    /// <summary>Replays one document whatever its changes weigh; this is what settles a <see cref="RecoveryStatus.Pending"/> document once the user chose to restore it.</summary>
    internal static RecoveredDocument ReplayDocument(JournalCompactor.DocLog log, Func<string, IBackendFactory?> backends, string payloads, RecoveryOptions? like = null) =>
        Replay(log, backends, payloads, new RecoveryOptions { HashInBackgroundAboveBytes = like?.HashInBackgroundAboveBytes ?? long.MaxValue });

    /// <summary>Bytes a replay of these records has to copy out of the journal and its payload files, and bytes it removes; a fill costs nothing, whatever its length.</summary>
    private static long ChangeWeight(List<JournalRecord> records)
    {
        long total = 0;
        foreach (var rec in records)
        {
            if (rec.Type != RecordType.DocEdit) continue;
            var e = JournalRecords.DecodeEdit(rec.Payload);
            if (e.List is { } list)
            {
                total += list.InsertedBytes + list.DeletedBytes;
                continue;
            }
            foreach (var x in e.Edits) total += x.Deleted + (x.Repeat > 0 ? 0 : x.External?.Length ?? x.Inserted.Length);
        }
        return total;
    }

    private static RecoveredDocument Replay(JournalCompactor.DocLog log, Func<string, IBackendFactory?> backends, string payloads, RecoveryOptions recovery, bool retried = false)
    {
        var open = JournalRecords.DecodeOpen(log.Open.Payload);
        var edits = log.Rest.Where(r => r.Type is RecordType.DocEdit or RecordType.DocUndo or RecordType.DocRedo or RecordType.DocCodec).ToList();
        long lastMs = log.Rest.Count > 0 ? log.Rest[^1].TimestampMs : log.Open.TimestampMs;

        RecoveredDocument Make(RecoveryStatus status, Document? doc = null, ExternalChange? conflict = null, string? error = null, long lastRevision = 0, long pendingBytes = 0) => new()
        {
            DocId = open.DocId,
            Status = status,
            Document = doc,
            Backend = open.Backend,
            Path = open.Path,
            BaseRevision = open.BaseRevision,
            LastRevision = lastRevision,
            PendingRecords = edits.Count,
            PendingBytes = pendingBytes,
            LastChange = lastMs == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastMs),
            Conflict = conflict,
            Error = error,
            Records = log.All,
        };

        var factory = backends(open.Backend);
        if (factory is null) return Make(RecoveryStatus.Failed, error: $"Backend '{open.Backend}' is not available.");
        var codec = TextCodec.ByName(open.Codec);
        if (codec is null) return Make(RecoveryStatus.Failed, error: $"Codec '{open.Codec}' is not known.");
        var options = new DocumentOptions { Format = codec, HashInBackgroundAboveBytes = recovery.HashInBackgroundAboveBytes, HashPassGate = recovery.HashPassGate, ExpectedFingerprint = edits.Count > 0 ? open.Fingerprint : null };
        Document? document = null;
        try
        {
            if (recovery.DeferAboveBytes > 0 && ChangeWeight(edits) is var weight && weight > recovery.DeferAboveBytes)
                return Make(RecoveryStatus.Pending, pendingBytes: weight);
            if (open.Path is { } path)
            {
                try { document = Document.Open(path, factory, options); }
                catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                {
                    return Make(RecoveryStatus.Missing, error: "The file no longer exists.");
                }
                if (open.Fingerprint is { } expected && edits.Count > 0 && !BaseMatches(document, expected))
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

            long last = ReplayRecords(document, edits, open.BaseRevision, payloads);
            var doc = document;
            document = null;
            return Make(open.Path is null || edits.Count > 0 ? RecoveryStatus.Restored : RecoveryStatus.Clean, doc, lastRevision: last);
        }
        catch (Exception e) when (e is JournalFormatException or InvalidDataException or EndOfStreamException or NotSupportedException or IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            // byte edits of the hex view can split characters, which only the byte backend can hold
            if (!retried && e is ArgumentException or InvalidDataException && open.Backend != "paged" && backends("paged") is { } paged)
                return Replay(log, name => name == open.Backend ? paged : backends(name), payloads, recovery, retried: true);
            return Make(RecoveryStatus.Failed, error: e is OutOfMemoryException ? "There is not enough memory to restore this change." : e.Message);
        }
        finally
        {
            document?.Dispose();
        }
    }

    /// <summary>
    /// Whether the file under a document that was just opened is the one the journal recorded. A document that was hashed while it opened answers by the hash. One whose hash is still being taken
    /// answers by size, and by write time: equal both is taken for the same file (the pass compares the hash later, see <see cref="RecoveryOptions.HashInBackgroundAboveBytes"/>); the same size with
    /// another write time, when the journal knows the hash, is settled by the pass, which is waited for.
    /// </summary>
    private static bool BaseMatches(Document document, FileFingerprint expected)
    {
        var actual = document.Fingerprint!;
        if (actual.HasHash || !expected.HasHash) return actual.SameContent(expected);
        if (actual.Size != expected.Size) return false;
        return actual.LastWriteUtc == expected.LastWriteUtc || document.ConfirmExpectedBase();
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
    private static long ReplayRecords(Document doc, List<JournalRecord> records, long baseRevision, string payloads)
    {
        int keyCounter = 0;
        foreach (var rec in records)
        {
            if (rec.Type == RecordType.DocEdit)
            {
                var e = JournalRecords.DecodeEdit(rec.Payload);
                string key = (e.NewUndoEntry ? null : doc.TopUndoKey) ?? "j" + ++keyCounter;
                if (e.Edits is [{ Repeat: > 0 } fill] && e.Kind == ChangeKind.Edit)
                {
                    var filled = doc.Fill(new ByteRange(fill.Start, fill.Start + fill.Deleted), fill.Inserted, fill.Repeat, new EditOptions { CoalesceKey = key, Source = e.Source });
                    if (!filled.Applied || baseRevision + doc.Revision != e.Revision)
                        throw new InvalidDataException($"Replay of revision {e.Revision} ended at {baseRevision + doc.Revision} ({filled.Conflict}).");
                    continue;
                }
                var byteEdits = e.List is { } list
                    ? JournalPayloads.LoadList(payloads, list)
                    : e.Edits.Select(x => new ByteEdit(new ByteRange(x.Start, x.Start + x.Deleted), x.External is { } external ? JournalPayloads.Load(payloads, external) : x.Inserted)).ToList();
                var target = e.Codec is null ? null : TextCodec.ByName(e.Codec) ?? throw new InvalidDataException($"Codec '{e.Codec}' is not known.");
                var result = target is not null && target != doc.Codec
                    ? doc.ApplyConversion(byteEdits, target, e.Source)
                    : doc.Apply(byteEdits, new EditOptions { CoalesceKey = key, Source = e.Source, RawBytes = true });
                if (!result.Applied || baseRevision + doc.Revision != e.Revision)
                    throw new InvalidDataException($"Replay of revision {e.Revision} ended at {baseRevision + doc.Revision} ({result.Conflict}).");
            }
            else if (rec.Type == RecordType.DocCodec)
            {
                var c = JournalRecords.DecodeCodec(rec.Payload);
                doc.ReinterpretAs(TextCodec.ByName(c.Codec) ?? throw new InvalidDataException($"Codec '{c.Codec}' is not known."));
                if (baseRevision + doc.Revision != c.Revision) throw new InvalidDataException($"Replay of the codec change for revision {c.Revision} ended at {baseRevision + doc.Revision}.");
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
                var bookmarks = new List<long>();
                if (doc is not null && rel >= 0 && rel <= doc.Revision)
                    foreach (var s in t.Selections)
                    {
                        var mapped = doc.MapRange(s, rel);
                        if (mapped.Ok && mapped.Range.End <= doc.ByteLength) selections.Add(mapped.Range);
                    }
                if (doc is not null && rel >= 0 && rel <= doc.Revision)
                    foreach (long b in (t.Bookmarks ?? []).Where(b => b >= 0 && (rel < doc.Revision || b <= doc.ByteLength)))
                        if (doc.CreateAnchor(b, AnchorGravity.Right, rel) is { IsValid: true } mappedMark) bookmarks.Add(mappedMark.Offset);
                var hexMarks = new List<StoredHexBookmark>();
                if (doc is not null && rel >= 0 && rel <= doc.Revision)
                    foreach (var h in t.HexBookmarks ?? [])
                        if (h.Start >= 0 && h.End > h.Start && doc.MapRegion(new ByteRange(h.Start, h.End), rel) is { } region && region.End <= doc.ByteLength)
                            hexMarks.Add(h with { Start = region.Start, End = region.End });
                tabs.Add(new(t.DocId, t.Title, doc?.Revision ?? 0, selections, bookmarks, hexMarks));
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

    /// <summary>A second open record of a document brings the hash of a file that was opened without one. It counts only before the first save, which replaces the fingerprint anyway, and only for a file of the same size.</summary>
    private static void AddHash(DocLog log, JournalRecord later)
    {
        if (log.Rest.Any(r => r.Type is RecordType.DocSave or RecordType.DocSaveIntent)) return;
        var open = JournalRecords.DecodeOpen(log.Open.Payload);
        if (open.Fingerprint is not { HasHash: false } old || JournalRecords.DecodeOpen(later.Payload).Fingerprint is not { HasHash: true } hashed || hashed.Size != old.Size) return;
        log.Open = log.Open with { Payload = JournalRecords.Encode(open with { Fingerprint = hashed }) };
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
                    if (logs.TryGetValue(id, out var known)) AddHash(known, rec);
                    else
                    {
                        logs[id] = new DocLog { Open = rec };
                        order.Add(id);
                    }
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
                    // an in-place save has no whole-file hash to record; the document will have none until it is read again
                    point = new SaveRecord(intent.DocId, intent.Revision, intent.Path, InPlaceIntent.IsInPlace(intent.Sha256) ? actual with { Sha256 = "" } : actual, intent.Codec);
                }
            }
            if (point is not null)
            {
                // a save that streams while the user keeps typing: edits of later revisions can stand before its record, and they are not in the saved file
                var ahead = log.Rest.Take(save).Where(r => r.Type is RecordType.DocEdit or RecordType.DocUndo or RecordType.DocRedo or RecordType.DocCodec && JournalRecords.RevisionOf(r) > point.Revision).ToList();
                var o = JournalRecords.DecodeOpen(log.Open.Payload);
                var open = o with { Path = point.Path, Fingerprint = point.Fingerprint, BaseRevision = point.Revision, SeedLength = 0, Codec = point.Codec ?? o.Codec };
                log.Open = new JournalRecord(0, log.Rest[save].TimestampMs, RecordType.DocOpen, JournalRecords.Encode(open));
                log.Rest = [.. ahead, .. log.Rest.GetRange(save + 1, log.Rest.Count - save - 1)];
            }
            int moved = log.Rest.FindLastIndex(r => r.Type == RecordType.DocMove);
            if (moved >= 0 && JournalRecords.DecodeOpen(log.Open.Payload) is { Path: not null } current)
                log.Open = new JournalRecord(0, log.Open.TimestampMs, RecordType.DocOpen, JournalRecords.Encode(current with { Path = JournalRecords.DecodeMove(log.Rest[moved].Payload).Path }));
            docs.Add(log);
        }
        return new CompactionPlan(docs, session);
    }
}
