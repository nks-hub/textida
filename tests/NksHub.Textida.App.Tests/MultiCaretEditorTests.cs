using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
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

    /// <summary>The client the platform input method gets from the focused editor.</summary>
    private static TextInputMethodClient ImeClient(DocTab t)
    {
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        t.Editor!.TextArea.RaiseEvent(args);
        return Assert.IsAssignableFrom<TextInputMethodClient>(args.Client);
    }

    /// <summary>The compositions drawn in the visible lines, as their offsets in the document.</summary>
    private IEnumerable<int> Drawn(DocTab t)
    {
        _w.UpdateLayout();
        var view = t.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        return [.. view.VisualLines.SelectMany(l => l.Elements.OfType<EditorSurface.PreeditElement>().Select(e => l.FirstDocumentLine.Offset + e.RelativeTextOffset))];
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
        TestApp.Redo(_w);
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
        Assert.DoesNotContain("status.carets", _w.StatusLeftText);
        Assert.Contains(Loc.Plural(K.StatusCarets, 3), _w.StatusLeftText);
        Assert.Contains(Loc.Get(K.StatusSelected, ("count", 6)), _w.StatusLeftText);
        Press(Key.Escape);
        Assert.DoesNotContain(Loc.Plural(K.StatusCarets, 3), _w.StatusLeftText);
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
        Press(Key.Left, TestApp.Word);
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
        Press(Key.Right, TestApp.Word);
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
        Press(Key.Back, TestApp.Word);
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
        tab.Doc.ChooseLanguage(LanguageModes.ById("csharp"));
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
    public void A_composition_is_drawn_at_every_caret_and_the_document_changes_only_with_the_commit()
    {
        var tab = Open("a b\nc");
        PlaceCarets(tab, 1, 3, 5);
        var client = ImeClient(tab);
        Assert.True(client.SupportsPreedit);
        long revision = tab.Doc.Doc.Revision;

        client.SetPreeditText("に");
        client.SetPreeditText("にほ");
        Assert.Equal([1, 3, 5], Drawn(tab));
        Assert.Equal("a b\nc", Text(tab));
        Assert.Equal(revision, tab.Doc.Doc.Revision);
        Assert.Equal(3, tab.Editor!.CaretCount);

        client.SetPreeditText(null); // what the platform does before it delivers the finished text
        _w.KeyTextInput("日本");
        Assert.Empty(Drawn(tab));
        Assert.Equal("a日本 b日本\nc日本", Bytes(tab));
        Assert.Equal(1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("a b\nc", Text(tab));
        Assert.Equal(["1:1", "3:3", "5:5"], Carets(tab));
    }

    [AvaloniaFact]
    public void A_composition_is_drawn_before_a_space_or_tab_shown_as_a_hidden_character()
    {
        var tab = Open("a b\tc");
        _w.ToggleHiddenCharacters();
        PlaceCarets(tab, 1, 3);
        ImeClient(tab).SetPreeditText("に");
        Assert.Equal([1, 3], Drawn(tab));
        Assert.Equal("a b\tc", Text(tab));
    }

    [AvaloniaFact]
    public void A_commit_that_arrives_while_the_composition_is_drawn_lands_once_at_every_caret()
    {
        var tab = Open("a b");
        PlaceCarets(tab, 1, 3);
        var client = ImeClient(tab);
        client.SetPreeditText("にほ");
        _w.KeyTextInput("日本");
        Assert.Empty(Drawn(tab));
        Assert.Equal("", tab.Editor!.Composition);
        Assert.Equal("a日本 b日本", Bytes(tab));
    }

    [AvaloniaFact]
    public void Escape_during_a_composition_belongs_to_the_input_method_and_a_cancelled_composition_leaves_text_and_carets()
    {
        var tab = Open("a b c");
        PlaceCarets(tab, 1, 3, 5);
        var client = ImeClient(tab);
        client.SetPreeditText("ni");
        Press(Key.Escape);
        Assert.Equal(3, tab.Editor!.CaretCount);
        client.SetPreeditText(null); // cancelled
        Assert.Empty(Drawn(tab));
        Assert.Equal("a b c", Text(tab));
        Assert.Equal(0, Steps(tab));

        Press(Key.Escape);
        Assert.False(tab.Editor.IsMulti);
    }

    [AvaloniaFact]
    public void A_composition_ends_when_the_caret_moves_or_another_tab_takes_the_focus_and_writes_nothing()
    {
        var tab = Open("abc\nabc");
        tab.Editor!.CaretOffset = 1;
        var client = ImeClient(tab);
        client.SetPreeditText("ka");
        Assert.Equal([1], Drawn(tab));
        tab.Editor.CaretOffset = 5;
        Assert.Empty(Drawn(tab));

        client.SetPreeditText("ka");
        Assert.Equal([5], Drawn(tab));
        var other = _w.NewDocument();
        Dispatcher.UIThread.RunJobs();
        other.Editor!.TextArea.Focus();
        Assert.Equal("", tab.Editor.Composition);
        Assert.Equal("abc\nabc", Text(tab));
        Assert.Equal(0, Steps(tab));
    }

    [AvaloniaFact]
    public void The_candidate_window_of_the_input_method_opens_after_the_composition()
    {
        var tab = Open("abc");
        tab.Editor!.CaretOffset = 1;
        _w.UpdateLayout();
        var client = ImeClient(tab);
        var start = client.CursorRectangle;
        int moved = 0;
        client.CursorRectangleChanged += (_, _) => moved++;

        client.SetPreeditText("にほ");
        var narrow = client.CursorRectangle;
        client.SetPreeditText("にほんご");
        var wide = client.CursorRectangle;
        Assert.True(narrow.X > start.X);
        Assert.True(wide.X > narrow.X);
        Assert.Equal(start.Y, wide.Y);
        Assert.True(moved >= 2);

        client.SetPreeditText(null);
        Assert.Equal(start, client.CursorRectangle);
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

    [AvaloniaFact]
    public void A_thousand_carets_in_a_one_mebibyte_file_take_the_insertion_the_undo_and_the_redo_as_expected()
    {
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 1 << 20; i++) sb.Append("line ").Append(i).Append(" lorem ipsum dolor sit amet consectetur\n");
        string original = sb.ToString();
        var tab = _w.OpenFile(_temp.File("one-mib.txt", original), null); // opened from a file like the performance gate does; a typed-in text of this size makes every edit far slower
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        tab.Editor.CaretOffset = 0;
        var d = tab.Editor.Document;
        int step = d.LineCount / 1000;
        var lineNumbers = Enumerable.Range(0, 1000).Select(i => 1 + i * step).ToArray();
        PlaceCarets(tab, [.. lineNumbers.Select(n => d.GetLineByNumber(n).Offset + 5)]);
        Assert.Equal(1000, tab.Editor.CaretCount);

        TestApp.Type(_w, "Qé");
        var lines = original.Split('\n');
        foreach (int n in lineNumbers) lines[n - 1] = lines[n - 1].Insert(5, "Qé");
        string expected = string.Join('\n', lines);
        Assert.True(expected == Text(tab), "the typed text did not land at every caret");
        Assert.True(expected == Bytes(tab), "the bytes of the document differ from the text");
        Assert.Equal(1, tab.Doc.Doc.UndoDepth); // the typing is one step
        Assert.Equal(1000, tab.Editor.CaretCount);

        Mod(Key.Z);
        Assert.True(original == Text(tab), "one undo did not restore the text");
        Assert.True(original == Bytes(tab), "one undo did not restore the bytes");
        Assert.Equal(1000, tab.Editor.CaretCount);

        Mod(Key.Z, shift: true);
        Assert.True(expected == Text(tab), "the redo did not repeat the insertion");
        Assert.Equal(1000, tab.Editor.CaretCount);
    }

    // ---- composition: keys and changes that end it ----

    [AvaloniaFact]
    public void Arrows_and_Enter_during_a_composition_do_not_move_the_other_carets_or_break_their_lines()
    {
        var tab = Open("a b c");
        var client = ImeClient(tab);
        foreach (var key in new[] { Key.Left, Key.Right, Key.Home, Key.End, Key.Up, Key.Down })
        {
            PlaceCarets(tab, 1, 3, 5);
            client.SetPreeditText("ni");
            Assert.True(tab.Editor!.IsComposing);
            Press(key);
            // the area moves its own caret as it always does; the other two stay where they were
            var carets = Carets(tab).ToList();
            Assert.True(carets.Contains("1:1") && carets.Contains("3:3"), $"{key} moved the carets to {string.Join(" ", carets)}");
            Assert.Equal(3, tab.Editor.CaretCount);
            Assert.Equal("a b c", Text(tab));
        }

        PlaceCarets(tab, 1, 3, 5);
        client.SetPreeditText("ni");
        Press(Key.Enter);
        Assert.Equal(3, tab.Editor!.CaretCount);
        Assert.True(Text(tab).Count(c => c == '\n') <= 1, "Enter was run as a command at every caret: " + Text(tab).Replace("\n", "\\n"));

        // the same key outside a composition moves every caret, so the checks above can fail
        tab.Editor.SetCarets(AdapterCarets.Create([Sel.At(1), Sel.At(3)], 1));
        Assert.False(tab.Editor.IsComposing);
        Press(Key.Right);
        Assert.Equal(["2:2", "4:4"], Carets(tab));
    }

    [AvaloniaFact]
    public void A_shortcut_with_Ctrl_still_runs_during_a_composition()
    {
        var tab = Open("a b c");
        PlaceCarets(tab, 1, 3);
        ImeClient(tab).SetPreeditText("ni");
        int tabs = _w.Tabs.Count();
        Mod(Key.N);
        Assert.Equal(tabs + 1, _w.Tabs.Count());
    }

    [AvaloniaFact]
    public void A_composition_ends_when_the_editor_loses_the_focus_to_another_control_and_writes_nothing()
    {
        var tab = Open("abc");
        tab.Editor!.CaretOffset = 1;
        ImeClient(tab).SetPreeditText("ka");
        Assert.Equal([1], Drawn(tab));

        TestApp.Press(_w, Key.F, TestApp.Mod); // the find bar takes the focus
        Assert.True(_w.FindBar.IsVisible);
        Assert.Equal("", tab.Editor.Composition);
        Assert.Empty(Drawn(tab));
        Assert.Equal("abc", Text(tab));
        Assert.Equal(0, Steps(tab));
    }

    [AvaloniaFact]
    public void Escape_during_a_composition_does_not_close_the_find_bar_and_closes_it_afterwards()
    {
        var tab = Open("abc");
        TestApp.Press(_w, Key.F, TestApp.Mod);
        Assert.True(_w.FindBar.IsVisible);
        tab.Editor!.TextArea.Focus();
        var client = ImeClient(tab);
        client.SetPreeditText("ka");
        Assert.True(tab.Editor.IsComposing);

        Press(Key.Escape);
        Assert.True(_w.FindBar.IsVisible);
        client.SetPreeditText(null); // the input method cancelled
        Press(Key.Escape);
        Assert.False(_w.FindBar.IsVisible);
    }

    [AvaloniaFact]
    public void A_change_of_the_document_from_another_source_ends_the_composition_without_writing_it()
    {
        var tab = Open("abc\nabc");
        PlaceCarets(tab, 1, 5);
        ImeClient(tab).SetPreeditText("ka");
        Assert.Equal([1, 5], Drawn(tab));

        Assert.True(tab.Doc.Doc.Insert(0, ">>"u8.ToArray()).Applied);
        Assert.Equal("", tab.Editor!.Composition);
        Assert.Empty(Drawn(tab));
        Assert.Equal(">>abc\nabc", Text(tab));
        Assert.Equal(2, tab.Editor.CaretCount);
    }

    [AvaloniaFact]
    public void An_edit_command_over_a_thousand_carets_is_one_undo_step_and_takes_seconds_not_minutes()
    {
        const int count = 1000;
        var tab = Open(string.Concat(Enumerable.Repeat("ab\n", count)));
        tab.Editor!.SetCarets(AdapterCarets.Create(Enumerable.Range(0, count).Select(i => new Sel(3 * i, 3 * i + 2)).ToList(), count - 1));
        int steps = Steps(tab);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = _app.Workbench.Commands.ExecuteAsync("nks.edit.upperCase", new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();
        watch.Stop();

        Assert.Equal(NksHub.Textida.Workbench.Commands.CommandOutcome.Handled, outcome);
        Assert.Equal(string.Concat(Enumerable.Repeat("AB\n", count)), Text(tab));
        Assert.Equal(steps + 1, Steps(tab));
        Assert.Equal(count, tab.Editor.CaretCount);
        // every edit used to move all the carets again, so the time grew with the square of their number: about 20 s for this in a debug build, a second now
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"took {watch.Elapsed}");
    }

    // ---- the batch of changes passes the events of the text on without the weak-event managers ----

    private static Delegate[] Listeners(AvaloniaEdit.Document.TextDocument d, string eventName) =>
        ((Delegate?)typeof(AvaloniaEdit.Document.TextDocument).GetField(eventName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(d))?.GetInvocationList() ?? [];

    [AvaloniaFact]
    public void The_bypass_reaches_the_listeners_of_the_views_and_gives_the_event_back()
    {
        var tab = Open("abc\nabc");
        var d = tab.Editor!.Document;
        tab.Editor.CaretOffset = 6;
        var changing = Listeners(d, "Changing");
        var changed = Listeners(d, "Changed");
        var seen = new List<string>();
        EventHandler<AvaloniaEdit.Document.DocumentChangeEventArgs> onChanged = (_, e) => seen.Add($"{e.Offset}:{e.InsertedText.Text}");

        using (var bypass = WeakEventBypass.Begin(d))
        {
            // if AvaloniaEdit changes the way it keeps its listeners this fails, and the multi-caret typing gate gets slow again
            Assert.True(bypass.Active);
            d.Changed += onChanged; // a listener added during the batch must survive it
            using (d.RunUpdate())
            {
                d.Insert(1, "x");
                d.Insert(0, "yy");
            }
        }

        Assert.Equal(["1:x", "0:yy"], seen);
        Assert.Equal(9, tab.Editor.CaretOffset); // the text area is a listener of the manager: it moved with both insertions
        Assert.Equal(changing, Listeners(d, "Changing"));
        Assert.Equal([.. changed, onChanged], Listeners(d, "Changed"));

        d.Insert(0, "z"); // and the managers work again
        Assert.Equal(10, tab.Editor.CaretOffset);
    }

    [AvaloniaFact]
    public void The_bypass_tells_a_view_only_about_the_changes_that_touch_its_lines_and_other_views_about_all()
    {
        var tab = Open(string.Concat(Enumerable.Range(0, 300).Select(i => $"line {i}\n")));
        var d = tab.Editor!.Document;
        tab.Editor.ScrollToLine(150);
        var view = tab.Editor.TextArea.TextView;
        void Layout()
        {
            _w.UpdateLayout();
            view.EnsureVisualLines();
        }
        Layout();
        Assert.True(view.IsMeasureValid && view.VisualLinesValid);

        // a second pane over the same text shows its first lines
        var pane = new TextEditor { Document = d };
        var paneWindow = new Avalonia.Controls.Window { Width = 600, Height = 300, Content = pane };
        paneWindow.Show();
        var paneView = pane.TextArea.TextView;
        paneWindow.UpdateLayout();
        paneView.EnsureVisualLines();
        Assert.True(paneView.VisualLinesValid);

        using (WeakEventBypass.Begin(d, view)) d.Insert(d.TextLength, "x"); // behind its lines: it hears nothing and has nothing to redo
        Assert.True(view.IsMeasureValid && view.VisualLinesValid);

        using (var bypass = WeakEventBypass.Begin(d, view)) // before its lines: it hears nothing but is asked once for a new measure, its lines stay
        {
            Assert.True(bypass.Active);
            d.Insert(d.GetLineByNumber(5).Offset, "y");
            d.Insert(d.GetLineByNumber(3).Offset, "y");
            Assert.True(view.IsMeasureValid);
            Assert.False(paneView.VisualLinesValid); // the other pane shows those lines and hears of them
        }
        Assert.False(view.IsMeasureValid);
        Assert.True(view.VisualLinesValid);
        paneWindow.UpdateLayout();
        paneView.EnsureVisualLines();
        Assert.Contains("y", d.GetText(paneView.VisualLines[2].FirstDocumentLine));

        Layout();
        using (WeakEventBypass.Begin(d, view)) d.Insert(view.VisualLines[2].FirstDocumentLine.Offset + 1, "z"); // in a line: the view redraws it
        Assert.False(view.VisualLinesValid);
        Layout();
        Assert.Contains("z", d.GetText(view.VisualLines[2].FirstDocumentLine));
        paneWindow.Close();
    }

    [AvaloniaFact]
    public void Typing_at_many_carets_leaves_the_carets_of_the_area_and_the_text_in_step()
    {
        var tab = Open("abc\nabc\nabc");
        PlaceCarets(tab, 1, 5, 9);
        TestApp.Type(_w, "x");
        Assert.Equal("axbc\naxbc\naxbc", Text(tab));
        Assert.Equal(["2:2", "7:7", "12:12"], Carets(tab));
        Assert.Equal(12, tab.Editor!.CaretOffset); // the primary one is the one of the text area
        Assert.Equal(1, Steps(tab));
    }
}
