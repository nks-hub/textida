using System.Text;
using System.Text.RegularExpressions;

namespace NksPad.Core.Tests;

public class SearchTests
{
    private static string RandomLines(Random r, int lines)
    {
        string[] words = ["foo", "bar", "ababab", "foofoo", "příliš", "žluťoučký", "kůň", "a", "aa", "x"];
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            int n = r.Next(0, 8);
            for (int j = 0; j < n; j++) sb.Append(words[r.Next(words.Length)]).Append(r.Next(3) == 0 ? "" : " ");
            sb.Append(r.Next(4) == 0 ? "\r\n" : "\n");
        }
        return sb.ToString();
    }

    private static long NaiveLiteral(string text, string pattern)
    {
        long total = 0;
        foreach (var line in text.Split('\n'))
        {
            int pos = 0;
            while ((pos = line.IndexOf(pattern, pos, StringComparison.Ordinal)) >= 0) { total++; pos += pattern.Length; }
        }
        return total;
    }

    [Theory]
    [InlineData("foo", 1)]
    [InlineData("foo", 7)]
    [InlineData("ababab", 3)]
    [InlineData("aa", 16)]
    [InlineData("kůň", 5)]
    [InlineData("x", 1000)]
    public async Task LiteralCountAndOffsetsMatchNaive(string pattern, int block)
    {
        var r = new Random(pattern.Length * 100 + block);
        for (int round = 0; round < 10; round++)
        {
            string text = RandomLines(r, r.Next(0, 80));
            var bytes = Encoding.UTF8.GetBytes(text);
            using var f = new TempFile(bytes);
            using var mf = MappedFile.Open(f.Path);
            var res = await PagedSearch.SearchLiteralAsync(mf, Encoding.UTF8.GetBytes(pattern), new SearchOptions { BlockSize = block, MaxResults = 100000 });
            Assert.Equal(NaiveLiteral(text, pattern), res.MatchCount);
            Assert.Equal(res.MatchCount, res.Matches.Count);
            var pb = Encoding.UTF8.GetBytes(pattern);
            long prev = -1;
            foreach (var m in res.Matches)
            {
                Assert.True(m.ByteOffset > prev);
                Assert.True(bytes.AsSpan((int)m.ByteOffset, m.ByteLength).SequenceEqual(pb));
                prev = m.ByteOffset;
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(4096)]
    public async Task RegexCountAndByteOffsetsMatchNaive(int block)
    {
        var regex = new Regex("(foo|ba)r? ?[a-ž]*", RegexOptions.CultureInvariant);
        var r = new Random(block);
        for (int round = 0; round < 10; round++)
        {
            string text = RandomLines(r, r.Next(0, 60));
            using var f = new TempFile(Encoding.UTF8.GetBytes(text));
            using var mf = MappedFile.Open(f.Path);
            var res = await PagedSearch.SearchRegexAsync(mf, Encoding.UTF8, regex, new SearchOptions { BlockSize = block, MaxResults = 100000 });
            long expected = 0;
            var offsets = new List<long>();
            long lineByteStart = 0;
            foreach (var line in text.Split('\n'))
            {
                foreach (Match m in regex.Matches(line))
                {
                    expected++;
                    offsets.Add(lineByteStart + Encoding.UTF8.GetByteCount(line.AsSpan(0, m.Index)));
                }
                lineByteStart += Encoding.UTF8.GetByteCount(line) + 1;
            }
            Assert.Equal(expected, res.MatchCount);
            Assert.Equal(offsets, res.Matches.Select(m => m.ByteOffset).ToList());
        }
    }

    [Fact]
    public async Task MaxResultsLimitsListButNotCount()
    {
        using var f = new TempFile(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("hit\n", 5000))));
        using var mf = MappedFile.Open(f.Path);
        var res = await PagedSearch.SearchLiteralAsync(mf, "hit"u8.ToArray(), new SearchOptions { BlockSize = 100, MaxResults = 10 });
        Assert.Equal(5000, res.MatchCount);
        Assert.Equal(10, res.Matches.Count);
        Assert.Equal(0, res.Matches[0].ByteOffset);
        Assert.Equal(36, res.Matches[9].ByteOffset);
    }

    [Fact]
    public async Task CancelledSearchThrows()
    {
        using var f = new TempFile(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("hit hit hit\n", 50000))));
        using var mf = MappedFile.Open(f.Path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PagedSearch.SearchLiteralAsync(mf, "hit"u8.ToArray(), null, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PagedSearch.SearchRegexAsync(mf, Encoding.UTF8, new Regex("hit"), null, cts.Token));
    }

    [Fact]
    public void MultiLinePatternsAreRejected()
    {
        using var f = new TempFile("a\nb"u8.ToArray());
        using var mf = MappedFile.Open(f.Path);
        Assert.Throws<ArgumentException>(() => { _ = PagedSearch.SearchLiteralAsync(mf, "a\nb"u8.ToArray()); });
        Assert.Throws<ArgumentException>(() => { _ = PagedSearch.SearchLiteralAsync(mf, ReadOnlyMemory<byte>.Empty); });
    }

    [Fact]
    public async Task LongSingleLineIsOneBlock()
    {
        string text = string.Concat(Enumerable.Repeat("abc", 10000));
        using var f = new TempFile(Encoding.ASCII.GetBytes(text));
        using var mf = MappedFile.Open(f.Path);
        var res = await PagedSearch.SearchLiteralAsync(mf, "cab"u8.ToArray(), new SearchOptions { BlockSize = 100 });
        Assert.Equal(9999, res.MatchCount);
    }

    [Fact]
    public async Task TreeSearchCountsAcrossFilesAndSkipsBinary()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nkspad-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "needle\nhay needle needle\n");
            File.WriteAllText(Path.Combine(dir, "sub", "b.txt"), "nothing\n");
            File.WriteAllText(Path.Combine(dir, "sub", "c.txt"), "needle");
            File.WriteAllBytes(Path.Combine(dir, "bin.dat"), [(byte)'n', 0, (byte)'e']);
            var lit = await TreeSearch.SearchLiteralAsync(dir, "needle"u8.ToArray());
            Assert.Equal((3L, 2L, 4L, 1L), (lit.FilesScanned, lit.FilesMatched, lit.Matches, lit.FilesSkipped));
            var rx = await TreeSearch.SearchRegexAsync(dir, new Regex("need.e"));
            Assert.Equal(4, rx.Matches);
        }
        finally { Directory.Delete(dir, true); }
    }
}
