using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Document.Contract;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// A 10 GiB sparse file that is NUL filler with small text islands. The expected content is kept as a list of islands that the same
/// edits are applied to, so the naive model never holds more than the islands.
/// </summary>
public class PagedHugeEditTests(ITestOutputHelper output)
{
    private const long Size = 10L << 30;
    private static readonly byte[] Needle = "needle"u8.ToArray();

    private sealed class Model(List<(long Start, byte[] Data)> islands, long length)
    {
        public List<(long Start, byte[] Data)> Islands { get; private set; } = islands;
        public long Length { get; private set; } = length;

        public Model Copy() => new([.. Islands], Length);

        public void Replace(long at, long delete, byte[] insert)
        {
            var next = new List<(long, byte[])>();
            foreach (var (s, d) in Islands)
            {
                long e = s + d.Length;
                if (s < at) next.Add((s, d[..(int)(Math.Min(e, at) - s)]));
                if (e > at + delete)
                {
                    long from = Math.Max(s, at + delete);
                    next.Add((from - delete + insert.Length, d[(int)(from - s)..]));
                }
            }
            if (insert.Length > 0) next.Add((at, insert));
            Islands = [.. next.OrderBy(i => i.Item1)];
            Length += insert.Length - delete;
        }

        /// <summary>Runs of bytes that touch each other; the filler between them has no terminator.</summary>
        public IEnumerable<(long Start, byte[] Data)> Runs()
        {
            long start = 0;
            var run = new List<byte>();
            foreach (var (s, d) in Islands)
            {
                if (run.Count > 0 && start + run.Count != s)
                {
                    yield return (start, [.. run]);
                    run.Clear();
                }
                if (run.Count == 0) start = s;
                run.AddRange(d);
            }
            if (run.Count > 0) yield return (start, [.. run]);
        }

        /// <summary>The bytes of a range: the islands over NUL filler.</summary>
        public byte[] Read(long start, long end)
        {
            var bytes = new byte[end - start];
            foreach (var (s, d) in Islands)
            {
                long from = Math.Max(s, start), to = Math.Min(s + d.Length, end);
                if (from < to) d.AsSpan((int)(from - s), (int)(to - from)).CopyTo(bytes.AsSpan((int)(from - start)));
            }
            return bytes;
        }

        /// <summary>Line starts by CR, LF and CRLF, computed by a plain pass over the bytes.</summary>
        public List<long> LineStarts()
        {
            var starts = new List<long> { 0 };
            foreach (var (s, d) in Runs())
            {
                for (int i = 0; i < d.Length; i++)
                {
                    if (d[i] == '\n') starts.Add(s + i + 1);
                    else if (i > 0 && d[i - 1] == '\r') starts.Add(s + i);
                }
                if (d[^1] == '\r') starts.Add(s + d.Length);
            }
            return starts;
        }
    }

    private static Model Create(string path, Random r, int islands)
    {
        var list = new List<(long Start, byte[] Data)>();
        var used = new SortedSet<long>();
        foreach (long hot in new[] { 0L, (1L << 31) - 1, (1L << 32) - 1, Size - 5 })
        {
            used.Add(hot >> 13);
            byte[] data = hot == 0 ? Text(r, 0) : hot == Size - 5 ? "tail\r"u8.ToArray() : "\r\n"u8.ToArray();
            list.Add((hot, data));
        }
        while (list.Count < islands)
        {
            long start = r.NextInt64(Size - (1 << 14));
            if (!used.Add(start >> 13)) continue; // at most one island per 8 KiB, so none touch or overlap
            list.Add((start, Text(r, list.Count)));
        }
        list.Sort((a, b) => a.Start.CompareTo(b.Start));

        File.Create(path).Dispose();
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("fsutil", $"sparse setflag \"{path}\"") { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        fs.SetLength(Size);
        foreach (var (s, d) in list)
        {
            fs.Position = s;
            fs.Write(d);
        }
        return new Model(list, Size);
    }

    private static byte[] Text(Random r, int id)
    {
        var sb = new StringBuilder("\n");
        for (int i = 0, n = r.Next(2, 30); i < n; i++)
            sb.Append($"row {id}.{i} {(r.Next(3) == 0 ? "needle" : "hay")} č{(r.Next(2) == 0 ? "\n" : "\r\n")}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>A random edit position on a character boundary: a line start or the middle of a CRLF inside an island, or somewhere in the filler.</summary>
    private static long PickPosition(Model m, Random r)
    {
        if (r.Next(3) > 0)
        {
            var (s, d) = m.Islands[r.Next(m.Islands.Count)];
            var at = Enumerable.Range(0, d.Length + 1).Where(i => i == 0 || d[i - 1] is (byte)'\n' or (byte)'\r').ToArray();
            return s + at[r.Next(at.Length)];
        }
        while (true)
        {
            long p = r.NextInt64(m.Length + 1);
            if (!m.Islands.Any(i => i.Start < p && p < i.Start + i.Data.Length)) return p;
        }
    }

    private static void Edit(Doc d, Model m, Random r, byte[][] inserts)
    {
        long at = PickPosition(m, r);
        int op = r.Next(10);
        if (op < 6)
        {
            var text = inserts[r.Next(inserts.Length)];
            Assert.True(d.Insert(at, text).Applied);
            m.Replace(at, 0, text);
        }
        else
        {
            long n = Math.Min(m.Length - at, r.Next(1, 4));
            if (n == 0) return;
            if (op < 8)
            {
                Assert.True(d.Delete(new ByteRange(at, at + n)).Applied);
                m.Replace(at, n, []);
            }
            else
            {
                var text = inserts[r.Next(inserts.Length)];
                Assert.True(d.Replace(new ByteRange(at, at + n), text).Applied);
                m.Replace(at, n, text);
            }
        }
    }

    private static void WaitForIndex(Doc d)
    {
        var until = DateTime.UtcNow.AddMinutes(10);
        while (!d.LineIndexState.Complete) { Assert.True(DateTime.UtcNow < until, "the line index did not complete"); Thread.Sleep(20); }
    }

    private static void AssertSameBytes(Doc d, Model m)
    {
        Assert.Equal(m.Length, d.ByteLength);
        foreach (var (s, data) in m.Islands) Assert.Equal(data, d.ReadBytes(new ByteRange(s, s + data.Length)));
    }

    private static void AssertLineStarts(Doc d, List<long> starts)
    {
        WaitForIndex(d);
        Assert.Equal((starts.Count, true), d.LineIndexState);
        for (int line = 0; line < starts.Count; line++)
        {
            Assert.True(d.TryGetOffset(new LinePosition(line, 0), out long off), $"line {line}");
            Assert.Equal(starts[line], off);
        }
    }

    private static bool HasRoom(string dir) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace > 2L << 30;

    [Fact]
    [Trait("Category", "Large")]
    public void After_1000_random_edits_of_a_10_GiB_file_the_line_starts_match_a_naive_pass()
    {
        using var dir = new TempDir();
        string path = dir.File("huge.txt");
        if (!HasRoom(path)) return;
        var r = new Random(7);
        var m = Create(path, r, 2500);
        var original = m.Copy();
        byte[][] inserts = ["\r"u8.ToArray(), "\n"u8.ToArray(), "\r\n"u8.ToArray(), "x"u8.ToArray(), "a\rb\nc"u8.ToArray(), "\n\r"u8.ToArray(), "\nneedle q\r\n"u8.ToArray()];

        using var d = Doc.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        WaitForIndex(d);
        AssertLineStarts(d, original.LineStarts());
        for (int step = 0; step < 1000; step++)
        {
            Edit(d, m, r, inserts);
            if (step % 250 == 249) AssertLineStarts(d, m.LineStarts());
        }
        AssertSameBytes(d, m);
        AssertLineStarts(d, m.LineStarts());

        while (d.CanUndo) d.Undo();
        AssertSameBytes(d, original);
        AssertLineStarts(d, original.LineStarts());
    }

    [Fact]
    [Trait("Category", "Large")]
    public async Task After_inserting_text_into_a_10_GiB_file_the_search_finds_it_with_offsets_and_line_numbers_of_the_index()
    {
        using var dir = new TempDir();
        string path = dir.File("huge.txt");
        if (!HasRoom(path)) return;
        var r = new Random(9);
        var m = Create(path, r, 1500);
        byte[][] inserts = ["\r"u8.ToArray(), "\n"u8.ToArray(), "\r\n"u8.ToArray(), "x"u8.ToArray(), "a\rb\nc"u8.ToArray(), "\nneedle q\r\n"u8.ToArray()];

        using var d = Doc.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        for (int step = 0; step < 300; step++) Edit(d, m, r, inserts);
        // the search must also find text inserted into the filler far from any island
        long far = (5L << 30) + 12345;
        while (m.Islands.Any(i => i.Start < far + 64 && far - 64 < i.Start + i.Data.Length)) far += 1 << 14;
        var marker = "\nneedle inserted\n"u8.ToArray();
        Assert.True(d.Insert(far, marker).Applied);
        m.Replace(far, 0, marker);

        AssertSameBytes(d, m);
        var starts = m.LineStarts();
        AssertLineStarts(d, starts);

        var expected = new List<long>();
        foreach (var (s, data) in m.Runs())
            for (int at = 0, i; (i = data.AsSpan(at).IndexOf(Needle)) >= 0; at += i + Needle.Length) expected.Add(s + at + i);
        Assert.Contains(far + 1, expected);

        var sw = Stopwatch.StartNew();
        var (hits, summary) = await SearchWorker.CollectAsync(new SearchPattern("needle"), new SearchScope([], [LiveBuffer.Of(d)]),
            new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false });
        output.WriteLine($"search over {Size >> 30} GiB: {sw.Elapsed.TotalSeconds:F1} s, {hits.Count} hits");
        Assert.Equal(SearchEnd.Completed, summary.End);
        Assert.Equal(expected.Count, summary.TotalMatches);
        var ordered = hits.OrderBy(h => h.Range.Start).ToList();
        Assert.Equal(expected, ordered.Select(h => h.Range.Start).ToList());
        foreach (var h in ordered)
        {
            int naive = starts.BinarySearch(h.Range.Start) is var f && f < 0 ? ~f - 1 : f;
            Assert.Equal(naive + 1, h.Line);
            if (starts[naive + 1 < starts.Count ? naive + 1 : naive] - starts[naive] > 1 << 20 || naive + 1 == starts.Count) continue; // a gigantic filler line: its column count would read GiBs
            Assert.True(d.TryGetPosition(h.Range.Start, out var p));
            Assert.Equal(naive, p.Line);
        }
    }

    private static void TypeDigits(HexEditor hex, string digits)
    {
        foreach (char c in digits) Assert.True(hex.TypeNibble(Convert.ToInt32(c.ToString(), 16)).Applied);
    }

    /// <summary>
    /// P3.5.9: what the hex editor typed over and into a 10 GiB file is what the text reads through the line index and what the text search finds.
    /// The edits go in ascending order, so a recorded offset is not moved by a later edit.
    /// </summary>
    [Fact]
    [Trait("Category", "Large")]
    public async Task After_hex_overwrites_and_inserts_in_a_10_GiB_file_the_text_and_the_search_read_the_new_bytes()
    {
        using var dir = new TempDir();
        string path = dir.File("huge.txt");
        if (!HasRoom(path)) return;
        var r = new Random(13);
        var m = Create(path, r, 1500);

        // kinds: 0 "needle" over a row start, 1 a LF typed as digits over a filler byte, 2 text inserted in the character column, 3 bytes inserted as digits
        var targets = new SortedDictionary<long, int>();
        foreach (var (s, data) in m.Islands.OrderBy(_ => r.Next()).Take(60))
        {
            int row = Enumerable.Range(1, Math.Max(0, data.Length - Needle.Length)).FirstOrDefault(i => data[i - 1] == '\n' && Ascii.IsValid(data.AsSpan(i, Needle.Length)));
            if (row > 0) targets[s + row] = r.Next(2) * 2;
        }
        bool Free(long p) => !m.Islands.Any(i => i.Start - 16 < p && p < i.Start + i.Data.Length + 16) && !targets.Keys.Any(k => Math.Abs(k - p) < 16);
        foreach (long hot in new[] { (1L << 32) + 64, (5L << 30) + 12345, Size - 4096 })
        {
            long p = hot;
            while (!Free(p)) p -= 1 << 14;
            targets[p] = 3;
        }
        while (targets.Count < 120)
        {
            long p = r.NextInt64(Size);
            if (Free(p)) targets[p] = 1 + r.Next(3);
        }

        using var d = Doc.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        var hex = new HexEditor(d, HexCharset.Utf8);
        const string typedText = "\nneedle hex č\n";
        byte[] typed = Encoding.UTF8.GetBytes(typedText), digits = "\nneedle\n"u8.ToArray();
        var written = new List<long>(); // where a needle the hex editor wrote starts in the result
        long shift = 0;
        foreach (var (p, kind) in targets)
        {
            long at = p + shift;
            hex.Insert = kind >= 2;
            hex.Column = kind is 0 or 2 ? HexColumn.Text : HexColumn.Hex;
            hex.MoveTo(at);
            switch (kind)
            {
                case 0:
                    Assert.True(hex.TypeText("needle").Applied);
                    m.Replace(at, Needle.Length, Needle);
                    written.Add(at);
                    break;
                case 1:
                    TypeDigits(hex, "0A");
                    m.Replace(at, 1, [0x0A]);
                    break;
                case 2:
                    Assert.True(hex.TypeText(typedText).Applied);
                    m.Replace(at, 0, typed);
                    written.Add(at + 1);
                    shift += typed.Length;
                    break;
                default:
                    TypeDigits(hex, Convert.ToHexString(digits));
                    m.Replace(at, 0, digits);
                    written.Add(at + 1);
                    shift += digits.Length;
                    break;
            }
        }
        Assert.Contains(targets.Values, k => k == 0);
        Assert.Contains(targets.Values, k => k == 1);

        AssertSameBytes(d, m);
        var starts = m.LineStarts();
        AssertLineStarts(d, starts);
        int LineOf(long offset) => starts.BinarySearch(offset) is var f && f < 0 ? ~f - 1 : f;

        // every written needle starts a short row; its text, read through the line index, is the new bytes
        foreach (long at in written)
        {
            int line = LineOf(at);
            Assert.Equal(starts[line], at);
            long end = line + 1 < starts.Count ? starts[line + 1] : m.Length;
            Assert.InRange(end - at, Needle.Length, 4096);
            Assert.True(d.TryGetOffset(new LinePosition(line, 0), out long off));
            Assert.Equal(at, off);
            string text = d.ReadText(new ByteRange(at, end));
            Assert.Equal(Encoding.UTF8.GetString(m.Read(at, end)), text);
            Assert.StartsWith("needle", text);
        }

        var expected = new List<long>();
        foreach (var (s, data) in m.Runs())
            for (int at = 0, i; (i = data.AsSpan(at).IndexOf(Needle)) >= 0; at += i + Needle.Length) expected.Add(s + at + i);
        var sw = Stopwatch.StartNew();
        var (hits, summary) = await SearchWorker.CollectAsync(new SearchPattern("needle"), new SearchScope([], [LiveBuffer.Of(d)]),
            new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false });
        output.WriteLine($"search over {Size >> 30} GiB after {targets.Count} hex edits: {sw.Elapsed.TotalSeconds:F1} s, {hits.Count} hits");
        Assert.Equal(SearchEnd.Completed, summary.End);
        Assert.Equal(expected.Count, summary.TotalMatches);
        var found = hits.Select(h => h.Range.Start).Order().ToList();
        Assert.Equal(expected, found);
        Assert.All(written, at => Assert.Contains(at, found));
        Assert.All(hits, h => Assert.Equal(LineOf(h.Range.Start) + 1, h.Line));
    }

    /// <summary>Edit at a random place plus the first line lookup after it, for two index block sizes. Prints p95; the block size is chosen from this, not here.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void Random_edit_cost_for_two_index_block_sizes_over_a_10_GiB_file()
    {
        using var dir = new TempDir();
        string path = dir.File("huge.txt");
        if (!HasRoom(path)) return;
        var m = Create(path, new Random(5), 2500);
        foreach (int block in new[] { 1 << 18, 1 << 20 })
        {
            var r = new Random(3);
            using var d = Doc.Open(path, new PagedBackendFactory(indexBlockSize: block), new DocumentOptions { Format = TestFormat.Utf8.Format });
            WaitForIndex(d);
            var ms = new List<double>();
            for (int i = 0; i < 300; i++)
            {
                long at = m.Islands[r.Next(m.Islands.Count)].Start + 1;
                var sw = Stopwatch.StartNew();
                d.Insert(at, "x"u8.ToArray());
                d.TryGetOffset(new LinePosition(r.Next(2000), 0), out _);
                ms.Add(sw.Elapsed.TotalMilliseconds);
            }
            ms.Sort();
            output.WriteLine($"index block {block >> 10} KiB: p50 {ms[ms.Count / 2]:F3} ms, p95 {ms[ms.Count * 95 / 100]:F3} ms");
        }
    }
}
