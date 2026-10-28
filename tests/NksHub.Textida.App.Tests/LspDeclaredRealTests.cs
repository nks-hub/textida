using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P8.5.2 with a real language server (category RealTools, skipped where gopls or go is not in PATH): a data plugin, installed through the plugin manager with the consent of the user, declares gopls (a copy of it under
/// a name of its own, in PATH, so that only the processes of this test are ever counted or killed). The editor starts that server only after the consent, and when the server is killed the language server plugin starts
/// it again and it answers the next definition request.
/// </summary>
[Collection("LocState")]
[Trait("Category", "RealTools")]
public class LspDeclaredRealTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string? _path = Environment.GetEnvironmentVariable("PATH");
    private string? _copy;

    public LspDeclaredRealTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Environment.SetEnvironmentVariable("PATH", _path);
        if (_copy is not null)
            foreach (var p in Running()) // anything left of this test's own copy
                try { p.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private Process[] Running() => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_copy!));

    private static async Task Until(Func<bool> condition, string what, int seconds = 60)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed.TotalSeconds < seconds, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private static async Task<T> Pump<T>(Task<T> task)
    {
        await Until(() => task.IsCompleted, "a command of the language server", 120);
        return await task;
    }

    [AvaloniaFact]
    public async Task A_data_plugin_with_a_real_language_server_starts_it_after_the_consent_and_the_server_is_started_again_after_it_is_killed()
    {
        Assert.SkipWhen(ToolLocator.Find("gopls") is null || ToolLocator.Find("go") is null, "gopls and go are not installed here (not found in PATH).");
        string source = ToolLocator.Find("gopls")!;
        string name = "textida-gopls-" + Guid.NewGuid().ToString("N")[..8];
        string bin = Directory.CreateDirectory(Path.Combine(_temp.Path, "bin")).FullName;
        _copy = Path.Combine(bin, name + Path.GetExtension(source));
        File.Copy(source, _copy);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + _path); // the plugin names the server by its bare name, the editor looks it up in PATH

        string zip = Path.Combine(_temp.Path, "acme-lang.zip");
        using (var archive = new ZipArchive(File.Create(zip), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry(PluginManifest.FileName).Open()))
            writer.Write("{\"schemaVersion\":1,\"id\":\"acme.lang\",\"name\":\"Acme Go\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"permissions\":[\"language.server\"],"
                + "\"contributes\":{\"languageServers\":[{\"id\":\"gox\",\"languages\":[\"go\"],\"command\":\"" + name + "\"}]}}");

        string project = Directory.CreateDirectory(Path.Combine(_temp.Path, "gomod")).FullName;
        File.WriteAllText(Path.Combine(project, "go.mod"), "module x\n\ngo 1.21\n");
        const string text = "package main\n\nfunc helper() {}\n\nfunc main() {\n\thelper()\n}\n";
        string file = Path.Combine(project, "main.go");
        File.WriteAllText(file, text);
        var tab = _w.OpenFile(file, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("go", tab.Doc.Language.Id);

        // the user refuses: the plugin is not installed and its server is nobody's business
        _dialogs.Answers.Enqueue(1);
        var refused = _w.InstallPluginAsync(zip);
        await Pump(refused);
        Assert.False(refused.Result);
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains(asked.Details!, d => d.StartsWith(DocumentPermissions.LanguageServer, StringComparison.Ordinal));
        Assert.Empty(_app.Plugins.Manager.Installed());
        Assert.Null(_app.Plugins.DeclaredServerFor("go", file));

        // the user agrees: the editor takes the server of the plugin for go files
        _dialogs.Answers.Enqueue(0);
        var installed = _w.InstallPluginAsync(zip);
        await Pump(installed);
        Assert.True(installed.Result);
        Assert.True(_app.Plugins.Permissions.IsGranted("acme.lang", DocumentPermissions.LanguageServer, GrantScope.All));
        Assert.NotNull(_app.Plugins.DeclaredServerFor("go", file));

        // a file shown in pages never reaches a language server, the declared one included
        _app.Workbench.Set("files.largeFileThreshold", System.Text.Json.Nodes.JsonValue.Create(1));
        var paged = _w.OpenFile(_temp.Bytes("big.go", Encoding.UTF8.GetBytes("package main\n" + string.Concat(Enumerable.Repeat("// a line of comment\n", (3 << 20) / 21)))), null);
        Assert.True(paged.Doc.IsLarge);
        Assert.False(await Pump(_app.Plugins.RunLspAsync(_w, paged, "lsp.start")));
        await Task.Delay(500);
        Assert.Empty(Running());

        Assert.True(await Pump(_app.Plugins.RunLspAsync(_w, tab, "lsp.start")));
        await Until(() => Running().Length >= 1, "the server of the plugin");
        var firstProcesses = Running().Select(p => p.Id).ToHashSet();

        foreach (var p in Running()) p.Kill(entireProcessTree: true); // the server dies under the plugin process
        await Until(() => Running().Any(p => !firstProcesses.Contains(p.Id)), "the server started again by the plugin process", 90);

        tab.Editor!.CaretOffset = text.LastIndexOf("helper", StringComparison.Ordinal) + 2;
        Assert.True(await Pump(_app.Plugins.RunLspAsync(_w, tab, "lsp.definition")));
        await Until(() => { var range = tab.Editor!.ByteSelections()!.PrimarySelection.ToRange(); return range.End > range.Start; }, "the definition selected in the editor");
        var selected = tab.Editor!.ByteSelections()!.PrimarySelection.ToRange();
        Assert.Equal("helper", Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(selected)));
        Assert.Equal((long)text.IndexOf("helper", StringComparison.Ordinal), selected.Start);
    }
}
