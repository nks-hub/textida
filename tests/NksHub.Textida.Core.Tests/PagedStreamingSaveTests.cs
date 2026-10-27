using System.Security.Cryptography;
using CrashHarness;
using NksHub.Textida.Core.Documents;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P7.1.3: a paged document is saved by streaming an immutable snapshot to a temporary file and swapping it in, while the user keeps editing.
/// The expected bytes come from <see cref="LargeModel"/>, which generates them from the edit script and never reads the product.
/// </summary>
public sealed class PagedStreamingSaveTests : IDisposable
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
}
