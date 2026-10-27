using System.Text;
using NksHub.Textida.Core.Csv;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class CsvTableTests
{
    private static Doc Make(string text, TextCodec? codec = null)
    {
        codec ??= TextCodec.Utf8;
        byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
        return Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = codec });
    }

    private static byte[] Bytes(Doc d) => d.ReadBytes(new ByteRange(0, d.ByteLength));
    private static string Text(Doc d) => d.Codec.GetString(Bytes(d)[d.BomLength..]);

    private const string Multi = "id,name,note\r\n1,Ann,\"first\r\nsecond, \"\"quoted\"\"\"\r\n2,Bob,plain\r\n3,Cyd,\"a,b\"\r\n";

    [Theory]
    [InlineData("a;b;c\r\n1;2,5;3\r\n4;5;6\r\n", ';')]
    [InlineData("a|b\n1|\"x|y\"\n", '|')]
    [InlineData("one column\nsecond\n", ',')]
    public void Delimiter_is_detected_from_the_first_lines(string text, char expected) =>
        Assert.Equal(expected, CsvDialect.Detect(text, CsvDialect.Csv).Delimiter);

    [Fact]
    public void Quotes_delimiters_and_multiline_cells_are_read()
    {
        using var d = Make(Multi);
        using var t = new CsvTable(d, CsvDialect.Csv);
        t.EnsureAll();
        Assert.Equal(4, t.RowCount);
        Assert.Equal(3, t.ColumnCount);
        Assert.Equal(["id", "name", "note"], t.Header());
        Assert.Equal("first\r\nsecond, \"quoted\"", t.Cell(1, 2));
        Assert.Equal("a,b", t.Cell(3, 2));
        Assert.Equal(["2", "Bob", "plain"], t.Row(2));
    }

    [Fact]
    public void Short_row_is_kept_short_and_padded_on_write()
    {
        using var d = Make("a,b,c\r\n1,2\r\n3\r\n");
        using var t = new CsvTable(d, CsvDialect.Csv);
        Assert.Equal(["1", "2"], t.Row(1));
        Assert.Equal("", t.Cell(1, 2));
        Assert.True(t.SetCell(1, 2, "x").Applied);
        Assert.Equal("a,b,c\r\n1,2,x\r\n3\r\n", Text(d));
        Assert.True(t.SetCell(2, 2, "y").Applied);
        Assert.Equal("a,b,c\r\n1,2,x\r\n3,,y\r\n", Text(d));
        Assert.Equal(0, t.SetCell(1, 5, "").Revision - d.Revision);
    }

    [Theory]
    [InlineData("a,b\r\n1,2")]
    [InlineData("a,b\n1,2\n")]
    [InlineData("a,b\r1,2\r")]
    [InlineData("a,b\r\n\r\n1,2\r\n")]
    [InlineData("a,\"unterminated\r\nrest")]
    [InlineData("a,\"x\"y,z\r\n")]
    [InlineData("")]
    public void Untouched_document_keeps_its_bytes_whatever_the_shape(string text)
    {
        using var d = Make(text);
        byte[] before = Bytes(d);
        using var t = new CsvTable(d, CsvDialect.Csv);
        t.EnsureAll();
        for (int r = 0; r < t.RowCount; r++) _ = t.Row(r);
        Assert.Equal(before, Bytes(d));
        Assert.Equal(0, d.Revision);
    }

    [Fact]
    public void Row_ends_follow_the_text_without_final_newline_or_blank_line()
    {
        using var d = Make("a,b\r\n\r\n1,2");
        using var t = new CsvTable(d, CsvDialect.Csv);
        t.EnsureAll();
        Assert.Equal(3, t.RowCount);
        Assert.Equal([""], t.Row(1));
        Assert.Equal(["1", "2"], t.Row(2));
    }

    [Fact]
    public void Quote_glued_text_and_unterminated_quote_are_read_leniently()
    {
        using var d = Make("\"x\"y,\"open\r\nstill");
        using var t = new CsvTable(d, CsvDialect.Csv);
        Assert.Equal(["xy", "open\r\nstill"], t.Row(0));
        t.EnsureAll();
        Assert.Equal(1, t.RowCount);
    }

    [Fact]
    public void Editing_one_cell_of_a_multiline_corpus_row_changes_only_that_cell()
    {
        var sb = new StringBuilder("id,name,description,amount,created\r\n");
        for (int i = 0; i < 300; i++)
            sb.Append(i).Append(",n").Append(i).Append(i % 3 == 0 ? ",\"řádek 1\r\n(řádek 2 buňky)\n\"\"citát\"\", a čárka, v buňce\"" : ",plain").Append(",1.50,2026-10-01\r\n");
        string original = sb.ToString();
        using var d = Make(original);
        byte[] before = Bytes(d);
        using var t = new CsvTable(d, CsvDialect.Csv);

        var cell = t.CellRange(151, 2);
        Assert.Contains("citát", t.Cell(151, 2));
        Assert.True(t.SetCell(151, 2, "nový, \"text\"\nna dva řádky").Applied);

        byte[] after = Bytes(d);
        byte[] expectedMiddle = Encoding.UTF8.GetBytes("\"nový, \"\"text\"\"\nna dva řádky\"");
        Assert.Equal(before[..(int)cell.Start], after[..(int)cell.Start]);
        Assert.Equal(before[(int)cell.End..], after[(int)(cell.Start + expectedMiddle.Length)..]);
        Assert.Equal(expectedMiddle, after[(int)cell.Start..(int)(cell.Start + expectedMiddle.Length)]);
        Assert.Equal("nový, \"text\"\nna dva řádky", t.Cell(151, 2));
        Assert.Equal("1.50", t.Cell(151, 3));

        d.Undo();
        Assert.Equal(before, Bytes(d));
    }

    [Fact]
    public void Edited_cell_keeps_quoting_style()
    {
        using var d = Make("a,b\r\n\"1\",2\r\n");
        using var t = new CsvTable(d, CsvDialect.Csv);
        t.SetCell(1, 0, "9");
        t.SetCell(1, 1, "x,y");
        Assert.Equal("a,b\r\n\"9\",\"x,y\"\r\n", Text(d));
        Assert.Equal(0, t.SetCell(1, 0, "9").Revision - d.Revision);
    }

    [Fact]
    public void Row_insert_and_delete_have_expected_bytes_and_undo()
    {
        const string original = "a,b\r\n1,2\r\n3,4";
        using var d = Make(original);
        using var t = new CsvTable(d, CsvDialect.Csv);

        t.InsertRow(1, ["x", "y,z"]);
        Assert.Equal("a,b\r\nx,\"y,z\"\r\n1,2\r\n3,4", Text(d));
        d.Undo();
        Assert.Equal(original, Text(d));

        t.InsertRow(3, ["5", "6"]);
        Assert.Equal("a,b\r\n1,2\r\n3,4\r\n5,6", Text(d));
        d.Undo();

        t.DeleteRow(1);
        Assert.Equal("a,b\r\n3,4", Text(d));
        d.Undo();
        t.DeleteRow(2);
        Assert.Equal("a,b\r\n1,2", Text(d));
        d.Undo();
        Assert.Equal(original, Text(d));
        t.EnsureAll();
        Assert.Equal(3, t.RowCount);
    }

    [Fact]
    public void Append_to_text_with_final_newline_keeps_it()
    {
        using var d = Make("a,b\n1,2\n");
        using var t = new CsvTable(d, CsvDialect.Csv);
        t.InsertRow(2, ["3", "4"]);
        Assert.Equal("a,b\n1,2\n3,4\n", Text(d));
        t.InsertRow(0);
        Assert.Equal(",\na,b\n1,2\n3,4\n", Text(d));
    }

    [Fact]
    public void Column_insert_and_delete_are_one_undo_step_each()
    {
        const string original = "a,b,c\r\n1,2,3\r\n4,5\r\n\"x,y\",\"q\",z\r\n";
        using var d = Make(original);
        using var t = new CsvTable(d, CsvDialect.Csv);

        t.InsertColumn(1, "new");
        Assert.Equal("a,new,b,c\r\n1,,2,3\r\n4,,5\r\n\"x,y\",,\"q\",z\r\n", Text(d));
        d.Undo();
        Assert.Equal(original, Text(d));

        t.DeleteColumn(1);
        Assert.Equal("a,c\r\n1,3\r\n4\r\n\"x,y\",z\r\n", Text(d));
        d.Undo();
        t.DeleteColumn(2);
        Assert.Equal("a,b\r\n1,2\r\n4,5\r\n\"x,y\",\"q\"\r\n", Text(d));
        d.Undo();
        Assert.Equal(original, Text(d));
        Assert.False(d.CanUndo);
    }

    [Fact]
    public void Filter_finds_rows_and_leaves_bytes_alone()
    {
        using var d = Make(Multi);
        byte[] before = Bytes(d);
        using var t = new CsvTable(d, CsvDialect.Csv);
        Assert.Equal([1], t.Filter(new CsvFilter("SECOND")));
        Assert.Equal([], t.Filter(new CsvFilter("SECOND", IgnoreCase: false)));
        Assert.Equal([1, 3], t.Filter(new CsvFilter("a,b", null)).Concat(t.Filter(new CsvFilter("quoted"))).Order());
        Assert.Equal([2], t.Filter(new CsvFilter("plain", 2)));
        Assert.Equal([], t.Filter(new CsvFilter("plain", 1)));
        Assert.Equal([1, 2, 3], t.Filter(new CsvFilter("")));
        Assert.Equal(before, Bytes(d));
        Assert.Equal(0, d.Revision);
    }

    [Fact]
    public void Tsv_has_no_quoting_trouble_and_refuses_values_it_cannot_hold()
    {
        using var d = Make("a\tb\n1\t\"2\n");
        using var t = new CsvTable(d, new CsvDialect('\t', '\0'));
        Assert.Equal(["1", "\"2"], t.Row(1));
        Assert.Throws<CsvFormatException>(() => t.SetCell(1, 0, "x\ty"));
        t.SetCell(1, 0, "x y");
        Assert.Equal("a\tb\nx y\t\"2\n", Text(d));
    }

    [Theory]
    [MemberData(nameof(Codecs))]
    public void Every_encoding_round_trips_and_edits_one_cell(string name)
    {
        var codec = TextCodec.ByName(name)!;
        const string text = "id;jméno\r\n1;\"Řeřicha;\r\nx\"\r\n2;Žaneta\r\n";
        using var d = Make(text, codec);
        byte[] before = Bytes(d);
        using var t = new CsvTable(d, new CsvDialect(';'));
        t.EnsureAll();
        Assert.Equal(3, t.RowCount);
        Assert.Equal("Řeřicha;\r\nx", t.Cell(1, 1));
        Assert.Equal("Žaneta", t.Cell(2, 1));
        t.SetCell(2, 1, "Žofie");
        Assert.Equal(text.Replace("Žaneta", "Žofie"), Text(d));
        Assert.Equal(before.Length - codec.Encode("Žaneta").Length + codec.Encode("Žofie").Length, d.ByteLength);
        Assert.Equal(before[..codec.Bom.Length], Bytes(d)[..codec.Bom.Length]);
    }

    public static IEnumerable<object[]> Codecs() =>
        new[] { TextCodec.Utf8, TextCodec.Utf8Bom, TextCodec.Utf16LEBom, TextCodec.Utf16BE, TextCodec.Utf32LEBom, TextCodec.Cp1250 }.Select(c => new object[] { c.Name });

    [Fact]
    public void Edit_made_elsewhere_refreshes_the_rows()
    {
        using var d = Make("a,b\r\n1,2\r\n");
        using var t = new CsvTable(d, CsvDialect.Csv);
        t.EnsureAll();
        int raised = 0;
        t.Invalidated += (_, _) => raised++;
        d.Insert(d.ByteLength, "3,4\r\n"u8.ToArray());
        Assert.Equal(1, raised);
        t.EnsureAll();
        Assert.Equal(3, t.RowCount);
        d.Undo();
        t.EnsureAll();
        Assert.Equal(2, t.RowCount);
    }

    [Fact]
    public void Lone_cr_before_an_inserted_lf_is_a_single_terminator()
    {
        using var d = Make("a,b\r1,2\r");
        using var t = new CsvTable(d, CsvDialect.Csv);
        t.EnsureAll();
        Assert.Equal(2, t.RowCount);
        d.Insert(4, "\n"u8.ToArray());
        t.EnsureAll();
        Assert.Equal(2, t.RowCount);
        Assert.Equal(["1", "2"], t.Row(1));
    }

    [Fact]
    public void Region_is_a_table_and_the_text_around_it_stays()
    {
        const string head = "# notes\n";
        const string body = "a,b\n1,\"2\n3\"\n";
        const string tail = "\nthe end, with commas\n";
        using var d = Make(head + body + tail);
        byte[] before = Bytes(d);
        using var t = new CsvTable(d, CsvDialect.Csv, new ByteRange(head.Length, head.Length + body.Length));
        t.EnsureAll();
        Assert.Equal(2, t.RowCount);
        Assert.Equal("2\n3", t.Cell(1, 1));
        t.SetCell(1, 0, "10");
        Assert.Equal(head + "a,b\n10,\"2\n3\"\n" + tail, Text(d));
        t.InsertRow(2, ["x", "y"]);
        Assert.Equal(head + "a,b\n10,\"2\n3\"\nx,y\n" + tail, Text(d));
        t.DeleteColumn(0);
        Assert.Equal(head + "b\n\"2\n3\"\ny\n" + tail, Text(d));
        while (d.CanUndo) d.Undo();
        Assert.Equal(before, Bytes(d));
        t.EnsureAll();
        Assert.Equal(2, t.RowCount);
    }

    [Fact]
    public void Dialect_must_be_ascii_and_distinct()
    {
        using var d = Make("a");
        Assert.Throws<ArgumentException>(() => new CsvTable(d, new CsvDialect('"')));
        Assert.Throws<ArgumentException>(() => new CsvTable(d, new CsvDialect('\n')));
        Assert.Throws<ArgumentException>(() => new CsvTable(d, new CsvDialect('é')));
        Assert.Equal(new CsvDialect('\t'), CsvDialect.ForPath("x.TSV"));
        Assert.Equal(CsvDialect.Csv, CsvDialect.ForPath("x.csv"));
    }

    [Fact]
    public void Several_rows_and_columns_go_in_one_undo_step()
    {
        const string original = "a,b,c,d\n1,2,3,4\n5,6,7,8\n9,10,11,12";
        using var d = Make(original);
        using var t = new CsvTable(d, CsvDialect.Csv);

        t.DeleteRows([1, 3]);
        Assert.Equal("a,b,c,d\n5,6,7,8", Text(d));
        d.Undo();
        t.DeleteRows([2, 3]);
        Assert.Equal("a,b,c,d\n1,2,3,4", Text(d));
        d.Undo();
        t.DeleteColumns(1, 2);
        Assert.Equal("a,d\n1,4\n5,8\n9,12", Text(d));
        d.Undo();
        t.DeleteColumns(2, 5);
        Assert.Equal("a,b\n1,2\n5,6\n9,10", Text(d));
        d.Undo();
        Assert.Equal(original, Text(d));
        Assert.False(d.CanUndo);
    }

    [Fact]
    public void Preview_cuts_a_long_cell_on_a_character_boundary()
    {
        using var d = Make("a,\"" + new string('ě', 3000) + "\"\n");
        using var t = new CsvTable(d, CsvDialect.Csv);
        var f = t.Fields(0)[1];
        string preview = t.Preview(f, 101);
        Assert.EndsWith("…", preview);
        Assert.All(preview[..^1], c => Assert.Equal('ě', c));
        Assert.Equal(new string('ě', 3000), t.Text(f));
    }
}

[Trait("Category", "Large")]
public class CsvTableLargeTests
{
    private const int Rows = 2_000_000;

    [Fact]
    public void A_file_in_the_paged_backend_is_scrolled_and_edited_without_building_cells()
    {
        using var temp = new TempFile();
        using (var w = new StreamWriter(temp.Path, false, new UTF8Encoding(false), 1 << 16))
        {
            w.Write("id,name,note\r\n");
            for (int i = 0; i < Rows; i++)
                w.Write(i % 7 == 0 ? $"{i},name {i},\"line one\r\nline \"\"two\"\", {i}\"\r\n" : $"{i},name {i},plain note number {i}\r\n");
        }
        using var d = Doc.Open(temp.Path, PagedBackendFactory.Instance);
        long length = d.ByteLength;
        Assert.True(length > 50_000_000);
        using var t = new CsvTable(d, CsvDialect.Csv);

        Assert.True(t.EnsureRows(1000));
        Assert.False(t.IsComplete);
        Assert.True(t.RowCount < 1100);
        Assert.Equal(["7", "name 7", "line one\r\nline \"two\", 7"], t.Row(7 + 1));

        const int target = 1_400_000 + 1;
        Assert.Equal("name 1400000", t.Cell(target, 1));
        var cell = t.CellRange(target, 1);
        byte[] around = d.ReadBytes(new ByteRange(cell.Start - 50, cell.End + 50));
        Assert.True(t.SetCell(target, 1, "renamed").Applied);
        Assert.Equal(length - cell.Length + 7, d.ByteLength);
        Assert.Equal("renamed", t.Cell(target, 1));
        Assert.Equal(around[..50], d.ReadBytes(new ByteRange(cell.Start - 50, cell.Start)));
        Assert.Equal(around[^50..], d.ReadBytes(new ByteRange(cell.Start + 7, cell.Start + 57)));

        t.EnsureAll();
        Assert.Equal(Rows + 1, t.RowCount);
        Assert.Equal($"{Rows - 1}", t.Cell(Rows, 0));
        d.Undo();
        Assert.Equal(length, d.ByteLength);
        Assert.Equal("name 1400000", t.Cell(target, 1));
    }
}
