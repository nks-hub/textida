using System.Text;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class LineDiffTests
{
    private static DiffSource Src(string text, TextCodec? codec = null) => DiffSource.OfText(text, codec);

    private static List<string> Lines(DiffSource s) => LineIndex.Build(s).ReadLines(0, int.MaxValue);

    private static string Text(DiffSource s)
    {
        var bytes = new byte[s.Length];
        s.Snapshot.Read(0, bytes);
        return s.Codec.GetString(bytes);
    }

    /// <summary>
    /// Moves the target side to the other one the way the view does: the first hunk is applied as one edit on the bytes of the target,
    /// the diff is made again, until nothing differs. Each step must remove at least one hunk.
    /// </summary>
    private static string ApplyAll(TextDiff diff, DiffSide target)
    {
        var mine = (target == DiffSide.A ? diff.A : diff.B).Source;
        var other = (target == DiffSide.A ? diff.B : diff.A).Source;
        string current = Text(mine), start = current;
        var log = new StringBuilder();
        var d = diff;
        for (int guard = 3 * diff.Hunks.Count + 3;d.Hunks.Count > 0; guard--)
        {
            Assert.True(guard > 0, $"the transfer does not converge: start={Esc(start)} first={string.Join(";", diff.Hunks)} log={log} now={Esc(current)} other={Esc(Text(other))} hunks={string.Join(";", d.Hunks)}");
            var edit = DiffTransfer.Plan(d, 0, target);
            log.Append($"[{current.Length}:{string.Join(";", d.Hunks)} -> {edit.Range} {Esc(Encoding.UTF8.GetString(edit.Bytes))}] ");
            var list = Encoding.UTF8.GetBytes(current).ToList();
            if (mine.Codec != TextCodec.Utf8) list = [.. mine.Codec.Encode(current)];
            list.RemoveRange((int)edit.Range.Start, (int)edit.Range.Length);
            list.InsertRange((int)edit.Range.Start, edit.Bytes);
            current = mine.Codec.GetString([.. list]);
            var changed = DiffSource.OfText(current, mine.Codec);
            d = target == DiffSide.A ? LineDiff.Compute(changed, other) : LineDiff.Compute(other, changed);
        }
        return current;
    }

    [Fact]
    public void Insert_delete_and_replace_are_hunks_of_lines()
    {
        var d = LineDiff.Compute(Src("a\nb\nc\nd\ne\n"), Src("a\nB\nc\nx\ny\nd\n"));
        Assert.True(d.Exact);
        Assert.Equal([new DiffHunk(1, 1, 1, 1), new DiffHunk(3, 0, 3, 2), new DiffHunk(4, 1, 6, 0)], d.Hunks);
    }

    [Fact]
    public void Equal_and_empty_inputs_give_no_hunks_or_one_whole_block()
    {
        Assert.True(LineDiff.Compute(Src("a\r\nb"), Src("a\r\nb")).IsIdentical);
        Assert.True(LineDiff.Compute(Src(""), Src("")).IsIdentical);
        Assert.Equal([new DiffHunk(0, 1, 0, 3)], LineDiff.Compute(Src(""), Src("x\ny\nz")).Hunks.Select(h => h with { ACount = 1 }).ToList());
    }

    [Fact]
    public void The_terminator_style_does_not_make_a_line_differ()
    {
        Assert.True(LineDiff.Compute(Src("a\r\nb\r\nc\r\n"), Src("a\nb\rc\n")).IsIdentical);
        var d = LineDiff.Compute(Src("a\r\nb\r\nc"), Src("a\nX\nc"));
        Assert.Equal([new DiffHunk(1, 1, 1, 1)], d.Hunks);
    }

    [Fact]
    public void A_lone_cr_before_a_changed_lf_does_not_split_a_terminator()
    {
        var d = LineDiff.Compute(Src("ab\rcd"), Src("ab\r\nxx"));
        Assert.Equal(["ab", "xx"], Lines(Src(ApplyAll(d, DiffSide.A))));
        Assert.Equal(["ab", "cd"], Lines(Src("ab\rcd")));
        Assert.Equal(["ab", "cd"], Lines(Src(ApplyAll(d, DiffSide.B))));
    }

    [Fact]
    public void Random_texts_are_minimal_and_transferring_every_hunk_makes_the_sides_equal()
    {
        var r = new Random(81);
        for (int i = 0; i < 1500; i++)
        {
            string a = Gen.LineSoup(r, r.Next(0, 24)), b = Gen.LineSoup(r, r.Next(0, 24));
            if (i % 3 == 0) b = Mutate(a, r);
            var d = LineDiff.Compute(Src(a), Src(b));
            var la = Lines(Src(a));
            var lb = Lines(Src(b));
            Assert.Equal(la.Count, d.A.LineCount);
            Assert.Equal(lb.Count, d.B.LineCount);
            Assert.Equal(la.Count + lb.Count - 2 * Lcs(la, lb), d.Hunks.Sum(h => h.ACount + h.BCount));
            Assert.True(lb.SequenceEqual(Lines(Src(ApplyAll(d, DiffSide.A)))), Show(a, b, d));
            Assert.True(la.SequenceEqual(Lines(Src(ApplyAll(d, DiffSide.B)))), Show(a, b, d) + " toA=" + Esc(ApplyAll(d, DiffSide.B)));
        }
    }

    private static string Esc(string t) => t.Replace("\r", "\\r").Replace("\n", "\\n");
    private static string Show(string a, string b, TextDiff d) => $"A={Esc(a)} B={Esc(b)} hunks={string.Join(";", d.Hunks)} got={Esc(ApplyAll(d, DiffSide.A))}";

    private static string Mutate(string text, Random r)
    {
        var sb = new StringBuilder(text);
        for (int k = r.Next(1, 4); k > 0 && sb.Length > 0; k--)
        {
            int at = r.Next(sb.Length);
            if (r.Next(2) == 0) sb.Remove(at, Math.Min(r.Next(1, 5), sb.Length - at)); else sb.Insert(at, r.Next(2) == 0 ? "zz\n" : "q");
        }
        return sb.ToString();
    }

    private static int Lcs(List<string> a, List<string> b)
    {
        var t = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
            for (int j = b.Count - 1; j >= 0; j--)
                t[i, j] = a[i] == b[j] ? t[i + 1, j + 1] + 1 : Math.Max(t[i + 1, j], t[i, j + 1]);
        return t[0, 0];
    }

    [Fact]
    public void Terminators_at_the_end_of_the_file_are_kept_right_by_a_transfer()
    {
        foreach (var (a, b) in new[] { ("q", "q\n"), ("q\n", "q"), ("q", "q\nx"), ("q\nx", "q"), ("", "\nx"), ("q\n", "q\n\nx"), ("x\ny", "y"), ("a\r\nb", "a\r\nb\r\nc") })
        {
            var d = LineDiff.Compute(Src(a), Src(b));
            Assert.Equal(b, ApplyAll(d, DiffSide.A));
            Assert.Equal(a, ApplyAll(d, DiffSide.B).Replace("\n", "\n") is var back && Lines(Src(back)).SequenceEqual(Lines(Src(a))) ? a : back);
        }
    }

    [Fact]
    public void Utf16_and_a_single_byte_codec_are_compared_by_their_lines()
    {
        var d = LineDiff.Compute(Src("a\nb\nc\n", TextCodec.Utf16LE), Src("a\nB\nc\n", TextCodec.Utf16LE));
        Assert.Equal([new DiffHunk(1, 1, 1, 1)], d.Hunks);
        Assert.Equal("a\nB\nc\n", ApplyAll(d, DiffSide.A));

        var mixed = LineDiff.Compute(Src("příliš\nžluťoučký\n", TextCodec.Utf8), Src("příliš\nžluťoučký\n", TextCodec.Cp1250));
        Assert.True(mixed.IsIdentical);
        var changed = LineDiff.Compute(Src("příliš\nkůň\n", TextCodec.Utf8), Src("příliš\nkoň\n", TextCodec.Cp1250));
        Assert.Equal([new DiffHunk(1, 1, 1, 1)], changed.Hunks);
        Assert.Equal("příliš\nkoň\n", ApplyAll(changed, DiffSide.A));
    }

    [Fact]
    public void Transfer_to_a_codec_that_cannot_hold_the_text_is_reported_as_lossy()
    {
        var d = LineDiff.Compute(Src("a\nb\n", TextCodec.Cp1250), Src("a\n€ 中\n", TextCodec.Utf8));
        Assert.True(DiffTransfer.Plan(d, 0, DiffSide.A).Lossy);
        Assert.False(DiffTransfer.Plan(d, 0, DiffSide.B).Lossy);
    }

    [Fact]
    public void A_bound_makes_the_diff_coarser_but_still_correct()
    {
        var a = string.Join("\n", Enumerable.Range(0, 400).Select(i => "line " + i));
        var b = string.Join("\n", Enumerable.Range(0, 400).Select(i => i % 2 == 0 ? "line " + i : "other " + i));

        var exact = LineDiff.Compute(Src(a), Src(b));
        Assert.True(exact.Exact);
        Assert.Equal(200, exact.Hunks.Count);

        var capped = LineDiff.Compute(Src(a), Src(b), new DiffLimits(MaxEditDistance: 50));
        Assert.False(capped.Exact);
        Assert.Equal(b, ApplyAll(capped, DiffSide.A));
        Assert.True(capped.Hunks.Count < exact.Hunks.Count);

        var few = LineDiff.Compute(Src(a), Src(b), new DiffLimits(MaxMiddleLines: 100));
        Assert.False(few.Exact);
        Assert.Single(few.Hunks);
        Assert.Equal(b, ApplyAll(few, DiffSide.A));

        var budget = LineDiff.Compute(Src(a), Src(b), new DiffLimits(MaxSteps: 500));
        Assert.False(budget.Exact);
        Assert.Equal(b, ApplyAll(budget, DiffSide.A));

        // a difference that fits the bound is exact even when the bound is small
        var small = LineDiff.Compute(Src(a), Src(a.Replace("line 200", "changed")), new DiffLimits(MaxEditDistance: 2));
        Assert.True(small.Exact);
        Assert.Equal([new DiffHunk(200, 1, 200, 1)], small.Hunks);
    }

    [Fact]
    public void Cancellation_stops_the_diff()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => LineDiff.Compute(Src("a\nb"), Src("a\nc"), ct: cts.Token));
    }

    [Fact]
    public void A_document_and_a_file_on_disk_are_compared_through_their_snapshots()
    {
        using var temp = new TempFile(Encoding.UTF8.GetBytes("one\ntwo\nthree\n"));
        using var doc = Doc.Open(temp.Path, new PagedBackendFactory(startIndexing: false));
        doc.Replace(new ByteRange(4, 7), "2"u8.ToArray());
        using var disk = new FileByteSnapshot(temp.Path);
        var d = LineDiff.Compute(DiffSource.Of(doc), new DiffSource(disk, TextCodec.Utf8));
        Assert.Equal([new DiffHunk(1, 1, 1, 1)], d.Hunks);
        Assert.Equal(["2"], d.A.ReadLines(1, 1));
        Assert.Equal(["two"], d.B.ReadLines(1, 1));
    }

    [Fact]
    public void A_bom_is_not_part_of_the_first_line()
    {
        using var doc = Doc.FromBytes(new byte[] { 0xEF, 0xBB, 0xBF }.Concat("a\nb"u8.ToArray()).ToArray(),new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8Bom });
        Assert.True(LineDiff.Compute(DiffSource.Of(doc), Src("a\nb")).IsIdentical);
    }

    [Fact]
    public void The_line_index_finds_and_reads_lines_across_strides()
    {
        string text = string.Join("\r\n", Enumerable.Range(0, 3000).Select(i => $"row {i}")) + "\r\n";
        var index = LineIndex.Build(Src(text));
        Assert.Equal(3001, index.LineCount);
        Assert.Equal(["row 700", "row 701", "row 702"], index.ReadLines(700, 3));
        Assert.Equal(["row 2999", ""], index.ReadLines(2999, 5));
        long at = index.OffsetOf(1234);
        Assert.Equal(text.IndexOf("row 1234", StringComparison.Ordinal), at);
        Assert.Equal(1234, index.LineOf(at + 3));
        Assert.Equal(1235, index.LineAtOrAfter(at + 1));
        Assert.Equal(1234, index.LineAtOrAfter(at));
        Assert.Equal(text.Length, index.OffsetOf(3001));
    }

    // ---- layout ----

    [Fact]
    public void Rows_keep_both_sides_level_and_map_lines_across_inserted_and_removed_blocks()
    {
        // a: 0..9; b: line 3 gets three inserted lines before it, lines 6 and 7 are removed
        var a = string.Join("\n", Enumerable.Range(0, 10).Select(i => "l" + i));
        var b = string.Join("\n", ["l0", "l1", "l2", "n1", "n2", "n3", "l3", "l4", "l5", "l8", "l9"]);
        var d = LineDiff.Compute(Src(a), Src(b));
        var layout = d.Layout;
        Assert.Equal(2, d.Hunks.Count);
        Assert.Equal(13, layout.RowCount);

        Assert.Equal(new DiffRow(2, 2, DiffRowKind.Equal), layout.Row(2));
        Assert.Equal(new DiffRow(-1, 3, DiffRowKind.Added), layout.Row(3));
        Assert.Equal(new DiffRow(3, 6, DiffRowKind.Equal), layout.Row(6));
        Assert.Equal(new DiffRow(6, -1, DiffRowKind.Removed), layout.Row(9));
        Assert.Equal(new DiffRow(9, 10, DiffRowKind.Equal), layout.Row(12));

        // every line of either side lands on a row that shows it, and equal rows map to the same text
        var la = Lines(Src(a));
        var lb = Lines(Src(b));
        for (long line = 0; line < la.Count; line++) Assert.Equal(line, layout.Row(layout.RowOf(DiffSide.A, line)).A);
        for (long line = 0; line < lb.Count; line++) Assert.Equal(line, layout.Row(layout.RowOf(DiffSide.B, line)).B);
        for (long line = 0; line < la.Count; line++)
        {
            long other = layout.MapLine(DiffSide.A, line);
            if (layout.Row(layout.RowOf(DiffSide.A, line)).Kind == DiffRowKind.Equal) Assert.Equal(la[(int)line], lb[(int)other]);
        }

        Assert.Equal(6, layout.MapLine(DiffSide.A, 3));   // l3 stands next to l3, behind the three inserted lines
        Assert.Equal(3, layout.MapLine(DiffSide.B, 4));   // inside the inserted block the other side stays where the block sits
        Assert.Equal(2, layout.MapLine(DiffSide.A, 2));
        Assert.Equal(9, layout.MapLine(DiffSide.A, 6));   // the removed lines sit before l8 of the right side
        Assert.Equal(10, layout.MapLine(DiffSide.A, 9));
        Assert.Equal(8, layout.MapLine(DiffSide.B, 9));
    }

    [Fact]
    public void Layout_of_random_diffs_is_consistent()
    {
        var r = new Random(7);
        for (int i = 0; i < 300; i++)
        {
            string a = string.Join("\n", Enumerable.Range(0, r.Next(0, 30)).Select(_ => "x" + r.Next(6)));
            string b = string.Join("\n", Enumerable.Range(0, r.Next(0, 30)).Select(_ => "x" + r.Next(6)));
            var layout = LineDiff.Compute(Src(a), Src(b)).Layout;
            long seenA = 0, seenB = 0;
            for (long row = 0; row < layout.RowCount; row++)
            {
                var x = layout.Row(row);
                if (x.A >= 0) Assert.Equal(seenA++, x.A);
                if (x.B >= 0) Assert.Equal(seenB++, x.B);
                Assert.True(x.A >= 0 || x.B >= 0);
            }
            Assert.Equal(layout.LinesA, seenA);
            Assert.Equal(layout.LinesB, seenB);
        }
    }
}

[Trait("Category", "Large")]
public class LineDiffLargeTests
{
    private static void Write(string path, int lines, Func<int, string> line)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16);
        for (int i = 0; i < lines; i++) w.Write(line(i) + "\n");
    }

    [Fact]
    public void Two_large_paged_files_are_compared_without_loading_either()
    {
        const int Lines = 3_000_000;
        using var f1 = new TempFile();
        using var f2 = new TempFile();
        Write(f1.Path, Lines, i => $"{i:D8} the quick brown fox jumps over the lazy dog");
        Write(f2.Path, Lines, i => i == 40 ? "changed near the start" : i == 1_500_000 ? "changed in the middle\nand one more" : i == Lines - 2 ? "changed near the end" : $"{i:D8} the quick brown fox jumps over the lazy dog");
        using var da = Doc.Open(f1.Path, PagedBackendFactory.Instance);
        using var db = Doc.Open(f2.Path, PagedBackendFactory.Instance);
        Assert.True(da.ByteLength > 150_000_000);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var d = LineDiff.Compute(DiffSource.Of(da), DiffSource.Of(db));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(d.Exact);
        Assert.Equal([new DiffHunk(40, 1, 40, 1), new DiffHunk(1_500_000, 1, 1_500_000, 2), new DiffHunk(Lines - 2, 1, Lines - 1, 1)], d.Hunks);
        Assert.Equal(Lines + 1, d.A.LineCount);
        // a small fraction of the 150 MB: two line indexes (8 bytes per 256 lines), the buffers and the hashes of the middle between the edits
        Assert.True(allocated < 120_000_000, $"{allocated} bytes allocated");
        Assert.Equal(["changed in the middle", "and one more"], d.B.ReadLines(1_500_000, 2));
        Assert.Equal($"{1_500_001:D8} the quick brown fox jumps over the lazy dog", d.A.ReadLines(1_500_001, 1)[0]);
    }

    [Fact]
    public void Files_that_differ_everywhere_stay_inside_the_bounds_and_the_diff_is_still_correct()
    {
        const int Lines = 400_000;
        using var f1 = new TempFile();
        using var f2 = new TempFile();
        Write(f1.Path, Lines, i => $"left {i}");
        Write(f2.Path, Lines, i => i % 3 == 0 ? $"right {i}" : $"left {i}");
        using var da = Doc.Open(f1.Path, PagedBackendFactory.Instance);
        using var db = Doc.Open(f2.Path, PagedBackendFactory.Instance);

        var d = LineDiff.Compute(DiffSource.Of(da), DiffSource.Of(db));

        Assert.Single(d.Hunks);
        Assert.False(d.Exact); // 133,334 changed lines are far beyond the default edit distance of 10,000
        var layout = d.Layout;
        for (long row = 0; row < layout.RowCount; row += 997)
        {
            var r = layout.Row(row);
            if (r.Kind == DiffRowKind.Equal) Assert.Equal(d.A.ReadLines(r.A, 1)[0], d.B.ReadLines(r.B, 1)[0]);
        }
    }
}
