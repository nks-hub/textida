using System.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Document.AvaloniaEdit.Tests;

/// <summary>
/// Line, case and typing commands. Notation: <c>«</c> and <c>»</c> mark a selection (anchor first), <c>|</c> an empty selection;
/// every case also proves the whole command is one undo step that restores the original bytes.
/// </summary>
public class EditingCommandTests
{
    private static readonly IndentSettings Spaces4 = new(true, 4, 4);

    private static (string Text, List<Sel> Sels) Parse(string marked)
    {
        var sb = new StringBuilder();
        var sels = new List<Sel>();
        int open = -1;
        foreach (var c in marked)
        {
            switch (c)
            {
                case '«': open = sb.Length; break;
                case '»': sels.Add(new Sel(open, sb.Length)); open = -1; break;
                case '|': sels.Add(Sel.At(sb.Length)); break;
                default: sb.Append(c); break;
            }
        }
        return (sb.ToString(), sels);
    }

    private static string Mark(string text, IReadOnlyList<Sel> sels)
    {
        var inserts = new List<(int At, string Mark, int Order)>();
        foreach (var s in sels)
        {
            if (s.IsEmpty) inserts.Add((s.Caret, "|", 0));
            else { inserts.Add((s.Anchor, "«", 0)); inserts.Add((s.Caret, "»", 1)); }
        }
        var sb = new StringBuilder(text);
        foreach (var (at, mark, _) in inserts.OrderByDescending(i => i.At).ThenByDescending(i => i.Order)) sb.Insert(at, mark);
        return sb.ToString();
    }

    /// <summary>Runs a command on a document that journals nothing, as the app does: inside an undo group.</summary>
    private static string Run(string marked, Func<AvaloniaEditBackend, IReadOnlyList<Sel>, IReadOnlyList<Sel>> command, string? expectedMarked = null)
    {
        var (text, sels) = Parse(marked);
        var original = Encoding.UTF8.GetBytes(text);
        using var doc = Doc.FromBytes(original, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        var backend = (AvaloniaEditBackend)doc.Backend;
        IReadOnlyList<Sel> result;
        using (doc.BeginUndoGroup()) result = command(backend, sels);
        string after = backend.TextDocument.Text;
        Assert.Equal(after, Encoding.UTF8.GetString(doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
        if (expectedMarked is not null) Assert.Equal(expectedMarked, Mark(after, result));

        if (after != text)
        {
            Assert.True(doc.IsModified);
            Assert.NotNull(doc.Undo());
            Assert.Equal(text, backend.TextDocument.Text);
            Assert.Equal(original, doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
            Assert.False(doc.CanUndo);
        }
        return after;
    }

    [Theory]
    [InlineData("a\n|b\nc", "a\n|c")]
    [InlineData("|a\nb\nc", "|b\nc")]
    [InlineData("a\nb\n|c", "a\n|b")]
    [InlineData("a\n«b\nc»\nd", "a\n|d")]
    [InlineData("«a\nb\nc»", "|")]
    [InlineData("a|\r\nb\r\nc", "|b\r\nc")]
    public void Delete_line(string input, string expected) => Run(input, (b, s) => LineOperations.DeleteLines(b.TextDocument, s), expected);

    [Fact]
    public void Delete_line_over_several_ranges()
    {
        Run("a|\nb\nc|\nd\ne|", (b, s) => LineOperations.DeleteLines(b.TextDocument, s), "|b\n|d");
        var after = Run("x\n|1\ny\n|2\nz", (b, s) => LineOperations.DeleteLines(b.TextDocument, s));
        Assert.Equal("x\ny\nz", after);
    }

    [Theory]
    [InlineData("a\n|b\nc", "a\nb\n|b\nc")]
    [InlineData("a\nb|", "a\nb\nb|")]
    [InlineData("«a\nb»\nc", "a\nb\n«a\nb»\nc")]
    [InlineData("a\r\n|b\r\nc", "a\r\nb\r\n|b\r\nc")]
    public void Duplicate_line(string input, string expected) => Run(input, (b, s) => LineOperations.DuplicateLines(b.TextDocument, s), expected);

    [Fact]
    public void Duplicate_line_over_several_ranges() =>
        Assert.Equal("a\na\nb\nc\nc", Run("|a\nb\n|c", (b, s) => LineOperations.DuplicateLines(b.TextDocument, s)));

    [Theory]
    [InlineData("a\n|b\nc", -1, "|b\na\nc")]
    [InlineData("a\n|b\nc", 1, "a\nc\n|b")]
    [InlineData("|a\nb", -1, "|a\nb")]
    [InlineData("a\nb|", 1, "a\nb|")]
    [InlineData("a\nb\n«c\nd»", -1, "a\n«c\nd»\nb")]
    [InlineData("a\r\nb\nc|", -1, "a\r\nc|\nb")]
    [InlineData("a\nb|", -1, "b|\na")]
    public void Move_line(string input, int direction, string expected) =>
        Run(input, (b, s) => LineOperations.MoveLines(b.TextDocument, s, direction), expected);

    [Fact]
    public void Move_line_over_several_ranges() =>
        Assert.Equal("b\na\nd\nc", Run("a\n|b\nc\n|d", (b, s) => LineOperations.MoveLines(b.TextDocument, s, -1)));

    [Fact]
    public void Move_down_keeps_mixed_line_endings_in_place() =>
        Assert.Equal("b\r\na\nc", Run("|a\r\nb\nc", (b, s) => LineOperations.MoveLines(b.TextDocument, s, 1)));

    [Theory]
    [InlineData("a|\nb\nc", "a b\nc")]
    [InlineData("a|\n    b", "a b")]
    [InlineData("a| \nb\nc", "a b\nc")]
    [InlineData("«a\nb\nc»", "a b c")]
    [InlineData("a|\n\nb", "a\nb")]
    [InlineData("a|\r\nb", "a b")]
    public void Join_lines(string input, string expected) =>
        Assert.Equal(expected, Run(input, (b, s) => LineOperations.JoinLines(b.TextDocument, s)));

    [Fact]
    public void Join_on_the_last_line_changes_nothing() =>
        Assert.Equal("a\nb", Run("a\n|b", (b, s) => LineOperations.JoinLines(b.TextDocument, s)));

    [Fact]
    public void Join_lines_over_several_ranges() =>
        Assert.Equal("a b\nc d", Run("a|\nb\nc|\nd", (b, s) => LineOperations.JoinLines(b.TextDocument, s)));

    [Theory]
    [InlineData("|a\nb", "    |a\nb")]
    [InlineData("«a\nb»", "    «a\n    b»")]
    [InlineData("«a\n\nb»", "    «a\n\n    b»")]
    [InlineData("a\n«b\nc\n»d", "a\n    «b\n    c\n»d")]
    public void Indent(string input, string expected) => Run(input, (b, s) => LineOperations.Indent(b.TextDocument, s, Spaces4), expected);

    [Fact]
    public void Indent_with_tabs_and_over_several_ranges()
    {
        Assert.Equal("\ta\nb\n\tc", Run("|a\nb\n|c", (b, s) => LineOperations.Indent(b.TextDocument, s, new IndentSettings(false, 4, 4))));
    }

    [Theory]
    [InlineData("    |a", "|a")]
    [InlineData("  |a", "|a")]
    [InlineData("\t«a\n\tb»", "«a\nb»")]
    [InlineData("      |a", "  |a")]
    [InlineData("|a", "|a")]
    public void Outdent(string input, string expected) => Run(input, (b, s) => LineOperations.Outdent(b.TextDocument, s, Spaces4), expected);

    [Theory]
    [InlineData("|", "    |")]
    [InlineData("a|", "a   |")]
    [InlineData("ab|", "ab  |")]
    [InlineData("abcd|", "abcd    |")]
    [InlineData("«ab»c", "    |c")]
    public void Insert_indent_to_the_next_stop(string input, string expected) =>
        Run(input, (b, s) => LineOperations.InsertIndent(b.TextDocument, s, Spaces4), expected);

    [Fact]
    public void Insert_indent_writes_a_tab_when_tabs_are_chosen() =>
        Assert.Equal("\tx", Run("|x", (b, s) => LineOperations.InsertIndent(b.TextDocument, s, new IndentSettings(false, 4, 4))));

    [Theory]
    [InlineData("«abc» def", true, "«ABC» def")]
    [InlineData("abc «def»", true, "abc «DEF»")]
    [InlineData("«ŘÁD příliš»", false, "«řád příliš»")]
    [InlineData("«řád»", true, "«ŘÁD»")]
    [InlineData("|abc", true, "|abc")]
    public void Change_case(string input, bool upper, string expected) =>
        Run(input, (b, s) => LineOperations.ChangeCase(b.TextDocument, s, upper), expected);

    [Fact]
    public void Change_case_over_several_ranges_is_one_undo_step() =>
        Assert.Equal("AB cd EF", Run("«ab» cd «ef»", (b, s) => LineOperations.ChangeCase(b.TextDocument, s, true)));

    [Fact]
    public void Column_rows_are_a_list_of_ranges()
    {
        // a rectangle over the middle column of three rows
        var after = Run("a«b»c\nd«e»f\ng«h»i", (b, s) => LineOperations.ChangeCase(b.TextDocument, s, true));
        Assert.Equal("aBc\ndEf\ngHi", after);
    }

    // ---- paste into a column ----

    [Theory]
    [InlineData("a«b»c\nd«e»f", "X", "aXc\ndXf")]
    [InlineData("..|.\n..|.", "1\n2", "..1.\n..2.")]
    [InlineData("a|b\nc|d\ne|f", "1\n2\n3", "a1b\nc2d\ne3f")]
    [InlineData("a|", "1\n2", "a1\n 2")]
    [InlineData("abc|\nd|", "1\n2", "abc1\nd2")]
    [InlineData("a|b\n|", "1\n2", "a1b\n2")]
    public void Column_paste_puts_one_clipboard_line_in_each_row(string input, string pasted, string expected) =>
        Assert.Equal(expected, Run(input, (b, s) => ColumnEdit.Paste(b.TextDocument, s, pasted, "\n")));

    [Fact]
    public void Column_paste_of_a_trailing_line_break_does_not_add_a_row() =>
        Assert.Equal("aXb\ncYd", Run("a|b\nc|d", (b, s) => ColumnEdit.Paste(b.TextDocument, s, "X\nY\n", "\n")));

    [Fact]
    public void Column_paste_extends_the_document_and_pads_short_rows() =>
        Assert.Equal("ab1\nc 2\n  3", Run("ab|\nc", (b, s) => ColumnEdit.Paste(b.TextDocument, [s[0]], "1\n2\n3", "\n")));

    // ---- paired characters ----

    private static string Typed(string marked, char c, string? expected = null)
    {
        var (text, sels) = Parse(marked);
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        var d = ((AvaloniaEditBackend)doc.Backend).TextDocument;
        bool handled;
        Sel result;
        using (doc.BeginUndoGroup()) handled = PairedCharacters.TryType(d, sels[0], c, out result);
        if (expected is null) { Assert.False(handled); return d.Text; }
        Assert.True(handled);
        Assert.Equal(expected, Mark(d.Text, [result]));
        doc.Undo();
        Assert.Equal(text, d.Text);
        return d.Text;
    }

    [Theory]
    [InlineData("|", '(', "(|)")]
    [InlineData("a |", '[', "a [|]")]
    [InlineData("|", '{', "{|}")]
    [InlineData("|", '"', "\"|\"")]
    [InlineData("«abc»", '(', "(«abc»)")]
    [InlineData("«abc»", '"', "\"«abc»\"")]
    [InlineData("(|)", ')', "()|")]
    [InlineData("\"a|\"", '"', "\"a\"|")]
    [InlineData("|)", '(', "(|))")]
    public void Paired_characters_are_added_wrapped_or_typed_over(string input, char typed, string expected) => Typed(input, typed, expected);

    [Theory]
    [InlineData("don|", '\'')]
    [InlineData("|x", '(')]
    [InlineData("a|", 'b')]
    [InlineData("|x", '"')]
    public void Paired_characters_stay_out_of_the_way(string input, char typed) => Typed(input, typed);

    [Fact]
    public void Backspace_between_a_pair_removes_both()
    {
        var (text, sels) = Parse("(|)");
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        var d = ((AvaloniaEditBackend)doc.Backend).TextDocument;
        Assert.True(PairedCharacters.TryBackspace(d, sels[0], out var r));
        Assert.Equal("", d.Text);
        Assert.Equal(0, r.Caret);
        doc.Undo();
        Assert.Equal("()", d.Text);
        Assert.False(PairedCharacters.TryBackspace(d, Sel.At(1), out _) && d.Text == "()");
    }

    // ---- comment continuation ----

    private static readonly CommentStyle SlashStyle = new("//", true);
    private static readonly CommentStyle HashStyle = new("#", false);

    [Theory]
    [InlineData("// text|", "// text\n// |")]
    [InlineData("    // text|", "    // text\n    // |")]
    [InlineData("/// doc|", "/// doc\n/// |")]
    [InlineData("/** doc|", "/** doc\n * |")]
    [InlineData(" * item|", " * item\n * |")]
    [InlineData("// a|b", "// a\n// |b")]
    public void Comment_is_continued_on_the_next_line(string input, string expected) => Continued(input, SlashStyle, expected);

    [Theory]
    [InlineData("# x|", "# x\n# |")]
    [InlineData("## x|", "## x\n## |")]
    public void Hash_comment_is_continued(string input, string expected) => Continued(input, HashStyle, expected);

    [Theory]
    [InlineData("// |", "\n|")]
    [InlineData("code|", null)]
    [InlineData("/* done */|", null)]
    [InlineData("|// text", null)]
    [InlineData("a // trailing|", null)]
    public void Empty_comment_ends_and_other_lines_are_left_to_the_editor(string input, string? expected) => Continued(input, SlashStyle, expected);

    private static void Continued(string marked, CommentStyle style, string? expected)
    {
        var (text, sels) = Parse(marked);
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        var d = ((AvaloniaEditBackend)doc.Backend).TextDocument;
        bool handled;
        Sel result;
        using (doc.BeginUndoGroup()) handled = CommentContinuation.TryContinue(d, sels[0], style, "\n", out result);
        if (expected is null) { Assert.False(handled); Assert.Equal(text, d.Text); return; }
        Assert.True(handled);
        Assert.Equal(expected, Mark(d.Text, [result]));
        doc.Undo();
        Assert.Equal(text, d.Text);
        Assert.False(doc.CanUndo);
    }

    // ---- typing and undo ----

    private static Doc New(string text)
    {
        var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        return doc;
    }

    [Fact]
    public void A_run_of_typed_characters_is_one_undo_step()
    {
        using var doc = New("hello");
        var d = ((AvaloniaEditBackend)doc.Backend).TextDocument;
        d.Insert(5, " ");
        d.Insert(6, "w");
        d.Insert(7, "o");
        d.Insert(8, "r");
        Assert.Equal("hello wor", d.Text);
        Assert.Equal(1, doc.UndoDepth);
        doc.Undo();
        Assert.Equal("hello", d.Text);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void Typing_elsewhere_or_after_another_change_starts_a_new_step()
    {
        using var doc = New("one\ntwo");
        var d = ((AvaloniaEditBackend)doc.Backend).TextDocument;
        d.Insert(3, "a");
        d.Insert(4, "b");
        d.Insert(0, "x"); // another place
        Assert.Equal(2, doc.UndoDepth);
        d.Insert(1, "y");
        Assert.Equal(2, doc.UndoDepth);
        doc.Undo();
        Assert.Equal("one\nab\ntwo".Replace("\nab\n", "ab\n"), d.Text);
        doc.Undo();
        Assert.Equal("one\ntwo", d.Text);
    }

    [Fact]
    public void Backspace_runs_and_pastes_are_grouped_by_kind()
    {
        using var doc = New("abcdef");
        var d = ((AvaloniaEditBackend)doc.Backend).TextDocument;
        d.Remove(5, 1);
        d.Remove(4, 1);
        d.Remove(3, 1);
        Assert.Equal("abc", d.Text);
        Assert.Equal(1, doc.UndoDepth);
        d.Insert(3, "this is a pasted block of text");
        Assert.Equal(2, doc.UndoDepth);
        d.Insert(d.TextLength, "!");
        Assert.Equal(3, doc.UndoDepth);
    }

    [Fact]
    public void Typing_after_a_save_does_not_merge_into_the_saved_step()
    {
        var path = Path.Combine(Path.GetTempPath(), "textida-typing-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "abc");
        try
        {
            using var doc = Doc.Open(path, AvaloniaEditBackendFactory.Instance);
            var d = ((AvaloniaEditBackend)doc.Backend).TextDocument;
            d.Insert(3, "d");
            Assert.True(doc.Save().Saved);
            d.Insert(4, "e");
            Assert.True(doc.IsModified);
            doc.Undo();
            Assert.Equal("abcd", d.Text);
            Assert.False(doc.IsModified);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Typed_characters_the_codec_cannot_hold_are_reported_before_saving()
    {
        using var doc = Doc.FromBytes(new byte[] { 0x61, 0x62 }, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Cp1252 });
        var backend = (AvaloniaEditBackend)doc.Backend;
        Assert.False(backend.TypedLoss().HasLoss);
        backend.TextDocument.Insert(1, "č");
        backend.TextDocument.Insert(2, "é");
        var loss = backend.TypedLoss();
        Assert.Equal(1, loss.Total);
        var entry = Assert.Single(loss.Entries);
        Assert.Equal('č', entry.CodePoint);
        Assert.Equal((1, 2), (entry.Line, entry.Column));
        Assert.Equal(new byte[] { 0x61, 0x3F, 0xE9, 0x62 }, doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
    }

    [Fact]
    public void Original_text_never_counts_as_loss_even_with_invalid_bytes()
    {
        using var doc = Doc.FromBytes(new byte[] { 0x61, 0xFF, 0xFE, 0x62 }, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        Assert.False(((AvaloniaEditBackend)doc.Backend).TypedLoss().HasLoss);
    }
}
