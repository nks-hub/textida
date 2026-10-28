using System.Security.Cryptography;
using CrashHarness;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P3.1.13: undo and redo after a save over the open file of a paged document, with the recovery journal running. A crash at every step of an in-place
/// write and after every undo, redo, edit and save that follows recovers the document as it stood then, and the file on disk is the original or the saved
/// revision, whole. Expected bytes come from <see cref="LargeModel"/>, never from the document.
/// </summary>
[Collection("Crash harness")]
public sealed class PagedSaveUndoJournalTests : IDisposable
{
    private const long Size = 3 << 20;
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-save-undo-journal-").FullName;
    private readonly List<Crash> _crashes = [];

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Data => Path.Combine(_dir, "data", "doc.txt");
    private string Journal => Path.Combine(_dir, "journal");

    private PagedBackendFactory Factory() => new(startIndexing: false, spillBudgetBytes: 64 << 10, spillDirectory: Path.Combine(_dir, "spill"));

    private static Func<string, IBackendFactory?> Backends(IBackendFactory factory) => name => name == "paged" ? factory : null;

    private string NewFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Data)!);
        LargeText.WriteFile(Data, Size);
        return HashFile(Data);
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

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from).Where(f => !f.EndsWith(".lock")))
        {
            using var input = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(Path.Combine(to, Path.GetFileName(f)), FileMode.Create, FileAccess.Write);
            input.CopyTo(output);
        }
        foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// <summary>One edit of a scenario: the document and the model take it, and the model after it is kept.</summary>
    private sealed class Steps(Doc doc, LargeModel start)
    {
        private readonly LargeModel _model = start.Clone();

        /// <summary>The content after each edit, the original first.</summary>
        public List<LargeModel> Models { get; } = [start.Clone()];

        public void Do(long offset, long delete, long seed, long length)
        {
            var bytes = new byte[length];
            LargeText.Fill(seed, 0, bytes);
            Assert.True(doc.Replace(new ByteRange(offset, offset + delete), bytes).Applied);
            _model.Replace(offset, delete, seed, length);
            Models.Add(_model.Clone());
        }
    }

    /// <summary>The disk and the journal as a crash at that moment leaves them, and what recovery has to make of them.</summary>
    private sealed record Crash(string Step, string Snapshot, string Content, string Disk);

    private void Capture(JournalSession session, string step, LargeModel content, string disk)
    {
        session.Flush();
        string snapshot = Path.Combine(_dir, $"snap-{_crashes.Count}-{step}");
        CopyTree(Path.Combine(_dir, "data"), Path.Combine(snapshot, "data"));
        CopyTree(Journal, Path.Combine(snapshot, "journal"));
        _crashes.Add(new Crash(step, snapshot, content.Sha256(), disk));
    }

    private void AssertEveryCrashRecovers(IBackendFactory factory)
    {
        Assert.NotEmpty(_crashes);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        foreach (var crash in _crashes)
        {
            Directory.Delete(Path.Combine(_dir, "data"), recursive: true);
            Directory.Delete(Journal, recursive: true);
            CopyTree(Path.Combine(crash.Snapshot, "data"), Path.Combine(_dir, "data"));
            CopyTree(Path.Combine(crash.Snapshot, "journal"), Journal);

            using var session = JournalSession.Start(Journal, Backends(factory));
            var recovered = Assert.Single(session.Recovery.Documents);
            Assert.True(recovered.Status is RecoveryStatus.Restored or RecoveryStatus.Clean, $"{crash.Step}: {recovered.Status} {recovered.Error}");
            Assert.True(crash.Content == HashOf(recovered.Document!), $"{crash.Step}: the recovered document differs from the document at that moment");
            Assert.True(crash.Disk == HashFile(Data), $"{crash.Step}: the file is neither the original nor the saved revision, whole, as it should be");
            recovered.Document!.Dispose();
        }
    }

    [Fact]
    public async Task Undo_and_redo_after_a_streaming_save_recover_at_every_step()
    {
        string original = NewFile();
        var factory = Factory();
        using (var session = JournalSession.Start(Journal, Backends(factory), new JournalOptions { Fsync = FsyncPolicy.Manual }))
        using (var doc = Doc.Open(Data, factory))
        {
            session.Track(doc);
            var steps = new Steps(doc, new LargeModel(Size));
            var models = steps.Models;
            steps.Do(100, 5, 71, 40);
            steps.Do(Size / 2, 0, 72, 300_000); // past the memory budget: the record refers to the spill file
            steps.Do(9, 0, 73, 3);
            steps.Do(Size / 3, 2000, 74, 0);
            steps.Do(70_000, 400, 75, 400);
            Capture(session, "edited", models[5], original);

            Assert.Equal(SaveMode.Stream, doc.PlanSave().Mode);
            Assert.True((await doc.SaveAsync()).Saved);
            string saved = HashFile(Data);
            Assert.Equal(models[5].Sha256(), saved);
            Capture(session, "saved", models[5], saved);

            for (int k = 1; k <= 3; k++)
            {
                Assert.NotNull(doc.Undo());
                Capture(session, "undo-" + k, models[5 - k], saved);
            }
            for (int k = 1; k <= 2; k++)
            {
                Assert.NotNull(doc.Redo());
                Capture(session, "redo-" + k, models[2 + k], saved);
            }
            Assert.Equal(models[4].Sha256(), HashOf(doc));

            // an edit after an undo drops the redo list; the next save writes that revision
            Assert.NotNull(doc.Undo());
            var fork = models[3].Clone();
            var bytes = new byte[25];
            LargeText.Fill(76, 0, bytes);
            Assert.True(doc.Replace(new ByteRange(1234, 1244), bytes).Applied);
            fork.Replace(1234, 10, 76, 25);
            Capture(session, "edit-after-undo", fork, saved);

            Assert.True((await doc.SaveAsync()).Saved);
            string savedAgain = HashFile(Data);
            Assert.Equal(fork.Sha256(), savedAgain);
            Capture(session, "saved-again", fork, savedAgain);

            while (doc.CanUndo) Assert.NotNull(doc.Undo());
            Assert.Equal(models[0].Sha256(), HashOf(doc));
            Capture(session, "undo-all", models[0], savedAgain);
        }
        AssertEveryCrashRecovers(factory);
    }

    [Fact]
    public void Undo_and_redo_after_an_in_place_save_recover_at_every_step_of_the_write_and_after_it()
    {
        string original = NewFile();
        var factory = Factory();
        using (var session = JournalSession.Start(Journal, Backends(factory), new JournalOptions { Fsync = FsyncPolicy.Manual }))
        using (var doc = Doc.Open(Data, factory))
        {
            session.Track(doc);
            var steps = new Steps(doc, new LargeModel(Size));
            var models = steps.Models;
            steps.Do(100, 5, 81, 5);
            steps.Do(Size / 2, 40, 82, 40);
            steps.Do(9, 3, 83, 3);
            steps.Do(70_000, 400, 84, 400);
            Assert.Equal(SaveMode.InPlace, doc.PlanSave().Mode);

            // the write is cut at each of its steps; before the commit mark recovery puts the old bytes back, after it the new ones stay.
            // commit-partial is left out: the writer still holds the patch journal exclusively there, so it is cut by a real crash in the test below
            string saved = models[4].Sha256();
            doc.InPlaceHooks = new PatchHooks { AfterStep = step => { if (step != "commit-partial") Capture(session, "in-place-" + step, models[4], step == "commit" ? saved : original); } };
            Assert.True(doc.Save().Saved);
            doc.InPlaceHooks = null;
            Assert.Equal(saved, HashFile(Data));
            Capture(session, "saved", models[4], saved);

            for (int k = 1; k <= 3; k++)
            {
                Assert.NotNull(doc.Undo());
                Capture(session, "undo-" + k, models[4 - k], saved);
            }
            for (int k = 1; k <= 2; k++)
            {
                Assert.NotNull(doc.Redo());
                Capture(session, "redo-" + k, models[1 + k], saved);
            }

            // the revision after the undo is written in place again, and the undo goes on to the original
            Assert.NotNull(doc.Undo());
            Assert.Equal(SaveMode.InPlace, doc.PlanSave().Mode);
            Assert.True(doc.Save().Saved);
            string savedAgain = models[2].Sha256();
            Assert.Equal(savedAgain, HashFile(Data));
            Capture(session, "saved-after-undo", models[2], savedAgain);

            while (doc.CanUndo) Assert.NotNull(doc.Undo());
            Assert.Equal(models[0].Sha256(), HashOf(doc));
            Capture(session, "undo-all", models[0], savedAgain);
        }
        AssertEveryCrashRecovers(factory);
    }

    [Fact]
    public void A_crash_between_the_halves_of_the_commit_mark_of_an_in_place_save_gives_the_edited_document_and_the_old_file()
    {
        string original = NewFile();
        var factory = Factory();
        using (var session = JournalSession.Start(Journal, Backends(factory), new JournalOptions { Fsync = FsyncPolicy.Manual }))
        using (var doc = Doc.Open(Data, factory))
        {
            session.Track(doc);
            var steps = new Steps(doc, new LargeModel(Size));
            steps.Do(100, 5, 81, 5);
            steps.Do(Size / 2, 40, 82, 40);
            Assert.Equal(SaveMode.InPlace, doc.PlanSave().Mode);

            doc.InPlaceHooks = new PatchHooks { AfterStep = step => { if (step == "commit-partial") throw new PatchCrash(); } };
            Assert.Throws<PatchCrash>(() => doc.Save());
            Capture(session, "in-place-commit-partial", steps.Models[2], original); // the handles of the dead writer are closed, as a crash leaves them
        }
        AssertEveryCrashRecovers(factory);
    }
}
