using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Tests;

public class LineIndexTests
{
    private static List<long> NaiveLineStarts(byte[] b)
    {
        var starts = new List<long> { 0 };
        for (int i = 0; i < b.Length; i++)
            if (b[i] == '\n' || (b[i] == '\r' && !(i + 1 < b.Length && b[i + 1] == '\n')))
                starts.Add(i + 1);
        return starts;
    }

    private static MappedFileSource OpenIndexed(TempFile f, int block)
    {
        var src = MappedFileSource.Open(f.Path, new MappedFileSourceOptions { IndexBlockSize = block });
        src.IndexTask.Wait();
        return src;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(1000)]
    public void IndexMatchesNaiveForEveryBlockSize(int block)
    {
        var r = new Random(block * 31);
        for (int round = 0; round < 25; round++)
        {
            var bytes = Encoding.UTF8.GetBytes(Gen.LineSoup(r, r.Next(0, 300)));
            using var f = new TempFile(bytes);
            using var src = OpenIndexed(f, block);
            var expected = NaiveLineStarts(bytes);

            Assert.Equal((expected.Count, true), src.LineIndexState);
            for (int line = 0; line < expected.Count; line++)
            {
                Assert.True(src.TryGetLineStart(line, out long off), $"line {line}");
                Assert.Equal(expected[line], off);
            }
            Assert.False(src.TryGetLineStart(expected.Count, out _));
            Assert.False(src.TryGetLineStart(-1, out _));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(64)]
    public void ReadLinesAndAlignMatchNaive(int block)
    {
        var r = new Random(block);
        for (int round = 0; round < 25; round++)
        {
            string text = Gen.LineSoup(r, r.Next(0, 200));
            var bytes = Encoding.UTF8.GetBytes(text);
            using var f = new TempFile(bytes);
            using var src = OpenIndexed(f, block);
            string[] expectedLines = Regex.Split(text, "\r\n|\n|\r");
            var starts = NaiveLineStarts(bytes);

            Assert.Equal(expectedLines, src.ReadLines(0, int.MaxValue));
            for (int line = 0; line < starts.Count; line++)
            {
                int max = r.Next(1, 6);
                Assert.Equal(expectedLines.Skip(line).Take(max).ToArray(), src.ReadLines(starts[line], max));
                Assert.Equal(expectedLines.Skip(line).Take(max).ToArray(), src.ReadLinesAtAsync(starts[line], max).AsTask().Result);
            }
            for (int off = 0; off <= bytes.Length; off++)
                Assert.Equal(starts.Last(s => s <= off), src.AlignToLineStart(off));
        }
    }

    [Fact]
    public void CrlfSplitAcrossBlocksIsOneTerminator()
    {
        var bytes = "ab\r\ncd\r\nef"u8.ToArray();
        for (int block = 1; block <= 12; block++)
        {
            using var f = new TempFile(bytes);
            using var src = OpenIndexed(f, block);
            Assert.Equal((3L, true), src.LineIndexState);
            Assert.True(src.TryGetLineStart(1, out long l1));
            Assert.Equal(4, l1);
            Assert.True(src.TryGetLineStart(2, out long l2));
            Assert.Equal(8, l2);
        }
    }

    [Fact]
    public void EmptyAndSingleTerminatorFiles()
    {
        foreach (var (content, lines) in new[] { ("", 1L), ("\n", 2L), ("\r", 2L), ("\r\n", 2L), ("\n\n", 3L), ("a", 1L) })
        {
            using var f = new TempFile(Encoding.ASCII.GetBytes(content));
            using var src = OpenIndexed(f, 1);
            Assert.Equal((lines, true), src.LineIndexState);
        }
        using var e = new TempFile();
        using var s = OpenIndexed(e, 4);
        Assert.Equal([""], s.ReadLines(0, 5));
    }

    [Fact]
    public void BomIsSkippedAndDetected()
    {
        using var f = new TempFile([0xEF, 0xBB, 0xBF, .. "ahoj\nsvět"u8.ToArray()]);
        using var src = OpenIndexed(f, 4);
        Assert.Same(Encodings.TextCodec.Utf8Bom, src.Codec);
        Assert.Equal(["ahoj", "svět"], src.ReadLines(0, 10));
    }

    [Fact]
    public void Cp1250IsDetectedAndDecoded()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp = Encoding.GetEncoding(1250);
        using var f = new TempFile(cp.GetBytes("příliš žluťoučký\nkůň"));
        using var src = OpenIndexed(f, 8);
        Assert.Same(Encodings.TextCodec.Cp1250, src.Codec);
        Assert.Equal(["příliš žluťoučký", "kůň"], src.ReadLines(0, 10));
    }

    [Fact]
    public void Utf16IsRejectedExplicitly()
    {
        using var f = new TempFile([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("abc")]);
        Assert.Throws<NotSupportedException>(() => MappedFileSource.Open(f.Path));
    }

    [Fact]
    public void ProgressEventFiresAndIndexCompletes()
    {
        var text = string.Concat(Enumerable.Repeat("line of text\n", 100_000));
        using var f = new TempFile(Encoding.ASCII.GetBytes(text));
        using var src = MappedFileSource.Open(f.Path, new MappedFileSourceOptions { IndexBlockSize = 4096 });
        int events = 0;
        src.IndexProgress += (_, _) => Interlocked.Increment(ref events);
        src.IndexTask.Wait();
        Assert.Equal((100_001L, true), src.LineIndexState);
        Assert.True(events >= 1);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(3, 4)]
    [InlineData(10, 100)]
    [InlineData(50, 8)]
    [InlineData(500, 10)]
    public void ReadColumnsMatchesSubstring(int startColumn, int count)
    {
        var r = new Random(startColumn + count);
        for (int round = 0; round < 40; round++)
        {
            string line = Gen.Mixed(r, 300, astral: false);
            using var f = new TempFile(Encoding.UTF8.GetBytes("první\n" + line + "\nposlední"));
            using var src = OpenIndexed(f, 7);
            Assert.True(src.TryGetLineStart(1, out long start));
            string expected = line.Substring(Math.Min(startColumn, line.Length), Math.Min(count, Math.Max(0, line.Length - startColumn)));
            Assert.Equal(expected, src.ReadColumns(start, startColumn, count));
        }
    }

    [Fact]
    public void ReadColumnsCountsSupplementaryCharactersAsTwoColumns()
    {
        using var f = new TempFile(Encoding.UTF8.GetBytes("x\n\U0001F600\U0001F600abc\nz"));
        using var src = OpenIndexed(f, 3);
        Assert.True(src.TryGetLineStart(1, out long start));
        Assert.Equal("😀abc", src.ReadColumns(start, 2, 10));
        Assert.Equal("abc", src.ReadColumns(start, 4, 10));
        Assert.Equal("", src.ReadColumns(start, 40, 10));
    }

    [Fact]
    public void ReadColumnsSkipsAcrossManyWindowsOfOneLongLine()
    {
        string line = string.Concat(Enumerable.Repeat("ěšč abc 中", 400_000));
        using var f = new TempFile(Encoding.UTF8.GetBytes(line + "\nnext"));
        using var src = OpenIndexed(f, 4096);
        foreach (int col in new[] { 0, 1, 999_999, 2_000_003, 3_199_990 })
            Assert.Equal(line.Substring(col, Math.Min(20, line.Length - col)), src.ReadColumns(0, col, 20));
        Assert.Equal("", src.ReadColumns(0, line.Length + 5, 20));
    }

    [Fact]
    public void FindLineEndStopsBeforeCrlf()
    {
        using var f = new TempFile("abc\r\ndef\rgh"u8.ToArray());
        using var src = OpenIndexed(f, 3);
        Assert.Equal(3, src.FindLineEnd(0));
        Assert.Equal(8, src.FindLineEnd(5));
        Assert.Equal(11, src.FindLineEnd(9));
    }
}
