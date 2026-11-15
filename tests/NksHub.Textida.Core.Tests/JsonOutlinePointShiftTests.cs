using System.Text;
using NksHub.Textida.Core.Data;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class JsonOutlinePointShiftTests
{
    private static Doc Make(string text) => Doc.FromBytes(Encoding.UTF8.GetBytes(text), new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8 });

    private static void MatchesFresh(Doc doc, JsonOutline outline, bool comments = false)
        => MatchesFresh(doc.GetSnapshot(), outline, comments);

    private static void MatchesFresh(IByteSnapshot source, JsonOutline outline, bool comments = false)
    {
        outline.ScanToEnd();
        using var fresh = new JsonOutline(source, 0, TextCodec.Utf8, comments);
        fresh.ScanToEnd();
        Assert.Equal(fresh.State, outline.State);
        Assert.Equal(fresh.Error, outline.Error);
        for (long n = 0; n < fresh.State.Lines; n += 509)
        {
            var expected = fresh.GetLine(n)!.Value;
            var actual = outline.GetLine(n)!.Value;
            Assert.Equal((expected.Kind, expected.KeyStart, expected.KeyEnd, expected.ValueStart, expected.ValueEnd, expected.End),
                (actual.Kind, actual.KeyStart, actual.KeyEnd, actual.ValueStart, actual.ValueEnd, actual.End));
            Assert.Equal(fresh.Render(expected).Text, outline.Render(actual).Text);
            Assert.Equal(fresh.PathOf(expected), outline.PathOf(actual));
            Assert.Equal(fresh.LineOfOffset(expected.ValueStart), outline.LineOfOffset(expected.ValueStart));
        }
        Assert.Equal(fresh.Render(fresh.GetLine(fresh.State.Lines - 1)!.Value).Text, outline.Render(outline.GetLine(outline.State.Lines - 1)!.Value).Text);
    }

    private sealed class Bytes(byte[] bytes) : IByteSnapshot
    {
        public long ByteLength => bytes.Length;
        public int Read(long offset, Span<byte> destination)
        {
            int length = (int)Math.Min(destination.Length, bytes.Length - offset);
            bytes.AsSpan((int)offset, length).CopyTo(destination);
            return length;
        }
    }

    [Fact]
    public void A_running_scan_appends_states_after_a_lazy_shift_without_reusing_old_offsets()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("[" + string.Concat(Enumerable.Repeat("\"text\",", 3_000_000)));
        bytes[^1] = (byte)']';
        using var outline = new JsonOutline(new Bytes(bytes), 0, TextCodec.Utf8);
        using var paused = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        int held = 0;
        outline.Progress += (_, _) =>
        {
            var state = outline.State;
            if (!state.Complete && state.Lines >= 200_000 && Interlocked.CompareExchange(ref held, 1, 0) == 0)
            {
                paused.Set();
                resume.Wait(TimeSpan.FromSeconds(30));
            }
            if (outline.State.Complete) done.Set();
        };
        outline.GetLines(0, 10);
        outline.Start();
        try
        {
            Assert.True(paused.Wait(TimeSpan.FromSeconds(30)));
            Assert.True(outline.SegmentCount > 256);
            Assert.False(outline.State.Complete);
            var next = new Bytes([.. bytes.AsSpan(0, 3), (byte)'X', .. bytes.AsSpan(3)]);
            Assert.True(outline.Update(next, 3, 0, 1));
            resume.Set();
            Assert.True(done.Wait(TimeSpan.FromSeconds(30)));
            Assert.Equal((3_000_002L, true), outline.State);
            MatchesFresh(next, outline);
        }
        finally { resume.Set(); }
    }

    [Fact]
    public void Edits_across_hundreds_of_thousands_of_lines_keep_offsets_and_resume_after_a_rescan()
    {
        const int records = 300_000;
        using var doc = Make("[" + string.Join(',', Enumerable.Range(0, records).Select(i => "\"value" + i + "\"")) + "]");
        using var outline = new JsonOutline(doc);
        outline.ScanToEnd();
        Assert.True(outline.SegmentCount > 512);
        int[] at = [0, 131_070, 131_071, 131_072, 150_000, 262_142, 262_143, records - 1];
        for (int round = 0; round < 3; round++)
        {
            foreach (int record in at)
            {
                var line = outline.GetLine(record + 1)!.Value;
                long start = line.ValueStart + 2;
                if (round == 1) doc.Delete(new(start, start + 1));
                else doc.Insert(start, "XYZ"u8.ToArray());
            }
            MatchesFresh(doc, outline);
        }
        var middle = outline.GetLine(150_001)!.Value;
        doc.Insert(middle.ValueStart + 1, "\""u8.ToArray());
        MatchesFresh(doc, outline);
        doc.Undo();
        MatchesFresh(doc, outline);
        Assert.Null(outline.Error);
        Assert.Equal(records + 2L, outline.State.Lines);
    }

    [Fact]
    public void Jsonc_keys_before_long_comments_and_errors_after_edits_keep_their_original_ranges()
    {
        string json = "{\"before\":\"text\",\"key\" /*\n" + string.Concat(Enumerable.Repeat("comment\n", 140_000)) + "*/ : \"last\"}";
        using var doc = Make(json);
        using var outline = new JsonOutline(doc, comments: true);
        outline.ScanToEnd();
        Assert.True(outline.SegmentCount > 256);
        outline.GetLines(0, 10);
        doc.Insert(json.IndexOf("text", StringComparison.Ordinal) + 1, "longer"u8.ToArray());
        MatchesFresh(doc, outline, comments: true);
        long comment = outline.GetLine(10_000)!.Value.ValueStart + 2;
        doc.Insert(comment, "abc"u8.ToArray());
        MatchesFresh(doc, outline, comments: true);
        doc.Delete(new(comment, comment + 2));
        MatchesFresh(doc, outline, comments: true);
        doc.Insert(0, "!"u8.ToArray());
        MatchesFresh(doc, outline, comments: true);
        Assert.NotNull(outline.Error);
        doc.Undo();
        MatchesFresh(doc, outline, comments: true);
        Assert.Null(outline.Error);
        Assert.Contains(outline.GetLines(outline.State.Lines - 3, 3), l => outline.Render(l).Text.Contains("\"last\"", StringComparison.Ordinal));
    }
}
