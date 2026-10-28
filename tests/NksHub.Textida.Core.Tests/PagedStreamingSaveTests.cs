using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using CrashHarness;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Tests;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P7.1.3: a paged document is saved by streaming an immutable snapshot to a temporary file and swapping it in, while the user keeps editing.
/// The expected bytes come from <see cref="LargeModel"/>, which generates them from the edit script and never reads the product.
/// </summary>
public sealed class PagedStreamingSaveTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-stream-").FullName;

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers(); // snapshots keep the replaced file open until they are collected
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string NewFile(long size)
    {
        string path = Path.Combine(_dir, "doc.txt");
        LargeText.WriteFile(path, size);
        return path;
    }

    private PagedBackendFactory Factory(long budget = 64 << 10) => new(startIndexing: false, spillBudgetBytes: budget, spillDirectory: Path.Combine(_dir, "spill"));

    private static void Edit(Doc doc, LargeModel model, long offset, long delete, long seed, long length, FillBytes? fill = null)
    {
        var bytes = new byte[length];
        (fill ?? LargeText.Fill)(seed, 0, bytes);
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

    [Fact]
    public async Task Edits_during_a_streaming_save_stay_unsaved_and_the_file_holds_exactly_the_snapshot()
    {
        const long size = 4 << 20;
        string path = NewFile(size);
        string original = HashFile(path);
        using var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        Edit(doc, model, 1000, 10, 11, 2 << 20); // far over the 64 KiB budget: the add buffer is on disk
        var beforeLast = model.Clone();
        Edit(doc, model, 5000, 0, 12, 50);
        var snapshot = model.Clone();
        long savedRevision = doc.Revision;

        var gate = new ManualResetEventSlim();
        string? tempHash = null, targetAtSwap = null;
        bool? finishedAtSwap = null;
        Task<SaveResult>? task = null;
        doc.StreamSaveStep = (step, temp) =>
        {
            if (step == "temp-written") gate.Wait();
            if (step == "before-swap")
            {
                tempHash = HashFile(temp);
                targetAtSwap = HashFile(path);
                finishedAtSwap = task!.IsCompleted;
            }
        };
        task = doc.SaveAsync();
        Assert.True(doc.IsSaving);
        Assert.True(doc.EditsDuringSave);
        Edit(doc, model, 0, 0, 13, 1 << 20);
        Edit(doc, model, 200_000, 300, 14, 70);
        gate.Set();
        var result = await task;

        Assert.True(result.Saved);
        Assert.Equal(snapshot.Sha256(), tempHash); // the temporary file was complete before the swap
        Assert.Equal(original, targetAtSwap);       // and the file was untouched until then
        Assert.False(finishedAtSwap);               // the save was not confirmed before
        Assert.Equal(snapshot.Sha256(), HashFile(path)); // the file is the snapshot, not a byte of what was typed meanwhile
        Assert.Equal(model.Sha256(), HashOf(doc));       // the editor is on the newer revision
        Assert.True(doc.IsModified);
        Assert.Equal(savedRevision, doc.SavedRevision);
        Assert.True(doc.Revision > savedRevision);
        Assert.False(doc.IsSaving);
        Assert.Equal(snapshot.Length, doc.Fingerprint!.Size);

        // undo works after the save: the two edits made during it, then the one before it
        Assert.NotNull(doc.Undo());
        Assert.True(doc.IsModified);
        Assert.NotNull(doc.Undo());
        Assert.Equal(snapshot.Sha256(), HashOf(doc));
        Assert.False(doc.IsModified);
        Assert.NotNull(doc.Undo());
        Assert.Equal(beforeLast.Sha256(), HashOf(doc));
        Assert.True(doc.IsModified);
        while (doc.CanRedo) doc.Redo();
        Assert.Equal(model.Sha256(), HashOf(doc));

        doc.StreamSaveStep = null;
        Assert.True((await doc.SaveAsync()).Saved);
        Assert.Equal(model.Sha256(), HashFile(path));
        Assert.False(doc.IsModified);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelling_a_streaming_save_leaves_the_original_untouched_and_the_edits_in_place(bool afterTheFileIsWritten)
    {
        const long size = 3 << 20;
        string path = NewFile(size);
        string original = HashFile(path);
        using var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        Edit(doc, model, 100, 0, 21, 1 << 20);

        var gate = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, _) =>
        {
            if (step != "temp-written") return;
            gate.Wait();
            if (afterTheFileIsWritten) doc.CancelSave();
        };
        var task = doc.SaveAsync();
        if (!afterTheFileIsWritten) doc.CancelSave();
        Edit(doc, model, 7000, 5, 22, 400);
        gate.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(original, HashFile(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Equal(model.Sha256(), HashOf(doc));
        Assert.True(doc.IsModified);
        Assert.False(doc.IsSaving);

        doc.StreamSaveStep = null;
        Assert.True((await doc.SaveAsync()).Saved);
        Assert.Equal(model.Sha256(), HashFile(path));
    }

    /// <summary>A save to another path has no check of the file on disk, so only the checks of the save itself can stop it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelling_a_streaming_save_to_another_path_leaves_no_file_there_and_the_original_untouched(bool whileWriting)
    {
        const long size = 3 << 20;
        string path = NewFile(size);
        string original = HashFile(path);
        string copy = Path.Combine(_dir, "copy.txt");
        using var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        Edit(doc, model, 100, 0, 21, 1 << 20);

        using var held = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        string holdAt = whileWriting ? "temp-writing" : "temp-written";
        bool fileComplete = false;
        doc.StreamSaveStep = (step, _) =>
        {
            if (step == "temp-written") fileComplete = true;
            if (step != holdAt) return;
            held.Set();
            proceed.Wait();
        };
        var task = doc.SaveAsync(copy);
        Assert.True(held.Wait(20_000));
        doc.CancelSave();
        proceed.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        if (whileWriting) Assert.False(fileComplete, "the write went on to the end after the cancel");
        Assert.False(File.Exists(copy));
        Assert.Equal(original, HashFile(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Equal(model.Sha256(), HashOf(doc));
        Assert.True(doc.IsModified);
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("temp-gone")]
    public async Task A_swap_that_fails_keeps_the_original_and_the_edits_and_a_later_save_works(string how)
    {
        const long size = 3 << 20;
        string path = NewFile(size);
        string original = HashFile(path);
        using var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        Edit(doc, model, 100, 0, 31, 1 << 20);

        var gate = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, temp) =>
        {
            if (step == "temp-written") gate.Wait();
            if (step != "before-swap") return;
            if (how == "throw") throw new IOException("disk gone");
            File.Delete(temp); // the swap itself fails, after the document let go of its mapping
        };
        var task = doc.SaveAsync();
        Edit(doc, model, 9000, 0, 32, 64);
        gate.Set();

        await Assert.ThrowsAnyAsync<IOException>(() => task);
        Assert.Equal(original, HashFile(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Equal(model.Sha256(), HashOf(doc));
        Assert.True(doc.IsModified);

        Edit(doc, model, 10, 0, 33, 5); // still editable and still on the original file
        doc.StreamSaveStep = null;
        Assert.True((await doc.SaveAsync()).Saved);
        Assert.Equal(model.Sha256(), HashFile(path));
        Assert.Equal(model.Sha256(), HashOf(doc));
    }

    [Fact]
    public void The_paged_document_puts_the_edits_of_a_streaming_save_on_top_of_the_saved_file()
    {
        const long size = 1 << 20;
        string path = NewFile(size);
        string temp = path + ".textida-test.tmp";
        using var doc = PagedDocument.Open(path, new MappedFileSourceOptions { StartIndexing = false, SpillBudgetBytes = 8 << 10, SpillDirectory = Path.Combine(_dir, "spill") });
        doc.Insert(10, new byte[3000]);

        var snapshot = doc.BeginStreamingSave();
        Assert.True(doc.InStreamingSave);
        Assert.Throws<InvalidOperationException>(() => doc.Undo());
        var expected = new byte[snapshot.ByteLength];
        snapshot.Read(0, expected);
        File.WriteAllBytes(temp, expected);

        doc.Insert(0, "head"u8);
        doc.ReplaceRepeated(20, 5, "xy"u8, 11);
        doc.Delete(doc.ByteLength - 3, 3);
        var edited = new byte[doc.ByteLength];
        doc.Read(0, edited);

        doc.CommitStreamingSave(temp, path);

        Assert.False(doc.InStreamingSave);
        var after = new byte[doc.ByteLength];
        doc.Read(0, after);
        Assert.Equal(edited, after);
        Assert.True(doc.IsModified);
        Assert.Equal(expected, File.ReadAllBytes(path)); // the file is the snapshot
        Assert.True(doc.Undo());
        Assert.True(doc.Undo());
        Assert.True(doc.Undo());
        var undone = new byte[doc.ByteLength];
        doc.Read(0, undone);
        Assert.Equal(expected, undone);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public async Task Undo_and_redo_during_a_streaming_save_move_the_editor_only_and_the_saved_state_stays_reachable()
    {
        const long size = 3 << 20;
        string path = NewFile(size);
        using var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        var original = model.Clone();
        Edit(doc, model, 100, 0, 51, 1 << 20);
        var snapshot = model.Clone();

        var gate = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, _) =>
        {
            if (step == "temp-written") gate.Wait();
        };
        var task = doc.SaveAsync();
        Assert.False(doc.UndoBlockedBySave);
        Assert.NotNull(doc.Undo()); // the step the save writes
        Assert.Equal(original.Sha256(), HashOf(doc));
        Assert.NotNull(doc.Redo());
        Assert.NotNull(doc.Undo());
        gate.Set();

        Assert.True((await task).Saved);
        Assert.Equal(snapshot.Sha256(), HashFile(path));
        Assert.Equal(original.Sha256(), HashOf(doc));
        Assert.True(doc.IsModified);
        Assert.NotNull(doc.Redo());
        Assert.False(doc.IsModified); // redo is back at the saved revision
        Assert.Equal(snapshot.Sha256(), HashOf(doc));
        Assert.NotNull(doc.Undo());
        Assert.Equal(original.Sha256(), HashOf(doc));
    }

    [Fact]
    public async Task A_new_edit_after_undoing_the_saved_step_during_a_save_leaves_the_document_unsaved_for_good()
    {
        const long size = 3 << 20;
        string path = NewFile(size);
        using var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        Edit(doc, model, 100, 0, 61, 1 << 20);
        var snapshot = model.Clone();

        var gate = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, _) =>
        {
            if (step == "temp-written") gate.Wait();
        };
        var task = doc.SaveAsync();
        Assert.NotNull(doc.Undo());
        Edit(doc, model, 0, 0, 62, 10); // drops the undone step from the redo list: the saved revision cannot be reached again
        gate.Set();

        Assert.True((await task).Saved);
        Assert.Equal(snapshot.Sha256(), HashFile(path));
        Assert.True(doc.IsModified);
        while (doc.CanUndo) doc.Undo();
        Assert.True(doc.IsModified);
    }

    [Fact]
    public async Task Fill_and_the_undo_of_a_fill_wait_for_the_end_of_a_streaming_save()
    {
        const long size = 3 << 20;
        string path = NewFile(size);
        using var doc = Doc.Open(path, Factory());
        Assert.True(doc.Fill(new ByteRange(0, 8), new byte[] { 7 }, InPlacePatch.MaxPatchBytes + 1).Applied);

        var gate = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, _) =>
        {
            if (step == "temp-written") gate.Wait();
        };
        var task = doc.SaveAsync();
        Assert.Equal(ConflictReason.Saving, doc.Fill(new ByteRange(0, 1), new byte[] { 1 }, 1).Conflict);
        Assert.True(doc.UndoBlockedBySave);
        Assert.Null(doc.Undo());
        gate.Set();
        Assert.True((await task).Saved);
        Assert.False(doc.UndoBlockedBySave);
    }

    [Fact]
    public async Task Undo_steps_beyond_the_memory_budget_live_in_a_scratch_file_and_still_undo_after_a_streaming_save()
    {
        const long size = 3 << 20;
        string path = NewFile(size);
        using var doc = Doc.Open(path, Factory(budget: 4 << 10));
        var model = new LargeModel(size);
        var original = model.Clone();
        for (int i = 0; i < 6; i++) Edit(doc, model, 1000 * (i + 1), 200, 70 + i, 50_000);
        Edit(doc, model, 5, 300_000, 80, 20);
        var snapshot = model.Clone();

        Assert.True((await doc.SaveAsync()).Saved);
        Assert.Equal(snapshot.Sha256(), HashFile(path));
        while (doc.CanUndo) Assert.NotNull(doc.Undo());
        Assert.Equal(original.Sha256(), HashOf(doc));
        while (doc.CanRedo) Assert.NotNull(doc.Redo());
        Assert.Equal(snapshot.Sha256(), HashOf(doc));
        Assert.False(doc.IsModified);
    }

    [Fact]
    [Trait("Category", "Large")]
    public async Task A_multi_GiB_file_is_edited_during_a_streaming_save_and_the_file_equals_the_snapshot()
    {
        const long size = 3L << 30;
        string path = NewFile(size);
        using var doc = Doc.Open(path, Factory(PagedSpillPlaceholders.MemoryBudgetBytes));
        var model = new LargeModel(size);
        Edit(doc, model, size / 3, 0, 41, 80 << 20);
        Edit(doc, model, size / 2, 1 << 20, 42, 10);
        var snapshot = model.Clone();

        var gate = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, _) =>
        {
            if (step == "temp-written") gate.Wait();
        };
        var task = doc.SaveAsync();
        Edit(doc, model, 5, 0, 43, 70 << 20);
        Edit(doc, model, size, 100, 44, 3);
        gate.Set();
        Assert.True((await task).Saved);

        Assert.Equal(snapshot.Sha256(), HashFile(path));
        Assert.Equal(model.Sha256(), HashOf(doc));
        Assert.True(doc.IsModified);
        Assert.NotNull(doc.Undo());
        Assert.NotNull(doc.Undo());
        Assert.False(doc.IsModified);
        Assert.Equal(snapshot.Sha256(), HashOf(doc));
    }

    private sealed class Sink(Action<SaveProgress> report) : IProgress<SaveProgress>
    {
        public void Report(SaveProgress value) => report(value);
    }

    [Fact]
    public async Task A_streaming_save_reports_its_progress_in_order_up_to_the_size_of_the_saved_revision()
    {
        const long size = 4 << 20;
        string path = NewFile(size);
        using var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        Edit(doc, model, 1000, 10, 11, 1 << 20);
        long saved = model.Length;
        var reports = new List<SaveProgress>();
        doc.ProgressIntervalMs = 0; // a report after every MiB
        var task = doc.SaveAsync(progress: new Sink(p => { lock (reports) reports.Add(p); }));
        Edit(doc, model, 0, 0, 12, 3 << 20); // the document grows meanwhile; the report still counts the revision that is saved
        Assert.True((await task).Saved);

        Assert.True(reports.Count >= 4, $"{reports.Count} reports for {saved} bytes");
        Assert.All(reports, r => Assert.Equal(saved, r.Total));
        Assert.Equal(reports.Select(r => r.Written).Order(), reports.Select(r => r.Written));
        Assert.True(reports[0].Written > 0);
        Assert.Equal(saved, reports[^1].Written);
    }

    [Fact]
    public async Task Cancelling_from_a_progress_report_stops_the_write_and_leaves_the_original_and_the_edits()
    {
        const long size = 4 << 20;
        string path = NewFile(size);
        string original = HashFile(path);
        using var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        Edit(doc, model, 1000, 10, 11, 1 << 20);
        using var cancel = new CancellationTokenSource();
        doc.ProgressIntervalMs = 0;
        var task = doc.SaveAsync(cancellationToken: cancel.Token, progress: new Sink(p => { if (p.Written > 0) cancel.Cancel(); }));
        Edit(doc, model, 5000, 0, 13, 100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.Equal(original, HashFile(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Equal(model.Sha256(), HashOf(doc));
        Assert.True(doc.IsModified);
        Assert.False(doc.IsSaving);

        Assert.True((await doc.SaveAsync()).Saved); // the next save is not affected
        Assert.Equal(model.Sha256(), HashFile(path));
    }

    [Fact]
    public async Task Disposing_the_document_during_a_streaming_save_stops_the_save_and_leaves_the_file_whole()
    {
        const long size = 3 << 20;
        string path = NewFile(size);
        string original = HashFile(path);
        var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        Edit(doc, model, 100, 0, 21, 1 << 20);

        using var writing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, _) =>
        {
            if (step != "temp-writing") return; // on the worker, before the first byte
            writing.Set();
            release.Wait(20_000);
        };
        var task = doc.SaveAsync();
        Assert.True(writing.Wait(20_000));
        doc.Dispose();
        release.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.Equal(original, HashFile(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task A_streaming_save_to_another_path_writes_the_snapshot_there_and_leaves_the_old_file_and_the_edits()
    {
        const long size = 3 << 20;
        string path = NewFile(size);
        string original = HashFile(path);
        string copy = Path.Combine(_dir, "copy.txt");
        using var doc = Doc.Open(path, Factory());
        var model = new LargeModel(size);
        Edit(doc, model, 100, 0, 31, 1 << 20);
        var snapshot = model.Clone();

        var gate = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, _) =>
        {
            if (step == "temp-written") gate.Wait();
        };
        var task = doc.SaveAsync(copy);
        Edit(doc, model, 9000, 4, 32, 64);
        gate.Set();

        Assert.True((await task).Saved);
        Assert.Equal(snapshot.Sha256(), HashFile(copy));
        Assert.Equal(original, HashFile(path));
        Assert.Equal(Path.GetFullPath(copy), doc.FilePath);
        Assert.Equal(model.Sha256(), HashOf(doc));
        Assert.True(doc.IsModified);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    /// <summary>
    /// Runs <paramref name="body"/> on the calling thread and resumes its awaits there, like the UI thread of the app. A document is edited and swapped by one
    /// thread; without this the end of the save runs on a pool thread while the test thread still edits, and an edit meets the replaced file.
    /// </summary>
    private static void OnOwnerThread(Func<Task> body)
    {
        var previous = SynchronizationContext.Current;
        var context = new OwnerThreadContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = body();
            _ = task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
            context.Run();
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class OwnerThreadContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _work = new();

        public override void Post(SendOrPostCallback d, object? state) => _work.Add((d, state));

        public void Complete() => _work.CompleteAdding();

        public void Run()
        {
            foreach (var (callback, state) in _work.GetConsumingEnumerable()) callback(state);
        }
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Private_memory_stays_bounded_while_a_multi_GiB_save_streams_and_edits_continue() => OnOwnerThread(() => SaveWhileEditingAsync(3L << 30));

    /// <summary>
    /// The size of the file of P7.1.3. The memory of the machine does not decide the result: the save holds a few buffers and one digest per MiB, not the file. The file and the expected
    /// bytes come from the fast generator of <see cref="HugeText"/>. It needs about 110 GiB of free disk, so it runs only where <c>TEXTIDA_HUGE=1</c> is set. On the test machine the save alone
    /// takes longer than the 20 minutes a test gets there (it hashes the file twice while it writes, at about 130 MiB/s each), so it is the next test that runs there.
    /// </summary>
    [Fact]
    [Trait("Category", "Large")]
    public void A_50_GiB_file_is_edited_and_saved_while_editing_continues_and_the_file_equals_the_independent_snapshot()
    {
        if (!HugeText.Enabled)
        {
            output.WriteLine("skipped: TEXTIDA_HUGE is not 1");
            return;
        }
        OnOwnerThread(() => SaveWhileEditingAsync(HugeText.Size(50L << 30), fast: true));
    }

    /// <summary>The same at 20 GiB, which is more than the memory of the test machine (16 GiB), and a save that fits in the time a test gets there; the figures of the 50 GiB file follow from it.</summary>
    [Fact]
    [Trait("Category", "Large")]
    public void A_20_GiB_file_larger_than_the_memory_is_edited_and_saved_while_editing_continues_and_the_file_equals_the_independent_snapshot()
    {
        if (!HugeText.Enabled)
        {
            output.WriteLine("skipped: TEXTIDA_HUGE is not 1");
            return;
        }
        OnOwnerThread(() => SaveWhileEditingAsync(HugeText.Size(20L << 30), fast: true));
    }

    private void Say(string line)
    {
        output.WriteLine(line);
        HugeText.Log(line);
    }

    private async Task SaveWhileEditingAsync(long size, bool fast = false)
    {
        long room = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_dir))!).AvailableFreeSpace;
        Assert.True(room > 2 * size + size / 5, $"{room >> 30} GiB are free; a file of {size >> 30} GiB and its saved copy need {2 * size >> 30} GiB");
        FillBytes fill = fast ? HugeText.Fill : LargeText.Fill;
        var clock = Stopwatch.StartNew();
        string path = Path.Combine(_dir, "doc.txt");
        if (fast) HugeText.WriteFile(path, size);
        else path = NewFile(size);
        Say($"{size / 1073741824.0:F1} GiB file written in {clock.Elapsed.TotalSeconds:F0} s; the machine has {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes >> 20} MiB of memory");
        clock.Restart();
        // as the app opens a big file: the hash and the block digests come from a pass in the background, which reads the file while this test goes on
        using var doc = Doc.Open(path, Factory(PagedSpillPlaceholders.MemoryBudgetBytes), fast ? new DocumentOptions { HashInBackgroundAboveBytes = 64L << 20 } : null);
        Say($"opened in {clock.Elapsed.TotalSeconds:F1} s");
        var model = new LargeModel(size, fill);
        Edit(doc, model, size / 3, 0, 41, 80 << 20, fill); // spilled to the scratch file, not held
        Edit(doc, model, size / 2, 1 << 20, 42, 10, fill);
        var snapshot = model.Clone();
        // the independent SHA-256 of revision N, taken from the edit script and never from the product, runs on a core of its own while the save writes
        var snapshotHashing = Task.Run(() => snapshot.Sha256());

        GC.Collect();
        long before = Process.GetCurrentProcess().PrivateMemorySize64, peak = before, peakSet = 0;
        using var stop = new CancellationTokenSource();
        var watcher = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var self = Process.GetCurrentProcess();
                peak = Math.Max(peak, self.PrivateMemorySize64);
                peakSet = Math.Max(peakSet, self.WorkingSet64);
                Thread.Sleep(50);
            }
        });
        clock.Restart();
        int reported = -1;
        var task = doc.SaveAsync(progress: new Sink(p =>
        {
            int percent = (int)(p.Written * 100 / Math.Max(1, p.Total));
            if (percent / 5 != Interlocked.Exchange(ref reported, percent / 5)) Say($"save {percent} % after {clock.Elapsed.TotalSeconds:F0} s");
        }));
        int edits = 0;
        for (int i = 0; !task.IsCompleted; i++)
        {
            if (i < 300) { Edit(doc, model, 4096L * i, 0, 100 + i, 64, fill); edits++; } // typing and pasting while the file streams; few enough that the model stays small
            await Task.Delay(20);
        }
        Assert.True((await task).Saved);
        stop.Cancel();
        await watcher;

        long grown = peak - before;
        Say($"save of {size / 1073741824.0:F1} GiB with {edits} edits meanwhile: {clock.Elapsed.TotalSeconds:F0} s; private memory {before >> 20} MiB before, peak {peak >> 20} MiB (grew {grown >> 20} MiB, limit {PagedSpillPlaceholders.SaveMemoryGrowthBytes >> 20}); peak working set {peakSet >> 20} MiB");
        Assert.True(grown <= PagedSpillPlaceholders.SaveMemoryGrowthBytes, $"private memory grew by {grown >> 20} MiB during the save");
        // what the save hashed while it wrote against the independent hash of revision N, and the bytes of the file and of the editor against the edit script
        clock.Restart();
        string expected = await snapshotHashing, written = doc.Fingerprint!.Sha256;
        Say($"SHA-256 of the saved file {written} (taken while it was written) against the independent {expected}; waited {clock.Elapsed.TotalSeconds:F0} s for the second");
        Assert.Equal(expected, written);
        clock.Restart();
        var fileComparing = Task.Run(() => snapshot.FirstDifferenceInFile(path));
        var editorComparing = Task.Run(() => fast ? model.FirstDifferenceIn(doc) : (model.Sha256() == HashOf(doc) ? -1 : 0));
        Assert.Equal(-1, await fileComparing); // the file holds exactly revision N
        Assert.Equal(-1, await editorComparing); // the editor holds revision N plus what was typed meanwhile
        Say($"the file holds revision N and the editor revision N+{edits}, byte by byte: compared in {clock.Elapsed.TotalSeconds:F0} s");
        Assert.True(doc.IsModified);
        Assert.NotNull(doc.Undo()); // undo after the save
        Assert.True(doc.IsModified);
    }

    [Fact]
    [Trait("Category", "Large")]
    [Trait("Category", "Perf")]
    public void Edits_and_the_end_of_a_multi_GiB_streaming_save_do_not_hold_the_caller_longer_than_the_limits() => OnOwnerThread(EditsAndTheEndOfTheSaveAsync);

    private async Task EditsAndTheEndOfTheSaveAsync()
    {
        const long size = 3L << 30;
        string path = NewFile(size);
        using var doc = Doc.Open(path, Factory(PagedSpillPlaceholders.MemoryBudgetBytes));
        var model = new LargeModel(size);
        Edit(doc, model, size / 2, 0, 41, 1 << 20);
        var snapshot = model.Clone();

        long swapAt = 0, doneAt = 0;
        doc.StreamSaveStep = (step, _) =>
        {
            if (step == "before-swap") swapAt = Stopwatch.GetTimestamp();
            if (step == "saved") doneAt = Stopwatch.GetTimestamp();
        };
        long started = Stopwatch.GetTimestamp();
        var task = doc.SaveAsync();
        double callMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; // what the caller's thread does before the first wait
        var times = new List<double>();
        var bytes = new byte[1];
        for (int i = 0; !task.IsCompleted; i++)
        {
            if (i < 1500)
            {
                long at = 64L * (i * 7919L % (size / 64));
                LargeText.Fill(100 + i, 0, bytes);
                long t0 = Stopwatch.GetTimestamp();
                Assert.True(doc.Replace(new ByteRange(at, at), bytes).Applied);
                times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                model.Replace(at, 0, 100 + i, 1);
            }
            await Task.Delay(5);
        }
        Assert.True((await task).Saved);

        times.Sort();
        double p99 = times[(int)(times.Count * 0.99)];
        double stall = Stopwatch.GetElapsedTime(swapAt, doneAt).TotalMilliseconds;
        output.WriteLine($"Edits during a 3 GiB streaming save: {times.Count} edits, p50 {times[times.Count / 2]:0.0}, p95 {times[(int)(times.Count * 0.95)]:0.0}, p99 {p99:0.0}, max {times[^1]:0.0} ms; SaveAsync call {callMs:0.0} ms; swap to done {stall:0} ms");
        Assert.True(times.Count >= 100, $"only {times.Count} edits fitted in the save");
        Assert.True(callMs <= PagedSpillPlaceholders.SaveEditLatencyP99Ms, $"starting the save held the caller for {callMs:0.0} ms");
        Assert.True(p99 <= PagedSpillPlaceholders.SaveEditLatencyP99Ms, $"edit p99 {p99:0.0} ms (median {times[times.Count / 2]:0.0} ms)");
        Assert.True(stall <= PagedSpillPlaceholders.SaveCommitStallMs, $"the end of the save held the caller for {stall:0} ms");
        Assert.Equal(snapshot.Sha256(), HashFile(path));
        Assert.Equal(model.Sha256(), HashOf(doc));
    }
}
