using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    public void A_line_over_the_limit_is_shown_but_cannot_be_edited_and_the_status_says_why()
    {
        var tab = OpenPaged([.. Encoding.ASCII.GetBytes(new string('x', 40_000) + "\n"), .. Rows(100_000)]);
        TestApp.Type(_w, "q");
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Contains("nedá upravit", _w.StatusLeftText);
        Press(Key.Down);
        TestApp.Type(_w, "q");
        Assert.Equal("q", Encoding.ASCII.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(40_001, 40_002))));
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

    [AvaloniaFact]
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
        Assert.False(client.SupportsPreedit); // the system window draws the composition
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
