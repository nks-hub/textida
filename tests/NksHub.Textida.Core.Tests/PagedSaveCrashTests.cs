using CrashHarness;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Journal;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P3.1.13: a crash during the streamed save of a 100 MiB text after 5 000 edits, in ASCII and in a UTF-8 log with Czech letters and CR LF line ends. At each step of the save the
/// disk is the whole original or the whole saved revision, and the journal brings back the document as it was then, with the edits made during the save and three steps undone,
/// one of them a step of the revision that was being saved. Expected content comes from a separate model of the file, never from the document.
/// </summary>
[Trait("Category", "Large")]
[Collection("Crash harness")]
public sealed class PagedSaveCrashTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-p3113-crash-").FullName;

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Data => Path.Combine(_dir, "data", "big.txt");
    private string Journal => Path.Combine(_dir, "journal");

    private static Func<string, IBackendFactory?> Backends(IBackendFactory factory) => name => name == "paged" ? factory : null;

    private sealed record Crash(string Step, string Snapshot, bool Swapped);

    private static void CopyTree(string from, string to, Func<string, bool>? skip = null)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from).Where(f => !f.EndsWith(".lock") && skip?.Invoke(f) != true))
        {
            using var input = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            using var output = new FileStream(Path.Combine(to, Path.GetFileName(f)), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            input.CopyTo(output);
        }
        foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)), skip);
    }

    private sealed class ProgressSink(Action<SaveProgress> report) : IProgress<SaveProgress>
    {
        public void Report(SaveProgress value) => report(value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_crash_at_every_step_of_a_streamed_save_after_5000_edits_leaves_the_original_or_the_saved_revision_whole_and_the_journal_brings_back_the_live_document(bool utf8Log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Data)!);
        string originalSha = PagedSaveUndoTests.Generate(Data, utf8Log);
        var model = new PagedSaveUndoTests.ChunkModel(Data);
        var factory = new PagedBackendFactory(startIndexing: false, spillBudgetBytes: 1 << 20, spillDirectory: Path.Combine(_dir, "spill"));
        var random = new Random(31);
        var crashes = new List<Crash>();
        var gate = new ManualResetEventSlim();
        var edited = new ManualResetEventSlim();
        int midWrite = 0;
        string savedSha, liveSha;

        using (var session = JournalSession.Start(Journal, Backends(factory), new JournalOptions { Fsync = FsyncPolicy.Manual }))
        using (var doc = Doc.Open(Data, factory, new() { Format = TextCodec.Utf8, ChangeLogCapacity = 8192 }))
        {
            session.Track(doc);
            string beforeLast = "";
            for (int i = 0; i < 5000; i++)
            {
                if (i == 4999) beforeLast = model.Sha256();
                PagedSaveUndoTests.Edit(doc, model, random, i, sameLength: false, utf8Log);
            }
            savedSha = model.Sha256();
            liveSha = beforeLast;
            Assert.Equal(SaveMode.Stream, doc.PlanSave().Mode);

            void Capture(string step, bool swapped, bool skipTemp)
            {
                session.Flush();
                string snapshot = Path.Combine(_dir, "snap-" + step);
                CopyTree(Path.GetDirectoryName(Data)!, Path.Combine(snapshot, "data"), skipTemp ? f => f.EndsWith(".tmp") : null);
                CopyTree(Journal, Path.Combine(snapshot, "journal"));
                crashes.Add(new Crash(step, snapshot, swapped));
            }

            doc.ProgressIntervalMs = 0;
            var progress = new ProgressSink(_ =>
            {
                if (Interlocked.Exchange(ref midWrite, 1) == 1) return;
                edited.Wait(); // the edits of the save are in the journal now
                Capture("mid-write", false, skipTemp: true);
            });
            doc.StreamSaveStep = (step, _) =>
            {
                if (step == "temp-writing") return;
                if (step == "temp-written") gate.Wait();
                Capture(step, step is "after-swap" or "saved", skipTemp: false);
            };
            var save = doc.SaveAsync(progress: progress);

            // while the file is written: two more edits, then the last two of them and the last step of the saved revision undone
            PagedSaveUndoTests.Edit(doc, model, random, 5000, sameLength: false, utf8Log);
            PagedSaveUndoTests.Edit(doc, model, random, 5001, sameLength: false, utf8Log);
            for (int k = 0; k < 3; k++) Assert.NotNull(doc.Undo());
            Assert.Equal(liveSha, PagedSaveUndoTests.DocSha(doc));
            edited.Set();
            gate.Set();
            Assert.True((await save).Saved);
            Assert.Equal(savedSha, PagedSaveUndoTests.DiskSha(Data));
            Assert.Equal(liveSha, PagedSaveUndoTests.DocSha(doc));
        }

        string[] order = ["mid-write", "temp-written", "before-swap", "after-swap", "saved"];
        Assert.Equal(order, crashes.Select(c => c.Step).OrderBy(s => Array.IndexOf(order, s)));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        foreach (var crash in crashes)
        {
            Directory.Delete(Path.GetDirectoryName(Data)!, recursive: true);
            Directory.Delete(Journal, recursive: true);
            CopyTree(Path.Combine(crash.Snapshot, "data"), Path.GetDirectoryName(Data)!);
            CopyTree(Path.Combine(crash.Snapshot, "journal"), Journal);

            using var session = JournalSession.Start(Journal, Backends(factory));
            var recovered = Assert.Single(session.Recovery.Documents);
            Assert.True(recovered.Status == RecoveryStatus.Restored, $"{crash.Step}: {recovered.Status} {recovered.Error}");
            Assert.True(PagedSaveUndoTests.DocSha(recovered.Document!) == liveSha, $"{crash.Step}: the recovered document differs from the live one");
            Assert.True(PagedSaveUndoTests.DiskSha(Data) == (crash.Swapped ? savedSha : originalSha), $"{crash.Step}: the file is neither the original nor the saved revision, whole, as it should be");
            Assert.True(Directory.GetFiles(Path.GetDirectoryName(Data)!, "*.tmp").Length == 0, $"{crash.Step}: a temporary file was left behind");
            Assert.True(recovered.Document!.IsModified);
            recovered.Document.Dispose();
        }
    }
}
