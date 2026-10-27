using System.IO.Compression;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
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
}
