using System.Security.Cryptography;
using CrashHarness;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P7.1.4: a large change is recorded in the journal as a reference to the bytes on disk (the spill file of the add buffer, or a payload
/// file of the journal) and a recovery after a crash restores it exactly, or says that it cannot and touches nothing. Expected bytes come from <see cref="LargeModel"/>.
/// </summary>
[Collection("Crash harness")]
public sealed class LargeChangeJournalTests : IDisposable
{
    private const long Size = 3 << 20;
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-large-journal-").FullName;

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Data => Path.Combine(_dir, "data", "doc.txt");
    private string Journal => Path.Combine(_dir, "journal");

    private string NewFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Data)!);
        LargeText.WriteFile(Data, Size);
        return Data;
    }

    private PagedBackendFactory Factory(long budget) => new(startIndexing: false, spillBudgetBytes: budget, spillDirectory: Path.Combine(_dir, "spill"));

    private static Func<string, IBackendFactory?> Backends(IBackendFactory factory) => name => name == "paged" ? factory : null;

    private static void Edit(Doc doc, LargeModel model, long offset, long delete, long seed, long length)
    {
        var bytes = new byte[length];
        LargeText.Fill(seed, 0, bytes);
        Assert.True(doc.Replace(new ByteRange(offset, offset + delete), bytes).Applied);
        model.Replace(offset, delete, seed, length);
    }

    private static string HashOf(Doc doc)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        for (long at = 0; at < doc.ByteLength;)
        {
            int n = doc.Read(at, buffer);
            hash.AppendData(buffer, 0, n);
            at += n;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    private static List<EditRecord> EditRecords(string journalPath)
    {
        using var reader = new JournalReader(journalPath);
        var list = new List<EditRecord>();
        while (reader.TryRead(out var rec))
            if (rec.Type == RecordType.DocEdit) list.Add(JournalRecords.DecodeEdit(rec.Payload));
        return list;
    }

    /// <summary>Edits a tracked document and ends the session the way a crash leaves it: records are on disk, nothing is cleaned up.</summary>
    private (LargeModel Model, string Journal) EditAndCrash(long budget, long insertLength)
    {
        NewFile();
        var model = new LargeModel(Size);
        var factory = Factory(budget);
        using var session = JournalSession.Start(Journal, Backends(factory), new JournalOptions { Fsync = FsyncPolicy.Manual });
        using (var doc = Doc.Open(Data, factory))
        {
            session.Track(doc);
            Edit(doc, model, 100, 5, 51, 40);
            Edit(doc, model, Size / 2, 0, 52, insertLength);
            Edit(doc, model, 9, 0, 53, 3);
            session.Flush();
        }
        return (model, session.JournalPath!);
    }

    private RecoveryResult Recover(long budget)
    {
        using var session = JournalSession.Start(Journal, Backends(Factory(budget)));
        // the session only has to read what the crash left; the caller looks at the documents of this result
        return session.Recovery;
    }

    [Theory]
    [InlineData(64 << 10, "-add-")] // the add buffer is on disk: the record refers to its spill file
    [InlineData(64 << 20, "-blob-")] // the add buffer is in memory: the journal keeps a copy of its own
    public void A_large_insert_is_recorded_as_a_reference_and_recovers_exactly(long budget, string kind)
    {
        var (model, journalPath) = EditAndCrash(budget, 2 << 20);

        var edit = Assert.Single(EditRecords(journalPath), e => e.Edits.Any(x => x.External is not null));
        var reference = Assert.Single(edit.Edits).External!;
        Assert.Contains(kind, reference.File);
        Assert.Empty(Assert.Single(edit.Edits).Inserted);
        Assert.True(new FileInfo(journalPath).Length < 16 << 10, "the journal grew with the inserted text");
        Assert.True(new FileInfo(Path.Combine(Journal, "payload", reference.File)).Length >= reference.Offset + reference.Length);

        using var session = JournalSession.Start(Journal, Backends(Factory(budget)));
        var recovered = Assert.Single(session.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, recovered.Status);
        Assert.Equal(model.Sha256(), HashOf(recovered.Document!));
        Assert.Equal(3, recovered.Document!.UndoDepth);
        recovered.Document.Dispose();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("short")]
    [InlineData("changed")]
    public void A_large_change_whose_bytes_are_gone_is_reported_and_the_original_stays_untouched(string damage)
    {
        var (_, journalPath) = EditAndCrash(64 << 10, 2 << 20);
        string original = HashFile(Data);
        var reference = EditRecords(journalPath).SelectMany(e => e.Edits).Single(x => x.External is not null).External!;
        string payload = Path.Combine(Journal, "payload", reference.File);
        if (damage == "missing") File.Delete(payload);
        else if (damage == "short") using (var fs = new FileStream(payload, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) fs.SetLength(reference.Offset + reference.Length / 2);
        else using (var fs = new FileStream(payload, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            fs.Position = reference.Offset;
            fs.WriteByte(0xEE);
        }

        using var session = JournalSession.Start(Journal, Backends(Factory(64 << 10)));
        var recovered = Assert.Single(session.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Failed, recovered.Status);
        Assert.Null(recovered.Document);
        Assert.False(string.IsNullOrEmpty(recovered.Error));
        Assert.Equal(original, HashFile(Data));
        Assert.True(recovered.PendingRecords > 0);
        // the records are kept for another try: the new journal holds them
        Assert.Contains(EditRecords(session.JournalPath!), e => e.Edits.Any(x => x.External is not null));
    }

    [Fact]
    public void A_journal_of_version_2_with_layout_5_records_still_recovers()
    {
        string path = NewFile();
        var print = FileFingerprint.TryRead(path)!;
        var id = Guid.NewGuid();
        byte[] Payload(Action<BinaryWriter> fill)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)5);
                fill(w);
            }
            return ms.ToArray();
        }

        var open = Payload(w =>
        {
            w.Write(id.ToByteArray());
            w.Write(true);
            w.Write(path);
            w.Write(true);
            w.Write(print.Size);
            w.Write(print.LastWriteUtc.Ticks);
            w.Write(print.Sha256);
            w.Write(Encodings.TextCodec.Utf8.Name);
            w.Write("paged");
            w.Write(0L);
            w.Write(0L);
        });
        var edit = Payload(w =>
        {
            w.Write(id.ToByteArray());
            w.Write(1L);
            w.Write((byte)ChangeKind.Edit);
            w.Write(false);
            w.Write(true);
            w.Write(1);
            w.Write(10L);
            w.Write(0L);
            w.Write(4);
            w.Write("ABCD"u8);
            w.Write(0L); // repeat
            w.Write(true);
            w.Write(Encodings.TextCodec.Utf8.Name);
        });
        Directory.CreateDirectory(Journal);
        using (var fs = new FileStream(Path.Combine(Journal, $"session-{Guid.NewGuid():N}.njl"), FileMode.CreateNew))
        {
            fs.Write(JournalFormat.FileHeader(2));
            fs.Write(JournalFormat.EncodeRecord(1, 1000, RecordType.DocOpen, open, 2));
            fs.Write(JournalFormat.EncodeRecord(2, 1001, RecordType.DocEdit, edit, 2));
        }

        using var session = JournalSession.Start(Journal, Backends(Factory(64 << 10)));
        var recovered = Assert.Single(session.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, recovered.Status);
        var doc = recovered.Document!;
        Assert.Equal(Size + 4, doc.ByteLength);
        var head = new byte[14];
        doc.Read(0, head);
        Assert.Equal("ABCD", System.Text.Encoding.ASCII.GetString(head, 10, 4));
        doc.Dispose();
    }

    [Fact]
    public void A_journal_of_version_3_with_layout_6_records_and_a_payload_reference_recovers_and_is_rewritten_as_version_4()
    {
        string path = NewFile();
        var print = FileFingerprint.TryRead(path)!;
        var id = Guid.NewGuid();
        string sessionId = Guid.NewGuid().ToString("N");
        byte[] Payload(Action<BinaryWriter> fill)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)6);
                fill(w);
            }
            return ms.ToArray();
        }

        var block = new byte[5000];
        LargeText.Fill(77, 0, block);
        string blob = $"{sessionId}-{id:N}-blob-0.payload";
        Directory.CreateDirectory(Path.Combine(Journal, "payload"));
        File.WriteAllBytes(Path.Combine(Journal, "payload", blob), block);

        var open = Payload(w =>
        {
            w.Write(id.ToByteArray());
            w.Write(true);
            w.Write(path);
            w.Write(true);
            w.Write(print.Size);
            w.Write(print.LastWriteUtc.Ticks);
            w.Write(print.Sha256);
            w.Write(Encodings.TextCodec.Utf8.Name);
            w.Write("paged");
            w.Write(0L);
            w.Write(0L);
        });
        var edit = Payload(w =>
        {
            w.Write(id.ToByteArray());
            w.Write(1L);
            w.Write((byte)ChangeKind.Edit);
            w.Write(false);
            w.Write(true);
            w.Write(2);
            w.Write(10L); // inline edit
            w.Write(0L);
            w.Write(4);
            w.Write("ABCD"u8);
            w.Write(0L);
            w.Write(false);
            w.Write(Size / 2); // edit whose bytes are in the payload file
            w.Write(0L);
            w.Write(0);
            w.Write(0L);
            w.Write(true);
            w.Write(blob);
            w.Write(0L);
            w.Write((long)block.Length);
            w.Write(JournalPayloads.SampleCrc(block.AsSpan(0, JournalPayloads.SampleBytes)));
            w.Write(JournalPayloads.SampleCrc(block.AsSpan(block.Length - JournalPayloads.SampleBytes)));
            w.Write(true);
            w.Write(Encodings.TextCodec.Utf8.Name);
        });
        string old = Path.Combine(Journal, $"session-{sessionId}.njl");
        using (var fs = new FileStream(old, FileMode.CreateNew))
        {
            fs.Write(JournalFormat.FileHeader(3));
            fs.Write(JournalFormat.EncodeRecord(1, 1000, RecordType.DocOpen, open, 3));
            fs.Write(JournalFormat.EncodeRecord(2, 1001, RecordType.DocEdit, edit, 3));
        }

        using (var session = JournalSession.Start(Journal, Backends(Factory(64 << 10)), new JournalOptions { Fsync = FsyncPolicy.Manual }))
        {
            var recovered = Assert.Single(session.Recovery.Documents);
            Assert.Equal(RecoveryStatus.Restored, recovered.Status);
            var doc = recovered.Document!;
            Assert.Equal(Size + 4 + block.Length, doc.ByteLength);
            Assert.Equal("ABCD", System.Text.Encoding.ASCII.GetString(doc.ReadBytes(new ByteRange(10, 14))));
            Assert.Equal(block, doc.ReadBytes(new ByteRange(Size / 2 + 4, Size / 2 + 4 + block.Length)));
            session.Flush();
            Assert.False(File.Exists(old), "the version 3 journal was adopted and removed");
            using var reader = new JournalReader(session.JournalPath!);
            Assert.Equal(JournalFormat.CurrentVersion, reader.Version);
            doc.Dispose();
        }
    }

    [Fact]
    public void Payload_files_of_closed_documents_and_dead_sessions_are_removed()
    {
        NewFile();
        var factory = Factory(64 << 10);
        string payloads = Path.Combine(Journal, "payload");
        string dead = Path.Combine(payloads, $"{new string('a', 32)}-{Guid.NewGuid():N}-add-0.payload");
        Directory.CreateDirectory(payloads);
        File.WriteAllBytes(dead, [1, 2, 3]);

        using var session = JournalSession.Start(Journal, Backends(factory), new JournalOptions { Fsync = FsyncPolicy.Manual });
        Assert.False(File.Exists(dead), "the file of a session that is gone and a document nobody knows is swept at start");
        using var doc = Doc.Open(Data, factory);
        session.Track(doc);
        Edit(doc, new LargeModel(Size), 50, 0, 61, 2 << 20);
        session.Flush();
        Assert.NotEmpty(Directory.GetFiles(payloads));

        session.Untrack(doc);
        Assert.Empty(Directory.GetFiles(payloads));
    }

    /// <summary>Where a crash can leave the disk while a document is saved over its own file with edits made meanwhile.</summary>
    private sealed record Crash(string Step, string Snapshot, bool Swapped);

    /// <param name="skip">Files that are not copied: the temporary file of a save that is still writing it is open without sharing.</param>
    private static void CopyTree(string from, string to, Func<string, bool>? skip = null)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from).Where(f => !f.EndsWith(".lock") && skip?.Invoke(f) != true))
        {
            using var input = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(Path.Combine(to, Path.GetFileName(f)), FileMode.Create, FileAccess.Write);
            input.CopyTo(output);
        }
        foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)), skip);
    }

    private sealed class ProgressSink(Action<SaveProgress> report) : IProgress<SaveProgress>
    {
        public void Report(SaveProgress value) => report(value);
    }
    [Fact]
    public Task A_crash_at_every_step_of_a_save_with_edits_meanwhile_recovers_the_edits_or_says_why_not() => CrashAtEveryStepOfASaveAsync(undoSavedStep: false);

    /// <summary>P7.1.3: the user undoes a step of the revision that is being saved, the 2 MiB insert held in the spill file, and does it again after a redo; then types.</summary>
    [Fact]
    public Task A_crash_at_every_step_of_a_save_during_which_a_step_of_the_saved_revision_is_undone_recovers_the_live_text() => CrashAtEveryStepOfASaveAsync(undoSavedStep: true);

    private async Task CrashAtEveryStepOfASaveAsync(bool undoSavedStep)
    {
        NewFile();
        string original = HashFile(Data);
        var factory = Factory(64 << 10);
        var model = new LargeModel(Size);
        var crashes = new List<Crash>();
        var gate = new ManualResetEventSlim();
        var edited = new ManualResetEventSlim();
        int midWrite = 0;
        LargeModel? snapshot = null;
        LargeModel afterFirst;

        using (var session = JournalSession.Start(Journal, Backends(factory), new JournalOptions { Fsync = FsyncPolicy.Manual }))
        using (var doc = Doc.Open(Data, factory))
        {
            session.Track(doc);
            Edit(doc, model, 100, 5, 71, 40);
            afterFirst = model.Clone();
            Edit(doc, model, Size / 2, 0, 72, 2 << 20);
            snapshot = model.Clone();

            doc.ProgressIntervalMs = 0; // a report after every MiB: the first one comes while the temporary file is half written
            var progress = new ProgressSink(_ =>
            {
                if (Interlocked.Exchange(ref midWrite, 1) == 1) return;
                edited.Wait(); // the edits made during the save are in the journal now
                session.Flush();
                string snap = Path.Combine(_dir, "snap-mid-write");
                CopyTree(Path.Combine(_dir, "data"), Path.Combine(snap, "data"), f => f.EndsWith(".tmp"));
                CopyTree(Journal, Path.Combine(snap, "journal"));
                crashes.Add(new Crash("mid-write", snap, false));
            });
            doc.StreamSaveStep = (step, temp) =>
            {
                if (step == "temp-writing") return; // the temp file is still open for writing, only the later steps are crash points here
                if (step == "temp-written") gate.Wait();
                session.Flush();
                string snap = Path.Combine(_dir, "snap-" + step);
                CopyTree(Path.Combine(_dir, "data"), Path.Combine(snap, "data"));
                CopyTree(Journal, Path.Combine(snap, "journal"));
                crashes.Add(new Crash(step, snap, step is "after-swap" or "saved"));
                if (step != "before-swap") return;
                // Windows ReplaceFile parks the target before it moves the temp file in: a crash between the two renames
                string mid = Path.Combine(_dir, "snap-mid-swap");
                CopyTree(snap, mid);
                File.Move(Path.Combine(mid, "data", "doc.txt"), Path.Combine(mid, "data", Path.GetFileName(FileSwap.BackupOf(temp))));
                crashes.Add(new Crash("mid-swap", mid, false));
            };
            var task = doc.SaveAsync(progress: progress);
            if (undoSavedStep)
            {
                Assert.NotNull(doc.Undo());
                Assert.Equal(afterFirst.Sha256(), HashOf(doc));
                Assert.NotNull(doc.Redo());
                Assert.Equal(snapshot.Sha256(), HashOf(doc));
                Assert.NotNull(doc.Undo());
                model = afterFirst.Clone();
                Edit(doc, model, 0, 0, 74, 33);
            }
            else
            {
                Edit(doc, model, 3000, 0, 73, 1 << 20);
                Edit(doc, model, 0, 0, 74, 33);
                var beforeLast = model.Clone();
                Edit(doc, model, 70_000, 400, 75, 0);
                Assert.NotNull(doc.Undo()); // an undo during the save is journaled as an edit and comes back with the others
                model = beforeLast;
            }
            edited.Set();
            gate.Set();
            Assert.True((await task).Saved);
            Assert.Equal(snapshot.Sha256(), HashFile(Data));
            Assert.Equal(model.Sha256(), HashOf(doc));
        }

        string[] order = ["mid-write", "temp-written", "before-swap", "mid-swap", "after-swap", "saved"];
        Assert.Equal(order, crashes.Select(c => c.Step).OrderBy(s => Array.IndexOf(order, s)));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        foreach (var crash in crashes)
        {
            Directory.Delete(Path.Combine(_dir, "data"), recursive: true);
            Directory.Delete(Journal, recursive: true);
            CopyTree(Path.Combine(crash.Snapshot, "data"), Path.Combine(_dir, "data"));
            CopyTree(Path.Combine(crash.Snapshot, "journal"), Journal);

            using var session = JournalSession.Start(Journal, Backends(factory));
            var recovered = Assert.Single(session.Recovery.Documents);
            Assert.True(recovered.Status == RecoveryStatus.Restored, $"{crash.Step}: {recovered.Status} {recovered.Error}");
            Assert.True(model.Sha256() == HashOf(recovered.Document!), $"{crash.Step}: the recovered document differs");
            Assert.True(HashFile(Data) == (crash.Swapped ? snapshot.Sha256() : original), $"{crash.Step}: the file is neither the original nor the saved revision as it should be");
            Assert.True(Directory.GetFiles(Path.Combine(_dir, "data"), "*.tmp").Length == 0, $"{crash.Step}: a temporary file was left behind");
            Assert.True(recovered.Document!.IsModified);
            recovered.Document.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Large")]
    public async Task A_multi_GiB_document_with_large_edits_and_a_streaming_save_recovers_after_a_crash()
    {
        const long big = 3L << 30;
        string path = Data;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        LargeText.WriteFile(path, big);
        string original = HashFile(path);
        var factory = Factory(PagedSpillPlaceholders.MemoryBudgetBytes);
        var model = new LargeModel(big);
        var gate = new ManualResetEventSlim();
        LargeModel? saved = null;

        using (var session = JournalSession.Start(Journal, Backends(factory), new JournalOptions { Fsync = FsyncPolicy.Manual }))
        using (var doc = Doc.Open(path, factory))
        {
            session.Track(doc);
            for (int i = 0; i < 4; i++) Edit(doc, model, big / 5 * (i + 1), 1000, 80 + i, 150 << 20); // 600 MiB of inserted text, in the spill file
            saved = model.Clone();
            doc.StreamSaveStep = (step, temp) =>
            {
                if (step == "temp-writing") return; // the temp file is still open for writing, only the later steps are crash points here
                if (step == "temp-written") gate.Wait();
                session.Flush();
            };
            var task = doc.SaveAsync();
            Edit(doc, model, 77, 0, 90, 200 << 20);
            gate.Set();
            Assert.True((await task).Saved);
            Assert.Equal(saved.Sha256(), HashFile(path));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        using var again = JournalSession.Start(Journal, Backends(factory));
        var recovered = Assert.Single(again.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, recovered.Status);
        Assert.Equal(model.Sha256(), HashOf(recovered.Document!));
        Assert.NotEqual(original, HashFile(path));
        recovered.Document!.Dispose();
    }

    private static string RunHarness(string work, params string[] args)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Journal.CrashHarness.dll");
        Assert.True(File.Exists(dll), dll);
        var psi = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { dll, "large", "--work", work }.Concat(args)) psi.ArgumentList.Add(a);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEndAsync();
        var error = proc.StandardError.ReadToEndAsync();
        Assert.True(proc.WaitForExit(3_000_000), "harness timed out");
        Assert.True(proc.ExitCode == 0, output.Result + error.Result);
        return output.Result;
    }

    [Fact]
    public void Killing_the_process_at_each_step_of_large_edits_and_a_streaming_save_recovers_the_edits_and_never_the_original()
    {
        // 10 named kill points (spill written, journal flushed, temp file half written, temp file written, before and in the middle of the swap, after it, after the save, cancel)
        // and random kills; the edits made during the save include an undo; 2500 small edits push the undo history into its scratch file as well
        var output = RunHarness(Path.Combine(_dir, "w"), "--iterations", "6", "--parallel", "4", "--seed", "9100", "--burst", "2500");
        Assert.Contains("failures 0", output);
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Killing_the_process_during_a_save_of_a_3_GiB_document_recovers_the_edits_and_leaves_the_original_whole()
    {
        var output = RunHarness(Path.Combine(_dir, "w"), "--iterations", "0", "--parallel", "1", "--seed", "9200", "--size", (3L << 30).ToString(),
            "--points", "journal-flushed,mid-write,temp-written,mid-swap,after-swap,saved,cancel");
        Assert.Contains("failures 0", output);
    }
}
