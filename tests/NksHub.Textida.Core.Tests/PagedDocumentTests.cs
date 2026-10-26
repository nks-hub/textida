namespace NksHub.Textida.Core.Tests;

public class PagedDocumentTests
{
    private static byte[] Snapshot(PagedDocument d)
    {
        var b = new byte[d.ByteLength];
        Assert.Equal(b.Length, d.Read(0, b));
        return b;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomEditsMatchReference(int seed)
    {
        var r = new Random(seed);
        var original = Gen.RandomBytes(r, 3000);
        using var tmp = new TempFile(original);
        using var doc = PagedDocument.Open(tmp.Path);

        var reference = original.ToList();
        var undo = new Stack<List<byte>>();
        var redo = new Stack<List<byte>>();

        for (int step = 0; step < 4000; step++)
        {
            int op = r.Next(10);
            if (op < 4)
            {
                int at = r.Next(reference.Count + 1);
                var ins = Gen.RandomBytes(r, r.Next(1, 40));
                undo.Push([.. reference]);
                redo.Clear();
                reference.InsertRange(at, ins);
                doc.Insert(at, ins);
            }
            else if (op < 7 && reference.Count > 0)
            {
                int at = r.Next(reference.Count);
                int len = r.Next(1, Math.Min(60, reference.Count - at) + 1);
                undo.Push([.. reference]);
                redo.Clear();
                reference.RemoveRange(at, len);
                doc.Delete(at, len);
            }
            else if (op < 8 && reference.Count > 0)
            {
                int at = r.Next(reference.Count);
                int len = r.Next(0, Math.Min(30, reference.Count - at) + 1);
                var ins = Gen.RandomBytes(r, r.Next(0, 20));
                if (len == 0 && ins.Length == 0) continue;
                undo.Push([.. reference]);
                redo.Clear();
                reference.RemoveRange(at, len);
                reference.InsertRange(at, ins);
                doc.Replace(at, len, ins);
            }
            else if (op < 9)
            {
                Assert.Equal(undo.Count > 0, doc.Undo());
                if (undo.Count > 0) { redo.Push([.. reference]); reference = undo.Pop(); }
            }
            else
            {
                Assert.Equal(redo.Count > 0, doc.Redo());
                if (redo.Count > 0) { undo.Push([.. reference]); reference = redo.Pop(); }
            }

            Assert.Equal(reference.Count, doc.ByteLength);
            if (step % 40 == 0) Assert.Equal(reference.ToArray(), Snapshot(doc));
            if (reference.Count > 0)
            {
                int at = r.Next(reference.Count);
                var buf = new byte[r.Next(1, 100)];
                int n = doc.Read(at, buf);
                Assert.Equal(reference.Skip(at).Take(buf.Length).ToArray(), buf[..n]);
            }
        }
        Assert.Equal(reference.ToArray(), Snapshot(doc));
    }

    [Fact]
    public void UndoAllRestoresOriginalAndRedoAllRestoresEdited()
    {
        var r = new Random(77);
        var original = Gen.RandomBytes(r, 500);
        using var tmp = new TempFile(original);
        using var doc = PagedDocument.Open(tmp.Path);
        for (int i = 0; i < 200; i++)
        {
            if (r.Next(2) == 0) doc.Insert(r.Next((int)doc.ByteLength + 1), Gen.RandomBytes(r, 5));
            else if (doc.ByteLength > 10) doc.Delete(r.Next((int)doc.ByteLength - 5), 5);
        }
        var edited = Snapshot(doc);
        while (doc.Undo()) { }
        Assert.Equal(original, Snapshot(doc));
        Assert.False(doc.IsModified);
        while (doc.Redo()) { }
        Assert.Equal(edited, Snapshot(doc));
    }

    [Fact]
    public void EmptyFileAcceptsInsert()
    {
        using var tmp = new TempFile();
        using var doc = PagedDocument.Open(tmp.Path);
        Assert.Equal(0, doc.ByteLength);
        doc.Insert(0, "ahoj\nsvět");
        Assert.Equal("ahoj\nsvět", System.Text.Encoding.UTF8.GetString(Snapshot(doc)));
        var lines = doc.ReadLinesAtAsync(0, 10).AsTask().Result;
        Assert.Equal(["ahoj", "svět"], lines);
    }

    [Fact]
    public void OutOfRangeEditsThrow()
    {
        using var tmp = new TempFile("abc"u8.ToArray());
        using var doc = PagedDocument.Open(tmp.Path);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Insert(4, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.Delete(2, 5));
    }

    [Fact]
    public async Task SaveToNewFileIsByteExactAndLeavesDocumentBound()
    {
        var r = new Random(5);
        var original = Gen.RandomBytes(r, 20000);
        using var tmp = new TempFile(original);
        using var doc = PagedDocument.Open(tmp.Path);
        var reference = original.ToList();
        for (int i = 0; i < 300; i++)
        {
            int at = r.Next(reference.Count + 1);
            if (r.Next(2) == 0) { var ins = Gen.RandomBytes(r, r.Next(1, 50)); reference.InsertRange(at, ins); doc.Insert(at, ins); }
            else if (reference.Count - at > 0) { int len = r.Next(1, Math.Min(80, reference.Count - at) + 1); reference.RemoveRange(at, len); doc.Delete(at, len); }
        }
        string outPath = tmp.Path + ".out";
        try
        {
            await doc.SaveAsync(outPath);
            Assert.Equal(reference.ToArray(), await File.ReadAllBytesAsync(outPath));
            Assert.Equal(original, await File.ReadAllBytesAsync(tmp.Path));
            Assert.True(doc.IsModified);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(outPath)!, Path.GetFileName(tmp.Path) + "*.tmp"));
        }
        finally { File.Delete(outPath); }
    }

    [Fact]
    public async Task SaveInPlaceRemapsAndAllowsFurtherEdits()
    {
        var r = new Random(6);
        var original = Gen.RandomBytes(r, 10000);
        using var tmp = new TempFile(original);
        using var doc = PagedDocument.Open(tmp.Path);
        var reference = original.ToList();
        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < 50; i++)
            {
                int at = r.Next(reference.Count + 1);
                var ins = Gen.RandomBytes(r, r.Next(1, 30));
                reference.InsertRange(at, ins);
                doc.Insert(at, ins);
                int d = r.Next(reference.Count - 10);
                reference.RemoveRange(d, 5);
                doc.Delete(d, 5);
            }
            await doc.SaveAsync();
            Assert.False(doc.IsModified);
            Assert.Equal(reference.ToArray(), await File.ReadAllBytesAsync(tmp.Path));
            Assert.Equal(reference.ToArray(), Snapshot(doc));
        }
    }

    private static List<long> NaiveLineStarts(byte[] b)
    {
        var starts = new List<long> { 0 };
        for (int i = 0; i < b.Length; i++)
            if (b[i] == '\n' || (b[i] == '\r' && (i + 1 == b.Length || b[i + 1] != '\n'))) starts.Add(i + 1);
        return starts;
    }

    [Theory]
    [InlineData(1, 64)]
    [InlineData(2, 1 << 20)]
    [InlineData(3, 7)]
    public async Task LineStartsAfterEditsMatchRescan(int seed, int blockSize)
    {
        var r = new Random(seed);
        var original = System.Text.Encoding.UTF8.GetBytes(Gen.LineSoup(r, 3000));
        using var tmp = new TempFile(original);
        using var doc = PagedDocument.Open(tmp.Path, new MappedFileSourceOptions { IndexBlockSize = blockSize });
        await doc.Source.IndexTask;
        byte[][] inserts = ["\r"u8.ToArray(), "\n"u8.ToArray(), "\r\n"u8.ToArray(), "x"u8.ToArray(), "a\rb\nc"u8.ToArray(), "\n\r"u8.ToArray()];
        for (int step = 0; step < 1500; step++)
        {
            int op = r.Next(10);
            if (op < 5) doc.Insert(r.NextInt64(doc.ByteLength + 1), inserts[r.Next(inserts.Length)]);
            else if (op < 8 && doc.ByteLength > 0)
            {
                long at = r.NextInt64(doc.ByteLength);
                doc.Delete(at, Math.Min(r.Next(1, 4), doc.ByteLength - at));
            }
            else if (op < 9) doc.Undo();
            else doc.Redo();

            if (step % 25 != 0 && step != 1499) continue;
            var starts = NaiveLineStarts(Snapshot(doc));
            Assert.Equal((starts.Count, true), doc.LineIndexState);
            for (int line = 0; line < starts.Count; line++)
            {
                Assert.True(doc.TryGetLineStart(line, out long off), $"step {step} line {line}");
                Assert.Equal(starts[line], off);
            }
            Assert.False(doc.TryGetLineStart(starts.Count, out _));
        }
    }

    [Fact]
    public void EditedLineStartsWaitForBaseIndex()
    {
        using var tmp = new TempFile("a\nb\nc"u8.ToArray());
        using var doc = PagedDocument.Open(tmp.Path, new MappedFileSourceOptions { StartIndexing = false });
        doc.Insert(1, "\n"u8);
        Assert.False(doc.TryGetLineStart(1, out _));
        Assert.False(doc.LineIndexState.Complete);
        Assert.Equal(["a", "", "b", "c"], doc.ReadLines(0, 10));
    }

    [Fact]
    public void ReadLinesFromEditedDocumentMatchesText()
    {
        var text = "první\r\ndruhý\ntřetí\rčtvrtý";
        using var tmp = new TempFile(System.Text.Encoding.UTF8.GetBytes(text));
        using var doc = PagedDocument.Open(tmp.Path);
        doc.Insert(0, "nový\n");
        doc.Delete(doc.ByteLength - 3, 3);
        var lines = doc.ReadLinesAtAsync(0, 100).AsTask().Result;
        Assert.Equal(["nový", "první", "druhý", "třetí", "čtvr"], lines);
    }
}
