using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.History;

namespace NksHub.Textida.App.Tests;

/// <summary>P5.3.4: the history panel of a file. What the store keeps is tested with <c>FileHistoryTests</c> and <c>HistoryStatsTests</c>; here is what the panel shows of it and does with it.</summary>
[Collection("LocState")]
public class HistoryPanelTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HistoryPanelTests()
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

    private static async Task Pump(int milliseconds)
    {
        for (var sw = Stopwatch.StartNew(); sw.ElapsedMilliseconds < milliseconds;)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static async Task<bool> Eventually(Func<bool> condition, int milliseconds = 15_000)
    {
        for (var sw = Stopwatch.StartNew(); !condition() && sw.ElapsedMilliseconds < milliseconds;) await Pump(20);
        return condition();
    }

    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>
    /// A file with a revision for each of <paramref name="versions"/> (the first is the text it is opened with, the others are recorded as saves), open in a tab that holds
    /// <paramref name="current"/> as an unsaved edit, and the history panel of it.
    /// </summary>
    private async Task<(DocTab Tab, string Path, HistoryView View)> OpenPanel(string[] versions, string? current = null)
    {
        string path = _temp.File("h.txt", versions[0]);
        var tab = _w.OpenFile(path, null);
        Assert.True(await Eventually(() => _app.History.List(path).Count == 1));
        for (int i = 1; i < versions.Length; i++)
        {
            Assert.Equal(HistoryStatus.Recorded, _app.History.Record(path, U(versions[i]), HistorySource.Save).Status);
            await Pump(5);
        }
        if (current is not null) tab.Editor!.Document.Text = current;
        _w.SelectTab(tab);
        _w.ShowHistoryPanel();
        var view = _w.ActiveHistory!;
        Assert.NotNull(view);
        Assert.True(await Eventually(() => view.Shown.Count == versions.Length));
        return (tab, path, view);
    }

    private static readonly string[] Versions = ["a\nb\nc\n", "a\nB\nc\n", "a\nB\nc\nd\ne\n", "e\n"];

    [AvaloniaFact]
    public async Task The_panel_lists_every_revision_of_the_store_newest_first_and_names_each_row_for_a_screen_reader()
    {
        Loc.SetLanguage("en");
        var (_, path, view) = await OpenPanel(Versions);

        var stored = _app.History.List(path);
        Assert.Equal(stored.Select(r => r.Id), view.Shown.Select(r => r.Id));
        Assert.Equal(stored.OrderByDescending(r => r.Id, StringComparer.Ordinal).Select(r => r.Id), view.Shown.Select(r => r.Id));
        foreach (var r in stored)
        {
            string name = view.RowName(r.Id)!;
            Assert.Contains(HistoryView.When(r), name);
            Assert.Contains(HistoryView.SourceText(r.Source), name);
            Assert.Contains(OpenDocument.FormatSize(r.Size), name);
        }
        Assert.Equal(Loc.Plural(K.HistoryPanelSummary, 4, ("size", OpenDocument.FormatSize(stored.Sum(r => r.StoredBytes))), ("oldest", stored[^1].TimeUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture))), view.SummaryText);
        Assert.Equal("History: h.txt", _w.Active!.Title);
        Assert.Equal(Loc.Get(K.A11yHistoryPanel), AutomationProperties.GetName(view));
        Assert.Equal(Loc.Get(K.A11yHistoryList), AutomationProperties.GetName(view.List));
    }

    [AvaloniaFact]
    public async Task The_counts_of_lines_are_made_in_the_background_and_are_those_of_the_diff_of_neighbouring_revisions()
    {
        Loc.SetLanguage("en");
        var (_, path, view) = await OpenPanel(Versions);
        Assert.True(await Eventually(() => !view.Counting && view.Shown.All(r => view.ChangeOf(r.Id) is not null || r.Id == view.Shown[^1].Id)), "the counts are not made");

        var shown = view.Shown;
        for (int i = 0; i < shown.Count - 1; i++)
        {
            var expected = HistoryStats.Between(_app.History.Read(path, shown[i + 1].Id), _app.History.Read(path, shown[i].Id), TextCodec.Utf8);
            Assert.Equal(expected, view.ChangeOf(shown[i].Id));
            Assert.Contains(Loc.Get(K.HistoryPanelChange, ("added", expected.Added), ("removed", expected.Removed)), view.RowName(shown[i].Id));
        }
        Assert.Contains(Loc.Get(K.HistoryPanelFirst), view.RowName(shown[^1].Id)); // the oldest has nothing before it
        Assert.Null(view.ChangeOf(shown[^1].Id));
    }

    [AvaloniaFact]
    public async Task A_chosen_revision_is_compared_with_the_current_text_by_the_line_diff_of_the_comparison()
    {
        var (tab, path, view) = await OpenPanel(Versions, current: "a\nB\nc\nd\nX\n");
        view.List.SelectedIndex = 2; // the second oldest: a B c
        Assert.True(await Eventually(() => view.Diff is { Session.Diff: not null }));

        var chosen = view.Shown[2];
        var diff = view.Diff!.Session.Diff!;
        var expected = LineDiff.Compute(DiffSource.OfText(Encoding.UTF8.GetString(_app.History.Read(path, chosen.Id))), DiffSource.Of(tab.Doc.Doc));
        Assert.Equal(expected.Hunks, diff.Hunks);
        Assert.NotEmpty(diff.Hunks);
        Assert.Equal(Loc.Get(K.HistoryPanelCurrent), view.Diff.Session.Right.Title);
        Assert.Same(tab.Doc, view.Diff.Session.Right.Document);
    }

    [AvaloniaFact]
    public async Task The_other_mode_compares_with_the_revision_before_and_two_chosen_revisions_with_each_other_the_older_on_the_left()
    {
        var (_, path, view) = await OpenPanel(Versions);
        view.ModeBox.SelectedIndex = 1;
        view.List.SelectedIndex = 1;
        Assert.True(await Eventually(() => view.Diff is { Session.Diff: not null } d && d.Session.Right.Text == Encoding.UTF8.GetString(_app.History.Read(path, view.Shown[1].Id))));
        Assert.Equal(Encoding.UTF8.GetString(_app.History.Read(path, view.Shown[2].Id)), view.Diff!.Session.Left.Text);

        view.List.SelectedItems!.Clear();
        view.List.SelectedItems.Add(view.List.Items[0]);
        view.List.SelectedItems.Add(view.List.Items[3]);
        Assert.True(await Eventually(() => view.Diff is { } d && d.Session.Left.Text == Encoding.UTF8.GetString(_app.History.Read(path, view.Shown[3].Id))));
        Assert.Equal(Encoding.UTF8.GetString(_app.History.Read(path, view.Shown[0].Id)), view.Diff!.Session.Right.Text);
        Assert.Equal(Loc.Get(K.HistoryPanelTwoSelected), view.HeaderText);
    }

    [AvaloniaFact]
    public async Task Restoring_the_whole_file_from_the_panel_is_one_undo_step_and_keeps_the_text_that_was_there()
    {
        var (tab, path, view) = await OpenPanel(Versions, current: "mine\n");
        view.List.SelectedIndex = 2;
        await Pump(50);
        var restored = view.Shown[2];
        string revisionText = Encoding.UTF8.GetString(_app.History.Read(path, restored.Id));

        view.RestoreButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(revisionText, tab.Editor!.Document.Text);
        Assert.Same(view, _w.ActiveHistory); // the panel is back in front
        Assert.Equal(Loc.Get(K.HistoryRestoredFile, ("time", HistoryView.When(restored))), _w.StatusMessageText);
        Assert.True(await Eventually(() => _app.History.List(path).Any(r => r.Source == HistorySource.BeforeRestore)));

        _w.SelectTab(tab);
        _w.Undo();
        Assert.Equal("mine\n", tab.Editor.Document.Text); // one step
    }

    [AvaloniaFact]
    public async Task A_block_of_the_comparison_goes_into_the_tab_as_one_undo_step()
    {
        var (tab, _, view) = await OpenPanel(Versions, current: "a\nB\nc\nZ\n");
        view.List.SelectedIndex = 1; // a B c d e
        Assert.True(await Eventually(() => view.Diff is { Session.Diff: not null }));
        var diff = view.Diff!;
        diff.NextDifference();
        Assert.True(diff.CopyCurrent(DiffSide.B)); // the block of the revision into the text of the tab
        Assert.Equal("a\nB\nc\nd\ne\n", tab.Editor!.Document.Text);
        _w.SelectTab(tab);
        _w.Undo();
        Assert.Equal("a\nB\nc\nZ\n", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task The_list_follows_a_save_a_label_a_pin_and_a_change_from_outside_without_reopening_the_panel()
    {
        Loc.SetLanguage("en");
        var (_, path, view) = await OpenPanel(Versions.Take(2).ToArray());

        _app.History.Record(path, U("third\n"), HistorySource.Save);
        Assert.True(await Eventually(() => view.Shown.Count == 3));
        _app.History.Record(path, U("fourth\n"), HistorySource.External);
        Assert.True(await Eventually(() => view.Shown.Count == 4));
        Assert.True(await Eventually(() => view.Shown[0].Source == HistorySource.External));
        Assert.True(await Eventually(() => !view.Counting));
        Assert.NotNull(view.ChangeOf(view.Shown[1].Id)); // the one that was the newest has its count, and the new one is counted

        var id = view.Shown[2].Id;
        _app.History.SetLabel(path, id, "before refactoring");
        Assert.True(await Eventually(() => view.RowName(id)!.Contains("before refactoring")));
        _app.History.Pin(path, id, "user");
        Assert.True(await Eventually(() => view.RowName(id)!.Contains(Loc.Get(K.HistoryPinnedTag))));
    }

    [AvaloniaFact]
    public async Task The_filters_narrow_the_list_by_the_reason_and_by_a_word_of_the_label_and_say_when_nothing_matches()
    {
        Loc.SetLanguage("en");
        var (_, path, view) = await OpenPanel(Versions);
        var third = view.Shown[1];
        _app.History.SetLabel(path, third.Id, "Release candidate");
        Assert.True(await Eventually(() => view.RowName(third.Id)!.Contains("Release candidate")));

        view.LabelFilterBox.Text = "candidate";
        Assert.True(await Eventually(() => view.Shown.Select(r => r.Id).SequenceEqual([third.Id]))); // the box tells about its text a moment later
        view.LabelFilterBox.Text = "";
        Assert.True(await Eventually(() => view.Shown.Count == Versions.Length));
        view.SourceBox.SelectedIndex = 1 + Array.IndexOf(new[] { HistorySource.Save, HistorySource.External, HistorySource.Initial, HistorySource.Manual, HistorySource.BeforeRestore }, HistorySource.Initial);
        Assert.Equal([HistorySource.Initial], view.Shown.Select(r => r.Source));
        view.LabelFilterBox.Text = "nothing like this";
        Assert.True(await Eventually(() => view.Shown.Count == 0));
        Assert.Equal(Loc.Get(K.HistoryPanelNoMatch), view.SummaryText);
    }

    [AvaloniaFact]
    public async Task The_limits_in_force_are_told_and_without_any_the_panel_says_so()
    {
        Loc.SetLanguage("en");
        var (_, _, view) = await OpenPanel(Versions.Take(2).ToArray());
        Assert.Equal(Loc.Get(K.HistoryPanelRetentionNone), view.RetentionText);

        _app.History.Policy = new HistoryPolicy { MaxAge = TimeSpan.FromDays(30), MaxRevisionsPerFile = 50, MaxBytesPerFile = 4L << 20 };
        view.Reload();
        Assert.Equal($"Retention: 30 days, 50 revisions, {OpenDocument.FormatSize(4L << 20)} per file", view.RetentionText);
        Assert.Equal("Retention: 1 day, 1 revision", HistoryView.RetentionOf(new HistoryPolicy { MaxAge = TimeSpan.FromDays(1), MaxRevisionsPerFile = 1 }));
        Loc.SetLanguage("cs");
        Assert.Equal("Retence: 2 dny, 5 revizí", HistoryView.RetentionOf(new HistoryPolicy { MaxAge = TimeSpan.FromDays(2), MaxRevisionsPerFile = 5 }));
    }

    [AvaloniaFact]
    public async Task There_is_one_panel_for_a_file_and_it_opens_from_the_command_the_tab_menu_and_closing_the_tab_ends_it()
    {
        var (tab, path, view) = await OpenPanel(Versions.Take(2).ToArray());
        var panelTab = _w.Active!;
        Assert.Same(view, panelTab.Custom);

        _w.SelectTab(tab);
        Assert.Same(panelTab, _w.OpenHistoryPanel(tab, path)); // the same panel, not a second one
        Assert.Same(view, _w.ActiveHistory);
        Assert.Single(_w.HistoryViews);

        await _w.CloseTabAsync(panelTab);
        Assert.Empty(_w.HistoryViews);
        Assert.Null(view.Diff);
        var reopened = _w.OpenHistoryPanel(tab, path);
        Assert.NotSame(panelTab, reopened);
        Assert.Single(_w.HistoryViews);

        Assert.True(_app.Workbench.Commands.TryGet("nks.file.historyPanel", out _));
        Assert.True(_app.Workbench.Commands.TryGet("nks.files.history", out _));
        Assert.NotNull(NksHub.Textida.App.Help.HelpCatalog.ForCommand("en", "nks.file.historyPanel"));
    }

    [AvaloniaFact]
    public async Task A_large_file_has_its_revisions_but_cannot_be_restored_from_the_panel()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        string path = _temp.File("large.txt", string.Concat(Enumerable.Repeat("a line of the large file\n", 50_000)));
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        Assert.True(await Eventually(() => tab.Paged!.Index.Complete));
        _app.History.RecordFile(path, HistorySource.Manual, "mark");
        _w.SelectTab(tab);
        _w.ShowHistoryPanel();
        var view = _w.ActiveHistory!;
        Assert.True(await Eventually(() => view.Shown.Count >= 1));
        Assert.False(view.RestoreButton.IsVisible);
        Assert.True(view.CopyButton.IsEnabled); // Save as a Copy works for every file
    }

    [AvaloniaFact]
    public async Task A_file_without_a_path_has_no_panel_and_the_status_line_says_so()
    {
        Loc.SetLanguage("en");
        _w.NewDocument();
        _w.ShowHistoryPanel();
        Assert.Null(_w.ActiveHistory);
        Assert.Equal(Loc.Get(K.HistoryNoFile), _w.StatusMessageText);
        await Pump(10);
    }

    [AvaloniaFact]
    public async Task The_buttons_the_filters_and_the_list_have_names_and_the_texts_follow_the_language()
    {
        var (_, _, view) = await OpenPanel(Versions.Take(2).ToArray());
        foreach (string language in Loc.Languages)
        {
            Loc.SetLanguage(language);
            view.ApplyTexts();
            foreach (Control control in new Control[] { view, view.List, view.SourceBox, view.ModeBox, view.LabelFilterBox, view.RestoreButton, view.LabelButton, view.CopyButton })
            {
                string? name = AutomationProperties.GetName(control);
                Assert.False(string.IsNullOrWhiteSpace(name), $"{language}: {control.GetType().Name}");
                Assert.DoesNotContain(".", name!.Replace("...", ""), StringComparison.Ordinal);
            }
            Assert.Equal(Loc.Get(K.HistoryRestoreFile), view.RestoreButton.Content);
            Assert.Equal(Loc.Get(K.HistoryPanelAgainstCurrent), ((ComboBoxItem)view.ModeBox.Items[0]!).Content);
        }
    }
}
