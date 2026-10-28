using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Tests.RealAgents;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.History;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.1.1 with the real programs that change files: a script, the real Claude Code (print mode) and the real Codex write into a folder the window watches, and the change shows up in the change
/// review hunk by hunk, with the content the history held as the base. The watch is the real one (the operating system reports the write); nothing here calls the review by hand.
/// The programs work in a folder below the temporary folder; Claude Code is limited to its Read and Edit tools there (<c>--restricted</c>), Codex to its workspace sandbox.
/// </summary>
[Collection("LocState")]
[Trait("Category", "RealAgents")]
public class ReviewRealAgentsTests : IDisposable
{
    private const string Before = "line 1\nline 2\nline 3\nline 4\nline 5\nline 6\nline 7\nline 8\nline 9\nline 10\nline 11\nline 12\n";
    private const string ByScript = "LINE ONE\nline 2\nline 3\nline 4\nline 5\nline 6\nline 7\nline 8\nline 9\nline 10\nline 11\nLINE TWELVE\n";
    private const string Prompt = "In a.txt change the text of the first line to LINE ONE and the text of the last line to LINE TWELVE. Edit the file in place. Reply done.";

    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public ReviewRealAgentsTests()
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

    private static async Task Until(Func<bool> condition, string what, int seconds = 30)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < seconds * 1000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    /// <summary>A folder the window watches, with a file whose content the history holds as the last one the user saw.</summary>
    private async Task<(string Folder, string Path)> WatchedFile()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        string path = Path.Combine(folder, "a.txt");
        File.WriteAllText(path, Before);
        Assert.Equal(HistoryStatus.Recorded, _app.History.Record(path, File.ReadAllBytes(path), HistorySource.Save).Status);
        Assert.True(await _w.ShowFolderAsync(folder));
        return (folder, path);
    }

    /// <summary>Waits for the watch to take the change in, opens the file from the list of waiting reviews and returns its tab with the hunks listed.</summary>
    private async Task<(DocTab Tab, int Hunks)> OpenWaitingReview(string path)
    {
        await Until(() => _app.Reviews.Find(path) is not null, "the folder watch to report the change", 60);
        var state = _app.Reviews.Find(path)!;
        Assert.Equal(Before, Encoding.UTF8.GetString(_app.History.Read(path, state.BaseRevisionId))); // the base is what the history held before the program wrote
        Assert.Equal(["review"], Assert.Single(_app.History.List(path), r => r.Pinned).Pins);
        Assert.DoesNotContain(_w.Documents, d => d.Path == path); // no tab took the change

        Assert.Equal(CommandOutcomeHandled, _w.ShowFolderReviews());
        Assert.Equal("a.txt", Assert.Single(_w.QuickResults).Title);
        _w.QuickResults[0].Run();
        Dispatcher.UIThread.RunJobs();
        var tab = Assert.Single(_w.Tabs, t => t.Path == path);
        var hunks = _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().Count();
        Assert.Equal($"± {hunks}", tab.Doc.ReviewBadge);
        return (tab, hunks);
    }

    private const NksHub.Textida.Workbench.Commands.CommandOutcome CommandOutcomeHandled = NksHub.Textida.Workbench.Commands.CommandOutcome.Handled;

    /// <summary>Reverts the whole file in the review: the base is written back with the ordinary save, and the record is gone.</summary>
    private async Task RevertAll(string path, DocTab tab)
    {
        _w.ResultsView!.Rows.OfType<SearchResultsView.FileRow>().Single().Selected = false;
        _w.ApplyPlan();
        await Until(() => File.ReadAllText(path) == Before && !tab.Doc.Doc.IsModified, "the base content to be saved");
        Assert.Null(_app.Reviews.Find(path));
        Assert.DoesNotContain(_app.History.List(path), r => r.Pinned);
    }

    [AvaloniaFact]
    public async Task A_script_that_rewrites_a_file_of_the_watched_folder_shows_up_hunk_by_hunk_and_a_single_hunk_can_be_reverted()
    {
        var (folder, path) = await WatchedFile();
        string source = Path.Combine(_temp.Path, "by-script.txt");
        File.WriteAllText(source, ByScript);
        var (program, args) = OperatingSystem.IsWindows() ? ("cmd.exe", new[] { "/d", "/c", "copy", "/y", source, path }) : ("cp", [source, path]);
        var run = await RealTools.RunAsync(RealTools.Find(Path.GetFileNameWithoutExtension(program)) ?? program, args, folder, TimeSpan.FromSeconds(30));
        Assert.Equal(0, run.ExitCode);

        var (tab, hunks) = await OpenWaitingReview(path);
        Assert.Equal(2, hunks); // the first and the last line are far apart

        // the first hunk goes back to the base, the second stays
        _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>().First().Selected = false;
        _w.ApplyPlan();
        await Until(() => File.ReadAllText(path) == ByScript.Replace("LINE ONE", "line 1"), "only the first hunk to be reverted");
    }

    [AvaloniaFact]
    public async Task A_change_by_the_real_Claude_Code_shows_up_hunk_by_hunk_and_reverting_it_writes_the_base_back()
    {
        var claude = RealTools.Require("claude");
        var (folder, path) = await WatchedFile();
        var run = await RealTools.RunAsync(claude, ["-p", "--restricted", "--strict-mcp-config", "--tools", "Read,Edit", "--permission-mode", "acceptEdits", "--model", "haiku",
            "--no-session-persistence", "--max-budget-usd", "0.5", Prompt], folder, TimeSpan.FromMinutes(3));
        Assert.False(run.TimedOut, run.Output);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(ByScript, File.ReadAllText(path)); // the program did what was asked

        var (tab, hunks) = await OpenWaitingReview(path);
        Assert.Equal(2, hunks);
        await RevertAll(path, tab);
    }

    [AvaloniaFact]
    public async Task A_change_by_the_real_Codex_shows_up_hunk_by_hunk_and_reverting_it_writes_the_base_back()
    {
        var codex = RealTools.Require("codex");
        var (folder, path) = await WatchedFile();
        // the sandbox of the workspace on Windows needs "unelevated" here: without it Codex falls back to read-only and writes nothing
        var run = await RealTools.RunAsync(codex, ["exec", "--skip-git-repo-check", "--ephemeral", "--ignore-user-config", "--ignore-rules", "-s", "workspace-write", "-c", "windows.sandbox=unelevated",
            "-c", "model_reasoning_effort=low", "-C", folder, Prompt], folder, TimeSpan.FromMinutes(5));
        Assert.False(run.TimedOut, run.Output);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(ByScript, File.ReadAllText(path)); // the program did what was asked

        var (tab, hunks) = await OpenWaitingReview(path);
        Assert.Equal(2, hunks);
        await RevertAll(path, tab);
    }
}
