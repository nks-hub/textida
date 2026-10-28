using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Startup;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>The measurement mode of the large-file path writes the private commit and the working set (P3.1.9); this runs it on a small file and reads its log.</summary>
[Collection("LocState")]
public class PagedMeasureTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly Dictionary<string, string?> _env = [];

    public PagedMeasureTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _env) Environment.SetEnvironmentVariable(name, value);
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private void SetEnv(string name, string value)
    {
        _env.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    [AvaloniaFact]
    public async Task A_run_on_a_file_logs_private_commit_and_working_set_with_the_state_of_the_index_and_the_mapped_cache()
    {
        string file = _temp.Bytes("measured.log", Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(1, 300_000).Select(i => $"line {i}\n"))));
        string log = Path.Combine(_temp.Path, "measure.jsonl");
        SetEnv("TEXTIDA_MEASURE", log);
        SetEnv("TEXTIDA_MEASURE_FILE", file);
        SetEnv("TEXTIDA_MEASURE_QUICK", "1");
        SetEnv("TEXTIDA_MEASURE_BACKEND", "paged");
        var lifetime = new ClassicDesktopStyleApplicationLifetime();
        bool shutDown = false;
        int exitCode = -1;
        lifetime.Exit += (_, e) => { shutDown = true; exitCode = e.ApplicationExitCode; };

        var run = new PagedMeasure(_app, _w, lifetime).RunAsync();
        for (var clock = System.Diagnostics.Stopwatch.StartNew(); !shutDown && clock.Elapsed.TotalSeconds < 90;)
        {
            // pump by hand: once the run shuts the application down, a continuation of an await would never be dispatched again
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs();
            _w.CaptureRenderedFrame();
        }
        await run;

        Assert.True(shutDown, "the run did not end");
        Assert.Equal(0, exitCode);
        var records = File.ReadAllLines(log).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
        Assert.DoesNotContain(records, r => (string?)r["t"] == "error");
        var memory = records.Where(r => (string?)r["t"] == "mem").ToList();
        string[] moments = [.. memory.Select(r => (string)r["moment"]!).Distinct()];
        foreach (var moment in new[] { "baseline", "opened", "opened+indexed", "jumped", "end" }) Assert.Contains(moment, moments);
        Assert.All(memory, r =>
        {
            Assert.True((double)r["privateMiB"]! > 0, "private commit missing in " + r);
            Assert.True((double)r["workingSetMiB"]! > 0, "working set missing in " + r);
            Assert.NotNull(r["mappedMiB"]);
        });
        Assert.Equal("complete", (string?)memory.First(r => (string?)r["moment"] == "opened+indexed")["indexing"]);
    }
}
