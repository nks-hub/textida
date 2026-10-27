using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Tests;

/// <summary>Shared helpers of the tests that run tools and read logs.</summary>
internal static class ToolTestKit
{
    public static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    public static string EchoDll => Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll");

    public static void Pump()
    {
        Thread.Sleep(25);
        Dispatcher.UIThread.RunJobs();
    }

    public static void WaitFor(Func<bool> done, string what, int rounds = 1200)
    {
        for (int i = 0; i < rounds && !done(); i++) Pump();
        Assert.True(done(), what);
    }

    /// <summary>The part of a localised sentence before its last placeholder, so a test can recognise a line whose number it cannot know.</summary>
    public static string Before(string key, params (string Name, object Value)[] args) =>
        Loc.Get(key, [.. args, ("seconds", "\u0001")]).Split('\u0001')[0];

    public static JsonObject Tool(string id, string title, IEnumerable<string> args, string? cwd = null, int? timeout = null, string? pattern = null)
    {
        var tool = new JsonObject
        {
            ["id"] = id,
            ["title"] = title,
            ["executable"] = Dotnet,
            ["args"] = new JsonArray([.. new[] { EchoDll }.Concat(args).Select(a => (JsonNode?)JsonValue.Create(a))]),
        };
        if (cwd is not null) tool["cwd"] = cwd;
        if (timeout is { } t) tool["timeoutSeconds"] = t;
        if (pattern is not null) tool["logPattern"] = pattern;
        return tool;
    }
}

/// <summary>P4.3.3 and P4.3.1 in the app: the launcher with its confirmation, the output window and the click on an error.</summary>
[Collection("LocState")]
public class ToolLauncherTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public ToolLauncherTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        _w.StopTool();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private void Define(JsonArray tools, JsonArray? patterns = null)
    {
        var root = new JsonObject { ["externalTools"] = tools, ["logPatterns"] = patterns ?? [] };
        File.WriteAllText(_app.Tools.FilePath, root.ToJsonString());
    }

    private ToolEntry Entry(string id) => _app.Tools.Load().Tools.Single(t => t.Spec.Id == id);

    private Task Run(string id) => _w.RunToolAsync(Entry(id));

    private void Finish(Task task) => ToolTestKit.WaitFor(() => task.IsCompleted, "the tool did not finish");

    private List<string> Lines() => [.. _app.Output.Read(OutputChannel.Tasks).Select(l => l.Text)];

    [AvaloniaFact]
    public void The_first_run_shows_command_and_directory_and_every_variable_is_one_argument()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "my docs")).FullName;
        string file = Path.Combine(folder, "a file.txt");
        File.WriteAllText(file, "x");
        _w.OpenFile(file, null);
        Define([ToolTestKit.Tool("echo", "Echo", ["${File}", "a \"quoted\" & b", "${FileBase}|${FileExt}"], cwd: "${FileDir}")]);
        _dialogs.Answers.Enqueue(0);

        Finish(Run("echo"));

        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains(asked.Details!, d => d.Contains(folder, StringComparison.Ordinal));
        Assert.Contains(asked.Details!, d => d.Contains(ToolTestKit.EchoDll, StringComparison.Ordinal));
        var lines = Lines();
        Assert.Contains($"[{file}]", lines);
        Assert.Contains("[a \"quoted\" & b]", lines);
        Assert.Contains("[a file|.txt]", lines);
        Assert.StartsWith(ToolTestKit.Before(K.ToolOutFinished, ("code", 0)), lines[^1]);
        Assert.True(_w.OutputPanelOpen);
        Assert.Equal(OutputChannel.Tasks, _w.ShownOutputChannel);
        Assert.Contains("[a file|.txt]", _w.OutputPanelText);
        Assert.False(_w.ToolRunningNow);
    }

    [AvaloniaFact]
    public void An_approved_tool_runs_without_asking_and_a_changed_definition_asks_again()
    {
        Define([ToolTestKit.Tool("echo", "Echo", ["one"])]);
        _dialogs.Answers.Enqueue(0);
        Finish(Run("echo"));
        Finish(Run("echo"));
        Assert.Single(_dialogs.Asked);
        Assert.Equal(2, Lines().Count(l => l == "[one]"));

        // a new run of the app with the same profile still knows the answer
        Assert.True(new ToolApprovals(Path.Combine(_app.ProfileDirectory, "tool-approvals.txt")).IsApproved(ToolRunnerResolve(Entry("echo"))));

        Define([ToolTestKit.Tool("echo", "Echo", ["two"])]);
        _dialogs.Answers.Enqueue(1);
        int before = Lines().Count;
        Finish(Run("echo"));
        Assert.Equal(2, _dialogs.Asked.Count);
        Assert.Equal(before, Lines().Count);
        Assert.DoesNotContain("[two]", Lines());
    }

    private static ToolInvocation ToolRunnerResolve(ToolEntry entry) => ToolRunner.Resolve(entry.Spec, _ => null, Environment.CurrentDirectory);

    [AvaloniaFact]
    public void An_unknown_variable_is_reported_before_anything_starts_and_asks_nothing()
    {
        Define([ToolTestKit.Tool("bad", "Bad", ["${Nope}"])]);
        Finish(Run("bad"));
        Assert.Empty(_dialogs.Asked);
        Assert.Contains("Nope", Assert.Single(Lines()));
        Assert.Contains(Loc.Get(K.ToolCannotRun, ("title", "Bad"), ("reason", "")).Split(' ')[0], _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void Exceeding_the_time_limit_ends_the_tool_and_reports_it()
    {
        Define([ToolTestKit.Tool("slow", "Slow", ["--sleep"], timeout: 1)]);
        _dialogs.Answers.Enqueue(0);
        Finish(Run("slow"));
        var lines = Lines();
        Assert.Contains("started", lines);
        Assert.Equal(Loc.Get(K.ToolOutTimedOut, ("seconds", "1")), lines[^1]);
        Assert.False(_w.ToolRunningNow);
        Assert.False(_w.OutputStopButton.IsVisible);
    }

    [AvaloniaFact]
    public void Stop_ends_a_running_tool_and_keeps_its_output()
    {
        Define([ToolTestKit.Tool("slow", "Slow", ["--sleep"])]);
        _dialogs.Answers.Enqueue(0);
        var task = Run("slow");
        ToolTestKit.WaitFor(() => Lines().Contains("started"), "the tool did not start");
        Assert.True(_w.ToolRunningNow);
        Assert.True(_w.OutputStopButton.IsVisible);
        Assert.NotEmpty(Loc.Get(K.PanelOutputStop));

        // a second tool does not start beside it
        Define([ToolTestKit.Tool("slow", "Slow", ["--sleep"]), ToolTestKit.Tool("echo", "Echo", ["x"])]);
        Finish(Run("echo"));
        Assert.Equal(Loc.Get(K.ToolBusy, ("title", "Slow")), _w.StatusMessageText);

        Assert.Equal(NksHub.Textida.Workbench.Commands.CommandOutcome.Handled, _w.StopTool());
        Finish(task);
        Assert.StartsWith(ToolTestKit.Before(K.ToolOutCancelled), Lines()[^1]);
        Assert.Contains("started", Lines());
        Assert.False(_w.OutputStopButton.IsVisible);
        Assert.Equal(NksHub.Textida.Workbench.Commands.CommandOutcome.NotHandled, _w.StopTool());
    }

    // ---- clicking an error ----

    private string Project()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "src dir")).FullName;
        File.WriteAllText(Path.Combine(folder, "a.c"), "one\ntwo\nthree is here\nfour\nfive\n");
        return folder;
    }

    private int OutputLineIndex(string contains) => Array.FindIndex(_w.OutputPanelText.Split('\n'), l => l.Contains(contains, StringComparison.Ordinal));

    [AvaloniaFact]
    public void Clicking_an_error_line_opens_the_file_at_the_line_and_column_the_pattern_found()
    {
        string folder = Project();
        Define([ToolTestKit.Tool("cc", "Compile", ["a.c:3:4: error: bad", "not an error"], cwd: folder, pattern: "gcc")],
            [new JsonObject { ["id"] = "gcc", ["title"] = "gcc", ["syntax"] = "mask", ["pattern"] = "[%F:%L:%C:*" }]);
        _dialogs.Answers.Enqueue(0);
        Finish(Run("cc"));

        int bad = OutputLineIndex("[a.c:3:4: error: bad]");
        Assert.True(bad >= 0);
        Assert.True(_w.OpenOutputLine(bad));
        Assert.Equal(Path.Combine(folder, "a.c"), _w.Active!.Path);
        Assert.Equal((3L, 4L), _w.Active.CaretPosition());

        Assert.False(_w.OpenOutputLine(OutputLineIndex("[not an error]")));
        Assert.Equal(Loc.Get(K.LogNoPlace), _w.StatusMessageText);
        Assert.False(_w.OpenOutputLine(-1));
        Assert.False(_w.OpenOutputLine(10_000));
    }

    [AvaloniaFact]
    public void Enter_in_the_output_opens_the_place_of_the_line_with_the_caret()
    {
        string folder = Project();
        Define([ToolTestKit.Tool("cc", "Compile", ["a.c:5:1: error: x"], cwd: folder, pattern: "gcc")],
            [new JsonObject { ["id"] = "gcc", ["title"] = "gcc", ["syntax"] = "mask", ["pattern"] = "[%F:%L:%C:*" }]);
        _dialogs.Answers.Enqueue(0);
        Finish(Run("cc"));
        _w.NewDocument();
        ToolTestKit.Pump();

        string text = _w.OutputTextBox.Text!;
        _w.OutputTextBox.CaretIndex = text.IndexOf("[a.c:5:1", StringComparison.Ordinal) + 3;
        _w.OutputTextBox.Focus();
        TestApp.Press(_w, Key.Enter);

        Assert.Equal(Path.Combine(folder, "a.c"), _w.Active!.Path);
        Assert.Equal((5L, 1L), _w.Active.CaretPosition());
    }

    [AvaloniaFact]
    public void A_mouse_click_on_an_error_line_in_the_output_window_opens_the_place()
    {
        string folder = Project();
        Define([ToolTestKit.Tool("cc", "Compile", ["a.c:4:2: error: x"], cwd: folder, pattern: "gcc")],
            [new JsonObject { ["id"] = "gcc", ["title"] = "gcc", ["syntax"] = "mask", ["pattern"] = "[%F:%L:%C:*" }]);
        _dialogs.Answers.Enqueue(0);
        _w.Width = 1000;
        _w.Height = 700;
        _w.Show();
        Finish(Run("cc"));
        _w.NewDocument();
        _w.UpdateLayout();
        ToolTestKit.Pump();

        var presenter = _w.OutputTextBox.GetVisualDescendants().OfType<TextPresenter>().Single();
        presenter.ApplyTemplate();
        int index = _w.OutputTextBox.Text!.IndexOf("[a.c:4:2", StringComparison.Ordinal) + 4;
        var rect = presenter.TextLayout.HitTestTextPosition(index);
        var point = presenter.TranslatePoint(new Point(rect.X + 2, rect.Y + rect.Height / 2), _w)!.Value;

        _w.MouseDown(point, MouseButton.Left);
        _w.MouseUp(point, MouseButton.Left);
        ToolTestKit.Pump();

        Assert.Equal(Path.Combine(folder, "a.c"), _w.Active!.Path);
        Assert.Equal((4L, 2L), _w.Active.CaretPosition());
    }

    [AvaloniaFact]
    public void A_file_the_output_names_is_looked_for_where_the_tool_ran_then_beside_the_open_file_then_among_open_documents()
    {
        string folder = Project();
        string other = Directory.CreateDirectory(Path.Combine(_temp.Path, "beside")).FullName;
        File.WriteAllText(Path.Combine(other, "b.c"), "1\n2\n3\n");
        var open = _w.OpenFile(Path.Combine(other, "note.txt").Also(p => File.WriteAllText(p, "n")), null);
        Define([ToolTestKit.Tool("cc", "Compile", ["b.c:2:1: e", "a.c:1:1: e", "missing.c:1:1: e"], cwd: folder, pattern: "gcc")],
            [new JsonObject { ["id"] = "gcc", ["title"] = "gcc", ["syntax"] = "mask", ["pattern"] = "[%F:%L:%C:*" }]);
        _dialogs.Answers.Enqueue(0);
        Finish(Run("cc"));

        Assert.True(_w.OpenOutputLine(OutputLineIndex("[b.c:2")));
        Assert.Equal(Path.Combine(other, "b.c"), _w.Active!.Path);
        Assert.True(_w.OpenOutputLine(OutputLineIndex("[a.c:1")));
        Assert.Equal(Path.Combine(folder, "a.c"), _w.Active.Path);
        Assert.False(_w.OpenOutputLine(OutputLineIndex("[missing.c")));
        Assert.Equal(Loc.Get(K.LogFileMissing, ("file", "missing.c")), _w.StatusMessageText);
        Assert.NotNull(open);
    }

    [AvaloniaFact]
    public void The_pattern_tester_shows_what_a_custom_pattern_finds_in_a_sample_and_hands_it_to_the_output()
    {
        string folder = Project();
        var window = new LogPatternWindow("%F(%L,%C)", LogPatternSyntax.Mask, "build started\nProgram.cs(12,5): error CS1002: ; expected\nlib/util.cs(7,1): warning\ndone");
        LogPattern? used = null;
        window.Used += p => used = p;

        var rows = window.ResultLines;
        Assert.Equal(4, rows.Count);
        Assert.Equal(Loc.Get(K.LogPatternPlaceColumn, ("number", 2), ("file", "Program.cs"), ("line", 12), ("column", 5)), rows[1]);
        Assert.Equal(Loc.Get(K.LogPatternPlaceColumn, ("number", 3), ("file", "lib/util.cs"), ("line", 7), ("column", 1)), rows[2]);
        Assert.Equal(Loc.Get(K.LogPatternNoPlace, ("number", 1), ("text", "build started")), rows[0]);
        Assert.Equal(Loc.Plural(K.LogPatternFound, 2), window.NoteText);
        Assert.True(window.CanUse);

        window.PatternText = "%F:%X";
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.CanUse);
        Assert.Contains("%X", window.NoteText);
        window.UseNow();
        Assert.Null(used);

        window.Syntax = LogPatternSyntax.Regex;
        window.PatternText = @"^(?<file>[^(]+)\((?<line>\d+),(?<column>\d+)\)";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Plural(K.LogPatternFound, 2), window.NoteText);
        window.PatternText = "%F:%L";
        window.Syntax = LogPatternSyntax.Mask;
        Dispatcher.UIThread.RunJobs();
        window.SampleText = "a.c:3: oops\nsrc dir";
        window.UseNow();
        Assert.NotNull(used);

        // the pattern the window handed over drives the clicks in the output
        _app.Output.Append(OutputChannel.Tasks, "manual", "a.c:2: something");
        _w.OpenFile(Path.Combine(folder, "a.c"), null);
        _w.UseLogPattern(used!);
        _w.ShowOutput(OutputChannel.Tasks);
        Assert.Equal(Loc.Get(K.LogPatternUsed), _w.StatusMessageText);
        Assert.True(_w.OpenOutputLine(OutputLineIndex("a.c:2: something")));
        Assert.Equal((2L, 1L), _w.Active!.CaretPosition());
    }

    [AvaloniaFact]
    public void The_catalog_keeps_the_valid_tools_and_names_the_invalid_ones()
    {
        File.WriteAllText(_app.Tools.FilePath, """
            {"externalTools": [
               {"id": "ok", "title": "Fine", "executable": "dotnet", "args": ["--version"]},
               {"id": "notitle", "title": "", "executable": "dotnet"},
               {"id": "unknown", "title": "U", "executable": "dotnet", "logPattern": "nope"},
               {"id": "zero", "title": "Z", "executable": "dotnet", "timeoutSeconds": 0}],
             "logPatterns": [{"id": "bad", "title": "B", "syntax": "mask", "pattern": "%F"},
                             {"id": "lv", "title": "Levels", "levels": {"error": "E!"}}]}
            """);
        var catalog = _app.Tools.Load();
        Assert.Equal(["ok"], catalog.Tools.Select(t => t.Spec.Id));
        Assert.Equal(["lv"], catalog.Patterns.Select(p => p.Definition.Id));
        Assert.Equal(3, catalog.Errors.Count(e => e.StartsWith("notitle", StringComparison.Ordinal) || e.StartsWith("unknown", StringComparison.Ordinal) || e.StartsWith("zero", StringComparison.Ordinal)));
        Assert.Contains(catalog.Errors, e => e.StartsWith("bad: ", StringComparison.Ordinal) && e.Contains("%L", StringComparison.Ordinal));

        File.WriteAllText(_app.Tools.FilePath, "{ not json");
        Assert.Single(_app.Tools.Load().Errors);
        File.Delete(_app.Tools.FilePath);
        Assert.Empty(_app.Tools.Load().Errors);
    }

    [AvaloniaFact]
    public void The_tool_list_offers_the_tools_and_a_row_that_opens_the_definitions()
    {
        Define([ToolTestKit.Tool("echo", "Echo", ["x"])]);
        _w.ShowToolMenu();
        var rows = _w.QuickResults;
        Assert.Equal(["Echo", Loc.Get(K.ToolsOpenDefinitions)], rows.Select(r => r.Title));
        rows[1].Run();
        Assert.Equal(_app.Tools.FilePath, _w.Active!.Path);
    }

    [AvaloniaFact]
    public void The_commands_are_in_the_tools_menu()
    {
        var tools = _w.MenuModel.Single(g => g.Label == "Nástroje");
        var ids = tools.Items.OfType<MenuCommand>().Select(c => c.CommandId).ToList();
        Assert.Contains("nks.tools.run", ids);
        Assert.Contains("nks.tools.stop", ids);
        Assert.Contains("nks.tools.logPattern", ids);
        Assert.Contains("nks.log.nextError", ids);
        Assert.Contains("nks.log.previousError", ids);
        Assert.Contains("nks.log.levels", ids);
    }
}

internal static class StringExtensions
{
    public static string Also(this string value, Action<string> action)
    {
        action(value);
        return value;
    }
}
