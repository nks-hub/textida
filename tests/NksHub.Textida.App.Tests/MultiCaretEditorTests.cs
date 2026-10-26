using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using AdapterCarets = NksHub.Textida.AvaloniaEditAdapter.Carets;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P2.2.1 and P2.2.3 to P2.2.5 on the running editor: keys, mouse, text, clipboard and undo with several carets, through the same input events the
/// real window gets. Carets are printed with <c>|</c> (empty) and <c>«»</c> (selection); every edit also checks the bytes of the document.
/// </summary>
[Collection("LocState")]
public class MultiCaretEditorTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static MultiCaretEditorTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public MultiCaretEditorTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab Open(string text)
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        Dispatcher.UIThread.RunJobs();
        tab.Doc.FlushEol();
        tab.Editor.TextArea.Focus();
        tab.Editor.CaretOffset = 0;
        return tab;
    }

    /// <summary>Undo steps made since the text of the tab was set.</summary>
    private static int Steps(DocTab t) => t.Doc.Doc.UndoDepth - 1;

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    private static string Bytes(DocTab t) => Encoding.UTF8.GetString(t.Doc.Doc.ReadBytes(new ByteRange(0, t.Doc.Doc.ByteLength)));

    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    private void Mod(Key key, bool shift = false) => TestApp.Press(_w, key, TestApp.Mod | (shift ? RawInputModifiers.Shift : RawInputModifiers.None));

    private static IEnumerable<string> Carets(DocTab t) => t.Editor!.GetSelections().Select(s => $"{s.Anchor}:{s.Caret}");

    private static string Marked(DocTab t)
    {
        var sb = new StringBuilder(Text(t));
        foreach (var s in t.Editor!.GetSelections().OrderByDescending(s => s.End))
        {
            if (s.IsEmpty) sb.Insert(s.Caret, '|');
            else { sb.Insert(s.End, '»'); sb.Insert(s.Start, '«'); }
        }
        return sb.ToString();
    }

    /// <summary>Several carets, the last one primary.</summary>
    private static void PlaceCarets(DocTab t, params int[] offsets) =>
        t.Editor!.SetCarets(AdapterCarets.Create([.. offsets.Select(Sel.At)], offsets.Length - 1));

    private Point At(DocTab tab, int line, int column)
    {
        _w.UpdateLayout();
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        return view.TranslatePoint(view.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.LineMiddle), _w)!.Value;
    }

    private void Click(Point p, RawInputModifiers mods = RawInputModifiers.None)
    {
        _w.MouseDown(p, MouseButton.Left, mods);
        _w.MouseUp(p, MouseButton.Left, mods);
    }

    // ---- adding: occurrences, above and below ----

    [AvaloniaFact]
    public void Ctrl_D_selects_the_word_then_adds_the_next_occurrences_and_typing_replaces_all_of_them_in_one_undo_step()
    {
        var tab = Open("foo bar foo food foo");
        tab.Editor!.CaretOffset = 1;
        Mod(Key.D);
        Assert.Equal("foo", tab.Editor.SelectedText);
        Assert.False(tab.Editor.IsMulti);
        Mod(Key.D);
        Mod(Key.D); // "food" is not the word
        Assert.Equal(3, tab.Editor.CaretCount);
        Assert.Equal("«foo» bar «foo» food «foo»", Marked(tab));

        TestApp.Type(_w, "X");
        Assert.Equal("X bar X food X", Text(tab));
        Assert.Equal("X bar X food X", Bytes(tab));
        Assert.Equal("X| bar X| food X|", Marked(tab));
        Assert.Equal(1, Steps(tab));

        Mod(Key.Z);
        Assert.Equal("foo bar foo food foo", Text(tab));
        Assert.Equal("«foo» bar «foo» food «foo»", Marked(tab));
        Mod(Key.Y);
        Assert.Equal("X bar X food X", Text(tab));
        Assert.Equal(3, tab.Editor.CaretCount);
    }

    [AvaloniaFact]
    public void Ctrl_K_Ctrl_D_skips_the_last_selection_and_Ctrl_Shift_L_selects_every_occurrence()
    {
        var tab = Open("ab ab ab ab");
        tab.Editor!.Select(0, 2);
        Mod(Key.D);
        Mod(Key.K);
        Mod(Key.D);
        Assert.Equal("«ab» ab «ab» ab", Marked(tab));
        Mod(Key.L, shift: true);
        Assert.Equal("«ab» «ab» «ab» «ab»", Marked(tab));
        Assert.Equal(4, tab.Editor.CaretCount);
    }

    [AvaloniaFact]
    public void Ctrl_Alt_Down_and_Up_add_carets_at_the_same_visual_column_across_short_lines()
    {
        var tab = Open("abcd\nab\nabcd");
        tab.Editor!.CaretOffset = 3;
        Press(Key.Down, TestApp.Mod | RawInputModifiers.Alt);
        Press(Key.Down, TestApp.Mod | RawInputModifiers.Alt);
        Assert.Equal("abc|d\nab|\nabc|d", Marked(tab));
        TestApp.Type(_w, "X");
        Assert.Equal("abcXd\nabX\nabcXd", Text(tab));

        var up = Open("abcd\nab\nabcd");
        up.Editor!.CaretOffset = 11;
        Press(Key.Up, TestApp.Mod | RawInputModifiers.Alt);
        Press(Key.Up, TestApp.Mod | RawInputModifiers.Alt);
        Assert.Equal("abc|d\nab|\nabc|d", Marked(up));
    }

    [AvaloniaFact]
    public void Escape_removes_the_extra_carets_and_keeps_the_primary_one()
    {
        var tab = Open("a b c");
        PlaceCarets(tab, 0, 2, 4);
        Assert.True(tab.Editor!.IsMulti);
        Press(Key.Escape);
        Assert.False(tab.Editor.IsMulti);
        Assert.Equal("a b |c", Marked(tab));
    }

    [AvaloniaFact]
    public void The_status_bar_counts_the_carets_and_the_selected_characters()
    {
        var tab = Open("ab ab ab");
        tab.Editor!.Select(0, 2);
        Mod(Key.L, shift: true);
        Assert.Equal(3, tab.Editor.CaretCount);
        Assert.Contains(Loc.Get(K.StatusCarets, ("count", 3)), _w.StatusLeftText);
        Assert.Contains(Loc.Get(K.StatusSelected, ("count", 6)), _w.StatusLeftText);
        Press(Key.Escape);
        Assert.DoesNotContain(Loc.Get(K.StatusCarets, ("count", 3)), _w.StatusLeftText);
    }

    // ---- mouse ----

    [AvaloniaFact]
    public void Alt_click_adds_a_caret_and_Alt_click_on_it_removes_it()
    {
        var tab = Open("abcdef\nabcdef\nabcdef");
        tab.Editor!.CaretOffset = 2;
        Click(At(tab, 2, 4), RawInputModifiers.Alt);
        Assert.Equal(["2:2", "10:10"], Carets(tab));
        Click(At(tab, 3, 2), RawInputModifiers.Alt);
        Assert.Equal(["2:2", "10:10", "15:15"], Carets(tab));
        TestApp.Type(_w, "-");
        Assert.Equal("ab-cdef\nabc-def\na-bcdef", Text(tab));

        Click(At(tab, 2, 5), RawInputModifiers.Alt); // the second caret now sits after the minus
        Assert.Equal(2, tab.Editor.CaretCount);
    }

    [AvaloniaFact]
    public void A_plain_click_removes_the_extra_carets_and_Shift_click_extends_the_primary_selection()
    {
        var tab = Open("abcdef\nabcdef\nabcdef");
        PlaceCarets(tab, 1, 8, 15);
        Click(At(tab, 2, 3));
        Assert.False(tab.Editor!.IsMulti);
        Assert.Equal(9, tab.Editor.CaretOffset);

        PlaceCarets(tab, 1, 8, 15);
        Click(At(tab, 3, 4), RawInputModifiers.Shift);
        Assert.False(tab.Editor.IsMulti);
        Assert.Equal(["15:17"], Carets(tab));
    }

    [AvaloniaFact]
    public void Alt_drag_makes_one_caret_per_row_and_typing_goes_to_every_row()
    {
        var tab = Open("abcdef\nabcdef\nabcdef");
        var from = At(tab, 1, 2);
        var to = At(tab, 3, 5);
        _w.MouseDown(from, MouseButton.Left, RawInputModifiers.Alt);
        _w.MouseMove(new Point(to.X, from.Y + 1), RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        _w.MouseMove(to, RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        _w.MouseUp(to, MouseButton.Left, RawInputModifiers.Alt);
        Assert.IsNotType<RectangleSelection>(tab.Editor!.TextArea.Selection);
        Assert.Equal(["1:4", "8:11", "15:18"], Carets(tab));
        TestApp.Type(_w, "Z");
        Assert.Equal("aZef\naZef\naZef", Text(tab));
        Mod(Key.Z);
        Assert.Equal("abcdef\nabcdef\nabcdef", Text(tab));
    }

    // ---- keys with several carets ----

    [AvaloniaFact]
    public void Arrows_Home_End_and_word_keys_move_every_caret_and_carets_that_meet_merge()
    {
        var tab = Open("one two\nthree four\nfive six"); // lines start at 0, 8, 19
        PlaceCarets(tab, 3, 13, 21);
        Press(Key.Right);
        Assert.Equal(["4:4", "14:14", "22:22"], Carets(tab));
        Press(Key.Left);
        Press(Key.Left);
        Assert.Equal(["2:2", "12:12", "20:20"], Carets(tab));
        Press(Key.Home);
        Assert.Equal(["0:0", "8:8", "19:19"], Carets(tab));
        Press(Key.End);
        Assert.Equal(["7:7", "18:18", "27:27"], Carets(tab));
        Press(Key.Left, TestApp.Mod);
        Assert.Equal(["4:4", "14:14", "24:24"], Carets(tab));
        Press(Key.Right, RawInputModifiers.Shift);
        Press(Key.Left, RawInputModifiers.Shift);
        Assert.Equal(3, tab.Editor!.CaretCount);

        PlaceCarets(tab, 0, 1);
        Press(Key.Left);
        Assert.False(tab.Editor.IsMulti); // both are at 0 now
        Assert.Equal(["0:0"], Carets(tab));
    }

    [AvaloniaFact]
    public void Ctrl_Right_moves_every_caret_to_the_next_word()
    {
        var tab = Open("one two three\nfour five six");
        PlaceCarets(tab, 0, 14);
        Press(Key.Right, TestApp.Mod);
        Assert.Equal(["4:4", "19:19"], Carets(tab));
    }

    [AvaloniaFact]
    public void Up_and_Down_keep_the_sticky_column_and_Shift_extends_the_selections()
    {
        var tab = Open("abcdef\nab\nabcdef\nxy"); // lines start at 0, 7, 10, 17
        PlaceCarets(tab, 4, 14);
        Press(Key.Down);
        Assert.Equal(["9:9", "19:19"], Carets(tab)); // the end of "ab" and the end of "xy"
        Press(Key.Up);
        Assert.Equal(["4:4", "14:14"], Carets(tab)); // column 4 again
        Press(Key.Down, RawInputModifiers.Shift);
        Assert.Equal(["4:9", "14:19"], Carets(tab));
    }

    [AvaloniaFact]
    public void Backspace_Delete_and_Ctrl_Backspace_work_at_every_caret_in_one_undo_step_each()
    {
        var tab = Open("abc def\nxyz uvw"); // line 2 starts at 8
        PlaceCarets(tab, 3, 11);
        Press(Key.Back);
        Assert.Equal("ab def\nxy uvw", Text(tab));
        Assert.Equal("ab def\nxy uvw", Bytes(tab));
        Mod(Key.Z);
        Assert.Equal("abc def\nxyz uvw", Text(tab));
        Assert.Equal(["3:3", "11:11"], Carets(tab));

        Press(Key.Delete);
        Assert.Equal("abcdef\nxyzuvw", Text(tab));
        Assert.Equal(["3:3", "10:10"], Carets(tab));
        Mod(Key.Z);

        PlaceCarets(tab, 7, 15);
        Press(Key.Back, TestApp.Mod);
        Assert.Equal("abc \nxyz ", Text(tab));
        Assert.Equal("abc |\nxyz |", Marked(tab));
    }

    [AvaloniaFact]
    public void Enter_breaks_every_line_with_its_own_indentation_in_one_undo_step()
    {
        var tab = Open("  a b\n    c d");
        PlaceCarets(tab, 3, 11);
        Press(Key.Enter);
        Assert.Equal("  a\n   b\n    c\n     d", Text(tab));
        Assert.Equal("  a\n  | b\n    c\n    | d", Marked(tab));
        Assert.Equal(1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("  a b\n    c d", Text(tab));
    }

    [AvaloniaFact]
    public void Enter_continues_a_comment_at_every_caret()
    {
        var tab = Open("// a\nx\n// b");
        tab.Doc.Language = LanguageModes.ById("csharp");
        PlaceCarets(tab, 4, 11);
        Press(Key.Enter);
        Assert.Equal("// a\n// \nx\n// b\n// ", Text(tab));
    }

    [AvaloniaFact]
    public void Tab_and_line_commands_run_over_every_caret()
    {
        var tab = Open("a\nb\nc");
        PlaceCarets(tab, 0, 2, 4);
        Press(Key.Tab);
        Assert.Equal("    a\n    b\n    c", Text(tab));
        Mod(Key.Z);

        var lines = Open("a\nb\nc\nd\ne");
        PlaceCarets(lines, 0, 4, 8); // lines 1, 3 and 5
        Mod(Key.D, shift: true);
        Assert.Equal("a\na\nb\nc\nc\nd\ne\ne", Text(lines));
        Mod(Key.Z);
        Assert.Equal("a\nb\nc\nd\ne", Text(lines));
        Assert.Equal(3, lines.Editor!.CaretCount);
    }

    [AvaloniaFact]
    public void Case_commands_apply_to_every_selection()
    {
        var tab = Open("ab cd ef");
        tab.Editor!.SetSelections([new Sel(0, 2), new Sel(6, 8)]);
        Mod(Key.K);
        Mod(Key.U);
        Assert.Equal("AB cd EF", Text(tab));
        Mod(Key.Z);
        Assert.Equal("ab cd ef", Text(tab));
    }

    [AvaloniaFact]
    public void Select_line_takes_the_line_of_every_caret()
    {
        var tab = Open("a\nb\nc\nd");
        PlaceCarets(tab, 0, 4);
        Mod(Key.L);
        Assert.Equal("«a\n»b\n«c\n»d", Marked(tab));
    }

    // ---- text, composition and the clipboard ----

    [AvaloniaFact]
    public void A_composition_committed_with_several_carets_is_inserted_once_at_each_and_undoes_as_one_step()
    {
        var tab = Open("a b c");
        PlaceCarets(tab, 1, 3, 5);
        _w.KeyTextInput("日本");
        Assert.Equal("a日本 b日本 c日本", Text(tab));
        Assert.Equal("a日本 b日本 c日本", Bytes(tab));
        Assert.Equal(1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("a b c", Text(tab));
        Assert.Equal(["1:1", "3:3", "5:5"], Carets(tab));
    }

    [AvaloniaFact]
    public void Typing_runs_with_several_carets_are_one_undo_step_and_a_caret_move_starts_another()
    {
        var tab = Open("a b");
        PlaceCarets(tab, 1, 3);
        TestApp.Type(_w, "xyz");
        Assert.Equal("axyz bxyz", Text(tab));
        Assert.Equal(1, Steps(tab));
        Press(Key.Left);
        TestApp.Type(_w, "!");
        Assert.Equal(2, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("axyz bxyz", Text(tab));
        Mod(Key.Z);
        Assert.Equal("a b", Text(tab));
    }

    [AvaloniaFact]
    public void Brackets_are_closed_and_typed_over_at_every_caret()
    {
        var tab = Open("a b");
        PlaceCarets(tab, 1, 3);
        TestApp.Type(_w, "(");
        Assert.Equal("a() b()", Text(tab));
        Assert.Equal("a(|) b(|)", Marked(tab));
        TestApp.Type(_w, ")");
        Assert.Equal("a() b()", Text(tab));
        Assert.Equal("a()| b()|", Marked(tab));
    }

    [AvaloniaFact]
    public void Three_clipboard_lines_into_three_carets_give_one_line_each_and_one_undo_restores_all_carets()
    {
        var tab = Open("x\ny\nz");
        PlaceCarets(tab, 1, 3, 5);
        _w.PasteText(tab.Editor!, "A\nB\nC");
        Assert.Equal("xA\nyB\nzC", Text(tab));
        Assert.Equal("xA\nyB\nzC", Bytes(tab));
        Assert.Equal(["2:2", "5:5", "8:8"], Carets(tab));
        Assert.Equal(1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("x\ny\nz", Text(tab));
        Assert.Equal(["1:1", "3:3", "5:5"], Carets(tab));
    }

    [AvaloniaFact]
    public void A_clipboard_text_of_another_line_count_goes_whole_into_every_caret()
    {
        var tab = Open("x\ny");
        PlaceCarets(tab, 1, 3);
        _w.PasteText(tab.Editor!, "1\n2\n3");
        Assert.Equal("x1\n2\n3\ny1\n2\n3", Text(tab));
    }

    [AvaloniaFact]
    public async Task Copy_puts_the_selected_texts_on_the_clipboard_one_per_line_and_cut_removes_them()
    {
        var tab = Open("foo bar foo");
        tab.Editor!.SetSelections([new Sel(0, 3), new Sel(8, 11)]);
        Mod(Key.C);
        await Task.Delay(150); // the clipboard is written asynchronously
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("foo" + tab.Doc.NewLine(null) + "foo", await _w.Clipboard!.TryGetTextAsync());
        Assert.Equal("foo bar foo", Text(tab));

        Mod(Key.X);
        Assert.Equal(" bar ", Text(tab));
        Mod(Key.Z);
        Assert.Equal("foo bar foo", Text(tab));
        Assert.Equal(2, tab.Editor.CaretCount);
    }

    // ---- changes from another source ----

    [AvaloniaFact]
    public void Carets_stay_on_their_text_when_another_source_changes_the_document()
    {
        var tab = Open("one two three");
        tab.Editor!.SetSelections([new Sel(0, 3), new Sel(8, 13)]);
        var doc = tab.Doc.Doc;

        Assert.True(doc.Insert(0, ">>> "u8.ToArray()).Applied);                              // before all of them
        Assert.Equal(">>> «one» two «three»", Marked(tab));
        Assert.True(doc.Replace(new ByteRange(8, 11), "2"u8.ToArray()).Applied);               // between them
        Assert.Equal(">>> «one» 2 «three»", Marked(tab));
        Assert.Equal(2, tab.Editor.CaretCount);

        doc.Undo();
        Assert.Equal(">>> «one» two «three»", Marked(tab));
    }

    [AvaloniaFact]
    public void Carets_survive_a_reread_of_the_file_as_another_encoding()
    {
        var path = _temp.Bytes("utf8.txt", [.. "é X\né X"u8]);
        var tab = _w.OpenFile(path, null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        Assert.Equal("é X\né X", Text(tab));
        tab.Editor.SetSelections([new Sel(2, 3), new Sel(6, 7)]); // the two X

        tab.Doc.Doc.ReinterpretAs(TextCodec.Cp1250);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, tab.Editor.CaretCount);
        var selected = tab.Editor.GetSelections().Select(s => Text(tab).Substring(s.Start, s.Length));
        Assert.Equal(["X", "X"], selected);
    }

    // ---- many carets ----

    [AvaloniaFact]
    public void A_thousand_carets_take_every_insertion_and_every_undo_as_expected()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 2000; i++) sb.Append("line ").Append(i).Append(" lorem ipsum\n");
        string original = sb.ToString();
        var tab = Open(original);
        var starts = Enumerable.Range(0, 1000).Select(i => tab.Editor!.Document.GetLineByNumber(i * 2 + 1).Offset + 5).ToArray(); // after "line "
        PlaceCarets(tab, starts);
        Assert.Equal(1000, tab.Editor!.CaretCount);

        TestApp.Type(_w, "abc");
        var lines = original.Split('\n');
        for (int i = 0; i < 1000; i++) lines[i * 2] = lines[i * 2].Insert(5, "abc");
        string expected = string.Join('\n', lines);
        Assert.Equal(expected, Text(tab));
        Assert.Equal(expected, Bytes(tab));
        Assert.Equal(1, Steps(tab));
        Assert.Equal(1000, tab.Editor.CaretCount);

        Press(Key.Back);
        Assert.Equal(2, Steps(tab)); // the deleting is a step of its own
        Mod(Key.Z);
        Mod(Key.Z);
        Assert.Equal(original, Text(tab));
        Assert.Equal(original, Bytes(tab));
        Assert.Equal(1000, tab.Editor.CaretCount);
    }
}
