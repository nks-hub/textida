using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>The geometry of the minimap: which lines it draws and where the viewport rectangle sits.</summary>
public class MinimapGeometryTests
{
    [Fact]
    public void A_file_that_fits_is_drawn_whole_with_the_rectangle_at_its_lines()
    {
        var layout = MinimapGeometry.Compute(lines: 100, first: 10, count: 30, height: 800, rowPx: 2);
        Assert.Equal((0, 100, false), (layout.Top, layout.Rows, layout.Surroundings));
        Assert.Equal(20, layout.ThumbTop);
        Assert.Equal(60, layout.ThumbHeight);
    }

    [Fact]
    public void A_file_a_few_screens_long_slides_its_window_with_the_scroll_position()
    {
        long lines = 900; // capacity 400, so up to 1600 lines slide
        var start = MinimapGeometry.Compute(lines, 0, 40, 800, 2);
        var end = MinimapGeometry.Compute(lines, lines - 40, 40, 800, 2);
        Assert.Equal(0, start.Top);
        Assert.Equal(lines - 400, end.Top);
        Assert.Equal(400, start.Rows);
        Assert.Equal(0, start.ThumbTop);
        Assert.Equal(800 - 80, end.ThumbTop);
        Assert.False(start.Surroundings);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(5_000L)]
    [InlineData(99_999_960L)]
    [InlineData(100_000_000L)]
    public void A_huge_file_shows_only_the_surroundings_of_the_viewport(long first)
    {
        long lines = 100_000_000;
        var layout = MinimapGeometry.Compute(lines, first, 40, 800, 2);
        Assert.True(layout.Surroundings);
        Assert.Equal(400, layout.Rows);
        Assert.InRange(layout.Top, 0, lines - 400);
        Assert.InRange(layout.ThumbTop, 0, 800 - layout.ThumbHeight);
        Assert.InRange(MinimapGeometry.LineAt(layout, lines, 2, 0), 0, lines - 1);
    }

    [Fact]
    public void Dragging_the_rectangle_over_its_whole_travel_scrolls_from_the_first_line_to_the_last()
    {
        foreach (long lines in new[] { 100L, 900L, 100_000_000L })
        {
            var layout = MinimapGeometry.Compute(lines, 0, 40, 800, 2);
            long end = MinimapGeometry.FirstAfterDrag(layout, lines, 40, 2, 800, 0, layout.Rows * 2.0 - layout.ThumbHeight);
            long back = MinimapGeometry.FirstAfterDrag(layout, lines, 40, 2, 800, end, -(layout.Rows * 2.0));
            Assert.Equal(0, back);
            if (!layout.Surroundings) Assert.InRange(end, lines - 40 - 2, lines - 40);
            else Assert.InRange(end, 1, 400);
        }
    }

    [Fact]
    public void A_sampled_huge_file_spreads_strips_over_the_whole_file_under_a_thin_pointer()
    {
        long lines = 100_000_000;
        var layout = MinimapGeometry.Compute(lines, 50_000_000, 40, 800, 2, sampled: true);
        Assert.True(layout.Sampled);
        Assert.False(layout.Surroundings);
        Assert.Equal((0L, 400), (layout.Top, layout.Rows));
        Assert.Equal(2, layout.ThumbHeight);
        Assert.Equal(400, layout.ThumbTop, 3);
        // 400 rows in strips of 8: 50 strips, each from the start of its fiftieth of the file
        Assert.Equal(0, MinimapGeometry.LineOfRow(layout, lines, 0, 8));
        Assert.Equal(7, MinimapGeometry.LineOfRow(layout, lines, 7, 8));
        Assert.Equal(2_000_000, MinimapGeometry.LineOfRow(layout, lines, 8, 8));
        Assert.Equal(98_000_007, MinimapGeometry.LineOfRow(layout, lines, 399, 8));
        Assert.Equal(25_000_000, MinimapGeometry.LineAt(layout, lines, 2, 200));
    }

    [Fact]
    public void Sampling_starts_beyond_a_few_screens_and_dragging_a_sampled_map_crosses_the_whole_file()
    {
        Assert.False(MinimapGeometry.Compute(1600, 0, 40, 800, 2, sampled: true).Sampled);
        Assert.False(MinimapGeometry.Compute(100_000_000, 0, 40, 800, 2).Sampled);
        long lines = 100_000_000;
        var layout = MinimapGeometry.Compute(lines, 0, 40, 800, 2, sampled: true);
        Assert.Equal(lines / 2, MinimapGeometry.FirstAfterDrag(layout, lines, 40, 2, 800, 0, 400));
        Assert.Equal(lines - 40, MinimapGeometry.FirstAfterDrag(layout, lines, 40, 2, 800, 0, 1000));
        Assert.Equal(0, MinimapGeometry.FirstAfterDrag(layout, lines, 40, 2, 800, lines / 2, -500));
    }
}

/// <summary>P3.6.1 and P3.3.1 on both surfaces: the view switches, the hidden characters, wrapping, the fonts and the minimap.</summary>
[Collection("LocState")]
public class ViewOptionsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    private readonly ITestOutputHelper _output;

    public ViewOptionsTests(ITestOutputHelper output)
    {
        _output = output;
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _w.Width = 1200;
        _w.Height = 800;
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private void Set(string key, JsonNode? value) => _app.Workbench.Set(key, value);

    private static void Settle(int rounds = 30)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private DocTab Open(string name, string content, bool paged = false)
    {
        if (paged) Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(_temp.File(name, content), null);
        if (paged)
        {
            Assert.True(tab.Doc.IsLarge);
            WaitForIndex(tab);
        }
        _w.UpdateLayout();
        Settle();
        return tab;
    }

    private static void WaitForIndex(DocTab tab, int seconds = 120)
    {
        var clock = Stopwatch.StartNew();
        while (!tab.Paged!.Index.Complete && clock.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged.Index.Complete);
    }

    private sealed record Shot(byte[] Data, int Stride);

    private Shot Capture()
    {
        _w.UpdateLayout();
        Settle(10);
        var frame = _w.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, data, 0, data.Length);
        return new Shot(data, buffer.RowBytes);
    }

    private static int Differences(Shot a, Shot b, Rect area)
    {
        int count = 0;
        for (int y = (int)area.Top; y < (int)area.Bottom; y++)
            for (int x = (int)area.Left; x < (int)area.Right; x++)
            {
                int at = y * a.Stride + x * 4;
                if (at + 3 < a.Data.Length && at + 3 < b.Data.Length && !a.Data.AsSpan(at, 4).SequenceEqual(b.Data.AsSpan(at, 4))) count++;
            }
        return count;
    }

    private static string Changed(Shot a, Shot b, Rect area)
    {
        var seen = new HashSet<string>();
        for (int y = (int)area.Top; y < (int)area.Bottom; y++)
            for (int x = (int)area.Left; x < (int)area.Right; x++)
            {
                int at = y * a.Stride + x * 4;
                if (!a.Data.AsSpan(at, 4).SequenceEqual(b.Data.AsSpan(at, 4)) && seen.Count < 8) seen.Add($"{x},{y}:{b.Data[at + 2]:X2}{b.Data[at + 1]:X2}{b.Data[at]:X2}");
            }
        return string.Join(" ", seen) + " area " + area;
    }

    private static int Matching(Shot shot, Rect area, IEnumerable<Color> colors, int tolerance = 3)
    {
        var wanted = colors.ToList();
        int count = 0;
        for (int y = (int)area.Top; y < (int)area.Bottom; y++)
            for (int x = (int)area.Left; x < (int)area.Right; x++)
            {
                int at = y * shot.Stride + x * 4;
                if (at + 3 >= shot.Data.Length) continue;
                byte b = shot.Data[at], g = shot.Data[at + 1], r = shot.Data[at + 2];
                // the headless frame is RGBA or BGRA depending on the platform: accept both orders
                if (wanted.Any(c => Math.Abs(c.G - g) <= tolerance && ((Math.Abs(c.R - r) <= tolerance && Math.Abs(c.B - b) <= tolerance) || (Math.Abs(c.R - b) <= tolerance && Math.Abs(c.B - r) <= tolerance)))) count++;
            }
        return count;
    }

    private Rect InWindow(Visual visual, Rect local)
    {
        var origin = visual.TranslatePoint(default, _w)!.Value;
        return new Rect(origin.X + local.X, origin.Y + local.Y, local.Width, local.Height);
    }

    /// <summary>The first text line of the editor from the given column to the right edge.</summary>
    private Rect EditorBand(DocTab tab, int fromColumn)
    {
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        var line = view.VisualLines[0];
        double x = fromColumn * view.WideSpaceWidth;
        return InWindow(view, new Rect(x, line.VisualTop - view.ScrollOffset.Y, Math.Max(10, view.Bounds.Width - x), line.Height));
    }

    /// <summary>The first row of the paged view, right of the line numbers and the first two columns.</summary>
    private Rect PagedBand(DocTab tab) => InWindow(tab.Paged!, new Rect(70, 0, 400, 17));

    private static IEnumerable<Color> SyntaxColors(params string[] kinds) =>
        kinds.SelectMany(k => new[] { SyntaxPalette.Light, SyntaxPalette.Dark }.Select(p => ((ISolidColorBrush)p.GetBrush(k)!).Color));

    private static string CodeLines(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"var value{i} = {i}; // line {i}\n"));

    // ---- hidden characters ----

    [AvaloniaFact]
    public void Hidden_characters_are_drawn_over_the_editor_text_and_leave_the_document_alone()
    {
        var tab = Open("a.txt", "a  b\t\tc\r\nsecond line\r\nlast\n");
        string text = tab.Editor!.Document.Text;
        long revision = tab.Doc.Doc.Revision;
        var plain = Capture();
        var band = EditorBand(tab, 1);

        _w.ToggleHiddenCharacters();
        var shown = Capture();

        Assert.True(Differences(plain, shown, band) > 40, "spaces, tab and line end were not drawn");
        Assert.True(tab.Editor.Options.ShowSpaces && tab.Editor.Options.ShowTabs);
        Assert.Contains(tab.Editor.TextArea.TextView.BackgroundRenderers, r => r is LineEndingRenderer { Enabled: true });
        Assert.Equal(text, tab.Editor.Document.Text);
        Assert.Equal(revision, tab.Doc.Doc.Revision);
        Assert.False(tab.Doc.HasUnsavedWork);

        _w.ToggleHiddenCharacters();
        Assert.Equal(0, Differences(plain, Capture(), band));
    }

    [AvaloniaFact]
    public void Line_endings_alone_are_drawn_after_the_last_character_of_each_line()
    {
        var tab = Open("e.txt", "ab\r\ncd\r\n");
        var plain = Capture();
        var band = EditorBand(tab, 2);
        Set("editor.renderLineEndings", JsonValue.Create(true));
        Assert.True(Differences(plain, Capture(), band) > 10);
        Assert.False(tab.Editor!.Options.ShowSpaces);
    }

    [AvaloniaFact]
    public void Hidden_characters_are_drawn_on_the_paged_surface_too()
    {
        var tab = Open("p.txt", "a  b\t\tc\r\nsecond line\r\n" + CodeLines(45_000), paged: true);
        string before = Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new NksHub.Textida.Core.Documents.ByteRange(0, tab.Doc.Doc.ByteLength)));
        long revision = tab.Doc.Doc.Revision;
        var plain = Capture();
        var band = PagedBand(tab);

        _w.ToggleHiddenCharacters();
        var shown = Capture();

        Assert.True(Differences(plain, shown, band) > 40, "spaces, tab and line end were not drawn");
        Assert.Equal(revision, tab.Doc.Doc.Revision);
        Assert.Equal(before, Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new NksHub.Textida.Core.Documents.ByteRange(0, tab.Doc.Doc.ByteLength))));
        _w.ToggleHiddenCharacters();
        Assert.Equal(0, Differences(plain, Capture(), band));
    }

    [AvaloniaFact]
    public void Line_endings_on_the_paged_surface_are_drawn_as_marks()
    {
        var tab = Open("pe.txt", "ab\r\ncd\n" + CodeLines(45_000), paged: true);
        var plain = Capture();
        Set("editor.renderLineEndings", JsonValue.Create(true));
        Assert.True(Differences(plain, Capture(), PagedBand(tab)) > 10, "no CRLF mark after the first line");
        Assert.Equal(("CRLF", "CR", "LF"), (LineEndingRenderer.Label("\r\n"), LineEndingRenderer.Label("\r"), LineEndingRenderer.Label("\n")));
    }

    [AvaloniaFact]
    public void Each_kind_of_line_break_is_named_and_drawn_on_the_paged_surface()
    {
        Set("editor.renderLineEndings", JsonValue.Create(true));
        var marked = new List<(string Ending, Shot Shot)>();
        var paged = new List<DocTab>();
        foreach (string ending in new[] { "\r\n", "\r", "\n" })
        {
            var tab = Open($"eol-{marked.Count}.txt", "ab" + ending + "cd\n" + CodeLines(45_000), paged: true);
            Assert.Equal(ending, tab.Paged!.LineEndingOf(0));
            Assert.Equal("", tab.Paged.LineEndingOf(tab.Paged.Index.Lines - 1));
            marked.Add((ending, Capture()));
            paged.Add(tab);
        }
        var band = PagedBand(paged[^1]); // only the tab on top is in the window
        Assert.True(Differences(marked[0].Shot, marked[1].Shot, band) > 5, "CRLF and CR look alike");
        Assert.True(Differences(marked[1].Shot, marked[2].Shot, band) > 5, "CR and LF look alike");
        Assert.True(Differences(marked[0].Shot, marked[2].Shot, band) > 5, "CRLF and LF look alike");
        Set("editor.renderLineEndings", JsonValue.Create(false));
        var plain = Capture();
        Assert.True(Differences(marked[2].Shot, plain, band) > 5, "no mark for the LF");
    }

    /// <summary>Captures once the window of the long first line at the current sideways position has been read.</summary>
    private Shot CaptureWindow(DocTab tab)
    {
        var paged = tab.Paged!;
        var clock = Stopwatch.StartNew();
        do Capture();
        while ((paged.WindowsPending || paged.CachedWindow(0) is null) && clock.Elapsed.TotalSeconds < 10);
        Assert.NotNull(paged.CachedWindow(0));
        return Capture();
    }

    /// <summary>Opens a paged document whose first line is longer than the decoded part, so its far columns are drawn from windows.</summary>
    private DocTab OpenLong(string name, string content)
    {
        var tab = Open(name, content, paged: true);
        Capture();
        Settle(); // the first draw posts the range of the sideways bar
        return tab;
    }

    [AvaloniaFact]
    public void Hidden_characters_are_drawn_in_the_far_part_of_a_long_line()
    {
        var tab = OpenLong("wide.txt", string.Concat(Enumerable.Repeat("ab  c\t\td ", 6000)) + "\r\nnext\n" + CodeLines(45_000));
        long revision = tab.Doc.Doc.Revision;
        tab.Paged!.ScrollToColumn(40_000);
        var plain = CaptureWindow(tab);
        var band = PagedBand(tab);

        _w.ToggleHiddenCharacters();
        var shown = CaptureWindow(tab);

        Assert.True(Differences(plain, shown, band) > 40, "spaces and tabs beyond the decoded part were not drawn");
        Assert.Equal(revision, tab.Doc.Doc.Revision);
        Assert.False(tab.Doc.HasUnsavedWork);
        _w.ToggleHiddenCharacters();
        Assert.Equal(0, Differences(plain, CaptureWindow(tab), band));
    }

    [AvaloniaFact]
    public void The_line_end_of_a_long_line_is_drawn_after_its_last_column()
    {
        Set("editor.renderLineEndings", JsonValue.Create(true));
        Shot AtEnd(string name, string content)
        {
            var tab = OpenLong(name, content);
            tab.Paged!.ScrollToColumn(double.MaxValue);
            return CaptureWindow(tab);
        }
        string line = new('x', 1_100_000); // above the 1 MiB threshold on its own, so the file opens paged with the long line last
        var ended = AtEnd("ended.txt", line + "\r\n");
        var last = AtEnd("last.txt", line); // the last line has no line end, so nothing is drawn after it
        var lines = _w.Active!.Paged!.Children[0]; // the lines surface without the bars: at the end of the range the mark sits at its right edge
        var row = InWindow(lines, new Rect(0, 0, lines.Bounds.Width, 17));
        Assert.True(Differences(ended, last, row) > 10, "no CRLF mark at the end of the long line");
    }

    [AvaloniaFact]
    public void The_sideways_range_of_a_long_line_leaves_room_for_the_line_end_mark()
    {
        var tab = OpenLong("range.txt", new string('x', 1_100_000) + "\nnext\n" + CodeLines(10));
        Settle();
        var paged = tab.Paged!;
        double plain = paged.HorizontalMaximum;
        Assert.InRange(plain, 1_000_000, 1_100_000);

        Set("editor.renderLineEndings", JsonValue.Create(true));
        Capture();
        Settle();
        Assert.True(paged.HorizontalMaximum > plain + 2, $"room for the line end mark: {plain} -> {paged.HorizontalMaximum}");
    }

    [AvaloniaFact]
    public void The_sideways_range_of_a_long_line_follows_a_change_of_the_font_size()
    {
        var tab = OpenLong("zoom.txt", new string('x', 1_100_000) + "\nnext\n" + CodeLines(10));
        Settle();
        var paged = tab.Paged!;
        double before = paged.HorizontalMaximum;
        Assert.InRange(before, 1_000_000, 1_100_000);

        _w.Zoom(8);
        Capture();
        Settle();
        Assert.True(paged.HorizontalMaximum > before + 5, $"fewer columns fit after the zoom: {before} -> {paged.HorizontalMaximum}");
    }

    // ---- line numbers, current line, margin, links ----

    [AvaloniaFact]
    public void Line_numbers_and_the_current_line_switch_off_on_the_editor_and_the_paged_surface()
    {
        var editor = Open("n.txt", "one\ntwo\nthree\n");
        var gutter = InWindow(editor.Editor!, new Rect(0, 0, 40, 100));
        var withNumbers = Capture();
        var line = EditorBand(editor, 1);

        _w.ToggleLineNumbers();
        Assert.False(editor.Editor.ShowLineNumbers);
        Assert.True(Differences(withNumbers, Capture(), gutter) > 5);
        _w.ToggleLineNumbers();
        Assert.True(editor.Editor.ShowLineNumbers);

        var highlighted = Capture();
        _w.ToggleCurrentLine();
        Assert.False(editor.Editor.Options.HighlightCurrentLine);
        Assert.True(Differences(highlighted, Capture(), line) > 50);
        _w.ToggleCurrentLine();

        var paged = Open("n2.txt", CodeLines(45_000), paged: true);
        var pagedGutter = InWindow(paged.Paged!, new Rect(0, 0, 40, 100));
        var pagedLine = PagedBand(paged);
        var pagedShot = Capture();
        _w.ToggleLineNumbers();
        Assert.True(Differences(pagedShot, Capture(), pagedGutter) > 5);
        _w.ToggleLineNumbers();
        pagedShot = Capture();
        _w.ToggleCurrentLine();
        Assert.True(Differences(pagedShot, Capture(), pagedLine) > 50);
    }

    [AvaloniaFact]
    public void The_right_margin_is_a_vertical_line_on_both_surfaces()
    {
        var editor = Open("m.txt", "short\n");
        var strip = new Rect(0, 400, 1200, 40);
        var before = Capture();
        Set("editor.rightMargin", JsonValue.Create(40));
        Assert.True(Differences(before, Capture(), strip) > 10);
        Set("editor.rightMargin", JsonValue.Create(0));

        var paged = Open("m2.txt", CodeLines(45_000), paged: true);
        before = Capture();
        Set("editor.rightMargin", JsonValue.Create(40));
        Assert.True(Differences(before, Capture(), strip) > 10);
        Assert.NotNull(paged.Paged);
        Assert.NotNull(editor.Editor);
    }

    [AvaloniaFact]
    public void Matching_brackets_are_colored_until_the_highlight_is_switched_off()
    {
        var tab = Open("b.js", "x = f(a, b);\n");
        tab.Editor!.CaretOffset = 5;
        var band = EditorBand(tab, 2);
        var marked = Capture();
        Set("editor.matchBrackets", JsonValue.Create(false));
        Assert.True(Differences(Capture(), marked, band) > 10, "the brackets were not colored");
    }

    [AvaloniaFact]
    public void Links_are_clickable_in_the_editor_until_they_are_switched_off()
    {
        var tab = Open("l.txt", "see https://example.org/page for details\n");
        var view = tab.Editor!.TextArea.TextView;
        bool Linked()
        {
            view.EnsureVisualLines();
            return view.GetVisualLine(1)!.Elements.OfType<VisualLineLinkText>().Any();
        }
        Assert.True(Linked());
        Assert.True(tab.Editor.Options.EnableHyperlinks);
        _w.ToggleLinks();
        Assert.False(tab.Editor.Options.EnableHyperlinks);
        Assert.False(Linked());
    }

    // ---- wrapping ----

    [AvaloniaFact]
    public void Word_wrap_follows_the_setting_but_a_giant_line_is_never_wrapped()
    {
        Set("editor.wordWrap", "on");
        var ordinary = Open("o.txt", new string('x', 3000) + "\n");
        Assert.True(ordinary.Editor!.WordWrap);
        Assert.False(ordinary.Editor.WrapSuppressed);

        var giant = Open("g.txt", new string('y', EditorSurface.WrapLineLimit + 1) + "\n");
        Assert.False(giant.Editor!.WordWrap);
        Assert.True(giant.Editor.WrapSuppressed);

        ordinary.Editor.Document.Insert(0, new string('z', EditorSurface.WrapLineLimit));
        Assert.False(ordinary.Editor.WordWrap);
        Assert.True(ordinary.Editor.WrapSuppressed);

        Set("editor.wordWrap", "off");
        Assert.False(ordinary.Editor.WrapSuppressed);
    }

    [AvaloniaFact]
    public void Toggling_wrap_over_a_giant_line_keeps_the_setting_and_says_why()
    {
        Set("editor.wordWrap", "on");
        var editor = Open("gw.txt", new string('y', EditorSurface.WrapLineLimit + 1) + "\nshort\n").Editor!;
        string WrapSetting() => _app.Workbench.Settings.GetString("editor.wordWrap");
        Assert.True(editor.WrapSuppressed);

        _w.ToggleWordWrap(); // the menu shows wrap off, so the click asks for wrap
        Assert.Equal("on", WrapSetting());
        Assert.False(editor.WordWrap);
        Assert.Equal(Loc.Get(K.ViewWrapSuppressed, ("limit", EditorSurface.WrapLineLimit)), _w.StatusMessageText);

        editor.Document.Remove(0, EditorSurface.WrapLineLimit);
        _w.ToggleWordWrap();
        Assert.True(editor.WordWrap);
        Assert.False(editor.WrapSuppressed);
        Assert.Equal("on", WrapSetting());

        _w.ToggleWordWrap();
        Assert.False(editor.WordWrap);
        Assert.Equal("off", WrapSetting());
    }

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public void A_line_of_100_MiB_opens_unwrapped_on_the_paged_surface_and_draws_its_first_part()
    {
        Set("editor.wordWrap", "on");
        string path = Path.Combine(_temp.Path, "giant.txt");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            var chunk = new byte[1 << 20];
            Array.Fill(chunk, (byte)'a');
            for (int i = 0; i < 100; i++) stream.Write(chunk);
        }
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        Assert.NotNull(tab.Paged);
        Assert.Null(tab.Surface);
        WaitForIndex(tab);
        Assert.Equal(1, tab.Paged!.Index.Lines);
        var clock = Stopwatch.StartNew();
        var shot = Capture();
        Assert.True(clock.Elapsed.TotalSeconds < 5, $"drawing took {clock.Elapsed}");
        Assert.True(Differences(shot, new Shot(new byte[shot.Data.Length], shot.Stride), PagedBand(tab)) > 100);
        Assert.Equal(100L << 20, tab.Doc.Doc.ByteLength);
    }

    // ---- fonts and zoom ----

    [AvaloniaFact]
    public void Fallback_fonts_follow_the_main_font_and_zoom_changes_the_size_setting()
    {
        var tab = Open("z.txt", "text ☃ ❤\n");
        var families = tab.Editor!.FontFamily.FamilyNames.ToList();
        Assert.Equal("Cascadia Mono", families[0]);
        Assert.True(families.IndexOf("Segoe UI Symbol") > families.IndexOf("monospace"), string.Join("|", families));
        Assert.True(FontManager.Current.TryMatchCharacter('☃', FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, tab.Editor.FontFamily, null, out _));

        double size = tab.Editor.FontSize;
        _w.Zoom(2);
        Assert.Equal(size + 2, tab.Editor.FontSize);
        Assert.Equal(SettingSource.User, _app.Workbench.Settings.Resolve("editor.fontSize")!.Source);

        var inside = tab.Editor.TranslatePoint(new Point(300, 200), _w)!.Value;
        _w.MouseWheel(inside, new Vector(0, 1), TestApp.Mod);
        Assert.Equal(size + 3, tab.Editor.FontSize);
        _w.MouseWheel(inside, new Vector(0, -1), TestApp.Mod);
        Assert.Equal(size + 2, tab.Editor.FontSize);

        _w.ZoomReset();
        Assert.Equal(size, tab.Editor.FontSize);

        Set("editor.fallbackFontFamily", "Noto Sans CJK SC");
        Assert.EndsWith("Noto Sans CJK SC", string.Join(", ", tab.Editor.FontFamily.FamilyNames));
    }

    [AvaloniaFact]
    public void A_character_the_main_font_lacks_is_drawn_on_both_surfaces_and_not_as_the_missing_glyph_box()
    {
        const string noFontHasIt = "\U0010FFFD"; // private use: drawn as the missing glyph box of the main font
        foreach (bool paged in new[] { false, true })
        {
            string rest = "\n" + CodeLines(paged ? 45_000 : 2);
            var han = Open($"han-{paged}.txt", "漢" + rest, paged);
            var band = paged ? InWindow(han.Paged!, new Rect(0, 0, 200, 17)) : EditorBand(han, 0);
            var drawn = Capture();
            Open($"box-{paged}.txt", noFontHasIt + rest, paged);
            Assert.True(Differences(drawn, Capture(), band) > 20, $"漢 looks like the missing glyph box (paged: {paged})");
        }
    }

    [AvaloniaFact]
    public void A_character_the_main_font_lacks_comes_from_the_first_listed_font_that_has_it()
    {
        var family = Open("u.txt", "漢 ☃\n").Editor!.FontFamily;
        static bool Has(string name, char c) =>
            FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var g) && g.FamilyName == name && g.CharacterToGlyphMap.ContainsGlyph(c);
        foreach (char c in "漢☃")
        {
            Assert.True(FontManager.Current.TryMatchCharacter(c, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, family, null, out var typeface), c.ToString());
            Assert.True(typeface.GlyphTypeface.CharacterToGlyphMap.ContainsGlyph(c), typeface.GlyphTypeface.FamilyName);
            // a machine without any of the listed fonts falls back to the system; with one installed, the list decides
            if (family.FamilyNames.FirstOrDefault(n => Has(n, c)) is { } listed) Assert.Equal(listed, typeface.GlyphTypeface.FamilyName);
        }
    }

    // ---- commands and settings ----

    [AvaloniaFact]
    public void Every_view_switch_is_a_command_in_the_view_menu_with_a_checked_state_and_a_setting_with_a_source()
    {
        var tab = Open("c.txt", "text\n");
        string[] ids =
        [
            "nks.view.toggleHiddenCharacters", "nks.view.toggleLineNumbers", "nks.view.toggleCurrentLine",
            "nks.view.toggleRightMargin", "nks.view.toggleLinks", "nks.view.toggleMinimap", "nks.view.toggleWordWrap",
        ];
        MenuCommand Item(string id) => FlattenCommands(_w.MenuModel).Single(c => c.CommandId == id);
        bool Checked(string id) => Item(id).CheckedNow?.Invoke() ?? Item(id).Checked ?? false;

        Assert.Equal(SettingSource.Default, _app.Workbench.Settings.Resolve("editor.lineNumbers")!.Source);
        foreach (string id in ids)
        {
            Assert.True(_app.Workbench.Commands.HasHandler(id), id);
            Assert.False(string.IsNullOrWhiteSpace(Item(id).Label), id);
            bool before = Checked(id);
            Item(id).Run();
            Assert.NotEqual(before, Checked(id));
            Item(id).Run();
            Assert.Equal(before, Checked(id));
        }
        _w.ToggleLineNumbers();
        Assert.Equal(SettingSource.User, _app.Workbench.Settings.Resolve("editor.lineNumbers")!.Source);
        Assert.NotNull(tab.Minimap);
    }

    [AvaloniaFact]
    public void The_view_commands_and_settings_have_texts_in_every_language()
    {
        string[] ids = ["toggleHiddenCharacters", "toggleLineNumbers", "toggleCurrentLine", "toggleRightMargin", "toggleLinks", "toggleMinimap"];
        string[] keys = ["fallbackFontFamily", "renderLineEndings", "lineNumbers", "highlightCurrentLine", "links", "rightMargin", "minimap"];
        var seen = new HashSet<string>();
        foreach (string language in Loc.Languages)
        {
            _app.SetLanguage(language);
            var labels = new HashSet<string>();
            foreach (string id in ids) labels.Add(_app.Workbench.Strings.Get("cmd.view." + id));
            foreach (string key in keys)
            {
                labels.Add(_app.Workbench.Strings.Get($"setting.editor.{key}.title"));
                labels.Add(_app.Workbench.Strings.Get($"setting.editor.{key}.description"));
            }
            Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l) || l.Contains("cmd.view.") || l.Contains("setting.editor."), l));
            seen.Add(string.Join("|", labels.Order()));
            Assert.False(string.IsNullOrWhiteSpace(Loc.Get(K.A11yMinimap)));
        }
        Assert.Equal(Loc.Languages.Count, seen.Count); // no language reuses another language's wording wholesale
    }

    private static IEnumerable<MenuCommand> FlattenCommands(IEnumerable<MenuNode> nodes) => nodes.SelectMany(n => n switch
    {
        MenuCommand c => [c],
        MenuGroup g => FlattenCommands(g.Items),
        _ => [],
    });

    // ---- minimap ----

    [AvaloniaFact]
    public void Both_surfaces_have_a_named_minimap_that_the_switch_hides()
    {
        var editor = Open("a.cs", CodeLines(100));
        var paged = Open("b.cs", CodeLines(45_000), paged: true);
        foreach (var tab in new[] { editor, paged })
        {
            Assert.NotNull(tab.Minimap);
            Assert.True(tab.Minimap!.IsVisible);
            Assert.Equal(Loc.Get(K.A11yMinimap), AutomationProperties.GetName(tab.Minimap));
        }
        _w.ToggleMinimap();
        Assert.False(editor.Minimap!.IsVisible);
        Assert.False(paged.Minimap!.IsVisible);
        Assert.Null(_w.NewDocument().Hex);
    }

    private static string KeywordFile(int lines) =>
        string.Concat(Enumerable.Range(0, lines).Select(i => $"var value{i} = {i}; // line {i}\n"));

    private Rect MinimapLowerPart(DocTab tab)
    {
        var map = tab.Minimap!;
        return InWindow(map, new Rect(2, map.Bounds.Height * 0.65, map.Bounds.Width - 12, map.Bounds.Height * 0.3));
    }

    private bool WaitForColors(DocTab tab, string[] kinds)
    {
        var colors = SyntaxColors(kinds).ToList();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 10)
        {
            if (Matching(Capture(), MinimapLowerPart(tab), colors) > 20) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_minimap_of_the_editor_draws_the_text_in_the_syntax_colors()
    {
        var tab = Open("m.cs", KeywordFile(3000));
        Assert.True(WaitForColors(tab, ["keyword", "comment"]), "no keyword or comment colors in the minimap");
        Assert.True(tab.Minimap!.Bounds.Width >= Minimap.LogicalWidth - 1);
    }

    [AvaloniaFact]
    public void The_minimap_of_the_paged_surface_draws_the_text_in_the_syntax_colors()
    {
        var tab = Open("m2.cs", KeywordFile(120_000), paged: true);
        Assert.True(WaitForColors(tab, ["keyword", "comment"]), "no keyword or comment colors in the minimap");
    }

    [AvaloniaFact]
    public void Clicking_and_dragging_the_minimap_scrolls_the_editor()
    {
        var tab = Open("d.cs", KeywordFile(3000));
        var view = tab.Editor!.TextArea.TextView;
        int First() { view.EnsureVisualLines(); return view.VisualLines[0].FirstDocumentLine.LineNumber; }
        Assert.Equal(1, First());
        var map = tab.Minimap!;
        var origin = map.TranslatePoint(default, _w)!.Value;
        var press = new Point(origin.X + 40, origin.Y + map.Bounds.Height * 0.6);

        _w.MouseDown(press, MouseButton.Left);
        _w.UpdateLayout();
        int clicked = First();
        Assert.InRange(clicked, 100, 500);

        _w.MouseMove(new Point(press.X, press.Y + 100));
        _w.UpdateLayout();
        int dragged = First();
        Assert.True(dragged > clicked + 20, $"{clicked} -> {dragged}");
        _w.MouseUp(new Point(press.X, press.Y + 100), MouseButton.Left);
        _w.MouseMove(new Point(press.X, press.Y + 300));
        _w.UpdateLayout();
        Assert.Equal(dragged, First());
    }

    [AvaloniaFact]
    public void Clicking_the_sampled_minimap_of_the_paged_surface_scrolls_to_that_share_of_the_file()
    {
        var tab = Open("d2.cs", KeywordFile(120_000), paged: true);
        var map = tab.Minimap!;
        Assert.True(map.IsSampled);
        var origin = map.TranslatePoint(default, _w)!.Value;
        Assert.Equal(0, tab.Paged!.TopLine);
        _w.MouseDown(new Point(origin.X + 40, origin.Y + map.Bounds.Height * 0.6), MouseButton.Left);
        _w.MouseUp(new Point(origin.X + 40, origin.Y + map.Bounds.Height * 0.6), MouseButton.Left);
        Assert.InRange(tab.Paged.TopLine, 66_000, 74_000);
    }

    [AvaloniaFact]
    public void The_sampled_minimap_of_a_large_file_shows_its_end_while_the_top_is_on_screen()
    {
        string text = string.Concat(Enumerable.Range(0, 60_000).Select(i => $"// only a comment on line {i}\n"))
            + string.Concat(Enumerable.Range(0, 60_000).Select(i => $"var value{i} = {i};\n"));
        var tab = Open("halves.cs", text, paged: true);
        Assert.Equal(0, tab.Paged!.TopLine);
        Assert.True(tab.Minimap!.IsSampled);
        // the lower part of the picture comes from the second half of the file, which the surroundings of the top would never show
        Assert.True(WaitForColors(tab, ["keyword"]), "no keyword colors in the lower part of the map");
    }

    [AvaloniaFact]
    public void The_minimap_of_a_log_draws_its_lines_in_the_colors_of_their_levels()
    {
        var tab = Open("levels.log", string.Concat(Enumerable.Range(0, 3000).Select(i => $"2026-01-01 ERROR request {i} failed\n")));
        Assert.True(tab.Doc.IsLog);
        Assert.True(WaitForColors(tab, ["log.error"]), "no error color in the minimap of the log");
    }

    [AvaloniaFact]
    public void The_map_of_a_paged_utf16_file_reads_its_lines()
    {
        string path = Path.Combine(_temp.Path, "wide.cs");
        // above the threshold of 1 MiB, so the file opens paged
        string text = string.Concat(Enumerable.Range(0, 50_000).Select(i => $"line {i} ěšč\r\n"));
        File.WriteAllBytes(path, [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)]);
        Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        WaitForIndex(tab);
        var host = (IMinimapHost)tab.Paged!;
        Assert.Equal("line 0 ěšč", host.LineText(0));
        Assert.Equal("line 1234 ěšč", host.LineText(1234));
        Assert.Equal("line 49999 ěšč", host.LineText(49_999));
        Assert.Equal("line 1 ěšč", host.LineText(1));
    }

    [AvaloniaFact]
    public void The_map_of_a_paged_utf32_file_reads_its_lines()
    {
        string path = Path.Combine(_temp.Path, "wide32.cs");
        string text = string.Concat(Enumerable.Range(0, 30_000).Select(i => $"line {i} ěšč\n"));
        File.WriteAllBytes(path, [.. Encoding.UTF32.GetPreamble(), .. Encoding.UTF32.GetBytes(text)]);
        Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        WaitForIndex(tab);
        var host = (IMinimapHost)tab.Paged!;
        Assert.Equal("line 0 ěšč", host.LineText(0));
        Assert.Equal("line 1234 ěšč", host.LineText(1234));
        Assert.Equal("line 29999 ěšč", host.LineText(29_999));
        Assert.Equal("line 1 ěšč", host.LineText(1));
    }

    [AvaloniaFact]
    public void Marks_in_the_strip_are_drawn_over_the_whole_file()
    {
        var tab = Open("k.cs", KeywordFile(3000));
        var strip = InWindow(tab.Minimap!, new Rect(tab.Minimap!.Bounds.Width - 5, 0, 4, tab.Minimap.Bounds.Height));
        var before = Capture();
        tab.Minimap.SetMarks([new MinimapMark(2900, MinimapMarkKind.Error), new MinimapMark(1500, MinimapMarkKind.Warning)]);
        var after = Capture();
        Assert.True(Differences(before, after, strip) > 4);
        Assert.True(Matching(after, strip, [Color.Parse("#E5484D")], 4) > 0, Changed(before, after, strip));
        Assert.True(Matching(after, strip, [Color.Parse("#E2A400")], 4) > 0);
    }

    [AvaloniaFact]
    public void Filter_marks_share_the_strip_with_the_find_marks()
    {
        var tab = Open("fm.cs", KeywordFile(3000));
        var map = tab.Minimap!;
        var strip = InWindow(map, new Rect(map.Bounds.Width - 5, 0, 4, map.Bounds.Height));
        var red = Color.Parse("#E5484D");
        map.SetMarks([new MinimapMark(500, MinimapMarkKind.Match)]);
        map.SetFilterMarks([new MinimapMark(2500, MinimapMarkKind.Error)]);
        var shot = Capture();
        Assert.True(Matching(shot, strip, [red], 4) > 0, "filter mark");
        Assert.True(Matching(shot, strip, [Color.Parse("#3B82F6")], 4) > 0, "find mark");
        map.SetFilterMarks([]);
        Assert.Equal(0, Matching(Capture(), strip, [red], 4));
    }

    private static uint PixelAt(Shot shot, int x, int y) => BitConverter.ToUInt32(shot.Data, y * shot.Stride + x * 4);

    /// <summary>Whether the minimap tints its right edge inside the viewport rectangle: the picture of the text never reaches that edge, so below the rectangle it is bare.</summary>
    private bool DrawsViewportRectangle(DocTab tab)
    {
        var map = tab.Minimap!;
        var shot = Capture();
        var origin = map.TranslatePoint(default, _w)!.Value;
        int x = (int)(origin.X + map.Bounds.Width) - 1;
        return PixelAt(shot, x, (int)origin.Y + 2) != PixelAt(shot, x, (int)origin.Y + 300);
    }

    [AvaloniaFact]
    public void The_viewport_rectangle_is_drawn_only_for_a_file_that_does_not_fit_on_the_screen()
    {
        foreach (bool paged in new[] { false, true })
        {
            // the paged surface starts above 1 MiB: three lines of 400 kB still fit on the screen
            string fewLines = paged ? string.Join('\n', Enumerable.Repeat(new string('x', 400_000), 3)) + "\n" : "var a = 1;\nvar b = 2;\n";
            var fits = Open($"fits-{paged}.cs", fewLines, paged);
            Assert.False(DrawsViewportRectangle(fits), $"a file that fits has nothing to scroll to (paged: {paged})");
            string manyLines = paged ? string.Concat(Enumerable.Repeat(new string('y', 3000) + "\n", 400)) : KeywordFile(400);
            var longer = Open($"longer-{paged}.cs", manyLines, paged);
            Assert.True(DrawsViewportRectangle(longer), $"the rectangle of a file that scrolls is missing (paged: {paged})");
        }
    }

    [AvaloniaFact]
    public void The_left_edge_of_the_minimap_is_a_line_in_the_border_color_of_the_theme()
    {
        var tab = Open("edge.cs", KeywordFile(400));
        var map = tab.Minimap!;
        Assert.True(map.TryFindResource("Border", map.ActualThemeVariant, out var found) && found is ISolidColorBrush);
        var origin = map.TranslatePoint(default, _w)!.Value;
        var edge = new Rect(origin.X, origin.Y + 200, 1, 40);
        Assert.Equal(40, Matching(Capture(), edge, [((ISolidColorBrush)found!).Color], 2));
    }

    [AvaloniaFact]
    public void Dragging_the_pointer_of_the_sampled_minimap_moves_the_paged_surface_by_the_share_of_the_file()
    {
        var tab = Open("sd.cs", KeywordFile(120_000), paged: true);
        var map = tab.Minimap!;
        Assert.True(map.IsSampled);
        long lines = ((IMinimapHost)tab.Paged!).LineCount;
        var origin = map.TranslatePoint(default, _w)!.Value;
        var start = new Point(origin.X + 40, origin.Y + 1); // on the pointer at the top, so the press itself scrolls nothing
        _w.MouseDown(start, MouseButton.Left);
        Assert.Equal(0, tab.Paged.TopLine);

        int height = (int)map.Bounds.Height, rowPx = Math.Max(2, (int)Math.Ceiling(height / 500.0));
        double mapped = height / rowPx * rowPx;
        _w.MouseMove(new Point(start.X, start.Y + 200));
        _w.UpdateLayout();
        long expected = (long)Math.Round(200 * lines / mapped);
        Assert.InRange(tab.Paged.TopLine, expected * 0.95, expected * 1.05);

        _w.MouseMove(new Point(start.X, start.Y + 100));
        _w.UpdateLayout();
        Assert.InRange(tab.Paged.TopLine, expected / 2 * 0.95, expected / 2 * 1.05);
        _w.MouseUp(new Point(start.X, start.Y + 100), MouseButton.Left);
    }

    [AvaloniaFact]
    public void The_sampled_minimap_of_a_paged_log_draws_its_lines_in_the_colors_of_their_levels()
    {
        var tab = Open("levels-big.log", string.Concat(Enumerable.Range(0, 60_000).Select(i => $"2026-01-01 ERROR request {i} failed\n")), paged: true);
        Assert.True(tab.Doc.IsLog);
        Assert.True(tab.Minimap!.IsSampled);
        Assert.True(WaitForColors(tab, ["log.error"]), "no error color in the sampled map of the log");
    }

    [AvaloniaFact]
    public void The_sampled_minimap_reads_a_few_blocks_of_the_file_and_not_the_file()
    {
        var tab = Open("blocks.cs", KeywordFile(400_000), paged: true);
        Assert.True(tab.Minimap!.IsSampled);
        Assert.True(WaitForColors(tab, ["keyword", "comment"]), "no colors in the map");
        Settle();
        long file = tab.Doc.Doc.ByteLength;
        Assert.True(file > 12_000_000, $"{file} bytes");
        Assert.InRange(tab.Paged!.MapBytesRead, 1, file / 4);
    }

    [AvaloniaFact]
    public void The_heatmap_of_a_log_is_drawn_in_the_strip_and_goes_when_it_is_removed()
    {
        var tab = Open("heat.cs", KeywordFile(3000));
        var map = tab.Minimap!;
        var strip = InWindow(map, new Rect(map.Bounds.Width - 5, 0, 4, map.Bounds.Height));
        var (red, yellow) = (Color.Parse("#E5484D"), Color.Parse("#E2A400"));
        var (errors, warnings, lines) = (new int[100], new int[100], Enumerable.Repeat(10, 100).ToArray());
        errors[70] = 10; // the densest bucket draws in full strength
        warnings[20] = 5;
        Assert.Equal(0, Matching(Capture(), strip, [red, yellow], 4));

        map.SetHeat(new LogHeatmap(errors, warnings, lines, sampled: false));
        var shot = Capture();
        Rect Bucket(int bucket) => InWindow(map, new Rect(map.Bounds.Width - 5, (int)(bucket * map.Bounds.Height / 100) - 1, 4, 3));
        Assert.True(Matching(shot, Bucket(70), [red], 4) > 0, "no error heat at its bucket");
        Assert.Equal(0, Matching(shot, Bucket(70), [yellow], 4));
        Assert.True(Matching(shot, Bucket(20), [yellow], 4) > 0, "no warning heat at its bucket");
        Assert.Equal(0, Matching(shot, Bucket(20), [red], 4));
        Assert.Equal(0, Matching(shot, Bucket(40), [red, yellow], 4)); // a bucket without a count stays bare

        map.SetHeat(null);
        Assert.Equal(0, Matching(Capture(), strip, [red, yellow], 4));
    }

    [AvaloniaFact]
    public void In_a_row_of_the_strip_the_more_severe_mark_wins_whichever_came_first()
    {
        var tab = Open("sev.cs", KeywordFile(3000));
        var map = tab.Minimap!;
        var strip = InWindow(map, new Rect(map.Bounds.Width - 5, 0, 4, map.Bounds.Height));
        var (red, yellow, blue) = (Color.Parse("#E5484D"), Color.Parse("#E2A400"), Color.Parse("#3B82F6"));

        // two neighbouring lines that fall into one row of the strip, whatever its height is
        long lines = tab.Editor!.Document.LineCount, height = (long)map.Bounds.Height;
        long line = Enumerable.Range(2400, 100).First(l => l * height / lines == (l + 1) * height / lines);
        map.SetMarks([new MinimapMark(line, MinimapMarkKind.Match)]);
        map.SetFilterMarks([new MinimapMark(line + 1, MinimapMarkKind.Error)]);
        var shot = Capture();
        Assert.True(Matching(shot, strip, [red], 4) > 0, "error over match");
        Assert.Equal(0, Matching(shot, strip, [blue], 4));

        map.SetMarks([new MinimapMark(line, MinimapMarkKind.Error)]);
        map.SetFilterMarks([new MinimapMark(line + 1, MinimapMarkKind.Warning)]);
        shot = Capture();
        Assert.True(Matching(shot, strip, [red], 4) > 0, "error over warning");
        Assert.Equal(0, Matching(shot, strip, [yellow], 4));

        map.SetMarks([new MinimapMark(line, MinimapMarkKind.Match)]);
        map.SetFilterMarks([new MinimapMark(line + 1, MinimapMarkKind.Warning)]);
        shot = Capture();
        Assert.True(Matching(shot, strip, [yellow], 4) > 0, "warning over match");
        Assert.Equal(0, Matching(shot, strip, [blue], 4));
    }

    [AvaloniaFact]
    public async Task Find_matches_are_marked_in_the_strip_and_the_marks_go_with_the_bar()
    {
        var tab = Open("f.cs", KeywordFile(3000));
        var strip = InWindow(tab.Minimap!, new Rect(tab.Minimap!.Bounds.Width - 5, 0, 4, tab.Minimap.Bounds.Height));
        var blue = Color.Parse("#3B82F6");
        Assert.Equal(0, Matching(Capture(), strip, [blue], 4));
        TestApp.Press(_w, Key.F, TestApp.Mod);
        _w.FindBar.FindBox.Text = "// line 2500";
        Dispatcher.UIThread.RunJobs();
        for (var clock = Stopwatch.StartNew(); _w.FindIsRunning && clock.ElapsedMilliseconds < 10_000;)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(Matching(Capture(), strip, [blue], 4) > 0, "the match is not marked");
        _w.HideFind();
        Assert.Equal(0, Matching(Capture(), strip, [blue], 4));
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void Dragging_the_minimap_moves_the_editor_within_the_budget()
    {
        var tab = Open("p.cs", KeywordFile(20_000));
        var map = tab.Minimap!;
        var origin = map.TranslatePoint(default, _w)!.Value;
        var start = new Point(origin.X + 40, origin.Y + 100);
        _w.MouseDown(start, MouseButton.Left);
        var times = new List<double>();
        for (int i = 0; i < 200; i++)
        {
            var clock = Stopwatch.StartNew();
            _w.MouseMove(new Point(start.X, start.Y + i * 2.5));
            _w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            times.Add(clock.Elapsed.TotalMilliseconds);
        }
        _w.MouseUp(new Point(start.X, start.Y + 500), MouseButton.Left);
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        Assert.True(p95 <= 33, $"p95 {p95:0.0} ms");
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void Dragging_the_sampled_minimap_moves_the_paged_surface_within_the_budget()
    {
        var tab = Open("pp.cs", KeywordFile(400_000), paged: true);
        var map = tab.Minimap!;
        Assert.True(map.IsSampled);
        var origin = map.TranslatePoint(default, _w)!.Value;
        var start = new Point(origin.X + 40, origin.Y + 2);
        _w.MouseDown(start, MouseButton.Left);
        var times = new List<double>();
        List<double> moves = [], layouts = [], jobs = [];
        long before = tab.Paged!.TopLine;
        for (int i = 0; i < 200; i++)
        {
            var clock = Stopwatch.StartNew();
            _w.MouseMove(new Point(start.X, start.Y + i * 2.5));
            double afterMove = clock.Elapsed.TotalMilliseconds;
            _w.UpdateLayout();
            double afterLayout = clock.Elapsed.TotalMilliseconds;
            Dispatcher.UIThread.RunJobs();
            times.Add(clock.Elapsed.TotalMilliseconds);
            moves.Add(afterMove);
            layouts.Add(afterLayout - afterMove);
            jobs.Add(times[^1] - afterLayout);
        }
        _w.MouseUp(new Point(start.X, start.Y + 500), MouseButton.Left);
        Assert.True(tab.Paged.TopLine > before + 100_000, $"{before} -> {tab.Paged.TopLine}");
        static string Spread(List<double> v) { var s = v.Order().ToArray(); return $"p50 {s[s.Length / 2]:0.0}, p95 {s[(int)(s.Length * 0.95)]:0.0}"; }
        _output.WriteLine($"sampled minimap drag, parts: move [{Spread(moves)}], layout [{Spread(layouts)}], jobs [{Spread(jobs)}] ms");
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        _output.WriteLine($"sampled minimap drag: p50 {times[times.Count / 2]:0.0}, p95 {p95:0.0}, max {times[^1]:0.0} ms");

        // where the time goes: the spans over a second drag back (the measured one above runs without them)
        _w.MouseDown(new Point(start.X, start.Y + 500), MouseButton.Left);
        NksHub.Textida.Core.TypingTrace.Reset();
        NksHub.Textida.Core.TypingTrace.Enabled = true;
        try
        {
            for (int i = 0; i < 200; i++)
            {
                _w.MouseMove(new Point(start.X, start.Y + 500 - i * 2.5));
                _w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally { NksHub.Textida.Core.TypingTrace.Enabled = false; }
        _w.MouseUp(new Point(start.X, start.Y), MouseButton.Left);
        _output.WriteLine("sampled minimap drag, spans of 200 moves:\n" + NksHub.Textida.Core.TypingTrace.Summary());
        NksHub.Textida.Core.TypingTrace.Reset();
        Assert.True(p95 <= 33, $"p95 {p95:0.0} ms");
    }

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public void A_ten_gibibyte_file_gets_a_sampled_map_without_reading_the_file()
    {
        string path = Path.Combine(_temp.Path, "huge.js");
        const long length = 10L << 30;
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(path, []);
            var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string arg in new[] { "sparse", "setflag", path }) command.ArgumentList.Add(arg);
            using var process = Process.Start(command)!;
            process.WaitForExit();
        }
        using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write))
        {
            stream.SetLength(length);
            stream.Position = 0;
            stream.Write(Encoding.UTF8.GetBytes(KeywordFile(5000)));
        }
        Set("files.largeFileThreshold", JsonValue.Create(1));
        var clock = Stopwatch.StartNew();
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        Assert.True(WaitForColors(tab, ["keyword", "comment"]), "no colors in the map of the 10 GiB file");
        Assert.True(tab.Minimap!.IsSampled);
        Assert.True(clock.Elapsed.TotalSeconds < 60, $"took {clock.Elapsed}");
        Assert.Equal(length, tab.Doc.Doc.ByteLength);
        Assert.InRange(tab.Paged!.MapBytesRead, 1, 64L << 20);
    }
}
