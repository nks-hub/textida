using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.6.4 with the real thing: the commit panel talks to the Git plugin process of the app, and what it stages, commits, amends and pushes is read back from real temporary repositories with the <c>git</c> of the machine.</summary>
[Collection("LocState")]
[Trait("Category", "RealTools")]
public class CommitPanelRealTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _repo;

    public CommitPanelRealTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1200;
        _w.Height = 800;
        _repo = Path.Combine(_temp.Path, "repo");
        Directory.CreateDirectory(_repo);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static string GitIn(string dir, params string[] args)
    {
        var info = new ProcessStartInfo(ToolLocator.Find("git")!) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "-c", "core.autocrlf=false" }.Concat(args)) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        string output = p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(30_000));
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {error}");
        return output.Replace("\r\n", "\n");
    }

    private string Git(params string[] args) => GitIn(_repo, args);

    private static async Task<bool> Eventually(Func<bool> condition, int milliseconds = 60_000)
    {
        for (var sw = Stopwatch.StartNew(); !condition() && sw.ElapsedMilliseconds < milliseconds;)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        return condition();
    }

    private static string Lines(Func<int, string?> change) => string.Concat(Enumerable.Range(1, 12).Select(i => (change(i) ?? $"line {i}") + "\n"));

    private void Init()
    {
        Git("init", "-q");
        Git("config", "user.name", "Test");
        Git("config", "user.email", "test@example.invalid");
        File.WriteAllText(Path.Combine(_repo, "a.txt"), Lines(_ => null));
        File.WriteAllText(Path.Combine(_repo, "b.txt"), Lines(_ => null));
        Git("add", ".");
        Git("commit", "-q", "-m", "first");
    }

    private static void Tick(Avalonia.Controls.Primitives.ToggleButton box) => box.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));

    [AvaloniaFact]
    public async Task A_ticked_file_and_a_ticked_change_put_exactly_that_in_the_index_and_the_commit_holds_only_it()
    {
        Assert.SkipWhen(ToolLocator.Find("git") is null, "git is not installed here (not found in PATH).");
        Loc.SetLanguage("en");
        Init();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), Lines(i => i switch { 2 => "line 2 changed", 10 => "line 10 changed", _ => null }));
        File.WriteAllText(Path.Combine(_repo, "b.txt"), Lines(i => i == 3 ? "line 3 changed" : null));
        File.WriteAllText(Path.Combine(_repo, "n.txt"), "new\n");

        var view = await _w.OpenCommitAsync(_repo);
        Assert.NotNull(view);
        Assert.True(await Eventually(() => view.Changes is { Entries.Count: 3 }));
        Assert.Equal(["Changes (2)", "Unversioned files (1)"], view.GroupNames);
        Assert.True(_w.IsPanelOpen(SidePanel.Commit));

        // one change of a.txt: the second box of the comparison of what is not staged
        Assert.True(view.Select("a.txt"));
        Assert.True(await Eventually(() => view.UnstagedHunkBoxes.Count == 2));
        Tick(view.UnstagedHunkBoxes[1]);
        Assert.True(await Eventually(() => Git("diff", "--cached", "--name-only").Contains("a.txt", StringComparison.Ordinal)));
        string cached = Git("diff", "--cached", "-U0", "--no-color");
        Assert.Equal(1, cached.Split('\n').Count(l => l.StartsWith("@@ ", StringComparison.Ordinal)));
        Assert.Contains("+line 10 changed\n", cached);
        Assert.DoesNotContain("line 2 changed", cached);

        // a whole file: the box of b.txt (once the panel has read the repository again; a click while it works is ignored)
        Assert.True(await Eventually(() => view.CheckOf("a.txt")!.IsChecked is null));
        Tick(view.CheckOf("b.txt")!);
        Assert.True(await Eventually(() => Git("diff", "--cached", "--name-only").Contains("b.txt", StringComparison.Ordinal)));
        Assert.True(await Eventually(() => view.CheckOf("b.txt")!.IsChecked == true && view.CheckOf("a.txt")!.IsChecked is null));

        view.MessageBox.Text = "Change two files\n\nOnly a part of the first.";
        Assert.True(await view.CommitAsync(push: false));
        Assert.Equal("Change two files\n\nOnly a part of the first.", Git("log", "-1", "--format=%B").TrimEnd('\n'));
        Assert.Equal(["a.txt", "b.txt"], Git("show", "--name-only", "--format=", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries).Order());
        string left = Git("status", "--porcelain");
        Assert.Contains(" M a.txt", left); // line 2 is still waiting
        Assert.Contains("?? n.txt", left);

        // the message is offered again
        Assert.True(await Eventually(() => view.RecentBox.Items.Count > 0 && view.RecentBox.IsVisible));
        Assert.Equal("Change two files", (string)((Avalonia.Controls.ComboBoxItem)view.RecentBox.Items[0]!).Content!);
    }

    [AvaloniaFact]
    public async Task Amend_replaces_the_last_commit_and_Commit_and_Push_sends_it_to_the_remote_after_the_plugin_asked()
    {
        Assert.SkipWhen(ToolLocator.Find("git") is null, "git is not installed here (not found in PATH).");
        Loc.SetLanguage("en");
        Init();
        string bare = Path.Combine(_temp.Path, "remote.git");
        Directory.CreateDirectory(bare);
        GitIn(bare, "init", "-q", "--bare");
        Git("remote", "add", "origin", bare);
        Git("push", "-q", "-u", "origin", "HEAD");
        int commits = int.Parse(Git("rev-list", "--count", "HEAD").Trim());

        File.WriteAllText(Path.Combine(_repo, "a.txt"), Lines(i => i == 5 ? "line 5 changed" : null));
        var view = await _w.OpenCommitAsync(_repo);
        Assert.NotNull(view);
        Assert.True(await Eventually(() => view.Changes is { Entries.Count: 1 }));
        Tick(view.CheckOf("a.txt")!);
        Assert.True(await Eventually(() => view.CheckOf("a.txt")!.IsChecked == true));
        view.MessageBox.Text = "Change line five";
        _dialogs.Answers.Enqueue(0); // the plugin asks before it pushes
        Assert.True(await view.CommitAsync(push: true));
        Assert.True(await Eventually(() => GitIn(bare, "log", "-1", "--format=%s").Trim() == "Change line five"));
        Assert.Equal(commits + 1, int.Parse(Git("rev-list", "--count", "HEAD").Trim()));

        view.AmendBox.IsChecked = true;
        Assert.Equal("Change line five", view.MessageBox.Text); // the old message comes into the box
        view.MessageBox.Text = "Change line five, better";
        Assert.True(await view.CommitAsync(push: false));
        Assert.Equal("Change line five, better", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal(commits + 1, int.Parse(Git("rev-list", "--count", "HEAD").Trim())); // replaced, not added
    }

    /// <summary>A bare repository as <c>origin</c> of the repository, with a clone of another person who pushes to it.</summary>
    private string WithRemote()
    {
        string bare = Path.Combine(_temp.Path, "remote.git");
        Directory.CreateDirectory(bare);
        GitIn(bare, "init", "-q", "--bare");
        GitIn(bare, "symbolic-ref", "HEAD", "refs/heads/" + Git("branch", "--show-current").Trim());
        Git("remote", "add", "origin", bare);
        Git("push", "-q", "-u", "origin", "HEAD");
        string other = Path.Combine(_temp.Path, "other");
        GitIn(_temp.Path, "clone", "-q", bare, other);
        GitIn(other, "config", "user.name", "Other");
        GitIn(other, "config", "user.email", "other@example.invalid");
        return other;
    }

    private static void TheyPush(string other, string file)
    {
        File.WriteAllText(Path.Combine(other, file), file + "\n");
        GitIn(other, "add", "-A");
        GitIn(other, "commit", "-q", "-m", "theirs " + file);
        GitIn(other, "push", "-q");
    }

    private void Mine(string file)
    {
        File.WriteAllText(Path.Combine(_repo, file), file + "\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "mine " + file);
    }

    private string GitStatusText => _app.Plugins.Ui.Status.TryGetValue(NksHub.Textida.App.Services.PluginServices.GitPluginId, out var s) ? s.Text : "";

    [AvaloniaFact]
    public async Task Pull_asks_once_with_a_choice_to_remember_keeps_it_in_the_settings_and_then_combines_that_way_without_asking()
    {
        Assert.SkipWhen(ToolLocator.Find("git") is null, "git is not installed here (not found in PATH).");
        Loc.SetLanguage("en");
        Init();
        string other = WithRemote();
        Assert.Equal("ask", _app.Workbench.Settings.GetString("git.pullMode"));

        TheyPush(other, "t1.txt");
        Mine("m1.txt");
        var pulling = _w.PullAsync(_repo);
        Assert.True(await Eventually(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice));
        Assert.Equal(["Merge", "Rebase", "Merge and remember", "Rebase and remember"], _w.QuickResults.Select(r => r.Title));
        _w.MovePalette(3);
        _w.RunPaletteSelection();
        Assert.True(await Eventually(() => pulling.IsCompleted));
        Assert.Equal("pulled", (await pulling)!.Value.GetProperty("status").GetString());
        Assert.Equal("rebase", _app.Workbench.Settings.GetString("git.pullMode"));
        Assert.Equal("", Git("log", "--merges", "--oneline").Trim());

        TheyPush(other, "t2.txt");
        Mine("m2.txt");
        pulling = _w.PullAsync(_repo);
        Assert.True(await Eventually(() => pulling.IsCompleted || _w.IsPaletteOpen));
        Assert.False(_w.IsPaletteOpen); // remembered: no question
        Assert.Equal("rebase", (await pulling)!.Value.GetProperty("mode").GetString());
        Assert.Equal(["mine m2.txt", "mine m1.txt", "theirs t2.txt", "theirs t1.txt", "first"], Git("log", "--format=%s").Split('\n', StringSplitOptions.RemoveEmptyEntries));

        // nothing new: told so, no question
        Assert.Equal("upToDate", (await _w.PullAsync(_repo))!.Value.GetProperty("status").GetString());
        Assert.False(_w.IsPaletteOpen);
    }

    [AvaloniaFact]
    public async Task The_background_fetch_stays_off_when_switched_off_or_offline_and_offers_an_update_for_new_commits()
    {
        Assert.SkipWhen(ToolLocator.Find("git") is null, "git is not installed here (not found in PATH).");
        Loc.SetLanguage("en");
        Init();
        string other = WithRemote();
        Assert.Equal(10, _app.Workbench.Settings.GetInt("git.autoFetchMinutes"));
        Assert.NotNull(await _w.OpenCommitAsync(_repo));
        TheyPush(other, "t1.txt");
        string tracked = Git("rev-parse", "@{u}").Trim();

        _app.Workbench.Set("git.autoFetchMinutes", System.Text.Json.Nodes.JsonValue.Create(0));
        Assert.Equal(GitAutoFetch.Off, await _w.AutoFetchOnceAsync());
        _app.Workbench.Set("git.autoFetchMinutes", System.Text.Json.Nodes.JsonValue.Create(10));
        _w.NetworkAvailable = () => false;
        Assert.Equal(GitAutoFetch.Offline, await _w.AutoFetchOnceAsync());
        Assert.Equal(tracked, Git("rev-parse", "@{u}").Trim()); // nothing was fetched

        _w.NetworkAvailable = () => true;
        Assert.Equal(GitAutoFetch.Fetched, await _w.AutoFetchOnceAsync());
        Assert.NotEqual(tracked, Git("rev-parse", "@{u}").Trim());
        Assert.False(File.Exists(Path.Combine(_repo, "t1.txt"))); // a fetch changes no file
        Assert.True(await Eventually(() => GitStatusText.Contains("↓1", StringComparison.Ordinal)));
        var bar = Assert.Single(_w.Notices, n => n.Message.Contains("new commits", StringComparison.Ordinal));
        Assert.Equal(GitAutoFetch.Fetched, await _w.AutoFetchOnceAsync());
        Assert.Single(_w.Notices, n => n.Message.Contains("new commits", StringComparison.Ordinal)); // told once

        bar.Buttons.Single(b => (string?)b.Content == "Update").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(await Eventually(() => File.Exists(Path.Combine(_repo, "t1.txt")) && !_w.Notices.Any(n => n.Message.Contains("new commits", StringComparison.Ordinal))));
        Assert.True(await Eventually(() => !GitStatusText.Contains('↓')));
    }

    [AvaloniaFact]
    public async Task The_checks_before_a_commit_are_kept_for_the_next_commit_panel()
    {
        Assert.SkipWhen(ToolLocator.Find("git") is null, "git is not installed here (not found in PATH).");
        Init();
        var view = await _w.OpenCommitAsync(_repo);
        Assert.NotNull(view);
        Assert.False(view.SaveCheckBox.IsChecked);
        Assert.False(view.FormatCheckBox.IsChecked);
        view.SaveCheckBox.IsChecked = true;
        Assert.True(_app.Workbench.Settings.GetBool("git.commitSaveAll"));
        Assert.False(_app.Workbench.Settings.GetBool("git.commitFormat"));

        var second = _app.CreateWindow(null);
        var again = await second.OpenCommitAsync(_repo);
        Assert.NotNull(again);
        Assert.True(again.SaveCheckBox.IsChecked);
        Assert.False(again.FormatCheckBox.IsChecked);

        _app.Workbench.Set("git.commitFormat", System.Text.Json.Nodes.JsonValue.Create(true)); // changed in the settings: the open panel follows
        Assert.True(again.FormatCheckBox.IsChecked);
    }
}
