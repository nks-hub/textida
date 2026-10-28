using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Agents;
using NksHub.Textida.Core.Agents.Hooks;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P8b.3.1 to P8b.3.3 through the running window: the token estimate in the status bar and its details, the hooks editor with its dry run and its proposal to the change review, and the transcript
/// viewer with its turns, hidden values, search, raw line and files. The fixtures are handmade; the key in them is made up.
/// </summary>
[Collection("LocState")]
public class AgentToolsAppTests : IDisposable
{
    private const string Key = "hunter2hunter2";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public AgentToolsAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        Loc.SetLanguage("en");
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        foreach (var window in _w.Transcripts.ToList()) window.Close();
        _w.HooksEditor?.Close();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        for (int i = 0; i < 600 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "timed out waiting for " + what);
    }

    [AvaloniaFact]
    public void The_three_commands_have_handlers_and_are_in_the_palette_and_the_agent_menu()
    {
        var registry = _app.Workbench.Commands;
        foreach (var id in new[] { "nks.agent.hooks", "nks.agent.transcript", "nks.agent.tokens" })
        {
            Assert.True(registry.HasHandler(id), id);
            Assert.Contains(registry.Commands, c => c.Id == id);
        }
    }

    // ---- tokens ----

    [AvaloniaFact]
    public void The_status_bar_shows_the_estimate_of_the_selection_as_an_estimate_and_follows_the_rule()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = new string('x', 400);
        tab.Editor.Select(5, 0);
        Assert.False(_w.TokensItem.IsVisible);

        tab.Editor.Select(0, 400);
        Assert.True(_w.TokensItem.IsVisible);
        Assert.Equal(Loc.Get(K.TokensStatus, ("count", "100")), _w.TokensItem.Content);
        Assert.StartsWith("≈", (string)_w.TokensItem.Content!);

        _w.TokenRule = TokenEstimator.Claude;
        Assert.Equal(Loc.Get(K.TokensStatus, ("count", "115")), _w.TokensItem.Content);
        _w.TokenRule = TokenEstimator.ClaudeNewTokenizer;
        Assert.Equal(Loc.Get(K.TokensStatus, ("count", "149")), _w.TokensItem.Content);

        _w.TokenRule = TokenEstimator.Generic;
        tab.Editor.Select(0, 40);
        Assert.Equal(Loc.Get(K.TokensStatus, ("count", "10")), _w.TokensItem.Content);
        tab.Editor.Select(7, 0);
        Assert.False(_w.TokensItem.IsVisible);
    }

    [AvaloniaFact]
    public void The_menu_of_the_item_lists_the_rules_marks_the_one_in_use_and_changes_it()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "some text to count";
        tab.Editor.Select(0, 18);
        var flyout = _w.ShowTokenMenu();
        var rules = flyout.Items.OfType<MenuItem>().Where(i => i.ToggleType == MenuItemToggleType.Radio).ToList();
        Assert.Equal(TokenEstimator.Presets.Select(p => _w.TokenRuleName(p)), rules.Select(r => (string)r.Header!));
        Assert.Equal(_w.TokenRuleName(TokenEstimator.Generic), (string)Assert.Single(rules, r => r.IsChecked).Header!);
        Assert.Contains(flyout.Items.OfType<MenuItem>(), i => (string?)i.Header == Loc.Get(K.TokensDetails));

        rules[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Same(TokenEstimator.Gemini, _w.TokenRule);
        flyout.Hide();
    }

    [AvaloniaFact]
    public async Task The_details_name_the_selection_the_document_the_rule_its_source_and_that_it_is_an_estimate()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = new string('x', 400);
        tab.Editor.Select(0, 40);
        _dialogs.Answers.Enqueue(0);
        Assert.Equal(CommandOutcome.Handled, _w.ShowTokenEstimate());
        await _w.TokenDialogTask;

        var asked = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.TokensDialogTitle), asked.Title);
        Assert.Contains(Loc.Get(K.TokensSelection, ("tokens", "10"), ("chars", "40")), asked.Message);
        Assert.Contains(Loc.Get(K.TokensDocument, ("tokens", "100"), ("chars", "400")), asked.Message);
        Assert.Contains(Loc.Get(K.TokensRule, ("rule", _w.TokenRuleName(TokenEstimator.Generic)), ("source", TokenEstimator.Generic.Source)), asked.Message);
        Assert.Contains(Loc.Get(K.TokensEstimateNote), asked.Message);
    }

    // ---- the hooks editor ----

    private const string Settings = "{\n  \"model\": \"x\",\n  \"hooks\": {\n    \"PreToolUse\": [\n      {\n        \"matcher\": \"Bash\",\n        \"hooks\": [\n          { \"type\": \"command\", \"command\": \"echo hi\" }\n        ]\n      }\n    ]\n  }\n}\n";

    private HooksEditorWindow OpenHooks(string name, string text)
    {
        var tab = _w.OpenFile(_temp.File(name, text), null);
        _w.SelectTab(tab);
        Assert.Equal(CommandOutcome.Handled, _w.ShowHooksEditor());
        return Assert.IsType<HooksEditorWindow>(_w.HooksEditor);
    }

    [AvaloniaFact]
    public void The_hooks_editor_lists_the_hooks_of_the_open_file_and_checks_them_against_the_schema()
    {
        var editor = OpenHooks("settings.json", Settings);
        Assert.Equal(HookProduct.ClaudeCode, editor.Product);
        Assert.Equal(["PreToolUse, Bash: command: echo hi"], editor.Handlers);
        Assert.Equal([Loc.Get(K.HooksFindingsNone)], editor.Findings);
        Assert.False(editor.CanPropose);
    }

    [AvaloniaFact]
    public void A_file_with_a_mistake_shows_findings_in_the_language_of_the_user()
    {
        var editor = OpenHooks("settings.json", "{ \"hooks\": { \"PreToolUze\": [], \"Stop\": [ { \"matcher\": \"x\", \"hooks\": [ { \"type\": \"command\" } ] } ] } }");
        Assert.Contains(editor.Findings, f => f.StartsWith(Loc.Get(K.HooksFindingError, ("text", Loc.Get(K.HooksIssueUnknownEvent, ("event", "PreToolUze"))))));
        Assert.Contains(editor.Findings, f => f.Contains("command"));
        Assert.Contains(editor.Findings, f => f.StartsWith(Loc.Get(K.HooksFindingWarning, ("text", Loc.Get(K.HooksIssueMatcherIgnored, ("event", "Stop"))))));
        Assert.False(editor.CanPropose);
    }

    [AvaloniaFact]
    public async Task A_hook_added_in_the_form_reaches_the_tab_only_through_the_change_review_and_nothing_is_run()
    {
        var tab = _w.OpenFile(_temp.File("settings.json", Settings), null);
        _w.SelectTab(tab);
        string onDisk = File.ReadAllText(tab.Path!);
        Assert.Equal(CommandOutcome.Handled, _w.ShowHooksEditor());
        var editor = _w.HooksEditor!;

        editor.FieldEvent = "Stop";
        editor.FieldMatcher = "";
        editor.FieldType = "command";
        editor.FieldMain = "echo done";
        editor.FieldTimeout = "15";
        Assert.True(editor.ApplyNow());
        Assert.Equal(["PreToolUse, Bash: command: echo hi", "Stop, : command: echo done"], editor.Handlers);
        Assert.Contains("\"timeout\": 15", editor.WorkingText);
        Assert.Equal(Settings, tab.Editor!.Document.Text); // the list changed, the tab did not
        Assert.True(editor.CanPropose);

        var proposing = editor.ProposeNow();
        Assert.NotEmpty(_w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>());
        Assert.Equal(Settings, tab.Editor.Document.Text); // the user decides first
        _w.ApplyPlan();
        Assert.True(await proposing);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(editor.WorkingText, tab.Editor.Document.Text);
        Assert.Contains("echo done", tab.Editor.Document.Text);
        Assert.True(tab.Doc.Doc.IsModified);
        Assert.Equal(onDisk, File.ReadAllText(tab.Path!)); // saving is the normal save of the tab
        Assert.False(editor.CanPropose);
        _w.SelectTab(tab);
        _w.Undo();
        Assert.Equal(Settings, tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task A_list_with_errors_or_a_tab_that_changed_is_not_proposed()
    {
        var tab = _w.OpenFile(_temp.File("settings.json", Settings), null);
        _w.SelectTab(tab);
        Assert.Equal(CommandOutcome.Handled, _w.ShowHooksEditor());
        var editor = _w.HooksEditor!;

        editor.FieldEvent = "Stop";
        editor.FieldType = "command";
        editor.FieldMain = "";
        Assert.True(editor.ApplyNow()); // the list takes it, the check reports the missing command
        Assert.Contains(editor.Findings, f => f.StartsWith(Loc.Get(K.HooksFindingError, ("text", ""))));
        Assert.False(await editor.ProposeNow());
        Assert.Equal(Loc.Get(K.HooksNoteErrors), editor.NoteText);
        Assert.Null(_w.ResultsView);

        editor.FieldMain = "echo fixed";
        Assert.True(editor.ApplyNow());
        tab.Editor!.Document.Text = "{}"; // the user typed in the tab meanwhile
        Assert.False(await editor.ProposeNow());
        Assert.Equal(Loc.Get(K.HooksNoteStale), editor.NoteText);
        Assert.Equal("{}", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public void A_wrong_timeout_is_refused_in_the_form_and_a_removed_hook_leaves_the_list()
    {
        var editor = OpenHooks("settings.json", Settings);
        editor.FieldEvent = "Stop";
        editor.FieldType = "command";
        editor.FieldMain = "echo";
        editor.FieldTimeout = "soon";
        Assert.False(editor.ApplyNow());
        Assert.Equal(Loc.Get(K.HooksNoteTimeout), editor.NoteText);
        Assert.Single(editor.Handlers);

        var item = editor.Tree.Items.OfType<TreeViewItem>().Single().Items.OfType<TreeViewItem>().Single().Items.OfType<TreeViewItem>().Single();
        editor.Tree.SelectedItem = item;
        Assert.True(editor.DeleteNow());
        Assert.Empty(editor.Handlers);
        Assert.Equal(Loc.Get(K.HooksNoteDeleted), editor.NoteText);
        Assert.Contains("\"model\": \"x\"", editor.WorkingText);
    }

    [AvaloniaFact]
    public void The_dry_run_shows_the_input_the_matcher_and_what_each_exit_code_does_and_starts_nothing()
    {
        var editor = OpenHooks("settings.json", Settings);
        editor.FieldEvent = "PreToolUse";
        editor.FieldMatcher = "Edit|Write";
        editor.FieldTested = "Write";
        string report = editor.DryRunNow();

        Assert.Contains(Loc.Get(K.HooksDryNothing), report);
        Assert.Contains(Loc.Get(K.HooksDryMatcherExact, ("matcher", "Edit|Write")), report);
        Assert.Contains(Loc.Get(K.HooksDryRuns, ("field", "tool_name"), ("value", "Write")), report);
        Assert.Contains("\"hook_event_name\": \"PreToolUse\"", report);
        Assert.Contains("\"tool_name\": \"Write\"", report);
        Assert.Contains(Loc.Get(K.HooksDryExit, ("code", 0), ("effect", Loc.Get(K.HooksEffectSuccess))), report);
        Assert.Contains(Loc.Get(K.HooksDryExitOther, ("code", 1), ("effect", Loc.Get(K.HooksEffectNonBlocking))), report);
        Assert.Contains(Loc.Get(K.HooksDryExit, ("code", 2), ("effect", Loc.Get(K.HooksEffectBlocks))), report);
        Assert.Contains("code.claude.com", report);
        Assert.Contains(Loc.Get(K.HooksDryNothing), editor.DryRunText);

        editor.FieldTested = "Read";
        Assert.Contains(Loc.Get(K.HooksDryRunsNot, ("field", "tool_name"), ("value", "Read")), editor.DryRunNow());

        editor.FieldEvent = "UserPromptSubmit";
        string noMatcher = editor.DryRunNow();
        Assert.Contains(Loc.Get(K.HooksDryNoMatcher), noMatcher);
        Assert.Contains(Loc.Get(K.HooksDryStdoutContext), noMatcher);
    }

    [AvaloniaFact]
    public void A_file_in_a_codex_folder_is_a_codex_file_and_a_toml_file_is_not_edited()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, ".codex"));
        var editor = OpenHooks(Path.Combine(".codex", "hooks.json"), "{}");
        Assert.Equal(HookProduct.Codex, editor.Product);
        editor.FieldEvent = "Stop";
        editor.FieldType = "command";
        editor.FieldMain = "python3 stop.py";
        Assert.True(editor.ApplyNow());
        Assert.Equal([Loc.Get(K.HooksFindingsNone)], editor.Findings);
        editor.FieldTested = "";
        Assert.Contains(Loc.Get(K.HooksDryStdoutJson), editor.DryRunNow());
        editor.Close();

        var toml = _w.OpenFile(_temp.File("config.toml", "[hooks]\n"), null);
        _w.SelectTab(toml);
        Assert.Equal(CommandOutcome.Handled, _w.ShowHooksEditor());
        Assert.Null(_w.HooksEditor);
        Assert.Equal(Loc.Get(K.HooksNoteToml), _w.StatusMessageText);
    }

    // ---- the transcript viewer ----

    private static string Json(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);

    private string[] Session(string touched) =>
    [
        """{"type":"summary","summary":"Fixture session"}""",
        """{"type":"user","message":{"role":"user","content":"Please read the env file"}}""",
        """{"type":"assistant","message":{"id":"m1","role":"assistant","content":[{"type":"text","text":"Reading it."},{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"/work/proj/.env"}}]}}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"API_KEY=hunter2hunter2\nMODE=dev"}]}}""",
        """{"type":"assistant","message":{"id":"m2","role":"assistant","content":[{"type":"text","text":"The key is hunter2hunter2."}]}}""",
        """{"type":"user","message":{"role":"user","content":"Now change the code"}}""",
        "{\"type\":\"assistant\",\"message\":{\"id\":\"m3\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t2\",\"name\":\"Edit\",\"input\":{\"file_path\":\"" + Json(touched) + "\",\"old_string\":\"int b = 2;\",\"new_string\":\"int b = 3;\"}}]}}",
        """{"type":"queue-operation","operation":"enqueue"}""",
    ];

    private async Task<(TranscriptWindow Window, string Path, string Touched)> OpenSession()
    {
        string touched = _temp.File("app.cs", "int b = 2;\n");
        string path = _temp.File("session.jsonl", string.Join("\n", Session(touched)) + "\n");
        var window = _w.OpenTranscript(path) ?? throw new InvalidOperationException("the transcript did not open");
        await Until(() => { window.Tick(); return window.IsListComplete; }, "the index");
        await Until(() => { window.Tick(); return window.TurnList.SelectedIndex >= 0; }, "the first turn");
        return (window, path, touched);
    }

    [AvaloniaFact]
    public async Task The_transcript_opens_read_only_and_lists_the_turns_while_it_is_indexed()
    {
        var (window, path, _) = await OpenSession();
        var before = SHA256.HashData(File.ReadAllBytes(path));
        Assert.Contains(window, _w.Transcripts);
        Assert.Equal(["Before the first prompt", "#1 Please read the env file", "#2 Now change the code"], window.TurnTitles);
        Assert.Equal(Loc.Get(K.TranscriptDone, ("lines", "8"), ("turns", "3")), window.StatusText);

        window.TurnList.SelectedIndex = 1;
        await window.TurnTask;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Please read the env file", window.RowTexts);
        Assert.Contains(window.RowData, r => r.Kind == NksHub.Textida.Core.Agents.Transcripts.TranscriptRowKind.ToolCall && r.Tool == "Read");
        Assert.Contains("≈", window.HeaderText);

        window.Close();
        Assert.Empty(_w.Transcripts);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [AvaloniaFact]
    public async Task The_value_of_an_env_file_is_hidden_in_the_rows_the_raw_line_and_the_hits()
    {
        var (window, _, _) = await OpenSession();
        window.TurnList.SelectedIndex = 1;
        await window.TurnTask;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(window.RowTexts, t => t.Contains(Key));
        Assert.Contains(window.RowTexts, t => t.Contains("API_KEY=" + SecretValues.Placeholder));
        Assert.Contains(window.RowTexts, t => t == "The key is " + SecretValues.Placeholder + ".");

        // the raw line behind the result opens in a document that cannot be saved; the value is hidden there too
        window.RowList.SelectedIndex = window.RowData.ToList().FindIndex(r => r.Kind == NksHub.Textida.Core.Agents.Transcripts.TranscriptRowKind.ToolResult);
        await window.OpenRawNow();
        Dispatcher.UIThread.RunJobs();
        var raw = _w.Active!;
        Assert.True(raw.Doc.Doc.IsVirtual);
        Assert.Contains("tool_result", raw.Editor!.Document.Text);
        Assert.DoesNotContain(Key, raw.Editor.Document.Text);
        Assert.Equal(Loc.Get(K.TranscriptRawTab, ("line", 4), ("name", "session.jsonl")), raw.Title);

        await window.SearchNow(Key);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.HitTexts);
        await window.SearchNow("api_key");
        Dispatcher.UIThread.RunJobs();
        var hit = Assert.Single(window.HitTexts);
        Assert.Equal(Loc.Get(K.TranscriptHit, ("line", 4), ("snippet", window.HitData[0].Snippet)), hit);
        Assert.DoesNotContain(Key, hit);
        Assert.Contains(SecretValues.Placeholder, hit);
    }

    [AvaloniaFact]
    public async Task A_hit_opens_its_turn_and_the_files_of_a_turn_can_be_opened_in_the_editor()
    {
        var (window, _, touched) = await OpenSession();
        await window.SearchNow("int b = 3");
        Dispatcher.UIThread.RunJobs();
        Assert.Single(window.HitTexts);
        Assert.Equal((6L, 2), (window.HitData[0].Line, window.HitData[0].Turn));

        window.SelectHit(0);
        await window.TurnTask;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, window.TurnList.SelectedIndex);
        Assert.Equal(6L, Assert.IsType<NksHub.Textida.Core.Agents.Transcripts.TranscriptRow>(Assert.IsType<Border>(window.RowList.SelectedItem).Tag).Line);
        Assert.Contains(window.RowData, r => r.Kind == NksHub.Textida.Core.Agents.Transcripts.TranscriptRowKind.DiffRemoved && r.Text == "-int b = 2;");
        Assert.Contains(window.RowData, r => r.Kind == NksHub.Textida.Core.Agents.Transcripts.TranscriptRowKind.DiffAdded && r.Text == "+int b = 3;");

        Assert.Equal([Loc.Get(K.TranscriptFileItem, ("tool", "Edit"), ("path", touched))], window.FileTexts);
        window.FileList.SelectedIndex = 0;
        Assert.True(window.OpenFileNow());
        Assert.Equal(touched, _w.Active!.Path);

        File.Delete(touched);
        _w.CloseTab(_w.Active);
        Assert.False(window.OpenFileNow());
        Assert.Equal(Loc.Get(K.TranscriptNoFile, ("path", touched)), window.StatusText);
    }

    [AvaloniaFact]
    public async Task A_codex_session_file_opens_as_turns_with_the_value_of_an_env_file_hidden_in_rows_raw_line_and_hits()
    {
        string[] lines =
        [
            """{"timestamp":"2026-01-01T10:00:00Z","type":"session_meta","payload":{"id":"s1","cwd":"/work/proj","originator":"test","cli_version":"0.0.0"}}""",
            """{"timestamp":"2026-01-01T10:00:01Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"Please read the env file"}]}}""",
            """{"timestamp":"2026-01-01T10:00:02Z","type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"cat\",\".env\"]}","call_id":"c1"}}""",
            """{"timestamp":"2026-01-01T10:00:03Z","type":"response_item","payload":{"type":"function_call_output","call_id":"c1","output":"API_KEY=hunter2hunter2\nMODE=dev"}}""",
            """{"timestamp":"2026-01-01T10:00:04Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"The key is hunter2hunter2."}]}}""",
        ];
        string path = _temp.File("rollout.jsonl", string.Join("\n", lines) + "\n");
        var window = _w.OpenTranscript(path)!;
        await Until(() => { window.Tick(); return window.IsListComplete; }, "the index");
        await Until(() => { window.Tick(); return window.TurnList.SelectedIndex >= 0; }, "the first turn");
        Assert.Equal(["Before the first prompt", "#1 Please read the env file"], window.TurnTitles);

        window.TurnList.SelectedIndex = 1;
        await window.TurnTask;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(window.RowTexts, t => t.Contains(Key));
        Assert.Contains(window.RowTexts, t => t.Contains("API_KEY=" + SecretValues.Placeholder));
        Assert.Contains(window.RowTexts, t => t == "The key is " + SecretValues.Placeholder + ".");

        window.RowList.SelectedIndex = window.RowData.ToList().FindIndex(r => r.Kind == NksHub.Textida.Core.Agents.Transcripts.TranscriptRowKind.ToolResult);
        await window.OpenRawNow();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Key, _w.Active!.Editor!.Document.Text);
        Assert.Contains("function_call_output", _w.Active.Editor.Document.Text);

        await window.SearchNow(Key);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.HitTexts);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Values_from_an_env_file_of_the_opened_tabs_are_hidden_from_the_start_and_a_file_that_is_missing_says_so()
    {
        var env = _w.OpenFile(_temp.File(".env", "TOKEN=abcdef123456\n"), null);
        Assert.NotNull(env);
        string path = _temp.File("pasted.jsonl", """{"type":"user","message":{"role":"user","content":"my token is abcdef123456"}}""" + "\n");
        var window = _w.OpenTranscript(path)!;
        await Until(() => { window.Tick(); return window.IsListComplete; }, "the index");
        Assert.Equal(["#1 my token is " + SecretValues.Placeholder], window.TurnTitles);

        Assert.Null(_w.OpenTranscript(Path.Combine(_temp.Path, "missing.jsonl")));
        Assert.StartsWith("Cannot open the transcript", _w.StatusMessageText);
    }
}
