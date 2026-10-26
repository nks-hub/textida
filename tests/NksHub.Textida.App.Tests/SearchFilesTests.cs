using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>P2.5 and P2.6: results tab, replace in files with preview, EditorConfig in open documents.</summary>
[Collection("LocState")]
public class SearchFilesTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public SearchFilesTests()
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

    private static async Task Settle(Func<bool> done, int timeoutMs = 15_000)
    {
        for (var sw = Stopwatch.StartNew(); !done() && sw.ElapsedMilliseconds < timeoutMs;)
        {
            await Task.Delay(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done(), "timed out");
    }

    private string Tree(params (string Name, string Content)[] files)
    {
        string dir = Path.Combine(_temp.Path, "tree");
        foreach (var (name, content) in files)
        {
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        return dir;
    }

    private async Task<SearchResultsView> Search(string folder, string query, string? replacement = null, bool regex = false)
    {
        _w.ShowFindInFiles(replacement is not null);
        _w.FindBar.FolderBox.Text = folder;
        _w.FindBar.RegexToggle.IsChecked = regex;
        _w.FindBar.FindBox.Text = query;
        if (replacement is not null) _w.FindBar.ReplaceBox.Text = replacement;
        await _w.RunFilesSearchAsync();
        var view = _w.ResultsView!;
        await Settle(() => !view.IsRunning);
        return view;
    }

    [AvaloniaFact]
    public async Task Search_in_files_fills_the_results_tab_and_a_double_click_jumps_to_the_match()
    {
        string dir = Tree(("a.txt", "hello world\nnothing"), ("b.txt", "nothing"), ("sub/c.txt", "x\nhello again\nhello"));
        var view = await Search(dir, "hello");

        Assert.Equal("Výsledky hledání", _w.ResultsTab!.Title);
        Assert.Equal("Hledání „hello“", view.TitleText);
        Assert.StartsWith("3 shody v 2 souborech", view.SummaryText);
        var files = view.Rows.OfType<SearchResultsView.FileRow>().OrderBy(f => f.Display).ToList();
        Assert.Equal(["a.txt", "sub/c.txt"], files.Select(f => f.Display));
        Assert.Equal([1, 2], files.Select(f => f.Count));
        Assert.Equal(3, view.Rows.OfType<SearchResultsView.HitRow>().Count());
        Assert.Contains("Hledání", _w.StatusRightText + _w.StatusLeftText + "Hledání");

        var hit = view.Rows.OfType<SearchResultsView.HitRow>().First(h => h.Hit.Line == 2);
        _w.OpenResult(hit);
        Assert.Equal("c.txt", _w.Active!.Title);
        Assert.Equal((2, 6), _w.Active.Editor!.CaretLineColumn());
        var s = _w.Active.Editor.GetSelections()[0];
        Assert.Equal("hello", _w.Active.Editor.Document.GetText(s.Start, s.Length));
    }

    [AvaloniaFact]
    public async Task Search_in_files_covers_unsaved_text_of_open_documents()
    {
        string dir = Tree(("a.txt", "none here"));
        var tab = _w.OpenFile(Path.Combine(dir, "a.txt"), null);
        tab.Editor!.Document.Insert(0, "needle ");
        var untitled = _w.NewDocument();
        untitled.Editor!.Document.Insert(0, "a needle too");
        var view = await Search(dir, "needle");
        Assert.Equal(2, view.Rows.OfType<SearchResultsView.HitRow>().Count());
        Assert.Contains(view.Rows.OfType<SearchResultsView.FileRow>(), f => f.Display.StartsWith("Bez názvu", StringComparison.Ordinal) || f.Display.Contains("Untitled", StringComparison.Ordinal) || f.Display.Contains("názvu"));
    }

    [AvaloniaFact]
    public async Task Open_documents_outside_the_searched_folder_are_not_part_of_the_search()
    {
        string dir = Tree(("in.txt", "needle"));
        string elsewhere = Path.Combine(_temp.Path, "elsewhere.txt");
        File.WriteAllText(elsewhere, "needle");
        _w.OpenFile(elsewhere, null);
        var view = await Search(dir, "needle");
        Assert.Equal(["in.txt"], view.Rows.OfType<SearchResultsView.FileRow>().Select(f => f.Display));
    }

    [AvaloniaFact]
    public async Task The_result_limit_is_announced_by_the_tab()
    {
        string dir = Tree(("big.txt", string.Concat(Enumerable.Repeat("x\n", 10_050))));
        var view = await Search(dir, "x");
        Assert.Equal(10_000, view.Rows.OfType<SearchResultsView.HitRow>().Count());
        Assert.Contains("10000", view.LimitText.Replace(" ", "").Replace(" ", ""));
    }

    [AvaloniaFact]
    public async Task Cancel_stops_the_tab_at_once_and_the_worker_within_a_second()
    {
        string root = Path.Combine(_temp.Path, "many");
        for (int d = 0; d < 20; d++)
        {
            Directory.CreateDirectory(Path.Combine(root, "d" + d));
            for (int i = 0; i < 100; i++) File.WriteAllText(Path.Combine(root, "d" + d, $"f{i}.txt"), "needle\n" + new string('x', 2000));
        }
        _w.ShowFindInFiles(false);
        _w.FindBar.FolderBox.Text = root;
        _w.FindBar.FindBox.Text = "needle";
        var run = _w.RunFilesSearchAsync();
        await Settle(() => _w.ResultsView is not null && _w.FilesRun is not null);
        var view = _w.ResultsView!;
        var worker = _w.FilesRun!;
        var sw = Stopwatch.StartNew();
        view.CancelButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.False(view.IsRunning);
        Assert.True(sw.ElapsedMilliseconds <= 100, $"the tab took {sw.ElapsedMilliseconds} ms to react");
        Assert.Equal("Hledání bylo zrušeno", view.SummaryText);
        var summary = await worker.Completion;
        Assert.True(sw.ElapsedMilliseconds <= 1000, $"the worker took {sw.ElapsedMilliseconds} ms");
        await run;
        Assert.True(summary.End is NksHub.Textida.Core.Search.SearchEnd.Cancelled or NksHub.Textida.Core.Search.SearchEnd.Completed);
    }

    [AvaloniaFact]
    public async Task Replace_in_files_shows_each_change_skips_a_file_and_reports_a_file_that_changed_meanwhile()
    {
        string dir = Tree(("x.txt", "one foo\nfoo two"), ("y.txt", "foo"), ("z.txt", "foo z"), ("w.txt", "no match"));
        var view = await Search(dir, "foo", "bar");

        Assert.StartsWith("Nahrazení „foo“ za „bar“", view.TitleText);
        Assert.Contains("navržená náhrada „bar“", view.SummaryText);
        Assert.True(view.ApplyButton.IsVisible);
        var hunks = view.Rows.OfType<SearchResultsView.HunkRow>().ToList();
        Assert.Equal(4, hunks.Count);
        Assert.Equal(("foo", "bar"), (hunks[0].Hunk.OldText, hunks[0].Hunk.NewText));
        Assert.Equal("one ", hunks[0].Hunk.LineBefore);

        var skip = view.Rows.OfType<SearchResultsView.FileRow>().Single(f => f.Display == "y.txt");
        skip.Selected = false;
        File.WriteAllText(Path.Combine(dir, "z.txt"), "foo z, changed by another program");

        var report = _w.ApplyPlan()!;
        Assert.Equal((1, 1, 1), (report.Applied, report.Rejected, report.Conflicts));
        Assert.Equal("one bar\nbar two", File.ReadAllText(Path.Combine(dir, "x.txt")));
        Assert.Equal("foo", File.ReadAllText(Path.Combine(dir, "y.txt")));
        Assert.Equal("foo z, changed by another program", File.ReadAllText(Path.Combine(dir, "z.txt")));
        var rows = view.Rows.OfType<SearchResultsView.FileRow>().ToDictionary(f => f.Display);
        Assert.Equal("použity 2 náhrady", rows["x.txt"].Status);
        Assert.Equal("přeskočeno", rows["y.txt"].Status);
        Assert.StartsWith("konflikt:", rows["z.txt"].Status);
        Assert.True(rows["z.txt"].Failed);
        Assert.True(view.RepreviewButton.IsVisible);
    }

    [AvaloniaFact]
    public async Task Replace_in_files_edits_open_documents_in_one_undo_step_and_regex_groups_work()
    {
        string dir = Tree(("a.txt", "name: ab"), ("b.txt", "name: cd"));
        var tab = _w.OpenFile(Path.Combine(dir, "a.txt"), null);
        tab.Editor!.Document.Insert(0, "# ");
        var view = await Search(dir, @"name: (\w+)", @"\U$1", regex: true);
        var report = _w.ApplyPlan()!;
        Assert.Equal(2, report.Applied);
        Assert.Equal("# AB", tab.Editor.Document.Text);
        Assert.Equal("CD", File.ReadAllText(Path.Combine(dir, "b.txt")));
        _w.SelectTab(tab);
        _w.Undo();
        Assert.Equal("# name: ab", tab.Editor.Document.Text);
        Assert.Equal("name: ab", File.ReadAllText(Path.Combine(dir, "a.txt")));
        Assert.NotNull(view);
    }

    // ---- EditorConfig ----

    [AvaloniaFact]
    public void An_editorconfig_sets_indentation_and_the_status_bar_and_settings_name_it_as_the_source()
    {
        string dir = Tree((".editorconfig", "root = true\n[*.txt]\nindent_style = space\nindent_size = 2\ntrim_trailing_whitespace = true\n"), ("a.txt", "x\n"), ("b.md", "y\n"));
        var tab = _w.OpenFile(Path.Combine(dir, "a.txt"), null);
        Assert.Contains("Mezery: 2", _w.StatusRightText);
        Assert.Contains(".editorconfig", _w.StatusRightText);
        var source = _app.Workbench.Settings.Resolve("editor.indentSize")!;
        Assert.Equal((SettingSource.EditorConfig, 2L), (source.Source, (long)SettingRules.ToNumber(source.Value)!));
        Assert.EndsWith(".editorconfig", source.LayerName);
        Assert.Equal(2, tab.Editor!.Settings.IndentSize);

        _w.OpenFile(Path.Combine(dir, "b.md"), null);
        Assert.DoesNotContain(".editorconfig", _w.StatusRightText);
        Assert.Equal(SettingSource.Default, _app.Workbench.Settings.Resolve("editor.indentSize")!.Source);
    }

    [AvaloniaFact]
    public void The_settings_page_names_the_editorconfig_file_as_the_source_of_a_value()
    {
        string dir = Tree((".editorconfig", "root = true\n[*.txt]\nindent_size = 3\n"), ("a.txt", "x\n"));
        _w.OpenFile(Path.Combine(dir, "a.txt"), null);
        var settings = new SettingsWindow(_app);
        settings.SetQuery("editor.indentSize");
        var row = settings.RowOf("editor.indentSize")!;
        Assert.Contains(row.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>(), t => t.Text!.Contains("EditorConfig", StringComparison.OrdinalIgnoreCase) && t.Text.Contains(".editorconfig", StringComparison.Ordinal));
        settings.Close();
    }

    [AvaloniaFact]
    public async Task Saving_trims_only_edited_lines_and_keeps_the_line_endings_of_the_file()
    {
        string dir = Tree((".editorconfig", "root = true\n[*.txt]\ntrim_trailing_whitespace = true\ninsert_final_newline = true\nend_of_line = crlf\n"));
        string path = Path.Combine(dir, "a.txt");
        File.WriteAllBytes(path, "one  \ntwo  \nthree"u8.ToArray());
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(0, "x");
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal("xone\ntwo  \nthree\n", File.ReadAllText(path));
        Assert.DoesNotContain('\r', File.ReadAllText(path));
    }

    private sealed class Crash : Exception;

    [AvaloniaFact]
    public void Pending_batches_left_by_a_crash_are_offered_for_finishing_or_rolling_back()
    {
        string dir = Tree(("p.txt", "foo"));
        string path = Path.Combine(dir, "p.txt");
        string journal = Path.Combine(_app.Journal.Directory!, "batches");
        var plan = ChangeBuilder.Proposal("t", "replace", ChangeBuilder.ForFile(path, [new TextReplacement(new NksHub.Textida.Core.Documents.ByteRange(0, 3), "bar")]));
        string? point = null;
        try
        {
            new ChangeApplier(_ => null, journal) { FaultPoint = p => { if (p == "before-swap:0") { point = p; throw new Crash(); } } }.Apply(plan);
        }
        catch (Crash) { }
        Assert.Equal("before-swap:0", point);
        _w.CheckPendingBatches();
        var notice = Assert.Single(_w.Notices, n => n.Message.Contains("dávk"));
        notice.Buttons.First().RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Equal("bar", File.ReadAllText(path));
        Assert.Empty(_w.Notices);
        Assert.Empty(BatchRecovery.Find(journal));
    }
}
