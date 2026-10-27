using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>Search numbers lines by the rule of the line index: LF, CRLF and a bare CR each end one line.</summary>
public class SearchLineNumberTests
{
    private static readonly SearchSettings Small = new() { ChunkBytes = 1 << 16, MaxResults = int.MaxValue, StopAtLimit = false };

    public static TheoryData<string> Endings => new() { "\n", "\r\n", "\r", "mixed" };

    private static string Build(string ending, int lines, int seed)
    {
        var r = new Random(seed);
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
            sb.Append(i % 3 == 0 ? "needle " : "hay ").Append(i).Append(ending == "mixed" ? new[] { "\n", "\r\n", "\r" }[r.Next(3)] : ending);
        return sb.ToString();
    }

    /// <summary>Text of 65535 bytes ending in CR, then LF: the CRLF lies across the cut of the first read chunk.</summary>
    private static string CrlfAtChunkEnd(int unit, int bom)
    {
        int crIndex = (65536 - bom) / unit - 1;
        var body = new StringBuilder();
        while (body.Length + 7 <= crIndex) body.Append("needle\r");
        string text = new string('p', crIndex - body.Length) + body + "\r";
        return text + "\nneedle\r\nneedle\rneedle\n";
    }

    private static List<(long Start, int Line)> Oracle(string text, int bom, int unit)
    {
        var hits = new List<(long, int)>();
        int line = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (string.CompareOrdinal(text, i, "needle", 0, 6) == 0) hits.Add((bom + (long)unit * i, line));
            if (text[i] == '\n' || text[i] == '\r' && !(i + 1 < text.Length && text[i + 1] == '\n')) line++;
        }
        return hits;
    }

    private static async Task Check(string text, Encoding enc, int bom, int unit, bool regex)
    {
        var expected = Oracle(text, bom, unit);
        var pattern = new SearchPattern(regex ? "need[l]e" : "needle", regex);
        byte[] bytes = [.. enc.GetPreamble(), .. enc.GetBytes(text)];

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Path);
        string path = dir.File("f.txt");
        File.WriteAllBytes(path, bytes);
        var (onDisk, _) = await SearchWorker.CollectAsync(pattern, new SearchScope([dir.Path]), Small);
        Assert.Equal(expected, onDisk.OrderBy(h => h.Range.Start).Select(h => (h.Range.Start, h.Line)).ToList());

        if (unit != 1) return;
        foreach (IBackendFactory factory in new IBackendFactory[] { ReferenceBackendFactory.Instance, PagedBackendFactory.Instance })
        {
            using var doc = Doc.FromBytes(bytes, factory, new DocumentOptions { Format = TestFormat.Utf8.Format });
            var (live, _) = await SearchWorker.CollectAsync(pattern, new SearchScope([], [LiveBuffer.Of(doc)]), Small);
            Assert.Equal(expected, live.OrderBy(h => h.Range.Start).Select(h => (h.Range.Start, h.Line)).ToList());
        }
    }

    [Theory]
    [MemberData(nameof(Endings))]
    public async Task Line_numbers_of_matches_follow_the_index_rule_for_any_line_ending(string ending)
    {
        string text = Build(ending, 20_000, 3);
        await Check(text, new UTF8Encoding(false), 0, 1, regex: false);
        await Check(text, new UTF8Encoding(false), 0, 1, regex: true);
        await Check(text, new UnicodeEncoding(false, true), 2, 2, regex: false);
        await Check(text, new UnicodeEncoding(false, true), 2, 2, regex: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_CRLF_across_the_cut_of_a_read_chunk_counts_once(bool regex)
    {
        await Check(CrlfAtChunkEnd(1, 0), new UTF8Encoding(false), 0, 1, regex);
        await Check(CrlfAtChunkEnd(2, 2), new UnicodeEncoding(false, true), 2, 2, regex);
    }

    [Fact]
    public async Task A_CRLF_in_a_paged_document_is_one_line_break_in_the_search_and_in_the_index()
    {
        // "\r\n" is one terminator, a hit on its LF stays on the line of the CR
        string text = "a\r\nb\rc\nd\r\r\ne";
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        var (hits, _) = await SearchWorker.CollectAsync(new SearchPattern("[abcde]", true), new SearchScope([], [LiveBuffer.Of(doc)]), Small);
        Assert.Equal([1, 2, 3, 4, 6], hits.OrderBy(h => h.Range.Start).Select(h => h.Line).ToArray());
        foreach (var h in hits)
        {
            Assert.True(doc.TryGetPosition(h.Range.Start, out var p));
            Assert.Equal(p.Line + 1, h.Line);
        }
    }

    [Fact]
    [Trait("Category", "Large")]
    public async Task Parallel_ranges_of_a_70_MiB_file_number_lines_like_one_pass()
    {
        string text = Build("mixed", 5_500_000, 5) + "needle end";
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(dir.File("big.txt"), text, new UTF8Encoding(false));
        Assert.True(new FileInfo(dir.File("big.txt")).Length > 64 << 20);
        var expected = Oracle(text, 0, 1);
        var (hits, _) = await SearchWorker.CollectAsync(new SearchPattern("needle"), new SearchScope([dir.Path]),
            new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false, Parallelism = 4 });
        Assert.Equal(expected, hits.OrderBy(h => h.Range.Start).Select(h => (h.Range.Start, h.Line)).ToList());
    }
}
