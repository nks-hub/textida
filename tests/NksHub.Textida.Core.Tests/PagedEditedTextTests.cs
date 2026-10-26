using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>The text view and the text search of a paged document read the current revision: overwritten, inserted and deleted bytes count, the file on disk does not.</summary>
public class PagedEditedTextTests
{
    private static readonly byte[] Needle = "needle"u8.ToArray();

    [Fact]
    public Task Text_and_search_follow_overwrites_inserts_and_deletes_of_the_byte_tree() => Check(minBytes: 200_000, edits: 700, indexBlock: 4096);

    [Fact]
    [Trait("Category", "Large")]
    public Task After_5000_edits_of_a_100_MiB_file_the_search_and_the_line_numbers_match_a_full_pass() => Check(minBytes: 100L << 20, edits: 5000, indexBlock: 1 << 20);

    private static byte[] Build(long minBytes)
    {
        var bytes = new MemoryStream();
        for (int i = 0; bytes.Length < minBytes; i++)
        {
            bytes.Write(Encoding.UTF8.GetBytes($"row {i} {(i % 7 == 0 ? "needle" : "hay")} č\n"));
        }
        return bytes.ToArray();
    }

    private static List<long> LineStarts(byte[] bytes)
    {
        var starts = new List<long> { 0 };
        for (int at = 0, i; (i = bytes.AsSpan(at).IndexOf((byte)'\n')) >= 0;)
        {
            at += i + 1;
            starts.Add(at);
        }
        return starts;
    }

    private static async Task Check(long minBytes, int edits, int indexBlock)
    {
        var r = new Random(11);
        byte[] original = Build(minBytes);
        var originalStarts = LineStarts(original);
        var chosen = Enumerable.Range(0, edits).Select(_ => r.Next(originalStarts.Count - 1)).Distinct().Order().Select(l => (Line: l, Kind: r.Next(4))).ToArray();
        byte[][] inserts = ["needle\n"u8.ToArray(), "x\r\nneedle y\r\n"u8.ToArray()];

        using var file = new TempFile(original);
        using var d = Doc.Open(file.Path, new PagedBackendFactory(indexBlockSize: indexBlock), new DocumentOptions { Format = TestFormat.Utf8.Format });
        var expected = new MemoryStream();
        var editedAt = new List<long>();
        long cursor = 0, shift = 0;
        foreach (var (line, kind) in chosen)
        {
            long p = originalStarts[line], at = p + shift;
            expected.Write(original.AsSpan((int)cursor, (int)(p - cursor)));
            editedAt.Add(at);
            switch (kind)
            {
                case 0: // overwrite
                    d.Replace(new ByteRange(at, at + Needle.Length), Needle);
                    expected.Write(Needle);
                    cursor = p + Needle.Length;
                    break;
                case 1 or 2: // insert
                    byte[] text = inserts[kind - 1];
                    d.Insert(at, text);
                    expected.Write(text);
                    shift += text.Length;
                    cursor = p;
                    break;
                default: // delete the four bytes of "row "
                    d.Delete(new ByteRange(at, at + 4));
                    shift -= 4;
                    cursor = p + 4;
                    break;
            }
        }
        expected.Write(original.AsSpan((int)cursor));
        byte[] want = expected.ToArray();

        Assert.Equal(want.Length, d.ByteLength);
        var chunk = new byte[1 << 20];
        for (long off = 0; off < want.Length; off += chunk.Length)
        {
            int n = (int)Math.Min(chunk.Length, want.Length - off);
            Assert.Equal(n, d.Read(off, chunk.AsSpan(0, n)));
            Assert.True(chunk.AsSpan(0, n).SequenceEqual(want.AsSpan((int)off, n)), $"bytes differ in the block at {off}");
        }

        var starts = LineStarts(want);
        var until = DateTime.UtcNow.AddMinutes(3);
        while (!d.LineIndexState.Complete && DateTime.UtcNow < until) Thread.Sleep(5);
        Assert.Equal((starts.Count, true), d.LineIndexState);
        var sample = new SortedSet<int>();
        for (int i = 0; i < starts.Count; i += Math.Max(1, starts.Count / 5000)) sample.Add(i);
        foreach (long at in editedAt) sample.Add(Math.Max(0, starts.BinarySearch(at) is var f && f < 0 ? ~f - 1 : f));
        foreach (int i in sample)
        {
            Assert.True(d.TryGetOffset(new LinePosition(i, 0), out long off), $"line {i}");
            Assert.Equal(starts[i], off);
            long end = i + 1 < starts.Count ? starts[i + 1] : want.Length;
            Assert.Equal(Encoding.UTF8.GetString(want, (int)starts[i], (int)(end - starts[i])), d.ReadText(new ByteRange(starts[i], end)));
        }

        var expectedHits = new List<(long Start, int Line)>();
        long lines = 1, seen = 0;
        for (int at = 0, i; (i = want.AsSpan(at).IndexOf(Needle)) >= 0; at += i + Needle.Length)
        {
            lines += want.AsSpan((int)seen, at + i - (int)seen).Count((byte)'\n');
            seen = at + i;
            expectedHits.Add((at + i, (int)lines));
        }
        var (hits, summary) = await SearchWorker.CollectAsync(new SearchPattern("needle"), new SearchScope([], [LiveBuffer.Of(d)]),
            new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false });
        Assert.Equal(SearchEnd.Completed, summary.End);
        Assert.Equal(expectedHits.Count, summary.TotalMatches);
        Assert.Equal(expectedHits, hits.OrderBy(h => h.Range.Start).Select(h => (h.Range.Start, h.Line)).ToList());
        Assert.All(hits, h => Assert.Equal(Needle.Length, h.Range.Length));
    }
}
