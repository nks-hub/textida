using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Workbench.Commands;
using AdapterCarets = NksHub.Textida.AvaloniaEditAdapter.Carets;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P4.5.1, P4.5.3 and P4.5.4 through the command registry of the running window: clipboard history, special paste, insert into lines, sort,
/// line ranges, alignment, wrapping, date, GUID and repeat. Every edit also checks the document bytes, one undo step, the line endings of the
/// document, a read-only editor and the paged surface.
/// </summary>
[Collection("LocState")]
public class LineCommandTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static LineCommandTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public LineCommandTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
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

    private static int Steps(DocTab t) => t.Doc.Doc.UndoDepth - 1;

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    private static string Bytes(DocTab t) => Encoding.UTF8.GetString(t.Doc.Doc.ReadBytes(new ByteRange(0, t.Doc.Doc.ByteLength)));

    private static void PlaceCarets(DocTab t, params int[] offsets) =>
        t.Editor!.SetCarets(AdapterCarets.Create([.. offsets.Select(Sel.At)], offsets.Length - 1));

    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    private void Mod(Key key) => TestApp.Press(_w, key, TestApp.Mod);

    private async Task Eventually(Func<bool> done)
    {
        for (int i = 0; i < 80 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        Assert.True(done());
    }

    /// <summary>Runs a text command and proves it was one undo step that gives back the original text and bytes.</summary>
    private void AssertOneStep(DocTab tab, string id, string expected, Action? before = null)
    {
        string original = Text(tab);
        int steps = Steps(tab);
        before?.Invoke();
        Assert.Equal(CommandOutcome.Handled, Run(id));
        Assert.Equal(expected, Text(tab));
        Assert.Equal(expected, Bytes(tab));
        Assert.Equal(steps + 1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal(original, Text(tab));
        Assert.Equal(original, Bytes(tab));
        Mod(Key.Y);
        Assert.Equal(expected, Text(tab));
    }

    // ---- P4.5.1 clipboard history ----

    [AvaloniaFact]
    public void The_history_lists_the_newest_first_moves_a_repeated_text_up_and_forgets_the_oldest()
    {
        int size = 3;
        var history = new ClipboardHistory(() => size);
        foreach (var t in new[] { "a", "b", "c", "a", "d" }) history.Add(t);
        Assert.Equal(["d", "a", "c"], history.Items);
        history.Add("");
        history.Add(null);
        history.Add(new string('x', ClipboardHistory.MaxItemLength + 1));
        Assert.Equal(["d", "a", "c"], history.Items);
        size = 1;
        history.Add("e");
        Assert.Equal(["e"], history.Items);
    }

    [AvaloniaFact]
    public void After_five_copies_the_list_shows_five_items_and_the_chosen_one_goes_into_every_caret_as_one_step()
    {
        var tab = Open("one two three four five\n.\n.\n.");
        foreach (var (start, length) in new[] { (0, 3), (4, 3), (8, 5), (14, 4), (19, 4) })
        {
            tab.Editor!.Select(start, length);
            Mod(Key.C);
        }
        Assert.Equal(["five", "four", "three", "two", "one"], _app.ClipboardHistory.Items);

        PlaceCarets(tab, 25, 27, 29);
        int steps = Steps(tab);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.clipboardHistory"));
        Assert.True(_w.IsPaletteOpen);
        Assert.Equal(PaletteMode.Choice, _w.PaletteModeNow);
        Assert.Equal(["five", "four", "three", "two", "one"], _w.QuickResults.Select(r => r.Title));
        _w.SetPaletteQuery("three");
        Assert.Equal("three", _w.PalettePreviewText);
        _w.RunPaletteSelection();

        Assert.Equal("one two three four five\n.three\n.three\n.three", Text(tab));
        Assert.Equal(Text(tab), Bytes(tab));
        Assert.Equal(3, tab.Editor!.CaretCount);
        Assert.Equal(steps + 1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("one two three four five\n.\n.\n.", Text(tab));
    }

    [AvaloniaFact]
    public void A_text_with_as_many_lines_as_carets_is_split_between_them_and_the_list_previews_all_lines()
    {
        var tab = Open("a\nb");
        _app.ClipboardHistory.Add("1\n2");
        PlaceCarets(tab, 1, 3);
        Run("nks.edit.clipboardHistory");
        Assert.Equal("1 2", _w.QuickResults[0].Title);
        Assert.Equal("1\n2", _w.PalettePreviewText);
        _w.RunPaletteSelection();
        Assert.Equal("a1\nb2", Text(tab));
    }

    [AvaloniaFact]
    public void An_empty_history_says_so_and_a_read_only_editor_refuses_the_paste()
    {
        var tab = Open("text");
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.clipboardHistory"));
        Assert.Empty(_w.QuickResults);
        Assert.Contains(Loc.Get(K.ClipEmpty), _w.PaletteEmptyText);
        _w.ClosePalette();

        _app.ClipboardHistory.Add("x");
        tab.Editor!.IsReadOnly = true;
        Assert.Equal(CommandOutcome.NotHandled, Run("nks.edit.clipboardHistory"));
        Assert.False(_w.IsPaletteOpen);
        Assert.Equal("text", Text(tab));
    }

    [AvaloniaFact]
    public async Task Cut_and_copy_in_the_paged_view_fill_the_history_and_the_list_pastes_into_it()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var rows = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, 150_000).Select(n => $"line {n}\n")));
        var tab = _w.OpenFile(_temp.Bytes("big.log", rows), null);
        Assert.True(tab.Doc.IsLarge);
        for (int n = 0; n < 400 && !tab.Paged!.Index.Complete; n++) { Thread.Sleep(25); Dispatcher.UIThread.RunJobs(); }
        tab.Paged!.Focus();
        TestApp.Press(_w, Key.End, RawInputModifiers.Shift);
        await tab.Paged.CopyAsync(cut: false);
        Assert.Equal(["line 1"], _app.ClipboardHistory.Items);
        TestApp.Press(_w, Key.Home);

        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.clipboardHistory"));
        _w.RunPaletteSelection();
        Assert.Equal("line 1line 1\nline 2", Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, 19))));
        Assert.Equal(CommandOutcome.Disabled, Run("nks.edit.sortLines"));
    }

    // ---- P4.5.3 special paste ----

    [AvaloniaFact]
    public async Task Paste_without_moving_the_caret_leaves_each_caret_in_front_of_its_text()
    {
        var tab = Open("ab\ncd");
        await _w.Clipboard!.SetTextAsync("X");
        tab.Editor!.CaretOffset = 1;
        int steps = Steps(tab);
        Run("nks.edit.pasteKeepCaret");
        await Eventually(() => Text(tab) == "aXb\ncd");
        Assert.Equal(["1:1"], tab.Editor.GetSelections().Select(s => $"{s.Anchor}:{s.Caret}"));
        Assert.Equal(steps + 1, Steps(tab));

        await _w.Clipboard!.SetTextAsync("P\nQ");
        PlaceCarets(tab, 0, 5);
        Run("nks.edit.pasteKeepCaret");
        await Eventually(() => Text(tab) == "PaXb\ncQd");
        Assert.Equal(["0:0", "6:6"], tab.Editor.GetSelections().Select(s => $"{s.Anchor}:{s.Caret}"));
        Mod(Key.Z);
        Assert.Equal("aXb\ncd", Text(tab));
    }

    [AvaloniaFact]
    public async Task Paste_without_moving_the_caret_replaces_a_selection_and_uses_the_line_ending_of_the_document()
    {
        var tab = Open("a\r\nb\r\nc");
        Assert.Equal("a\r\nb\r\nc", Text(tab));
        await _w.Clipboard!.SetTextAsync("1\n2");
        tab.Editor!.Select(3, 1);
        Run("nks.edit.pasteKeepCaret");
        await Eventually(() => Text(tab) == "a\r\n1\r\n2\r\nc");
        Assert.Equal("a\r\n1\r\n2\r\nc", Bytes(tab));
        Assert.Equal(["3:3"], tab.Editor.GetSelections().Select(s => $"{s.Anchor}:{s.Caret}"));
    }

    // ---- P4.5.3 paste as HTML ----

    private async Task PutOnClipboardAsync(string text, byte[] html)
    {
        var item = DataTransferItem.CreateText(text);
        item.Set(ClipboardHtml.Format, html);
        var data = new DataTransfer();
        data.Add(item);
        await _w.Clipboard!.SetDataAsync(data);
    }

    [AvaloniaFact]
    public async Task Paste_as_html_puts_the_html_of_the_clipboard_into_every_caret_as_one_step()
    {
        var tab = Open("a\r\nb");
        await PutOnClipboardAsync("bold é", ClipboardHtml.CfHtml("<b>bold é</b>"));
        PlaceCarets(tab, 1, 4);
        int steps = Steps(tab);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.pasteAsHtml"));
        await Eventually(() => Text(tab) == "a<b>bold é</b>\r\nb<b>bold é</b>");
        Assert.Equal(Text(tab), Bytes(tab));
        Assert.Equal(steps + 1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("a\r\nb", Text(tab));
        Assert.Equal("a\r\nb", Bytes(tab));
    }

    [AvaloniaFact]
    public async Task Paste_as_html_without_html_on_the_clipboard_says_so_and_a_read_only_editor_refuses_it()
    {
        var tab = Open("a");
        await _w.Clipboard!.SetTextAsync("plain");
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.pasteAsHtml"));
        await Eventually(() => _w.StatusLeftText.Contains(Loc.Get(K.LinesPasteHtmlNone)));
        Assert.Equal("a", Text(tab));

        tab.Editor!.IsReadOnly = true;
        Assert.Equal(CommandOutcome.NotHandled, Run("nks.edit.pasteAsHtml"));
        Assert.Equal(CommandOutcome.NotHandled, Run("nks.edit.insertFromFile"));
        Assert.Equal(CommandOutcome.NotHandled, Run("nks.edit.reformatShortLines"));
    }

    [AvaloniaFact]
    public void The_html_of_the_clipboard_is_the_fragment_its_header_marks_or_else_the_whole_text()
    {
        Assert.Equal("<b>é</b>", ClipboardHtml.Decode(ClipboardHtml.CfHtml("<b>é</b>")));
        Assert.Equal("<p>x</p>", ClipboardHtml.Decode([.. Encoding.UTF8.GetBytes("<p>x</p>"), 0, 0]));
        string html = "<html><body>y</body></html>";
        static string Header(int start, int end) => $"Version:0.9\r\nStartHTML:{start:D10}\r\nEndHTML:{end:D10}\r\nStartFragment:-1\r\nEndFragment:-1\r\n";
        int start = Header(0, 0).Length;
        Assert.Equal(html, ClipboardHtml.Decode(Encoding.UTF8.GetBytes(Header(start, start + html.Length) + html)));
    }

    // ---- P4.5.3 insert from file ----

    [AvaloniaFact]
    public async Task Insert_from_file_puts_the_decoded_text_into_every_caret_with_the_line_endings_of_the_document()
    {
        var tab = Open("a\r\nb");
        _dialogs.Picked.Add(_temp.Bytes("part.txt", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("ž1\n2")]));
        PlaceCarets(tab, 1, 4);
        int steps = Steps(tab);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.insertFromFile"));
        await Eventually(() => Text(tab) != "a\r\nb");
        Assert.Equal("až1\r\n2\r\nbž1\r\n2", Text(tab));
        Assert.Equal(Text(tab), Bytes(tab));
        Assert.Equal(steps + 1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("a\r\nb", Text(tab));
        Assert.Equal("a\r\nb", Bytes(tab));
    }

    [AvaloniaFact]
    public async Task Insert_from_file_refuses_a_file_over_the_limit_and_does_nothing_when_no_file_is_picked()
    {
        var tab = Open("a");
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.insertFromFile"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("a", Text(tab));

        string big = _temp.Bytes("big.bin", []);
        using (var stream = File.OpenWrite(big)) stream.SetLength(PluginLimitsPlaceholders.InsertFromFileMaxBytes + 1);
        _dialogs.Picked.Add(big);
        Run("nks.edit.insertFromFile");
        await Eventually(() => _w.StatusLeftText.Contains("big.bin"));
        Assert.Equal("a", Text(tab));
    }

    // ---- P4.5.3 insert into lines ----

    [AvaloniaFact]
    public void Insert_into_lines_writes_into_the_lines_of_the_selection_keeping_crlf_and_shows_a_preview()
    {
        var tab = Open("a\r\nb\r\nc\r\nd");
        _dialogs.InsertOptions.Enqueue(new LineInsertOptions(LineInsertMode.Numbering, Start: 1, Width: 2, Separator: ". "));
        AssertOneStep(tab, "nks.edit.insertIntoLines", "a\r\n01. b\r\n02. c\r\nd", () => tab.Editor!.Select(3, 4));
        Assert.Equal("01. b\r\n02. c", _dialogs.LinePreviews.Single());
    }

    [AvaloniaFact]
    public void Numbering_puts_a_number_into_every_caret_as_one_step()
    {
        var tab = Open("a\nb\nc");
        _dialogs.InsertOptions.Enqueue(new LineInsertOptions(LineInsertMode.Numbering, Place: LineInsertPlace.Selections, Start: 1));
        AssertOneStep(tab, "nks.edit.insertIntoLines", "1a\n2b\n3c", () => PlaceCarets(tab, 0, 2, 4));
    }

    [AvaloniaFact]
    public void Insert_into_lines_does_nothing_when_the_dialog_is_cancelled_and_refuses_a_read_only_editor_and_the_paged_view()
    {
        var tab = Open("a\nb");
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.insertIntoLines"));
        Assert.Equal("a\nb", Text(tab));
        Assert.Equal(1, _dialogs.LineDialogsAsked);

        _dialogs.InsertOptions.Enqueue(new LineInsertOptions(Text: "x"));
        tab.Editor!.IsReadOnly = true;
        foreach (var id in new[] { "nks.edit.insertIntoLines", "nks.edit.sortLines", "nks.edit.insertGuid", "nks.edit.insertDateTime", "nks.edit.justifyRight", "nks.edit.reformatParagraph" })
            Assert.Equal(CommandOutcome.NotHandled, Run(id));
        Assert.Equal("a\nb", Text(tab));
        Assert.Equal(1, _dialogs.LineDialogsAsked);
    }

    // ---- P4.5.3 sort ----

    [AvaloniaFact]
    public void Sort_previews_then_sorts_the_selected_lines_as_one_step_and_keeps_crlf()
    {
        var tab = Open("x\r\nc\r\nB\r\na\r\ny");
        _dialogs.SortOptionsAnswers.Enqueue(new SortOptions(RemoveDuplicates: true));
        AssertOneStep(tab, "nks.edit.sortLines", "x\r\na\r\nB\r\nc\r\ny", () => tab.Editor!.Select(3, 7));
        Assert.Equal("a\nB\nc", _dialogs.LinePreviews[0].Replace("\r", ""));
    }

    [AvaloniaFact]
    public void Sort_without_a_selection_sorts_all_lines_descending()
    {
        var tab = Open("b\nc\na");
        _dialogs.SortOptionsAnswers.Enqueue(new SortOptions(Descending: true));
        AssertOneStep(tab, "nks.edit.sortLines", "c\nb\na");
    }

    // ---- P4.5.3 select lines by range ----

    [AvaloniaFact]
    public void Lines_10_to_20_and_40_to_50_make_two_selections_and_the_dialog_previews_the_count()
    {
        var tab = Open(string.Concat(Enumerable.Range(1, 60).Select(i => $"line {i}\n")));
        _dialogs.Prompts.Enqueue("10-20, 40-50");
        Assert.Equal(CommandOutcome.Handled, Run("nks.search.selectLinesByRange"));
        var d = tab.Editor!.Document;
        Assert.Equal(2, tab.Editor.CaretCount);
        var sels = tab.Editor.GetSelections();
        Assert.Equal((d.GetLineByNumber(10).Offset, d.GetLineByNumber(21).Offset), (sels[0].Start, sels[0].End));
        Assert.Equal((d.GetLineByNumber(40).Offset, d.GetLineByNumber(51).Offset), (sels[1].Start, sels[1].End));
        Assert.Equal(Loc.Get(K.LinesRangesResult, ("selections", 2), ("lines", 22)), _dialogs.Previews.Single());
    }

    [AvaloniaFact]
    public void A_wrong_list_of_lines_says_so_and_selects_nothing()
    {
        var tab = Open("a\nb");
        _dialogs.Prompts.Enqueue("1-x");
        Run("nks.search.selectLinesByRange");
        Assert.Contains(Loc.Get(K.LinesRangesInvalid), _w.StatusLeftText);
        Assert.False(tab.Editor!.IsMulti);
    }

    // ---- P4.5.3 align and wrap ----

    [AvaloniaFact]
    public void Align_commands_work_on_the_selected_lines_in_one_step()
    {
        var tab = Open("x\na\n  ccc\nbb\ny");
        AssertOneStep(tab, "nks.edit.justifyRight", "x\n  a\nccc\n bb\ny", () => tab.Editor!.Select(2, 11));
        Mod(Key.Z);
        AssertOneStep(tab, "nks.edit.justifyCenter", "x\n a\nccc\nbb\ny", () => tab.Editor!.Select(2, 11));
    }

    [AvaloniaFact]
    public void Reformat_wraps_at_the_right_margin_and_tells_where_to_set_it_when_there_is_none()
    {
        var tab = Open("aaa bbb ccc ddd\neee\n\nzz");
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.reformatParagraph"));
        Assert.Equal("aaa bbb ccc ddd\neee\n\nzz", Text(tab));
        Assert.Contains(Loc.Get(K.LinesReformatNoMargin), _w.StatusLeftText);

        _app.Workbench.Set("editor.rightMargin", JsonValue.Create(10));
        AssertOneStep(tab, "nks.edit.reformatParagraph", "aaa bbb\nccc ddd\neee\n\nzz", () => tab.Editor!.CaretOffset = 1);
    }

    [AvaloniaFact]
    public void Reformat_ending_at_a_short_line_stops_after_a_line_shorter_than_half_the_margin()
    {
        _app.Workbench.Set("editor.rightMargin", JsonValue.Create(30));
        var tab = Open("alpha beta gamma\r\ndelta.\r\nepsilon zeta eta\r\ntheta iota");
        AssertOneStep(tab, "nks.edit.reformatShortLines", "alpha beta gamma delta.\r\nepsilon zeta eta\r\ntheta iota", () => tab.Editor!.CaretOffset = 1);
        Mod(Key.Z);
        AssertOneStep(tab, "nks.edit.reformatParagraph", "alpha beta gamma delta.\r\nepsilon zeta eta theta iota", () => tab.Editor!.CaretOffset = 1);
    }

    // ---- P4.5.4 date, GUID, repeat ----

    [AvaloniaFact]
    public void Date_and_time_go_into_every_caret_in_the_format_of_the_setting()
    {
        _app.Clock = () => new DateTime(2026, 10, 27, 13, 5, 9);
        var tab = Open("a\nb\nc");
        AssertOneStep(tab, "nks.edit.insertDateTime", "2026-10-27 13:05a\n2026-10-27 13:05b\n2026-10-27 13:05c", () => PlaceCarets(tab, 0, 2, 4));

        Loc.SetLanguage("en");
        _app.Workbench.Set("editor.dateTimeFormat", JsonValue.Create("dddd, d MMMM yyyy"));
        tab.Editor!.SetSelections([new Sel(0, Text(tab).Length)]);
        AssertOneStep(tab, "nks.edit.insertDateTime", "Tuesday, 27 October 2026");
    }

    [AvaloniaFact]
    public void A_date_format_that_is_not_valid_changes_nothing_and_says_why()
    {
        var tab = Open("text");
        _app.Workbench.Set("editor.dateTimeFormat", JsonValue.Create("%"));
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.insertDateTime"));
        Assert.Equal("text", Text(tab));
        Assert.Contains(Loc.Get(K.LinesDateFormatInvalid, ("reason", "")).Split(':')[0], _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void A_guid_goes_into_every_caret_each_one_different()
    {
        var tab = Open("a\nb\nc");
        PlaceCarets(tab, 0, 2, 4);
        int steps = Steps(tab);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.insertGuid"));
        var lines = Text(tab).Split('\n');
        Assert.Equal(3, lines.Length);
        var guids = lines.Select((l, i) => Guid.Parse(l[..^1])).ToList();
        Assert.Equal(3, guids.Distinct().Count());
        Assert.All(lines, l => Assert.Equal(l[..^1].ToLowerInvariant(), l[..^1]));
        Assert.Equal(steps + 1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("a\nb\nc", Text(tab));
        Assert.Equal("a\nb\nc", Bytes(tab));
    }

    [AvaloniaFact]
    public void Repeat_runs_the_last_repeatable_command_with_the_same_parameters_without_asking()
    {
        var tab = Open("a\nb\nc");
        Assert.Equal(CommandOutcome.NotHandled, Run("nks.edit.repeatLast"));

        _dialogs.InsertOptions.Enqueue(new LineInsertOptions(Text: "> "));
        tab.Editor!.CaretOffset = 0;
        Run("nks.edit.insertIntoLines");
        Assert.Equal("> a\n> b\n> c", Text(tab));
        Run("nks.edit.copy");
        int steps = Steps(tab);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.repeatLast"));
        Assert.Equal("> > a\n> > b\n> > c", Text(tab));
        Assert.Equal(1, _dialogs.LineDialogsAsked);
        Assert.Equal(steps + 1, Steps(tab));

        Run("nks.edit.deleteLine");
        Run("nks.edit.repeatLast");
        Assert.Equal("> > c", Text(tab));
    }

    [AvaloniaFact]
    public void Repeating_a_sort_sorts_again_with_the_rules_of_the_last_run()
    {
        var tab = Open("b\nc\na");
        _dialogs.SortOptionsAnswers.Enqueue(new SortOptions(Descending: true));
        Run("nks.edit.sortLines");
        Assert.Equal("c\nb\na", Text(tab));
        tab.Editor!.Document.Text = "x\nz\ny";
        Run("nks.edit.repeatLast");
        Assert.Equal("z\ny\nx", Text(tab));
        Assert.Equal(1, _dialogs.LineDialogsAsked);
    }

    // ---- the real dialogs ----

    private static T Find<T>(Window w, Func<T, bool> match, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) where T : Control =>
        w.GetVisualDescendants().OfType<T>().FirstOrDefault(match) ?? throw new InvalidOperationException($"{typeof(T).Name} not found (test line {line})");

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static TextBox Field(Window w, string label) => Find<TextBox>(w, t => AutomationProperties.GetName(t) == label);

    [AvaloniaFact]
    public async Task The_insert_dialog_follows_its_choices_shows_a_preview_and_returns_them_on_OK()
    {
        var dialogs = new AvaloniaDialogs();
        var previews = new List<string>();
        bool numberRowHidden = false, startRowShown = false;
        dialogs.Shown = w => Dispatcher.UIThread.Post(() =>
        {
            var start = Field(w, Loc.Get(K.LinesInsertStart));
            numberRowHidden = !((Control)start.Parent!).IsVisible;
            Find<RadioButton>(w, r => r.Content as string == Loc.Get(K.LinesInsertModeNumbering)).IsChecked = true;
            startRowShown = ((Control)start.Parent!).IsVisible;
            start.Text = "5";
            Field(w, Loc.Get(K.LinesInsertWidth)).Text = "3";
            Field(w, Loc.Get(K.LinesInsertSeparator)).Text = ". ";
            Find<RadioButton>(w, r => r.Content as string == Loc.Get(K.LinesInsertPlaceEnd)).IsChecked = true;
            Click(Find<Button>(w, b => b.Content as string == Loc.Get(K.DialogOk)));
        }, DispatcherPriority.Background);

        var result = await dialogs.InsertIntoLinesAsync(_w, new LineInsertOptions(), o => { previews.Add($"{o.Mode}/{o.Place}/{o.Start}/{o.Width}"); return "preview"; });

        Assert.Equal(new LineInsertOptions(LineInsertMode.Numbering, "", LineInsertPlace.LineEnd, 1, false, false, 5, 3, ". "), result);
        Assert.True(numberRowHidden);
        Assert.True(startRowShown);
        Assert.Contains("Numbering/LineEnd/5/3", previews);
    }

    [AvaloniaFact]
    public async Task The_insert_dialog_returns_nothing_on_cancel()
    {
        var dialogs = new AvaloniaDialogs { Shown = w => Dispatcher.UIThread.Post(() => Click(Find<Button>(w, b => b.Content as string == Loc.Get(K.DialogCancel))), DispatcherPriority.Background) };
        Assert.Null(await dialogs.InsertIntoLinesAsync(_w, new LineInsertOptions(), _ => ""));
    }

    [AvaloniaFact]
    public async Task The_sort_dialog_refuses_an_unknown_locale_and_returns_the_rules_on_OK()
    {
        var dialogs = new AvaloniaDialogs();
        bool okWhenWrong = true, okWhenRight = false;
        string noteWhenWrong = "";
        dialogs.Shown = w => Dispatcher.UIThread.Post(() =>
        {
            Find<RadioButton>(w, r => r.Content as string == Loc.Get(K.LinesSortByLocale)).IsChecked = true;
            Find<RadioButton>(w, r => r.Content as string == Loc.Get(K.LinesSortDescending)).IsChecked = true;
            Find<CheckBox>(w, c => c.Content as string == Loc.Get(K.LinesSortDuplicates)).IsChecked = true;
            var ok = Find<Button>(w, b => b.Content as string == Loc.Get(K.DialogOk));
            var locale = Field(w, Loc.Get(K.LinesSortLocale));
            locale.Text = "zz-nope-zz";
            Dispatcher.UIThread.RunJobs();
            okWhenWrong = ok.IsEnabled;
            noteWhenWrong = Find<TextBlock>(w, t => t.Text == Loc.Get(K.LinesSortLocaleInvalid)).Text!;
            locale.Text = "0405";
            Dispatcher.UIThread.RunJobs();
            okWhenRight = ok.IsEnabled;
            Field(w, Loc.Get(K.LinesSortFrom)).Text = "3";
            Click(ok);
        }, DispatcherPriority.Background);

        var result = await dialogs.SortLinesAsync(_w, new SortOptions(), o => "p");

        Assert.Equal(new SortOptions(Descending: true, RemoveDuplicates: true, Method: SortMethod.Locale, Locale: "0405", ColumnFrom: 3), result);
        Assert.False(okWhenWrong);
        Assert.True(okWhenRight);
        Assert.Equal(Loc.Get(K.LinesSortLocaleInvalid), noteWhenWrong);
    }

    // ---- the closed list ----

    [AvaloniaFact]
    public void The_commands_of_the_three_items_are_exactly_these_and_each_has_a_handler_a_title_in_every_language_and_a_place_in_a_menu()
    {
        string[] ids =
        [
            "nks.edit.clipboardHistory",
            "nks.edit.pasteKeepCaret", "nks.edit.pasteAsHtml", "nks.edit.insertFromFile", "nks.edit.insertIntoLines", "nks.edit.sortLines", "nks.search.selectLinesByRange",
            "nks.edit.reformatParagraph", "nks.edit.reformatShortLines", "nks.edit.justifyLeft", "nks.edit.justifyRight", "nks.edit.justifyCenter", "nks.edit.justifyBlock",
            "nks.edit.insertDateTime", "nks.edit.insertGuid", "nks.edit.repeatLast",
        ];
        var wb = _app.Workbench;
        var inMenus = wb.Commands.CommandsInMenus();
        foreach (var id in ids)
        {
            Assert.True(wb.Commands.TryGet(id, out var command), id);
            Assert.True(wb.Commands.HasHandler(id), id);
            Assert.Contains(id, inMenus);
            Assert.NotEqual(command.TitleKey, wb.Strings.Get(command.TitleKey));
        }
        string[] repeatable = [.. wb.Commands.Commands.Where(c => c.Repeatable).Select(c => c.Id).Order()];
        string[] expected =
        [
            "nks.edit.convert", "nks.edit.deleteLine", "nks.edit.duplicateLine", "nks.edit.indent", "nks.edit.insertDateTime", "nks.edit.insertGuid", "nks.edit.insertIntoLines",
            "nks.edit.joinLines", "nks.edit.justifyBlock", "nks.edit.justifyCenter", "nks.edit.justifyLeft", "nks.edit.justifyRight", "nks.edit.lowerCase",
            "nks.edit.moveLineDown", "nks.edit.moveLineUp", "nks.edit.outdent", "nks.edit.reformatParagraph", "nks.edit.reformatShortLines", "nks.edit.runRecipe", "nks.edit.sortLines",
            "nks.edit.toggleComment", "nks.edit.upperCase", .. NksHub.Textida.Core.Conversions.SpecialConversions.All.Select(c => c.CommandId),
        ];
        Assert.Equal(expected.Order(), repeatable);
        Assert.Equal("nks.edit.clipboardHistory", wb.Keymap.Bindings.Single(b => b.Sequence.Chords.Count == 1 && b.Sequence.Chords[0].Key == "v" && b.Sequence.Chords[0].Modifiers.HasFlag(NksHub.Textida.Workbench.Keys.KeyModifiers.Shift)).CommandId);
    }

    [AvaloniaFact]
    public void The_options_of_the_dialog_commands_are_exactly_these()
    {
        static string[] Parameters(Type t) => [.. t.GetConstructors().Single().GetParameters().Select(p => p.Name!)];
        Assert.Equal(["Mode", "Text", "Place", "Column", "SkipIndent", "SkipEmptyLines", "Start", "Width", "Separator"], Parameters(typeof(LineInsertOptions)));
        Assert.Equal(["Text", "Numbering", "Bullets"], Enum.GetNames<LineInsertMode>());
        Assert.Equal(["LineStart", "LineEnd", "Column", "Selections"], Enum.GetNames<LineInsertPlace>());
        Assert.Equal(["Descending", "CaseSensitive", "RemoveDuplicates", "Method", "Locale", "ColumnFrom", "ColumnTo"], Parameters(typeof(SortOptions)));
        Assert.Equal(["Ordinal", "Locale", "Numeric"], Enum.GetNames<SortMethod>());
        Assert.Equal(["Left", "Right", "Center", "Block"], Enum.GetNames<Justification>());
    }
}
