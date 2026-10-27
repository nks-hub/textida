using System.Text;

namespace NksHub.Textida.Core.Tests;

/// <summary>P7.1.5: an edit inside a large inserted block splits it and the line counts of both halves are measured; that must not cost a megabyte of garbage each time.</summary>
public class PagedPieceSplitTests
{
    [Fact]
    public void Splitting_a_pasted_block_again_and_again_does_not_allocate_a_megabyte_each_time()
    {
        using var file = new TempFile(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 2000).Select(i => $"row {i}\n"))));
        using var doc = PagedDocument.Open(file.Path);
        for (int i = 0; i < 400 && !doc.LineIndexState.Complete; i++) Thread.Sleep(10);
        Assert.True(doc.LineIndexState.Complete);

        var block = new byte[4 << 20];
        for (int i = 0; i < block.Length; i++) block[i] = (byte)(i % 61 == 0 ? '\n' : 'x');
        doc.Insert(10, block);
        Assert.True(doc.LineIndexState.Complete); // reading the state of an edited document starts the line counts of the pieces
        doc.Insert(10 + 100_000, "a"u8);
        doc.Insert(10 + 200_000, "a"u8); // the pool has its array now

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 30; i++) doc.Insert(10 + 300_000 + i * 1000, "a"u8);
        long perInsert = (GC.GetAllocatedBytesForCurrentThread() - before) / 30;

        Assert.True(perInsert < 64 << 10, $"{perInsert} bytes per insert");
        long expectedBreaks = 2000 + (block.Length + 60) / 61;
        Assert.Equal(expectedBreaks + 1, doc.LineIndexState.Lines);
    }

    [Fact]
    public void A_carriage_return_at_the_end_of_a_block_of_a_long_inserted_piece_is_one_line_break()
    {
        using var file = new TempFile("start\n"u8.ToArray());
        using var doc = PagedDocument.Open(file.Path);
        for (int i = 0; i < 400 && !doc.LineIndexState.Complete; i++) Thread.Sleep(10);
        Assert.True(doc.LineIndexState.Complete);

        // 1 MiB + 1 byte: the second block of the scanner is the last byte alone, a CR; the LF that follows stands in the next piece. The LF at index 1 sits in the
        // first block, at the place where a block that is read into a longer buffer would still see it behind that CR
        var block = new byte[(1 << 20) + 1];
        Array.Fill(block, (byte)'x');
        block[1] = (byte)'\n';
        block[^1] = (byte)'\r';
        doc.Insert(0, block);
        doc.Insert(block.Length, "\nend"u8);
        var (lines, complete) = doc.LineIndexState;

        Assert.True(complete);
        Assert.Equal(4, lines); // the LF at index 1, the CR and the LF after it as one break, and the LF that ends "start": three breaks
    }
}
