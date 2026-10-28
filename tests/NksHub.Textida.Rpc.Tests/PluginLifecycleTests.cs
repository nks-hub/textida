using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.PluginHost.Ui;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P3b.1.3: activation events, lazy start, stop, restart and idle stop of the plugin processes, the end of all of them when the editor closes, and the shared error codes.
/// The processes are the real test plugin, so what is checked here is what the operating system sees.
/// </summary>
public class PluginLifecycleTests
{
    const string Header = "\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"Demo\",\"version\":\"1.2.3\",\"api\":{\"min\":1,\"max\":1}";

    static PluginSpec Spec(string id, params string[] args) => SpecOn(id, ["onCommand:" + id], args);

    static PluginSpec SpecOn(string id, string[] events, params string[] args) => new(id, "Plugin " + id, T.DotnetHost, [T.PluginDll, .. args], events);

    static async Task<PluginHost.PluginHost> StartedAsync(PluginHostOptions options, params PluginSpec[] specs)
    {
        var host = new PluginHost.PluginHost(options);
        foreach (var s in specs) host.Register(s);
        foreach (var s in specs) await host.ActivateAsync(s.ActivationEvents[0]);
        return host;
    }

    static byte[] Args(string json) => T.Json(json);

    static async Task<int> Pid(PluginHost.PluginHost host, string id) => (await host.CallAsync(id, "pid")).GetInt32();

    static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Waits until the plugin has at least <paramref name="atLeast"/> requests in progress (the question itself counts as one).</summary>
    static async Task InFlightAsync(PluginHost.PluginHost host, string id, int atLeast)
    {
        var sw = Stopwatch.StartNew();
        while ((await host.CallAsync(id, "stats")).GetProperty("inFlight").GetInt32() < atLeast)
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("The call did not reach the plugin.");
            await Task.Delay(20);
        }
    }

    // ---- activation events ----

    [Theory]
    [InlineData("onStartup", true)]
    [InlineData("onCommand:acme.demo.run", true)]
    [InlineData("onLanguage:json", true)]
    [InlineData("onFileType:.md", true)]
    [InlineData("onPanel:git:status", true)]
    [InlineData("workspaceContains:package.json", true)]
    [InlineData("workspaceContains:**/*.csproj", true)]
    [InlineData("onStartup:now", false)]
    [InlineData("onCommand:", false)]
    [InlineData("workspaceContains:", false)]
    [InlineData("workspaceContains:a b", false)]
    [InlineData("whenever", false)]
    public void ActivationEventsAreCheckedByTheirGrammar(string activationEvent, bool valid) => Assert.Equal(valid, ActivationEvents.IsValid(activationEvent));

    [Fact]
    public void AFileTypeDoesNotDependOnTheCaseOfTheExtensionButACommandDoes()
    {
        Assert.Equal("onFileType:.md", ActivationEvents.FileType("C:/notes/README.MD"));
        Assert.Null(ActivationEvents.FileType("Makefile"));
        Assert.True(ActivationEvents.Same("onFileType:.MD", "onFileType:.md"));
        Assert.False(ActivationEvents.Same("onCommand:Run", "onCommand:run"));
        Assert.True(ActivationEvents.Lists(["onStartup", "onFileType:.TXT"], "onFileType:.txt"));
    }

    [Fact]
    public void AManifestMayListOnStartupAndWorkspaceContainsAndRefusesABrokenOne()
    {
        static PluginLoadResult Load(string events)
        {
            var dir = T.TempDir();
            File.WriteAllText(Path.Combine(dir, "run.cmd"), "");
            File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), "{" + Header + ",\"main\":{\"executable\":\"./run.cmd\",\"activationEvents\":" + events + "}}");
            return PluginManifestLoader.Load(dir, 1, 1 << 20);
        }

        var good = Load("[\"onStartup\",\"workspaceContains:pom.xml\",\"onFileType:.md\",\"onLanguage:json\"]");
        Assert.True(good.Succeeded, string.Join("; ", good.Errors));
        Assert.Equal(["onStartup", "workspaceContains:pom.xml", "onFileType:.md", "onLanguage:json"], good.Plugin!.Spec!.ActivationEvents);

        Assert.Contains(Load("[\"onStartup:now\"]").Errors, e => e.Contains("Activation event"));
        Assert.Contains(Load("[\"workspaceContains:\"]").Errors, e => e.Contains("Activation event"));
    }

    [Fact]
    public void TheWorkspaceSearchHonoursDepthEntriesAndSkippedFolders()
    {
        var dir = T.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "a", "b", "c"));
        File.WriteAllText(Path.Combine(dir, "a", "b", "c", "deep.txt"), "");
        File.WriteAllText(Path.Combine(dir, "a", "top.md"), "");
        Directory.CreateDirectory(Path.Combine(dir, "node_modules"));
        File.WriteAllText(Path.Combine(dir, "node_modules", "skipme.txt"), "");

        string[] globs = ["deep.txt", "skipme.txt", "*.md", "a/b/c/deep.txt", "missing.xyz"];
        Assert.Equal(["*.md", "a/b/c/deep.txt", "deep.txt"], WorkspaceScan.Find([dir], globs, 100, 5, ["node_modules"]).Order(StringComparer.Ordinal));
        Assert.Contains("skipme.txt", WorkspaceScan.Find([dir], globs, 100, 5, []));
        Assert.DoesNotContain("deep.txt", WorkspaceScan.Find([dir], ["deep.txt"], 100, 2, [])); // c is deeper than 2 folders below the root
        Assert.Empty(WorkspaceScan.Find([dir], ["deep.txt"], 2, 5, [])); // two entries are looked at before it
        Assert.Empty(WorkspaceScan.Find([Path.Combine(dir, "missing")], ["deep.txt"], 100, 5, []));
    }

    [Fact]
    public async Task OnStartupAndWorkspaceContainsStartOnlyThePluginsThatListThemAndOnlyWhenTheyFire()
    {
        var folder = T.TempDir();
        File.WriteAllText(Path.Combine(folder, "pom.xml"), "");
        await using var host = new PluginHost.PluginHost(PluginHostTests.Options(o => o with { WorkspaceScan = new WorkspaceScanLimits(1000, 4, [".git"]) }));
        host.Register(SpecOn("boot", [ActivationEvents.Startup]));
        host.Register(SpecOn("maven", ["workspaceContains:pom.xml"]));
        host.Register(SpecOn("node", ["workspaceContains:**/package.json"]));
        host.Register(SpecOn("lang", [ActivationEvents.Language("json")]));

        Assert.Empty(await host.ActivateWorkspaceAsync([]));
        Assert.All(new[] { "boot", "maven", "node", "lang" }, id => Assert.Equal(PluginState.Inactive, host.GetState(id)));

        Assert.Equal(["maven"], await host.ActivateWorkspaceAsync([folder]));
        Assert.Equal(PluginState.Running, host.GetState("maven"));
        Assert.Equal(PluginState.Inactive, host.GetState("node"));

        Assert.Equal(["boot"], await host.ActivateAsync(ActivationEvents.Startup));
        Assert.Equal(["lang"], await host.ActivateAsync(ActivationEvents.Language("json")));
        Assert.Empty(await host.ActivateAsync(ActivationEvents.Language("json"))); // already running: nothing starts twice
        Assert.Equal(PluginState.Inactive, host.GetState("node"));
    }

    [Fact]
    public async Task ASecondActivationWaitsForAPluginThatIsStillStartingInsteadOfPassingItBy()
    {
        await using var host = new PluginHost.PluginHost(PluginHostTests.Options());
        host.Register(SpecOn("late", ["onCommand:late", "onLanguage:json"], "--resync-delay", "4000"));
        var first = host.ActivateAsync("onCommand:late");
        await T.Until(() => host.GetStderrTail("late").Any(l => l.Contains("resync started")), 20_000);
        Assert.Equal(PluginState.Starting, host.GetState("late"));

        var second = host.ActivateAsync("onLanguage:json");
        await Task.Delay(300);
        Assert.False(second.IsCompleted);
        Assert.Equal(["late"], await first);
        Assert.Empty(await second); // it was started for the first event; the second only waited for it
        Assert.Equal(PluginState.Running, host.GetState("late"));
        Assert.Equal(1, (await host.CallAsync("late", "echo", Args("1"))).GetInt32());
    }

    // ---- stop, start, restart ----

    [Fact]
    public async Task AStoppedPluginEndsItsProcessWithoutACrashAndStartsAgainAtTheNextEvent()
    {
        var crashed = new List<PluginCrashInfo>();
        var states = new List<PluginState>();
        await using var host = new PluginHost.PluginHost(PluginHostTests.Options());
        host.Crashed += i => { lock (crashed) crashed.Add(i); };
        host.StateChanged += (_, s) => { lock (states) states.Add(s); };
        host.Register(Spec("p"));
        await host.ActivateAsync("onCommand:p");
        var first = await Pid(host, "p");
        Assert.Equal([first], host.ProcessIds());

        await host.StopAsync("p");

        Assert.Equal(PluginState.Inactive, host.GetState("p"));
        Assert.Empty(host.ProcessIds());
        await T.Until(() => !Alive(first));
        await Assert.ThrowsAsync<PluginUnavailableException>(() => host.CallAsync("p", "pid"));
        lock (crashed) Assert.Empty(crashed);
        Assert.Equal(0, host.GetStatus("p").Crashes);
        lock (states) Assert.Equal([PluginState.Starting, PluginState.Running, PluginState.Stopping, PluginState.Inactive], states);

        Assert.Equal(["p"], await host.ActivateAsync("onCommand:p"));
        Assert.NotEqual(first, await Pid(host, "p"));
        Assert.False((await host.CallAsync("p", "getResync")).GetProperty("restarted").GetBoolean()); // a new start, not the restart after a crash
    }

    [Fact]
    public async Task ACallThatIsRunningWhenThePluginIsStoppedEndsAsUnavailableNotAsACrash()
    {
        var file = T.TempFile();
        var crashed = new List<PluginCrashInfo>();
        await using var host = await StartedAsync(PluginHostTests.Options(), Spec("p"));
        host.Crashed += i => { lock (crashed) crashed.Add(i); };
        var call = host.CallAsync("p", "slow", Args("{\"ms\":60000,\"file\":" + JsonSerializer.Serialize(file) + "}"));
        await InFlightAsync(host, "p", 2);

        await host.StopAsync("p");

        var e = await Assert.ThrowsAsync<PluginUnavailableException>(() => call);
        Assert.Equal(PluginState.Stopping, e.State);
        lock (crashed) Assert.Empty(crashed);
        Assert.Equal(PluginState.Inactive, host.GetState("p"));
    }

    [Fact]
    public async Task AStopWhileThePluginWaitsForItsRestartCancelsTheRestart()
    {
        await using var host = await StartedAsync(PluginHostTests.Options(o => o with { Restart = o.Restart with { BaseDelay = TimeSpan.FromSeconds(2) } }), Spec("p"));
        await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("p", "crash", Args("{\"code\":3}")));
        await T.Until(() => host.GetState("p") == PluginState.WaitingRestart);

        await host.StopAsync("p");

        Assert.Equal(PluginState.Inactive, host.GetState("p"));
        await Task.Delay(2500); // longer than the wait the restart had
        Assert.Equal(PluginState.Inactive, host.GetState("p"));
        Assert.Empty(host.ProcessIds());
        Assert.Equal(1, host.GetStatus("p").Crashes); // the crash stays on record; the stop is not one
    }

    [Fact]
    public async Task StartGivesATurnedOffPluginACleanSlateAndRestartGivesANewProcess()
    {
        await using var host = await StartedAsync(PluginHostTests.Options(o => o with { Restart = o.Restart with { BaseDelay = TimeSpan.FromMilliseconds(50) } }), Spec("flaky"));
        for (var i = 0; i < 3; i++)
        {
            await T.Until(() => host.GetState("flaky") == PluginState.Running);
            await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("flaky", "crash", Args("{\"code\":1}")));
        }
        await T.Until(() => host.GetState("flaky") == PluginState.Disabled);
        var off = host.GetStatus("flaky");
        Assert.NotNull(off.DisabledReason);
        Assert.NotNull(off.LastCrash);
        Assert.Null(off.ProcessId);

        await host.StartAsync("flaky");

        var on = host.GetStatus("flaky");
        Assert.Equal(PluginState.Running, on.State);
        Assert.Null(on.DisabledReason);
        Assert.Equal(0, on.Crashes);
        Assert.NotNull(on.ProcessId);

        var before = await Pid(host, "flaky");
        await host.RestartAsync("flaky");
        Assert.Equal(PluginState.Running, host.GetState("flaky"));
        Assert.NotEqual(before, await Pid(host, "flaky"));
        await T.Until(() => !Alive(before));
    }

    [Fact]
    public async Task TheStatusNamesTheLastCrashAndTheProcessWhileOneRuns()
    {
        await using var host = await StartedAsync(PluginHostTests.Options(o => o with { Restart = o.Restart with { BaseDelay = TimeSpan.FromSeconds(3) } }), Spec("p"));
        Assert.Equal(await Pid(host, "p"), host.GetStatus("p").ProcessId);
        Assert.False(host.GetStatus("p").IdleStop);

        await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("p", "crash", Args("{\"code\":7}")));
        await T.Until(() => host.GetStatus("p").LastCrash is not null);
        var status = host.GetStatus("p");
        Assert.Equal(PluginEndReason.Exited, status.LastCrash!.Reason);
        Assert.Equal(7, status.LastCrash.ExitCode);
        Assert.Equal(1, status.Crashes);
        Assert.Equal(PluginState.WaitingRestart, status.State);
        Assert.Null(status.ProcessId);
        Assert.Equal(["p"], host.GetStatuses().Select(s => s.Id));
    }

    // ---- idle stop ----

    [Fact]
    public async Task APluginThatAskedForIdleStopEndsAfterTheQuietTimeAndStartsAgainAtTheNextEvent()
    {
        var crashed = new List<PluginCrashInfo>();
        await using var host = new PluginHost.PluginHost(PluginHostTests.Options(o => o with { IdleStopAfter = TimeSpan.FromMilliseconds(700) }));
        host.Crashed += i => { lock (crashed) crashed.Add(i); };
        host.Register(Spec("idle", "--idle-stop"));
        await host.ActivateAsync("onCommand:idle");
        var pid = await Pid(host, "idle");
        Assert.True(host.GetStatus("idle").IdleStop);

        await T.Until(() => host.GetState("idle") == PluginState.Inactive, 30_000);

        await T.Until(() => !Alive(pid));
        lock (crashed) Assert.Empty(crashed);
        Assert.Equal(["idle"], await host.ActivateAsync("onCommand:idle"));
        Assert.NotEqual(pid, await Pid(host, "idle"));
    }

    [Fact]
    public async Task AnIdleStopSparesAPluginThatIsBusyHoldsStateInTheEditorOrDidNotAsk()
    {
        var holds = new bool[1];
        await using var host = new PluginHost.PluginHost(PluginHostTests.Options(o => o with { IdleStopAfter = TimeSpan.FromMilliseconds(500), HoldsEditorState = _ => Volatile.Read(ref holds[0]) }));
        host.Register(Spec("busy", "--idle-stop"));
        host.Register(Spec("plain"));
        await host.ActivateAsync("onCommand:busy");
        await host.ActivateAsync("onCommand:plain");

        // a call that lasts longer than the quiet time is not cut off, and the quiet time starts when it ends
        Assert.Equal("done", (await host.CallAsync("busy", "slow", Args("{\"ms\":2000,\"file\":" + JsonSerializer.Serialize(T.TempFile()) + "}"))).GetString());
        Assert.Equal(PluginState.Running, host.GetState("busy"));

        Volatile.Write(ref holds[0], true);
        await Task.Delay(1800);
        Assert.Equal(PluginState.Running, host.GetState("busy"));
        Assert.Equal(PluginState.Running, host.GetState("plain"));

        Volatile.Write(ref holds[0], false);
        await T.Until(() => host.GetState("busy") == PluginState.Inactive, 30_000);
        Assert.Equal(PluginState.Running, host.GetState("plain")); // it never asked for an idle stop
    }

    // ---- the end of all of them ----

    [Fact]
    public async Task AShutdownThatTakesTooLongKillsWhatIsLeftByProcessId()
    {
        var host = new PluginHost.PluginHost(PluginHostTests.Options(o => o with { ShutdownGrace = TimeSpan.FromSeconds(30), ShutdownTotal = TimeSpan.FromMilliseconds(800) }));
        host.Register(Spec("stubborn", "--linger"));
        host.Register(Spec("polite"));
        await host.ActivateAsync("onCommand:stubborn");
        await host.ActivateAsync("onCommand:polite");
        int[] pids = [await Pid(host, "stubborn"), await Pid(host, "polite")];
        Assert.Equal(pids.Order(), host.ProcessIds().Order());

        var sw = Stopwatch.StartNew();
        await host.ShutdownAsync();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"The shutdown took {sw.Elapsed}; the grace time alone is 30 s.");
        await T.Until(() => pids.All(p => !Alive(p)));
        Assert.Empty(host.ProcessIds());
        Assert.Equal(PluginState.Stopped, host.GetState("stubborn"));
    }

    [Fact]
    public async Task KillAllEndsEveryPluginProcessByItsId()
    {
        await using var host = await StartedAsync(PluginHostTests.Options(), Spec("a"), Spec("b"));
        int[] pids = [.. host.ProcessIds()];
        Assert.Equal(2, pids.Length);

        Assert.Equal(2, host.KillAll());

        await T.Until(() => pids.All(p => !Alive(p)));
    }

    // ---- the shared layer ----

    [Fact]
    public async Task AHandlerThatTimedOutIsAnsweredWithTheTimeoutCodeInEveryProtocol()
    {
        foreach (var protocol in new[] { RpcProtocols.Plugin, RpcProtocols.Lsp, RpcProtocols.Mcp, RpcProtocols.Acp })
        {
            var handler = new DelegateHandler((m, p, ct) => throw new RpcTimeoutException("downstream", TimeSpan.FromSeconds(1)));
            await using var pair = Pair.Create(protocol, T.Roomy, new DelegateHandler(), T.Roomy, handler);
            var e = await Assert.ThrowsAsync<RpcException>(() => pair.A.RequestAsync("m"));
            Assert.Equal(RpcErrorCodes.RequestTimeout, e.Code);
            Assert.Contains("downstream", e.Message);
        }
    }

    [Fact]
    public void NoErrorCodeIsUsedTwiceInThePluginProtocol()
    {
        var codes = new[] { typeof(RpcErrorCodes), typeof(DocumentErrorCodes), typeof(UiErrorCodes) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(int))
                .Select(f => (Name: t.Name + "." + f.Name, Value: (int)f.GetRawConstantValue()!)))
            .ToList();
        var clashes = codes.GroupBy(c => c.Value).Where(g => g.Count() > 1).Select(g => g.Key + ": " + string.Join(", ", g.Select(c => c.Name))).ToList();
        Assert.True(clashes.Count == 0, "error codes used twice: " + string.Join("; ", clashes));
        Assert.Contains(codes, c => c.Name == "UiErrorCodes.QuotaExceeded");
    }

    [Fact]
    public void TheHandshakeCarriesTheRequestForAnIdleStop()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new InitializeResult(1, "x", EventAcks: true, IdleStop: true), PluginJson.Default.InitializeResult);
        Assert.Contains("\"idleStop\":true", System.Text.Encoding.UTF8.GetString(json));
        Assert.True(JsonSerializer.Deserialize(json, PluginJson.Default.InitializeResult)!.IdleStop);
        Assert.False(JsonSerializer.Deserialize("{\"apiVersion\":1,\"name\":\"x\"}"u8, PluginJson.Default.InitializeResult)!.IdleStop);
    }
}
