using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Tests;

namespace NksHub.Textida.Core.Tests;

/// <summary>P7.1.2: inserted text and undo history of a paged document spill to disk, so memory stays at the budget however much is inserted.</summary>
[Collection("Process memory")]
public sealed class PagedSpillTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-spill-test-").FullName;

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers(); // snapshots may still hold a delete-on-close spill file
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private MappedFileSourceOptions Options(long budget, string? spill = null) => new() { SpillBudgetBytes = budget, SpillDirectory = spill ?? Path.Combine(_dir, "spill"), StartIndexing = false };

    private static byte[] Block(int index, int length)
    {
        var bytes = new byte[length];
        new Random(index).NextBytes(bytes);
        return bytes;
    }

    private static string HashOf(PagedDocument doc)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        for (long at = 0; at < doc.ByteLength;)
        {
            int n = doc.Read(at, buffer);
            sha.AppendData(buffer, 0, n);
            at += n;
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private static string HashOfBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Fact]
    public void Inserting_far_more_than_the_budget_stays_in_the_budget_and_undo_and_redo_restore_every_byte()
    {
        const int blocks = 2048, blockSize = 8192; // 16 MiB against a 64 KiB budget
        byte[] original = Encoding.ASCII.GetBytes("start\nmiddle\nend\n");
        var options = Options(64 << 10);
        using var doc = PagedDocument.OpenMemory(original, options);
        doc.ReplaceRepeated(0, 0, "ab"u8, 1000); // a pattern piece ends up in the spilled undo records
        string patterned = HashOfBytes([.. Enumerable.Range(0, 1000).Select(i => (byte)(i % 2 == 0 ? 'a' : 'b')), .. original]);
        Assert.Equal(patterned, HashOf(doc));
        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);

        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        expected.AppendData(Enumerable.Range(0, 1000).Select(i => (byte)(i % 2 == 0 ? 'a' : 'b')).Concat(original).ToArray());
        for (int i = 0; i < blocks; i++)
        {
            var block = Block(i, blockSize);
            doc.Insert(doc.ByteLength, block);
            expected.AppendData(block);
        }
        long grown = GC.GetTotalMemory(forceFullCollection: true) - before;

        Assert.True(grown < 4 << 20, $"16 MiB were inserted and managed memory grew by {grown} bytes.");
        Assert.True(Directory.GetFiles(options.SpillDirectory!, "*.spill").Length >= 2, "both the add buffer and the undo history should be on disk");
        Assert.Equal(1000 + original.Length + (long)blocks * blockSize, doc.ByteLength);
        Assert.Equal(Convert.ToHexString(expected.GetHashAndReset()), HashOf(doc));

        string full = HashOf(doc);
        int undone = 0;
        while (doc.CanUndo && undone < blocks)
        {
            Assert.True(doc.Undo());
            undone++;
        }
        Assert.Equal(1000 + original.Length, doc.ByteLength);
        Assert.Equal(patterned, HashOf(doc));
        while (doc.CanRedo) Assert.True(doc.Redo());
        Assert.Equal(full, HashOf(doc));

        // the spilled history reaches the bottom: the pattern step is still there
        while (doc.CanUndo) doc.Undo();
        Assert.Equal(HashOfBytes(original), HashOf(doc));
    }

    [Fact]
    public void The_text_read_back_from_the_spill_equals_the_text_that_went_in()
    {
        const int blocks = 256, blockSize = 4096;
        var options = Options(32 << 10);
        using var doc = PagedDocument.OpenMemory([], options);
        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int i = 0; i < blocks; i++)
        {
            var block = Block(i, blockSize);
            doc.Insert(doc.ByteLength, block);
            expected.AppendData(block);
        }
        Assert.Equal(Convert.ToHexString(expected.GetHashAndReset()), HashOf(doc));
        var snapshot = doc.CreateSnapshot();
        doc.Undo();
        var read = new byte[blockSize];
        Assert.Equal(blockSize, snapshot.Read((blocks - 1L) * blockSize, read));
        Assert.Equal(Block(blocks - 1, blockSize), read);
    }

    [Fact]
    public void Line_starts_inside_one_huge_inserted_piece_are_exact_across_scan_blocks()
    {
        // a 17-byte line puts a CR on the last byte of the first 1 MiB scan block and its LF on the first byte of the next
        const int lines = 180_000;
        var line = Encoding.ASCII.GetBytes("123456789012345\r\n");
        var text = new byte[lines * line.Length];
        for (int i = 0; i < lines; i++) line.CopyTo(text, i * line.Length);
        using var doc = PagedDocument.OpenMemory("tail\n"u8, new MappedFileSourceOptions { SpillBudgetBytes = 64 << 10, SpillDirectory = Path.Combine(_dir, "lines"), StartIndexing = true });
        var wait = Stopwatch.StartNew();
        while (!doc.Source.LineIndexState.Complete && wait.Elapsed < TimeSpan.FromSeconds(20)) Thread.Sleep(5);
        doc.Insert(0, text);

        Assert.Equal((lines + 2L, true), doc.LineIndexState);
        foreach (long k in new long[] { 0, 1, 61_680, 61_681, 61_682, 123_361, lines - 1, lines })
        {
            Assert.True(doc.TryGetLineStart(k, out long at));
            Assert.Equal(k * line.Length, at);
        }
    }

    [Fact]
    public void A_spill_file_that_cannot_be_created_refuses_the_edit_and_leaves_the_document_whole()
    {
        string blocker = Path.Combine(_dir, "not-a-folder");
        File.WriteAllText(blocker, "x");
        byte[] original = Block(1, 5000);
        using var doc = PagedDocument.OpenMemory(original, Options(1 << 10, Path.Combine(blocker, "spill")));
        doc.Insert(0, Block(2, 100));
        string before = HashOf(doc);

        Assert.ThrowsAny<IOException>(() => doc.Insert(0, Block(3, 4096)));
        Assert.Equal(before, HashOf(doc));
        Assert.True(doc.Undo());
        Assert.Equal(HashOfBytes(original), HashOf(doc));
    }

    [Fact]
    public void A_killed_process_leaves_a_spill_file_that_the_next_start_removes_and_the_original_is_untouched()
    {
        string spill = Path.Combine(_dir, "orphans");
        Directory.CreateDirectory(spill);
        string orphan = Path.Combine(spill, $"{int.MaxValue}-{Guid.NewGuid():N}-add.spill");
        File.WriteAllBytes(orphan, Block(9, 10_000)); // what a dead process left half written
        string alive = Path.Combine(spill, $"{Environment.ProcessId}-{Guid.NewGuid():N}-add.spill");
        File.WriteAllBytes(alive, [1]);

        string original = Path.Combine(_dir, "original.txt");
        File.WriteAllBytes(original, Block(4, 20_000));
        string hash = HashOfBytes(File.ReadAllBytes(original));
        using (var doc = PagedDocument.Open(original, Options(1 << 10, spill)))
        {
            doc.Insert(5, Block(5, 8192)); // spills; the sweep runs when the first spill file is made
            doc.Insert(0, Block(6, 8192));
            Assert.False(File.Exists(orphan));
            Assert.True(File.Exists(alive), "a file of a live process is not touched");
        }
        Assert.Equal(hash, HashOfBytes(File.ReadAllBytes(original)));
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Inserting_over_2_GiB_and_undoing_it_stays_under_1_GiB_of_private_memory_and_restores_the_bytes()
    {
        const int blockSize = 4 << 20;
        int blocks = Math.Max(1, (int)(HugeText.SizePast(2L << 30, 3L << 30) / blockSize)); // 513 blocks, 3 GiB (768 blocks) with TEXTIDA_HUGE=1
        byte[] original = Encoding.ASCII.GetBytes("start\nend\n");
        var options = Options(PagedSpillPlaceholders.MemoryBudgetBytes);
        using var doc = PagedDocument.OpenMemory(original, options);
        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        expected.AppendData(original);
        var block = new byte[blockSize];
        long peak = 0;
        for (int i = 0; i < blocks; i++)
        {
            new Random(i).NextBytes(block);
            doc.Insert(doc.ByteLength, block);
            expected.AppendData(block);
            if (i % 32 == 0) peak = Math.Max(peak, Process.GetCurrentProcess().PrivateMemorySize64);
        }
        Assert.Equal(original.Length + (long)blocks * blockSize, doc.ByteLength);
        Assert.Equal(Convert.ToHexString(expected.GetHashAndReset()), HashOf(doc));
        Assert.True(peak < 1L << 30, $"private memory reached {peak} bytes");

        while (doc.CanUndo) doc.Undo();
        Assert.Equal(HashOfBytes(original), HashOf(doc));
        while (doc.CanRedo) doc.Redo();
        Assert.Equal(original.Length + (long)blocks * blockSize, doc.ByteLength);
    }
}
