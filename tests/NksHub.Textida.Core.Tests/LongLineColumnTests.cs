using System.Text;
using NksHub.Textida.Core.Documents;

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
    public void AFarReadOfAnEditedLineOnAnotherThreadMatchesTheDocumentAndItsPointsComeBack()
    {
        string line = LongLine(400_000);
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var doc = PagedDocument.Open(f.Path);
        doc.Insert(0, "12345");
        string edited = "12345" + line;
        Assert.False(doc.IsColumnNear(0, 3_000_000));

        var columns = doc.SnapshotColumns();
        string text = Task.Run(() => columns.ReadColumns(0, 3_000_000, 15)).Result;
        Assert.Equal(Expected(edited, 3_000_000, 15), text);
        Assert.False(doc.IsColumnNear(0, 3_000_000)); // the points stay with the snapshot until they are taken over

        doc.KeepColumns(columns);
        Assert.True(doc.IsColumnNear(0, 3_000_000));
        Assert.True(doc.IsColumnNear(0, 3_000_010));
        Assert.Equal(Expected(edited, 3_000_010, 15), doc.ReadColumns(0, 3_000_010, 15));
    }

    [Fact]
    public void ASnapshotReadKeepsItsTextButNotItsPointsWhenTheDocumentChangedMeanwhile()
    {
        string line = LongLine(400_000);
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var doc = PagedDocument.Open(f.Path);
        doc.Insert(0, "12345");
        var columns = doc.SnapshotColumns();
        doc.Insert(0, "ab"); // after the snapshot: its points describe text that has moved

        Assert.Equal(Expected("12345" + line, 3_000_000, 15), columns.ReadColumns(0, 3_000_000, 15));
        doc.KeepColumns(columns);
        Assert.False(doc.IsColumnNear(0, 3_000_000));
        Assert.Equal(Expected("ab12345" + line, 3_000_000, 15), doc.ReadColumns(0, 3_000_000, 15));
    }

    [Fact]
    public void AnUneditedDocumentReadsItsSnapshotFromTheFileAndNotesThePointsThere()
    {
        string line = LongLine(400_000);
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var doc = PagedDocument.Open(f.Path);
        var columns = doc.SnapshotColumns();
        Assert.Equal(Expected(line, 3_000_000, 15), Task.Run(() => columns.ReadColumns(0, 3_000_000, 15)).Result);
        Assert.True(doc.IsColumnNear(0, 3_000_000));
        Assert.True(doc.Source.IsColumnNear(0, 3_000_000));
    }

    [Fact]
    public void MergingAnIndexAddsItsPointsLengthsAndLastRead()
    {
        var index = new ColumnIndex();
        index.Add(0, 1_000_000, 1_500_000);
        var other = new ColumnIndex();
        other.Add(0, 2_000_000, 3_000_000);
        other.Add(7_000_000, 1_000_000, 8_000_000);
        other.SetLength(0, 2_500_000);
        other.SetLast(0, 2_400_000, 3_600_000);

        index.Merge(other);

        Assert.True(index.TryNearest(0, 2_100_000, out long column, out long position));
        Assert.Equal((2_000_000L, 3_000_000L), (column, position));
        Assert.True(index.TryNearest(0, 1_500_000, out column, out _));
        Assert.Equal(1_000_000, column);
        Assert.True(index.TryNearest(0, 2_450_000, out column, out position));
        Assert.Equal((2_400_000L, 3_600_000L), (column, position));
        Assert.True(index.TryNearest(7_000_000, 1_200_000, out column, out _));
        Assert.Equal(1_000_000, column);
        Assert.True(index.TryGetLength(0, out long length));
        Assert.Equal(2_500_000, length);
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

    private static int ByteOffset(string text, int column) => Encoding.UTF8.GetByteCount(text.AsSpan(0, column));

    /// <summary>A column on a character boundary near <paramref name="column"/> (never between the two halves of a surrogate pair).</summary>
    private static int Boundary(string text, int column) => column > 0 && column < text.Length && char.IsLowSurrogate(text[column]) ? column + 1 : column;

    [Fact]
    public void ColumnAndOffsetAgreeWithTheTextAfterManyEditsOfALongLine()
    {
        string line = LongLine(300_000) + "😀tail";
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var doc = PagedDocument.Open(f.Path);
        var random = new Random(7);
        string model = line;
        string[] pieces = ["x", "ě", "中", "😀", "abc", "ěš\tč", ""];

        void Check(params int[] columns)
        {
            foreach (int c in columns.Append(random.Next(model.Length)).Append(model.Length).Append(0))
            {
                int col = Boundary(model, Math.Min(c, model.Length));
                long offset = ByteOffset(model, col);
                Assert.Equal(col, doc.ColumnOf(0, offset));
                Assert.Equal(offset, doc.OffsetOfColumn(0, col));
                Assert.Equal(Expected(model, col, 12), doc.ReadColumns(0, col, 12));
            }
        }

        Check(1_000_000, 2_500_000);
        for (int step = 0; step < 40; step++)
        {
            int at = Boundary(model, random.Next(model.Length));
            int remove = step % 3 == 0 ? Boundary(model, Math.Min(model.Length, at + random.Next(30))) - at : 0;
            string insert = pieces[random.Next(pieces.Length)];
            long offset = ByteOffset(model, at);
            long removeBytes = ByteOffset(model, at + remove) - offset;
            doc.Replace(offset, removeBytes, Encoding.UTF8.GetBytes(insert));
            model = model.Remove(at, remove).Insert(at, insert);
            Check(at, at + insert.Length, at - 1, 1_048_576, 2_097_152, 2_500_000 + step);
        }
        while (doc.Undo()) { }
        model = line;
        Check(1_000_000, 2_500_000);
    }

    [Fact]
    public void ColumnAndOffsetWorkOnUtf16LinesWithABomBeforeAndAfterAnEdit()
    {
        string line = LongLine(150_000) + "😀";
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(line + "\nnext")];
        using var f = new TempFile(bytes);
        using var doc = PagedDocument.Open(f.Path, new MappedFileSourceOptions { Codec = Core.Encodings.TextCodec.Utf16LEBom });
        for (int i = 0; i < 200 && !doc.LineIndexState.Complete; i++) Thread.Sleep(10);
        long Offset(int col) => 2 + 2L * col;

        foreach (int col in new[] { 0, 7, 1_000_000, 1_200_001, line.Length })
        {
            Assert.Equal(col, doc.ColumnOf(2, Offset(col)));
            Assert.Equal(Offset(col), doc.OffsetOfColumn(2, col));
        }
        doc.Replace(Offset(1_200_000), 0, Encoding.Unicode.GetBytes("ab"));
        foreach (int col in new[] { 0, 7, 1_000_000, 1_200_000, 1_200_002, line.Length + 2 })
        {
            Assert.Equal(col, doc.ColumnOf(2, Offset(col)));
            Assert.Equal(Offset(col), doc.OffsetOfColumn(2, col));
        }
    }

    [Fact]
    public void AnEditKeepsTheColumnPointsBeforeItAndDropsTheOnesAfter()
    {
        var index = new ColumnIndex();
        index.Add(0, 1_000_000, 1_500_000);
        index.Add(0, 2_000_000, 3_000_000);
        index.Add(0, 3_000_000, 4_500_000);
        index.Add(9_000_000, 1_000_000, 9_000_100);
        index.SetLast(0, 3_000_005, 4_500_010);

        index.DropFrom(3_000_000);

        Assert.True(index.TryNearestAtPosition(0, 4_000_000, out long column, out long position));
        Assert.Equal((2_000_000L, 3_000_000L), (column, position));
        Assert.True(index.TryNearestAtPosition(0, 3_000_000, out column, out _));
        Assert.Equal(2_000_000, column);
        Assert.False(index.TryNearest(9_000_000, 5_000_000, out _, out _));
        Assert.False(index.TryNearestAtPosition(0, 4_600_000, out column, out _) && column > 2_000_000);

        var copy = new ColumnIndex();
        copy.CopyBefore(index, 3_500_000);
        Assert.Equal(2, copy.PointCount);
        Assert.True(copy.TryNearestAtPosition(0, 2_900_000, out column, out _));
        Assert.Equal(1_000_000, column);
    }

    [Fact]
    public void PositionsOfAnEditedLongLineComeFromTheColumnIndex()
    {
        string line = LongLine(300_000) + "😀";
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var backend = PagedBackend.FromFile(f.Path, NksHub.Textida.Core.Encodings.TextCodec.Utf8);
        for (int i = 0; i < 200 && !backend.LineIndexState.Complete; i++) Thread.Sleep(10);
        Assert.True(backend.LineIndexState.Complete);

        backend.Replace(ByteOffset(line, 2_000_000), 0, "ZZ"u8);
        string edited = line.Insert(2_000_000, "ZZ");
        foreach (int col in new[] { 0, 5, 1_999_999, 2_000_000, 2_000_002, edited.Length })
        {
            long offset = ByteOffset(edited, Boundary(edited, col));
            Assert.True(backend.TryGetPosition(offset, out var position));
            Assert.Equal(new LinePosition(0, Boundary(edited, col)), position);
            Assert.True(backend.TryGetOffset(position, out long back));
            Assert.Equal(offset, back);
        }
        Assert.False(backend.TryGetOffset(new LinePosition(0, edited.Length + 1), out _));
        Assert.False(backend.TryGetOffset(new LinePosition(0, edited.Length - 1), out _)); // between the halves of the surrogate pair
    }

    [Fact]
    public void AColdLongLineIsFarFromItsMiddleAndNearAfterTheColumnsWereCounted()
    {
        string line = LongLine(400_000);
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var doc = PagedDocument.Open(f.Path, new MappedFileSourceOptions { StartIndexing = false });
        const long within = 1 << 20;
        long offset = ByteOffset(line, 3_000_000);

        Assert.True(doc.IsOffsetNear(0, 100, within));
        Assert.True(doc.IsColumnNear(0, 100, within));
        Assert.False(doc.IsOffsetNear(0, offset, within));
        Assert.False(doc.IsColumnNear(0, 3_000_000, within));

        Assert.Equal(3_000_000, doc.ColumnOf(0, offset));
        Assert.True(doc.IsOffsetNear(0, offset, within));
        Assert.True(doc.IsOffsetNear(0, offset + 5000, within));
        Assert.True(doc.IsColumnNear(0, 3_000_000, within));
        Assert.False(doc.IsOffsetNear(0, ByteOffset(line, 3_500_000), 1000)); // a point 500 000 columns back is not near for a limit of 1000
    }

    [Fact]
    public void ASnapshotOfTheColumnsCountsLikeTheDocumentAndHandsItsPointsBack()
    {
        string line = LongLine(400_000);
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var doc = PagedDocument.Open(f.Path, new MappedFileSourceOptions { StartIndexing = false });
        const long within = 1 << 20;
        long offset = ByteOffset(line, 2_500_000);

        var cold = doc.SnapshotColumns();
        Assert.Equal(2_500_000, cold.ColumnOf(0, offset));
        Assert.Equal(offset, cold.OffsetOfColumn(0, 2_500_000));
        Assert.True(doc.IsOffsetNear(0, offset, within)); // an unedited document shares the index of its file, so the points are there at once

        doc.Insert(ByteOffset(line, 1_000_000), "ZZ"u8);
        string edited = line.Insert(1_000_000, "ZZ");
        long editedOffset = ByteOffset(edited, 3_000_000);
        Assert.False(doc.IsOffsetNear(0, editedOffset, within));

        var snapshot = doc.SnapshotColumns();
        Assert.Equal(3_000_000, snapshot.ColumnOf(0, editedOffset));
        Assert.Equal(editedOffset, snapshot.OffsetOfColumn(0, 3_000_000));
        Assert.False(doc.IsOffsetNear(0, editedOffset, within)); // the points are in the copy of the snapshot until the document takes them
        doc.KeepColumns(snapshot);
        Assert.True(doc.IsOffsetNear(0, editedOffset, within));
        Assert.Equal(3_000_000, doc.ColumnOf(0, editedOffset));

        doc.Insert(ByteOffset(edited, 10), "Y"u8); // an edit near the start drops the points behind it
        string current = edited.Insert(10, "Y");
        long far = ByteOffset(current, 3_000_000);
        Assert.False(doc.IsOffsetNear(0, far, within));

        var stale = doc.SnapshotColumns();
        Assert.Equal(3_000_000, stale.ColumnOf(0, far));
        doc.Delete(ByteOffset(current, 3), 1); // an edit after the snapshot: the points it noted do not describe the text any more
        doc.KeepColumns(stale);
        Assert.False(doc.IsOffsetNear(0, far, within));
    }

    [Fact]
    [Trait("Category", "Large")]
    public void TypingInAHundredMiBLineKeepsTheColumnsRightAndTheAllocationsSmall()
    {
        const int size = 100 * 1024 * 1024;
        var bytes = new byte[size];
        for (int i = 0; i < size; i++) bytes[i] = (byte)('a' + i * 7 % 26);
        using var f = new TempFile(bytes);
        using var doc = PagedDocument.Open(f.Path, new MappedFileSourceOptions { StartIndexing = false });
        long caret = 50_000_000;
        Assert.Equal(caret, doc.ColumnOf(0, caret)); // cold: counts the line once and notes points

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
        {
            doc.Replace(caret, 0, "é"u8);
            caret += 2;
            Assert.Equal(50_000_000 + i + 1, doc.ColumnOf(0, caret));
            Assert.Equal(1, doc.ReadColumns(0, 50_000_000 + i, 1).Length);
        }
        for (int i = 0; i < 100; i++)
        {
            doc.Replace(caret - 2, 2, default);
            caret -= 2;
            Assert.Equal(50_000_000 + 299 - i, doc.ColumnOf(0, caret));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 32 << 20, $"allocated {allocated >> 20} MiB for 400 edits");
        Assert.Equal(size + 200 * 2, doc.ByteLength);
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

        // this thread only: tests of other classes allocate in the same process at the same time
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (long col in new long[] { 0, 50_000_000, 90_000_000 })
            Assert.Equal(string.Concat(Enumerable.Range(0, count).Select(k => At(col + k))), src.ReadColumns(0, col, count));
        Assert.Equal("", src.ReadColumns(0, long.MaxValue, 0));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 16 << 20, $"allocated {allocated >> 20} MiB");

        Assert.InRange(src.Columns.PointCount, 90, 110);
        Assert.True(src.Columns.TryNearest(0, 90_000_000, out long known, out long position));
        Assert.InRange(90_000_000 - known, 0, ColumnIndex.Spacing);
        Assert.Equal(known, position);
    }
}
