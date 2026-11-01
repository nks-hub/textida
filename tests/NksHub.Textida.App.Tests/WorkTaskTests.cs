using Avalonia.Headless.XUnit;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.7.2: a named task brings back the tabs, all selections, the find strip, the filter, the last recipe and the replacement that waited in the preview.</summary>
[Collection("LocState")]
public class WorkTaskTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public WorkTaskTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task A_saved_task_returns_the_tabs_selections_find_filter_recipe_and_the_pending_replacement()
    {
        string folder = Path.Combine(_temp.Path, "proj");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "foo bar foo\nfoo\n");
        File.WriteAllText(Path.Combine(folder, "b.txt"), "no match\n");
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(Path.Combine(folder, "a.txt"), null);
        tab.Editor!.SetSelections([new Sel(0, 3), new Sel(8, 11), new Sel(12, 15)]);
        w.DocumentFilter.Text = "a.t";
        _app.LastParameters.Recipe = "profile/tidy";
        w.ShowFindInFiles(replace: true);
        w.FindBar.FindBox.Text = "foo";
        w.FindBar.ReplaceBox.Text = "baz";
        w.FindBar.FolderBox.Text = folder;
        await w.RunFilesSearchAsync();
        Assert.True(w.SaveNamedSession("morning"));

        // another day: a fresh window and a clean state
        w.CloseTab(tab);
        w.DocumentFilter.Text = "";
        _app.LastParameters.Recipe = "";
        File.WriteAllText(Path.Combine(folder, "b.txt"), "foo here too\n"); // the files moved on since the task was saved
        var again = _app.Workspace.FindSession("morning")!;
        Assert.NotNull(again.Plan);
        Assert.Equal(3, again.Groups[0].Tabs[0].Selections!.Count);

        Assert.Empty(w.OpenNamedSession(again));
        await w.PlanTask;

        var back = w.Tabs.Single(t => t.Path == Path.Combine(folder, "a.txt"));
        Assert.Equal([(0, 3), (8, 11), (12, 15)], back.Editor!.GetSelections().Select(s => (s.Start, s.End)));
        Assert.Equal("a.t", w.DocumentFilter.Text);
        Assert.Equal("profile/tidy", _app.LastParameters.Recipe);
        Assert.Equal("foo", w.FindBar.FindBox.Text);
        Assert.Equal("baz", w.FindBar.ReplaceBox.Text);
        Assert.True(w.FindBar.FilesMode && w.FindBar.ReplaceShown);
        // the preview is planned again on the files as they are now: b.txt matches too
        var report = w.ApplyPlan()!;
        Assert.Equal(2, report.Applied);
        Assert.Equal("baz here too\n", File.ReadAllText(Path.Combine(folder, "b.txt")));
    }

    [AvaloniaFact]
    public async Task A_task_without_a_find_strip_or_plan_opens_as_a_session_did()
    {
        var w = _app.CreateWindow(null);
        w.OpenFile(_temp.File("only.txt", "x"), null);
        Assert.True(w.SaveNamedSession("plain"));
        var saved = _app.Workspace.FindSession("plain")!;
        Assert.Null(saved.Find);
        Assert.Null(saved.Plan);
        Assert.Null(saved.DocumentFilter);
        Assert.Empty(w.OpenNamedSession(saved));
        await w.PlanTask;
    }
}
