using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.6.3: the peek at a change of the Git gutter, reverting a change, stepping through the changes and the marks beside the text. What Git says is tested with the plugin (<c>GitPluginTests</c>).</summary>
[Collection("LocState")]
public class GitPeekTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public GitPeekTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        _app.Contrast.Restore();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static void Settle(int rounds = 20)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private DocTab Open(string text, string name = "g.cs")
    {
        var tab = _w.OpenFile(_temp.File(name, text), null);
        _w.UpdateLayout();
        Settle();
        return tab;
    }

    private static string Lines(int count) => string.Concat(Enumerable.Range(1, count).Select(i => $"line {i}\n"));

    private static GitMark Mark(int line, int lines, GitMarkKind kind = GitMarkKind.Modified, bool staged = false, int oldLines = 1) => new(line, lines, kind, staged, 0, oldLines);

    private static GitPeekActions Actions(List<string> log) => new(() => log.Add("revert"), () => log.Add("stage"), () => log.Add("previous"), () => log.Add("next"), () => log.Add("close"));

    private GitPeek PeekOf(DocTab tab, GitMark mark, bool endsWithNewline = true, bool truncated = false, params string[] old) =>
        new(tab.Doc.Doc.Revision, mark, old, endsWithNewline, truncated);

    private static string Text(DocTab tab) => tab.Surface!.Document.Text;

    // ---- the peek ----

    [AvaloniaFact]
    public void The_peek_lies_under_the_last_line_of_the_change_follows_the_scrolling_and_hides_when_the_line_is_out_of_the_view()
    {
        var tab = Open(Lines(120));
        var surface = tab.Surface!;
        var mark = Mark(10, 2);
        surface.SetGitMarks([mark]);
        surface.ShowGitPeek(PeekOf(tab, mark, true, false, "old a", "old b"), 0, 1, Actions([]), focus: false);
        _w.UpdateLayout();
        Settle();

        var peek = surface.PeekView!;
        var view = surface.TextArea.TextView;
        view.EnsureVisualLines();
        Assert.True(peek.IsVisible);
        Assert.IsType<OverlayLayer>(peek.GetVisualParent());
        double under = view.GetVisualTopByDocumentLine(13) - view.VerticalOffset; // the 1-based line under the change (lines 11 and 12 changed)
        Assert.InRange(peek.TranslatePoint(default, view)!.Value.Y, under - 1, under + 1);
        Assert.Equal(PeekRows(peek), peek.Rows.Select(r => r.Text).ToArray());

        surface.ScrollToLine(100);
        _w.UpdateLayout();
        Settle();
        Assert.False(peek.IsVisible); // the change is not on the screen
        surface.ScrollToLine(1);
        _w.UpdateLayout();
        Settle();
        Assert.True(peek.IsVisible);
        Assert.InRange(peek.TranslatePoint(default, view)!.Value.Y, under - 1, under + 1);
        Assert.Equal(0, surface.CaretOffset); // the peek never moves the caret
    }

    private static string[] PeekRows(GitPeekView peek) => [.. peek.RowControls.Select(g => string.Concat(g.Children.OfType<TextBlock>().Last().Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)))];

    [AvaloniaFact]
    public void The_peek_shows_the_old_lines_in_red_and_the_new_lines_in_green_with_signs_and_the_changed_words_marked()
    {
        var tab = Open("a\nb\nc\nlet total = 25;\nd\n");
        var surface = tab.Surface!;
        var mark = Mark(3, 1);
        surface.SetGitMarks([mark]);
        surface.ShowGitPeek(PeekOf(tab, mark, true, false, "let total = 10;"), 0, 1, Actions([]), focus: false);
        _w.UpdateLayout();
        Settle();

        var peek = surface.PeekView!;
        Assert.Equal([true, false], peek.Rows.Select(r => r.Old));
        Assert.Equal(["let total = 10;", "let total = 25;"], peek.Rows.Select(r => r.Text));
        var rows = peek.RowControls;
        Assert.Equal(2, rows.Count);
        Assert.Equal(["−", "+"], rows.Select(g => g.Children.OfType<TextBlock>().First().Text)); // the kind is told by a sign, not by the color alone
        Assert.Equal(Brush(peek, "DiffRemoved"), Assert.IsAssignableFrom<ISolidColorBrush>(rows[0].Background).Color);
        Assert.Equal(Brush(peek, "DiffAdded"), Assert.IsAssignableFrom<ISolidColorBrush>(rows[1].Background).Color);
        var oldRuns = rows[0].Children.OfType<TextBlock>().Last().Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
        var marked = oldRuns.Where(r => r.Background is not null).ToList();
        Assert.Equal(["10"], marked.Select(r => r.Text));
        Assert.Equal(Brush(peek, "DiffRemovedWord"), Assert.IsAssignableFrom<ISolidColorBrush>(marked[0].Background).Color);
        Assert.Equal("let total = 10;", string.Concat(oldRuns.Select(r => r.Text)));
    }

    private static Color Brush(Control control, string key)
    {
        Assert.True(control.TryFindResource(key, control.ActualThemeVariant, out var value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    [AvaloniaFact]
    public void The_peek_has_named_buttons_and_the_description_of_the_change_and_stage_becomes_unstage_for_a_staged_change()
    {
        Loc.SetLanguage("en");
        var tab = Open(Lines(30));
        var surface = tab.Surface!;
        var mark = Mark(4, 3, oldLines: 2);
        surface.SetGitMarks([mark, Mark(10, 1)]);
        surface.ShowGitPeek(PeekOf(tab, mark, true, false, "o1", "o2"), 0, 2, Actions([]), focus: false);
        var peek = surface.PeekView!;

        Assert.Equal("Change 1 of 2 · 3 changed lines (before: 2), not staged, against the index", peek.HeaderText);
        Assert.Equal("Change in Git: 3 changed lines (before: 2), not staged, against the index", AutomationProperties.GetName(peek));
        Assert.Equal(["Revert", "Stage", "Previous", "Next", "Close"], new[] { peek.RevertButton, peek.StageButton, peek.PreviousButton, peek.NextButton, peek.CloseButton }.Select(b => (string)b.Content!));
        Assert.All(new[] { peek.RevertButton, peek.StageButton, peek.PreviousButton, peek.NextButton, peek.CloseButton }, b => Assert.Equal((string)b.Content!, AutomationProperties.GetName(b)));
        Assert.True(peek.RevertButton.IsEnabled);

        var staged = Mark(4, 3, staged: true, oldLines: 2);
        surface.SetGitMarks([staged]);
        surface.ShowGitPeek(PeekOf(tab, staged, true, false, "o1", "o2"), 0, 1, Actions([]), focus: false);
        peek = surface.PeekView!;
        Assert.Equal("Unstage", (string)peek.StageButton.Content!);
        Assert.False(peek.RevertButton.IsEnabled); // a change in the index is not taken back in the text
        Assert.False(peek.PreviousButton.IsEnabled && peek.NextButton.IsEnabled); // the only change of the file

        Loc.SetLanguage("cs");
        peek.UpdateTexts();
        Assert.Equal("Vrátit z indexu", (string)peek.StageButton.Content!);
        Assert.StartsWith("Změna v Gitu: 3 změněné řádky (dříve 2), v indexu", AutomationProperties.GetName(peek));
    }

    [AvaloniaFact]
    public void A_change_longer_than_the_answer_says_so_and_cannot_be_reverted_from_the_peek()
    {
        Loc.SetLanguage("en");
        var tab = Open(Lines(30));
        var surface = tab.Surface!;
        var mark = Mark(4, 3, oldLines: 9);
        surface.SetGitMarks([mark]);
        surface.ShowGitPeek(PeekOf(tab, mark, true, true, "o1", "o2"), 0, 1, Actions([]), focus: false);
        var peek = surface.PeekView!;

        Assert.False(peek.RevertButton.IsEnabled);
        Assert.Contains(peek.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Only the first 2 lines of the change are shown; it cannot be reverted from here.");
    }

    [AvaloniaFact]
    public void The_buttons_do_what_the_window_gave_them_and_close_gives_the_focus_back_to_the_text()
    {
        var tab = Open(Lines(30));
        var surface = tab.Surface!;
        var mark = Mark(4, 1);
        var log = new List<string>();
        surface.SetGitMarks([mark, Mark(9, 1)]);
        surface.ShowGitPeek(PeekOf(tab, mark, true, false, "o"), 0, 2, Actions(log), focus: true);
        Settle();
        var peek = surface.PeekView!;
        Assert.True(peek.IsKeyboardFocusWithin); // the keyboard command puts the focus in the peek

        foreach (var button in new[] { peek.RevertButton, peek.StageButton, peek.PreviousButton, peek.NextButton })
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["revert", "stage", "previous", "next"], log);

        peek.CloseButton.Focus();
        peek.CloseButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle();
        Assert.Equal("close", log[^1]);
        Assert.Null(surface.PeekView);
        Assert.True(surface.TextArea.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void Esc_closes_the_peek_from_the_text_and_from_the_peek_and_so_do_an_edit_and_marks_without_the_change()
    {
        var tab = Open(Lines(30));
        var surface = tab.Surface!;
        var mark = Mark(4, 1);
        var log = new List<string>();
        void Show() => surface.ShowGitPeek(PeekOf(tab, mark, true, false, "o"), 0, 1, Actions(log), focus: false);
        surface.SetGitMarks([mark]);

        Show();
        surface.TextArea.Focus();
        TestApp.Press(_w, Key.Escape);
        Assert.Null(surface.PeekView);

        Show();
        surface.PeekView!.CloseButton.Focus();
        TestApp.Press(_w, Key.Escape);
        Assert.Null(surface.PeekView);
        Assert.Equal("close", log[^1]); // the window hears of it, to give the text its focus

        Show();
        surface.Document.Insert(0, "x");
        Settle();
        Assert.Null(surface.PeekView); // the change is not true any more

        Show();
        surface.SetGitMarks([]);
        Assert.Null(surface.PeekView);
    }

    // ---- reverting ----

    [AvaloniaFact]
    public void Reverting_a_changed_block_puts_the_old_lines_back_as_one_undo_step()
    {
        var tab = Open(Lines(10));
        var surface = tab.Surface!;
        string before = Text(tab);
        var mark = Mark(2, 2, oldLines: 3);
        Assert.True(surface.RevertGitChange(PeekOf(tab, mark, true, false, "first old", "second old", "third old")));
        Assert.Equal("line 1\nline 2\nfirst old\nsecond old\nthird old\nline 5\nline 6\nline 7\nline 8\nline 9\nline 10\n", Text(tab));

        _w.Undo();
        Assert.Equal(before, Text(tab));
    }

    [AvaloniaFact]
    public void Reverting_an_added_block_deletes_its_lines_and_reverting_a_removal_puts_the_lines_back_between_the_neighbours()
    {
        var tab = Open(Lines(6));
        var surface = tab.Surface!;
        Assert.True(surface.RevertGitChange(PeekOf(tab, Mark(1, 2, GitMarkKind.Added, oldLines: 0), true, false)));
        Assert.Equal("line 1\nline 4\nline 5\nline 6\n", Text(tab));

        Assert.True(surface.RevertGitChange(PeekOf(tab, Mark(1, 0, GitMarkKind.Removed, oldLines: 2), true, false, "gone one", "gone two")));
        Assert.Equal("line 1\ngone one\ngone two\nline 4\nline 5\nline 6\n", Text(tab));

        // lines at the end of a text without a final line break
        var last = Open("a\nb\nc", "end.txt");
        var end = last.Surface!;
        Assert.True(end.RevertGitChange(PeekOf(last, Mark(2, 1, oldLines: 1), false, false, "old")));
        Assert.Equal("a\nb\nold", end.Document.Text);
        Assert.True(end.RevertGitChange(PeekOf(last, Mark(2, 1, GitMarkKind.Added, oldLines: 0), true, false)));
        Assert.Equal("a\nb", end.Document.Text);
        Assert.True(end.RevertGitChange(PeekOf(last, Mark(2, 0, GitMarkKind.Removed, oldLines: 1), false, false, "c")));
        Assert.Equal("a\nb\nc", end.Document.Text);
    }

    [AvaloniaFact]
    public void Lines_put_back_use_the_line_break_of_the_text_around_them()
    {
        var tab = Open("one\r\ntwo\r\nthree\r\n", "crlf.txt");
        Assert.True(tab.Surface!.RevertGitChange(PeekOf(tab, Mark(1, 1, oldLines: 2), true, false, "x", "y")));
        Assert.Equal("one\r\nx\r\ny\r\nthree\r\n", Text(tab));
    }

    [AvaloniaFact]
    public void A_change_is_not_reverted_in_a_read_only_text_when_it_is_cut_when_it_is_in_the_index_or_when_the_text_is_not_the_one_it_was_asked_for()
    {
        var tab = Open(Lines(10));
        var surface = tab.Surface!;
        string before = Text(tab);
        var mark = Mark(2, 1);

        Assert.False(surface.RevertGitChange(PeekOf(tab, mark, true, true, "o")));
        Assert.False(surface.RevertGitChange(PeekOf(tab, Mark(2, 1, staged: true), true, false, "o")));
        Assert.False(surface.RevertGitChange(new GitPeek(tab.Doc.Doc.Revision - 1, mark, ["o"], true, false)));
        Assert.False(surface.RevertGitChange(PeekOf(tab, Mark(9, 5), true, false, "o"))); // the lines are not there
        surface.IsReadOnly = true;
        Assert.False(surface.RevertGitChange(PeekOf(tab, mark, true, false, "o")));
        Assert.Equal(before, Text(tab));
    }

    // ---- the marks beside the text ----

    [AvaloniaFact]
    public void The_minimap_draws_the_changes_on_the_rows_of_their_lines()
    {
        var tab = Open(Lines(120));
        var map = tab.Minimap!;
        GitMark[] marks = [Mark(3, 4), Mark(40, 0, GitMarkKind.Removed), Mark(60, 2, GitMarkKind.Added, oldLines: 0), Mark(200, 1)];
        map.SetChangeMarks(marks);
        var layout = map.Layout!.Value;

        var bars = map.ChangeBars();
        Assert.Equal(3, bars.Count); // the change below the end of the file has no row
        Assert.Equal([(3, 4), (40, 1), (60, 2)], bars.Select(b => ((int)(b.Mark.Line - layout.Top), b.Rows)));
        map.SetChangeMarks([]);
        Assert.Empty(map.ChangeBars());
    }

    [AvaloniaFact]
    public void The_changes_are_in_the_gutter_the_minimap_and_the_strip_beside_the_scroll_bar_at_once()
    {
        var tab = Open(Lines(80));
        var surface = tab.Surface!;
        int raised = 0;
        surface.GitMarksChanged += () => raised++;
        GitMark[] marks = [Mark(3, 4), Mark(40, 2, GitMarkKind.Added, oldLines: 0)];
        surface.SetGitMarks(marks);
        Assert.Equal(marks, surface.GitMarks);
        Assert.Equal(1, raised);
        Assert.Equal(marks, surface.TextArea.LeftMargins.OfType<GitMargin>().Single().Marks);
        surface.SetGitMarks(null);
        Assert.Empty(surface.GitMarks);
        Assert.Equal(2, raised);
    }

    [AvaloniaFact]
    public void The_gutter_is_named_for_a_screen_reader_in_every_language_and_is_wide_enough_to_be_seen()
    {
        var tab = Open(Lines(5));
        var margin = tab.Surface!.TextArea.LeftMargins.OfType<GitMargin>().Single();
        Assert.True(GitMargin.MarginWidth >= 8);
        foreach (string language in Loc.Languages)
        {
            Loc.SetLanguage(language);
            margin.UpdateName();
            Assert.Equal(Loc.Get(K.A11yGitGutter), AutomationProperties.GetName(margin));
            Assert.NotEqual(K.A11yGitGutter, AutomationProperties.GetName(margin));
        }
    }

    [AvaloniaFact]
    public void The_colours_of_the_changes_are_tokens_of_the_theme_for_every_variant_and_one_colour_in_high_contrast()
    {
        var tab = Open(Lines(5));
        var margin = tab.Surface!;
        foreach (var variant in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
            foreach (var key in new[] { "GitGutterAdded", "GitGutterModified", "GitGutterRemoved", "DiffAddedWord", "DiffRemovedWord" })
                Assert.True(_app.TryFindResource(key, variant, out var value) && value is ISolidColorBrush, $"{variant}: {key}");

        _app.Contrast.Apply(ContrastColors.Black);
        Dispatcher.UIThread.RunJobs();
        var colors = new[] { "GitGutterAdded", "GitGutterModified", "GitGutterRemoved" }.Select(k => Brush(margin, k)).Distinct().ToList();
        Assert.Equal([ContrastColors.Black.HotLight], colors);
        Assert.All(colors, c => Assert.True(ContrastColors.Ratio(c, ContrastColors.Black.Window) >= 3));
        _app.Contrast.Restore();
    }

    // ---- stepping through the changes ----

    [AvaloniaFact]
    public void The_commands_step_to_the_next_and_the_previous_change_round_the_file_and_say_when_there_is_none()
    {
        Loc.SetLanguage("en");
        var tab = Open(Lines(60));
        var surface = tab.Surface!;
        int Line() => (int)surface.CaretLineColumn().Line - 1;

        _w.GoToGitChange(forward: true);
        Assert.Equal(Loc.Get(K.GitNoChanges), _w.StatusLeftText.Split(" · ").Last());

        surface.SetGitMarks([Mark(5, 2), Mark(20, 0, GitMarkKind.Removed), Mark(40, 3, GitMarkKind.Added, oldLines: 0)]);
        surface.CaretOffset = 0;
        int[] forward = [5, 20, 40, 5];
        foreach (int expected in forward)
        {
            _w.GoToGitChange(forward: true);
            Assert.Equal(expected, Line());
        }
        _w.GoToGitChange(forward: false);
        Assert.Equal(40, Line()); // before the first comes the last
        _w.GoToGitChange(forward: false);
        Assert.Equal(20, Line());
        Settle();
    }

    [AvaloniaFact]
    public void A_click_on_a_mark_of_a_file_Git_cannot_answer_for_opens_nothing_and_the_status_line_says_so()
    {
        Loc.SetLanguage("en");
        var tab = Open(Lines(30)); // a file outside every repository
        var surface = tab.Surface!;
        surface.SetGitMarks([Mark(4, 2)]);
        _w.UpdateLayout();
        var view = surface.TextArea.TextView;
        view.EnsureVisualLines();
        var margin = surface.TextArea.LeftMargins.OfType<GitMargin>().Single();
        double y = view.GetVisualTopByDocumentLine(5) - view.VerticalOffset + 4;
        var point = margin.TranslatePoint(new Point(3, y), _w)!.Value;
        _w.MouseDown(point, MouseButton.Left);
        _w.MouseUp(point, MouseButton.Left);
        Settle();

        Assert.Null(surface.PeekView);
        Assert.Contains(Loc.Get(K.GitPeekStale), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void The_commands_of_the_changes_have_a_help_topic_a_menu_entry_and_a_key_that_nothing_else_uses()
    {
        string[] ids = ["nks.git.nextChange", "nks.git.previousChange", "nks.git.showChange", "nks.git.revertHunk"];
        foreach (var id in ids)
        {
            Assert.True(_app.Workbench.Commands.TryGet(id, out var command), id);
            Assert.NotNull(NksHub.Textida.App.Help.HelpCatalog.ForCommand("en", id));
        }
        foreach (var platform in new[] { NksHub.Textida.Workbench.Keys.Platform.Windows, NksHub.Textida.Workbench.Keys.Platform.MacOS })
        {
            var keymap = NksHub.Textida.Workbench.WorkbenchHost.CreateBuiltin().BuildKeymap(platform);
            Assert.DoesNotContain(keymap.Conflicts, c => c.IsAmbiguous);
            Assert.Equal(3, keymap.Bindings.Count(b => ids.Contains(b.CommandId)));
            Assert.NotEmpty(keymap.ShortcutsFor("nks.git.nextChange"));
        }
    }
}
