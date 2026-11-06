using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.Core.Data;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class JsonOutlineTests
{
    private static Doc Make(string text) => Doc.FromBytes(Encoding.UTF8.GetBytes(text), new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8 });

    private static List<string> Pretty(JsonOutline outline)
    {
        outline.ScanToEnd();
        return [.. outline.GetLines(0, int.MaxValue).Select(l => outline.Render(l).Text)];
    }

    private static string[] Reference(string json) =>
        JsonNode.Parse(json, documentOptions: new JsonDocumentOptions())!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Split(["\r\n", "\n"], StringSplitOptions.None);

    /// <summary>Records of a fixed width repeated without end, read on demand: a JSON array of any length that takes no memory.</summary>
    private sealed class Synthetic(long records) : IByteSnapshot
    {
        public static readonly byte[] Record = "{\"id\":\"abcdefgh\",\"n\":12345,\"ok\":true},"u8.ToArray();
        public long ByteLength { get; } = 1 + records * Record.Length; // '[' + records, the comma of the last one becomes ']'
        public long Reads;

        public int Read(long offset, Span<byte> destination)
        {
            int n = (int)Math.Min(destination.Length, ByteLength - offset);
            Interlocked.Add(ref Reads, n);
            for (int i = 0; i < n; i++)
            {
                long at = offset + i;
                destination[i] = at == 0 ? (byte)'[' : at == ByteLength - 1 ? (byte)']' : Record[(at - 1) % Record.Length];
            }
            return n;
        }
    }

    [Theory]
    [InlineData("{\"a\":1,\"b\":[1,2,{\"c\":null}],\"d\":{},\"e\":[],\"f\":\"text\",\"g\":true,\"h\":-1.5e3}")]
    [InlineData("[[],[[1]],{\"x\":{\"y\":{\"z\":[false]}}}]")]
    [InlineData("  {  \"spaced\" :\t[ 1 ,\n 2 ] ,\r\n \"k\" : \"v\" }  ")]
    [InlineData("42")]
    [InlineData("\"only a string\"")]
    [InlineData("{\"escaped \\\"key\\\"\":\"a\\\\b\\n\"}")]
    public void Single_line_json_is_formatted_like_a_reference_pretty_print(string json)
    {
        using var doc = Make(json);
        using var outline = new JsonOutline(doc);
        Assert.Equal(Reference(json), Pretty(outline));
        Assert.Null(outline.Error);
    }

    [Fact]
    public void Json_lines_are_values_one_after_another()
    {
        using var doc = Make("{\"a\":1}\n{\"a\":2}\n[3]\n");
        using var outline = new JsonOutline(doc);
        Assert.Equal(["{", "  \"a\": 1", "}", "{", "  \"a\": 2", "}", "[", "  3", "]"], Pretty(outline));
        Assert.Null(outline.Error);
    }

    [Fact]
    public void Invalid_json_shows_the_source_text_from_the_error_with_its_place()
    {
        string json = "{\"a\":1,\"b\":,\"c\":2}\nnext line";
        using var doc = Make(json);
        using var outline = new JsonOutline(doc);
        var lines = Pretty(outline);
        Assert.Equal(["{", "  \"a\": 1,", "\"b\":,\"c\":2}", "next line"], lines);
        Assert.Equal(json.IndexOf("\"b\":", StringComparison.Ordinal) + 4, outline.Error!.Value.Offset);
        var raw = outline.GetLines(2, 2);
        Assert.All(raw, l => Assert.Equal(JsonLineKind.Raw, l.Kind));
    }

    [Theory]
    [InlineData("{\"a\":\"never closed", 5)]
    [InlineData("[1,2", 4)]
    [InlineData("[1,2]]", 5)]
    [InlineData("{\"a\" 1}", 5)]
    public void Each_kind_of_error_is_found_where_it_is(string json, long at)
    {
        using var doc = Make(json);
        using var outline = new JsonOutline(doc);
        Pretty(outline);
        Assert.Equal(at, outline.Error!.Value.Offset);
    }

    [Fact]
    public void The_first_screen_of_a_large_single_line_file_reads_a_few_blocks()
    {
        var source = new Synthetic(1L << 25); // 1.25 GiB
        Assert.True(source.ByteLength > 1L << 30);
        using var outline = new JsonOutline(source, 0, TextCodec.Utf8);
        var lines = outline.GetLines(0, 60);
        Assert.Equal(60, lines.Count);
        Assert.Equal("[", outline.Render(lines[0]).Text);
        Assert.Equal("  {", outline.Render(lines[1]).Text);
        Assert.Equal("    \"id\": \"abcdefgh\",", outline.Render(lines[2]).Text);
        Assert.Equal("    \"ok\": true", outline.Render(lines[4]).Text);
        Assert.Equal("  },", outline.Render(lines[5]).Text);
        Assert.True(source.Reads <= 256 * 1024, $"read {source.Reads} bytes");
        Assert.Equal("$[9].n", outline.PathOf(lines[9 * 5 + 3]));
    }

    [Fact]
    public void A_full_scan_keeps_one_state_per_segment_and_folds_by_depth()
    {
        const long records = 200_000; // 8 MiB
        var source = new Synthetic(records);
        using var outline = new JsonOutline(source, 0, TextCodec.Utf8);
        outline.ScanToEnd();
        var (count, complete) = outline.State;
        Assert.True(complete);
        Assert.Equal(records * 5 + 2, count);
        Assert.Equal((int)(count / JsonOutline.SegmentLines) + 1, outline.SegmentCount);
        Assert.Equal(count - 1, outline.FindClose(0));
        long last = (records - 1) * 5 + 1;
        Assert.Equal(last + 4, outline.FindClose(last));
        Assert.Null(outline.FindClose(2)); // a value line opens nothing
        Assert.Equal("]", outline.Render(outline.GetLine(count - 1)!.Value).Text);
        long far = source.ByteLength - 4;
        Assert.Equal(count - 3, outline.LineOfOffset(far)); // the "ok" member of the last record
    }

    [Fact]
    public void Collapsing_hides_the_container_and_expanding_brings_it_back()
    {
        using var doc = Make("{\"a\":{\"b\":[1,2,3],\"c\":4},\"d\":5}");
        using var outline = new JsonOutline(doc);
        outline.ScanToEnd();
        var folds = new JsonFolds();
        long open = 1; // "a": {
        long close = outline.FindClose(open)!.Value;
        Assert.Equal(8, close);
        long inner = outline.FindClose(2)!.Value; // "b": [
        Assert.Equal(6, inner);
        folds.Fold(2, inner);
        folds.Fold(open, close);
        Assert.Equal(7, folds.Hidden);
        Assert.Equal(9, folds.LineOfRow(2)); // "d": 5
        Assert.Equal(1, folds.RowOfLine(4)); // inside the fold: the row of its opening line
        Assert.True(folds.Unfold(open));
        Assert.Equal(4, folds.Hidden); // the inner fold stays
        Assert.Equal(7, folds.LineOfRow(3));
        folds.Unfold(2);
        Assert.Equal(0, folds.Hidden);
    }

    [Fact]
    public void A_key_is_found_after_the_caret_and_then_from_the_start()
    {
        using var doc = Make("{\"x\":1,\"list\":[{\"name\":\"a\"},{\"name\":\"b\"}],\"tab\\tkey\":2,\"name\":3}");
        using var outline = new JsonOutline(doc);
        outline.ScanToEnd();
        var names = Enumerable.Range(0, (int)outline.State.Lines).Where(i => outline.KeyText(outline.GetLine(i)!.Value) == "name").ToList();
        Assert.Equal(3, names.Count);
        Assert.Equal(names[0], outline.FindKey("name", 0));
        Assert.Equal(names[1], outline.FindKey("name", names[0]));
        Assert.Equal(names[2], outline.FindKey("name", names[1]));
        Assert.Equal(names[0], outline.FindKey("name", names[2]));
        long tab = outline.FindKey("tab\tkey", 0);
        Assert.Equal("$[\"tab\\tkey\"]", outline.PathOf(outline.GetLine(tab)!.Value));
        Assert.Equal(-1, outline.FindKey("missing", 0));
        Assert.Equal("$.list[1].name", outline.PathOf(outline.GetLine(names[1])!.Value));
    }

    [Fact]
    public async Task Saving_without_changes_keeps_every_byte()
    {
        string dir = Directory.CreateTempSubdirectory("textida-json-").FullName;
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes("\uFEFF{\"a\" : [1, 2],\t\"b\":{\"c\":\"ž\"}}\r\n");
            string path = Path.Combine(dir, "a.json"), copy = Path.Combine(dir, "b.json");
            File.WriteAllBytes(path, bytes);
            using (var doc = Doc.Open(path, new PagedBackendFactory(startIndexing: false)))
            using (var outline = new JsonOutline(doc))
            {
                var lines = Pretty(outline);
                Assert.Equal("    \"c\": \"ž\"", lines[6]);
                var folds = new JsonFolds();
                folds.Fold(1, outline.FindClose(1)!.Value);
                await doc.SaveAsync(copy);
                await doc.SaveAsync();
            }
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(bytes, File.ReadAllBytes(copy));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Typing_into_a_value_of_the_formatted_view_changes_only_its_bytes()
    {
        string json = "{\"name\":\"abc\",\"list\":[1,22,3],\"tail\":{\"k\":\"v\"}}";
        using var doc = Make(json);
        using var outline = new JsonOutline(doc);
        outline.ScanToEnd();
        var shiftedEdits = new List<bool>();
        outline.Edited += (_, e) => shiftedEdits.Add(e.Shifted);
        var line = outline.GetLine(4)!.Value; // "    22,"
        var rendered = outline.Render(line);
        Assert.Equal("    22,", rendered.Text);
        long at = outline.SourceOffsetAt(rendered, 5); // between the two 2s
        Assert.Equal(json.IndexOf("22", StringComparison.Ordinal) + 1, at);
        Assert.Equal(-1, outline.SourceOffsetAt(rendered, 1)); // the indent takes no edit
        doc.Insert(at, "7"u8.ToArray());
        string expected = json.Insert((int)at, "7");
        Assert.Equal(expected, Encoding.UTF8.GetString(doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
        Assert.Equal([true], shiftedEdits);
        Assert.Equal("    272,", outline.Render(outline.GetLine(4)!.Value).Text);
        Assert.Equal(Reference(expected), outline.GetLines(0, 100).Select(l => outline.Render(l).Text));

        var name = outline.Render(outline.GetLine(1)!.Value); // "  "name": "abc","
        long inName = outline.SourceOffsetAt(name, name.Text.IndexOf("abc", StringComparison.Ordinal) + 3);
        doc.Delete(new ByteRange(inName - 1, inName));
        Assert.Equal(expected.Remove((int)inName - 1, 1), Encoding.UTF8.GetString(doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
        Assert.Equal("  \"name\": \"ab\",", outline.Render(outline.GetLine(1)!.Value).Text);
    }

    [Fact]
    public void Edits_that_change_the_structure_rescan_and_match_a_fresh_outline()
    {
        var text = new StringBuilder("[");
        for (int i = 0; i < 3000; i++) text.Append(i > 0 ? "," : "").Append("{\"i\":").Append(i).Append(",\"s\":\"x\"}");
        text.Append(']');
        using var doc = Make(text.ToString());
        using var outline = new JsonOutline(doc);
        outline.ScanToEnd();
        var rng = new Random(7);
        string[] inserts = ["1", "\"", ",\"z\":[1,2]", "}", "{", "]", " ", "\\"];
        bool anyShifted = false, anyRescanned = false;
        outline.Edited += (_, e) => { if (e.Shifted) anyShifted = true; else anyRescanned = true; };
        for (int step = 0; step < 40; step++)
        {
            long at = 1 + rng.NextInt64(doc.ByteLength - 2);
            if (step % 3 == 2) doc.Delete(new ByteRange(at, Math.Min(doc.ByteLength, at + rng.Next(1, 4))));
            else doc.Insert(at, Encoding.UTF8.GetBytes(inserts[rng.Next(inserts.Length)]));
            outline.ScanToEnd();
            using var fresh = new JsonOutline(doc.GetSnapshot(), 0, TextCodec.Utf8);
            fresh.ScanToEnd();
            Assert.Equal(fresh.State, outline.State);
            Assert.Equal(fresh.Error, outline.Error);
            var expected = fresh.GetLines(0, int.MaxValue);
            var actual = outline.GetLines(0, int.MaxValue);
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(fresh.Render(expected[i]).Text, outline.Render(actual[i]).Text);
                if (i % 97 == 0) Assert.Equal(fresh.PathOf(expected[i]), outline.PathOf(actual[i]));
            }
        }
        Assert.True(anyShifted && anyRescanned);
    }

    [Fact]
    public void A_background_scan_reports_progress_and_completes()
    {
        var source = new Synthetic(100_000);
        using var outline = new JsonOutline(source, 0, TextCodec.Utf8);
        using var done = new ManualResetEventSlim();
        outline.Progress += (_, _) => { if (outline.State.Complete) done.Set(); };
        outline.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(30)));
        Assert.Equal(100_000 * 5 + 2, outline.State.Lines);
    }

    [Fact]
    public void A_whole_gibibyte_is_scanned_in_bounded_memory()
    {
        if (Environment.GetEnvironmentVariable("TEXTIDA_HUGE") != "1") return;
        var source = new Synthetic(1L << 25);
        long before = GC.GetTotalMemory(true);
        using var outline = new JsonOutline(source, 0, TextCodec.Utf8);
        outline.ScanToEnd();
        long grown = GC.GetTotalMemory(true) - before;
        Assert.Equal((1L << 25) * 5 + 2, outline.State.Lines);
        Assert.True(grown < 64L << 20, $"grew by {grown} bytes");
        Assert.Equal(outline.State.Lines - 1, outline.FindClose(0));
    }
}
