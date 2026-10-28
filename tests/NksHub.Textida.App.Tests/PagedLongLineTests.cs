using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

/// <summary>Horizontal windows of a long line on the paged surface: reads off the UI thread (also after an edit), stale reads, colors, the scroll range in characters.</summary>
[Collection("LocState")]
public class PagedLongLineTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PagedLongLineTests()
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

    /// <summary>Writes one line of <paramref name="size"/> ASCII letters and a second line "next".</summary>
    internal static void WriteLongLine(string path, int size)
    {
        using var file = File.Create(path);
        var chunk = new byte[1 << 20];
        for (long done = 0; done < size; done += chunk.Length)
        {
            for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)('a' + (done + i) * 7 % 26);
            file.Write(chunk, 0, (int)Math.Min(chunk.Length, size - done));
        }
        file.Write("\nnext\n"u8);
    }

    private DocTab OpenLine(string line) => OpenBytes(Encoding.UTF8.GetBytes(line + "\nnext\n"));

    private DocTab OpenBytes(byte[] content, string name = "long.txt")
    {
        var tab = _w.OpenFile(_temp.Bytes(name, content), null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged!.Index.Complete);
        tab.Paged.Focus();
        Draw();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private void Draw()
    {
        _w.UpdateLayout();
        _w.CaptureRenderedFrame();
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task A_far_window_is_read_off_the_UI_thread_and_a_stale_read_is_never_painted()
    {
        var tab = OpenLine(new string('a', 3_000_000));
        var paged = tab.Paged!;
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        paged.WindowRead = (_, column, _, _) =>
        {
            if (column < 2_400_000) { started.Set(); gate.Wait(TimeSpan.FromSeconds(10)); return "STALE"; }
            return "FRESH";
        };

        long t0 = Stopwatch.GetTimestamp();
        paged.ScrollToColumn(2_000_000);
        Draw();
        Assert.True(Stopwatch.GetElapsedTime(t0).TotalSeconds < 5, "the jump waited for the read");
        Assert.False(gate.IsSet);
        Assert.True(paged.WindowsPending);
        Assert.Null(paged.CachedWindow(0));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        paged.ScrollToColumn(2_500_000);
        Draw();
        await Eventually(() => paged.CachedWindow(0) == "FRESH");
        Assert.False(paged.WindowsPending);

        gate.Set(); // the older read ends now and must be dropped
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        Draw();
        Assert.Equal("FRESH", paged.CachedWindow(0));
    }

    [AvaloniaFact]
    public async Task A_far_window_of_an_edited_line_is_read_off_the_UI_thread_and_shows_the_edited_text()
    {
        var random = new Random(3);
        string line = string.Create(3_000_000, random, (span, r) => { for (int i = 0; i < span.Length; i++) span[i] = (char)('a' + r.Next(26)); });
        var tab = OpenLine(line);
        var paged = tab.Paged!;
        TestApp.Type(_w, "XYZ"); // the caret starts at the beginning of the line
        Draw();
        string edited = "XYZ" + line;
        var document = ((PagedBackend)tab.Doc.Doc.Backend).Paged;
        Assert.True(document.IsModified);
        Assert.False(document.IsColumnNear(0, 2_500_000)); // no point near: the window is read the far way, on the thread pool

        paged.ScrollToColumn(2_500_000);
        Draw();
        string? window = null;
        await Eventually(() => { Draw(); return (window = paged.CachedWindow(0)) is { Length: > 0 }; });
        // the bar may land one column early: hScroll / cell is rounded down
        Assert.True(window == edited.Substring(2_500_000, window!.Length) || window == edited.Substring(2_499_999, window.Length), "the window does not show the edited text");
        Assert.False(paged.WindowsPending);
        Assert.True(document.IsColumnNear(0, 2_500_000)); // the points of the far read came back, so scrolling on reads on the UI thread
    }

    [AvaloniaFact]
    public async Task A_far_window_of_a_long_line_gets_the_colors_of_its_grammar()
    {
        string line = "[" + string.Concat(Enumerable.Repeat("\"ab\",1,", 160_000)) + "0]"; // 1 120 003 characters, above the 1 MiB threshold of the paged mode
        var tab = OpenBytes(Encoding.UTF8.GetBytes(line + "\n[2]\n"), "long.json");
        var paged = tab.Paged!;
        Assert.True(paged.Syntax.Active);

        paged.ScrollToColumn(150_000);
        Draw();
        bool Colored() => paged.TokensOf(0) is { } tokens
            && tokens.Any(t => t.Start >= 150_010 && t.Kind == TokenKinds.String)
            && tokens.Any(t => t.Start >= 150_010 && t.Kind == TokenKinds.Number);
        await Eventually(() => { Draw(); return Colored(); });

        foreach (var token in paged.TokensOf(0)!.Where(t => t.Start >= 150_010 && t.End < 150_050))
        {
            string text = line.Substring(token.Start, token.Length);
            if (token.Kind == TokenKinds.String) Assert.Equal("\"ab\"", text);
            if (token.Kind == TokenKinds.Number) Assert.Equal("1", text);
        }
        Assert.NotNull(paged.CachedWindow(0));
    }

    private const string Unit = "ěšč abc 中";

    private static string Repeat(int units) => string.Concat(Enumerable.Repeat(Unit, units));

    private static long ByteOffset(string text, int column) => Encoding.UTF8.GetByteCount(text.AsSpan(0, column));

    private static string Text(DocTab tab) => Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));

    [AvaloniaFact]
    public void A_line_of_millions_of_columns_is_edited_in_place_with_the_caret_on_the_right_column()
    {
        string model = Repeat(300_000); // 3 000 000 characters, 4.2 MB
        var tab = OpenLine(model);
        var paged = tab.Paged!;
        int col = 2_500_000;
        paged.GoToOffset(ByteOffset(model, col));

        void Expect(string? whole = null, (long Line, long Column)? caret = null)
        {
            Draw();
            Assert.True((whole ?? model + "\nnext\n") == Text(tab), "the document differs from the model");
            Assert.Equal(caret ?? (1L, (long)col + 1), tab.CaretPosition());
            if (caret is null) Assert.Equal(ByteOffset(model, col), paged.CaretOffset);
        }
        void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) { TestApp.Press(_w, key, mods); Dispatcher.UIThread.RunJobs(); }
        void Type(string text)
        {
            TestApp.Type(_w, text);
            model = model.Insert(col, text);
            col += text.Length;
        }

        Expect();
        Type("XY");
        Expect();
        // the window of the edited revision is read off the UI thread: draw until it has come back
        for (int i = 0; i < 1500 && paged.CachedWindow(0)?.Contains("XY", StringComparison.Ordinal) != true; i++)
        {
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
            Draw();
        }
        Assert.Contains("XY", paged.CachedWindow(0));

        Press(Key.Back);
        model = model.Remove(col - 1, 1);
        col--;
        Expect();

        Press(Key.Delete);
        model = model.Remove(col, 1);
        Expect();

        Press(Key.Left);
        Press(Key.Left);
        Press(Key.Right);
        col--;
        Expect();

        Type("😀");
        Expect();
        Press(Key.Back); // the whole surrogate pair
        model = model.Remove(col - 2, 2);
        col -= 2;
        Expect();

        Press(Key.End);
        col = model.Length;
        Expect();
        Type("Z");
        Expect();
        Press(Key.Home);
        col = 0;
        Expect();
        Type("A");
        Expect();

        // the selection: three characters by Shift+Right, replaced by typing; then to the end of the line
        col = 1_000_000;
        paged.GoToOffset(ByteOffset(model, col));
        for (int i = 0; i < 3; i++) Press(Key.Right, RawInputModifiers.Shift);
        Draw();
        Assert.Equal(model.Substring(col, 3), paged.SelectedText());
        TestApp.Type(_w, "Q");
        model = model[..col] + "Q" + model[(col + 3)..];
        col++;
        Expect();
        Press(Key.End, RawInputModifiers.Shift);
        Draw();
        Assert.True(model[col..] == paged.SelectedText(), "the selection to the end of the line differs");
        Press(Key.Delete);
        model = model[..col];
        Expect();

        // up and down keep the wanted column through the short line below
        Press(Key.Down);
        Draw();
        Assert.Equal((2L, 5L), tab.CaretPosition());
        Press(Key.Up);
        Expect();

        // a paste of one MiB in the middle, then Enter splits the line and Backspace joins it again
        col = 500_000;
        paged.GoToOffset(ByteOffset(model, col));
        string pasted = new string('p', 1 << 20) + "é";
        Assert.True(paged.PasteText(pasted));
        model = model.Insert(col, pasted);
        col += pasted.Length;
        Expect();
        Press(Key.Enter);
        string eol = Text(tab)[col] == '\r' ? "\r\n" : "\n"; // a file whose first lines hold no break gets the default of the platform
        Expect(model[..col] + eol + model[col..] + "\nnext\n", (2L, 1L));
        Press(Key.Back);
        Expect();

        for (int i = 0; i < 100 && tab.Doc.Doc.UndoDepth > 0; i++) { TestApp.Press(_w, Key.Z, TestApp.Mod); Dispatcher.UIThread.RunJobs(); }
        Draw();
        Assert.True(Repeat(300_000) + "\nnext\n" == Text(tab), "undo did not restore the line");
    }

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public void Typing_in_the_middle_of_a_100_MiB_line_does_not_grow_the_memory_by_the_size_of_the_line()
    {
        const int size = 100 * 1024 * 1024;
        string path = Path.Combine(_temp.Path, "line100.txt");
        WriteLongLine(path, size);
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged!.Index.Complete);
        var paged = tab.Paged;
        paged.Focus();
        Draw();
        paged.GoToOffset(50_000_000);
        Draw();

        long managed = GC.GetTotalMemory(true);
        for (int i = 0; i < 100; i++) TestApp.Type(_w, "x");
        TestApp.Press(_w, Key.Back);
        TestApp.Press(_w, Key.Delete);
        Assert.True(paged.PasteText(new string('v', 1 << 20)));
        Draw();
        long grown = GC.GetTotalMemory(true) - managed;

        Assert.Equal((1L, 50_000_000L + 99 + (1 << 20) + 1), tab.CaretPosition());
        Assert.True(grown < 16 << 20, $"managed memory grew by {grown >> 20} MiB");
    }

    [AvaloniaFact]
    public async Task The_scroll_range_of_a_utf8_line_ends_at_its_last_character_not_its_last_byte()
    {
        var tab = OpenLine(new string('ě', 1_500_000)); // two bytes each
        var paged = tab.Paged!;
        paged.ScrollToColumn(1_400_000);
        Draw();
        await Eventually(() => !paged.WindowsPending && paged.CachedWindow(0) is { Length: > 0 });
        await Eventually(() => { Draw(); return paged.HorizontalMaximum is > 1_499_000 and <= 1_500_000; });
        paged.ScrollToColumn(3_000_000);
        Assert.InRange(paged.HorizontalMaximum, 1_499_000, 1_500_000);
    }

    [AvaloniaFact]
    public async Task A_failed_read_of_a_window_is_tried_again_by_the_next_draw_and_not_by_a_loop_of_redraws()
    {
        var tab = OpenLine(new string('a', 3_000_000));
        var paged = tab.Paged!;
        int reads = 0;
        bool fail = true;
        paged.WindowRead = (_, _, _, _) =>
        {
            Interlocked.Increment(ref reads);
            return fail ? throw new IOException("the file is gone") : "OK";
        };

        paged.ScrollToColumn(2_000_000);
        Draw();
        await Eventually(() => Volatile.Read(ref reads) >= 1 && !paged.WindowsPending);
        int first = Volatile.Read(ref reads);
        for (int i = 0; i < 5; i++)
        {
            Draw(); // a failed read that invalidated the view would start its read again in each of these frames
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.InRange(Volatile.Read(ref reads), first, first + 1); // the frame of the first draw may still be asking once more
        Assert.Null(paged.CachedWindow(0));

        fail = false;
        paged.ScrollToColumn(2_000_001); // a change the next draw has to answer
        Draw();
        await Eventually(() => { Draw(); return paged.CachedWindow(0) == "OK"; });
    }

    private static TextInputMethodClient ImeClient(PagedTextView paged)
    {
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        paged.RaiseEvent(args);
        return Assert.IsAssignableFrom<TextInputMethodClient>(args.Client);
    }

    [AvaloniaFact]
    public async Task The_caret_and_a_click_in_a_far_window_follow_the_real_width_of_a_proportional_font_with_wide_characters()
    {
        _app.Workbench.Set("editor.fontFamily", JsonValue.Create("Arial"));
        const string unit = "iiii.WWWW中中中ěš";
        string model = string.Concat(Enumerable.Repeat(unit, 200_000)); // 2.8 million characters
        var tab = OpenLine(model);
        var paged = tab.Paged!;
        int col = unit.Length * 100_000;
        paged.GoToOffset(ByteOffset(model, col));
        Draw();
        await Eventually(() => { Draw(); return !paged.WindowsPending && paged.CachedWindow(0) is { Length: > 0 }; });
        // the jump leaves the caret at the right edge; put it well inside so that the steps below do not scroll
        paged.ScrollToColumn(col - 40);
        Draw();
        await Eventually(() => { Draw(); return !paged.WindowsPending && paged.CachedWindow(0) is { Length: > 0 }; });

        var options = _w.Options;
        double Width(string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(options.FullFontFamily)), options.FontSize, Brushes.Black).WidthIncludingTrailingWhitespace;
        double cell = Width("0");
        Assert.True(Math.Abs(Width(model.Substring(col, 5)) - 5 * cell) > 3, "the sample must be wider or narrower than the fixed cells, or the test proves nothing");

        var client = ImeClient(paged);
        double x0 = client.CursorRectangle.X;
        var steps = new List<(int Columns, Rect Caret)>();
        for (int k = 1; k <= 14; k++)
        {
            TestApp.Press(_w, Key.Right);
            Draw();
            steps.Add((k, client.CursorRectangle));
        }
        Assert.Equal(ByteOffset(model, col + 14), paged.CaretOffset);
        foreach (var (k, caret) in steps)
        {
            double real = Width(model.Substring(col, k));
            Assert.True(Math.Abs(caret.X - x0 - real) < 1.5, $"after {k} columns the caret moved {caret.X - x0:0.0} px, the text is {real:0.0} px wide");
        }

        // a click on the place of a caret puts the caret at that column
        foreach (int k in new[] { 4, 5, 7, 12 })
        {
            var caret = steps[k - 1].Caret;
            var point = client.TextViewVisual.TranslatePoint(new Point(caret.X + 0.2, caret.Y + caret.Height / 2), _w)!.Value;
            _w.MouseDown(point, MouseButton.Left);
            _w.MouseUp(point, MouseButton.Left);
            Assert.Equal(ByteOffset(model, col + k), paged.CaretOffset);
        }
    }
}

/// <summary>
/// P3.1.3 gate: jumps to column 0, 50 000 000 and 90 000 000 of a 100 MiB line, each as the first jump without a column index and as a scroll after the index exists.
/// The time is from the jump until the frame is rendered; a first jump answers with the placeholder frame and the window follows when the read ends (reported, not gated).
/// </summary>
[Collection("LocState")]
[Trait("Category", "Perf")]
public class PagedLongLinePerfTests(ITestOutputHelper output) : IDisposable
{
    private const int Size = 100 * 1024 * 1024;
    private readonly TempDir _temp = new();
    private App? _app;

    public void Dispose()
    {
        if (_app is not null) TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private (MainWindow Window, DocTab Tab) Open()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        string path = Path.Combine(_temp.Path, "line.txt");
        PagedLongLineTests.WriteLongLine(path, Size);
        var tab = w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged!.Index.Complete);
        tab.Paged.Focus();
        Present(w);
        Dispatcher.UIThread.RunJobs();
        return (w, tab);
    }

    private static void Present(MainWindow w)
    {
        w.UpdateLayout();
        w.CaptureRenderedFrame();
    }

    private static double Since(long t0) => Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

    private static (double P50, double P95, double P99) Percentiles(List<double> times)
    {
        times.Sort();
        return (times[times.Count / 2], times[(int)(times.Count * 0.95)], times[(int)(times.Count * 0.99)]);
    }

    private static MappedFileSource SourceOf(DocTab tab) => ((PagedBackend)tab.Doc.Doc.Backend).Paged.Source;

    private static async Task BuildIndex(MainWindow w, DocTab tab, int column)
    {
        var source = SourceOf(tab);
        if (column == 0)
        {
            source.ReadColumns(0, long.MaxValue, 0);
            // one scroll through the UI like the far columns get below, so the first measured jump does not pay for the first paint path
            tab.Paged!.ScrollToColumn(5);
            Present(w);
            return;
        }
        tab.Paged!.ScrollToColumn(column + 5);
        Present(w);
        for (int i = 0; i < 2000 && (tab.Paged.WindowsPending || !source.TryGetLineColumns(0, out _)); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Assert.True(source.TryGetLineColumns(0, out _));
    }

    private void Gate(string name, List<double> times)
    {
        var (p50, p95, p99) = Percentiles(times);
        output.WriteLine($"{name}: p50 {p50:0.0}, p95 {p95:0.0}, p99 {p99:0.0} ms over {times.Count} jumps");
        Assert.True(p95 <= 33 && p99 <= 80, $"{name}: p50 {p50:0.0}, p95 {p95:0.0}, p99 {p99:0.0} ms");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(50_000_000)]
    [InlineData(90_000_000)]
    public async Task First_jump_without_an_index_answers_within_the_budget(int column)
    {
        var (w, tab) = Open();
        var paged = tab.Paged!;
        var source = SourceOf(tab);
        await BuildIndex(w, tab, column); // the first read starts the background build of the index; wait for it, then every jump starts cold

        var first = new List<double>();
        var complete = new List<double>();
        // 100 jumps: with 20 the p95 is simply the worst sample
        for (int i = 0; i < 100; i++)
        {
            source.Columns.Clear();
            long t0 = Stopwatch.GetTimestamp();
            paged.ScrollToColumn(column + 10 + i * 3);
            Present(w);
            first.Add(Since(t0));
            while (paged.WindowsPending) { await Task.Delay(1); Dispatcher.UIThread.RunJobs(); }
            Present(w);
            complete.Add(Since(t0));
            if (column > 0) Assert.NotEmpty(paged.CachedWindow(0) ?? "");
        }
        output.WriteLine($"column {column}: window complete after " + string.Join(", ", complete.Order().Select(v => v.ToString("0.0"))) + " ms");
        Gate($"first jump to column {column}", first);
    }

    /// <summary>P7.1.5 gate: a keystroke in the middle of a 100 MiB line, from the key until the frame is rendered.</summary>
    [AvaloniaTheory]
    [InlineData(50_000_000, false)]
    [InlineData(90_000_000, false)]
    [InlineData(50_000_000, true)]
    public void Typing_and_Backspace_inside_a_100_MiB_line_stay_within_the_budget(int column, bool backspace)
    {
        var (w, tab) = Open();
        var paged = tab.Paged!;
        paged.GoToOffset(column);
        Present(w);
        TestApp.Type(w, new string('w', 400)); // material for Backspace, and the first, cold edits stay out of the measurement
        Present(w);
        var times = new List<double>();
        for (int i = 0; i < 300; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            if (backspace) TestApp.Press(w, Avalonia.Input.Key.Back);
            else TestApp.Type(w, "x");
            Present(w);
            Dispatcher.UIThread.RunJobs();
            times.Add(Since(t0));
        }
        Gate($"{(backspace ? "Backspace" : "typing")} at column {column}", times);
        Assert.InRange(tab.Doc.Doc.UndoDepth, 1, 2); // a run of keystrokes at one place is one undo step
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(50_000_000)]
    [InlineData(90_000_000)]
    public async Task Scrolling_after_the_index_exists_paints_the_window_within_the_budget(int column)
    {
        var (w, tab) = Open();
        var paged = tab.Paged!;
        var source = SourceOf(tab);
        await BuildIndex(w, tab, column);

        var times = new List<double>();
        for (int i = 0; i < 200; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            paged.ScrollToColumn(column + 10 + i * 37 % 5000);
            Present(w);
            times.Add(Since(t0));
            Assert.False(paged.WindowsPending);
            if (column > 0) Assert.NotEmpty(paged.CachedWindow(0) ?? "");
        }
        Gate($"scroll to column {column}", times);
    }
}
