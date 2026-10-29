using System.Diagnostics;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.6.4: the commit panel. What the plugin does to a repository is tested with <c>GitPluginTests</c> on real repositories; here is what the panel shows of it and asks of it.</summary>
[Collection("LocState")]
public class CommitViewTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public CommitViewTests()
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

    /// <summary>A repository that is only a list of files and the calls made on it, standing in for the Git plugin.</summary>
    private sealed class Fake(string root)
    {
        public List<CommitEntry> Entries = [];
        public List<string> Messages = ["last message"];
        public string Branch = "main";
        public string? Upstream = "origin/main";
        public int Ahead = 2, Behind = 1;
        public bool HasCommits = true, CommitResult = true;
        public readonly List<string> Calls = [];
        public readonly Dictionary<(string, bool), CommitBlob> Blobs = [];

        public CommitChanges Snapshot() => new(root, Branch, Upstream, Ahead, Behind, HasCommits, Entries.Count, [.. Entries], [.. Messages]);

        private static CommitEntry Staged(CommitEntry e, bool on) =>
            on ? new CommitEntry(e.Path, e.Untracked ? 'A' : e.Index == ' ' ? 'M' : e.Index, ' ', e.From, false)
               : new CommitEntry(e.Path, e.Index == 'A' ? '?' : ' ', e.Index == 'A' ? '?' : 'M', e.From, false);

        public CommitViewActions Actions => new(
            Read: () => Task.FromResult<CommitChanges?>(Snapshot()),
            Stage: (paths, unstage) =>
            {
                Calls.Add($"{(unstage ? "unstage" : "stage")} {string.Join(',', paths)}");
                Entries = [.. Entries.Select(e => paths.Contains(e.Path) ? Staged(e, !unstage) : e)];
                return Task.CompletedTask;
            },
            StageHunk: (file, unstage, line) =>
            {
                Calls.Add($"hunk {file} {(unstage ? "unstage" : "stage")} {line}");
                return Task.FromResult(true);
            },
            Blob: (file, head) => Task.FromResult<CommitBlob?>(Blobs.TryGetValue((file, head), out var blob) ? blob : new CommitBlob(false, false, [])),
            Commit: (message, amend) =>
            {
                Calls.Add($"commit {message}|{amend}");
                return Task.FromResult(CommitResult);
            },
            Push: () =>
            {
                Calls.Add("push");
                return Task.CompletedTask;
            });
    }

    private static CommitEntry Entry(string path, char index, char tree, bool conflict = false) => new(path, index, tree, null, conflict);

    /// <summary>The panel for a fake repository in the folder of the test, shown in a tab and filled.</summary>
    private async Task<(CommitView View, Fake Repo)> Open(params CommitEntry[] entries)
    {
        var repo = new Fake(_temp.Path) { Entries = [.. entries] };
        var view = new CommitView(_temp.Path, repo.Actions, () => _w.Options);
        var tab = _w.AddCommitTab(view);
        _w.SelectTab(tab);
        await view.ReloadAsync();
        await Pump(20);
        return (view, repo);
    }

    private static readonly CommitEntry[] Mixed =
    [
        Entry("a.txt", 'M', ' '), Entry("b.txt", 'M', 'M'), Entry("d.txt", ' ', 'M'), Entry("n.txt", '?', '?'), Entry("c.txt", 'U', 'U', conflict: true),
    ];

    [AvaloniaFact]
    public async Task The_panel_lists_conflicts_first_then_the_tracked_files_then_the_new_ones_with_the_state_of_each_box_and_row()
    {
        Loc.SetLanguage("en");
        var (view, _) = await Open(Mixed);

        Assert.Equal(["c.txt", "a.txt", "b.txt", "d.txt", "n.txt"], view.Files.Items.OfType<ListBoxItem>().Select(i => (AutomationProperties.GetName(i) ?? "").Split(',')[0]));
        Assert.True(view.CheckOf("a.txt")!.IsChecked);           // everything of it is in the index
        Assert.Null(view.CheckOf("b.txt")!.IsChecked);           // a part of it is
        Assert.False(view.CheckOf("d.txt")!.IsChecked);
        Assert.False(view.CheckOf("n.txt")!.IsChecked);
        Assert.False(view.CheckOf("c.txt")!.IsEnabled);          // a conflict is resolved first
        Assert.Equal("a.txt, staged: modified, in the commit", view.RowName("a.txt"));
        Assert.Equal("b.txt, staged: modified; not staged: modified, partly in the commit", view.RowName("b.txt"));
        Assert.Equal("n.txt, untracked, not in the commit", view.RowName("n.txt"));
        Assert.Equal("c.txt, conflict, not in the commit", view.RowName("c.txt"));
        Assert.Equal("Branch main", view.BranchText);
        Assert.Equal("tracks origin/main · 2 commits to push · 1 commit to pull", view.DistanceText);
        Assert.Equal("5 changed files, 2 in the commit.", view.SummaryText);
        Assert.Null(view.AllBox.IsChecked);
        Assert.Equal("Commit: " + Path.GetFileName(_temp.Path), _w.Active!.Title);
        Assert.Equal(Loc.Get(K.A11yCommitPanel), AutomationProperties.GetName(view));
        Assert.Equal(Loc.Get(K.A11yCommitInclude, ("file", "a.txt")), AutomationProperties.GetName(view.CheckOf("a.txt")!));
        Assert.Equal(Loc.Get(K.A11yCommitMessage), AutomationProperties.GetName(view.MessageBox));
    }

    [AvaloniaFact]
    public async Task A_clean_repository_without_an_upstream_says_so()
    {
        Loc.SetLanguage("en");
        var repo = new Fake(_temp.Path) { Upstream = null, Ahead = 0, Behind = 0, Messages = [] };
        var view = new CommitView(_temp.Path, repo.Actions, () => _w.Options);
        _w.SelectTab(_w.AddCommitTab(view));
        await view.ReloadAsync();

        Assert.Equal("No changes. The working tree is clean.", view.SummaryText);
        Assert.Equal("tracks no remote branch", view.DistanceText);
        Assert.False(view.RecentBox.IsVisible);
        Assert.False(view.CommitButton.IsEnabled);
        Assert.False(view.AllBox.IsEnabled);
    }

    [AvaloniaFact]
    public async Task A_box_stages_or_unstages_the_whole_file_and_the_box_of_all_files_does_it_for_every_file_that_is_not_in_conflict()
    {
        Loc.SetLanguage("en");
        var (view, repo) = await Open(Mixed);

        view.CheckOf("d.txt")!.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));
        Assert.True(await Eventually(() => repo.Calls.Count == 1));
        Assert.Equal("stage d.txt", repo.Calls[0]);
        Assert.True(await Eventually(() => view.CheckOf("d.txt")!.IsChecked == true)); // the list is read again

        view.CheckOf("a.txt")!.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));
        Assert.True(await Eventually(() => repo.Calls.Count == 2 && view.CheckOf("a.txt")!.IsChecked == false));
        Assert.Equal("unstage a.txt", repo.Calls[1]);

        view.CheckOf("b.txt")!.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent)); // only a part is in: the rest goes in
        Assert.True(await Eventually(() => repo.Calls.Count == 3 && view.CheckOf("b.txt")!.IsChecked == true));
        Assert.Equal("stage b.txt", repo.Calls[2]);

        view.AllBox.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));
        Assert.True(await Eventually(() => repo.Calls.Count == 4 && view.AllBox.IsChecked == true));
        Assert.Equal("stage a.txt,b.txt,d.txt,n.txt", repo.Calls[3]); // not c.txt
        view.AllBox.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));
        Assert.True(await Eventually(() => repo.Calls.Count == 5 && view.AllBox.IsChecked != true));
        Assert.StartsWith("unstage ", repo.Calls[4]);
    }

    [AvaloniaFact]
    public async Task Commit_needs_something_in_the_index_and_a_message_and_amend_needs_only_a_commit_and_fills_the_old_message()
    {
        Loc.SetLanguage("en");
        var (view, repo) = await Open(Entry("a.txt", ' ', 'M'));
        Assert.False(view.CommitButton.IsEnabled);                        // nothing staged
        view.MessageBox.Text = "wanted";
        await Pump(40); // the box tells about a change of its text a moment later
        Assert.False(view.CommitButton.IsEnabled);

        repo.Entries = [Entry("a.txt", 'M', ' ')];
        await view.ReloadAsync();
        await Pump(40);
        Assert.True(view.CommitButton.IsEnabled);
        Assert.True(view.CommitPushButton.IsEnabled);
        view.MessageBox.Text = "  ";
        await Pump(40);
        Assert.False(view.CommitButton.IsEnabled);                        // a message of blanks is none

        view.AmendBox.IsChecked = true;                                   // amend: the old message is offered, the index may be empty
        Assert.Equal("last message", view.MessageBox.Text);
        Assert.True(view.CommitButton.IsEnabled);
        repo.HasCommits = false;
        await view.ReloadAsync();
        Assert.False(view.CommitButton.IsEnabled);                        // nothing to amend in a repository without a commit
    }

    [AvaloniaFact]
    public async Task A_commit_sends_the_message_and_empties_the_box_only_when_it_was_made_and_commit_and_push_pushes_after_it()
    {
        Loc.SetLanguage("en");
        var (view, repo) = await Open(Entry("a.txt", 'M', ' '));
        view.MessageBox.Text = "first line\n\nsecond paragraph";

        repo.CommitResult = false;
        view.CommitButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(await Eventually(() => repo.Calls.Contains("commit first line\n\nsecond paragraph|False")));
        Assert.Equal("first line\n\nsecond paragraph", view.MessageBox.Text); // not made: the message stays
        Assert.DoesNotContain("push", repo.Calls);

        repo.CommitResult = true;
        view.CommitPushButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(await Eventually(() => repo.Calls.Contains("push")));
        Assert.Equal("", view.MessageBox.Text);
        Assert.False(view.AmendBox.IsChecked);
        Assert.Equal(["commit first line\n\nsecond paragraph|False", "commit first line\n\nsecond paragraph|False", "push"], repo.Calls);
    }

    [AvaloniaFact]
    public async Task An_earlier_message_fills_the_box()
    {
        Loc.SetLanguage("en");
        var (view, repo) = await Open(Entry("a.txt", 'M', ' '));
        repo.Messages = ["Fix the thing\n\nBecause it broke", "Older"];
        await view.ReloadAsync();
        Assert.Equal(["Fix the thing", "Older"], view.RecentBox.Items.OfType<ComboBoxItem>().Select(i => (string)i.Content!));

        view.RecentBox.SelectedIndex = 0;

        Assert.Equal("Fix the thing\n\nBecause it broke", view.MessageBox.Text);
    }

    [AvaloniaFact]
    public async Task The_chosen_file_is_shown_as_what_is_staged_and_what_is_not_and_a_button_stages_the_change_the_comparison_is_on()
    {
        Loc.SetLanguage("en");
        _temp.File("b.txt", "1\n2\n3\nfour\n");
        var repo = new Fake(_temp.Path) { Entries = [Entry("a.txt", 'M', ' '), Entry("b.txt", 'M', 'M')] };
        repo.Blobs[("b.txt", true)] = new CommitBlob(true, false, U("1\n2\n"));
        repo.Blobs[("b.txt", false)] = new CommitBlob(true, false, U("1\n2\n3\n"));
        repo.Blobs[("a.txt", true)] = new CommitBlob(true, false, U("x\n"));
        repo.Blobs[("a.txt", false)] = new CommitBlob(true, false, U("y\n"));
        var view = new CommitView(_temp.Path, repo.Actions, () => _w.Options);
        _w.SelectTab(_w.AddCommitTab(view));
        await view.ReloadAsync();

        Assert.True(view.Select("b.txt"));
        Assert.True(await Eventually(() => view.StagedDiff?.Session.Diff is not null && view.UnstagedDiff?.Session.Diff is not null));
        var staged = view.StagedDiff!;
        var unstaged = view.UnstagedDiff!;
        Assert.Equal("1\n2\n", staged.Session.Left.Text);
        Assert.Equal("1\n2\n3\n", staged.Session.Right.Text);
        Assert.Equal(["HEAD", "Index"], [staged.LeftTitleText, staged.RightTitleText]);
        Assert.Equal("1\n2\n3\n", unstaged.Session.Left.Text);
        Assert.Equal(Path.Combine(_temp.Path, "b.txt"), unstaged.Session.Right.FilePath);
        Assert.Equal(["Index", "File"], [unstaged.LeftTitleText, unstaged.RightTitleText]);
        Assert.Equal("b.txt  ·  staged: modified; not staged: modified", view.FileHeaderText);
        Assert.Equal("Stage change", unstaged.ActionButtons.Single().Content);
        Assert.Equal("Unstage change", staged.ActionButtons.Single().Content);

        Assert.True(await Eventually(() => staged.CurrentHunk == 0 && unstaged.CurrentHunk == 0)); // the first difference is current from the start
        Assert.True(unstaged.RunAction(0));
        Assert.True(await Eventually(() => repo.Calls.Any(c => c.StartsWith("hunk ", StringComparison.Ordinal))));
        Assert.Equal("hunk b.txt stage 3", repo.Calls.Single(c => c.StartsWith("hunk ", StringComparison.Ordinal))); // the fourth line is line 3
        // the list is read again and the comparisons are made again; the buttons of the new ones act
        Assert.True(await Eventually(() => view.StagedDiff is { } s && !ReferenceEquals(s, staged) && s.Session.Diff is not null && s.CurrentHunk == 0));
        Assert.True(view.StagedDiff!.RunAction(0));
        Assert.True(await Eventually(() => repo.Calls.Contains("hunk b.txt unstage 2")));

        Assert.True(view.Select("a.txt"));
        Assert.True(await Eventually(() => view.StagedDiff is not null && view.UnstagedDiff is null)); // nothing is unstaged in a.txt
    }

    [AvaloniaFact]
    public async Task A_file_that_is_binary_too_large_or_in_conflict_is_only_named()
    {
        Loc.SetLanguage("en");
        _temp.File("n.txt", "text\n");
        var repo = new Fake(_temp.Path) { Entries = [Entry("bin.dat", 'M', ' '), Entry("big.txt", 'M', ' '), Entry("c.txt", 'U', 'U', conflict: true)] };
        repo.Blobs[("bin.dat", true)] = new CommitBlob(true, false, [1, 2, 0, 3]);
        repo.Blobs[("bin.dat", false)] = new CommitBlob(true, false, [1, 2, 0, 4]);
        repo.Blobs[("big.txt", false)] = new CommitBlob(true, true, []);
        var view = new CommitView(_temp.Path, repo.Actions, () => _w.Options);
        _w.SelectTab(_w.AddCommitTab(view));
        await view.ReloadAsync();

        view.Select("bin.dat");
        Assert.True(await Eventually(() => view.NoteText == "bin.dat is a binary file. No comparison is shown."));
        Assert.Null(view.StagedDiff);
        view.Select("big.txt");
        Assert.True(await Eventually(() => view.NoteText == "big.txt is too large to be compared here."));
        view.Select("c.txt");
        Assert.True(await Eventually(() => view.NoteText == Loc.Get(K.CommitConflictNote)));
    }

    [AvaloniaFact]
    public async Task A_new_file_is_compared_with_nothing_and_a_file_gone_from_the_disk_with_an_empty_side()
    {
        Loc.SetLanguage("en");
        _temp.File("n.txt", "one\ntwo\n");
        var repo = new Fake(_temp.Path) { Entries = [Entry("n.txt", '?', '?'), Entry("gone.txt", ' ', 'D')] };
        repo.Blobs[("gone.txt", false)] = new CommitBlob(true, false, U("was here\n"));
        var view = new CommitView(_temp.Path, repo.Actions, () => _w.Options);
        _w.SelectTab(_w.AddCommitTab(view));
        await view.ReloadAsync();

        view.Select("n.txt");
        Assert.True(await Eventually(() => view.UnstagedDiff?.Session.Diff is not null && view.UnstagedDiff.Session.Right.FilePath!.EndsWith("n.txt", StringComparison.Ordinal)));
        Assert.Equal("", view.UnstagedDiff!.Session.Left.Text);
        Assert.Null(view.StagedDiff);
        Assert.Single(view.UnstagedDiff.Session.Diff!.Hunks);

        view.Select("gone.txt");
        Assert.True(await Eventually(() => view.UnstagedDiff?.Session.Left.Text == "was here\n"));
        Assert.Equal("", view.UnstagedDiff!.Session.Right.Text);
    }

    [AvaloniaFact]
    public async Task The_panel_follows_the_language()
    {
        Loc.SetLanguage("en");
        var (view, _) = await Open(Mixed);
        Assert.Equal("Commit and Push", view.CommitPushButton.Content);

        Loc.SetLanguage("cs");
        _w.ApplyTexts();

        Assert.Equal(Loc.Get(K.CommitCommitPush), view.CommitPushButton.Content);
        Assert.Equal("Commit a odeslat", view.CommitPushButton.Content);
        Assert.Equal("Větev main", view.BranchText);
        Assert.Equal("Všechny soubory", view.AllBox.Content);
        Assert.Contains("v indexu: změněno", view.RowName("a.txt"));
        Assert.Equal("Panel commitu", AutomationProperties.GetName(view));
    }

    [AvaloniaFact]
    public async Task Closing_the_tab_disposes_the_panel()
    {
        Loc.SetLanguage("en");
        var (view, _) = await Open(Mixed);
        Assert.Contains(view, _w.CommitViews);

        await _w.CloseTabAsync(_w.Active!);

        Assert.DoesNotContain(view, _w.CommitViews);
    }

    [AvaloniaFact]
    public void The_commands_of_the_toolbar_and_the_branch_and_stash_commands_have_handlers_and_say_when_there_is_no_repository()
    {
        Loc.SetLanguage("en");
        var registry = _app.Workbench.Commands;
        string[] ids = ["nks.git.commit", "nks.git.fetch", "nks.git.pull", "nks.git.push", "nks.git.switchBranch", "nks.git.createBranch", "nks.git.deleteBranch",
            "nks.git.stashSave", "nks.git.stashApply", "nks.git.stashDrop", "nks.git.logFile", "nks.git.logRepo"];
        Assert.All(ids, id => Assert.True(registry.HasHandler(id), id));
        Assert.False(registry.Commands.Single(c => c.Id == "nks.git.logFile").Enablement!.Evaluate(new WhenContext().Set(ContextKeys.HasDocument, false)));

        foreach (var id in ids.Where(i => i != "nks.git.logFile")) // the history of a file needs a file: it is off without a document
        {
            registry.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();
            Assert.Equal(Loc.Get(K.GitNoRepository), _w.StatusMessageText);
            _w.SetStatusMessage("");
        }
    }
}
