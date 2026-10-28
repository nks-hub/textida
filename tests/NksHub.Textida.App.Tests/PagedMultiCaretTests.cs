using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using TextSelection = NksHub.Textida.Core.Documents.TextSelection;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// Several carets on the paged surface of a large file, through the same input events the window gets: Ctrl+D and its relatives, Ctrl+Alt+Up and Down,
/// Alt+click and Esc, typing, deleting, Enter, Tab, the clipboard and undo at every caret, the cap on the number of carets, and carets over the blocks of a mapped file.
/// Carets are printed with <c>|</c> (empty) and <c>«»</c> (selection) over the text of the document.
/// </summary>
[Collection("LocState")]
public class PagedMultiCaretTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static PagedMultiCaretTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public PagedMultiCaretTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab OpenUnindexed(byte[] content, string name)
    {
        var tab = _w.OpenFile(_temp.Bytes(name, content), null);
        Assert.True(tab.Doc.IsLarge);
        _w.UpdateLayout();
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private static void PumpUntil(Func<bool> condition, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < until)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition());
    }

    private DocTab OpenPaged(byte[] content, string name = "multi.txt")
    {
        var tab = OpenUnindexed(content, name);
        PumpUntil(() => tab.Paged!.Index.Complete);
        return tab;
    }

    private DocTab OpenPaged(string text, string name = "multi.txt") => OpenPaged(Encoding.UTF8.GetBytes(text), name);

    private static byte[] AllBytes(DocTab tab) => tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength));

    private static string Text(DocTab tab) => Encoding.UTF8.GetString(AllBytes(tab));

    private static string Marked(DocTab tab)
    {
        string text = Text(tab);
        var sb = new StringBuilder(text);
        int Char(long offset) => Encoding.UTF8.GetCharCount(tab.Doc.Doc.ReadBytes(new ByteRange(0, offset)));
        foreach (var s in tab.Paged!.AllCarets().Items.OrderByDescending(s => s.End))
        {
            if (s.IsEmpty) sb.Insert(Char(s.Caret), '|');
            else { sb.Insert(Char(s.End), '»'); sb.Insert(Char(s.Start), '«'); }
        }
        return sb.ToString();
    }

    /// <summary>Empty carets at byte offsets, the last one primary.</summary>
    private static void Place(DocTab tab, params long[] offsets) =>
        tab.Paged!.SetCarets(SelectionSet.Create([.. offsets.Select(TextSelection.At)], offsets.Length - 1));

    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    private void Mod(Key key, bool shift = false) => TestApp.Press(_w, key, TestApp.Mod | (shift ? RawInputModifiers.Shift : RawInputModifiers.None));

    private static TextInputMethodClient ImeClient(PagedTextView paged)
    {
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        paged.RaiseEvent(args);
        return Assert.IsAssignableFrom<TextInputMethodClient>(args.Client);
    }

    private static long Bytes(string text, int chars) => Encoding.UTF8.GetByteCount(text[..chars]);

    // ---- adding: occurrences, above and below ----

    [AvaloniaFact]
    public void Ctrl_D_selects_the_word_then_adds_the_next_occurrences_and_typing_replaces_all_in_one_undo_step()
    {
        var tab = OpenPaged("foo bar foo food foo\n");
        var paged = tab.Paged!;
        paged.GoToOffset(1);
        Mod(Key.D);
        Assert.Equal("«foo» bar foo food foo\n", Marked(tab));
        Assert.False(paged.IsMulti);
        Mod(Key.D);
        Mod(Key.D); // "food" is not the word
        Assert.Equal(3, paged.CaretCount);
        Assert.Equal("«foo» bar «foo» food «foo»\n", Marked(tab));

        TestApp.Type(_w, "X");
        Assert.Equal("X bar X food X\n", Text(tab));
        Assert.Equal("X| bar X| food X|\n", Marked(tab));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);

        Mod(Key.Z);
        Assert.Equal("foo bar foo food foo\n", Text(tab));
        Assert.Equal("«foo» bar «foo» food «foo»\n", Marked(tab));
        Mod(Key.Z, shift: true);
        Assert.Equal("X bar X food X\n", Text(tab));
        Assert.Equal(3, paged.CaretCount);
    }

    [AvaloniaFact]
    public void Ctrl_K_Ctrl_D_skips_the_last_selection_and_Ctrl_Shift_L_selects_every_occurrence()
    {
        var tab = OpenPaged("ab ab ab ab\n");
        var paged = tab.Paged!;
        paged.SelectBytes(0, 2);
        Mod(Key.D);
        Mod(Key.K);
        Mod(Key.D);
        Assert.Equal("«ab» ab «ab» ab\n", Marked(tab));
        Mod(Key.L, shift: true);
        Assert.Equal("«ab» «ab» «ab» «ab»\n", Marked(tab));
        Assert.Equal(4, paged.CaretCount);
        Assert.Equal(2, paged.AllCarets().Primary); // the occurrence that was primary stays primary
    }

    [AvaloniaFact]
    public void The_commands_work_by_menu_name_and_the_status_bar_counts_the_carets()
    {
        var tab = OpenPaged("ab ab ab\n");
        var paged = tab.Paged!;
        paged.SelectBytes(0, 2);
        _ = _app.Workbench.Commands.ExecuteAsync("nks.edit.selectAllOccurrences", new ShellContext(_w, forMenu: false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, paged.CaretCount);
        Assert.Contains(Loc.Plural(K.StatusCarets, 3), _w.StatusLeftText);
        _ = _app.Workbench.Commands.ExecuteAsync("nks.edit.clearCarets", new ShellContext(_w, forMenu: false));
        Dispatcher.UIThread.RunJobs();
        Assert.False(paged.IsMulti);
        Assert.DoesNotContain(Loc.Plural(K.StatusCarets, 3), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void Ctrl_Alt_Down_and_Up_add_carets_at_the_same_display_column_across_short_lines()
    {
        var tab = OpenPaged("abcd\nab\nabcd\n");
        tab.Paged!.GoToOffset(3);
        Press(Key.Down, TestApp.Mod | RawInputModifiers.Alt);
        Press(Key.Down, TestApp.Mod | RawInputModifiers.Alt);
        Assert.Equal("abc|d\nab|\nabc|d\n", Marked(tab));
        TestApp.Type(_w, "X");
        Assert.Equal("abcXd\nabX\nabcXd\n", Text(tab));

        var up = OpenPaged("abcd\nab\nabcd\n", "up.txt");
        up.Paged!.GoToOffset(11);
        Press(Key.Up, TestApp.Mod | RawInputModifiers.Alt);
        Press(Key.Up, TestApp.Mod | RawInputModifiers.Alt);
        Assert.Equal("abc|d\nab|\nabc|d\n", Marked(up));
    }

    [AvaloniaFact]
    public void Escape_removes_the_extra_carets_and_keeps_the_primary_one()
    {
        var tab = OpenPaged("a b c\n");
        Place(tab, 0, 2, 4);
        Assert.True(tab.Paged!.IsMulti);
        Press(Key.Escape);
        Assert.False(tab.Paged.IsMulti);
        Assert.Equal("a b |c\n", Marked(tab));
    }

    [AvaloniaFact]
    public void Adding_carets_stops_at_the_limit_and_the_status_bar_says_so()
    {
        var tab = OpenPaged(string.Concat(Enumerable.Repeat("ab ", 50)) + "\n");
        var paged = tab.Paged!;
        paged.CaretLimit = 10;
        paged.SelectBytes(0, 2);
        Mod(Key.L, shift: true);
        Assert.Equal(10, paged.CaretCount);
        Assert.Contains(Loc.Get(K.PagedCaretsLimit, ("limit", 10)), _w.StatusLeftText);
        var carets = paged.AllCarets().Items.ToList();

        _w.SetStatusMessage("");
        Mod(Key.D); // the eleventh caret
        Assert.Equal(10, paged.CaretCount);
        Assert.Equal(carets, paged.AllCarets().Items);
        Assert.Contains(Loc.Get(K.PagedCaretsLimit, ("limit", 10)), _w.StatusLeftText);

        paged.CaretLimit = 2;
        Place(tab, 0, 10);
        _w.SetStatusMessage("");
        Press(Key.Down, TestApp.Mod | RawInputModifiers.Alt); // a caret on the empty last line would be the third
        Assert.Equal(2, paged.CaretCount);
        Assert.Contains(Loc.Get(K.PagedCaretsLimit, ("limit", 2)), _w.StatusLeftText);
    }

    // ---- moving ----

    [AvaloniaFact]
    public void Arrows_Home_End_and_Shift_move_and_extend_every_caret_and_carets_that_meet_become_one()
    {
        var tab = OpenPaged("ab cd\nef gh\n");
        var paged = tab.Paged!;
        Place(tab, 1, 7);
        Assert.Equal("a|b cd\ne|f gh\n", Marked(tab));
        Press(Key.Right);
        Assert.Equal("ab| cd\nef| gh\n", Marked(tab));
        Press(Key.End);
        Assert.Equal("ab cd|\nef gh|\n", Marked(tab));
        Press(Key.Home);
        Assert.Equal("|ab cd\n|ef gh\n", Marked(tab));
        Press(Key.Right, RawInputModifiers.Shift);
        Press(Key.Right, RawInputModifiers.Shift);
        Assert.Equal("«ab» cd\n«ef» gh\n", Marked(tab));
        Press(Key.Left); // a selection collapses to its start
        Assert.Equal("|ab cd\n|ef gh\n", Marked(tab));

        Press(Key.Right);
        Press(Key.Right);
        Press(Key.Down); // the second caret goes to the empty last line, the first keeps column 2
        Assert.Equal("ab cd\nef| gh\n|", Marked(tab));
        Press(Key.Down);
        Assert.False(paged.IsMulti);
        Assert.Equal(12, paged.CaretOffset);
    }

    [AvaloniaFact]
    public void The_page_keys_and_a_jump_end_the_several_carets()
    {
        var tab = OpenPaged(string.Concat(Enumerable.Range(0, 300).Select(i => $"row {i}\n")));
        Place(tab, 0, 14, 21);
        Press(Key.PageDown);
        Assert.False(tab.Paged!.IsMulti);
        Place(tab, 0, 14);
        tab.Paged.GoToLine(5);
        Assert.False(tab.Paged.IsMulti);
    }

    [AvaloniaFact]
    public void Alt_click_adds_a_caret_where_it_lands_and_a_second_click_removes_it()
    {
        var tab = OpenPaged("one\ntwo\nthree\n");
        var paged = tab.Paged!;
        _w.CaptureRenderedFrame();
        paged.GoToOffset(7); // the end of "two"
        var client = ImeClient(paged);
        var caret = client.CursorRectangle;
        var point = client.TextViewVisual.TranslatePoint(new Point(caret.X + 0.2, caret.Y + caret.Height / 2), _w)!.Value;
        paged.GoToOffset(0);

        _w.MouseDown(point, MouseButton.Left, RawInputModifiers.Alt);
        _w.MouseUp(point, MouseButton.Left, RawInputModifiers.Alt);
        Assert.Equal("|one\ntwo|\nthree\n", Marked(tab));
        Assert.Equal(7, paged.CaretOffset); // the new caret is primary

        _w.MouseDown(point, MouseButton.Left, RawInputModifiers.Alt);
        _w.MouseUp(point, MouseButton.Left, RawInputModifiers.Alt);
        Assert.Equal("|one\ntwo\nthree\n", Marked(tab));
        Assert.False(paged.IsMulti);
    }

    // ---- editing ----

    [AvaloniaFact]
    public void Typing_Backspace_and_Delete_at_every_caret_keep_multibyte_text_and_CRLF_exact_and_one_undo_step_each_run()
    {
        string text = "žluť\r\n日本語\r\n😀a\r\nz\r\n";
        var tab = OpenPaged(text);
        string[] lines = ["žluť\r\n", "日本語\r\n", "😀a\r\n", "z\r\n"];
        Place(tab, 0, Bytes(text, 6), Bytes(text, 11), Bytes(text, 16));

        TestApp.Type(_w, "é");
        TestApp.Type(_w, "ů");
        string typed = string.Concat(lines.Select(l => "éů" + l));
        Assert.Equal(typed, Text(tab));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth); // two characters in a row are one run

        Press(Key.Back);
        Assert.Equal(string.Concat(lines.Select(l => "é" + l)), Text(tab));
        Assert.Equal(2, tab.Doc.Doc.UndoDepth);
        Press(Key.Delete); // the character behind the caret, a surrogate pair in one of the lines
        Assert.Equal(string.Concat(lines.Select(l => "é" + l[(char.IsSurrogate(l[0]) ? 2 : 1)..])), Text(tab));
        Assert.Equal(2, tab.Doc.Doc.UndoDepth); // Backspace and Delete in a row are one run

        Mod(Key.Z);
        Assert.Equal(typed, Text(tab));
        Mod(Key.Z);
        Assert.Equal(text, Text(tab));
        Assert.Equal(4, tab.Paged!.CaretCount);
        Assert.Equal(Encoding.UTF8.GetBytes(text), AllBytes(tab));
    }

    [AvaloniaFact]
    public void Delete_and_Backspace_remove_a_whole_CRLF_and_carets_that_meet_become_one()
    {
        var tab = OpenPaged("ab\r\ncd\r\nef\r\n");
        Place(tab, 2, 6, 10);
        Press(Key.Delete);
        Assert.Equal("abcdef", Text(tab));
        Assert.Equal("ab|cd|ef|", Marked(tab));
        Press(Key.Back);
        Assert.Equal("ace", Text(tab));
        Mod(Key.Z);
        Assert.Equal("ab\r\ncd\r\nef\r\n", Text(tab)); // Delete and Backspace in a row are one run

        Place(tab, 6, 8); // the end of "cd" and the start of "ef": Backspace removes the d and the line break, and the carets meet
        Press(Key.Back);
        Assert.Equal("ab\r\ncef\r\n", Text(tab));
        Assert.False(tab.Paged!.IsMulti);
        Assert.Equal(5, tab.Paged.CaretOffset);
    }

    [AvaloniaFact]
    public void Enter_and_Tab_act_at_every_caret()
    {
        var tab = OpenPaged("ab cd\nxyz\n");
        string nl = tab.Doc.NewLine(_w.Options.ConfiguredEol);
        Place(tab, 2, 9);
        Press(Key.Enter);
        Assert.Equal("ab" + nl + " cd\nxyz" + nl + "\n", Text(tab));
        Mod(Key.Z);
        Assert.Equal("ab cd\nxyz\n", Text(tab));

        var options = _w.Options;
        int size = Math.Max(1, options.InsertSpaces ? options.IndentSize : options.TabSize);
        string Pad(int column) => options.InsertSpaces ? new string(' ', size - column % size) : "\t";
        Place(tab, 2, 9); // columns 2 and 3
        Press(Key.Tab);
        Assert.Equal("ab" + Pad(2) + " cd\nxyz" + Pad(3) + "\n", Text(tab));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth); // the Enter step was undone, the Tab step stands
        Mod(Key.Z);
        Assert.Equal("ab cd\nxyz\n", Text(tab));
    }

    [AvaloniaFact]
    public void Paste_gives_one_clipboard_line_per_caret_when_the_counts_match_else_the_whole_text_to_every_caret()
    {
        var tab = OpenPaged("a b c\n");
        var paged = tab.Paged!;
        Place(tab, 1, 3, 5);
        Assert.True(paged.PasteText("1\n2\n3"));
        Assert.Equal("a1 b2 c3\n", Text(tab));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        Mod(Key.Z);
        Assert.Equal("a b c\n", Text(tab));

        Place(tab, 1, 3, 5);
        Assert.True(paged.PasteText("zz\nyy")); // two lines, three carets: the whole text goes to every one
        string nl = tab.Doc.NewLine(_w.Options.ConfiguredEol);
        Assert.Equal($"azz{nl}yy bzz{nl}yy czz{nl}yy\n", Text(tab));
        Mod(Key.Z);
        Assert.Equal("a b c\n", Text(tab));
    }

    [AvaloniaFact]
    public void A_paste_over_the_limit_and_a_locked_file_leave_the_document_unchanged()
    {
        var tab = OpenPaged("a b c\n");
        var paged = tab.Paged!;
        paged.MultiPasteLimitBytes = 8;
        Place(tab, 1, 3, 5);
        Assert.False(paged.PasteText("0123456789"));
        Assert.Equal("a b c\n", Text(tab));
        Assert.Contains(Loc.Get(K.PagedMultiPasteTooLarge, ("size", OpenDocument.FormatSize(30)), ("limit", OpenDocument.FormatSize(8))), _w.StatusLeftText);

        paged.ReadOnlyReason = "locked by another program";
        Assert.False(paged.PasteText("x"));
        Assert.Equal("a b c\n", Text(tab));
        Assert.Contains("locked by another program", _w.StatusLeftText);
    }

    [AvaloniaFact]
    public async Task Copy_and_cut_take_the_selected_texts_of_all_carets_one_per_line()
    {
        var tab = OpenPaged("one two three\n");
        var paged = tab.Paged!;
        string nl = tab.Doc.NewLine(_w.Options.ConfiguredEol);
        paged.SetCarets(SelectionSet.Create([new TextSelection(0, 3), new TextSelection(4, 7), TextSelection.At(13)], 2));

        await paged.CopyAsync(cut: false);
        Assert.Equal("one" + nl + "two", await _w.Clipboard!.TryGetTextAsync());
        Assert.Equal("one two three\n", Text(tab));

        await paged.CopyAsync(cut: true);
        Assert.Equal("one" + nl + "two", await _w.Clipboard!.TryGetTextAsync());
        Assert.Equal("  three\n", Text(tab));
        Assert.Equal("| | three|\n", Marked(tab)); // the carets without a selection stay
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        Mod(Key.Z);
        Assert.Equal("one two three\n", Text(tab));
        Assert.Equal("«one» «two» three|\n", Marked(tab));
    }

    [AvaloniaFact]
    public void A_long_selection_is_refused_for_the_search_of_its_occurrences()
    {
        var tab = OpenPaged("abcd abcd\n");
        var paged = tab.Paged!;
        paged.OccurrenceLimitBytes = 3;
        paged.SelectBytes(0, 4);
        Mod(Key.D);
        Assert.False(paged.IsMulti);
        Assert.Contains(Loc.Get(K.PagedOccurrenceTooLong, ("limit", OpenDocument.FormatSize(3))), _w.StatusLeftText);
    }

    // ---- the input method ----

    [AvaloniaFact]
    public void A_composition_is_drawn_at_every_caret_and_its_commit_is_written_at_all_of_them_as_one_undo_step()
    {
        var tab = OpenPaged("a b\nc d\n");
        var paged = tab.Paged!;
        Place(tab, 1, 5, 7);
        var client = ImeClient(paged);
        client.SetPreeditText("に");
        _w.CaptureRenderedFrame(); // draws the composition at three carets in two lines
        Assert.Equal("a b\nc d\n", Text(tab)); // composing changes nothing
        Assert.Equal(0, tab.Doc.Doc.UndoDepth);

        TestApp.Type(_w, "日");
        Assert.Equal("a日 b\nc日 d日\n", Text(tab));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        Mod(Key.Z);
        Assert.Equal("a b\nc d\n", Text(tab));
    }

    // ---- drawing ----

    [AvaloniaFact]
    public void Only_the_carets_on_screen_are_drawn()
    {
        var tab = OpenPaged(string.Concat(Enumerable.Range(1, 5000).Select(i => $"line {i}\n")));
        var paged = tab.Paged!;
        paged.CaretLimit = 5000;
        paged.SelectBytes(0, 4);
        Mod(Key.L, shift: true);
        Assert.Equal(5000, paged.CaretCount);
        _w.CaptureRenderedFrame();
        Assert.InRange(paged.DrawnCarets, 1, paged.PageLines + 3);
    }

    // ---- carets that follow the document ----

    [AvaloniaFact]
    public void Carets_follow_a_change_from_another_source_and_undo_of_it()
    {
        var tab = OpenPaged("ab ab ab\n");
        var paged = tab.Paged!;
        paged.SelectBytes(0, 2);
        Mod(Key.L, shift: true);
        Assert.Equal("«ab» «ab» «ab»\n", Marked(tab));

        tab.Doc.Doc.Insert(0, "ZZ"u8.ToArray()); // not through the view
        Assert.Equal("ZZ«ab» «ab» «ab»\n", Marked(tab));
        Assert.Equal(3, paged.CaretCount);
        tab.Doc.Doc.Delete(new ByteRange(0, 2));
        Assert.Equal("«ab» «ab» «ab»\n", Marked(tab));

        TestApp.Type(_w, "Q");
        Assert.Equal("Q Q Q\n", Text(tab));
        Mod(Key.Z);
        Assert.Equal("«ab» «ab» «ab»\n", Marked(tab));
    }

    [AvaloniaFact]
    public void Carets_set_while_the_line_index_is_built_stay_valid_and_edit_afterwards()
    {
        string rows = string.Concat(Enumerable.Range(0, 120_000).Select(i => $"row {i} é\n"));
        var tab = OpenUnindexed(Encoding.UTF8.GetBytes(rows), "growing.txt");
        var paged = tab.Paged!;
        long end = tab.Doc.Doc.ByteLength;
        long middle = Encoding.UTF8.GetByteCount(string.Concat(Enumerable.Range(0, 60_000).Select(i => $"row {i} é\n"))); // the start of a line
        paged.SetCarets(SelectionSet.Create([TextSelection.At(4), TextSelection.At(middle), TextSelection.At(end)], 2));
        var carets = paged.AllCarets().Items.ToList();
        PumpUntil(() => paged.Index.Complete, seconds: 60);
        Assert.Equal(carets, paged.AllCarets().Items);

        TestApp.Type(_w, "Ž");
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        var bytes = AllBytes(tab);
        Assert.Equal(end + 6, bytes.Length);
        Assert.Equal("row Ž0 é\n", Encoding.UTF8.GetString(bytes[..11]));
        Assert.Equal((byte)0xC5, bytes[^2]); // the last Ž, two bytes, at the end of the file
        Mod(Key.Z);
        Assert.Equal(end, tab.Doc.Doc.ByteLength);
    }

    // ---- over the blocks of a mapped file ----

    [AvaloniaFact]
    public void Select_all_occurrences_over_the_blocks_of_a_mapped_file_edits_exactly_those_bytes()
    {
        const int Line = 81;
        byte[] data = new byte[2_400_000 / Line * Line];
        for (int i = 0; i < data.Length; i += Line)
        {
            Array.Fill(data, (byte)'.', i, Line - 2);
            data[i + Line - 2] = (byte)'\r';
            data[i + Line - 1] = (byte)'\n';
        }
        var needle = "NEEDLE"u8.ToArray();
        var at = new List<long>();
        foreach (long block in new[] { 128 * 1024L, 1024 * 1024L, 2 * 1024 * 1024L })
        {
            long p = block - 3; // the match crosses the boundary of the block
            while (p % Line is < 8 or > 60) p++;
            needle.CopyTo(data, p);
            at.Add(p);
        }
        var tab = OpenPaged(data);
        var paged = tab.Paged!;
        paged.SelectBytes(at[0], at[0] + needle.Length);
        Mod(Key.L, shift: true);
        Assert.Equal(3, paged.CaretCount);
        Assert.Equal(at, paged.AllCarets().Items.Select(s => s.Start));

        TestApp.Type(_w, "ž");
        byte[] expected = [.. data];
        var list = expected.ToList();
        foreach (long p in Enumerable.Reverse(at))
        {
            list.RemoveRange((int)p, needle.Length);
            list.InsertRange((int)p, "ž"u8.ToArray());
        }
        Assert.True(list.ToArray().AsSpan().SequenceEqual(AllBytes(tab)), "the document differs from the bytes with the three matches replaced");
        Mod(Key.Z);
        Assert.True(data.AsSpan().SequenceEqual(AllBytes(tab)));
    }

    // ---- a file over 2 GiB ----

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public void Carets_at_the_end_of_a_file_over_2_GiB_type_and_undo()
    {
        string path = Path.Combine(_temp.Path, "huge.txt");
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace < 3L << 30) return;
        byte[] unit = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, 100_000).Select(i => $"line {i}\n")));
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
            for (long length = 0; length < (2L << 30) + (64 << 20); length += unit.Length) fs.Write(unit);
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        tab.Paged!.Focus();
        PumpUntil(() => tab.Paged!.Index.Complete, seconds: 900);
        var doc = tab.Doc.Doc;
        long length0 = doc.ByteLength;
        Assert.True(length0 > int.MaxValue);

        var paged = tab.Paged;
        paged.GoToOffset(length0);
        Press(Key.Up, TestApp.Mod | RawInputModifiers.Alt);
        Press(Key.Up, TestApp.Mod | RawInputModifiers.Alt);
        Assert.Equal(3, paged.CaretCount);
        TestApp.Type(_w, "Ž");
        Assert.Equal(length0 + 6, doc.ByteLength);
        byte[] tail = doc.ReadBytes(new ByteRange(length0 - 40, length0 + 6));
        Assert.Contains("Ž", Encoding.UTF8.GetString(tail));
        Mod(Key.Z);
        Assert.Equal(length0, doc.ByteLength);
        Assert.Equal(3, paged.CaretCount);
    }
}
