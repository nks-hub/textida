using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P8.8.1 in the window: PHPStan after a save, with a copy of ToolEcho named phpstan in vendor/bin that prints a recorded sample. The underline, its removal after a fix, the line in the Tasks tab, a missing
/// program, a missing time limit and a run over the limit. The real PHPStan is not installed here, so its command line and output are not verified.
/// </summary>
[Collection("LocState")]
public class LintAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _project;
    private readonly string _php;
    private readonly string _tool;

    private const string Found = """{"totals":{"errors":0,"file_errors":1},"files":{"{FILEJSON}":{"errors":1,"messages":[{"message":"Function foo not found.","line":3,"ignorable":true,"identifier":"function.notFound"}]}},"errors":[]}""";
    private const string Clean = """{"totals":{"errors":0,"file_errors":0},"files":[],"errors":[]}""";

    public LintAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _project = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        File.WriteAllText(Path.Combine(_project, "composer.json"), "{}");
        _php = Path.Combine(_project, "a.php");
        File.WriteAllText(_php, "<?php\n\nfoo($x);\n", new UTF8Encoding(false));
        string folder = Directory.CreateDirectory(Path.Combine(_project, "vendor", "bin")).FullName;
        foreach (var extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho" + extension), Path.Combine(folder, "NksHub.Textida.ToolEcho" + extension), overwrite: true);
        string exe = OperatingSystem.IsWindows() ? "NksHub.Textida.ToolEcho.exe" : "NksHub.Textida.ToolEcho";
        _tool = Path.Combine(folder, OperatingSystem.IsWindows() ? "phpstan.exe" : "phpstan");
        File.Copy(Path.Combine(AppContext.BaseDirectory, exe), _tool, overwrite: true);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string ActionsFile => Path.Combine(_app.ProfileDirectory, SaveActionConfig.FileName);

    private void Configure(int? lintTimeoutSeconds, bool lint = true, Dictionary<string, string>? toolPaths = null) =>
        SaveActionConfig.Write(ActionsFile, new SaveActionsFile { LintTimeoutSeconds = lintTimeoutSeconds, ToolPaths = toolPaths, Actions = lint ? [new SaveActionEntry { Kind = "lint", Id = "phpstan" }] : [] });

    private void Say(string sample) =>
        File.WriteAllText(_tool + ".sample", sample.Replace("{FILEJSON}", System.Text.Json.JsonSerializer.Serialize(_php)[1..^1]), new UTF8Encoding(false));

    /// <summary>The program asks for a yes before its first run, as every tool; here it has been given.</summary>
    private void Approve(string? userPath = null) => _app.ToolApprovals.Approve(Linters.Prepare(Linters.Find("phpstan")!, _php, TimeSpan.FromSeconds(5), userPath));

    private static async Task Until(Func<bool> condition, string what, int timeoutMs = 30_000)
    {
        for (var sw = Stopwatch.StartNew(); !condition(); await Task.Delay(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Waited for " + what);
        }
    }

    private async Task SaveEditedAsync(DocTab tab, string insert)
    {
        tab.Editor!.Document.Insert(tab.Editor!.Document.TextLength, insert);
        var task = _w.SaveAsync(tab, SaveOrigin.Manual);
        await Until(() => task.IsCompleted, "the save");
        Assert.True(await task);
    }

    private List<string> TaskLines() => [.. _app.Output.Read(OutputChannel.Tasks).Select(l => l.Text)];

    [AvaloniaFact]
    public async Task A_PHPStan_error_is_underlined_after_a_save_and_gone_after_the_fix_and_the_next_save()
    {
        Configure(60);
        Say(Found);
        Approve();
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");
        await Until(() => tab.Surface!.Diagnostics.Count == 1, "the underline");

        var mark = tab.Surface!.Diagnostics[0];
        string text = tab.Editor!.Document.Text;
        Assert.Equal("foo($x);", text[mark.Start..mark.End]); // PHPStan names a line and no column: the text of the line
        Assert.Equal(DiagnosticSeverity.Error, mark.Severity);
        Assert.Contains("Function foo not found.", mark.Message);
        string line = $"{Path.GetFullPath(_php)}:3:1: error: Function foo not found. [function.notFound]";
        Assert.Contains(line, TaskLines());
        Assert.NotNull(LogPattern.Compile(MakefileTargets.ErrorPattern, LogPatternSyntax.Regex).Match(line)); // a click on the line opens that place

        // a line typed above moves the underline with the text
        tab.Editor!.Document.Insert(0, "<?php // x\n");
        Assert.Equal("foo($x);", tab.Editor!.Document.Text[tab.Surface!.Diagnostics[0].Start..tab.Surface!.Diagnostics[0].End]);

        Say(Clean);
        await SaveEditedAsync(tab, "");
        await Until(() => tab.Surface!.Diagnostics.Count == 0, "the underline to go after the fix");
        Assert.Contains(Loc.Get(K.LintClean, ("name", "PHPStan")), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task Without_lintTimeoutSeconds_the_provisional_limit_applies_and_the_linter_runs()
    {
        Configure(null);
        Say(Found);
        Approve();
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");
        await Until(() => tab.Surface!.Diagnostics.Count == 1, "the underline");

        Assert.Equal(TimeSpan.FromSeconds(PluginLimitsPlaceholders.LintTimeoutSeconds), _w.LoadSaveActions(_php).LintTimeout);
    }

    [AvaloniaFact]
    public async Task A_missing_program_is_reported_and_the_save_is_not_held_up()
    {
        Assert.SkipWhen(ToolLocator.Find("phpstan") is not null, "PHPStan is installed in PATH here, so it is not missing.");
        Configure(60);
        File.Delete(_tool);
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");

        Assert.Equal(Loc.Get(K.LintMissing, ("name", "PHPStan"), ("program", "phpstan")), _w.StatusMessageText);
        Assert.Empty(tab.Surface!.Diagnostics);
    }

    [AvaloniaFact]
    public async Task A_linter_over_its_time_limit_is_reported_while_the_window_keeps_working()
    {
        Configure(1);
        Say("#sleep");
        Approve();
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");
        await Until(() => File.Exists(_tool + ".runs"), "the linter to start"); // a save that is typed over before the run starts is not linted
        tab.Editor!.Document.Insert(0, "typing while it runs "); // the editor accepts input meanwhile
        await Until(() => _w.StatusMessageText == Loc.Get(K.LintTimedOut, ("name", "PHPStan"), ("seconds", "1")), "the report of the time-out");

        Assert.Empty(tab.Surface!.Diagnostics);
    }

    [AvaloniaFact]
    public async Task Two_saves_in_a_row_start_one_run_because_the_newer_save_replaces_the_waiting_one()
    {
        Configure(60);
        Say(Found);
        Approve();
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");
        await SaveEditedAsync(tab, "\n");
        await Until(() => tab.Surface!.Diagnostics.Count == 1, "the underline");
        await Task.Delay(PluginLimitsPlaceholders.LintOnSaveDebounceMilliseconds + 300);

        Assert.Single(File.ReadAllLines(_tool + ".runs"));
    }

    [AvaloniaFact]
    public async Task Text_typed_while_the_linter_runs_moves_the_underline_with_the_text()
    {
        Configure(60);
        Say("#delay 1500\n" + Found);
        Approve();
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");
        await Until(() => File.Exists(_tool + ".runs"), "the linter to start");
        tab.Editor!.Document.Insert(0, "<?php // typed during the run\n");
        await Until(() => tab.Surface!.Diagnostics.Count == 1, "the underline");

        var mark = tab.Surface!.Diagnostics[0];
        Assert.Equal("foo($x);", tab.Editor!.Document.Text[mark.Start..mark.End]);
    }

    [AvaloniaFact]
    public async Task A_finding_whose_text_was_changed_while_the_linter_ran_is_dropped_and_the_report_says_so()
    {
        Configure(60);
        Say("#delay 1500\n" + Found);
        Approve();
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");
        await Until(() => File.Exists(_tool + ".runs"), "the linter to start");
        tab.Editor!.Document.Replace(tab.Editor!.Document.Text.IndexOf("foo", StringComparison.Ordinal), 3, "bar");
        await Until(() => _w.StatusMessageText == Loc.Get(K.LintStale, ("number", 1)), "the report that the finding is stale");

        Assert.Empty(tab.Surface!.Diagnostics);
        Assert.Contains(Loc.Get(K.LintStale, ("number", 1)), TaskLines());
    }

    [AvaloniaFact]
    public async Task A_program_path_from_the_user_in_the_profile_finds_a_linter_that_is_not_in_the_project()
    {
        string mine = Directory.CreateDirectory(Path.Combine(_temp.Path, "mine")).FullName;
        foreach (var extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho" + extension), Path.Combine(mine, "NksHub.Textida.ToolEcho" + extension), overwrite: true);
        string own = Path.Combine(mine, Path.GetFileName(_tool));
        File.Move(_tool, own);
        Configure(60, toolPaths: new Dictionary<string, string> { ["phpstan"] = own });
        File.WriteAllText(own + ".sample", Found.Replace("{FILEJSON}", System.Text.Json.JsonSerializer.Serialize(_php)[1..^1]), new UTF8Encoding(false));
        Approve(own);
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");
        await Until(() => tab.Surface!.Diagnostics.Count == 1, "the underline");

        Assert.Equal("foo($x);", tab.Editor!.Document.Text[tab.Surface!.Diagnostics[0].Start..tab.Surface!.Diagnostics[0].End]);
    }

    [AvaloniaFact]
    public async Task A_version_the_check_was_not_written_for_is_said_next_to_the_result_and_the_findings_still_show()
    {
        Configure(60);
        Say(Found);
        File.WriteAllText(_tool + ".version", "PHPStan - PHP Static Analysis Tool 0.12.99\n");
        Approve();
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");
        await Until(() => tab.Surface!.Diagnostics.Count == 1, "the underline");

        string old = Loc.Get(K.LintVersionOld, ("name", "PHPStan"), ("found", "0.12.99"), ("min", "1.0.0"));
        Assert.Contains(old, _w.StatusMessageText);
        Assert.Contains(Loc.Get(K.LintFound, ("name", "PHPStan"), ("number", 1)), _w.StatusMessageText);
        Assert.Contains(_app.Output.Read(OutputChannel.Tasks).Select(l => l.Text), t => t.Contains(old, StringComparison.Ordinal));
        Assert.Single(File.ReadAllLines(_tool + ".runs")); // the version request left no run behind
    }

    [AvaloniaFact]
    public async Task A_version_inside_the_window_adds_nothing_and_a_failed_run_with_a_newer_version_names_it_as_a_reason()
    {
        Configure(60);
        Say(Found);
        File.WriteAllText(_tool + ".version", "PHPStan - PHP Static Analysis Tool 2.1.0\n");
        Approve();
        var tab = _w.OpenFile(_php, null);

        await SaveEditedAsync(tab, "\n");
        await Until(() => tab.Surface!.Diagnostics.Count == 1, "the underline");
        Assert.DoesNotContain("2.1.0", _w.StatusMessageText);

        File.WriteAllText(_tool + ".version", "PHPStan - PHP Static Analysis Tool 3.4.0\n"); // another time stamp is another file of the program
        File.SetLastWriteTimeUtc(_tool, DateTime.UtcNow.AddMinutes(1));
        Say("not what the documentation says");
        await SaveEditedAsync(tab, "\n");
        string news = Loc.Get(K.LintVersionNew, ("name", "PHPStan"), ("found", "3.4.0"), ("min", "1.0.0"), ("below", "3.0.0"));
        await Until(() => _w.StatusMessageText.Contains(news, StringComparison.Ordinal), "the report with the version");

        Assert.StartsWith(Loc.Get(K.LintFailed, ("name", "PHPStan"), ("reason", "")).TrimEnd(), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task A_linter_is_offered_in_the_save_actions_and_off_until_switched_on()
    {
        var tab = _w.OpenFile(_php, null);

        _w.ShowSaveActions();
        var offered = Assert.Single(_w.QuickResults, r => r.Title == Loc.Get(K.SaveActionsLint, ("name", "PHPStan")));
        Assert.Contains(Loc.Get(K.SaveActionsOfferedLint), offered.Detail!);
        Assert.False(File.Exists(ActionsFile));

        offered.Run();
        var action = Assert.Single(SaveActionConfig.Load(ActionsFile, null).Actions);
        Assert.Equal((SaveActionKind.Lint, "phpstan", true), (action.Kind, action.Id, action.Enabled));
        _w.CloseTab(tab);
        await Task.CompletedTask;
    }
}
