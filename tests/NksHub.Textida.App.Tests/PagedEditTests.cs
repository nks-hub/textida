using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>Editing on the paged surface through the keyboard path of the window: typing, moving, deleting, undo, redo and save.</summary>
[Collection("LocState")]
public class PagedEditTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static PagedEditTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public PagedEditTests()
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

    private static byte[] Rows(int count, string end = "\n") => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, count).Select(i => $"line {i}{end}")));

    private DocTab OpenPaged(byte[] content, string name = "big.log")
    {
        var tab = _w.OpenFile(_temp.Bytes(name, content), null);
        Assert.True(tab.Doc.IsLarge);
        WaitIndexed(tab);
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private static void WaitIndexed(DocTab tab)
    {
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged!.Index.Complete);
    }

    private static string Head(DocTab tab, int bytes)
    {
        var doc = tab.Doc.Doc;
        return Encoding.UTF8.GetString(doc.ReadBytes(new ByteRange(0, Math.Min(bytes, doc.ByteLength))));
    }

    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    [AvaloniaTheory]
    [InlineData(0, 10, 14, true)]
    [InlineData(0, 10, 13, false)]
    [InlineData(0, 10, 10, false)]
    [InlineData(20, 5, 12, false)]
    public void Only_lines_ending_before_the_first_change_keep_their_decoded_text(long start, long length, long firstChange, bool kept) =>
        Assert.Equal(kept, PagedTextView.LineSurvives(start, length, firstChange));

    /// <summary>The text the screen-reader window of the view holds is the document from the start of its first line.</summary>
    private static void AssertWindowMatchesDocument(DocTab tab)
    {
        using var surface = new PagedTextView.TextSurface(tab.Paged!);
        var paged = ((PagedBackend)tab.Doc.Doc.Backend).Paged;
        Assert.True(paged.TryGetLineStart(surface.FirstLine, out long start));
        Assert.Equal(Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(start, start + surface.Length))), surface.GetText(0, surface.Length));
    }

    [AvaloniaFact]
    public void Enter_joining_lines_and_undo_decode_again_only_the_lines_they_touch()
    {
        var tab = OpenPaged(Rows(200_000));
        var view = tab.Paged!;
        for (int i = 0; i < 4; i++) Press(Key.Down);
        Press(Key.End); // end of "line 5", on screen with lines above and below it
        _w.CaptureRenderedFrame();

        int before = view.DecodedLines;
        Press(Key.Enter);
        _w.CaptureRenderedFrame();
        Assert.Equal(2, view.DecodedLines - before); // "line 5" and the new empty line; the lines below only moved one line down
        Assert.Equal((6L, 1L), tab.CaretPosition());
        AssertWindowMatchesDocument(tab);

        before = view.DecodedLines;
        Press(Key.Back); // joins the empty line to "line 5" again
        _w.CaptureRenderedFrame();
        Assert.Equal(1, view.DecodedLines - before);
        Assert.Equal((5L, 7L), tab.CaretPosition());
        AssertWindowMatchesDocument(tab);

        before = view.DecodedLines;
        TestApp.Press(_w, Key.Z, TestApp.Mod); // undo of the join: the break comes back
        _w.CaptureRenderedFrame();
        Assert.InRange(view.DecodedLines - before, 1, 2);
        AssertWindowMatchesDocument(tab);
        Assert.Equal("line 5\n\nline 6\n", Encoding.ASCII.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(28, 28 + 15))));
    }

    [AvaloniaFact]
    public void The_decoded_lines_stay_within_their_budget_while_paging_down()
    {
        var tab = OpenPaged(Rows(200_000));
        var view = tab.Paged!;
        view.LineCacheBudget = 32 * 1024;
        _w.CaptureRenderedFrame();
        long peak = 0;
        for (int k = 1; k <= 60; k++)
        {
            Press(Key.PageDown);
            _w.CaptureRenderedFrame();
            peak = Math.Max(peak, view.CachedLineBytes);
            Assert.True(view.CachedLineBytes <= view.LineCacheBudget + 64 * 1024, $"{view.CachedLineBytes} bytes of decoded lines after {k} pages");
            Assert.Equal((1L + k * view.PageLines, 1L), tab.CaretPosition());
        }
        Assert.True(peak > view.LineCacheBudget / 2, $"the pages filled the cache ({peak} bytes), so lines were dropped on the way");
        AssertWindowMatchesDocument(tab);
    }

    [AvaloniaFact]
    public async Task Typing_moving_deleting_undo_redo_and_save_keep_the_bytes_exact()
    {
        var original = Rows(150_000);
        var tab = OpenPaged(original);

        TestApp.Type(_w, "ab");
        Press(Key.Enter);
        TestApp.Type(_w, "é");
        Assert.Equal("ab\néline 1\n", Head(tab, 12));
        Assert.Equal((2L, 2L), tab.CaretPosition());
        Assert.Contains("Ř 2, Sl 2", _w.StatusLeftText);
        Assert.Contains("revize " + tab.Doc.Doc.Revision, _w.StatusLeftText);

        Press(Key.Left);
        Press(Key.Delete); // removes the two bytes of é
        Assert.Equal("ab\nline 1\n", Head(tab, 10));
        Press(Key.Back); // joins the lines
        Assert.Equal("abline 1\nline ", Head(tab, 14));
        Assert.Equal((1L, 3L), tab.CaretPosition());

        TestApp.Press(_w, Key.Z, TestApp.Mod);
        Assert.Equal("ab\nline 1\n", Head(tab, 10));
        Assert.Equal((2L, 1L), tab.CaretPosition());
        TestApp.Press(_w, Key.Z, TestApp.Mod);
        Assert.Equal("ab\néline 1\n", Head(tab, 12));
        TestApp.Press(_w, Key.Z, TestApp.Mod);
        Assert.Equal("ab\nline 1\n", Head(tab, 10));
        TestApp.Press(_w, Key.Z, TestApp.Mod | RawInputModifiers.Shift);
        Assert.Equal("ab\néline 1\n", Head(tab, 12));
        Assert.Equal((2L, 2L), tab.CaretPosition());

        Press(Key.Down);
        Press(Key.Home);
        TestApp.Type(_w, "x");
        Assert.True(tab.Doc.Doc.IsModified);
        Assert.True(await _w.SaveAsync(tab));
        Assert.False(tab.Doc.Doc.IsModified);

        byte[] expected = [.. "ab\né"u8, .. original[..7], (byte)'x', .. original[7..]];
        Assert.True(expected.AsSpan().SequenceEqual(File.ReadAllBytes(tab.Path!)));

        WaitIndexed(tab);
        TestApp.Type(_w, "z");
        Assert.Equal("xz", Encoding.ASCII.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(12, 14))));
    }

    [AvaloniaFact]
    public void Arrows_keep_the_wanted_column_and_cross_line_ends_and_surrogate_pairs()
    {
        var tab = OpenPaged(Encoding.UTF8.GetBytes("short\na😀b long line\nz\n" + string.Concat(Enumerable.Range(0, 200_000).Select(i => $"row {i}\n"))));
        Press(Key.End);
        Assert.Equal((1L, 6L), tab.CaretPosition());
        Press(Key.Down); // wanted column 5 on a longer line
        Assert.Equal((2L, 6L), tab.CaretPosition());
        Press(Key.Home);
        Press(Key.Right);
        Press(Key.Right); // over the pair of UTF-16 units in one step
        Assert.Equal((2L, 4L), tab.CaretPosition());
        Press(Key.Left);
        Assert.Equal((2L, 2L), tab.CaretPosition());
        Press(Key.End);
        Press(Key.Right); // end of the line goes to the start of the next one
        Assert.Equal((3L, 1L), tab.CaretPosition());
        Press(Key.Down);
        Press(Key.Up);
        Assert.Equal((3L, 1L), tab.CaretPosition());
        Press(Key.Left);
        Assert.Equal((2L, 15L), tab.CaretPosition());
        Press(Key.End, TestApp.Mod);
        Assert.Equal(tab.Doc.Doc.ByteLength, tab.Paged!.CaretOffset);
        Press(Key.Home, TestApp.Mod);
        Assert.Equal(0L, tab.Paged.CaretOffset);
    }

    [AvaloniaFact]
    public void Backspace_removes_a_whole_character_and_delete_a_crlf_as_one()
    {
        var tab = OpenPaged(Encoding.UTF8.GetBytes("a😀\r\nb\r\n" + string.Concat(Enumerable.Range(0, 200_000).Select(i => $"row {i}\r\n"))));
        Press(Key.End);
        Press(Key.Back);
        Assert.Equal("a\r\nb\r\n", Head(tab, 6));
        Press(Key.Delete); // joins with the next line, both bytes of the break go
        Assert.Equal("ab\r\nrow", Head(tab, 7));
        Assert.Equal((1L, 2L), tab.CaretPosition());
    }

    [AvaloniaFact]
    public void Enter_uses_the_line_ending_of_the_document_and_tab_follows_the_indent_setting()
    {
        var tab = OpenPaged(Rows(200_000, "\r\n"));
        Press(Key.Enter);
        Press(Key.Tab);
        Assert.Equal("\r\n    line 1\r\n", Head(tab, 14));
    }

    [AvaloniaFact]
    public void A_line_longer_than_the_decoded_part_is_edited_at_its_start_and_its_end()
    {
        var tab = OpenPaged([.. Encoding.ASCII.GetBytes(new string('x', 40_000) + "\n"), .. Rows(100_000)]);
        TestApp.Type(_w, "q");
        Press(Key.End);
        TestApp.Type(_w, "z");
        Assert.Equal("q", Head(tab, 1));
        Assert.Equal("xz\nline", Encoding.ASCII.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(40_000, 40_007))));
        Assert.Equal((1L, 40_003L), tab.CaretPosition());
        Press(Key.Down);
        Press(Key.Home);
        TestApp.Type(_w, "q");
        Assert.Equal("q", Encoding.ASCII.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(40_003, 40_004))));
    }

    [AvaloniaFact]
    public void A_character_the_encoding_cannot_hold_is_refused_not_replaced()
    {
        var line = Encoding.GetEncoding(1250).GetBytes("Příliš žluťoučký kůň úpěl ďábelské ódy\n");
        var tab = OpenPaged([.. Enumerable.Range(0, 30_000).SelectMany(_ => line)], "cs.txt");
        Assert.Equal("Windows-1250", tab.Doc.Doc.Codec.Name);
        TestApp.Type(_w, "日");
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Contains("Windows-1250", _w.StatusLeftText);
        TestApp.Type(_w, "ř");
        Assert.Equal(0xF8, tab.Doc.Doc.ReadBytes(new ByteRange(0, 1))[0]);
    }

    private const RawInputModifiers Shift = RawInputModifiers.Shift;

    private static long Offset(string all, int line, int column = 0) =>
        Encoding.UTF8.GetByteCount(string.Join("", all.Split('\n').Take(line).Select(l => l + "\n"))) + column;

    [AvaloniaFact]
    public void Selection_over_index_blocks_and_crlf_is_exactly_the_bytes_between_the_ends()
    {
        string all = Encoding.UTF8.GetString(Rows(200_000, "\r\n"));
        var tab = OpenPaged(Encoding.UTF8.GetBytes(all));
        var view = tab.Paged!;
        Press(Key.Right);
        Press(Key.Right);
        Press(Key.End, TestApp.Mod | Shift); // the whole file is over 2 MiB: several blocks of the line index
        Assert.True(view.HasSelection);
        Assert.Equal(new ByteRange(2, all.Length), view.SelectionRange);
        Assert.Equal(all[2..], view.SelectedText());

        Press(Key.Home, TestApp.Mod | Shift); // the other end of the same anchor: the selection turns around
        Assert.Equal(new ByteRange(0, 2), view.SelectionRange);
        Assert.Equal("li", view.SelectedText());

        Press(Key.Right); // collapses to the end of the selection
        Assert.False(view.HasSelection);
        Assert.Equal(2L, view.CaretOffset);

        Press(Key.End); // before the line break
        Press(Key.Right, Shift); // the break CR LF is one step
        Assert.Equal(new ByteRange(6, 8), view.SelectionRange);
        Assert.Equal("\r\n", view.SelectedText());
        Press(Key.Left, Shift);
        Assert.False(view.HasSelection);
        Press(Key.Left, Shift);
        Assert.Equal(new ByteRange(5, 6), view.SelectionRange);
        Press(Key.Left); // collapses to the start
        Assert.Equal(5L, view.CaretOffset);
    }

    [AvaloniaFact]
    public void Home_end_and_paging_land_on_the_positions_of_a_naive_pass()
    {
        string all = Encoding.UTF8.GetString(Rows(200_000));
        var tab = OpenPaged(Encoding.UTF8.GetBytes(all));
        _w.UpdateLayout();
        var view = tab.Paged!;
        int page = view.PageLines;
        Assert.True(page > 1);

        Press(Key.Right);
        Press(Key.Right);
        Press(Key.PageDown);
        Assert.Equal((page + 1L, 3L), tab.CaretPosition()); // the wanted column stays
        Assert.Equal(Offset(all, page, 2), view.CaretOffset);
        Press(Key.PageDown, Shift);
        Assert.Equal(new ByteRange(Offset(all, page, 2), Offset(all, 2 * page, 2)), view.SelectionRange);
        Press(Key.PageUp, Shift);
        Assert.False(view.HasSelection);
        Press(Key.PageUp);
        Assert.Equal(Offset(all, 0, 2), view.CaretOffset);

        Press(Key.End, Shift);
        Assert.Equal(new ByteRange(2, 6), view.SelectionRange);
        Press(Key.Home, Shift);
        Assert.Equal(new ByteRange(0, 2), view.SelectionRange);
        Press(Key.Home);
        Assert.Equal(0L, view.CaretOffset);

        Press(Key.End, TestApp.Mod);
        Assert.Equal((200_001L, 1L), tab.CaretPosition()); // the empty line after the last break
        Press(Key.PageUp);
        Assert.Equal(Offset(all, 200_000 - page), view.CaretOffset);
        Press(Key.PageDown);
        Press(Key.PageDown);
        Assert.Equal((long)all.Length, view.CaretOffset);
    }

    [AvaloniaFact]
    public async Task Copy_gives_the_exact_text_over_blocks_crlf_and_pieces_of_edits()
    {
        string all = Encoding.UTF8.GetString(Rows(200_000, "\r\n"));
        var tab = OpenPaged(Encoding.UTF8.GetBytes(all));
        var view = tab.Paged!;
        TestApp.Type(_w, "xy");
        Press(Key.Enter); // "xy\r\n" is now a piece of its own in front of the mapped file
        Press(Key.Home, TestApp.Mod);
        Press(Key.Right);
        Press(Key.Down, Shift);
        Assert.Equal("y\r\nl", view.SelectedText());
        await view.CopyAsync(cut: false);
        Assert.Equal("y\r\nl", await _w.Clipboard!.TryGetTextAsync());

        Press(Key.End, TestApp.Mod | Shift);
        await view.CopyAsync(cut: false);
        string expected = "xy\r\n" + all;
        Assert.Equal(expected[1..], await _w.Clipboard!.TryGetTextAsync());
    }

    [AvaloniaFact]
    public async Task Cut_and_paste_are_one_undo_step_each_and_typing_over_a_selection_replaces_it()
    {
        var tab = OpenPaged(Rows(200_000, "\r\n"));
        var view = tab.Paged!;
        var doc = tab.Doc.Doc;
        void Select()
        {
            Press(Key.Home, TestApp.Mod);
            Press(Key.Right);
            Press(Key.Down, Shift);
        }

        Select();
        string selected = view.SelectedText();
        Assert.Equal("ine 1\r\nl", selected);
        int depth = doc.UndoDepth;
        await view.CopyAsync(cut: true);
        Assert.Equal(depth + 1, doc.UndoDepth);
        Assert.Equal("line 2\r\nline", Head(tab, 12));
        Assert.Equal("ine 1\r\nl", await _w.Clipboard!.TryGetTextAsync());
        Press(Key.Z, TestApp.Mod);
        Assert.Equal(depth, doc.UndoDepth);
        Assert.Equal("line 1\r\nline 2", Head(tab, 14));

        Select();
        await _w.Clipboard!.SetTextAsync("A\nB\rC");
        await view.PasteAsync(); // line breaks become the CRLF of the document
        Assert.Equal(depth + 1, doc.UndoDepth);
        Assert.Equal("lA\r\nB\r\nCine ", Head(tab, 12));
        Assert.Equal((3L, 2L), tab.CaretPosition());
        Press(Key.Z, TestApp.Mod);
        Assert.Equal("line 1\r\nline 2", Head(tab, 14));

        Select();
        TestApp.Type(_w, "Q");
        TestApp.Type(_w, "R");
        Assert.Equal("lQRine 2", Head(tab, 8));
        Press(Key.Z, TestApp.Mod);
        Assert.Equal("lQine 2", Head(tab, 7)); // the replacement is its own step, the next typed character is a run of its own
        Press(Key.Z, TestApp.Mod);
        Assert.Equal("line 1\r\nline 2", Head(tab, 14));

        Select();
        Press(Key.Back);
        Assert.Equal("line 2", Head(tab, 6));
        Press(Key.Z, TestApp.Mod);
        Assert.Equal("line 1\r\nline 2", Head(tab, 14));
        Select();
        Press(Key.Delete);
        Assert.Equal("line 2", Head(tab, 6));
        Assert.False(view.HasSelection);
    }

    [AvaloniaFact]
    public async Task Select_all_copy_cut_and_paste_commands_reach_the_paged_surface()
    {
        string all = Encoding.UTF8.GetString(Rows(200_000));
        var tab = OpenPaged(Encoding.UTF8.GetBytes(all));
        TestApp.Press(_w, Key.A, TestApp.Mod);
        Assert.Equal(new ByteRange(0, all.Length), tab.Paged!.SelectionRange);
        TestApp.Press(_w, Key.C, TestApp.Mod);
        await Eventually(async () => await _w.Clipboard!.TryGetTextAsync() == all);

        Press(Key.Home, TestApp.Mod);
        Press(Key.Down, Shift);
        Assert.Equal(new ByteRange(0, 7), tab.Paged.SelectionRange);
        TestApp.Press(_w, Key.X, TestApp.Mod);
        await Eventually(() => Task.FromResult(Head(tab, 6) == "line 2"), () => Head(tab, 30) + "|" + _w.StatusLeftText);
        TestApp.Press(_w, Key.V, TestApp.Mod);
        await Eventually(() => Task.FromResult(Head(tab, 6) == "line 1"));
        Assert.Equal(2, tab.Doc.Doc.UndoDepth);
    }

    private static async Task Eventually(Func<Task<bool>> condition, Func<string>? state = null)
    {
        for (int i = 0; i < 200 && !await condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(await condition(), state?.Invoke());
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void Typing_on_the_paged_surface_stays_within_33_ms_at_p95()
    {
        var tab = OpenPaged(Rows(300_000));
        _w.UpdateLayout();
        for (int i = 0; i < 30; i++) { TestApp.Type(_w, "w"); Dispatcher.UIThread.RunJobs(); } // warm up
        var times = new List<double>();
        for (int i = 0; i < 300; i++)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            TestApp.Type(_w, "x");
            _w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        }
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        Assert.True(p95 <= 33, $"typing p95 {p95:0.0} ms (median {times[times.Count / 2]:0.0} ms)");
        Assert.Equal(1, tab.Doc.Doc.UndoDepth); // 330 keystrokes at one place are one undo step
    }

    [AvaloniaFact]
    public void The_input_method_gets_a_client_that_follows_the_caret_and_committed_text_lands_once()
    {
        var tab = OpenPaged(Rows(200_000));
        _w.UpdateLayout();
        var args = new Avalonia.Input.TextInput.TextInputMethodClientRequestedEventArgs { RoutedEvent = Avalonia.Input.InputElement.TextInputMethodClientRequestedEvent };
        tab.Paged!.RaiseEvent(args);
        var client = Assert.IsAssignableFrom<Avalonia.Input.TextInput.TextInputMethodClient>(args.Client);
        Assert.True(client.SupportsPreedit); // the composition is drawn inline at the caret
        var start = client.CursorRectangle;
        int changes = 0;
        client.CursorRectangleChanged += (_, _) => changes++;

        Press(Key.End);
        var atEnd = client.CursorRectangle;
        Assert.True(atEnd.X > start.X);
        Assert.Equal("line 1", client.SurroundingText);

        _w.KeyTextInput("日本"); // what a finished composition delivers
        Assert.Equal("line 1日本\nline 2", Head(tab, 6 + 6 + 1 + 6));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        Assert.True(changes >= 2);

        Press(Key.Down);
        Assert.True(client.CursorRectangle.Y > atEnd.Y);
    }

    [AvaloniaFact]
    public void Automation_hook_lets_the_accessibility_library_supply_the_peer_of_the_paged_surface()
    {
        var provider = new RecordingProvider();
        EditorAutomationHook.Provider = provider;
        try
        {
            var tab = OpenPaged(Rows(200_000));
            _ = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(tab.Paged!);
            Assert.Same(tab.Paged, provider.Asked);
        }
        finally { EditorAutomationHook.Provider = null; }
    }

    private sealed class RecordingProvider : IEditorAutomationProvider
    {
        public Avalonia.Controls.Control? Asked { get; private set; }

        public Avalonia.Automation.Peers.AutomationPeer? CreatePeer(Avalonia.Controls.Control editor, Document document)
        {
            Asked = editor;
            return null;
        }
    }
}
