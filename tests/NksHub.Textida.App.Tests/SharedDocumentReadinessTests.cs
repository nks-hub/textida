using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class SharedDocumentReadinessTests : IDisposable
{
    private const int FixturePaddingChars = 2 << 20;
    private readonly TempDir temp = new();
    private readonly string? previousRoot = Environment.GetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE");
    private readonly string directory;
    private readonly App app;
    private readonly MainWindow window;

    public SharedDocumentReadinessTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The opt-in native readiness transport is Windows only.");
        directory = Path.Combine(temp.Path, "readiness");
        Environment.SetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE", directory);
        app = TestApp.Prepare(temp);
        app.History.Policy = new Core.History.HistoryPolicy { Enabled = false };
        app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        window = app.CreateWindow(null);
    }

    [AvaloniaFact]
    public async Task Ready_snapshot_before_structure_does_not_start_syntax_or_column_work()
    {
        var tab = await OpenReadyDocument();
        var view = tab.Paged!;
        var backend = Assert.IsType<PagedBackend>(tab.Doc.Doc.Backend);
        Assert.False(backend.Paged.Source.TryGetLineColumns(0, out _));
        int runs = view.Syntax.ViewportRuns;
        for (int i = 0; i < 3; i++)
        {
            var snapshot = window.CaptureDocumentReadiness(Request(tab));
            Assert.True(snapshot["ready"]!.GetValue<bool>());
            Assert.Equal(1, snapshot["view"]!["longRows"]!.GetValue<int>());
            Assert.Equal(0, snapshot["view"]!["requiredColumnIndexes"]!.GetValue<int>());
            Assert.Equal(0, snapshot["view"]!["columnWork"]!.GetValue<int>());
            Assert.Equal(tab.Doc.Doc.Id.ToString(), snapshot["documentId"]!.GetValue<string>());
            Assert.True(snapshot["captureBeginQpc"]!.GetValue<long>() >= snapshot["requestedQpc"]!.GetValue<long>());
            Assert.True(snapshot["captureEndQpc"]!.GetValue<long>() >= snapshot["captureBeginQpc"]!.GetValue<long>());
        }
        Assert.Equal(runs, view.Syntax.ViewportRuns);
        Assert.False(backend.Paged.Source.TryGetLineColumns(0, out _));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TreeView>(), tree => tree.Name == "StructureTree");
    }

    [AvaloniaFact]
    public async Task Horizontal_scroll_requires_the_exact_rendered_window_without_reading_it_in_the_snapshot()
    {
        var tab = await OpenReadyDocument();
        var view = tab.Paged!;
        foreach (double column in new[] { 20000.0, 22000.0 })
        {
            view.ScrollToColumn(column);
            Assert.False(view.WindowsPending);
            var before = window.CaptureDocumentReadiness(Request(tab));
            Assert.False(before["ready"]!.GetValue<bool>());
            Assert.False(before["view"]!["currentCache"]!.GetValue<bool>());
            Assert.Equal(1, before["view"]!["windowRows"]!.GetValue<int>());
            Assert.Equal(0, before["view"]!["cachedWindows"]!.GetValue<int>());
            Assert.False(view.WindowsPending);
            await Until(() =>
            {
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                return window.CaptureDocumentReadiness(Request(tab))["ready"]!.GetValue<bool>();
            });
            var after = window.CaptureDocumentReadiness(Request(tab));
            Assert.Equal(1, after["view"]!["cachedWindows"]!.GetValue<int>());
            Assert.Equal(1, after["view"]!["completeColumnIndexes"]!.GetValue<int>());
        }
    }

    [AvaloniaFact]
    public void Default_window_has_no_readiness_worker()
    {
        Environment.SetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE", null);
        try
        {
            var ordinary = app.CreateWindow(null);
            Assert.True(ordinary.DocumentReadinessCompletion.IsCompletedSuccessfully);
            using var process = Process.GetCurrentProcess();
            var snapshot = ordinary.CaptureDocumentReadiness(new("disabled", Stopwatch.GetTimestamp(), Environment.ProcessId, process.SessionId));
            Assert.Equal("disabled", snapshot["status"]!.GetValue<string>());
            Assert.False(snapshot["ready"]!.GetValue<bool>());
            ordinary.Close();
        }
        finally { Environment.SetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE", directory); }
    }

    [AvaloniaFact]
    public async Task Incomplete_source_index_stays_not_ready_without_starting_more_work()
    {
        string path = temp.Bytes("loading.json", Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', FixturePaddingChars) + "\"}"));
        Assert.True(new FileInfo(path).Length > (1L << 20));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var previousHook = MappedFileSource.IndexBlockHook;
        MappedFileSource.IndexBlockHook = (source, _) =>
        {
            if (source.Path != path) return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The source index test was not released.");
        };
        try
        {
            var tab = window.OpenFile(path, null);
            Assert.NotNull(tab.Paged);
            Assert.IsType<PagedBackend>(tab.Doc.Doc.Backend);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            int runs = tab.Paged!.Syntax.ViewportRuns;
            var snapshot = window.CaptureDocumentReadiness(Request(tab));
            Assert.False(snapshot["indexTaskComplete"]!.GetValue<bool>());
            Assert.False(snapshot["ready"]!.GetValue<bool>());
            Assert.Equal(runs, tab.Paged.Syntax.ViewportRuns);
            release.Set();
            await Until(() =>
            {
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                return window.CaptureDocumentReadiness(Request(tab))["ready"]!.GetValue<bool>();
            });
        }
        finally { release.Set(); MappedFileSource.IndexBlockHook = previousHook; }
    }

    [AvaloniaFact]
    public void Syntax_without_a_completed_request_is_not_ready_even_when_pending_is_false()
    {
        using var document = OpenDocument.FromSource(Encoding.UTF8.GetBytes("{\"n\":1}"), "sample.json");
        int applied = 0;
        using var feeder = new SyntaxFeeder(document, _ => applied++, () => { }, _ => null, () => 1, () => { });
        int runs = feeder.ViewportRuns;
        var before = feeder.CaptureReadiness(document.Doc);
        Assert.False(before.Pending);
        Assert.False(before.Ready(document.Doc.Revision));
        Assert.Equal(runs, feeder.ViewportRuns);
        feeder.SetVisible(0, 0, 0, 80);
        Assert.Equal(1, applied);
        Assert.True(feeder.CaptureReadiness(document.Doc).Ready(document.Doc.Revision));
        feeder.Dispose();
        Assert.False(feeder.CaptureReadiness(document.Doc).Ready(document.Doc.Revision));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Published_column_eof_waits_for_the_worker_and_its_ui_callback(bool holdWorkerAfterCallback)
    {
        var tab = await OpenReadyDocument();
        var view = tab.Paged!;
        var source = Assert.IsType<PagedBackend>(tab.Doc.Doc.Backend).Paged.Source;
        using var readComplete = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim(!holdWorkerAfterCallback);
        var work = new PagedTextView.ColumnIndexWork(view);
        Task worker = Task.Run(() =>
        {
            source.ReadColumns(0, long.MaxValue, 0);
            work.PostCallback();
            readComplete.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The column test worker was not released.");
        });
        work.Observe(worker);
        try
        {
            Assert.True(readComplete.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(source.TryGetLineColumns(0, out _));
            if (holdWorkerAfterCallback)
            {
                Dispatcher.UIThread.RunJobs();
                Assert.False(worker.IsCompleted);
            }
            else Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));
            var snapshot = window.CaptureDocumentReadiness(Request(tab));
            Assert.Equal(1, snapshot["view"]!["columnWork"]!.GetValue<int>());
            Assert.False(snapshot["ready"]!.GetValue<bool>());
            release.Set();
            await worker;
            await Until(() => window.CaptureDocumentReadiness(Request(tab))["ready"]!.GetValue<bool>());
        }
        finally { release.Set(); await worker; }
    }

    [AvaloniaFact]
    public async Task Request_transport_rejects_stale_identities_and_stops_with_the_window()
    {
        var tab = await OpenReadyDocument();
        var ready = await Send(Request(tab));
        Assert.True(ready["ready"]!.GetValue<bool>());
        var wrongProcess = await Send(Request(tab) with { Pid = Environment.ProcessId + 1 });
        Assert.Equal("stale-request", wrongProcess["status"]!.GetValue<string>());
        Assert.False(wrongProcess["ready"]!.GetValue<bool>());
        var wrongRevision = await Send(Request(tab) with { Revision = tab.Doc.Doc.Revision + 1 });
        Assert.Equal("stale-document", wrongRevision["status"]!.GetValue<string>());
        Assert.False(wrongRevision["ready"]!.GetValue<bool>());
        var oldRequest = await Send(Request(tab) with { RequestedQpc = 1 });
        Assert.Equal("stale-request", oldRequest["status"]!.GetValue<string>());
        Assert.False(oldRequest["ready"]!.GetValue<bool>());
        window.Close();
        await window.DocumentReadinessCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        string final = File.ReadAllText(Path.Combine(directory, "shared-document.json"));
        Write(Request(tab));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(final, File.ReadAllText(Path.Combine(directory, "shared-document.json")));
    }

    private async Task<DocTab> OpenReadyDocument()
    {
        string path = temp.Bytes("sample.json", Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', FixturePaddingChars) + "\",\"n\":1}"));
        Assert.True(new FileInfo(path).Length > (1L << 20));
        var tab = window.OpenFile(path, null);
        Assert.NotNull(tab.Paged);
        Assert.IsType<PagedBackend>(tab.Doc.Doc.Backend);
        await Until(() =>
        {
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            return window.CaptureDocumentReadiness(Request(tab))["ready"]!.GetValue<bool>();
        });
        return tab;
    }

    private static MainWindow.DocumentReadinessRequest Request(DocTab tab)
    {
        using var process = Process.GetCurrentProcess();
        return new(Guid.NewGuid().ToString("N"), Stopwatch.GetTimestamp(), Environment.ProcessId, process.SessionId,
            DocumentId: tab.Doc.Doc.Id, Revision: tab.Doc.Doc.Revision);
    }

    private void Write(MainWindow.DocumentReadinessRequest request)
    {
        var json = new JsonObject { ["nonce"] = request.Nonce, ["requestedQpc"] = request.RequestedQpc,
            ["pid"] = request.Pid, ["session"] = request.Session, ["documentId"] = request.DocumentId?.ToString(),
            ["revision"] = request.Revision };
        string path = Path.Combine(directory, "shared-document-request.json"), temporary = path + ".tmp";
        File.WriteAllText(temporary, json.ToJsonString());
        File.Move(temporary, path, true);
    }

    private async Task<JsonObject> Send(MainWindow.DocumentReadinessRequest request)
    {
        Write(request);
        JsonObject? response = null;
        await Until(() =>
        {
            string path = Path.Combine(directory, "shared-document.json");
            if (!File.Exists(path)) return false;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            response = JsonNode.Parse(file)!.AsObject();
            return response["nonce"]?.GetValue<string>() == request.Nonce;
        });
        return response!;
    }

    private static async Task Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.Fail("The shared document did not reach the expected state.");
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Environment.SetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE", previousRoot);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temp.Dispose();
    }
}
