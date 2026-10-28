using System.IO.Compression;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P3b.4.1 and P3b.4.2 in the app: the consent screen, what an install changes, and the actions of the manager. The unpacking rules and the process are tested with the manager itself.
/// P3b.3.2: a command of an installed plugin with a process edits the active tab within the permissions the user gave it.
/// </summary>
[Collection("LocState")]
public class PluginManagerAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PluginManagerAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string Zip(string version, string[] permissions, Action<ZipArchive>? change = null, string id = "acme.clips")
    {
        var path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("nkspad-plugin.json").Open()))
                writer.Write("{\"schemaVersion\":1,\"id\":\"" + id + "\",\"name\":\"Acme clips\",\"version\":\"" + version + "\",\"api\":{\"min\":1,\"max\":1}," +
                    "\"permissions\":[" + string.Join(",", permissions.Select(p => "\"" + p + "\"")) + "]," +
                    "\"contributes\":{\"commands\":[{\"id\":\"" + id + ".hi\",\"title\":\"Hi\"}],\"clips\":[{\"id\":\"hi\",\"title\":\"Hi\",\"text\":\"hi\",\"command\":\"" + id + ".hi\"}]}}");
            change?.Invoke(zip);
        }
        return path;
    }

    /// <summary>The closing part of a consent line, the one that says whether the permission is new.</summary>
    private static string Tail(string key) => Loc.Get(key)[Loc.Get(key).LastIndexOf('(')..];

    private static void Finish(Task task) => ToolTestKit.WaitFor(() => task.IsCompleted, "the task did not finish");

    private static void Finish(Task<bool> task, bool expected)
    {
        Finish((Task)task);
        Assert.Equal(expected, task.Result);
    }

    [AvaloniaFact]
    public void The_consent_screen_lists_the_permissions_and_nothing_is_installed_before_the_user_agrees()
    {
        var zip = Zip("1.0.0", [DocumentPermissions.Read, DocumentPermissions.WorkspaceFiles]);

        _dialogs.Answers.Enqueue(1);
        Finish(_w.InstallPluginAsync(zip), expected: false);
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains(asked.Details!, d => d.StartsWith(DocumentPermissions.WorkspaceFiles, StringComparison.Ordinal) && d.EndsWith(Tail(K.PluginsConsentNew), StringComparison.Ordinal));
        Assert.Contains(Loc.Get(K.PluginsConsentNotice), asked.Message);
        Assert.Empty(_app.Plugins.Manager.Installed());
        Assert.Empty(_app.Clips.Clips);
        Assert.False(_app.Plugins.Permissions.IsGranted("acme.clips", DocumentPermissions.WorkspaceFiles, GrantScope.All));

        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(zip), expected: true);
        Assert.Equal(Loc.Get(K.PluginsStatusInstalled, ("name", "Acme clips"), ("version", "1.0.0")), _w.StatusMessageText);
        Assert.True(_app.Plugins.Permissions.IsGranted("acme.clips", DocumentPermissions.WorkspaceFiles, GrantScope.All));
        Assert.Equal("acme.clips", Assert.Single(_app.Clips.Clips).PluginId);
    }

    [AvaloniaFact]
    public void An_update_asks_about_new_permissions_only_and_a_refused_zip_names_its_error()
    {
        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(Zip("1.0.0", [DocumentPermissions.Read])), expected: true);
        _dialogs.Asked.Clear();

        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(Zip("1.1.0", [DocumentPermissions.Read, DocumentPermissions.EditDirect])), expected: true);
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.PluginsConsentTitleUpdate), asked.Title);
        Assert.Contains(asked.Details!, d => d.StartsWith(DocumentPermissions.Read, StringComparison.Ordinal) && d.EndsWith(Tail(K.PluginsConsentKnown), StringComparison.Ordinal));
        Assert.Contains(asked.Details!, d => d.StartsWith(DocumentPermissions.EditDirect, StringComparison.Ordinal) && d.EndsWith(Tail(K.PluginsConsentNew), StringComparison.Ordinal));
        Assert.Equal(Loc.Get(K.PluginsStatusUpdated, ("name", "Acme clips"), ("version", "1.1.0")), _w.StatusMessageText);

        _dialogs.Asked.Clear();
        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(Zip("1.2.0", [], z => z.CreateEntry("../evil.txt"))), expected: false);
        var error = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.PluginsErrorTitle), error.Title);
        Assert.Equal(Loc.Get(K.PluginsErrorUnsafePath), error.Message);
        Assert.Equal("1.1.0", Assert.Single(_app.Plugins.Manager.Installed()).Version);

        _dialogs.Asked.Clear();
        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(Zip("2.0.0", [], id: "other.plugin"), updateOf: "acme.clips"), expected: false);
        Assert.Equal(Loc.Get(K.PluginsErrorOtherPlugin, ("id", "other.plugin")), Assert.Single(_dialogs.Asked).Message);
        Assert.Equal("1.1.0", Assert.Single(_app.Plugins.Manager.Installed()).Version);
    }

    [AvaloniaFact]
    public void Disable_roll_back_and_uninstall_work_through_the_manager_questions()
    {
        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(Zip("1.0.0", [DocumentPermissions.Read])), expected: true);
        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(Zip("1.1.0", [DocumentPermissions.Read])), expected: true);
        _dialogs.Asked.Clear();

        _dialogs.Answers.Enqueue(0); // Disable
        Finish(_w.ManagePluginAsync("acme.clips"));
        Assert.False(Assert.Single(_app.Plugins.Manager.Installed()).Enabled);
        Assert.Empty(_app.Clips.Clips);
        Assert.False(_app.Plugins.Permissions.IsGranted("acme.clips", DocumentPermissions.Read, GrantScope.All));

        _dialogs.Answers.Enqueue(0); // Enable
        Finish(_w.ManagePluginAsync("acme.clips"));
        Assert.Single(_app.Clips.Clips);

        _dialogs.Answers.Enqueue(2); // Roll back (Disable/Enable, Update, Roll back)
        Finish(_w.ManagePluginAsync("acme.clips"));
        Assert.Equal("1.0.0", Assert.Single(_app.Plugins.Manager.Installed()).Version);

        _dialogs.Answers.Enqueue(4); // Uninstall
        _dialogs.Answers.Enqueue(0); // confirm
        Finish(_w.ManagePluginAsync("acme.clips"));
        Assert.Empty(_app.Plugins.Manager.Installed());
        Assert.Empty(_app.Clips.Clips);
        Assert.False(Directory.Exists(Path.Combine(_app.Plugins.Manager.Root, "acme.clips")));
    }

    // ---- P3b.2.1: what a plugin contributes comes and goes without a restart ----

    private const string KitCommands = """
        "commands":[{"id":"acme.kit.hi","title":"Hi"},{"id":"acme.kit.run","title":"Run"}],
        "menus":[{"menu":"tools","command":"acme.kit.hi","group":"9_plugins"},{"menu":"tools","command":"acme.kit.run","group":"9_plugins"}],
        "keybindings":[{"key":"mod+alt+shift+j","command":"acme.kit.hi"}],
        "settings":[{"key":"acme.kit.width","type":"integer","default":7,"title":"Width"}],
        "clips":[{"id":"hi","title":"Hi","text":"hi","command":"acme.kit.hi"}]
        """;

    /// <summary>The same without the command that only a process runs: every command of a data plugin is run by a clip.</summary>
    private const string KitDataCommands = """
        "commands":[{"id":"acme.kit.hi","title":"Hi"}],
        "menus":[{"menu":"tools","command":"acme.kit.hi","group":"9_plugins"}],
        "keybindings":[{"key":"mod+alt+shift+j","command":"acme.kit.hi"}],
        "settings":[{"key":"acme.kit.width","type":"integer","default":7,"title":"Width"}],
        "clips":[{"id":"hi","title":"Hi","text":"hi","command":"acme.kit.hi"}]
        """;

    /// <summary>A ZIP with the manifest of the plugin <c>acme.kit</c>: a data plugin, or with <paramref name="process"/> one that has the test plugin as its process.</summary>
    private string KitZip(string version, string contributes = KitCommands, bool process = false)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var executable = host ?? Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        string args = host is null ? "[]" : JsonSerializer.Serialize(new[] { Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll") });
        var path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("nkspad-plugin.json").Open()))
            writer.Write("{\"schemaVersion\":1,\"id\":\"acme.kit\",\"name\":\"Acme kit\",\"version\":\"" + version + "\",\"api\":{\"min\":1,\"max\":1},\"permissions\":[]," +
                (process ? "\"main\":{\"executable\":" + JsonSerializer.Serialize(executable) + ",\"args\":" + args + "}," : "") + "\"contributes\":{" + contributes + "}}");
        return path;
    }

    private bool KitShown(string command)
    {
        var wb = _app.Workbench;
        var context = new ShellContext(_w, forMenu: true);
        bool registered = wb.Commands.TryGet(command, out _);
        Assert.Equal(registered, wb.Commands.HasHandler(command));
        Assert.Equal(registered, wb.Commands.GetMenu("tools", context).Any(i => i.CommandId == command));
        Assert.Equal(registered, wb.Commands.Commands.Any(c => c.Id == command));
        return registered;
    }

    private void AssertKitShown(bool shown, params string[] commands)
    {
        var wb = _app.Workbench;
        foreach (var command in commands) Assert.Equal(shown, KitShown(command));
        Assert.Equal(shown, wb.Keymap.ShortcutsFor("acme.kit.hi").Count > 0);
        Assert.Equal(shown, wb.Host.Settings.TryGet("acme.kit.width", out _));
        Assert.Equal(shown, wb.SearchIndex.Search("Acme kit", wb.Settings, wb.Commands, wb.Keymap).Any(h => h.Id == "acme.kit.width"));
        Assert.Equal(shown, _app.Clips.Clips.Any(c => c.PluginId == "acme.kit"));
    }

    [AvaloniaFact]
    public void Switching_off_or_removing_a_data_plugin_takes_its_commands_menu_items_keys_and_settings_away_without_a_restart()
    {
        // a plugin without a process whose command nothing runs is refused when it is installed
        _dialogs.Answers.Enqueue(0); // OK on the error
        Finish(_w.InstallPluginAsync(KitZip("1.0.0")), expected: false);
        Assert.Contains(Assert.Single(_dialogs.Asked).Details!, d => d.Contains("acme.kit.run", StringComparison.Ordinal) && d.Contains("runs nothing", StringComparison.Ordinal));
        Assert.Empty(_app.Plugins.Manager.Installed());

        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(KitZip("1.0.0", KitDataCommands)), expected: true);
        AssertKitShown(true, "acme.kit.hi");
        Assert.Equal(7, _app.Workbench.Settings.GetInt("acme.kit.width"));

        _dialogs.Answers.Enqueue(0); // Disable
        Finish(_w.ManagePluginAsync("acme.kit"));
        AssertKitShown(false, "acme.kit.hi");

        _dialogs.Answers.Enqueue(0); // Enable
        Finish(_w.ManagePluginAsync("acme.kit"));
        AssertKitShown(true, "acme.kit.hi");

        _dialogs.Answers.Enqueue(3); // Disable/Enable, Update, Permissions, Uninstall
        _dialogs.Answers.Enqueue(0); // confirm
        Finish(_w.ManagePluginAsync("acme.kit"));
        Assert.Empty(_app.Plugins.Manager.Installed());
        AssertKitShown(false, "acme.kit.hi");
        Assert.Empty(_app.Clips.Problems);
    }

    [AvaloniaFact]
    public void A_plugin_with_a_process_runs_its_data_commands_and_loses_everything_it_contributed_when_switched_off_replaced_or_removed()
    {
        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(KitZip("1.0.0", process: true)), expected: true);
        Assert.Empty(_app.Clips.Problems);
        AssertKitShown(true, "acme.kit.hi", "acme.kit.run");
        Assert.Equal(PluginState.Inactive, StateOf("acme.kit"));

        // the data command inserts the clip without starting the process
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "x";
        tab.Editor.CaretOffset = 1;
        Assert.Equal(CommandOutcome.Handled, _app.Workbench.Commands.ExecuteAsync("acme.kit.hi", new ShellContext(_w, forMenu: false)).AsTask().GetAwaiter().GetResult());
        Assert.Equal("xhi", tab.Editor.Document.Text);
        Assert.Equal(PluginState.Inactive, StateOf("acme.kit"));

        _dialogs.Answers.Enqueue(0); // Disable (Disable/Enable, Start, Update, ...)
        Finish(_w.ManagePluginAsync("acme.kit"));
        AssertKitShown(false, "acme.kit.hi", "acme.kit.run");

        _dialogs.Answers.Enqueue(0); // Enable
        Finish(_w.ManagePluginAsync("acme.kit"));
        AssertKitShown(true, "acme.kit.hi", "acme.kit.run");

        // a new version with other commands replaces the old ones at once
        const string next = """
            "commands":[{"id":"acme.kit.bye","title":"Bye"}],
            "menus":[{"menu":"tools","command":"acme.kit.bye","group":"9_plugins"}]
            """;
        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(KitZip("1.1.0", next, process: true)), expected: true);
        AssertKitShown(false, "acme.kit.hi", "acme.kit.run");
        Assert.True(KitShown("acme.kit.bye"));

        _dialogs.Answers.Enqueue(5); // Disable/Enable, Start, Update, Roll back, Permissions, Uninstall
        _dialogs.Answers.Enqueue(0); // confirm
        Finish(_w.ManagePluginAsync("acme.kit"));
        Assert.Empty(_app.Plugins.Manager.Installed());
        Assert.False(KitShown("acme.kit.bye"));
        AssertKitShown(false, "acme.kit.hi", "acme.kit.run");
    }

    // ---- P3b.3.2: a command of an installed plugin edits the active tab ----

    private const string EditPlugin = "acme.edit";

    private const string ApplyH = """{"method":"edit/apply","params":{"document":"$document","revision":$revision,"batchId":"b1","edits":[{"start":0,"end":1,"text":"H"}]}}""";

    /// <summary>Installs the test plugin as a plugin with a process; its command sends <paramref name="onCommand"/> to the editor. Returns a new tab with "hello", which is then the active one.</summary>
    private DocTab InstallEditor(string[] permissions, string onCommand)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var executable = host ?? Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        List<string> args = host is null ? [] : [Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll")];
        args.AddRange(["--on-command", onCommand]);
        static string List(IEnumerable<string> items) => "[" + string.Join(",", items.Select(i => JsonSerializer.Serialize(i))) + "]";

        var path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("nkspad-plugin.json").Open()))
            writer.Write("{\"schemaVersion\":1,\"id\":\"" + EditPlugin + "\",\"name\":\"Acme edit\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"permissions\":" + List(permissions) +
                ",\"main\":{\"executable\":" + JsonSerializer.Serialize(executable) + ",\"args\":" + List(args) + "},\"contributes\":{\"commands\":[{\"id\":\"" + EditPlugin + ".run\",\"title\":\"Run\"}]}}");
        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(path), expected: true);

        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "hello";
        return tab;
    }

    private Task<CommandOutcome> RunEditCommand() => _app.Workbench.Commands.ExecuteAsync(EditPlugin + ".run", new ShellContext(_w, forMenu: false)).AsTask();

    /// <summary>The one command the plugin ran: the request it got and what the editor answered to its call.</summary>
    private JsonElement RanCommand()
    {
        var task = _app.Plugins.HostForTests.CallAsync(EditPlugin, "commands");
        Finish(task);
        return Assert.Single(task.Result.Clone().EnumerateArray());
    }

    [AvaloniaFact]
    public void A_command_of_an_installed_plugin_with_direct_edit_writes_the_active_tab_as_one_undo_step()
    {
        var tab = InstallEditor([DocumentPermissions.Read, DocumentPermissions.EditDirect], ApplyH);
        var doc = tab.Doc.Doc;
        long revision = doc.Revision;
        int depth = doc.UndoDepth;

        var run = RunEditCommand();
        Finish(run);

        Assert.Equal(CommandOutcome.Handled, run.Result);
        Assert.Equal("Hello", tab.Editor!.Document.Text);
        Assert.Equal(depth + 1, doc.UndoDepth);
        var command = RanCommand();
        Assert.Equal(doc.Id.ToString(), command.GetProperty("params").GetProperty("document").GetString());
        Assert.Equal(revision, command.GetProperty("params").GetProperty("revision").GetInt64());
        Assert.True(command.GetProperty("answer").GetProperty("applied").GetBoolean());
        Assert.False(_app.Plugins.Documents.IsExposed(doc.Id)); // seen only while the command ran
    }

    [AvaloniaFact]
    public void A_proposal_of_an_installed_plugin_waits_in_the_change_review_and_the_plugin_hears_the_decision()
    {
        var tab = InstallEditor([DocumentPermissions.Read, DocumentPermissions.EditPropose],
            """{"method":"edit/propose","params":{"document":"$document","revision":$revision,"title":"Shout","edits":[{"start":0,"end":5,"text":"HELLO"}]}}""");

        var run = RunEditCommand();
        ToolTestKit.WaitFor(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        Assert.False(run.IsCompleted);
        Assert.Equal("hello", tab.Editor!.Document.Text);

        _w.ApplyPlan();
        Finish(run);

        Assert.Equal("HELLO", tab.Editor.Document.Text);
        Assert.Equal("applied", RanCommand().GetProperty("answer").GetProperty("decision").GetString());
    }

    [AvaloniaFact]
    public void A_direct_edit_of_an_installed_plugin_without_the_permission_is_an_error_and_writes_nothing()
    {
        var tab = InstallEditor([DocumentPermissions.Read], ApplyH);

        var run = RunEditCommand();
        Finish(run);

        Assert.Equal("hello", tab.Editor!.Document.Text);
        Assert.Equal(DocumentErrorCodes.PermissionDenied, RanCommand().GetProperty("answer").GetProperty("error").GetInt32());
    }

    [AvaloniaFact]
    public void The_manager_lists_the_built_in_plugins_and_the_shipped_language_client_holds_workspace_files()
    {
        _w.ShowPluginManager();
        Assert.Equal(PluginServices.BuiltIns.Count, PluginServices.BuiltIns.Count(b => _app.Plugins.Permissions.GrantsOf(b.Id).Count > 0));
        Assert.True(_app.Plugins.Permissions.IsGranted(PluginServices.LspPluginId, DocumentPermissions.WorkspaceFiles, GrantScope.All));
        Assert.False(_app.Plugins.Permissions.IsGranted(PluginServices.JsonPluginId, DocumentPermissions.WorkspaceFiles, GrantScope.All));
        Assert.Empty(_dialogs.Asked);
    }

    // ---- P3b.1.3: activation events and the life of the process ----

    /// <summary>Installs the test plugin as a plugin with a process that lists <paramref name="activationEvents"/> and no command.</summary>
    private void InstallProcessPlugin(string id, string[] activationEvents, params string[] pluginArgs)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var executable = host ?? Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        List<string> args = host is null ? [] : [Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll")];
        args.AddRange(pluginArgs);
        static string List(IEnumerable<string> items) => "[" + string.Join(",", items.Select(i => JsonSerializer.Serialize(i))) + "]";

        var path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("nkspad-plugin.json").Open()))
            writer.Write("{\"schemaVersion\":1,\"id\":\"" + id + "\",\"name\":\"Acme " + id + "\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"permissions\":[]," +
                "\"main\":{\"executable\":" + JsonSerializer.Serialize(executable) + ",\"args\":" + List(args) + ",\"activationEvents\":" + List(activationEvents) + "}}");
        _dialogs.Answers.Enqueue(0);
        Finish(_w.InstallPluginAsync(path), expected: true);
    }

    private PluginState StateOf(string id) => _app.Plugins.StatusOf(id)!.State;

    private Task<JsonElement> CallPlugin(string id, string method) => _app.Plugins.HostForTests.CallAsync(id, method);

    [AvaloniaFact]
    public void A_plugin_starts_when_a_document_in_its_language_or_of_its_file_type_is_shown()
    {
        InstallProcessPlugin("acme.lang", ["onLanguage:json"]);
        InstallProcessPlugin("acme.type", ["onFileType:.md"]);
        Assert.Equal(PluginState.Inactive, StateOf("acme.lang"));
        Assert.Equal(PluginState.Inactive, StateOf("acme.type"));

        var tab = _w.NewDocument();
        _app.Plugins.TrackActivation(_w, tab); // a plain text document asks for neither
        Assert.Equal(PluginState.Inactive, StateOf("acme.lang"));

        tab.Doc.ChooseLanguage(LanguageModes.ById("json"));
        _app.Plugins.TrackActivation(_w, tab);
        ToolTestKit.WaitFor(() => StateOf("acme.lang") == PluginState.Running, "the plugin of the language to start");
        Assert.Equal(PluginState.Inactive, StateOf("acme.type"));
    }

    [AvaloniaFact]
    public void A_stopped_plugin_starts_again_when_a_document_of_its_language_is_shown_again_but_not_at_a_mere_refresh()
    {
        InstallProcessPlugin("acme.lang", ["onLanguage:json"]);
        var plain = _w.NewDocument();
        var json = _w.NewDocument();
        json.Doc.ChooseLanguage(LanguageModes.ById("json"));
        _w.SelectTab(json); // the window looks at the active tab too, so the calls below say what it would say
        _app.Plugins.TrackActivation(_w, json);
        ToolTestKit.WaitFor(() => StateOf("acme.lang") == PluginState.Running, "the plugin of the language to start");

        Finish(_app.Plugins.StopPluginAsync("acme.lang"));
        Assert.Equal(PluginState.Inactive, StateOf("acme.lang"));
        for (int i = 0; i < 10; i++)
        {
            _app.Plugins.TrackActivation(_w, json); // the status bar looking again at the same document is no event
            ToolTestKit.Pump();
        }
        Assert.Equal(PluginState.Inactive, StateOf("acme.lang"));

        _app.Plugins.TrackActivation(_w, plain);
        _app.Plugins.TrackActivation(_w, json); // the document is shown again
        ToolTestKit.WaitFor(() => StateOf("acme.lang") == PluginState.Running, "the stopped plugin to start again");
    }

    [AvaloniaFact]
    public void A_plugin_that_lists_onStartup_starts_once_the_editor_has_started_and_not_before()
    {
        InstallProcessPlugin("acme.boot", [ActivationEvents.Startup]);
        Assert.Equal(PluginState.Inactive, StateOf("acme.boot"));

        _app.Plugins.ActivateStartup();
        ToolTestKit.WaitFor(() => StateOf("acme.boot") == PluginState.Running, "the plugin to start");
    }

    [AvaloniaFact]
    public void A_plugin_that_lists_workspaceContains_starts_when_an_open_folder_holds_a_matching_file()
    {
        InstallProcessPlugin("acme.maven", ["workspaceContains:pom.xml"]);
        var empty = Directory.CreateDirectory(Path.Combine(_temp.Path, "empty")).FullName;
        var project = Directory.CreateDirectory(Path.Combine(_temp.Path, "project")).FullName;
        File.WriteAllText(Path.Combine(project, "pom.xml"), "");

        var none = _app.Plugins.HostForTests.ActivateWorkspaceAsync([empty]);
        Finish(none);
        Assert.Empty(none.Result);
        Assert.Equal(PluginState.Inactive, StateOf("acme.maven"));

        var some = _app.Plugins.HostForTests.ActivateWorkspaceAsync([empty, project]);
        Finish(some);
        Assert.Equal(["acme.maven"], some.Result);
        Assert.Equal(PluginState.Running, StateOf("acme.maven"));
    }

    [AvaloniaFact]
    public void The_manager_starts_stops_and_restarts_the_process_of_a_plugin_and_says_what_it_does()
    {
        InstallProcessPlugin("acme.run", ["onCommand:acme.run.go"]);
        _dialogs.Asked.Clear();

        _dialogs.Answers.Enqueue(1); // Disable/Enable, Start
        Finish(_w.ManagePluginAsync("acme.run"));
        Assert.Contains(Loc.Get(K.PluginStateInactive), Assert.Single(_dialogs.Asked).Message);
        Assert.Equal(PluginState.Running, StateOf("acme.run"));
        Assert.Equal(Loc.Get(K.PluginsStatusStarted, ("name", "Acme acme.run")), _w.StatusMessageText);
        var first = CallPlugin("acme.run", "pid");
        Finish(first);

        _dialogs.Asked.Clear();
        _dialogs.Answers.Enqueue(2); // Disable/Enable, Stop, Restart
        Finish(_w.ManagePluginAsync("acme.run"));
        Assert.Contains(Loc.Get(K.PluginStateRunning), Assert.Single(_dialogs.Asked).Message);
        Assert.Equal(Loc.Get(K.PluginsStatusRestarted, ("name", "Acme acme.run")), _w.StatusMessageText);
        var second = CallPlugin("acme.run", "pid");
        Finish(second);
        Assert.NotEqual(first.Result.GetInt32(), second.Result.GetInt32());

        _dialogs.Answers.Enqueue(1); // Stop
        Finish(_w.ManagePluginAsync("acme.run"));
        Assert.Equal(PluginState.Inactive, StateOf("acme.run"));
        Assert.Equal(Loc.Get(K.PluginsStatusStopped, ("name", "Acme acme.run")), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void A_plugin_that_lists_onPanel_is_offered_in_the_list_of_panels_and_starts_when_the_user_opens_it()
    {
        InstallProcessPlugin("acme.tree", ["onPanel:acme.files"]);
        InstallProcessPlugin("acme.other", ["onPanel:acme.other"]);
        Assert.Equal(PluginState.Inactive, StateOf("acme.tree"));

        Assert.Equal(CommandOutcome.Handled, _w.ShowPluginPanels());
        Assert.Equal(["acme.files", "acme.other"], _w.QuickResults.Select(r => r.Title).Order(StringComparer.Ordinal));
        Assert.All(_w.QuickResults, r => Assert.Equal(Loc.Get(K.PluginPanelsStarts), r.Detail));
        Assert.Equal(PluginState.Inactive, StateOf("acme.tree")); // listing a panel starts nothing

        _w.SetPaletteQuery("acme.files");
        _w.RunPaletteSelection();
        ToolTestKit.WaitFor(() => StateOf("acme.tree") == PluginState.Running, "the plugin of the panel to start");
        Assert.Equal(PluginState.Inactive, StateOf("acme.other"));
    }

    [AvaloniaFact]
    public void A_plugin_that_answers_the_handshake_with_another_api_version_is_turned_off_and_the_manager_says_why()
    {
        InstallProcessPlugin("acme.old", [ActivationEvents.Startup], "--api", "2");
        InstallProcessPlugin("acme.good", [ActivationEvents.Startup]);

        _app.Plugins.ActivateStartup();
        ToolTestKit.WaitFor(() => StateOf("acme.old") == PluginState.Disabled && StateOf("acme.good") == PluginState.Running, "the refused plugin to be turned off and the other to run");

        var text = PluginServices.StateText(_app.Plugins.StatusOf("acme.old")!);
        Assert.Contains("version 2", text);
        Assert.Contains("version 1", text);
        Finish(_app.Plugins.StartPluginAsync("acme.old"));
        Assert.Equal(PluginState.Disabled, StateOf("acme.old"));
    }

    [AvaloniaFact]
    public void Closing_the_editor_ends_every_plugin_process_and_ending_twice_changes_nothing()
    {
        InstallProcessPlugin("acme.one", [ActivationEvents.Startup]);
        InstallProcessPlugin("acme.two", [ActivationEvents.Startup]);
        _app.Plugins.ActivateStartup();
        ToolTestKit.WaitFor(() => StateOf("acme.one") == PluginState.Running && StateOf("acme.two") == PluginState.Running, "both plugins to start");
        int[] pids = [.. new[] { "acme.one", "acme.two" }.Select(id => _app.Plugins.StatusOf(id)!.ProcessId!.Value)];
        Assert.All(pids, pid => Assert.True(Alive(pid), "process " + pid));

        Finish(Task.Run(_app.Plugins.Shutdown)); // the end waits for jobs of the UI thread, which Finish keeps running
        Assert.All(pids, pid => Assert.False(Alive(pid), "process " + pid));
        Finish(Task.Run(_app.Plugins.Shutdown));
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    [AvaloniaFact]
    public void A_built_in_plugin_offers_start_while_it_does_not_run_and_the_dialog_names_its_state()
    {
        _dialogs.Answers.Enqueue(1); // Close (the only other button; the plugin does not run, so Start is first)
        Finish(_w.ManageBuiltInAsync(PluginServices.TextToolsPluginId, "Text tools"));
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains(Loc.Get(K.PluginStateInactive), asked.Message);
        Assert.Equal([Loc.Get(K.PluginsManageStart), Loc.Get(K.PluginsManageClose)], asked.Buttons.Select(b => b.Label));
        Assert.Equal(PluginState.Inactive, StateOf(PluginServices.TextToolsPluginId));
    }
}

/// <summary>The words the plugin manager uses for the state of a plugin process.</summary>
[Collection("LocState")]
public class PluginStateTextTests : IDisposable
{
    public void Dispose() => Loc.SetLanguage(Loc.DefaultLanguage);

    private static PluginStatus Status(PluginState state, PluginCrashInfo? crash = null, string? reason = null, bool idleStop = false) => new("p", "Plugin", state, null, crash, reason, 0, idleStop);

    private static readonly PluginCrashInfo Crash = new("p", "Plugin", PluginEndReason.Unresponsive, null, "", [], TimeSpan.FromSeconds(4.2));

    public static IEnumerable<object[]> Languages() => Loc.Languages.Select(l => new object[] { l });

    [Fact]
    public void Every_state_has_its_words_in_english()
    {
        Loc.SetLanguage("en");
        Assert.Equal("not running, starts when needed", PluginServices.StateText(Status(PluginState.Inactive)));
        Assert.Equal("not running, starts when needed", PluginServices.StateText(Status(PluginState.Stopped)));
        Assert.Equal("starting", PluginServices.StateText(Status(PluginState.Starting)));
        Assert.Equal("running", PluginServices.StateText(Status(PluginState.Running)));
        Assert.Equal("running, ends when it has no work", PluginServices.StateText(Status(PluginState.Running, idleStop: true)));
        Assert.Equal("stopping", PluginServices.StateText(Status(PluginState.Stopping)));
        Assert.Equal("stopped (no answer), starts again in 5 s", PluginServices.StateText(Status(PluginState.WaitingRestart, Crash)));
        Assert.Equal("turned off (no answer)", PluginServices.StateText(Status(PluginState.Disabled, Crash)));
        Assert.Equal("turned off (refused the handshake)", PluginServices.StateText(Status(PluginState.Disabled, reason: "refused the handshake")));
    }

    [Theory, MemberData(nameof(Languages))]
    public void Every_state_reads_as_text_without_leftover_placeholders_in_every_language(string language)
    {
        Loc.SetLanguage(language);
        var texts = new[]
        {
            Status(PluginState.Inactive), Status(PluginState.Starting), Status(PluginState.Running), Status(PluginState.Running, idleStop: true), Status(PluginState.Stopping),
            Status(PluginState.WaitingRestart, Crash), Status(PluginState.Disabled, Crash),
        }.Select(PluginServices.StateText).ToList();
        Assert.All(texts, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t));
            Assert.DoesNotContain('{', t);
        });
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }
}
