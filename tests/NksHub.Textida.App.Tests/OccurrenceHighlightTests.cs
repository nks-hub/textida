using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.12: the other occurrences of the selected text are highlighted, counted and marked, on both text surfaces.</summary>
[Collection("LocState")]
public class OccurrenceHighlightTests : IDisposable
{
    private const string Beta = "alpha beta\nbeta gamma\nBeta delta beta\n";

    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public OccurrenceHighlightTests()
    {
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

    private void Set(string key, JsonNode? value)
    {
        _app.Workbench.Set(key, value);
        Dispatcher.UIThread.RunJobs();
    }

    private DocTab Open(string name, string content, bool paged = false)
    {
        if (paged) Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(_temp.File(name, content), null);
        if (paged)
        {
            Assert.True(tab.Doc.IsLarge);
            for (var clock = Stopwatch.StartNew(); !tab.Paged!.Index.Complete && clock.Elapsed.TotalSeconds < 60;)
            {
                Thread.Sleep(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(tab.Paged.Index.Complete);
        }
        _w.UpdateLayout();
        Pump();
        return tab;
    }

    private static void Pump(int rounds = 10)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    /// <summary>Looks for the occurrences now instead of after the delay and waits for the count.</summary>
    private static async Task Settled(OccurrenceTracker tracker)
    {
        var scan = tracker.SettleAsync();
        for (var clock = Stopwatch.StartNew(); !scan.IsCompleted && clock.ElapsedMilliseconds < 10_000;)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(scan.IsCompleted, "the scan did not end");
        await scan;
    }

    private static async Task<OccurrenceScan> Select(DocTab tab, int start, int end)
    {
        tab.Editor!.SetSelections([new Sel(start, end)]);
        await Settled(tab.Editor.Occurrences);
        return tab.Editor.Occurrences.Result;
    }

    private static long[] Lines(OccurrenceScan scan) => [.. scan.Lines];

    // ---- what is found ----

    [AvaloniaFact]
    public async Task Selecting_text_finds_the_other_occurrences_counts_them_and_the_status_bar_says_so()
    {
        var tab = Open("a.txt", Beta);
        var scan = await Select(tab, 6, 10);
        var target = tab.Editor!.Occurrences.Target;
        Assert.Equal(new OccurrenceTarget(new OccurrenceQuery("beta", MatchCase: true, WholeWord: false), 6), target);
        Assert.Equal(2, scan.Count); // "Beta" is another text with the case on; the selection itself is not an other occurrence
        Assert.Equal([1L, 2L], Lines(scan));
        Assert.False(scan.InView);
        Assert.False(scan.Limited);
        Assert.Contains(Loc.Plural(K.StatusOccurrences, 2), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public async Task Whole_words_and_case_change_what_is_found_as_soon_as_the_setting_changes()
    {
        var tab = Open("w.txt", "cat concat cat catalog\n");
        var scan = await Select(tab, 0, 3);
        Assert.Equal(3, scan.Count); // inside concat and catalog too

        Set("editor.occurrences.wholeWord", JsonValue.Create(true));
        Assert.Null(tab.Editor!.Occurrences.Target); // dropped at once, looked for again after the delay
        await Settled(tab.Editor.Occurrences);
        Assert.True(tab.Editor.Occurrences.Target!.Value.Query.WholeWord);
        Assert.Equal(1, tab.Editor.Occurrences.Result.Count);
        Assert.Equal([0L], Lines(tab.Editor.Occurrences.Result));
        Set("editor.occurrences.wholeWord", JsonValue.Create(false));

        var cased = Open("c.txt", "Foo foo FOO\n");
        Assert.Equal(0, (await Select(cased, 0, 3)).Count); // the case counts by default
        Set("editor.occurrences.matchCase", JsonValue.Create(false));
        await Settled(cased.Editor!.Occurrences);
        Assert.Equal(2, cased.Editor.Occurrences.Result.Count);
        Assert.False(cased.Editor.Occurrences.Target!.Value.Query.MatchCase);
    }

    [AvaloniaFact]
    public async Task A_selection_that_is_too_short_spans_lines_or_is_only_white_space_highlights_nothing()
    {
        var tab = Open("m.txt", "a b a a\nxx yy xx\n   z   \n");
        var surface = tab.Editor!;
        await Select(tab, 0, 1);
        Assert.Null(surface.Occurrences.Target); // shorter than the minimum of 2

        Set("editor.occurrences.minLength", JsonValue.Create(1));
        await Settled(surface.Occurrences);
        Assert.Equal(2, surface.Occurrences.Result.Count);

        await Select(tab, 4, 11); // "a a", the line break and "xx"
        Assert.Null(surface.Occurrences.Target);
        Assert.Equal(0, surface.Occurrences.Result.Count);
        Assert.Empty(surface.Occurrences.Result.Lines);

        await Select(tab, 17, 20); // the three spaces in front of the z
        Assert.Null(surface.Occurrences.Target);

        Set("editor.occurrences.minLength", JsonValue.Create(3));
        await Select(tab, 8, 10); // "xx" is shorter than the raised minimum
        Assert.Null(surface.Occurrences.Target);
    }

    [AvaloniaFact]
    public async Task A_selection_over_several_lines_is_not_looked_for_even_when_the_text_repeats()
    {
        var tab = Open("n.txt", "one\ntwo\none\ntwo\n");
        await Select(tab, 0, 7);
        Assert.Null(tab.Editor!.Occurrences.Target);
        Assert.Equal(0, tab.Editor.Occurrences.Result.Count);
        Assert.DoesNotContain(Loc.Plural(K.StatusOccurrences, 1), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public async Task The_word_under_the_caret_is_highlighted_only_when_the_setting_is_on_and_only_as_a_whole_word()
    {
        var tab = Open("p.txt", "foo bar foo\nfoobar foo\n");
        var surface = tab.Editor!;
        surface.SetSelections([Sel.At(1)]);
        await Settled(surface.Occurrences);
        Assert.Null(surface.Occurrences.Target); // off by default

        Set("editor.occurrences.atCaret", JsonValue.Create(true));
        await Settled(surface.Occurrences);
        var target = surface.Occurrences.Target!.Value;
        Assert.Equal(("foo", true, 0L), (target.Query.Needle, target.Query.WholeWord, target.Own)); // whole words, though the setting for them is off
        Assert.Equal(2, surface.Occurrences.Result.Count); // not the one in foobar
        Assert.Equal([0L, 1L], Lines(surface.Occurrences.Result));

        surface.SetSelections([Sel.At(3)]); // behind the word, still touching it
        await Settled(surface.Occurrences);
        Assert.Equal("foo", surface.Occurrences.Target!.Value.Query.Needle);
        surface.SetSelections([Sel.At(4)]); // between the space and "bar": the word bar
        await Settled(surface.Occurrences);
        Assert.Equal("bar", surface.Occurrences.Target!.Value.Query.Needle);
        Assert.Equal(0, surface.Occurrences.Result.Count);
    }

    [AvaloniaFact]
    public async Task Switching_the_feature_off_removes_what_is_shown_and_shows_nothing_afterwards()
    {
        var tab = Open("o.txt", Beta);
        await Select(tab, 6, 10);
        Assert.NotNull(tab.Editor!.Occurrences.Target);
        Set("editor.occurrences.enabled", JsonValue.Create(false));
        Assert.Null(tab.Editor.Occurrences.Target);
        Assert.Empty(tab.Editor.Occurrences.Result.Lines);
        Assert.DoesNotContain(Loc.Plural(K.StatusOccurrences, 2), _w.StatusLeftText);
        await Select(tab, 11, 15);
        Assert.Null(tab.Editor.Occurrences.Target);
        Set("editor.occurrences.enabled", JsonValue.Create(true));
        await Settled(tab.Editor.Occurrences);
        Assert.NotNull(tab.Editor.Occurrences.Target);
    }

    [AvaloniaFact]
    public async Task The_other_occurrences_of_a_secret_value_are_not_shown_while_values_are_hidden()
    {
        var tab = Open(".env", "TOKEN=abcdef123\nOTHER=abcdef123\n");
        Assert.True(tab.Editor!.SecretsHidden);
        await Select(tab, 6, 15);
        Assert.Null(tab.Editor.Occurrences.Target);
        Assert.Empty(tab.Editor.Occurrences.Result.Lines);
    }

    // ---- timing ----

    [AvaloniaFact]
    public async Task A_change_of_the_selection_drops_the_highlight_at_once_and_looks_again_after_the_delay()
    {
        var tab = Open("t.txt", Beta);
        var occurrences = tab.Editor!.Occurrences;
        occurrences.Debounce = TimeSpan.FromMilliseconds(20);
        await Select(tab, 6, 10);
        Assert.NotNull(occurrences.Target);

        tab.Editor.SetSelections([new Sel(0, 5)]); // "alpha"
        Assert.Null(occurrences.Target);
        Assert.Empty(occurrences.Result.Lines);
        Assert.True(occurrences.Pending);

        for (var clock = Stopwatch.StartNew(); occurrences.Target is null && clock.ElapsedMilliseconds < 5000;)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal("alpha", occurrences.Target!.Value.Query.Needle);
        Assert.False(occurrences.Pending);
    }

    [AvaloniaFact]
    public async Task A_selection_that_keeps_changing_is_looked_for_once_when_it_stops()
    {
        var tab = Open("d.txt", Beta);
        var occurrences = tab.Editor!.Occurrences;
        occurrences.Debounce = TimeSpan.FromMilliseconds(60);
        int changes = 0;
        occurrences.Changed += () => changes++;
        for (int end = 7; end <= 10; end++)
        {
            tab.Editor.SetSelections([new Sel(6, end)]);
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Null(occurrences.Target); // not settled yet
        for (var clock = Stopwatch.StartNew(); (occurrences.Target is null || !occurrences.Scanning.IsCompleted) && clock.ElapsedMilliseconds < 5000;)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal("beta", occurrences.Target!.Value.Query.Needle);
        Assert.Equal(1, changes); // one result, none for the half-made selections
    }

    [AvaloniaFact]
    public async Task Typing_without_a_selection_neither_waits_nor_searches_and_typing_over_a_selection_drops_the_highlight()
    {
        var tab = Open("y.txt", Beta);
        var occurrences = tab.Editor!.Occurrences;
        tab.Editor.TextArea.Focus();
        tab.Editor.CaretOffset = 0;
        TestApp.Type(_w, "xyz");
        Dispatcher.UIThread.RunJobs();
        Assert.False(occurrences.Pending);
        Assert.Null(occurrences.Target);
        Assert.True(occurrences.Scanning.IsCompleted);

        await Select(tab, 9, 13); // "beta" of the first line, moved by the typing
        Assert.Equal("beta", tab.Editor.Document.GetText(9, 4));
        Assert.NotNull(occurrences.Target);
        TestApp.Type(_w, "q");
        Assert.Null(occurrences.Target);
        Assert.Empty(occurrences.Result.Lines);
        await Settled(occurrences);
        Assert.Null(occurrences.Target); // nothing is selected any more
    }

    [AvaloniaFact]
    public async Task A_text_change_elsewhere_drops_the_highlight_and_it_comes_back_for_the_new_text()
    {
        var tab = Open("e.txt", Beta);
        await Select(tab, 6, 10);
        Assert.Equal(2, tab.Editor!.Occurrences.Result.Count);
        tab.Editor.Document.Insert(tab.Editor.Document.TextLength, "beta\n");
        Assert.Null(tab.Editor.Occurrences.Target);
        await Settled(tab.Editor.Occurrences);
        Assert.Equal(3, tab.Editor.Occurrences.Result.Count);
    }

    // ---- what is drawn ----

    private Color Backdrop(string key = "BgEditor") => ThemeColor(_w.ActualThemeVariant, key);

    private static Color ThemeColor(ThemeVariant variant, string key)
    {
        Assert.True(Application.Current!.TryFindResource(key, variant, out var value), key);
        return ((ISolidColorBrush)value!).Color;
    }

    private static Color Blend(Color top, Color under)
    {
        double a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    private sealed record Shot(byte[] Data, int Stride);

    private Shot Capture()
    {
        _w.UpdateLayout();
        Pump(6);
        var frame = _w.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, data, 0, data.Length);
        return new Shot(data, buffer.RowBytes);
    }

    private static int Matching(Shot shot, Rect area, Color color, int tolerance = 4)
    {
        int count = 0;
        for (int y = (int)area.Top; y < (int)area.Bottom; y++)
            for (int x = (int)area.Left; x < (int)area.Right; x++)
            {
                int at = y * shot.Stride + x * 4;
                if (at + 3 >= shot.Data.Length) continue;
                byte b = shot.Data[at], g = shot.Data[at + 1], r = shot.Data[at + 2];
                // the headless frame is RGBA or BGRA depending on the platform: accept both orders
                if (Math.Abs(color.G - g) <= tolerance && ((Math.Abs(color.R - r) <= tolerance && Math.Abs(color.B - b) <= tolerance) || (Math.Abs(color.R - b) <= tolerance && Math.Abs(color.B - r) <= tolerance))) count++;
            }
        return count;
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

    private Rect InWindow(Visual visual, Rect local)
    {
        var origin = visual.TranslatePoint(default, _w)!.Value;
        return new Rect(origin.X + local.X, origin.Y + local.Y, local.Width, local.Height);
    }

    private Rect LineBand(DocTab tab, int index)
    {
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        var line = view.VisualLines[index];
        return InWindow(view, new Rect(0, line.VisualTop - view.ScrollOffset.Y, view.Bounds.Width - 8, line.Height));
    }

    [AvaloniaFact]
    public async Task The_other_occurrences_get_a_background_that_the_selection_and_the_current_line_do_not_have()
    {
        var tab = Open("draw.txt", Beta);
        var fill = Blend(Backdrop("OccurrenceBg"), Backdrop());
        await Select(tab, 6, 10);
        var shot = Capture();
        Assert.True(Matching(shot, LineBand(tab, 1), fill) > 20, "the second line has no highlight");
        Assert.True(Matching(shot, LineBand(tab, 2), fill) > 20, "the third line has no highlight");
        Assert.Equal(0, Matching(shot, LineBand(tab, 0), fill)); // the selection is not an other occurrence

        Set("editor.occurrences.enabled", JsonValue.Create(false));
        Assert.Equal(0, Matching(Capture(), LineBand(tab, 1), fill));
    }

    [AvaloniaFact]
    public async Task Only_the_matching_case_is_drawn_when_the_case_counts()
    {
        var tab = Open("case.txt", "Foo\nfoo\nFoo\n");
        var fill = Blend(Backdrop("OccurrenceBg"), Backdrop());
        await Select(tab, 0, 3);
        var shot = Capture();
        Assert.Equal(0, Matching(shot, LineBand(tab, 1), fill));
        Assert.True(Matching(shot, LineBand(tab, 2), fill) > 10);
    }

    [AvaloniaFact]
    public async Task The_lines_are_marked_at_the_rows_of_the_strip_and_of_the_minimap()
    {
        string text = string.Concat(Enumerable.Range(0, 3000).Select(i => i is 100 or 1500 or 2900 ? "needle here\n" : $"filler text {i}\n"));
        var tab = Open("marks.txt", text);
        var surface = tab.Editor!;
        int first = surface.Document.GetLineByNumber(101).Offset;
        var scan = await Select(tab, first, first + 6);
        Assert.Equal([1500L, 2900L], Lines(scan));

        var map = tab.Minimap!;
        int height = (int)Math.Round(map.Bounds.Height);
        var expectedRows = new[] { (int)(1500L * height / 3000), (int)(2900L * height / 3000) };
        Assert.Equal(expectedRows, OccurrenceStrip.Rows(scan.Lines, surface.Document.LineCount, height));

        var on = Capture();
        Set("editor.occurrences.enabled", JsonValue.Create(false));
        var off = Capture();
        foreach (int row in expectedRows)
            Assert.True(Differences(on, off, InWindow(map, new Rect(map.Bounds.Width - 4, row, 3, 1))) > 0, $"the minimap has no mark at row {row}");
        var view = surface.TextArea.TextView;
        var stripRows = OccurrenceStrip.Rows(scan.Lines, surface.Document.LineCount, (int)view.Bounds.Height);
        Assert.Equal(2, stripRows.Count);
        foreach (int row in stripRows)
            Assert.True(Differences(on, off, InWindow(view, new Rect(view.Bounds.Width - 4, row, 3, 1))) > 0, $"the strip beside the scrollbar has no mark at row {row}");
        int bare = expectedRows[0] + 40; // a row without a mark
        Assert.Equal(0, Differences(on, off, InWindow(map, new Rect(map.Bounds.Width - 4, bare, 3, 1))));
    }

    [AvaloniaFact]
    public async Task The_minimap_of_a_paged_file_marks_the_same_lines_as_the_count()
    {
        var tab = Open("pm.txt", Pad(""), paged: true);
        var paged = tab.Paged!;
        paged.SelectBytes(6, 10);
        await Settled(paged.Occurrences);
        var host = (IMinimapHost)paged;
        Assert.Equal(paged.Occurrences.Result.Lines, host.OccurrenceLines);
        Assert.NotEmpty(host.OccurrenceLines);
    }

    // ---- the large file ----

    /// <summary>The head and 400 lines of "alpha beta N" padded to 3000 characters: over the 1 MiB from which a file is shown paged.</summary>
    private static string Pad(string head) => head + string.Concat(Enumerable.Range(0, 400).Select(i => $"alpha beta {i} {new string('x', 3000)}\n"));

    [AvaloniaFact]
    public async Task The_paged_view_looks_at_the_lines_on_screen_only_and_looks_again_after_a_scroll()
    {
        var tab = Open("big.txt", Pad(""), paged: true);
        var paged = tab.Paged!;
        paged.SelectBytes(6, 10);
        await Settled(paged.Occurrences);
        var scan = paged.Occurrences.Result;
        Assert.True(scan.InView);
        Assert.True(scan.Count is > 5 and < 100, $"count {scan.Count}"); // the lines on screen, not the 399 of the file
        Assert.Equal(1, scan.Lines[0]);
        Assert.Equal(scan.Count, scan.Lines.Count);
        Assert.Contains(Loc.Plural(K.StatusOccurrencesView, scan.Count), _w.StatusLeftText);

        var fill = Blend(Backdrop("OccurrenceBg"), Backdrop());
        Assert.True(Matching(Capture(), InWindow(paged, new Rect(70, 20, 400, 10)), fill) > 5, "the second row has no highlight");

        ((IMinimapHost)paged).ScrollToLine(200);
        Pump();
        await Settled(paged.Occurrences);
        scan = paged.Occurrences.Result;
        Assert.Equal(200, scan.Lines[0]);
        Assert.True(scan.Count is > 5 and < 100);
        Assert.NotNull(paged.Occurrences.Target); // the text looked for stayed
    }

    [AvaloniaFact]
    public async Task The_paged_view_follows_the_options_and_does_not_look_for_a_selection_over_lines()
    {
        var tab = Open("pw.txt", Pad("cat concat cat\ncat\n"), paged: true);
        var paged = tab.Paged!;
        paged.SelectBytes(0, 3);
        await Settled(paged.Occurrences);
        Assert.Equal(3, paged.Occurrences.Result.Count);
        Set("editor.occurrences.wholeWord", JsonValue.Create(true));
        await Settled(paged.Occurrences);
        Assert.Equal(2, paged.Occurrences.Result.Count);
        paged.SelectBytes(0, 18); // over the line break
        await Settled(paged.Occurrences);
        Assert.Null(paged.Occurrences.Target);
    }

    // ---- the screen reader ----

    [AvaloniaFact]
    public async Task The_count_is_announced_once_per_interval_and_never_for_no_occurrence()
    {
        var tab = Open("sr.txt", "ab ab\ncd cd\nef gh\n");
        var occurrences = tab.Editor!.Occurrences;
        occurrences.AnnounceInterval = TimeSpan.FromHours(1);
        var told = new List<string>();
        occurrences.Announce += scan => told.Add(scan.Describe()!);

        await Select(tab, 0, 2);
        Assert.Equal([Loc.Plural(K.StatusOccurrences, 1)], told);
        Assert.Equal(Loc.Plural(K.StatusOccurrences, 1), _w.AnnouncedText);
        Assert.Equal(AutomationLiveSetting.Polite, _w.AnnouncedUrgency);

        await Select(tab, 6, 8); // throttled
        Assert.Single(told);
        occurrences.AnnounceInterval = TimeSpan.Zero;
        await Select(tab, 12, 14); // "ef": no other occurrence, nothing to say
        Assert.Single(told);
        await Select(tab, 0, 2);
        Assert.Equal(2, told.Count);
    }

    [AvaloniaTheory]
    [InlineData("cs")]
    [InlineData("en")]
    [InlineData("sk")]
    [InlineData("de")]
    public void The_sentences_read_naturally_in_every_language_for_every_kind_of_count(string language)
    {
        Loc.SetLanguage(language);
        foreach (long count in new long[] { 1, 2, 5, 21, 100 })
            foreach (var scan in new[] { new OccurrenceScan([], count), new OccurrenceScan([], count, InView: true) })
            {
                string text = scan.Describe()!;
                Assert.Contains(count.ToString(), text);
                Assert.DoesNotContain('{', text);
            }
        Assert.Contains("20000", new OccurrenceScan([], 20000, Limited: true).Describe()!);
        Assert.Null(OccurrenceScan.None.Describe());
    }

    // ---- colors and settings ----

    [AvaloniaFact]
    public void The_colors_differ_from_the_selection_the_current_line_and_the_other_highlights_in_light_and_dark()
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var ground = ThemeColor(variant, "BgEditor");
            var fill = Blend(ThemeColor(variant, "OccurrenceBg"), ground);
            foreach (var other in new[] { "SelectionBg", "CurrentLine", "Highlight", "BracketMatch" })
            {
                var seen = Blend(ThemeColor(variant, other), ground);
                int distance = Math.Abs(fill.R - seen.R) + Math.Abs(fill.G - seen.G) + Math.Abs(fill.B - seen.B);
                Assert.True(distance >= 30, $"{variant}: the occurrences look like {other} ({distance})");
            }
            Assert.True(ThemeColor(variant, "OccurrenceBorder").A > 0);
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void In_high_contrast_the_occurrences_have_their_own_fill_and_an_outline_of_the_text_colour(bool dark)
    {
        var colors = dark ? NksHub.Textida.App.Shell.ContrastColors.Black : NksHub.Textida.App.Shell.ContrastColors.White;
        var palette = colors.Brushes;
        foreach (var other in new[] { "SelectionBg", "CurrentLine", "Highlight", "BracketMatch" })
            Assert.NotEqual(palette["OccurrenceBg"], palette[other]);
        Assert.Equal(colors.WindowText, palette["OccurrenceBorder"]);
        Assert.True(NksHub.Textida.App.Shell.ContrastColors.Ratio(palette["EditorFg"], palette["OccurrenceBg"]) >= 4.5, "text on the occurrences does not read");
    }

    [AvaloniaFact]
    public void The_settings_exist_with_the_defaults_of_the_placeholders_and_can_be_searched()
    {
        var workbench = _app.Workbench;
        Assert.Equal(new OccurrenceOptions(Enabled: true, WholeWord: false, MatchCase: true, AtCaret: false, MinLength: PluginLimitsPlaceholders.OccurrenceMinLength), OccurrenceOptions.From(workbench.Settings));
        Assert.Equal(2, PluginLimitsPlaceholders.OccurrenceMinLength);
        foreach (string key in new[] { "enabled", "wholeWord", "matchCase", "atCaret", "minLength" })
            Assert.Contains(workbench.Host.Settings.Definitions, d => d.Key == "editor.occurrences." + key);
        // by a word of the tags and by a word of the title, which the Czech interface writes without diacritics
        foreach (string query in new[] { "occurrence", "vyskyty" })
        {
            var hits = workbench.SearchIndex.Search(query, workbench.Settings, workbench.Commands, workbench.Keymap);
            Assert.Contains(hits, hit => hit.Id == "editor.occurrences.enabled");
        }
    }
}

/// <summary>P3.6.12 gate: the highlight must not slow typing or redrawing; numbers are those of the other typing gates. Headless runs measure CPU work, not the compositor.</summary>
[Collection("LocState")]
[Trait("Category", "Perf")]
public class OccurrenceHighlightPerfTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir _temp = new();
    private App? _app;

    public void Dispose()
    {
        if (_app is not null) TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private (MainWindow Window, DocTab Tab) OpenMiB()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 1 << 20; i++) sb.Append("line ").Append(i).Append(" lorem ipsum dolor sit amet consectetur\n");
        var tab = w.OpenFile(_temp.File("one-mib.txt", sb.ToString()), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        w.UpdateLayout();
        return (w, tab);
    }

    private static (double P50, double P95, double P99) Percentiles(List<double> times)
    {
        times.Sort();
        return (times[times.Count / 2], times[(int)(times.Count * 0.95)], times[(int)(times.Count * 0.99)]);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Typing_with_the_highlight_on_stays_within_the_typing_budget(bool wordAtCaret)
    {
        var (w, tab) = OpenMiB();
        _app!.Workbench.Set("editor.occurrences.atCaret", JsonValue.Create(wordAtCaret));
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.CaretOffset = 5;
        for (int i = 0; i < 20; i++) { TestApp.Type(w, "w"); Dispatcher.UIThread.RunJobs(); } // warm up

        // Up to 3 rounds like the other gates: a busy runner spoils one round, a regression spoils all.
        var rounds = new List<string>();
        double p95 = double.MaxValue, p99 = double.MaxValue;
        for (int round = 0; round < 3 && !(p95 <= 33 && p99 <= 80); round++)
        {
            var times = new List<double>();
            for (int i = 0; i < 200; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                TestApp.Type(w, "x");
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            }
            var (p50, q95, q99) = Percentiles(times);
            (p95, p99) = (q95, q99);
            rounds.Add($"p50 {p50:0.0}, p95 {p95:0.0}, p99 {p99:0.0} ms");
            output.WriteLine($"word at caret {wordAtCaret}, round {round + 1}: {rounds[^1]}");
        }
        Assert.True(p95 <= 33 && p99 <= 80, $"word at caret {wordAtCaret}: " + string.Join("; ", rounds));
    }

    [AvaloniaFact]
    public async Task Redrawing_a_screen_full_of_occurrences_stays_within_a_frame()
    {
        var (w, tab) = OpenMiB();
        tab.Editor!.SetSelections([new Sel(0, 4)]); // "line": on every line of the screen
        var scan = tab.Editor.Occurrences.SettleAsync();
        for (var clock = Stopwatch.StartNew(); !scan.IsCompleted && clock.ElapsedMilliseconds < 30_000;)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Editor.Occurrences.Result.Count > 10_000, "the scan of the whole file did not find the lines");
        var times = new List<double>();
        for (int i = 0; i < 100; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            tab.Editor.ScrollToVerticalOffset(tab.Editor.VerticalOffset + 17);
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        }
        var (p50, p95, p99) = Percentiles(times);
        output.WriteLine($"scroll with the highlight: p50 {p50:0.0}, p95 {p95:0.0}, p99 {p99:0.0} ms");
        Assert.True(p95 <= 33, $"p95 {p95:0.0} ms");
    }
}
