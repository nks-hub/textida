using Avalonia.Headless.XUnit;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;

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

    private (Shell.MainWindow Window, string File) WindowWithProposal(string name)
    {
        string file = _temp.File(name, "foo bar\nbaz\n");
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(file, null);
        var change = ChangeBuilder.ForDocument(tab.Doc.Doc, [new TextReplacement(new ByteRange(0, 3), "FOO")]);
        _ = w.ShowProposalAsync("p1", "Make it loud", "plugin:acme", tab, change, CancellationToken.None);
        return (w, file);
    }

    [AvaloniaFact]
    public async Task A_change_proposal_that_waited_in_the_review_comes_back_with_the_task_and_applies_to_the_same_text()
    {
        var (w, file) = WindowWithProposal("loud.txt");
        Assert.True(w.SaveNamedSession("review"));
        Assert.Null(w.ProposalLeftOut);
        var saved = _app.Workspace.FindSession("review")!;
        Assert.Equal("Make it loud", saved.Groups[0].Tabs[0].Proposal!.Title);

        var other = _app.CreateWindow(null);
        Assert.Empty(other.OpenNamedSession(saved));

        Assert.NotNull(other.RestoredProposal);
        var report = other.ApplyPlan()!;
        Assert.Equal(1, report.Applied);
        Assert.Equal("FOO bar\nbaz\n", other.Tabs.Single(t => t.Path == file).Editor!.Document.Text);
        Assert.Equal((await other.RestoredProposal!).Outcome, NksHub.Textida.PluginHost.Documents.ProposalOutcome.Applied);
    }

    [AvaloniaFact]
    public void When_the_text_is_not_the_one_the_proposal_was_made_for_nothing_is_opened_and_a_proposal_that_cannot_be_kept_is_named_at_the_save()
    {
        var (w, file) = WindowWithProposal("changed.txt");
        Assert.True(w.SaveNamedSession("review2"));
        var saved = _app.Workspace.FindSession("review2")!;
        File.WriteAllText(file, "someone else wrote this\n");
        var other = _app.CreateWindow(null);
        other.OpenNamedSession(saved);
        Assert.Null(other.RestoredProposal);

        // an edit after the proposal was made leaves it stale: the save says it could not keep it
        w.Tabs.Single(t => t.Path == file).Editor!.Document.Insert(0, "x");
        Assert.True(w.SaveNamedSession("review3"));
        Assert.Equal("Make it loud", w.ProposalLeftOut);
        Assert.Null(_app.Workspace.FindSession("review3")!.Groups[0].Tabs[0].Proposal);
    }
}
