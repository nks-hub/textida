using System.Diagnostics;
using System.Reflection;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class PagedBackendTests
{
    [Fact]
    public void TextOfAPagedDocumentCannotBeCalled()
    {
        foreach (var type in new[] { typeof(PagedDocument), typeof(PagedBackend) })
        {
            var text = type.GetProperty("Text");
            Assert.NotNull(text);
            var obsolete = text!.GetCustomAttribute<ObsoleteAttribute>();
            Assert.True(obsolete is { IsError: true }, $"{type.Name}.Text must be a compile error");
        }
        foreach (var type in new[] { typeof(IByteBackend), typeof(IByteSnapshot), typeof(DocumentSnapshot), typeof(Doc), typeof(IPagedTextSource), typeof(PagedSnapshot) })
            Assert.Null(type.GetProperty("Text"));
    }

    [Fact]
    public void LineQueriesAreUnknownUntilTheOriginalIsIndexed()
    {
        using var tmp = new TempFile("a\nb\r\nc"u8.ToArray());
        using var d = Doc.Open(tmp.Path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TestFormat.Utf8.Format });
        Assert.False(d.LineIndexState.Complete);
        Assert.False(d.TryGetPosition(2, out _));
        d.Insert(1, "x"u8.ToArray());
        Assert.False(d.LineIndexState.Complete);
        Assert.False(d.TryGetPosition(2, out _));
        Assert.False(d.TryGetOffset(new(1, 0), out _));
        Assert.Equal("ax\nb\r\nc"u8.ToArray(), d.ReadBytes(new(0, d.ByteLength)));
    }

    [Fact]
    public void PositionsInsideTheIndexedPrefixAreKnownWhileTheRestIsBeingIndexed()
    {
        string text = string.Concat(Enumerable.Range(0, 120).Select(i => i % 5 == 0 ? $"row {i}\r\n" : $"row {i}\n"));
        var bytes = Encoding.ASCII.GetBytes(text);
        using var tmp = new TempFile(bytes);
        using var gate = new ManualResetEventSlim();
        MappedFileSource.IndexBlockHook = (source, block) => { if (source.Path == tmp.Path && block >= 4) gate.Wait(); };
        try
        {
            using var d = Doc.Open(tmp.Path, new PagedBackendFactory(indexBlockSize: 64), new DocumentOptions { Format = TestFormat.Utf8.Format });
            var paged = ((PagedBackend)d.Backend).Paged;
            for (int i = 0; i < 1000 && paged.IndexedBytes < 256; i++) Thread.Sleep(5);
            Assert.Equal(256, paged.IndexedBytes);
            Assert.False(d.LineIndexState.Complete);

            var starts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n' || text[i] == '\r' && text[i + 1] != '\n') starts.Add(i + 1);
            long known = d.LineIndexState.Lines;
            for (int offset = 0; offset <= text.Length; offset++)
            {
                bool inside = offset < 256 && !(offset > 0 && text[offset - 1] == '\r' && text[offset] == '\n');
                if (!d.IsCharBoundary(offset) || offset > 0 && offset < text.Length && text[offset - 1] == '\r' && text[offset] == '\n') continue;
                Assert.Equal(inside, d.TryGetPosition(offset, out var p));
                if (!inside) continue;
                int line = starts.FindLastIndex(s => s <= offset);
                Assert.Equal(new LinePosition(line, offset - starts[line]), p);
            }
            for (int line = 0; line < 120; line++)
            {
                bool expected = line + 1 < known;
                Assert.Equal(expected, d.TryGetOffset(new(line, 2), out long at));
                if (expected) Assert.Equal(starts[line] + 2, at);
            }
        }
        finally
        {
            gate.Set();
            MappedFileSource.IndexBlockHook = null;
        }
    }

    [Theory]
    [InlineData(1, 64)]
    [InlineData(2, 7)]
    [InlineData(3, 1 << 20)]
    public void LineStartsAfterRandomCrLfEditsMatchANaivePass(int seed, int blockSize)
    {
        var r = new Random(seed);
        byte[] original = Encoding.UTF8.GetBytes(Gen.LineSoup(r, 2000));
        using var tmp = new TempFile(original);
        using var d = Doc.Open(tmp.Path, new PagedBackendFactory(indexBlockSize: blockSize), new DocumentOptions { Format = TestFormat.Utf8.Format });
        byte[][] inserts = ["\r"u8.ToArray(), "\n"u8.ToArray(), "\r\n"u8.ToArray(), "x"u8.ToArray(), "a\rb\nc"u8.ToArray(), "\n\r"u8.ToArray()];
        for (int step = 0; step < 800; step++)
        {
            int op = r.Next(10);
            var bounds = new NaiveModel(d.ReadBytes(new(0, d.ByteLength))).Boundaries().Order().ToArray();
            if (op < 5) d.Insert(bounds[r.Next(bounds.Length)], inserts[r.Next(inserts.Length)]);
            else if (op < 8 && bounds.Length > 1)
            {
                int i = r.Next(bounds.Length - 1);
                d.Delete(new(bounds[i], bounds[Math.Min(bounds.Length - 1, i + r.Next(1, 3))]));
            }
            else if (op < 9) d.Undo();
            else d.Redo();

            if (step % 20 != 0) continue;
            var bytes = d.ReadBytes(new(0, d.ByteLength));
            var starts = new NaiveModel(bytes).LineStarts();
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!d.LineIndexState.Complete && DateTime.UtcNow < until) Thread.Sleep(1);
            Assert.Equal((starts.Count, true), d.LineIndexState);
            for (int line = 0; line < starts.Count; line++)
            {
                Assert.True(d.TryGetOffset(new(line, 0), out long off), $"step {step} line {line}");
                Assert.Equal(starts[line], off);
            }
        }
    }

    [Fact]
    public void UnitsAgreeWithTheFrameworkDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var r = new Random(7);
        foreach (var f in TestFormat.All.Append(TestFormat.Utf32LE).Append(TestFormat.Utf32BE))
        {
            var codec = f.Format;
            for (int round = 0; round < 300; round++)
            {
                var bytes = new byte[r.Next(0, 40)];
                r.NextBytes(bytes);
                if (r.Next(2) == 0) for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(bytes[i] % 4 == 0 ? 0xD8 + r.Next(4) : bytes[i] % 7 == 0 ? 0xFF : bytes[i] & 0x7F);
                int at = 0;
                var sb = new StringBuilder();
                while (at < bytes.Length)
                {
                    int len = codec.ReadUnit(bytes.AsSpan(at), out int chars);
                    Assert.True(len > 0);
                    string unit = f.Enc.GetString(bytes, at, len);
                    Assert.Equal(chars, unit.Length);
                    sb.Append(unit);
                    at += len;
                }
                Assert.Equal(f.Enc.GetString(bytes), sb.ToString());
            }
        }
    }

    [Fact]
    public void ABackendRoundTripKeepsEverythingForReferenceAndPaged()
    {
        BackendSwitch.RoundTrip(ReferenceBackendFactory.Instance, PagedBackendFactory.Instance);
        BackendSwitch.RoundTrip(PagedBackendFactory.Instance, ReferenceBackendFactory.Instance);
    }

    [Fact]
    [Trait("Category", "Large")]
    public void LineStartsAfterRandomCrLfEditsMatchANaivePassOverAFileOver4GiB()
    {
        const long size = BigFileBackendFactory.FileSize;
        var r = new Random(11);
        string path = BigFileBackendFactory.NewPath();
        long[] hot = [0, (1L << 31) - 1, (1L << 32) - 1, size - 2];
        using (var fs = BigFileBackendFactory.CreateSparse(path))
        {
            fs.Write(Encoding.UTF8.GetBytes(Gen.LineSoup(r, 300)));
            foreach (long at in hot[1..]) { fs.Position = at; fs.Write("\r\n"u8); }
        }

        using var d = Doc.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        Assert.True(d.ByteLength > 1L << 32);
        byte[][] inserts = ["\r"u8.ToArray(), "\n"u8.ToArray(), "\r\n"u8.ToArray(), "x"u8.ToArray(), "a\rb\nc"u8.ToArray(), "\n\r"u8.ToArray()];
        for (int step = 0; step < 60; step++)
        {
            // edits at the CRLF pairs on the 2^31 and 2^32 boundaries (one byte before, between, after) and in the small head
            long at = r.Next(3) == 0 ? r.Next(1, 600) : hot[r.Next(1, hot.Length)] + r.Next(-1, 3);
            at = Math.Clamp(at, 0, d.ByteLength);
            int op = r.Next(10);
            if (op < 6) d.Insert(at, inserts[r.Next(inserts.Length)]);
            else if (op < 8) d.Delete(new(at, Math.Min(d.ByteLength, at + r.Next(1, 3))));
            else if (op < 9) d.Undo();
            else d.Redo();
            if (step % 20 == 19) AssertLineStartsMatchNaivePass(d);
        }
        AssertLineStartsMatchNaivePass(d);
        while (d.CanUndo) d.Undo();
        AssertLineStartsMatchNaivePass(d);
    }

    private static void AssertLineStartsMatchNaivePass(Doc d)
    {
        var until = DateTime.UtcNow.AddMinutes(5);
        while (!d.LineIndexState.Complete) { Assert.True(DateTime.UtcNow < until); Thread.Sleep(20); }
        var starts = new List<long> { 0 };
        const int chunk = 1 << 26;
        long length = d.ByteLength;
        byte prev = 0;
        for (long at = 0; at < length; at += chunk)
        {
            var bytes = d.ReadBytes(new(at, Math.Min(length, at + chunk)));
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b == '\n') starts.Add(at + i + 1);
                else if (prev == '\r') starts.Add(at + i);
                prev = b;
            }
        }
        if (prev == '\r') starts.Add(length);
        Assert.Equal((starts.Count, true), d.LineIndexState);
        for (int line = 0; line < starts.Count; line++)
        {
            Assert.True(d.TryGetOffset(new(line, 0), out long off), $"line {line}");
            Assert.Equal(starts[line], off);
        }
    }

    [Fact]
    [Trait("Category", "Large")]
    public void ContractOperationsWorkOnAFileOver4GiBWithPositionsBeyondInt()
    {
        const long size = 4_500_000_000;
        using var dir = new TempDir();
        string path = dir.File("big.txt");
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;
        if (new DriveInfo(root).AvailableFreeSpace < size + (1L << 30)) return;

        // known layout: NUL filler with CR LF pairs straddling 2^31 and 2^32 and a last line near the end
        File.Create(path).Dispose();
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("fsutil", $"sparse setflag \"{path}\"") { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            fs.SetLength(size);
            fs.Write("L0\r"u8);
            foreach (long cr in new[] { (1L << 31) - 1, (1L << 32) - 1, size - 6 })
            {
                fs.Position = cr;
                fs.Write("\r\n"u8);
            }
            fs.Position = size - 4;
            fs.Write("tail"u8);
        }
        long[] lineStarts = [0, 3, (1L << 31) + 1, (1L << 32) + 1, size - 4];

        using var d = Doc.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        Assert.Equal(size, d.ByteLength);
        var until = DateTime.UtcNow.AddMinutes(5);
        while (!d.LineIndexState.Complete) { Assert.True(DateTime.UtcNow < until); Thread.Sleep(20); }
        Assert.Equal(lineStarts.Length, d.LineIndexState.Lines);
        for (int line = 0; line < lineStarts.Length; line++)
        {
            Assert.True(d.TryGetOffset(new(line, 0), out long off));
            Assert.Equal(lineStarts[line], off);
            Assert.True(d.TryGetPosition(lineStarts[line], out var p));
            Assert.Equal(new LinePosition(line, 0), p);
        }
        Assert.True(d.TryGetPosition(size, out var end));
        Assert.Equal(new LinePosition(4, 4), end);

        // an edit past 4 GiB, a CRLF cut in two by an insertion, and the undo of both
        var tail = d.ReadBytes(new(size - 6, size));
        Assert.Equal("\r\ntail"u8.ToArray()[2..], tail[2..]);
        Assert.True(d.Insert((1L << 32) + 10, "é"u8.ToArray()).Applied);
        Assert.True(d.Insert(1L << 32, "x"u8.ToArray()).Applied);
        Assert.Equal(size + 3, d.ByteLength);
        while (!d.LineIndexState.Complete) Thread.Sleep(5);
        Assert.Equal(lineStarts.Length + 1, d.LineIndexState.Lines);
        Assert.True(d.TryGetOffset(new(4, 0), out long afterSplit));
        Assert.Equal((1L << 32) + 2, afterSplit);
        Assert.True(d.TryGetPosition(size + 3, out end));
        Assert.Equal(new LinePosition(5, 4), end);
        Assert.True(d.TryGetPosition((1L << 32) + 11, out var pos));
        Assert.Equal(new LinePosition(4, 9), pos);
        Assert.False(d.TryGetPosition((1L << 32) + 12, out _));
        Assert.True(d.TryGetPosition((1L << 32) + 13, out pos));
        Assert.Equal(new LinePosition(4, 10), pos);
        Assert.Equal("é"u8.ToArray(), d.ReadBytes(new((1L << 32) + 11, (1L << 32) + 13)));

        d.Undo();
        d.Undo();
        Assert.Equal(size, d.ByteLength);
        while (!d.LineIndexState.Complete) Thread.Sleep(5);
        Assert.Equal(lineStarts.Length, d.LineIndexState.Lines);
        Assert.True(d.TryGetOffset(new(3, 0), out long third));
        Assert.Equal(lineStarts[3], third);
    }

    [Fact]
    public void SaveAsInTheSameEncodingWritesTheEditedBytesAndKeepsUndo()
    {
        using var tmp = new TempFile("a\nb\n"u8.ToArray());
        using var d = Doc.Open(tmp.Path, new PagedBackendFactory(), new DocumentOptions { Format = TestFormat.Utf8.Format });
        Assert.True(d.Insert(2, "é"u8.ToArray()).Applied);
        var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(tmp.Path)!, "copy.txt");

        var result = d.SaveAs(target, d.Codec);

        Assert.True(result.Saved);
        Assert.Equal("a\né"u8.ToArray().Concat("b\n"u8.ToArray()).ToArray(), File.ReadAllBytes(target));
        Assert.True(d.CanUndo);
    }
}
