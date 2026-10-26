using System.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;
using TextDocument = AvaloniaEdit.Document.TextDocument;

namespace NksHub.Textida.Document.AvaloniaEdit.Tests;

/// <summary>
/// P2.2.4 and P2.2.5 below the window: caret movement, adding carets and edits over several carets. Notation: <c>«</c> and <c>»</c> mark a
/// selection (anchor first), <c>|</c> an empty caret; the first mark is primary unless a <c>*</c> follows the mark of another one (several carets print it). Every edit
/// case also proves that the bytes and the text view agree and that one undo gives back the original bytes and one selection per caret.
/// </summary>
public class MultiCaretTests
{
    private static (string Text, Carets Carets) Parse(string marked)
    {
        var sb = new StringBuilder();
        var sels = new List<Sel>();
        int open = -1, primary = 0;
        foreach (char c in marked)
        {
            switch (c)
            {
                case '«': open = sb.Length; break;
                case '»': sels.Add(new Sel(open, sb.Length)); break;
                case '|': sels.Add(Sel.At(sb.Length)); break;
                case '*': primary = sels.Count - 1; break;
                default: sb.Append(c); break;
            }
        }
        if (sels.Count == 0) sels.Add(Sel.At(0));
        return (sb.ToString(), Carets.Create(sels, primary));
    }

    private static string Mark(string text, Carets carets)
    {
        var inserts = new List<(int At, string Mark, int Order)>();
        for (int i = 0; i < carets.Count; i++)
        {
            var s = carets.Items[i];
            string star = i == carets.Primary && carets.Count > 1 ? "*" : "";
            if (s.IsEmpty) inserts.Add((s.Caret, "|" + star, 0));
            else { inserts.Add((s.Anchor, "«", 0)); inserts.Add((s.Caret, "»" + star, 1)); }
        }
        var sb = new StringBuilder(text);
        foreach (var (at, mark, _) in inserts.OrderByDescending(i => i.At).ThenByDescending(i => i.Order)) sb.Insert(at, mark);
        return sb.ToString();
    }

    private sealed class Opened(Doc doc, Carets carets) : IDisposable
    {
        public Doc Doc { get; } = doc;
        public AvaloniaEditBackend Backend { get; } = (AvaloniaEditBackend)doc.Backend;
        public Carets Carets { get; set; } = carets;
        public TextDocument Text => Backend.TextDocument;
        public byte[] Bytes => Doc.ReadBytes(new ByteRange(0, Doc.ByteLength));
        public string Marked => Mark(Text.Text, Carets);
        public void Dispose() => Doc.Dispose();
    }

    private static Opened Open(string marked, TextCodec? codec = null)
    {
        var (text, carets) = Parse(marked);
        codec ??= TextCodec.Utf8;
        return new(Doc.FromBytes(codec.Encode(text), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = codec }), carets);
    }

    private static string NoStars(string s) => s.Replace("*", "");

    // ---- movement ----

    [Theory]
    [InlineData("a|bc d|ef", CaretMove.Right, false, "ab|c de|f")]
    [InlineData("a|bc d|ef", CaretMove.Left, false, "|abc |def")]
    [InlineData("«ab»c d|ef", CaretMove.Left, false, "|abc |def")]
    [InlineData("«ab»c d«ef»", CaretMove.Right, false, "ab|c def|")]
    [InlineData("a|bc d|ef", CaretMove.Right, true, "a«b»c d«e»f")]
    [InlineData("one| two| three", CaretMove.WordRight, false, "one |two |three")]
    [InlineData("one| two| three", CaretMove.WordLeft, false, "|one |two three")]
    [InlineData("  a|b\n  c|d", CaretMove.LineStart, false, "  |ab\n  |cd")]
    [InlineData("  |ab\n  |cd", CaretMove.LineStart, false, "|  ab\n|  cd")]
    [InlineData("a|b\nc|d", CaretMove.LineEnd, false, "ab|\ncd|")]
    [InlineData("a|b\nc|d", CaretMove.DocumentEnd, false, "ab\ncd|")]
    [InlineData("a|b\nc|d", CaretMove.DocumentStart, false, "|ab\ncd")]
    public void Carets_move_together(string input, CaretMove move, bool extend, string expected)
    {
        var (text, carets) = Parse(input);
        using var h = Open(input);
        Assert.Equal(expected, NoStars(Mark(text, MultiCaret.Move(h.Text, carets, move, extend))));
    }

    [Fact]
    public void Carets_that_meet_become_one()
    {
        using var h = Open("abcd");
        var moved = MultiCaret.Move(h.Text, Carets.Create([Sel.At(0), Sel.At(1)], 1), CaretMove.Left, false);
        Assert.Equal([Sel.At(0)], moved.Items);
    }

    [Fact]
    public void Up_and_down_keep_the_column_across_a_short_line()
    {
        using var h = Open("abcdef\nab\nabcdef\nxyz"); // lines start at 0, 7, 10, 17
        var down1 = MultiCaret.MoveVertical(h.Text, Carets.One(Sel.At(4)), 1, false, 4, null, out var cols);
        Assert.Equal(Sel.At(9), down1.Items[0]); // end of "ab"
        var down2 = MultiCaret.MoveVertical(h.Text, down1, 1, false, 4, cols, out cols);
        Assert.Equal(Sel.At(14), down2.Items[0]); // column 4 again
        var down3 = MultiCaret.MoveVertical(h.Text, down2, 1, false, 4, cols, out cols);
        Assert.Equal(Sel.At(20), down3.Items[0]); // "xyz" is shorter: its end
        var up = MultiCaret.MoveVertical(h.Text, down3, -1, false, 4, cols, out _);
        Assert.Equal(Sel.At(14), up.Items[0]);
    }

    [Fact]
    public void Vertical_movement_counts_a_tab_as_the_columns_it_covers()
    {
        using var h = Open("\tabc\n12345678abc"); // after the tab and "a": column 5
        var down = MultiCaret.MoveVertical(h.Text, Carets.One(Sel.At(2)), 1, false, 4, null, out _);
        Assert.Equal(Sel.At(10), down.Items[0]); // offset 5 starts line 2, column 5 is five chars in
    }

    [Fact]
    public void The_first_and_last_line_go_to_the_edge_and_carets_that_meet_there_merge()
    {
        using var h = Open("a|b\nc|d\ne|f");
        var down = MultiCaret.MoveVertical(h.Text, h.Carets, 1, false, 4, null, out var cols);
        Assert.Equal("ab\nc|d\ne|f|", NoStars(Mark(h.Text.Text, down)));
        var again = MultiCaret.MoveVertical(h.Text, down, 1, false, 4, cols, out _);
        Assert.Equal("ab\ncd\ne|f|", NoStars(Mark(h.Text.Text, again)));
    }

    // ---- adding ----

    [Fact]
    public void Add_next_occurrence_selects_the_word_then_each_call_adds_one_wrapping_around_the_end()
    {
        using var h = Open("foo bar foo food foo");
        bool whole = false;
        var first = MultiCaret.AddNextOccurrence(h.Text, Carets.One(Sel.At(1)), ref whole)!;
        Assert.True(whole);
        Assert.Equal("«foo» bar foo food foo", Mark(h.Text.Text, first));
        var second = MultiCaret.AddNextOccurrence(h.Text, first, ref whole)!;
        Assert.Equal("«foo» bar «foo»* food foo", Mark(h.Text.Text, second));
        var third = MultiCaret.AddNextOccurrence(h.Text, second, ref whole)!; // "food" is not a whole word
        Assert.Equal("«foo» bar «foo» food «foo»*", Mark(h.Text.Text, third));
        Assert.Null(MultiCaret.AddNextOccurrence(h.Text, third, ref whole)); // every occurrence is taken
    }

    [Fact]
    public void A_selection_made_by_hand_matches_inside_words_too()
    {
        using var h = Open("«foo» bar foo food");
        bool whole = false;
        var next = MultiCaret.AddNextOccurrence(h.Text, h.Carets, ref whole)!;
        var again = MultiCaret.AddNextOccurrence(h.Text, next, ref whole)!;
        Assert.Equal("«foo» bar «foo» «foo»*d", Mark(h.Text.Text, again));
    }

    [Fact]
    public void Skip_occurrence_moves_the_last_selection_to_the_next_hit()
    {
        using var h = Open("foo foo foo");
        bool whole = true;
        var added = MultiCaret.AddNextOccurrence(h.Text, Carets.Create([new Sel(0, 3)], 0), ref whole)!;
        Assert.Equal("«foo» «foo»* foo", Mark(h.Text.Text, added));
        var skipped = MultiCaret.SkipOccurrence(h.Text, added, ref whole)!;
        Assert.Equal("«foo» foo «foo»*", Mark(h.Text.Text, skipped));
    }

    [Fact]
    public void Select_all_occurrences_of_a_selection_or_of_the_word_under_the_caret()
    {
        using var h = Open("a.b a.b ab a.b");
        bool whole = false;
        var all = MultiCaret.AllOccurrences(h.Text, Carets.Create([new Sel(0, 3)], 0), ref whole)!;
        Assert.Equal("«a.b»* «a.b» ab «a.b»", Mark(h.Text.Text, all));
        using var w = Open("x foo foo2 foo");
        var words = MultiCaret.AllOccurrences(w.Text, Carets.One(Sel.At(3)), ref whole)!;
        Assert.Equal("x «foo»* foo2 «foo»", Mark(w.Text.Text, words)); // foo2 is another word
        using var gap = Open("a  b");
        Assert.Null(MultiCaret.AllOccurrences(gap.Text, Carets.One(Sel.At(2)), ref whole)); // a caret between two blanks has no word
    }

    [Fact]
    public void Add_a_caret_above_or_below_at_the_same_visual_column()
    {
        using var h = Open("abcd\nab\nabcd"); // lines start at 0, 5, 8
        var below = MultiCaret.AddVertical(h.Text, Carets.One(Sel.At(3)), 1, 4)!;
        Assert.Equal([Sel.At(3), Sel.At(7)], below.Items);
        Assert.Equal(1, below.Primary);
        var below2 = MultiCaret.AddVertical(h.Text, below, 1, 4, column: 3)!; // the remembered column, not the short line's end
        Assert.Equal([Sel.At(3), Sel.At(7), Sel.At(11)], below2.Items);
        Assert.Null(MultiCaret.AddVertical(h.Text, below2, 1, 4)); // no line below
        var above = MultiCaret.AddVertical(h.Text, Carets.One(Sel.At(8)), -1, 4)!;
        Assert.Equal([Sel.At(5), Sel.At(8)], above.Items);
    }

    [Fact]
    public void Alt_click_adds_a_caret_and_a_second_click_on_it_removes_it()
    {
        var one = Carets.One(Sel.At(1));
        var two = MultiCaret.AddOrRemoveAt(one, 5);
        Assert.Equal([Sel.At(1), Sel.At(5)], two.Items);
        Assert.Equal(1, two.Primary);
        Assert.Equal([Sel.At(1)], MultiCaret.AddOrRemoveAt(two, 5).Items);
        Assert.Same(one, MultiCaret.AddOrRemoveAt(one, 1)); // the only caret stays
    }

    [Fact]
    public void Rows_of_a_column_selection_become_one_caret_each_with_the_last_row_primary()
    {
        var rows = MultiCaret.FromRows([new Sel(1, 3), new Sel(8, 10), new Sel(15, 17)]);
        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows.Primary);
    }

    [Theory]
    [InlineData("a\nb\nc", 3, "a|b|c")]
    [InlineData("a\nb\nc\n", 3, "a|b|c")]
    [InlineData("a\nb", 3, null)]
    [InlineData("a\r\nb\r\nc", 3, "a|b|c")]
    [InlineData("a\nb\nc", 1, null)]
    [InlineData("a\n\nc", 3, "a||c")]
    public void Clipboard_lines_are_dealt_to_the_carets_when_the_counts_agree(string text, int carets, string? expected) =>
        Assert.Equal(expected, MultiCaret.SplitForCarets(text, carets) is { } parts ? string.Join('|', parts) : null);

    [Fact]
    public void Copy_joins_the_selected_texts_in_document_order_and_ignores_empty_carets()
    {
        using var h = Open("«ab»c|d«ef»");
        Assert.Equal("ab\nef", MultiCaret.CopyText(h.Text, h.Carets, "\n"));
        Assert.Null(MultiCaret.CopyText(h.Text, Carets.One(Sel.At(1)), "\n"));
    }

    // ---- edits: bytes, one undo step, carets ----

    private static void Check(string input, Func<Opened, List<Planned>> plan, string expectedMarked, string expectedText, TextCodec? codec = null)
    {
        codec ??= TextCodec.Utf8;
        using var h = Open(input, codec);
        var original = h.Bytes;
        int count = h.Carets.Count;
        var result = MultiEdit.Apply(h.Doc, h.Backend, h.Carets, plan(h), out var after);
        Assert.True(result.Applied, result.Conflict.ToString());
        h.Carets = after;
        Assert.Equal(expectedText, h.Text.Text);
        Assert.Equal(expectedMarked, h.Marked);
        Assert.Equal(codec.Encode(expectedText), h.Bytes); // bytes and the text view agree
        Assert.Equal(1, h.Doc.UndoDepth);

        var undone = h.Doc.Undo()!;
        Assert.Equal(original, h.Bytes);
        Assert.Equal(count, undone.Selections.Count);
        var redone = h.Doc.Redo()!;
        Assert.Equal(codec.Encode(expectedText), h.Bytes);
        Assert.Equal(after.Count, redone.Selections.Count);
    }

    [Fact]
    public void Typing_a_character_goes_to_every_caret()
    {
        Check("a|b|c|", h => MultiEdit.PlanTyped(h.Text, h.Carets, "X", [PairedCharactersRule.Instance]), "aX|*bX|cX|", "aXbXcX");
    }

    [Fact]
    public void Typing_replaces_every_selection_and_writes_czech_text_as_exact_utf8()
    {
        Check("«ab»c «de»f", h => MultiEdit.PlanText(h.Carets, "příliš"), "příliš|*c příliš|f", "přílišc přílišf");
    }

    [Fact]
    public void Typing_an_opening_bracket_closes_it_at_every_caret_and_wraps_selections()
    {
        Check("a| b|", h => MultiEdit.PlanTyped(h.Text, h.Carets, "(", [PairedCharactersRule.Instance]), "a(|*) b(|)", "a() b()");
        Check("«a»x«b»", h => MultiEdit.PlanTyped(h.Text, h.Carets, "[", [PairedCharactersRule.Instance]), "[«a»*]x[«b»]", "[a]x[b]");
    }

    [Fact]
    public void Backspace_and_delete_remove_a_character_or_the_selection_at_every_caret()
    {
        Check("ab|c d|ef «gh»", h => MultiEdit.PlanDelete(h.Text, h.Carets, backwards: true, word: false, pairs: true), "a|*c |ef |", "ac ef ");
        Check("a|bc |def", h => MultiEdit.PlanDelete(h.Text, h.Carets, backwards: false, word: false, pairs: true), "a|*c |ef", "ac ef");
    }

    [Fact]
    public void Backspace_between_a_pair_removes_both_and_neighbouring_carets_do_not_clash()
    {
        Check("(|) [|]", h => MultiEdit.PlanDelete(h.Text, h.Carets, true, false, true), "|* |", " ");
        Check("ab|c|d", h => MultiEdit.PlanDelete(h.Text, h.Carets, true, false, true), "a|d", "ad"); // both deletions end at the same caret: one caret remains
        Check("(|)|", h => MultiEdit.PlanDelete(h.Text, h.Carets, true, false, true), "|", ""); // the pair takes the character the second caret would delete
    }

    [Fact]
    public void Delete_word_backwards_removes_up_to_the_word_border_at_every_caret()
    {
        Check("one two| three four|", h => MultiEdit.PlanDelete(h.Text, h.Carets, true, true, false), "one |* three |", "one  three ");
    }

    [Fact]
    public void Three_clipboard_lines_into_three_carets_give_one_line_each_and_one_undo_restores_all()
    {
        Check("|\n|\n|", h => MultiEdit.PlanText(h.Carets, "", MultiCaret.SplitForCarets("A\nB\nC", 3)), "A|*\nB|\nC|", "A\nB\nC");
    }

    [Fact]
    public void A_clipboard_text_of_another_line_count_goes_whole_into_every_caret()
    {
        Check("|-|", h => MultiEdit.PlanText(h.Carets, "x y"), "x y|*-x y|", "x y-x y");
    }

    [Fact]
    public void Enter_breaks_the_line_at_every_caret_keeping_the_indentation()
    {
        Check("  a|b\n    c|d", h => MultiEdit.PlanLineBreaks(h.Text, h.Carets, "\n", null), "  a\n  |*b\n    c\n    |d", "  a\n  b\n    c\n    d");
    }

    [Fact]
    public void Enter_continues_a_comment_at_every_caret()
    {
        var style = new CommentStyle("//", true);
        Check("// a|\nx\n// b|", h => MultiEdit.PlanLineBreaks(h.Text, h.Carets, "\n", style), "// a\n// |*\nx\n// b\n// |", "// a\n// \nx\n// b\n// ");
    }

    [Fact]
    public void A_single_byte_encoding_takes_the_same_path_with_the_same_bytes_and_still_reports_typed_loss()
    {
        Check("a|b|", h => MultiEdit.PlanText(h.Carets, "ž"), "až|*bž|", "ažbž", TextCodec.Cp1250);
        using var h = Open("a|b|", TextCodec.Cp1250);
        Assert.True(MultiEdit.Apply(h.Doc, h.Backend, h.Carets, MultiEdit.PlanText(h.Carets, "ж"), out _).Applied);
        Assert.Equal([0x61, 0x3F, 0x62, 0x3F], h.Bytes);
        Assert.True(h.Backend.TypedLoss().HasLoss);
    }

    [Fact]
    public void The_bytes_of_a_file_with_a_bom_and_crlf_stay_exact_when_edited_at_many_carets()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] original = [.. bom, .. "a\r\nb\r\nc"u8];
        using var doc = Doc.FromBytes(original, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8Bom });
        var backend = (AvaloniaEditBackend)doc.Backend;
        var carets = Carets.Create([Sel.At(1), Sel.At(4), Sel.At(7)], 0);
        Assert.True(MultiEdit.Apply(doc, backend, carets, MultiEdit.PlanText(carets, "€"), out var after).Applied);
        Assert.Equal([.. bom, .. "a€\r\nb€\r\nc€"u8], doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
        Assert.Equal(3, after.Count);
        doc.Undo();
        Assert.Equal(original, doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
    }
}
