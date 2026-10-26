using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NksPad.Core.Documents;
using NksPad.Core.Journal;
using NksPad.Document.Contract;
using Doc = NksPad.Core.Documents.Document;

namespace NksPad.Core.Tests;

internal sealed class JournalTempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nkspad-journal-" + Guid.NewGuid().ToString("N")[..10]);

    public JournalTempDir() => Directory.CreateDirectory(Path);
    public string Combine(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public class JournalFormatTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static string WriteJournal(string path, int records, int version = JournalFormat.CurrentVersion)
    {
        using var fs = new FileStream(path, FileMode.CreateNew);
        fs.Write(JournalFormat.FileHeader(version));
        for (int i = 1; i <= records; i++)
            fs.Write(JournalFormat.EncodeRecord(i, 1000 + i, RecordType.DocClose, U($"payload-{i}-{new string('x', i)}"), version));
        return path;
    }

    private static List<JournalRecord> ReadAll(string path, out JournalTail tail, out long valid)
    {
        using var reader = new JournalReader(path);
        var list = new List<JournalRecord>();
        while (reader.TryRead(out var r)) list.Add(r);
        tail = reader.Tail;
        valid = reader.ValidLength;
        return list;
    }

    [Fact]
    public void RecordsRoundTrip()
    {
        using var dir = new JournalTempDir();
        var records = ReadAll(WriteJournal(dir.Combine("a.njl"), 5), out var tail, out _);
        Assert.Equal(JournalTail.Clean, tail);
        Assert.Equal(5, records.Count);
        Assert.Equal(U("payload-3-xxx"), records[2].Payload);
        Assert.Equal(1003, records[2].TimestampMs);
    }

    [Fact]
    public void CutAtEveryByteGivesExactlyThePreviousRecords()
    {
        using var dir = new JournalTempDir();
        var path = WriteJournal(dir.Combine("a.njl"), 12);
        var full = File.ReadAllBytes(path);
        var ends = new List<long> { JournalFormat.FileHeaderSize };
        foreach (var r in ReadAll(path, out _, out _)) ends.Add(ends[^1] + JournalFormat.RecordHeaderSize(2) + r.Payload.Length);

        for (int cut = JournalFormat.FileHeaderSize; cut <= full.Length; cut++)
        {
            var cutPath = dir.Combine($"cut{cut}.njl");
            File.WriteAllBytes(cutPath, full[..cut]);
            var records = ReadAll(cutPath, out var tail, out long valid);
            int expected = ends.Count(e => e <= cut) - 1;
            Assert.Equal(expected, records.Count);
            Assert.Equal(ends[expected], valid);
            Assert.Equal(ends.Contains(cut) ? JournalTail.Clean : JournalTail.Truncated, tail);
        }
    }

    [Fact]
    public void AFlippedBitEndsTheReadAtThatRecordAndKeepsTheEarlierOnes()
    {
        using var dir = new JournalTempDir();
        var path = WriteJournal(dir.Combine("a.njl"), 8);
        var full = File.ReadAllBytes(path);
        var ends = new List<long> { JournalFormat.FileHeaderSize };
        foreach (var r in ReadAll(path, out _, out _)) ends.Add(ends[^1] + JournalFormat.RecordHeaderSize(2) + r.Payload.Length);

        for (int pos = JournalFormat.FileHeaderSize; pos < full.Length; pos++)
        {
            var damaged = (byte[])full.Clone();
            damaged[pos] ^= 0x10;
            var p = dir.Combine($"flip{pos}.njl");
            File.WriteAllBytes(p, damaged);
            var records = ReadAll(p, out var tail, out _);
            int hit = ends.FindIndex(e => e > pos) - 1;
            Assert.Equal(hit, records.Count);
            Assert.NotEqual(JournalTail.Clean, tail);
        }
    }

    [Fact]
    public void ADamagedOrNewerHeaderIsRejectedNotMisread()
    {
        using var dir = new JournalTempDir();
        var path = WriteJournal(dir.Combine("a.njl"), 2);
        var bytes = File.ReadAllBytes(path);
        bytes[3] ^= 1;
        File.WriteAllBytes(dir.Combine("bad.njl"), bytes);
        Assert.Throws<JournalFormatException>(() => new JournalReader(dir.Combine("bad.njl")));

        var future = JournalFormat.FileHeader(JournalFormat.CurrentVersion + 1);
        File.WriteAllBytes(dir.Combine("future.njl"), future);
        Assert.Throws<JournalFormatException>(() => new JournalReader(dir.Combine("future.njl")));
        Assert.True(JournalRecovery.Recover(dir.Combine("future.njl"), _ => null).Unreadable);
    }

    [Fact]
    public void VersionOneJournalIsReadAndMigratedWhenAdopted()
    {
        using var dir = new JournalTempDir();
        var journalDir = dir.Combine("j");
        Directory.CreateDirectory(journalDir);
        var id = Guid.NewGuid();
        var legacy = Path.Combine(journalDir, "session-" + Guid.NewGuid().ToString("N") + ".njl");
        var bytes = U("Příliš žluťoučký");
        using (var fs = new FileStream(legacy, FileMode.CreateNew))
        {
            fs.Write(JournalFormat.FileHeader(1));
            var format = DocFormat.Of(EncodingDetector.Detect(bytes, true));
            fs.Write(JournalFormat.EncodeRecord(1, 0, RecordType.DocOpen, JournalRecords.Encode(new OpenRecord(id, null, null, format, "reference", 0, bytes.Length)), 1));
            fs.Write(JournalFormat.EncodeRecord(2, 0, RecordType.DocSeed, JournalRecords.Encode(new SeedRecord(id, 0, bytes)), 1));
            fs.Write(JournalFormat.EncodeRecord(3, 0, RecordType.DocEdit, JournalRecords.Encode(new EditRecord(id, 1, ChangeKind.Edit, null, true,
                [new ChangeEdit(0, 0, U("A "))])), 1));
        }

        var direct = JournalRecovery.Recover(legacy, ReferenceResolver);
        Assert.Equal(RecoveryStatus.Restored, Assert.Single(direct.Documents).Status);
        Assert.Contains("migrated", string.Join(' ', direct.Notes));

        using var session = JournalSession.Start(journalDir, ReferenceResolver);
        var doc = Assert.Single(session.Recovery.Documents).Document!;
        Assert.Equal("A Příliš žluťoučký", doc.ReadText(new ByteRange(0, doc.ByteLength)));
        session.Flush();
        Assert.False(File.Exists(legacy));
        using var reader = new JournalReader(session.JournalPath!);
        Assert.Equal(JournalFormat.CurrentVersion, reader.Version);
        doc.Dispose();
    }

    internal static IBackendFactory? ReferenceResolver(string name) => name switch
    {
        "reference" => ReferenceBackendFactory.Instance,
        "paged" => PagedBackendFactory.Instance,
        _ => null,
    };

    [Fact]
    public void FlushIntervalAboveTheLossLimitIsRefused()
    {
        using var dir = new JournalTempDir();
        Assert.Throws<ArgumentOutOfRangeException>(() => new JournalWriter(dir.Combine("a.njl"), new JournalOptions { FlushInterval = TimeSpan.FromSeconds(3) }));
    }

    [Fact]
    public void WriterPoliciesDecideWhenRecordsReachTheDisk()
    {
        using var dir = new JournalTempDir();
        using (var every = new JournalWriter(dir.Combine("e.njl"), new JournalOptions { Fsync = FsyncPolicy.EveryRecord }))
        {
            every.Append(RecordType.DocClose, U("x"));
            Assert.Single(ReadAll(dir.Combine("e.njl"), out _, out _));
        }
        using (var manual = new JournalWriter(dir.Combine("m.njl"), new JournalOptions { Fsync = FsyncPolicy.Manual }))
        {
            manual.Append(RecordType.DocClose, U("x"));
            Assert.Empty(ReadAll(dir.Combine("m.njl"), out _, out _));
            manual.Flush();
            Assert.Single(ReadAll(dir.Combine("m.njl"), out _, out _));
        }
    }

    [Fact]
    public void BatchedRecordsAreOnDiskWithinTheFlushIntervalAndWellInsideTwoSeconds()
    {
        using var dir = new JournalTempDir();
        using var writer = new JournalWriter(dir.Combine("i.njl"), new JournalOptions { FlushInterval = TimeSpan.FromMilliseconds(500) });
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) writer.Append(RecordType.DocClose, U("x"));
        while (ReadAll(dir.Combine("i.njl"), out _, out _).Count < 100 && sw.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(10);
        Assert.InRange(sw.ElapsedMilliseconds, 0, 1500);
    }

    [Fact]
    public void AFailedWriteStopsTheWriterInsteadOfAppendingAfterATornRecord()
    {
        using var dir = new JournalTempDir();
        var writer = new JournalWriter(dir.Combine("f.njl"), new JournalOptions { Fsync = FsyncPolicy.Manual });
        writer.Append(RecordType.DocClose, U("x"));
        writer.Flush();
        typeof(JournalWriter).GetField("_file", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(writer).As<FileStream>().Dispose();
        writer.Append(RecordType.DocClose, U("y"));
        writer.Flush();
        Assert.NotNull(writer.Fault);
        writer.Append(RecordType.DocClose, U("z"));
        writer.Dispose();
        Assert.Single(ReadAll(dir.Combine("f.njl"), out _, out _));
    }
}

internal static class ObjectExtensions
{
    public static T As<T>(this object? o) => (T)o!;
}

public class JournalRecoveryTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);
    private static byte[] All(Doc d) => d.ReadBytes(new ByteRange(0, d.ByteLength));
    private static readonly Func<string, IBackendFactory?> Backends = JournalFormatTests.ReferenceResolver;

    private static JournalSession NewSession(JournalTempDir dir, JournalOptions? options = null) =>
        JournalSession.Start(dir.Combine("journal"), Backends, options ?? new JournalOptions { Fsync = FsyncPolicy.Manual });

    private static RecoveryResult Crash(JournalSession session)
    {
        session.Flush();
        return JournalRecovery.Recover(session.JournalPath!, Backends);
    }

    [Fact]
    public void UnnamedDocumentComesBackWithBytesRevisionUndoAndUnsavedState()
    {
        using var dir = new JournalTempDir();
        using var session = NewSession(dir);
        using var doc = Doc.FromBytes(U("řeč 😀\r\nline"), ReferenceBackendFactory.Instance);
        session.Track(doc);
        doc.Insert(0, U("A"), new EditOptions { CoalesceKey = "t" });
        doc.Insert(1, U("B"), new EditOptions { CoalesceKey = "t" });
        doc.Replace(new ByteRange(3, 5), U("Ž"));
        doc.Undo();
        doc.Redo();
        doc.Insert(doc.ByteLength, U("\n\u00e9"));

        var recovery = Crash(session);
        var back = Assert.Single(recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        using var restored = back.Document!;
        Assert.Equal(All(doc), All(restored));
        Assert.Equal(doc.Revision, restored.Revision + back.BaseRevision);
        Assert.True(restored.IsModified);
        Assert.Equal(doc.UndoDepth, restored.UndoDepth);
        while (doc.CanUndo)
        {
            doc.Undo();
            restored.Undo();
            Assert.Equal(All(doc), All(restored));
        }
        Assert.False(restored.CanUndo);
    }

    [Fact]
    public void EditsOfNamedDocumentsReplayOnTheUnchangedFileWithoutTouchingIt()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("one\ntwo\nthree\n"));
        var stamp = File.GetLastWriteTimeUtc(file);
        var before = File.ReadAllBytes(file);

        using var session = NewSession(dir);
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        doc.Insert(4, U("1.5\n"));
        doc.Delete(new ByteRange(0, 3));

        var recovery = Crash(session);
        var back = Assert.Single(recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        using var restored = back.Document!;
        Assert.Equal(All(doc), All(restored));
        Assert.True(restored.IsModified);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
        Assert.Equal(["note.txt"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void ChangedFileIsAConflictAndNothingIsAppliedOrWritten()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("original"));
        using var session = NewSession(dir);
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        doc.Insert(0, U("my edit "));
        session.Flush();

        File.WriteAllBytes(file, U("changed by another program"));
        var recovery = JournalRecovery.Recover(session.JournalPath!, Backends);
        var back = Assert.Single(recovery.Documents);
        Assert.Equal(RecoveryStatus.Conflict, back.Status);
        Assert.Null(back.Document);
        Assert.Equal(ExternalChangeKind.Modified, back.Conflict!.Kind);
        Assert.Equal(U("changed by another program"), File.ReadAllBytes(file));

        File.Delete(file);
        Assert.Equal(RecoveryStatus.Missing, Assert.Single(JournalRecovery.Recover(session.JournalPath!, Backends).Documents).Status);
    }

    [Fact]
    public void ConflictSurvivesAdoptionSoTheEditsAreNotDiscardedBehindTheUsersBack()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("original"));
        var journalDir = dir.Combine("journal");
        var first = JournalSession.Start(journalDir, Backends, new JournalOptions { Fsync = FsyncPolicy.Manual });
        var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        first.Track(doc);
        doc.Insert(0, U("kept "));
        first.Flush();
        first.Dispose();
        File.WriteAllBytes(file, U("external"));

        using var second = JournalSession.Start(journalDir, Backends);
        Assert.Equal(RecoveryStatus.Conflict, Assert.Single(second.Recovery.Documents).Status);
        second.Flush();
        File.WriteAllBytes(file, U("original"));
        var third = JournalRecovery.Recover(second.JournalPath!, Backends);
        Assert.Equal(RecoveryStatus.Restored, Assert.Single(third.Documents).Status);
        third.Documents[0].Document!.Dispose();
        doc.Dispose();
    }

    [Fact]
    public void PagedDocumentIsJournaledAsEditsPlusFingerprintWithoutCopyingTheFile()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("big.txt");
        var line = U("0123456789 abcdefghij ř č ž 0123456789 abcdefghij\n");
        using (var fs = File.Create(file))
            for (int i = 0; i < (20 << 20) / line.Length; i++) fs.Write(line);
        long size = new FileInfo(file).Length;

        using var session = NewSession(dir);
        using var doc = Doc.Open(file, PagedBackendFactory.Instance);
        session.Track(doc);
        long Nudge(long at)
        {
            while (!doc.IsCharBoundary(at)) at++;
            return at;
        }
        Assert.True(doc.Insert(Nudge(1_000_000), U("typed")).Applied);
        Assert.True(doc.Delete(new ByteRange(Nudge(5_000_000), Nudge(5_000_010))).Applied);
        Assert.True(doc.Replace(new ByteRange(10, 20), U("ř")).Applied);
        var recovery = Crash(session);

        Assert.True(new FileInfo(session.JournalPath!).Length < 4096, "the journal must hold the edits, not the file");
        var back = Assert.Single(recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        using var restored = back.Document!;
        Assert.Equal("paged", restored.Capabilities.Name);
        Assert.Equal(doc.ByteLength, restored.ByteLength);
        Assert.NotEqual(size, restored.ByteLength);
        foreach (var range in new[] { new ByteRange(0, 100), new ByteRange(999_990, 1_000_020), new ByteRange(4_999_990, 5_000_020), new ByteRange(restored.ByteLength - 60, restored.ByteLength) })
            Assert.Equal(doc.ReadBytes(range), restored.ReadBytes(range));
    }

    [Fact]
    public void RecoveryAfterASaveStartsFromTheSavedFileAndKeepsLaterEdits()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("start"));
        using var session = NewSession(dir);
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        doc.Insert(5, U(" one"));
        Assert.True(doc.Save().Saved);
        doc.Insert(doc.ByteLength, U(" two"));
        doc.Undo();
        doc.Redo();

        var back = Assert.Single(Crash(session).Documents);
        using var restored = back.Document!;
        Assert.Equal(U("start one two"), All(restored));
        Assert.Equal(doc.Revision, restored.Revision + back.BaseRevision);
        Assert.True(restored.IsModified);
        restored.Undo();
        Assert.Equal(U("start one"), All(restored));
        Assert.False(restored.IsModified);
    }

    [Fact]
    public void ACleanNamedDocumentIsReopenedFromItsFile()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("abc"));
        using var session = NewSession(dir);
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        var back = Assert.Single(Crash(session).Documents);
        Assert.Equal(RecoveryStatus.Clean, back.Status);
        Assert.False(back.Document!.IsModified);
        back.Document.Dispose();
    }

    [Fact]
    public void CrashInsideTheSwapOfASaveIsRecognisedByTheIntentRecord()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("start"));
        using var session = NewSession(dir);
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        doc.Insert(5, U(" saved"));
        Assert.True(doc.Save().Saved);
        doc.Insert(0, U(">> "));
        session.Flush();

        // the process died after the file was swapped but before the save record reached the disk: cut the journal there
        var path = session.JournalPath!;
        long offset = JournalFormat.FileHeaderSize;
        long cutAt = -1;
        using (var reader = new JournalReader(path))
            while (reader.TryRead(out var rec))
            {
                if (rec.Type == RecordType.DocSave) cutAt = offset;
                offset += JournalFormat.RecordHeaderSize(2) + rec.Payload.Length;
            }
        Assert.True(cutAt > 0);
        var copy = dir.Combine("crashed.njl");
        using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { var head = new byte[cutAt]; src.ReadExactly(head); File.WriteAllBytes(copy, head); }

        var back = Assert.Single(JournalRecovery.Recover(copy, Backends).Documents);
        Assert.Equal(RecoveryStatus.Clean, back.Status);
        Assert.Equal(U("start saved"), All(back.Document!));
        back.Document!.Dispose();
    }

    [Fact]
    public void ClosedDocumentsAreNotRecovered()
    {
        using var dir = new JournalTempDir();
        using var session = NewSession(dir);
        using var kept = Doc.FromBytes(U("keep"), ReferenceBackendFactory.Instance);
        using var closed = Doc.FromBytes(U("close"), ReferenceBackendFactory.Instance);
        session.Track(kept);
        session.Track(closed);
        closed.Insert(0, U("x"));
        session.Untrack(closed);
        var docs = Crash(session).Documents;
        Assert.Equal(U("keep"), All(Assert.Single(docs).Document!));
        docs[0].Document!.Dispose();
    }

    [Fact]
    public void UndoPastASaveStillGivesTheRightBytes()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("base"));
        using var session = NewSession(dir);
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        doc.Insert(4, U("1"));
        doc.Insert(5, U("2"));
        doc.Save();
        doc.Undo();
        doc.Undo();
        doc.Redo();
        doc.Insert(0, U("<"));
        var back = Assert.Single(Crash(session).Documents);
        Assert.Equal(All(doc), All(back.Document!));
        Assert.Equal(doc.Revision, back.Document!.Revision + back.BaseRevision);
        back.Document.Dispose();
    }

    [Fact]
    public void RandomHistoriesReplayToTheSameBytesAndUndoSteps()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            using var dir = new JournalTempDir();
            var rng = new Random(seed);
            using var session = NewSession(dir);
            using var doc = Doc.FromBytes(U("seed text ř č"), ReferenceBackendFactory.Instance);
            session.Track(doc);
            for (int i = 0; i < 120; i++)
            {
                int r = rng.Next(10);
                long at = rng.Next((int)doc.ByteLength + 1);
                while (!doc.IsCharBoundary(at)) at++;
                if (r < 4) doc.Insert(at, U(rng.Next(3) == 0 ? "ř" : "a"), new EditOptions { CoalesceKey = rng.Next(2) == 0 ? "t" : null });
                else if (r < 5 && at < doc.ByteLength)
                {
                    long end = at + 1;
                    while (!doc.IsCharBoundary(end)) end++;
                    doc.Delete(new ByteRange(at, end));
                }
                else if (r < 7) doc.Undo();
                else if (r < 9) doc.Redo();
                else using (doc.BeginUndoGroup()) { doc.Insert(at, U("g")); doc.Insert(at, U("h")); }
            }
            var back = Assert.Single(Crash(session).Documents);
            using var restored = back.Document!;
            Assert.Equal(All(doc), All(restored));
            Assert.Equal(doc.Revision, restored.Revision + back.BaseRevision);
            Assert.Equal(doc.UndoDepth, restored.UndoDepth);
            Assert.Equal(doc.RedoDepth, restored.RedoDepth);
            while (doc.CanUndo)
            {
                doc.Undo();
                restored.Undo();
                Assert.Equal(All(doc), All(restored));
            }
        }
    }

    [Fact]
    public void CompactionDropsClosedDocumentsAndOldHistoryButNotWork()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("x"));
        using var session = NewSession(dir);
        using var named = Doc.Open(file, ReferenceBackendFactory.Instance);
        using var unnamed = Doc.FromBytes(U("unnamed"), ReferenceBackendFactory.Instance);
        using var gone = Doc.FromBytes(new byte[50_000], ReferenceBackendFactory.Instance);
        session.Track(named);
        session.Track(unnamed);
        session.Track(gone);
        for (int i = 0; i < 200; i++) named.Insert(named.ByteLength, U("line of text\n"));
        named.Save();
        named.Insert(0, U("after save "));
        unnamed.Insert(0, U("edited "));
        session.Untrack(gone);
        session.Flush();
        long before = new FileInfo(session.JournalPath!).Length;

        session.Compact();
        long after = new FileInfo(session.JournalPath!).Length;
        Assert.True(after < before / 4, $"{before} -> {after}");
        named.Insert(0, U("again "));
        unnamed.Insert(0, U("and "));

        var docs = Crash(session).Documents;
        Assert.Equal(2, docs.Count);
        Assert.Equal(All(named), All(docs[0].Document!));
        Assert.Equal(All(unnamed), All(docs[1].Document!));
        foreach (var d in docs) d.Document!.Dispose();
        Assert.Empty(Directory.GetFiles(dir.Combine("journal"), "*.tmp"));
    }

    [Fact]
    public void LargeJournalCompactsItselfInTheBackground()
    {
        using var dir = new JournalTempDir();
        using var session = JournalSession.Start(dir.Combine("journal"), Backends, new JournalOptions { FlushInterval = TimeSpan.FromMilliseconds(50), CompactThresholdBytes = 50_000 });
        using var doc = Doc.FromBytes(U("a"), ReferenceBackendFactory.Instance);
        using var closed = Doc.FromBytes(new byte[100_000], ReferenceBackendFactory.Instance);
        session.Track(doc);
        session.Track(closed);
        session.Untrack(closed);
        session.Flush();
        long start = new FileInfo(session.JournalPath!).Length;
        var sw = Stopwatch.StartNew();
        while (new FileInfo(session.JournalPath!).Length >= start && sw.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(100);
        Assert.True(new FileInfo(session.JournalPath!).Length < start / 2);
        doc.Insert(1, U("b"));
        Assert.Equal(U("ab"), All(Assert.Single(Crash(session).Documents).Document!));
    }

    [Fact]
    public void LiveJournalIsNotAdoptedByASecondStartButACrashedOneIs()
    {
        using var dir = new JournalTempDir();
        var journalDir = dir.Combine("journal");
        using var live = JournalSession.Start(journalDir, Backends, new JournalOptions { Fsync = FsyncPolicy.Manual });
        using var doc = Doc.FromBytes(U("live"), ReferenceBackendFactory.Instance);
        live.Track(doc);
        live.Flush();

        using var other = JournalSession.Start(journalDir, Backends);
        Assert.Empty(other.Recovery.Documents);
        Assert.True(File.Exists(live.JournalPath!));

        live.Dispose();
        using var next = JournalSession.Start(journalDir, Backends);
        var back = Assert.Single(next.Recovery.Documents);
        Assert.Equal(U("live"), All(back.Document!));
        Assert.False(File.Exists(live.JournalPath!));
        back.Document!.Dispose();
    }

    [Fact]
    public void AdoptedDocumentsKeepBeingJournaledUnderTheirOldId()
    {
        using var dir = new JournalTempDir();
        var journalDir = dir.Combine("journal");
        var first = JournalSession.Start(journalDir, Backends, new JournalOptions { Fsync = FsyncPolicy.Manual });
        var doc = Doc.FromBytes(U("abc"), ReferenceBackendFactory.Instance);
        first.Track(doc);
        doc.Insert(3, U("d"));
        first.Dispose();

        using var second = JournalSession.Start(journalDir, Backends);
        var adopted = Assert.Single(second.Recovery.Documents).Document!;
        adopted.Insert(4, U("e"));
        adopted.Undo();
        adopted.Redo();
        second.Flush();
        var again = Assert.Single(JournalRecovery.Recover(second.JournalPath!, Backends).Documents);
        Assert.Equal(U("abcde"), All(again.Document!));
        Assert.Equal(adopted.Revision, again.Document!.Revision + again.BaseRevision);
        again.Document.Dispose();
        adopted.Dispose();
        doc.Dispose();
    }

    [Fact]
    public void JournalNeverWritesTheUsersFile()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("untouched\n"));
        File.SetLastWriteTimeUtc(file, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var hash = SHA256.HashData(File.ReadAllBytes(file));
        var stamp = File.GetLastWriteTimeUtc(file);
        var listing = Directory.GetFiles(dir.Path);

        var journalDir = dir.Combine("journal");
        using var session = JournalSession.Start(journalDir, Backends, new JournalOptions { FlushInterval = TimeSpan.FromMilliseconds(20) });
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        for (int i = 0; i < 50; i++) doc.Insert(0, U("edit "));
        Thread.Sleep(200);
        session.Flush();
        session.Compact();
        session.UpdateSession(new SessionModel([new([new SessionTab(session.IdOf(doc)!.Value, null, doc.Revision, [])], null)], 0));
        session.Flush();
        var back = JournalRecovery.Recover(session.JournalPath!, Backends);
        back.Documents[0].Document!.Dispose();
        session.Dispose();
        using (var next = JournalSession.Start(journalDir, Backends)) next.Recovery.Documents[0].Document!.Dispose();

        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(file)));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
        Assert.Equal(listing, Directory.GetFiles(dir.Path));
        Assert.True(doc.IsModified);
    }
}

public class SessionTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);
    private static readonly Func<string, IBackendFactory?> Backends = JournalFormatTests.ReferenceResolver;

    [Fact]
    public void TabsGroupsOrderActiveTabSelectionsAndUnnamedTitlesComeBack()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("named.txt");
        File.WriteAllBytes(file, U("named file text"));
        var journalDir = dir.Combine("journal");
        var first = JournalSession.Start(journalDir, Backends, new JournalOptions { Fsync = FsyncPolicy.Manual });
        var named = Doc.Open(file, ReferenceBackendFactory.Instance);
        var unnamed1 = Doc.FromBytes(U("first unnamed"), ReferenceBackendFactory.Instance);
        var unnamed2 = Doc.FromBytes(U("second unnamed"), ReferenceBackendFactory.Instance);
        var idN = first.Track(named);
        var id1 = first.Track(unnamed1);
        var id2 = first.Track(unnamed2);
        unnamed1.Insert(0, U("typed "));
        unnamed2.Insert(0, U(">"));

        SessionTab Tab(Guid id, Doc d, string? title, long a, long b) =>
            SessionTab.Capture(id, d, title, [(d.CreateAnchor(a), d.CreateAnchor(b))]);
        first.UpdateSession(new SessionModel(
        [
            new([Tab(id1, unnamed1, "Untitled 1", 6, 11), Tab(idN, named, null, 0, 0)], idN),
            new([Tab(id2, unnamed2, "Untitled 2", 2, 2)], id2),
        ], 1));
        unnamed1.Insert(0, U("later "));
        first.Dispose();

        using var second = JournalSession.Start(journalDir, Backends);
        var model = second.Recovery.Session!;
        Assert.Equal(2, model.Groups.Count);
        Assert.Equal(1, model.ActiveGroup);
        Assert.Equal([2, 1], model.Groups.Select(g => g.Tabs.Count));
        var tabs = model.AllTabs.ToList();
        Assert.Equal(["Untitled 1", null, "Untitled 2"], tabs.Select(t => t.Title));
        Assert.Equal((Guid?)model.Groups[0].Tabs[1].DocId, model.Groups[0].ActiveDocId);

        var byId = second.Recovery.Documents.ToDictionary(d => d.DocId);
        Assert.Equal(U("later typed first unnamed"), All(byId[tabs[0].DocId].Document!));
        Assert.Equal(U(">second unnamed"), All(byId[tabs[2].DocId].Document!));
        Assert.Equal(U("named file text"), All(byId[tabs[1].DocId].Document!));
        Assert.Equal(RecoveryStatus.Clean, byId[tabs[1].DocId].Status);
        // the selection [6, 11) of "typed first" moved behind the later insertion of "later "
        Assert.Equal(new ByteRange(12, 17), Assert.Single(tabs[0].Selections));
        Assert.Equal(new ByteRange(2, 2), Assert.Single(tabs[2].Selections));
        foreach (var d in second.Recovery.Documents) d.Document!.Dispose();
        named.Dispose();
        unnamed1.Dispose();
        unnamed2.Dispose();
    }

    private static byte[] All(Doc d) => d.ReadBytes(new ByteRange(0, d.ByteLength));

    [Fact]
    public void RestoreReadsOnlyJournalsAndIgnoresForeignSessionFiles()
    {
        using var dir = new JournalTempDir();
        var journalDir = dir.Combine("journal");
        Directory.CreateDirectory(journalDir);
        var foreign = new[] { "PSPad.ini", "session.ses", "recent.txt", "session-abc.txt", "readme.njl.bak" };
        foreach (var name in foreign) File.WriteAllText(Path.Combine(journalDir, name), "[Session]\nFile0=C:\\secret\\other.txt\n");
        var stamps = foreign.ToDictionary(n => n, n => File.GetLastWriteTimeUtc(Path.Combine(journalDir, n)));

        using var session = JournalSession.Start(journalDir, Backends);
        Assert.Empty(session.Recovery.Documents);
        Assert.Null(session.Recovery.Session);
        foreach (var name in foreign)
        {
            Assert.Equal("[Session]\nFile0=C:\\secret\\other.txt\n", File.ReadAllText(Path.Combine(journalDir, name)));
            Assert.Equal(stamps[name], File.GetLastWriteTimeUtc(Path.Combine(journalDir, name)));
        }
    }

    [Fact]
    public void UnreadableJournalIsReportedAndLeftAlone()
    {
        using var dir = new JournalTempDir();
        var journalDir = dir.Combine("journal");
        Directory.CreateDirectory(journalDir);
        var bad = Path.Combine(journalDir, "session-deadbeef.njl");
        File.WriteAllBytes(bad, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17]);
        using var session = JournalSession.Start(journalDir, Backends);
        Assert.Empty(session.Recovery.Documents);
        Assert.Contains("session-deadbeef.njl", string.Join(' ', session.Recovery.Notes));
        Assert.True(File.Exists(bad));
    }
}

public class BackupAndAutosaveTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void SaveBacksUpTheVersionOnDiskInTheBackupDirectoryAndKeepsTheConfiguredNumber()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("v0"));
        var backups = new BackupService(new BackupPolicy { Directory = dir.Combine("bak"), KeepVersions = 3 });
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        for (int i = 1; i <= 5; i++)
        {
            doc.Replace(new ByteRange(0, doc.ByteLength), U("v" + i));
            var outcome = backups.Save(doc);
            Assert.True(outcome.Save!.Saved);
            Assert.Equal(BackupStatus.Created, outcome.Backup.Status);
            Assert.Equal(U("v" + (i - 1)), File.ReadAllBytes(outcome.Backup.BackupPath!));
        }
        var folder = Assert.Single(Directory.GetDirectories(dir.Combine("bak")));
        var kept = Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllBytes).ToList();
        Assert.Equal([U("v2"), U("v3"), U("v4")], kept);
        Assert.Equal(U("v5"), File.ReadAllBytes(file));
    }

    [Fact]
    public void BackupBesideTheFileKeepsTheLastVersionAndANewFileNeedsNoBackup()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        var backups = new BackupService(new BackupPolicy { Placement = BackupPlacement.BesideFile });
        using var doc = Doc.FromBytes(U("first"), ReferenceBackendFactory.Instance);
        Assert.Equal(BackupStatus.Skipped, backups.Save(doc, file).Backup.Status);
        doc.Insert(0, U("second "));
        Assert.Equal(BackupStatus.Created, backups.Save(doc).Backup.Status);
        Assert.Equal(U("first"), File.ReadAllBytes(file + ".bak"));
    }

    [Fact]
    public void UnavailableBackupLocationIsReportedAndOnlyBlocksTheSaveWhenRequired()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("old"));
        var blocker = dir.Combine("not-a-directory");
        File.WriteAllText(blocker, "x");
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        doc.Insert(0, U("new "));

        var soft = new BackupService(new BackupPolicy { Directory = Path.Combine(blocker, "sub") });
        Assert.NotNull(soft.CheckAvailability());
        var outcome = soft.Save(doc);
        Assert.Equal(BackupStatus.Failed, outcome.Backup.Status);
        Assert.True(outcome.Save!.Saved);

        doc.Insert(0, U("again "));
        var strict = new BackupService(new BackupPolicy { Directory = Path.Combine(blocker, "sub"), RequireBackup = true });
        var blocked = strict.Save(doc);
        Assert.Null(blocked.Save);
        Assert.Equal(U("new old"), File.ReadAllBytes(file));
    }

    [Fact]
    public void MinIntervalSkipsBackupsThatWouldFillTheFolder()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("a"));
        var backups = new BackupService(new BackupPolicy { Directory = dir.Combine("bak"), MinInterval = TimeSpan.FromHours(1) });
        Assert.Equal(BackupStatus.Created, backups.BackupBeforeSave(file).Status);
        Assert.Equal(BackupStatus.Skipped, backups.BackupBeforeSave(file).Status);
    }

    [Fact]
    public void AutosaveIsOffByDefaultAndTheFileChangesOnlyOnAnExplicitSave()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("saved"));
        var hash = SHA256.HashData(File.ReadAllBytes(file));
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        using var autosave = new AutosaveService(doc, new AutosaveOptions { Interval = TimeSpan.FromMilliseconds(20) });
        doc.Insert(0, U("unsaved "));
        Assert.False(autosave.RunOnce());
        Thread.Sleep(150);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(file)));
        Assert.True(doc.IsModified);

        Assert.True(doc.Save().Saved);
        Assert.Equal(U("unsaved saved"), File.ReadAllBytes(file));
    }

    [Fact]
    public void EnabledAutosaveSavesThroughTheDocumentAndRespectsExternalChanges()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("saved"));
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        using var autosave = new AutosaveService(doc, new AutosaveOptions { Enabled = true, Interval = TimeSpan.FromHours(1) },
            new BackupService(new BackupPolicy { Directory = dir.Combine("bak") }));
        ExternalChange? conflict = null;
        autosave.Conflict += (_, c) => conflict = c;

        Assert.False(autosave.RunOnce());
        doc.Insert(0, U("auto "));
        Assert.True(autosave.RunOnce());
        Assert.Equal(U("auto saved"), File.ReadAllBytes(file));
        Assert.Single(Directory.GetFiles(Directory.GetDirectories(dir.Combine("bak"))[0]));

        doc.Insert(0, U("more "));
        File.WriteAllBytes(file, U("someone else"));
        Assert.False(autosave.RunOnce());
        Assert.NotNull(conflict);
        Assert.Equal(U("someone else"), File.ReadAllBytes(file));
    }

    [Fact]
    public void JournalFallsBackWhenThePrimaryIsUnavailableAndAdmitsWhenNothingWorks()
    {
        using var dir = new JournalTempDir();
        var blocker = dir.Combine("file");
        File.WriteAllText(blocker, "x");
        var bad1 = Path.Combine(blocker, "primary");
        var bad2 = Path.Combine(blocker, "fallback");

        var ok = JournalLocation.Resolve(dir.Combine("primary"), dir.Combine("fallback"));
        Assert.Equal(dir.Combine("primary"), ok.Directory);
        Assert.Null(ok.Warning);

        var fallback = JournalLocation.Resolve(bad1, dir.Combine("fallback"));
        Assert.True(fallback.UsedFallback);
        Assert.Equal(dir.Combine("fallback"), fallback.Directory);
        Assert.NotNull(fallback.PrimaryProblem);
        Assert.Contains("not available", fallback.Warning);

        var none = JournalLocation.Resolve(bad1, bad2);
        Assert.False(none.RecoveryAvailable);
        Assert.Contains("Crash recovery is off", none.Warning);

        using var session = JournalSession.Start(none.Directory, JournalFormatTests.ReferenceResolver);
        using var doc = Doc.FromBytes(U("kept in the tab"), ReferenceBackendFactory.Instance);
        session.Track(doc);
        doc.Insert(0, U("still editable "));
        Assert.False(session.IsActive);
        Assert.Equal(U("still editable kept in the tab"), doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
    }
}

public class CrashHarnessSmokeTest
{
    [Fact]
    public void KillingTheEditorAtRandomPointsRecoversTheDocumentsWithinTheLossLimit()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksPad.Journal.CrashHarness.dll");
        Assert.True(File.Exists(dll), dll);
        using var dir = new JournalTempDir();
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { dll, "run", "--iterations", "8", "--parallel", "4", "--seed", "5000", "--work", dir.Combine("w") }) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEndAsync();
        var error = proc.StandardError.ReadToEndAsync();
        Assert.True(proc.WaitForExit(240_000), "harness timed out");
        Assert.True(proc.ExitCode == 0, output.Result + error.Result);
    }
}
