using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class HexEditorTests
{
    private static (Doc Doc, HexEditor Editor) Make(byte[] bytes)
    {
        var d = Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        return (d, new HexEditor(d, HexCharset.Windows1252));
    }

    private static byte[] All(Doc d) => d.ReadBytes(new(0, d.ByteLength));

    [Fact]
    public void Typing_digits_overwrites_by_nibbles_and_a_run_is_one_undo_step()
    {
        var (d, e) = Make([0x00, 0x00, 0x00, 0x00]);
        e.MoveTo(1);
        foreach (int digit in new[] { 0x4, 0xE, 0x4, 0xB }) Assert.True(e.TypeNibble(digit).Applied);
        Assert.Equal([0x00, 0x4E, 0x4B, 0x00], All(d));
        Assert.Equal(3, e.Caret);
        Assert.Equal(1, d.UndoDepth);
        Assert.True(e.Undo());
        Assert.Equal(new byte[4], All(d));
        Assert.Equal(1, e.Caret);
        Assert.True(e.Redo());
        Assert.Equal([0x00, 0x4E, 0x4B, 0x00], All(d));
    }

    [Fact]
    public void Moving_ends_the_typing_run_and_a_nibble_keeps_the_other_half()
    {
        var (d, e) = Make([0xAB, 0xCD]);
        e.TypeNibble(0x1);
        Assert.Equal([0x1B, 0xCD], All(d));
        e.MoveTo(1);
        e.TypeNibble(0x2);
        Assert.Equal(2, d.UndoDepth);
        Assert.Equal([0x1B, 0x2D], All(d));
    }

    [Fact]
    public void Overwrite_never_makes_the_file_longer_and_says_so()
    {
        var (d, e) = Make([1, 2]);
        e.MoveTo(2);
        Assert.Equal(HexIssue.EndOfFile, e.TypeNibble(5).Issue);
        e.Column = HexColumn.Text;
        Assert.Equal(HexIssue.EndOfFile, e.TypeText("x").Issue);
        e.MoveTo(1);
        Assert.Equal(HexIssue.EndOfFile, e.TypeText("é€").Issue);
        Assert.Equal([1, 2], All(d));
    }

    [Fact]
    public void Insert_mode_adds_a_byte_per_two_digits_and_at_the_end()
    {
        var (d, e) = Make([1, 2]);
        e.Insert = true;
        e.MoveTo(1);
        e.TypeNibble(0xA);
        e.TypeNibble(0x5);
        Assert.Equal([1, 0xA5, 2], All(d));
        Assert.Equal(2, e.Caret);
        e.MoveTo(3);
        e.TypeNibble(0xF);
        e.TypeNibble(0x0);
        Assert.Equal([1, 0xA5, 2, 0xF0], All(d));
    }

    [Fact]
    public void Delete_in_overwrite_mode_asks_and_changes_nothing_until_answered()
    {
        var (d, e) = Make([1, 2, 3, 4]);
        e.Select(1, 3);
        var asked = e.Delete(DeleteMode.Ask);
        Assert.False(asked.Applied);
        Assert.Equal(HexIssue.NeedsChoice, asked.Issue);
        Assert.Equal([1, 2, 3, 4], All(d));
        Assert.True(e.Delete(DeleteMode.Zero).Applied);
        Assert.Equal([1, 0, 0, 4], All(d));
        e.Select(1, 3);
        Assert.True(e.Delete(DeleteMode.Remove).Applied);
        Assert.Equal([1, 4], All(d));
    }

    [Fact]
    public void Delete_and_backspace_in_insert_mode_remove_directly()
    {
        var (d, e) = Make([1, 2, 3]);
        e.Insert = true;
        e.MoveTo(1);
        Assert.True(e.Delete(DeleteMode.Ask).Applied);
        Assert.Equal([1, 3], All(d));
        Assert.True(e.Backspace(DeleteMode.Ask).Applied);
        Assert.Equal([3], All(d));
        Assert.Equal(0, e.Caret);
    }

    [Fact]
    public void Backspace_in_overwrite_mode_only_moves()
    {
        var (d, e) = Make([1, 2, 3]);
        e.MoveTo(2);
        Assert.True(e.Backspace(DeleteMode.Ask).Applied);
        Assert.Equal(1, e.Caret);
        Assert.Equal([1, 2, 3], All(d));
    }

    [Fact]
    public void A_paste_longer_than_the_rest_asks_then_extends_or_truncates()
    {
        var (d, e) = Make([1, 2, 3, 4]);
        e.MoveTo(2);
        byte[] data = [9, 9, 9, 9];
        Assert.Equal(HexIssue.NeedsChoice, e.Paste(data).Issue);
        Assert.Equal([1, 2, 3, 4], All(d));
        Assert.True(e.Paste(data, PasteLonger.Truncate).Applied);
        Assert.Equal([1, 2, 9, 9], All(d));
        e.Undo();
        e.MoveTo(2);
        Assert.True(e.Paste(data, PasteLonger.Extend).Applied);
        Assert.Equal([1, 2, 9, 9, 9, 9], All(d));
        e.Undo();
        Assert.Equal([1, 2, 3, 4], All(d));
    }

    [Fact]
    public void A_paste_that_fits_overwrites_and_in_insert_mode_inserts()
    {
        var (d, e) = Make([1, 2, 3, 4]);
        Assert.True(e.Paste([7, 8]).Applied);
        Assert.Equal([7, 8, 3, 4], All(d));
        e.Insert = true;
        e.MoveTo(4);
        Assert.True(e.Paste([5]).Applied);
        Assert.Equal([7, 8, 3, 4, 5], All(d));
    }

    [Fact]
    public void Text_typing_writes_the_bytes_of_the_charset()
    {
        var (d, e) = Make(new byte[6]);
        e.Column = HexColumn.Text;
        e.Charset = HexCharset.Windows1250;
        Assert.True(e.TypeText("ž").Applied);
        Assert.Equal(0x9E, All(d)[0]);
        e.Charset = HexCharset.Utf8;
        Assert.True(e.TypeText("€").Applied);
        Assert.Equal([0x9E, 0xE2, 0x82, 0xAC, 0, 0], All(d));
        e.Charset = HexCharset.Windows1252;
        Assert.Equal(HexIssue.NotEncodable, e.TypeText("Ж").Issue);
    }

    [Fact]
    public void Writing_a_value_from_the_inspector_is_one_undo_step_and_keeps_the_caret()
    {
        var (d, e) = Make(new byte[8]);
        e.MoveTo(6);
        Assert.True(DataInspector.TryEncode(InspectorKind.UInt32, "0xCAFEBABE", true, out var bytes, out _));
        Assert.True(e.Write(new ByteRange(2, 6), bytes).Applied);
        Assert.Equal([0, 0, 0xCA, 0xFE, 0xBA, 0xBE, 0, 0], All(d));
        Assert.Equal(6, e.Caret);
        Assert.True(e.Undo());
        Assert.Equal(new byte[8], All(d));
    }

    [Fact]
    public void Edits_are_refused_while_a_save_runs_and_say_busy()
    {
        using var file = new TempFile(new byte[16]);
        using var d = Doc.Open(file.Path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        var e = new HexEditor(d, HexCharset.Windows1252);
        using var inFlush = new ManualResetEventSlim();
        d.Replace(new(0, 1), new byte[] { 1 }, new() { RawBytes = true });
        d.InPlaceHooks = new PatchHooks { BeforeFlush = ct => { inFlush.Set(); ct.WaitHandle.WaitOne(300); } };
        var task = d.SaveAsync();
        Assert.True(inFlush.Wait(5000));
        Assert.Equal(HexIssue.Busy, e.TypeNibble(3).Issue);
        Assert.True(task.GetAwaiter().GetResult().Saved);
        Assert.True(e.TypeNibble(3).Applied);
    }

    [Theory]
    [InlineData("4E 4B 03", new byte[] { 0x4E, 0x4B, 0x03 })]
    [InlineData("4e4b03", new byte[] { 0x4E, 0x4B, 0x03 })]
    [InlineData("0x4E, 0x4B", new byte[] { 0x4E, 0x4B })]
    public void Hex_text_from_the_clipboard_is_read_as_bytes(string text, byte[] expected)
    {
        Assert.True(HexEditor.TryParseHexText(text, out var bytes));
        Assert.Equal(expected, bytes);
        Assert.Equal("4E 4B", HexEditor.ToHexText([0x4E, 0x4B]));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("4E 4")]
    [InlineData("")]
    [InlineData("??")]
    public void Ordinary_text_is_not_taken_for_hex(string text) => Assert.False(HexEditor.TryParseHexText(text, out _));

    // ---- character column ----

    private static HexCell[] Map(HexCharset set, byte[] file, int row, int count)
    {
        int before = Math.Min(row, set.Lookbehind), after = Math.Min(file.Length - row - count, set.Lookahead);
        var cells = new HexCell[count];
        set.Map(file.AsSpan(row - before, before + count + Math.Max(0, after)), before, count, row - before, cells);
        return cells;
    }

    [Fact]
    public void Utf8_column_shows_the_character_on_its_first_byte_and_dots_on_the_rest()
    {
        var cells = Map(HexCharset.Utf8, "a€b"u8.ToArray(), 0, 5);
        Assert.Equal(["a", "€", "·", "·", "b"], cells.Select(c => c.Glyph));
        Assert.Equal([HexCellKind.Normal, HexCellKind.Normal, HexCellKind.Continuation, HexCellKind.Continuation, HexCellKind.Normal], cells.Select(c => c.Kind));
    }

    [Fact]
    public void Utf8_column_marks_an_invalid_byte_with_a_replacement_character()
    {
        var cells = Map(HexCharset.Utf8, [0x61, 0xFF, 0xE2, 0x82, 0x62], 0, 5);
        Assert.Equal(HexCellKind.Invalid, cells[1].Kind);
        Assert.Equal("�", cells[1].Glyph);
        Assert.Equal(HexCellKind.Invalid, cells[2].Kind);
        Assert.Equal(HexCellKind.Invalid, cells[3].Kind);
        Assert.Equal("b", cells[4].Glyph);
    }

    [Fact]
    public void Utf8_column_knows_a_character_that_starts_in_the_row_before()
    {
        byte[] file = [.. new byte[13], 0xF0, 0x9F, 0x98, 0x80, 0x41, .. new byte[10]];
        var row = Map(HexCharset.Utf8, file, 15, 4);
        Assert.Equal([HexCellKind.Continuation, HexCellKind.Continuation, HexCellKind.Normal, HexCellKind.Control], row.Select(c => c.Kind));
        Assert.Equal("A", row[2].Glyph);
    }

    [Fact]
    public void Utf16_column_pairs_bytes_and_shows_a_lone_last_byte_as_a_dot()
    {
        var cells = Map(HexCharset.Utf16LE, [0x41, 0x00, 0x42, 0x00, 0x43], 0, 5);
        Assert.Equal(["A", "·", "B", "·", "·"], cells.Select(c => c.Glyph));
        Assert.Equal(HexCellKind.Control, cells[4].Kind);
        var pair = Map(HexCharset.Utf16LE, [0x3D, 0xD8, 0x00, 0xDE], 0, 4);
        Assert.Equal("😀", pair[0].Glyph);
        Assert.All(pair.Skip(1), c => Assert.Equal(HexCellKind.Continuation, c.Kind));
        var lone = Map(HexCharset.Utf16LE, [0x00, 0xDC], 0, 2);
        Assert.Equal(HexCellKind.Invalid, lone[0].Kind);
    }

    [Fact]
    public void Single_byte_columns_use_the_code_page_and_dot_the_controls()
    {
        var cells = Map(HexCharset.Windows1250, [0x41, 0x9E, 0x00, 0x0A, 0x7F, 0x20], 0, 6);
        Assert.Equal(["A", "ž", "·", "·", "·", " "], cells.Select(c => c.Glyph));
        Assert.Equal(HexCellKind.Control, cells[2].Kind);
        Assert.Equal(HexCellKind.Normal, cells[5].Kind);
    }
}
