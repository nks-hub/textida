using System.Diagnostics;
using System.Text.Json;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginSdk;

namespace NksHub.Textida.Rpc.Tests;

public class PluginHostTests
{
    internal static PluginHostOptions Options(Func<PluginHostOptions, PluginHostOptions>? change = null)
    {
        var o = new PluginHostOptions
        {
            Limits = new RpcLimits(1 << 20, 2, 16, 1000, 1 << 20, 1 << 20),
            Restart = new RestartPolicy(3, TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5)),
            ApiVersion = 1,
            HandshakeTimeout = TimeSpan.FromSeconds(30),
            PingInterval = TimeSpan.FromSeconds(2),
            PingTimeout = TimeSpan.FromSeconds(10),
            MissedPingsBeforeKill = 3,
            ShutdownGrace = TimeSpan.FromSeconds(5),
            StderrTailLines = 10,
            DefaultCallTimeout = TimeSpan.FromSeconds(90), // a stuck call fails the test with its name instead of hanging the run
        };
        return change?.Invoke(o) ?? o;
    }

    /// <summary>Quick pings for the tests of a frozen plugin only; a loaded machine can stall a healthy plugin process for longer than these allow.</summary>
    static PluginHostOptions FastPings(PluginHostOptions o) => o with { PingInterval = TimeSpan.FromMilliseconds(150), PingTimeout = TimeSpan.FromMilliseconds(500) };

    static PluginSpec Spec(string id, params string[] args) =>
        new(id, "Plugin " + id, T.DotnetHost, [T.PluginDll, .. args], ["onCommand:" + id]);

    static async Task<PluginHost.PluginHost> Started(PluginHostOptions options, PluginApiCallback? api = null, IPluginStateSource? state = null, params PluginSpec[] specs)
    {
        var host = new PluginHost.PluginHost(options, api, state);
        foreach (var s in specs) host.Register(s);
        foreach (var s in specs) await host.ActivateAsync(s.ActivationEvents[0]);
        return host;
    }

    static byte[] Args(string json) => T.Json(json);

    static async Task<int> Pid(PluginHost.PluginHost h, string id) => (await h.CallAsync(id, "pid")).GetInt32();

    static bool Alive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return false; }
    }

    [Fact]
    public async Task APluginStartsOnlyAtItsFirstActivationEvent()
    {
        await using var host = new PluginHost.PluginHost(Options());
        host.Register(Spec("a"));
        Assert.Equal(PluginState.Inactive, host.GetState("a"));
        await Assert.ThrowsAsync<PluginUnavailableException>(() => host.CallAsync("a", "pid"));
        Assert.Empty(await host.ActivateAsync("onCommand:other"));
        Assert.Equal(PluginState.Inactive, host.GetState("a"));
        Assert.Equal(["a"], await host.ActivateAsync("onCommand:a"));
        Assert.Equal(PluginState.Running, host.GetState("a"));
        Assert.Equal("{\"x\":1}", (await host.CallAsync("a", "echo", Args("{\"x\":1}"))).GetRawText());
    }

    [Fact]
    public async Task ADifferentApiVersionIsRefusedWithAReadableMessage()
    {
        await using var host = new PluginHost.PluginHost(Options());
        host.Register(Spec("old", "--api", "2"));
        var e = await Assert.ThrowsAsync<PluginHandshakeException>(() => host.ActivateAsync("onCommand:old"));
        Assert.Contains("Plugin old", e.Message);
        Assert.Contains("version 2", e.Message);
        Assert.Contains("version 1", e.Message);
        Assert.Equal(PluginState.Disabled, host.GetState("old"));
    }

    [Fact]
    public async Task ACrashNamesThePluginAndOtherPluginsKeepRunning()
    {
        var crashed = new List<PluginCrashInfo>();
        await using var host = await Started(Options(), null, null, Spec("bad"), Spec("good"));
        host.Crashed += i => { lock (crashed) crashed.Add(i); };
        var e = await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("bad", "crash", Args("{\"code\":7}")));
        Assert.Equal("bad", e.Info.PluginId);
        Assert.Equal(7, e.Info.ExitCode);
        Assert.Contains("Plugin bad", e.Message);
        Assert.Contains(e.Info.StderrTail, l => l.Contains("asked to crash"));
        Assert.Equal("{\"y\":2}", (await host.CallAsync("good", "echo", Args("{\"y\":2}"))).GetRawText());
        await T.Until(() => { lock (crashed) return crashed.Count == 1; });
        Assert.Equal("bad", crashed[0].PluginId);
    }

    [Fact]
    public async Task RestartsBackOffAndThePluginIsDisabledAfterRepeatedCrashes()
    {
        var crashed = new List<PluginCrashInfo>();
        await using var host = await Started(Options(), null, null, Spec("flaky"));
        host.Crashed += i => { lock (crashed) crashed.Add(i); };
        var firstPid = await Pid(host, "flaky");

        await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("flaky", "crash", Args("{\"code\":1}")));
        await T.Until(() => host.GetState("flaky") == PluginState.Running);
        Assert.NotEqual(firstPid, await Pid(host, "flaky"));

        await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("flaky", "crash", Args("{\"code\":1}")));
        await T.Until(() => host.GetState("flaky") == PluginState.Running);
        await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("flaky", "crash", Args("{\"code\":1}")));
        await T.Until(() => host.GetState("flaky") == PluginState.Disabled);

        lock (crashed)
        {
            Assert.Equal([TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(600), null], crashed.Select(c => c.NextRestartIn));
        }
        await Assert.ThrowsAsync<PluginUnavailableException>(() => host.CallAsync("flaky", "pid"));
    }

    [Fact]
    public async Task APluginThatCrashesOnStartIsReportedAndEventuallyDisabled()
    {
        await using var host = new PluginHost.PluginHost(Options(o => o with { Restart = o.Restart with { BaseDelay = TimeSpan.FromMilliseconds(50) } }));
        host.Register(Spec("dead", "--crash-on-start", "9"));
        var e = await Assert.ThrowsAsync<PluginCrashedException>(() => host.ActivateAsync("onCommand:dead"));
        Assert.Equal("dead", e.Info.PluginId);
        Assert.Equal(9, e.Info.ExitCode);
        Assert.Contains(e.Info.StderrTail, l => l.Contains("crashing on start"));
        await T.Until(() => host.GetState("dead") == PluginState.Disabled);
    }

    [Fact]
    public async Task AfterARestartTheHandshakeRepeatsAndLostEventsAndStateAreReported()
    {
        var state = new FixedState("{\"documents\":[{\"id\":1,\"revision\":5}],\"proposals\":[]}");
        await using var host = await Started(Options(o => o with { Restart = o.Restart with { BaseDelay = TimeSpan.FromSeconds(2) } }), null, state, Spec("p"));
        var first = (await host.CallAsync("p", "getResync")).Clone();
        Assert.False(first.GetProperty("restarted").GetBoolean());
        Assert.Equal(0, first.GetProperty("lostEvents").GetInt32());

        await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("p", "crash", Args("{\"code\":3}")));
        await T.Until(() => host.GetState("p") == PluginState.WaitingRestart);
        await host.PublishEventAsync("documentChanged", Args("{\"id\":1}"));
        await host.PublishEventAsync("documentChanged", Args("{\"id\":1}"));
        Assert.Equal(2, host.GetLostEvents("p"));

        await T.Until(() => host.GetState("p") == PluginState.Running);
        var again = await host.CallAsync("p", "getResync");
        Assert.True(again.GetProperty("restarted").GetBoolean());
        Assert.Equal(2, again.GetProperty("lostEvents").GetInt32());
        Assert.Equal(5, again.GetProperty("state").GetProperty("documents")[0].GetProperty("revision").GetInt32());
        Assert.Equal(0, host.GetLostEvents("p"));
        await T.Until(() => Volatile.Read(ref state.ResumedCount) == 2); // the state source hears of the first start and of the restart
    }

    [Fact]
    public async Task APacedEventWaitsWhileThePluginHasTheWindowUnhandledAndGivesUpWhenItEnds()
    {
        await using var host = await Started(Options(o => o with { EventWindow = 1 }), null, null, Spec("p"));
        await host.CallAsync("p", "setEventDelay", Args("{\"ms\":60000}"));
        Assert.True(await host.SendEventAsync("p", "first", Args("{}"), paced: true));
        var held = host.SendEventAsync("p", "second", Args("{}"), paced: true);
        Assert.True(await host.SendEventAsync("p", "notice", Args("{}"))); // not paced: a cancelled task must not wait behind document events
        await Task.Delay(300);
        Assert.False(held.IsCompleted);

        await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("p", "crash", Args("{\"code\":3}")));
        Assert.False(await held.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    sealed class FailingEvents() : PluginBase("failing-events", 1)
    {
        public int Seen;

        protected override ValueTask OnEventAsync(JsonElement eventPayload)
        {
            Interlocked.Increment(ref Seen);
            throw new InvalidOperationException("test: the event handler failed");
        }
    }

    [Fact]
    public async Task AnSdkPluginDeclaresEventAcksAndAcknowledgesEveryEventAlsoAFailedOne()
    {
        var toPlugin = new MemoryPipe();
        var toEditor = new MemoryPipe();
        var plugin = new FailingEvents();
        var run = PluginRunner.RunAsync(plugin, T.Roomy, toPlugin, toEditor);
        var acks = 0;
        await using var editor = new JsonRpcDispatcher(toEditor, toPlugin, RpcProtocols.Plugin, T.Roomy, new DelegateHandler(onNotification: (method, p) =>
        {
            if (method == PluginMethods.EventsHandled) Interlocked.Add(ref acks, p.GetProperty("count").GetInt32());
            return ValueTask.CompletedTask;
        }));

        var init = await editor.RequestAsync(PluginMethods.Initialize, Args("{\"apiVersion\":1,\"restarted\":false,\"hostName\":\"test\"}"));
        Assert.True(init.GetProperty("eventAcks").GetBoolean());
        await editor.NotifyAsync(PluginMethods.Event, Args("{\"name\":\"a\",\"payload\":null}"));
        await editor.NotifyAsync(PluginMethods.Event, Args("{\"name\":\"b\",\"payload\":null}"));
        await T.Until(() => Volatile.Read(ref acks) == 2);
        Assert.Equal(2, Volatile.Read(ref plugin.Seen));

        await editor.NotifyAsync(PluginMethods.Exit);
        await run.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task AWriteInterruptedByACrashIsNotRepeatedAfterTheRestart()
    {
        var file = T.TempFile();
        try
        {
            await using var host = await Started(Options(), null, null, Spec("w"));
            var e = await Assert.ThrowsAsync<PluginCrashedException>(() => host.CallAsync("w", "writeThenCrash", Args($"{{\"file\":{JsonSerializer.Serialize(file)},\"text\":\"once\"}}")));
            Assert.Equal(42, e.Info.ExitCode);
            await T.Until(() => host.GetState("w") == PluginState.Running);
            await Task.Delay(500);
            Assert.Equal(["once"], File.ReadAllLines(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task AFrozenPluginIsKilledNamedAndRestarted()
    {
        var crashed = new List<PluginCrashInfo>();
        await using var host = await Started(Options(FastPings), null, null, Spec("hang"));
        host.Crashed += i => { lock (crashed) crashed.Add(i); };
        var pid = await Pid(host, "hang");
        await host.CallAsync("hang", "freeze");
        await T.Until(() => { lock (crashed) return crashed.Count == 1; });
        Assert.Equal(PluginEndReason.Unresponsive, crashed[0].Reason);
        Assert.Equal("hang", crashed[0].PluginId);
        await T.Until(() => !Alive(pid));
        await T.Until(() => host.GetState("hang") == PluginState.Running);
    }

    [Fact]
    public async Task APluginWaitingForTheUserIsNotKilledEvenIfItAnswersNoPings()
    {
        var crashed = 0;
        await using var host = await Started(Options(FastPings), null, null, Spec("ask"));
        host.Crashed += _ => Interlocked.Increment(ref crashed);
        var answer = await host.CallAsync("ask", "awaitFrozen", Args("{\"ms\":2000}"));
        Assert.Equal("decided", answer.GetString());
        Assert.Equal(0, crashed);
        Assert.Equal(PluginState.Running, host.GetState("ask"));
    }

    [Theory]
    [InlineData("edit/propose")]
    [InlineData("workspace/edit")]
    [InlineData("agent/permission")]
    [InlineData("ui/quickPick")]
    [InlineData("ui/input")]
    [InlineData("ui/confirm")]
    public async Task APluginWaitingForADecisionOfTheUserIsNotKilledEvenIfItAnswersNoPings(string decisionMethod)
    {
        var decision = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var crashed = 0;
        PluginApiCallback api = (_, method, _, _) => method == decisionMethod ? new ValueTask<byte[]?>(decision.Task) : throw new RpcException(RpcErrorCodes.MethodNotFound, method);
        await using var host = await Started(Options(FastPings), api, null, Spec("ask"));
        host.Crashed += _ => Interlocked.Increment(ref crashed);
        await host.CallAsync("ask", "freeze");
        var pending = host.CallAsync("ask", "callEditor", Args($"{{\"method\":\"{decisionMethod}\",\"params\":{{}}}}"));
        await Task.Delay(3500); // far past three missed pings (150 ms interval, 500 ms timeout)
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, crashed);
        Assert.Equal(PluginState.Running, host.GetState("ask"));

        decision.SetResult(System.Text.Encoding.UTF8.GetBytes("{\"decision\":\"applied\"}"));
        Assert.Equal("applied", (await pending).GetProperty("decision").GetString());
    }

    [Fact]
    public async Task ShutdownEndsEveryPluginProcess()
    {
        var host = await Started(Options(), null, null, Spec("a"), Spec("b"));
        var pids = new[] { await Pid(host, "a"), await Pid(host, "b") };
        Assert.All(pids, p => Assert.True(Alive(p)));
        await host.ShutdownAsync();
        await T.Until(() => pids.All(p => !Alive(p)), 10_000);
        Assert.Equal(PluginState.Stopped, host.GetState("a"));
    }

    [Fact]
    public async Task ACancelledCallReachesThePluginAndFreesItsResources()
    {
        var file = T.TempFile();
        try
        {
            await using var host = await Started(Options(), null, null, Spec("c"));
            using var cts = new CancellationTokenSource();
            var call = host.CallAsync("c", "slow", Args($"{{\"ms\":60000,\"file\":{JsonSerializer.Serialize(file)}}}"), ct: cts.Token);
            await Task.Delay(500);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            await T.Until(() => File.Exists(file) && File.ReadAllText(file).Contains("cancelled"));
            await T.Until(() => host.CallAsync("c", "stats").GetAwaiter().GetResult().GetProperty("inFlight").GetInt32() <= 1);
            Assert.Equal(PluginState.Running, host.GetState("c"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task EachLimitReturnsItsOwnErrorAndThePluginsKeepRunning()
    {
        var api = new PluginApiCallback(async (id, method, p, ct) =>
        {
            await Task.Delay(400, ct);
            return T.Json("true");
        });
        await using var host = await Started(Options(), api, null, Spec("q"), Spec("other"));

        var items = await Assert.ThrowsAsync<RpcException>(() => host.CallAsync("q", "items", Args("{\"count\":5000}")));
        Assert.Equal(RpcErrorCodes.TooManyResults, items.Code);

        var big = await Assert.ThrowsAsync<RpcException>(() => host.CallAsync("q", "bigText", Args("{\"bytes\":2000000}"), TimeSpan.FromSeconds(20)));
        Assert.Equal(RpcErrorCodes.MessageTooLarge, big.Code);

        var hammer = await host.CallAsync("q", "hammer", Args("{\"n\":6}"));
        Assert.True(hammer.GetProperty("tooMany").GetInt32() >= 1);
        Assert.True(hammer.GetProperty("ok").GetInt32() >= 2);
        Assert.Equal(0, hammer.GetProperty("other").GetInt32());

        Assert.Equal(PluginState.Running, host.GetState("q"));
        Assert.Equal("1", (await host.CallAsync("q", "echo", Args("1"))).GetRawText());
        Assert.Equal("1", (await host.CallAsync("other", "echo", Args("1"))).GetRawText());
    }

    [Fact]
    public async Task APluginThatFloodsTheEditorBeyondTheSessionBudgetIsEndedAndNamed()
    {
        var crashed = new List<PluginCrashInfo>();
        var options = Options(o => o with { Limits = o.Limits with { MaxSessionBytes = 100_000 } });
        await using var host = await Started(options, null, null, Spec("loud"), Spec("calm"));
        host.Crashed += i => { lock (crashed) crashed.Add(i); };
        // The plugin is cut off while it is still sending, so its reply may never arrive.
        await Record.ExceptionAsync(() => host.CallAsync("loud", "notifyBig", Args("{\"bytes\":200000}")));
        await T.Until(() => { lock (crashed) return crashed.Count == 1; });
        Assert.Equal("loud", crashed[0].PluginId);
        Assert.Equal(PluginEndReason.ProtocolViolation, crashed[0].Reason);
        Assert.Equal("1", (await host.CallAsync("calm", "echo", Args("1"))).GetRawText());
    }

    /// <summary>An event of about 600 kB: two of them fill the outgoing queue of <see cref="Options"/> (1 MiB) once the plugin stops reading.</summary>
    static byte[] Bulk() => Args("{\"text\":\"" + new string('e', 600_000) + "\"}");

    [Fact]
    public async Task APluginThatStopsReadingHoldsBackOnlyItselfAndACallToItEndsWithItsTimeout()
    {
        await using var host = await Started(Options(), null, null, Spec("deaf"), Spec("other"));
        await host.CallAsync("deaf", "deaf", Args("{\"ms\":10000}"));
        var events = Task.WhenAll(host.PublishEventAsync("bulk", Bulk()), host.PublishEventAsync("bulk", Bulk()));

        await Assert.ThrowsAsync<RpcTimeoutException>(() => host.CallAsync("deaf", "echo", Args("1"), TimeSpan.FromMilliseconds(500)).WaitAsync(TimeSpan.FromSeconds(10)));
        await T.Until(() => host.CallAsync("other", "eventCount").GetAwaiter().GetResult().GetInt32() == 2, 5000);
        Assert.False(events.IsCompleted);
        Assert.Equal(PluginState.Running, host.GetState("other"));

        // once it reads again, what was held back arrives and nothing is lost
        await events.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal("1", (await host.CallAsync("deaf", "echo", Args("1"))).GetRawText());
        Assert.Equal(2, (await host.CallAsync("deaf", "eventCount")).GetInt32());
        Assert.Equal(0, host.GetLostEvents("deaf"));
        Assert.Equal(PluginState.Running, host.GetState("deaf"));
    }

    [Fact]
    public async Task APluginThatStopsReadingIsKilledAsUnresponsiveEvenWithItsQueueFull()
    {
        var crashed = new List<PluginCrashInfo>();
        await using var host = await Started(Options(FastPings), null, null, Spec("deaf"));
        host.Crashed += i => { lock (crashed) crashed.Add(i); };
        var pid = await Pid(host, "deaf");
        await host.CallAsync("deaf", "deaf", Args("{\"ms\":600000}"));
        var events = Task.WhenAll(host.PublishEventAsync("bulk", Bulk()), host.PublishEventAsync("bulk", Bulk()));

        await T.Until(() => { lock (crashed) return crashed.Count == 1; });
        Assert.Equal("deaf", crashed[0].PluginId);
        Assert.Equal(PluginEndReason.Unresponsive, crashed[0].Reason);
        await events.WaitAsync(TimeSpan.FromSeconds(10)); // the event held back fails with the connection instead of waiting for ever
        await T.Until(() => !Alive(pid));
        await T.Until(() => host.GetState("deaf") == PluginState.Running);
    }

    [Fact]
    public async Task ChattyStderrIsDrainedAndOnlyTheTailIsKept()
    {
        await using var host = new PluginHost.PluginHost(Options());
        host.Register(Spec("noisy", "--stderr-flood", "20000"));
        await host.ActivateAsync("onCommand:noisy");
        Assert.Equal("1", (await host.CallAsync("noisy", "echo", Args("1"))).GetRawText());
        await T.Until(() => host.GetStderrTail("noisy").Any(l => l.StartsWith("flood 19999", StringComparison.Ordinal)));
        Assert.Equal(10, host.GetStderrTail("noisy").Count);
    }

    sealed class FixedState(string json) : IPluginStateSource
    {
        public int ResumedCount;
        public byte[] BuildState(string pluginId) => T.Json(json);
        void IPluginStateSource.Resumed(string pluginId) => Interlocked.Increment(ref ResumedCount);
    }
}
