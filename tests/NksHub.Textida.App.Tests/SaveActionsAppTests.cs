using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Remote;
using NksHub.Textida.Remote.Tests;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P8.3.1 in the window: the save actions of a file around a manual save and an autosave, with the test program of ToolEcho as the formatter and as the tool,
/// and the upload against a real FTPS server in the test process. The formatters of the catalog (gofmt and the others) are not started.
/// </summary>
[Collection("LocState")]
public class SaveActionsAppTests : IDisposable
{
    private const string Password = "S3cret-Pass-9981";

    private readonly TempDir _temp = new();
    private readonly TempDir _server = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly MemoryCredentials _secrets = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private FtpsServer? _ftps;

    public SaveActionsAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _app.Remote = new RemoteServices(_app.ProfileDirectory, _secrets);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        if (_ftps is not null) Task.Run(() => _ftps.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(10));
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
        _server.Dispose();
    }

    private string ActionsFile => Path.Combine(_app.ProfileDirectory, SaveActionConfig.FileName);

    private void Configure(int? formatTimeoutSeconds, params SaveActionEntry[] actions) =>
        SaveActionConfig.Write(ActionsFile, new SaveActionsFile { FormatTimeoutSeconds = formatTimeoutSeconds, Actions = [.. actions] });

    private static SaveActionEntry Entry(string kind, string? id = null, bool onlyManual = false) => new() { Kind = kind, Id = id, OnlyManual = onlyManual };

    private static FormatterDef Fake(params string[] mode) => new("fake", "Fake", ToolTestKit.Dotnet, [ToolTestKit.EchoDll, .. mode, "${File}"], [], [".txt"]);

    private static async Task Until(Func<bool> condition, string what, int timeoutMs = 30_000)
    {
        for (var sw = Stopwatch.StartNew(); !condition(); await Task.Delay(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Waited for " + what);
        }
    }

    private static string? Read(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
    }

    private async Task<DocTab> OpenRemoteAsync(string remotePath)
    {
        _ftps = await FtpsServer.StartAsync(_server.Path, password: Password);
        _app.Remote.Connections.Add(_ftps.Connection(_ftps.Identity) with { Name = "Test server" }, Password, _secrets);
        _dialogs.Prompts.Enqueue(remotePath);
        _w.OpenRemote();
        await Until(() => _w.QuickResults.Count > 0, "the list of connections");
        _w.QuickResults[0].Run();
        await Until(() => _w.Tabs.Any(t => t.Doc.RemoteCopy is not null), "the remote file to open");
        var tab = _w.Tabs.First(t => t.Doc.RemoteCopy is not null);
        await Until(() => tab.Bar is InfoBar bar && bar.Message != Loc.Get(K.RemoteBarOffline), "the bar about the server");
        return tab;
    }

    private async Task SaveAsync(DocTab tab, SaveOrigin origin = SaveOrigin.Manual)
    {
        var task = _w.SaveAsync(tab, origin);
        await Until(() => task.IsCompleted, "the save");
        Assert.True(await task);
    }

    private List<string> TaskLines() => [.. _app.Output.Read(OutputChannel.Tasks).Select(l => l.Text)];

    private int Runs(string mark) => TaskLines().Count(l => l.StartsWith("> ", StringComparison.Ordinal) && l.Contains(mark));

    private void DefineTools(params (string Id, string Mark)[] tools)
    {
        var array = new System.Text.Json.Nodes.JsonArray();
        foreach (var (id, mark) in tools) array.Add(ToolTestKit.Tool(id, id, [mark]));
        File.WriteAllText(_app.Tools.FilePath, new System.Text.Json.Nodes.JsonObject { ["externalTools"] = array, ["logPatterns"] = new System.Text.Json.Nodes.JsonArray() }.ToJsonString());
        foreach (var entry in _app.Tools.Load().Tools) _app.ToolApprovals.Approve(_w.ResolveTool(entry.Spec)); // a tool nobody confirmed does not start after an autosave
    }

    [AvaloniaFact]
    public async Task The_upload_follows_the_save_and_sends_the_saved_text_even_when_the_formatter_timed_out()
    {
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "server one");
        var tab = await OpenRemoteAsync("/a.txt");
        Configure(2, Entry("format", "fake"), Entry("upload"));
        _w.SaveActionRunner = new SaveActionRunner(_ => Fake("--sleep"));
        tab.Editor!.Document.Insert(0, "edited ");

        await SaveAsync(tab);
        await Until(() => Read(Path.Combine(_server.Path, "a.txt")) == "edited server one", "the upload");

        Assert.Equal("edited server one", File.ReadAllText(tab.Path!)); // saved as typed, not formatted
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Equal(LocalState.InSync, _app.Remote.Copies.LocalStateOf(tab.Doc.RemoteCopy!));
        Assert.Contains(Loc.Get(K.SaveActionsFormatTimedOut, ("name", "fake"), ("seconds", "2")), TaskLines());
    }

    [AvaloniaFact]
    public async Task A_formatter_that_succeeds_is_saved_and_that_text_is_the_one_uploaded()
    {
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "server one");
        var tab = await OpenRemoteAsync("/a.txt");
        Configure(60, Entry("format", "fake"), Entry("upload"));
        _w.SaveActionRunner = new SaveActionRunner(_ => Fake("--format"));
        tab.Editor!.Document.Insert(0, "edited ");

        await SaveAsync(tab);
        await Until(() => Read(Path.Combine(_server.Path, "a.txt")) == "EDITED SERVER ONE", "the upload of the formatted text");

        Assert.Equal("EDITED SERVER ONE", tab.Editor!.Document.Text);
        Assert.Equal("EDITED SERVER ONE", File.ReadAllText(tab.Path!));
        Assert.Equal(["a.txt"], Directory.GetFiles(_server.Path).Select(Path.GetFileName));
        Assert.DoesNotContain(Directory.GetFiles(Path.GetDirectoryName(tab.Path!)!), f => f.Contains(".textida-fmt-", StringComparison.Ordinal)); // no temporary copy is left next to the file
    }

    [AvaloniaFact]
    public async Task A_syntax_error_saves_the_file_unformatted_and_says_why()
    {
        string path = _temp.File("s.txt", "abc");
        var tab = _w.OpenFile(path, null);
        Configure(60, Entry("format", "fake"));
        _w.SaveActionRunner = new SaveActionRunner(_ => Fake("--format-fail"));
        tab.Editor!.Document.Insert(0, "x");

        await SaveAsync(tab);

        Assert.Equal("xabc", File.ReadAllText(path));
        Assert.Contains("syntax error", _w.StatusMessageText);
        Assert.Contains("code 2", _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task A_missing_formatter_saves_the_file_unformatted_and_says_why()
    {
        string path = _temp.File("m.txt", "abc");
        var tab = _w.OpenFile(path, null);
        Configure(60, Entry("format", "fake"));
        _w.SaveActionRunner = new SaveActionRunner(_ => new FormatterDef("fake", "Fake", "textida-no-such-formatter", ["${File}"], [], [".txt"]));
        tab.Editor!.Document.Insert(0, "x");

        await SaveAsync(tab);

        Assert.Equal("xabc", File.ReadAllText(path));
        Assert.Contains("textida-no-such-formatter", _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task An_autosave_leaves_out_the_actions_for_a_manual_save_only_and_a_save_by_a_plugin_runs_none()
    {
        string path = _temp.File("auto.txt", "a");
        var tab = _w.OpenFile(path, null);
        DefineTools(("always", "--mark=always"), ("manual", "--mark=manual"));
        Configure(null, Entry("tool", "always"), Entry("tool", "manual", onlyManual: true));
        _app.Workbench.Set("files.autoSave", "afterDelay");
        Assert.NotNull(tab.Doc.Autosave);

        tab.Editor!.Document.Insert(1, "b");
        var autosave = tab.Doc.Autosave!.Pipeline!();
        await Until(() => autosave.IsCompleted, "the autosave");
        Assert.Equal("ab", File.ReadAllText(path));
        Assert.Equal(1, Runs("--mark=always"));
        Assert.Equal(0, Runs("--mark=manual"));

        tab.Editor!.Document.Insert(2, "c");
        await SaveAsync(tab, SaveOrigin.Plugin);
        Assert.Equal("abc", File.ReadAllText(path));
        Assert.Equal(1, Runs("--mark=always"));

        tab.Editor!.Document.Insert(3, "d");
        await SaveAsync(tab);
        await Until(() => Runs("--mark=manual") == 1, "the manual-only tool after a manual save");
        Assert.Equal(2, Runs("--mark=always"));
    }

    [AvaloniaFact]
    public async Task A_tool_that_nobody_confirmed_does_not_start_after_an_autosave()
    {
        string path = _temp.File("auto2.txt", "a");
        var tab = _w.OpenFile(path, null);
        var array = new System.Text.Json.Nodes.JsonArray { ToolTestKit.Tool("new", "new", ["--mark=new"]) };
        File.WriteAllText(_app.Tools.FilePath, new System.Text.Json.Nodes.JsonObject { ["externalTools"] = array, ["logPatterns"] = new System.Text.Json.Nodes.JsonArray() }.ToJsonString());
        Configure(null, Entry("tool", "new"));
        _app.Workbench.Set("files.autoSave", "afterDelay");

        tab.Editor!.Document.Insert(1, "b");
        var autosave = tab.Doc.Autosave!.Pipeline!();
        await Until(() => autosave.IsCompleted, "the autosave");

        Assert.Equal("ab", File.ReadAllText(path));
        Assert.Empty(_dialogs.Asked);
        Assert.DoesNotContain(TaskLines(), l => l.StartsWith("> ", StringComparison.Ordinal));
        Assert.Contains(TaskLines(), l => l.Contains(Loc.Get(K.SaveActionsToolNotApproved)));
    }

    [AvaloniaFact]
    public async Task A_formatter_is_offered_only_where_its_configuration_exists_and_is_off_until_switched_on()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        string go = Path.Combine(folder, "main.go");
        File.WriteAllText(go, "package main\n");
        var tab = _w.OpenFile(go, null);

        _w.ShowSaveActions();
        Assert.DoesNotContain(_w.QuickResults, r => r.Title.Contains("gofmt"));

        File.WriteAllText(Path.Combine(folder, "go.mod"), "module x\n");
        _w.ShowSaveActions();
        var offered = Assert.Single(_w.QuickResults, r => r.Title.Contains("gofmt"));
        Assert.Contains(Loc.Get(K.SaveActionsOff), offered.Detail!);
        Assert.False(File.Exists(ActionsFile)); // offering it turns nothing on

        offered.Run();
        var set = SaveActionConfig.Load(ActionsFile, null);
        var action = Assert.Single(set.Actions);
        Assert.Equal((SaveActionKind.Format, "gofmt", true), (action.Kind, action.Id, action.Enabled));
        Assert.Contains(_w.QuickResults, r => r.Title.Contains("gofmt") && r.Detail!.Contains(Loc.Get(K.SaveActionsOn)));

        _w.QuickResults.First(r => r.Title == Loc.Get(K.SaveActionsToggleManual, ("name", Loc.Get(K.SaveActionsFormat, ("name", "gofmt"))))).Run();
        Assert.True(Assert.Single(SaveActionConfig.Load(ActionsFile, null).Actions).OnlyManual);
        await Task.CompletedTask;
        _w.CloseTab(tab);
    }
}
