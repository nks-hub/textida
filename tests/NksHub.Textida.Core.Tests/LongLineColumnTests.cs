using System.Text;

namespace NksHub.Textida.Core.Tests;

public class LongLineColumnTests
{
    private const string Unit = "ěšč abc 中";

    private static string LongLine(int repeats) => string.Concat(Enumerable.Repeat(Unit, repeats));

    private static string Expected(string line, long col, int count) =>
        line.Substring((int)Math.Min(col, line.Length), (int)Math.Min(count, Math.Max(0, line.Length - col)));

    /// <summary>Runs the reader over a byte array and counts the windows it asked for.</summary>
    private static (string Text, int Windows) Read(byte[] bytes, MappedFileSource src, ColumnIndex index, long col, int count)
    {
        int windows = 0;
        string text = ColumnReader.Read((pos, max) =>
        {
            windows++;
            return bytes.AsSpan((int)pos, (int)Math.Min(max, bytes.Length - pos));
        }, bytes.Length, src.Codec, src.BomLength, index, 0, col, count, CancellationToken.None);
        return (text, windows);
    }

    [Fact]
    public void JumpAfterAnEarlierJumpDoesNotRescanFromTheLineStart()
    {
        string line = LongLine(400_000);
        var bytes = Encoding.UTF8.GetBytes(line + "\nnext");
        using var f = new TempFile(bytes);
        using var src = MappedFileSource.Open(f.Path);
        var index = new ColumnIndex();

        var (text, cold) = Read(bytes, src, index, 3_000_000, 20);
        Assert.Equal(Expected(line, 3_000_000, 20), text);
        Assert.True(cold > 2);

        var (near, scrolled) = Read(bytes, src, index, 3_000_100, 20);
        Assert.Equal(Expected(line, 3_000_100, 20), near);
        Assert.True(scrolled <= 2, $"scrolling right asked for {scrolled} windows");

        var (back, jumped) = Read(bytes, src, index, 2_200_007, 20);
        Assert.Equal(Expected(line, 2_200_007, 20), back);
        Assert.True(jumped <= 4, $"jumping back asked for {jumped} windows");
    }

    [Fact]
    public void BuildingTheIndexOfAWholeLineMakesEveryLaterJumpCheap()
    {
        string line = LongLine(500_000);
        var bytes = Encoding.UTF8.GetBytes(line);
        using var f = new TempFile(bytes);
        using var src = MappedFileSource.Open(f.Path);

        Assert.Equal("", src.ReadColumns(0, long.MaxValue, 0));
        var index = src.Columns;
        Assert.InRange(index.PointCount, 2, 6);

        foreach (long col in new long[] { 0, 1, 1_048_575, 1_048_576, 2_999_999, 4_000_000, 4_499_990 })
        {
            var (text, windows) = Read(bytes, src, index, col, 20);
            Assert.Equal(Expected(line, col, 20), text);
            Assert.True(windows <= 4, $"column {col} asked for {windows} windows");
            Assert.Equal(Expected(line, col, 20), src.ReadColumns(0, col, 20));
        }
    }

    [Fact]
    public void ReachingTheEndOfALineNotesItsLengthInCharactersNotBytes()
    {
        string line = LongLine(30_000) + "😀";
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var src = MappedFileSource.Open(f.Path);
        Assert.False(src.TryGetLineColumns(0, out _));
        Assert.False(src.IsColumnNear(0, 2_000_000));

        src.ReadColumns(0, long.MaxValue, 0);
        Assert.True(src.TryGetLineColumns(0, out long columns));
        Assert.Equal(line.Length, columns);
        Assert.True(src.IsColumnNear(0, 5));
    }

    [Fact]
    public void ColumnPointsStayWithinTheirBound()
    {
        var index = new ColumnIndex();
        for (long line = 0; line < 40; line++)
            for (long k = 1; k <= 1000; k++) index.Add(line * 1_000_000, k * ColumnIndex.Spacing, k * 777);
        Assert.True(index.PointCount <= ColumnIndex.MaxPoints + 1);

        long newest = 39 * 1_000_000;
        Assert.True(index.TryNearest(newest, 500 * ColumnIndex.Spacing + 5, out long column, out long position));
        Assert.Equal(500 * ColumnIndex.Spacing, column);
        Assert.Equal(500 * 777, position);
        Assert.False(index.TryNearest(0, 10 * ColumnIndex.Spacing, out _, out _));
    }

    [Fact]
    public void EditsDropTheNotedPointsSoTheWindowShowsTheNewText()
    {
        string line = LongLine(400_000);
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var doc = PagedDocument.Open(f.Path);
        Assert.Equal(Expected(line, 2_500_000, 15), doc.ReadColumns(0, 2_500_000, 15));
        Assert.Equal(Expected(line, 2_500_010, 15), doc.ReadColumns(0, 2_500_010, 15));

        doc.Insert(0, "12345");
        string edited = "12345" + line;
        foreach (long col in new long[] { 2_500_000, 2_500_010, 3_100_000, 0 })
            Assert.Equal(Expected(edited, col, 15), doc.ReadColumns(0, col, 15));

        const int at = 9 * 200_000 + 5;
        doc.Insert(Encoding.UTF8.GetByteCount(edited.AsSpan(0, at)), "|MIDDLE|");
        string middle = edited.Insert(at, "|MIDDLE|");
        foreach (long col in new long[] { 2_500_000, at - 3, 3_100_000 })
            Assert.Equal(Expected(middle, col, 15), doc.ReadColumns(0, col, 15));

        Assert.True(doc.Undo());
        Assert.True(doc.Undo());
        Assert.Equal(Expected(line, 2_500_000, 15), doc.ReadColumns(0, 2_500_000, 15));
    }

    [Fact]
    public void ChangingTheCodecDropsTheNotedPoints()
    {
        var bytes = Encoding.UTF8.GetBytes(LongLine(400_000));
        using var f = new TempFile(bytes);
        using var src = MappedFileSource.Open(f.Path);
        src.ReadColumns(0, 3_000_000, 5);
        Assert.True(src.Columns.PointCount > 0);
        src.SetCodec(src.Codec);
        Assert.Equal(0, src.Columns.PointCount);
    }

    [Fact]
    [Trait("Category", "Large")]
    public void HundredMiBLineShowsWindowsAtFarColumnsWithoutMaterializingTheLine()
    {
        const int size = 100 * 1024 * 1024;
        static char At(long i) => (char)('a' + i * 7 % 26);
        var bytes = new byte[size];
        for (int i = 0; i < size; i++) bytes[i] = (byte)At(i);
        using var f = new TempFile(bytes);
        using var src = MappedFileSource.Open(f.Path, new MappedFileSourceOptions { StartIndexing = false });
        const int count = 200;

        long before = GC.GetTotalAllocatedBytes(precise: true);
        foreach (long col in new long[] { 0, 50_000_000, 90_000_000 })
            Assert.Equal(string.Concat(Enumerable.Range(0, count).Select(k => At(col + k))), src.ReadColumns(0, col, count));
        Assert.Equal("", src.ReadColumns(0, long.MaxValue, 0));
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        Assert.True(allocated < 16 << 20, $"allocated {allocated >> 20} MiB");

        Assert.InRange(src.Columns.PointCount, 90, 110);
        Assert.True(src.Columns.TryNearest(0, 90_000_000, out long known, out long position));
        Assert.InRange(90_000_000 - known, 0, ColumnIndex.Spacing);
        Assert.Equal(known, position);
    }
}
