using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P6.2.2 and P6.2.6 in the app: a real test plugin process opens a virtual document and the diff of two revisions through the document API,
/// stages one hunk into its index, and the window shows the tab, searches it, refuses writes and keeps hidden values hidden.
/// </summary>
[Collection("LocState")]
public class PluginDiffVirtualTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PluginDiffVirtualTests()
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

    // The test thread is the UI thread, so everything the plugin sends waits for RunJobs; these helpers keep pumping until their condition holds.
    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static async Task<T> Pump<T>(Task<T> task)
    {
        await Until(() => task.IsCompleted, "a call");
        return await task;
    }

    private PluginHost.PluginHost Host => _app.Plugins.HostForTests;

    private async Task StartPluginAsync()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll");
        Assert.True(File.Exists(dll), dll);
        Host.Register(new PluginSpec("p", "Test plugin", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [dll], ["onCommand:p"]));
        await Pump(Host.ActivateAsync("onCommand:p"));
        _app.Plugins.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
    }

    private async Task<JsonElement> Editor(string method, string paramsJson)
    {
        var json = $"{{\"method\":\"{method}\",\"params\":{paramsJson}}}";
        return await Pump(Call(json));
    }

    private async Task<JsonElement> Call(string json) => (await Host.CallAsync("p", "callEditor", Encoding.UTF8.GetBytes(json))).Clone();

    private static int ErrorOf(JsonElement r) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("error", out var e) ? e.GetInt32() : 0;

    private static string OpenParams(string key, string name, string text, string? title = null) =>
        $"{{\"key\":\"{key}\",\"name\":\"{name}\",\"text\":{JsonSerializer.Serialize(text)}{(title is null ? "" : $",\"title\":\"{title}\"")}}}";

    private DocTab VirtualTab(JsonElement opened) =>
        _w.Tabs.Single(t => t.Doc.Doc.Id == opened.GetProperty("document").GetGuid());

    private static string Selected(DocTab t)
    {
        var s = t.Editor!.GetSelections()[0];
        return t.Editor.Document.GetText(s.Start, s.Length);
    }

    private async Task<SearchResultsView> SearchOpenDocuments(string query)
    {
        string folder = Path.Combine(_temp.Path, "empty");
        Directory.CreateDirectory(folder);
        _w.ShowFindInFiles(false);
        _w.FindBar.FolderBox.Text = folder;
        _w.FindBar.FindBox.Text = query;
        await _w.RunFilesSearchAsync();
        var view = _w.ResultsView!;
        await Until(() => !view.IsRunning, "the search");
        return view;
    }

    [AvaloniaFact]
    public async Task A_virtual_document_is_a_read_only_tab_the_editor_finds_text_in_and_a_write_is_refused()
    {
        await StartPluginAsync();
        var opened = await Editor(DocumentMethods.VirtualOpen, OpenParams("agent:1", "transcript.md", "alpha\nbeta\n", "Transcript"));
        var tab = VirtualTab(opened);

        Assert.Same(tab, _w.Active);
        Assert.Equal("Transcript", tab.Title);
        Assert.Equal("alpha\nbeta\n", tab.Editor!.Document.Text);
        Assert.True(tab.Editor.IsReadOnly);
        Assert.True(tab.Doc.IsVirtual && tab.Doc.ReadOnly);
        Assert.False(tab.Doc.HasUnsavedWork);

        TestApp.Press(_w, Key.F, TestApp.Mod);
        _w.FindBar.FindBox.Text = "beta";
        Dispatcher.UIThread.RunJobs();
        await Until(() => !_w.FindIsRunning && Selected(tab) == "beta", "the find selecting the match");
        Assert.Equal("beta", Selected(tab));

        var revision = opened.GetProperty("revision").GetInt64();
        _app.Plugins.Permissions.Grant("p", DocumentPermissions.EditDirect, GrantScope.All); // allowed to write, still refused: the document is a view
        var write = await Editor(DocumentMethods.EditApply, $"{{\"document\":\"{tab.Doc.Doc.Id}\",\"revision\":{revision},\"batchId\":\"b\",\"edits\":[{{\"start\":0,\"end\":5,\"text\":\"ALPHA\"}}]}}");
        Assert.Equal(DocumentErrorCodes.ReadOnlyDocument, ErrorOf(write));
        _w.Undo();
        Assert.False(await _w.SaveAsync(tab));
        Assert.False(await _w.SaveAsAsync(tab));
        Assert.Empty(_dialogs.Asked);
        Assert.Equal("alpha\nbeta\n", tab.Editor.Document.Text);
        Assert.Equal(revision, tab.Doc.Doc.Revision);

        var updated = await Editor(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{tab.Doc.Doc.Id}\",\"baseRevision\":{revision},\"text\":\"alpha\\nbeta\\ngamma\\n\"}}");
        Assert.Equal(revision + 1, updated.GetProperty("revision").GetInt64());
        Assert.Equal("alpha\nbeta\ngamma\n", tab.Editor.Document.Text);
        Assert.False(tab.Doc.HasUnsavedWork);
        _w.Undo();
        Assert.Equal("alpha\nbeta\ngamma\n", tab.Editor.Document.Text);

        Assert.True(await _w.CloseTabAsync(tab));
        Assert.Empty(_dialogs.Asked);
        var again = await Editor(DocumentMethods.VirtualOpen, OpenParams("agent:1", "transcript.md", "new\n"));
        Assert.True(again.GetProperty("created").GetBoolean());
    }

    [AvaloniaFact]
    public async Task Find_in_files_covers_a_virtual_document_and_shows_no_hidden_value()
    {
        await StartPluginAsync();
        var opened = await Editor(DocumentMethods.VirtualOpen, OpenParams("env", ".env", "API_KEY=hunter2\nname=shown\n"));
        var tab = VirtualTab(opened);
        Assert.True(tab.Editor!.SecretsHidden);

        var view = await SearchOpenDocuments("hunter2");
        var hit = Assert.Single(view.Rows.OfType<SearchResultsView.HitRow>()).Hit;
        Assert.Equal(tab.Doc.Doc.Id, hit.Source.DocumentId);
        Assert.DoesNotContain("hunter2", hit.Preview);
        Assert.Contains(SecretValues.Placeholder, hit.Preview);

        var key = await SearchOpenDocuments("API_KEY");
        Assert.StartsWith("API_KEY=", Assert.Single(key.Rows.OfType<SearchResultsView.HitRow>()).Hit.Preview);

        var revision = opened.GetProperty("revision").GetInt64();
        var read = await Editor(DocumentMethods.ReadText, $"{{\"document\":\"{tab.Doc.Doc.Id}\",\"revision\":{revision},\"start\":0,\"end\":{tab.Doc.Doc.ByteLength}}}");
        Assert.DoesNotContain("hunter2", read.GetProperty("text").GetString());
    }

    [AvaloniaFact]
    public async Task A_plugin_opens_the_diff_of_two_revisions_and_the_tab_keeps_those_revisions()
    {
        await StartPluginAsync();
        var tree = _w.NewDocument();
        tree.Editor!.Document.Text = "a\nb\nc\n";
        _app.Plugins.Documents.Expose(tree.Doc.Doc, tree.Editor.ByteSelections);
        _app.Plugins.Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        var snapshot = (await Editor(DocumentMethods.SnapshotCreate, $"{{\"document\":\"{tree.Doc.Doc.Id}\"}}")).GetProperty("snapshot").GetString();
        tree.Editor.Document.Replace(2, 1, "B");
        long revision = tree.Doc.Doc.Revision;

        var shown = await Editor(DocumentMethods.DiffOpen,
            $"{{\"left\":{{\"snapshot\":\"{snapshot}\"}},\"right\":{{\"document\":\"{tree.Doc.Doc.Id}\",\"revision\":{revision}}},\"title\":\"HEAD / tree\",\"leftTitle\":\"HEAD\"}}");
        Assert.True(shown.GetProperty("opened").GetBoolean());

        var view = (DiffView)_w.Active!.Custom!;
        await view.Session.RefreshAsync();
        Assert.Equal("HEAD / tree", _w.Active.Title);
        Assert.Equal("HEAD", view.Session.Left.Title);
        Assert.Contains(tree.Title, view.Session.Right.Title);
        Assert.Contains(revision.ToString(), view.Session.Right.Title);
        Assert.Equal([new DiffHunk(1, 1, 1, 1)], view.Session.Diff!.Hunks);

        tree.Editor.Document.Replace(0, 1, "X");
        await view.Session.RefreshAsync();
        Assert.Equal([new DiffHunk(1, 1, 1, 1)], view.Session.Diff!.Hunks);
        Assert.Equal(["a"], view.Session.Diff.B.ReadLines(0, 1));
    }

    [AvaloniaFact]
    public async Task Staging_one_hunk_changes_the_index_tab_and_an_index_that_moved_is_a_conflict()
    {
        await StartPluginAsync();
        var tree = _w.NewDocument();
        tree.Editor!.Document.Text = "a\nB\nc\nD\ne\n";
        _app.Plugins.Documents.Expose(tree.Doc.Doc, tree.Editor.ByteSelections);
        _app.Plugins.Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        var index = VirtualTab(await Editor(DocumentMethods.VirtualOpen, OpenParams("index", "file.txt", "a\nb\nc\nd\ne\n", "Index")));
        string Side(DocTab t) => $"{{\"document\":\"{t.Doc.Doc.Id}\",\"revision\":{t.Doc.Doc.Revision}}}";
        long indexRevision = index.Doc.Doc.Revision;

        var diff = await Editor(DocumentMethods.DiffCompute, $"{{\"left\":{Side(index)},\"right\":{Side(tree)}}}");
        Assert.Equal(2, diff.GetProperty("hunkCount").GetInt32());

        // the index moved after the diff was made: nothing is staged
        var oldIndex = Side(index);
        await Editor(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{index.Doc.Doc.Id}\",\"text\":\"a\\nb\\nc\\nd\\ne\\nf\\n\"}}");
        var conflict = await Editor(DocumentMethods.DiffStage, $"{{\"base\":{oldIndex},\"source\":{Side(tree)},\"hunk\":1}}");
        Assert.Equal(DocumentErrorCodes.RevisionConflict, ErrorOf(conflict));
        Assert.Equal(indexRevision + 1, conflict.GetProperty("data").GetProperty("currentRevision").GetInt64());
        Assert.Equal("a\nb\nc\nd\ne\nf\n", index.Editor!.Document.Text);

        var staged = await Editor(DocumentMethods.DiffStage, $"{{\"base\":{Side(index)},\"source\":{Side(tree)},\"hunk\":1}}");
        Assert.Equal(indexRevision + 2, staged.GetProperty("revision").GetInt64());
        Assert.Equal("a\nb\nc\nD\ne\nf\n", index.Editor.Document.Text);
        Assert.False(index.Doc.HasUnsavedWork);
        Assert.Equal("a\nB\nc\nD\ne\n", tree.Editor!.Document.Text);
    }
}
