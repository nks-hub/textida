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

    // ---- P8.2.1: editor of definitions, typed variables, tools from a folder ----

    [AvaloniaFact]
    public void A_tool_made_in_the_editor_runs_with_the_arguments_the_runner_gives_the_same_definition()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "my docs")).FullName;
        string file = Path.Combine(folder, "a file.txt");
        File.WriteAllText(file, "x");
        _w.OpenFile(file, null);
        Define([ToolTestKit.Tool("ref", "Ref", ["${File}", "a \"quoted\" & b", "${FileBase}|${FileExt}", "${FileDir}/o"], cwd: "${FileDir}")],
            [new JsonObject { ["id"] = "gcc", ["title"] = "gcc", ["syntax"] = "mask", ["pattern"] = "[%F:%L:%C:*" }]);

        var editor = new ToolEditorWindow(_app.Tools, _w.PreviewTool);
        Assert.Equal(["Ref"], editor.Titles);
        editor.NewNow();
        editor.Draft = new ToolDraft
        {
            Id = "ed", Title = "Ed", Executable = ToolTestKit.Dotnet, Cwd = "${FileDir}", Variables = "Out:path=${FileDir}/o",
            Args = string.Join('\n', ToolTestKit.EchoDll, "${File}", "a \"quoted\" & b", "${FileBase}|${FileExt}", "${Out}"),
        };
        Assert.True(editor.SaveNow());
        Assert.Equal(["Ref", "Ed"], editor.Titles);

        var catalog = _app.Tools.Load();
        Assert.Single(catalog.Patterns);
        Assert.Empty(catalog.Errors);
        Assert.Equal(_w.ResolveTool(Entry("ref").Spec).Args, _w.ResolveTool(Entry("ed").Spec).Args);
        editor.Select(1);
        Assert.Equal(Loc.Get(K.ToolEditPreview, ("command", _w.ResolveTool(Entry("ed").Spec).CommandLine)), editor.PreviewText);

        _dialogs.Answers.Enqueue(0);
        Finish(Run("ed"));
        var lines = Lines();
        Assert.Contains($"[{file}]", lines);
        Assert.Contains("[a \"quoted\" & b]", lines);
        Assert.Contains("[a file|.txt]", lines);
        Assert.Contains($"[{folder}/o]", lines);

        Assert.True(editor.DeleteNow());
        Assert.Equal(["Ref"], editor.Titles);
        Assert.Equal(["ref"], _app.Tools.Load().Tools.Select(t => t.Spec.Id));
    }

    [AvaloniaFact]
    public void The_editor_refuses_what_is_not_a_definition_and_keeps_the_file_as_it_was()
    {
        Define([ToolTestKit.Tool("ref", "Ref", ["x"])]);
        string before = File.ReadAllText(_app.Tools.FilePath);
        var editor = new ToolEditorWindow(_app.Tools, _w.PreviewTool);
        var ok = new ToolDraft { Id = "n", Title = "N", Executable = ToolTestKit.Dotnet };

        editor.NewNow();
        editor.Draft = new ToolDraft { Id = "n" };
        Assert.False(editor.SaveNow());
        Assert.Equal(Loc.Get(K.ToolEditNeeds), editor.NoteText);
        editor.Draft = new ToolDraft { Id = "n", Title = "N", Executable = "x", Timeout = "abc" };
        Assert.False(editor.SaveNow());
        Assert.Equal(Loc.Get(K.ToolEditTimeoutBad), editor.NoteText);
        editor.Draft = new ToolDraft { Id = "n", Title = "N", Executable = "x", Variables = "Out:number" };
        Assert.False(editor.SaveNow());
        Assert.Equal(Loc.Get(K.ToolEditVariableBad, ("line", 1)), editor.NoteText);
        editor.Draft = new ToolDraft { Id = "ref", Title = "N", Executable = "x" };
        Assert.False(editor.SaveNow());
        Assert.Equal(Loc.Get(K.ToolEditDuplicate, ("id", "ref")), editor.NoteText);
        Assert.Equal(before, File.ReadAllText(_app.Tools.FilePath));

        editor.Draft = ok;
        Assert.True(editor.SaveNow());
        Assert.Equal(["ref", "n"], _app.Tools.Load().Tools.Select(t => t.Spec.Id));
    }

    [AvaloniaFact]
    public void A_typed_variable_of_the_wrong_type_is_refused_before_the_tool_starts_and_a_user_variable_fills_it()
    {
        string path = Path.Combine(_app.ProfileDirectory, NksHub.Textida.Workbench.Clips.UserVariables.FileName);
        var tool = ToolTestKit.Tool("typed", "Typed", ["${Target}"]);
        tool["variables"] = new JsonArray(new JsonObject { ["name"] = "Target", ["type"] = "path" });
        Define([tool]);

        File.WriteAllText(path, """{"Target": "bad*name"}""");
        Finish(Run("typed"));
        Assert.Empty(_dialogs.Asked);
        var refused = Assert.Single(Lines());
        Assert.Contains("${Target}", refused, StringComparison.Ordinal);
        Assert.False(_w.ToolRunningNow);

        File.WriteAllText(path, """{"Target": "good/name.txt"}""");
        _dialogs.Answers.Enqueue(0);
        Finish(Run("typed"));
        Assert.Contains("[good/name.txt]", Lines());

        var wrong = ToolTestKit.Tool("typed", "Typed", ["${Target}"]);
        wrong["variables"] = new JsonArray(new JsonObject { ["name"] = "Target", ["type"] = "color" });
        Define([wrong]);
        Assert.Empty(_app.Tools.Load().Tools);
        Assert.Single(_app.Tools.Load().Errors);
    }

    [AvaloniaFact]
    public void A_damaged_user_variables_file_stops_a_tool_before_anything_is_asked_and_names_the_reason()
    {
        Define([ToolTestKit.Tool("plain", "Plain", ["x"])]);
        string path = Path.Combine(_app.ProfileDirectory, NksHub.Textida.Workbench.Clips.UserVariables.FileName);
        File.WriteAllText(path, "{ not json");
        var reason = NksHub.Textida.Workbench.Clips.UserVariables.Load(path).Problem;
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Finish(Run("plain"));
        Assert.Empty(_dialogs.Asked);
        var line = Assert.Single(Lines());
        Assert.Contains(Loc.Get(K.ClipVariablesBroken, ("reason", "")).Split(':')[0], line, StringComparison.Ordinal);
        Assert.Contains(reason!, line, StringComparison.Ordinal);
        Assert.False(_w.ToolRunningNow);
    }

    [AvaloniaFact]
    public void A_tool_that_uses_a_project_variable_is_asked_about_again_when_the_project_changes_its_value()
    {
        string project = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        string variables = Path.Combine(Directory.CreateDirectory(Path.Combine(project, ".textida")).FullName, "variables.json");
        string file = Path.Combine(project, "a.txt");
        File.WriteAllText(file, "x");
        void Set(string env) => File.WriteAllText(variables, new JsonObject { ["Env"] = env }.ToJsonString());
        Set("one");
        _w.OpenFile(file, null);
        Define([ToolTestKit.Tool("uses", "Uses", ["${Env}"]), ToolTestKit.Tool("plain", "Plain", ["fixed"])]);

        _dialogs.Answers.Enqueue(0);
        Finish(Run("uses"));
        var asked = Assert.Single(_dialogs.Asked);
        Assert.EndsWith(" one", asked.Details![0], StringComparison.Ordinal); // the question shows the value the project gives
        Assert.Contains("[one]", Lines());
        Finish(Run("uses"));
        Assert.Single(_dialogs.Asked); // the same value: not asked again

        Set("two");
        _dialogs.Answers.Enqueue(1);
        Finish(Run("uses"));
        Assert.Equal(2, _dialogs.Asked.Count);
        Assert.EndsWith(" two", _dialogs.Asked[1].Details![0], StringComparison.Ordinal);
        Assert.DoesNotContain("[two]", Lines());

        Set("one");
        Finish(Run("uses")); // the value that was approved before
        Assert.Equal(2, _dialogs.Asked.Count);
        Assert.Equal(3, Lines().Count(l => l == "[one]"));

        // a tool that uses no variable of the project is not tied to the project's file
        _dialogs.Answers.Enqueue(0);
        Finish(Run("plain"));
        Set("three");
        Finish(Run("plain"));
        Assert.Equal(3, _dialogs.Asked.Count);
        Assert.Equal(2, Lines().Count(l => l == "[fixed]"));
    }

    [AvaloniaFact]
    public void A_tool_from_a_folder_runs_only_after_a_yes_and_a_changed_definition_asks_again()
    {
        string project = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        string sub = Directory.CreateDirectory(Path.Combine(project, "src")).FullName;
        string file = Path.Combine(sub, "a.txt");
        File.WriteAllText(file, "x");
        string config = Path.Combine(Directory.CreateDirectory(Path.Combine(project, ".textida")).FullName, "tools.json");
        void Write(string arg) => File.WriteAllText(config, new JsonObject { ["externalTools"] = new JsonArray(ToolTestKit.Tool("p", "Project tool", [arg])) }.ToJsonString());
        ToolEntry Folder() => _app.Tools.Load(sub).Tools.Single(t => t.Spec.Id == "p");
        Write("one");
        _w.OpenFile(file, null);

        Assert.True(Folder().Foreign);
        Assert.DoesNotContain(_app.Tools.Load().Tools, t => t.Spec.Id == "p");

        _dialogs.Answers.Enqueue(1);
        Finish(_w.RunToolAsync(Folder()));
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.ToolConfirmForeign, ("title", "Project tool")), asked.Message);
        Assert.Contains(asked.Details!, d => d.Contains(config, StringComparison.Ordinal));
        Assert.DoesNotContain("[one]", Lines());

        _dialogs.Answers.Enqueue(0);
        Finish(_w.RunToolAsync(Folder()));
        Finish(_w.RunToolAsync(Folder()));
        Assert.Equal(2, _dialogs.Asked.Count);
        Assert.Equal(2, Lines().Count(l => l == "[one]"));

        Write("two");
        _dialogs.Answers.Enqueue(1);
        Finish(_w.RunToolAsync(Folder()));
        Assert.Equal(3, _dialogs.Asked.Count);
        Assert.DoesNotContain("[two]", Lines());

        // a tool of the profile with the same id wins; the folder tool is reported
        Define([ToolTestKit.Tool("p", "Project tool", ["two"])]);
        Assert.Contains(_app.Tools.Load(sub).Errors, e => e.StartsWith("p:", StringComparison.Ordinal));
    }

    // ---- P8.2.2: Makefile targets ----

    [AvaloniaFact]
    public void A_makefile_target_runs_through_the_runner_and_an_error_line_in_the_output_opens_its_file()
    {
        string folder = Project();
        string makefile = Path.Combine(folder, "Makefile");
        File.WriteAllText(makefile, ".PHONY: build\nbuild: a.c\n\techo hi\ntest:\n\techo t\n");

        _w.OpenFile(makefile, null);
        _w.ShowMakeTargets();
        Assert.Equal(["build", "test"], _w.QuickResults.Select(r => r.Title));
        Assert.Equal(Loc.Get(K.MakeDetail, ("line", 2)), _w.QuickResults[0].Detail);

        _dialogs.Answers.Enqueue(0);
        Finish(_w.RunMakeTargetAsync(makefile, "build", ToolTestKit.Dotnet, [ToolTestKit.EchoDll, "a.c:3:4: error: boom"]));
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains(asked.Details!, d => d.Contains("-f", StringComparison.Ordinal) && d.Contains("build", StringComparison.Ordinal));
        Assert.Contains(asked.Details!, d => d.Contains(folder, StringComparison.Ordinal));
        var lines = Lines();
        Assert.Contains("[-f]", lines);
        Assert.Contains("[Makefile]", lines);
        Assert.Contains("[build]", lines);

        Assert.True(_w.OpenOutputLine(OutputLineIndex("[a.c:3:4: error: boom]")));
        Assert.Equal(Path.Combine(folder, "a.c"), _w.Active!.Path);
        Assert.Equal((3L, 4L), _w.Active.CaretPosition());

        Finish(_w.RunMakeTargetAsync(makefile, "build", ToolTestKit.Dotnet, [ToolTestKit.EchoDll, "a.c:3:4: error: boom"]));
        Assert.Single(_dialogs.Asked);
    }

    [AvaloniaFact]
    public void The_targets_of_the_nearest_makefile_are_listed_for_any_file_and_a_folder_without_one_says_so()
    {
        string project = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        string src = Directory.CreateDirectory(Path.Combine(project, "src")).FullName;
        string source = Path.Combine(src, "a.c");
        File.WriteAllText(source, "int x;\n");
        _w.OpenFile(source, null);

        _w.ShowMakeTargets();
        Assert.Equal(Loc.Get(K.MakeNotMakefile), _w.StatusMessageText);

        File.WriteAllText(Path.Combine(project, "Makefile"), "all: a.o\nclean:\n\trm -f a.o\n");
        _w.ShowMakeTargets();
        Assert.Equal(["all", "clean"], _w.QuickResults.Select(r => r.Title));

        string big = Path.Combine(src, "makefile");
        File.WriteAllText(big, "x:\n" + new string('#', (int)PluginLimitsPlaceholders.MakefileMaxBytes + 1));
        _w.ShowMakeTargets();
        Assert.Equal(Loc.Get(K.MakeTooBig, ("file", big), ("bytes", PluginLimitsPlaceholders.MakefileMaxBytes)), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void The_last_makefile_target_is_run_again_with_the_program_of_the_setting()
    {
        string folder = Project();
        string makefile = Path.Combine(folder, "Makefile");
        File.WriteAllText(makefile, "build:\n\techo hi\n");

        _w.RepeatMakeTarget();
        Assert.Equal(Loc.Get(K.MakeNoneYet), _w.StatusMessageText);

        _dialogs.Answers.Enqueue(0);
        Finish(_w.RunMakeTargetAsync(makefile, "build", ToolTestKit.Dotnet, [ToolTestKit.EchoDll]));
        Assert.Single(_dialogs.Asked);

        _app.Workbench.Settings.SetLayer(NksHub.Textida.Workbench.Settings.SettingSource.User, new JsonObject { ["tools.makeProgram"] = ToolTestKit.Dotnet });
        _dialogs.Answers.Enqueue(1); // a different command than before, so it asks; no is enough to see it
        _w.RepeatMakeTarget();
        ToolTestKit.WaitFor(() => _dialogs.Asked.Count == 2, "the repeated run did not ask");
        var details = _dialogs.Asked[1].Details!;
        Assert.Contains(details, d => d.Contains("-f", StringComparison.Ordinal) && d.Contains("Makefile", StringComparison.Ordinal) && d.Contains("build", StringComparison.Ordinal) && !d.Contains(ToolTestKit.EchoDll, StringComparison.Ordinal));
        Assert.Contains(details, d => d.Contains(folder, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void A_relative_file_in_the_output_of_a_recursive_make_is_looked_for_in_the_folder_it_entered()
    {
        if (!OperatingSystem.IsWindows()) return; // the stand-in for make is a batch file
        string project = Directory.CreateDirectory(Path.Combine(_temp.Path, "tree")).FullName;
        string sub = Directory.CreateDirectory(Path.Combine(project, "sub")).FullName;
        File.WriteAllText(Path.Combine(sub, "b.c"), "one\ntwo\nthree\n");
        string script = Path.Combine(project, "emit.cmd");
        File.WriteAllText(script, "@echo off\r\necho make[1]: Entering directory '%~dp0sub'\r\necho b.c:2:1: error: x\r\necho make[1]: Leaving directory '%~dp0sub'\r\necho b.c:3:1: error: y\r\n");
        var tool = new JsonObject { ["id"] = "mk", ["title"] = "Make", ["executable"] = script, ["args"] = new JsonArray(), ["cwd"] = project, ["logPattern"] = "mk" };
        Define([tool], [new JsonObject { ["id"] = "mk", ["title"] = "mk", ["syntax"] = "regex", ["pattern"] = MakefileTargets.ErrorPattern }]);
        _dialogs.Answers.Enqueue(0);
        Finish(Run("mk"));

        // after "Leaving directory" the name is relative to the folder of the tool again, where there is no b.c (and b.c is not open yet)
        Assert.False(_w.OpenOutputLine(OutputLineIndex("b.c:3:1: error: y")));
        Assert.Equal(Loc.Get(K.LogFileMissing, ("file", "b.c")), _w.StatusMessageText);

        Assert.True(_w.OpenOutputLine(OutputLineIndex("b.c:2:1: error: x")));
        Assert.Equal(Path.Combine(sub, "b.c"), _w.Active!.Path);
        Assert.Equal((2L, 1L), _w.Active.CaretPosition());
    }

    // ---- P8.2.1: selection, word, project root, defaults from the settings ----

    [AvaloniaFact]
    public void The_selection_the_word_and_the_project_root_reach_the_tool_and_no_selection_stops_it()
    {
        string project = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        string file = Path.Combine(Directory.CreateDirectory(Path.Combine(project, "src")).FullName, "a.txt");
        File.WriteAllText(file, "alpha beta\ngamma\n");
        _w.OpenFile(file, null);
        Define([ToolTestKit.Tool("vars", "Vars", ["${Selection}", "${Word}", "${ProjectRoot}"], cwd: "${ProjectRoot}")]);

        _w.Active!.Surface!.SetSelections([new Sel(0, 4)]);
        _dialogs.Answers.Enqueue(0);
        Finish(Run("vars"));
        var lines = Lines();
        Assert.Contains("[alph]", lines);
        Assert.Contains("[alpha]", lines);
        Assert.Contains($"[{project}]", lines);

        _w.Active.Surface!.SetSelections([Sel.At(3)]);
        Finish(Run("vars"));
        Assert.Single(_dialogs.Asked);
        Assert.Contains(Lines(), l => l.Contains("Selection", StringComparison.Ordinal) && l.Contains(Entry("vars").Title, StringComparison.Ordinal));
        Assert.False(_w.ToolRunningNow);
    }

    [AvaloniaFact]
    public void The_settings_give_a_tool_the_time_limit_and_the_output_encoding_its_definition_leaves_out()
    {
        Define([ToolTestKit.Tool("own", "Own", ["x"], timeout: 3), ToolTestKit.Tool("plain", "Plain", ["\u00E9"])]);
        Assert.Null(_w.ResolveTool(Entry("plain").Spec).Timeout); // 0: no limit chosen, as before
        Assert.Equal(65001, _w.ResolveTool(Entry("plain").Spec).OutputEncoding!.CodePage);

        _app.Workbench.Settings.SetLayer(NksHub.Textida.Workbench.Settings.SettingSource.User,
            new JsonObject { ["tools.timeoutSeconds"] = 7, ["tools.outputEncoding"] = "iso-8859-1" });
        Assert.Equal(TimeSpan.FromSeconds(7), _w.ResolveTool(Entry("plain").Spec).Timeout);
        Assert.Equal(TimeSpan.FromSeconds(3), _w.ResolveTool(Entry("own").Spec).Timeout); // the definition wins

        _dialogs.Answers.Enqueue(0);
        Finish(Run("plain"));
        Assert.Contains("[\u00C3\u00A9]", Lines()); // read as Latin-1

        var named = ToolTestKit.Tool("named", "Named", ["\u00E9"]);
        named["outputEncoding"] = "utf-8";
        Define([named]);
        _dialogs.Answers.Enqueue(0);
        Finish(Run("named"));
        Assert.Contains("[\u00E9]", Lines());

        var unknown = ToolTestKit.Tool("bad", "Bad", ["x"]);
        unknown["outputEncoding"] = "no-such-encoding";
        Define([unknown]);
        Assert.Empty(_app.Tools.Load().Tools);
        Assert.Contains("no-such-encoding", Assert.Single(_app.Tools.Load().Errors), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void The_editor_keeps_the_output_encoding_and_refuses_one_the_system_does_not_know()
    {
        var editor = new ToolEditorWindow(_app.Tools, _w.PreviewTool);
        editor.NewNow();
        editor.Draft = new ToolDraft { Id = "e", Title = "E", Executable = ToolTestKit.Dotnet, OutputEncoding = "no-such-encoding" };
        Assert.False(editor.SaveNow());
        Assert.Equal(Loc.Get(K.ToolEditEncodingBad, ("name", "no-such-encoding")), editor.NoteText);

        editor.Draft = new ToolDraft { Id = "e", Title = "E", Executable = ToolTestKit.Dotnet, OutputEncoding = "iso-8859-1" };
        Assert.True(editor.SaveNow());
        Assert.Equal("iso-8859-1", _app.Tools.LoadDefinitions().Single().OutputEncoding);
        Assert.Equal(Encoding.Latin1.CodePage, _app.Tools.Load().Tools.Single().Spec.OutputEncoding!.CodePage);
    }

    [AvaloniaFact]
    public void The_commands_are_in_the_tools_menu()
    {
        var tools = _w.MenuModel.Single(g => g.Label == "Nástroje");
        var ids = tools.Items.AllCommands().Select(c => c.CommandId).ToList();
        Assert.Contains("nks.tools.run", ids);
        Assert.Contains("nks.tools.stop", ids);
        Assert.Contains("nks.tools.logPattern", ids);
        Assert.Contains("nks.tools.edit", ids);
        Assert.Contains("nks.tools.makeTargets", ids);
        Assert.Contains("nks.tools.makeRepeat", ids);
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
