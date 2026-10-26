using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class FileSearchTests
{
    private const string Smile = "\U0001F600";
    private static readonly SearchSettings Small = new() { ChunkBytes = 1 << 16, MaxResults = int.MaxValue, StopAtLimit = false };

    private static async Task<(List<SearchHit> Hits, SearchSummary Summary)> Run(string root, SearchPattern p, SearchSettings? s = null, params LiveBuffer[] buffers)
    {
        var (hits, summary) = await SearchWorker.CollectAsync(p, new SearchScope([root], buffers), s ?? Small);
        return ([.. hits.OrderBy(h => h.Source.Path, StringComparer.Ordinal).ThenBy(h => h.Range.Start)], summary);
    }

    private record Expect(long Start, int Length, int Line, int Column);

    /// <summary>Independent oracle: matches of the decoded text, positions by encoding the text before them with a plain .NET encoding.</summary>
    private static List<Expect> Oracle(string text, Encoding enc, int bom, Func<string, IEnumerable<(int Index, int Length)>> find)
    {
        var list = new List<Expect>();
        foreach (var (idx, len) in find(text))
        {
            int ls = idx == 0 ? 0 : text.LastIndexOf('\n', idx - 1) + 1;
            list.Add(new(bom + enc.GetByteCount(text[..idx]), enc.GetByteCount(text.Substring(idx, len)), text.Take(idx).Count(c => c == '\n') + 1, idx - ls));
        }
        return list;
    }

    private static IEnumerable<(int, int)> Literal(string text, string needle)
    {
        for (int i = 0; (i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0; i += needle.Length) yield return (i, needle.Length);
    }

    private static IEnumerable<(int, int)> Rx(string text, string pattern) =>
        Regex.Matches(text, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant).Where(m => m.Length > 0).Select(m => (m.Index, m.Length));

    private static string Soup(int seed, int lines, bool unicode)
    {
        var r = new Random(seed);
        string[] words = unicode
            ? ["řez", "žluť", "příliš", "kůň", "plain", "řec", "αβγ", "日本語", Smile + "x", "end", "\t"]
            : ["řec", "ěčř", "ýáíé", "plain", "text", "end", "\t"];
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            int n = r.Next(0, 9);
            for (int k = 0; k < n; k++) sb.Append(words[r.Next(words.Length)]).Append(' ');
            sb.Append(r.Next(4) == 0 ? "\r\n" : "\n");
        }
        return sb.ToString();
    }

    public static IEnumerable<object[]> Encodings()
    {
        yield return ["utf8", false];
        yield return ["utf8bom", false];
        yield return ["utf16le", false];
        yield return ["utf16be", false];
        yield return ["cp1250", false];
        yield return ["utf8", true];
        yield return ["utf16le", true];
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public async Task OffsetsLinesAndColumnsMatchAnIndependentOracleInEveryEncoding(string kind, bool regex)
    {
        using var dir = new JournalTempDir();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        bool unicode = kind != "cp1250";
        string text = Soup(7, unicode ? 9000 : 30000, unicode);
        var (enc, bom) = kind switch
        {
            "utf8" => (new UTF8Encoding(false), Array.Empty<byte>()),
            "utf8bom" => (new UTF8Encoding(false), TextCodec.Utf8Bom.Bom),
            "utf16le" => (new UnicodeEncoding(false, false), TextCodec.Utf16LEBom.Bom),
            "utf16be" => (new UnicodeEncoding(true, false), TextCodec.Utf16BEBom.Bom),
            _ => (Encoding.GetEncoding(1250), Array.Empty<byte>()),
        };
        string path = dir.Combine("f.txt");
        File.WriteAllBytes(path, [.. bom, .. enc.GetBytes(text)]);
        Assert.True(new FileInfo(path).Length > 3 * (1 << 16));

        string needle = unicode ? "řez" : "řec";
        string rx = needle + @"\b|ě\w*";
        var pattern = regex ? new SearchPattern(rx, true) : new SearchPattern(needle);
        var expected = Oracle(text, enc, bom.Length, regex ? t => Rx(t, rx) : t => Literal(t, needle));
        var (hits, summary) = await Run(dir.Path, pattern);

        Assert.Equal(expected.Count, summary.TotalMatches);
        Assert.Equal(expected, hits.Select(h => new Expect(h.Range.Start, (int)h.Range.Length, h.Line, h.Column)).ToList());
        Assert.All(hits, h => Assert.DoesNotContain('\r', h.Preview));
    }

    [Fact]
    public async Task InvalidUtf8KeepsExactByteOffsets()
    {
        using var dir = new JournalTempDir();
        var bytes = new List<byte>();
        var expected = new List<long>();
        var r = new Random(3);
        // the first MiB is valid UTF-8, so the file is detected as UTF-8 and the damage comes in later chunks
        for (int i = 0; i < 40000; i++) bytes.AddRange(Encoding.UTF8.GetBytes("prefix ěš line " + i + "\n"));
        for (int i = 0; i < 20000; i++)
        {
            if (i % 7 == 0) bytes.AddRange([0xFF, 0xC3]);
            if (i % 11 == 0) bytes.AddRange(Encoding.UTF8.GetBytes("ěš"));
            if (i % 5 == 0) { expected.Add(bytes.Count); bytes.AddRange("needle"u8); }
            bytes.AddRange(Encoding.UTF8.GetBytes(r.Next(1000) + "\n"));
        }
        File.WriteAllBytes(dir.Combine("bad.txt"), [.. bytes]);

        var (lit, _) = await Run(dir.Path, new SearchPattern("needle"));
        Assert.Equal(expected, lit.Select(h => h.Range.Start).ToList());
        var (rx, summary) = await Run(dir.Path, new SearchPattern("need[a-z]+", true));
        Assert.Equal(expected, rx.Select(h => h.Range.Start).ToList());
        Assert.Equal(expected.Count, summary.TotalMatches);
        Assert.All(rx, h => Assert.Equal(6, h.Range.Length));
    }

    [Fact]
    public async Task MatchesDoNotOverlapAndSurrogatePairsAreWhole()
    {
        using var dir = new JournalTempDir();
        File.WriteAllText(dir.Combine("a.txt"), "aaaaa\n" + Smile + Smile + "\n", new UTF8Encoding(false));
        var (aa, _) = await Run(dir.Path, new SearchPattern("aa"));
        Assert.Equal([0L, 2L], aa.Select(h => h.Range.Start));
        var (emoji, _) = await Run(dir.Path, new SearchPattern(Smile));
        Assert.Equal([6L, 10L], emoji.Select(h => h.Range.Start));
        Assert.All(emoji, h => Assert.Equal(4, h.Range.Length));
        var (halves, summary) = await Run(dir.Path, new SearchPattern(@"\uD83D", true));
        Assert.Empty(halves);
        Assert.Equal(0, summary.TotalMatches);
    }

    [Fact]
    public async Task ShowsTheLineAndFindsMatchesAtTheStartAndEndOfFiles()
    {
        using var dir = new JournalTempDir();
        File.WriteAllText(dir.Combine("a.txt"), "needle first\r\nsecond\r\nlast needle", new UTF8Encoding(false));
        var (hits, _) = await Run(dir.Path, new SearchPattern("needle"));
        Assert.Equal([(1, 0, "needle first"), (3, 5, "last needle")], hits.Select(h => (h.Line, h.Column, h.Preview)));
    }

    [Fact]
    public async Task BinaryFilesAreSkippedButUtf16TextIsSearched()
    {
        using var dir = new JournalTempDir();
        File.WriteAllBytes(dir.Combine("bin.dat"), [.. "needle"u8.ToArray(), 0, 1, 2, 3]);
        File.WriteAllBytes(dir.Combine("wide.txt"), [.. TextCodec.Utf16LEBom.Bom, .. Encoding.Unicode.GetBytes("a needle\n")]);
        var (hits, summary) = await Run(dir.Path, new SearchPattern("needle"));
        Assert.Single(hits);
        Assert.EndsWith("wide.txt", hits[0].Source.Path);
        Assert.Equal(1, summary.SkippedBinary);
        Assert.Equal(TextCodec.Utf16LEBom.Name, hits[0].Source.Codec.Name);
    }

    [Fact]
    public async Task CaseWholeWordAndLookaheadPatternsWork()
    {
        using var dir = new JournalTempDir();
        File.WriteAllText(dir.Combine("a.txt"), "Foo foo FOO food foobar\nfoo-bar foo_x\n");
        Assert.Single((await Run(dir.Path, new SearchPattern("Foo"))).Hits);
        Assert.Equal(7, (await Run(dir.Path, new SearchPattern("foo", CaseSensitive: false))).Hits.Count);
        Assert.Equal(4, (await Run(dir.Path, new SearchPattern("foo", CaseSensitive: false, WholeWord: true))).Hits.Count);
        Assert.Equal(2, (await Run(dir.Path, new SearchPattern("foo(?=bar|d)", true))).Hits.Count);
        Assert.Single((await Run(dir.Path, new SearchPattern("-bar", WholeWord: true))).Hits);
    }

    [Fact]
    public void AnInvalidOrEmptyPatternThrowsWhenTheSearchStarts()
    {
        using var dir = new JournalTempDir();
        Assert.ThrowsAny<ArgumentException>(() => SearchWorker.Start(new SearchPattern("(", true), new SearchScope([dir.Path])));
        Assert.ThrowsAny<ArgumentException>(() => SearchWorker.Start(new SearchPattern(""), new SearchScope([dir.Path])));
    }

    [Fact]
    public async Task ResultsStreamInBatchesAndTheLimitIsReported()
    {
        using var dir = new JournalTempDir();
        for (int f = 0; f < 40; f++) File.WriteAllText(dir.Combine($"f{f}.txt"), string.Concat(Enumerable.Repeat("hit\n", 50)));

        var run = SearchWorker.Start(new SearchPattern("hit"), new SearchScope([dir.Path]), new SearchSettings { BatchSize = 100, MaxResults = 1_000_000 });
        var sizes = new List<int>();
        await foreach (var b in run.Batches.ReadAllAsync()) sizes.Add(b.Hits.Count);
        var all = await run.Completion;
        Assert.True(sizes.Count >= 2, string.Join(",", sizes));
        Assert.Equal(2000, sizes.Sum());
        Assert.Equal(SearchEnd.Completed, all.End);

        var (hits, limited) = await SearchWorker.CollectAsync(new SearchPattern("hit"), new SearchScope([dir.Path]), new SearchSettings { MaxResults = 120 });
        Assert.Equal(120, hits.Count);
        Assert.Equal(SearchEnd.LimitReached, limited.End);
        Assert.True(limited.LimitReached);

        var (counted, counting) = await SearchWorker.CollectAsync(new SearchPattern("hit"), new SearchScope([dir.Path]), new SearchSettings { MaxResults = 120, StopAtLimit = false });
        Assert.Equal(120, counted.Count);
        Assert.Equal(2000, counting.TotalMatches);
        Assert.True(counting.HitsDropped);
        Assert.Equal(SearchEnd.Completed, counting.End);
    }

    [Fact]
    public async Task CancelStopsTheWorkersWellInsideOneSecond()
    {
        using var dir = new JournalTempDir();
        var line = Encoding.UTF8.GetBytes("2025-01-01 12:00:00 INFO request id=1234567 status=200 took=12ms path=/api/items\n");
        for (int f = 0; f < 3; f++)
        {
            using var fs = File.Create(dir.Combine($"big{f}.log"));
            for (int i = 0; i < 700_000; i++) fs.Write(line);
        }
        foreach (var p in new[] { new SearchPattern("took="), new SearchPattern(@"status=\d+ took=\d+ms", true) })
        {
            var run = SearchWorker.Start(p, new SearchScope([dir.Path]), new SearchSettings { MaxResults = 1_000_000_000, StopAtLimit = false, Parallelism = 2 });
            await run.Batches.WaitToReadAsync();
            var sw = Stopwatch.StartNew();
            run.Cancel();
            var summary = await run.Completion;
            Assert.True(sw.ElapsedMilliseconds < 1000, $"stopped after {sw.ElapsedMilliseconds} ms");
            Assert.Equal(SearchEnd.Cancelled, summary.End);
        }
    }

    [Fact]
    public async Task AnOpenDocumentIsSearchedInItsCurrentRevisionNotOnDisk()
    {
        using var dir = new JournalTempDir();
        string path = dir.Combine("open.txt");
        File.WriteAllText(path, "disk only needle\nsecond\n");
        File.WriteAllText(dir.Combine("other.txt"), "needle in another file\n");
        using var doc = Doc.Open(path, ReferenceBackendFactory.Instance);
        doc.Replace(new ByteRange(0, 9), "live"u8.ToArray());
        doc.Insert(doc.ByteLength, "unsaved needle\n"u8.ToArray());
        using var unnamed = Doc.FromBytes("untitled needle\n"u8.ToArray(), ReferenceBackendFactory.Instance);

        var (hits, summary) = await Run(dir.Path, new SearchPattern("needle"), null, LiveBuffer.Of(doc), LiveBuffer.Of(unnamed, "Untitled 1"));

        Assert.Equal(4, hits.Count);
        var live = hits.Where(h => h.Source.DocumentId == doc.Id).ToList();
        Assert.Equal(2, live.Count);
        Assert.All(live, h => Assert.Equal(doc.Revision, h.Source.Revision));
        Assert.Equal("live needle", doc.ReadText(new ByteRange(0, 11)));
        Assert.All(live, h => Assert.Equal("needle", doc.ReadText(h.Range)));
        var untitled = hits.Single(h => h.Source.DocumentId == unnamed.Id);
        Assert.Null(untitled.Source.Path);
        Assert.Equal("Untitled 1", untitled.Source.Title);
        Assert.Equal(3, summary.FilesScanned);
    }

    [Fact]
    public async Task AnOpenDocumentReportsProgressAndItsOffsetsMatchAnIndependentWalkAcrossChunks()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 3000).Select(i => $"řádek {i} žluťoučký needle ✓\n")));
        using var doc = Doc.FromBytes(bytes, ReferenceBackendFactory.Instance);
        long reported = 0;
        var settings = new SearchSettings { MaxResults = 1_000_000, ChunkBytes = 1 << 16, Progress = n => Interlocked.Add(ref reported, n) };
        var run = SearchWorker.Start(new SearchPattern("needle"), new SearchScope([], [LiveBuffer.Of(doc)]), settings);
        var hits = new List<SearchHit>();
        await foreach (var b in run.Batches.ReadAllAsync()) hits.AddRange(b.Hits);
        await run.Completion;

        var expected = new List<long>();
        for (int i = bytes.AsSpan().IndexOf("needle"u8); i >= 0 && i < bytes.Length;)
        {
            expected.Add(i);
            int next = bytes.AsSpan(i + 6).IndexOf("needle"u8);
            i = next < 0 ? -1 : i + 6 + next;
        }
        Assert.Equal(expected, hits.Select(h => h.Range.Start));
        Assert.All(hits, h => Assert.Equal(6, h.Range.Length));
        Assert.Equal(bytes.Length, reported);
        Assert.Equal((bytes.Length, (long)bytes.Length), run.Progress);
    }

    [Fact]
    public async Task AnEditAfterTheSearchNeverLeavesAHitWithAnInvalidOffset()
    {
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 200).Select(i => $"line {i} needle\n"))), ReferenceBackendFactory.Instance);
        var buffer = LiveBuffer.Of(doc);
        var (hits, _) = await SearchWorker.CollectAsync(new SearchPattern("needle"), new SearchScope([], [buffer]), Small);
        Assert.Equal(200, hits.Count);

        doc.Insert(0, "inserted before everything\n"u8.ToArray());
        doc.Delete(new ByteRange(100, 130));
        int mapped = 0, conflicts = 0;
        foreach (var h in hits)
        {
            var r = doc.MapRange(h.Range, h.Source.Revision!.Value);
            if (!r.Ok) { conflicts++; continue; }
            mapped++;
            Assert.Equal("needle", doc.ReadText(r.Range));
        }
        Assert.True(mapped > 150);
        Assert.True(conflicts > 0);
    }

    [Fact]
    public async Task AFileNamedAsTheRootIsSearchedWhateverItsSizeButFilesFoundByTheWalkRespectTheLimit()
    {
        using var dir = new JournalTempDir();
        string big = dir.Combine("big.txt");
        File.WriteAllText(big, string.Concat(Enumerable.Repeat("needle line\n", 2000)));
        var settings = Small with { Walk = new FileWalkOptions { MaxFileSize = 1000 } };

        var (single, _) = await SearchWorker.CollectAsync(new SearchPattern("needle"), new SearchScope([big]), settings);
        Assert.Equal(2000, single.Count);

        var (walked, summary) = await SearchWorker.CollectAsync(new SearchPattern("needle"), new SearchScope([dir.Path]), settings);
        Assert.Empty(walked);
        Assert.Equal(1, summary.Walk.TooLarge);
    }

    [Fact]
    public async Task ALargeFileScannedInParallelRangesGivesTheSameHitsAsOneSequentialPass()
    {
        using var dir = new JournalTempDir();
        string path = dir.Combine("large.log");
        using (var fs = File.Create(path))
        {
            fs.Write(TextCodec.Utf8Bom.Bom);
            var sb = new StringBuilder();
            for (int i = 0; i < 3_000_000; i++)
            {
                sb.Clear();
                sb.Append("row ").Append(i).Append(i % 61 == 0 ? " needle7 " : " plain ").Append("ěš");
                sb.Append(i % 9 == 0 ? "\r\n" : "\n");
                fs.Write(Encoding.UTF8.GetBytes(sb.ToString()));
            }
        }
        Assert.True(new FileInfo(path).Length > ContentScannerLimit);

        foreach (var pattern in new[] { new SearchPattern("needle7"), new SearchPattern(@"ne+dle(\d)\s", true) })
        {
            var sequential = await SearchWorker.CollectAsync(pattern, new SearchScope([path]), new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false, Parallelism = 1 });
            var parallel = await SearchWorker.CollectAsync(pattern, new SearchScope([path]), new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false, Parallelism = 8 });
            Assert.Equal(sequential.Summary.TotalMatches, parallel.Summary.TotalMatches);
            Assert.True(sequential.Hits.Count > 20_000);
            Assert.Equal(
                sequential.Hits.Select(h => (h.Range, h.Line, h.Column, h.Preview)),
                parallel.Hits.OrderBy(h => h.Range.Start).Select(h => (h.Range, h.Line, h.Column, h.Preview)));
        }
    }

    private const long ContentScannerLimit = 64L << 20;

    [Fact]
    public async Task FilesRemovedWhileTheSearchRunsDoNotBreakIt()
    {
        using var dir = new JournalTempDir();
        for (int i = 0; i < 200; i++) File.WriteAllText(dir.Combine($"f{i}.txt"), "needle\n");
        var run = SearchWorker.Start(new SearchPattern("needle"), new SearchScope([dir.Path]), Small);
        for (int i = 0; i < 200; i += 3) File.Delete(dir.Combine($"f{i}.txt"));
        await foreach (var _ in run.Batches.ReadAllAsync()) { }
        var summary = await run.Completion;
        Assert.Equal(SearchEnd.Completed, summary.End);
        Assert.True(summary.FilesScanned >= 133);
    }
}
