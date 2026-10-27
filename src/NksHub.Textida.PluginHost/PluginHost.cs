using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost;

/// <summary>
/// Runs every plugin in its own child process behind the plugin protocol: lazy activation, handshake, watchdog,
/// restart with backoff, resync, ordered shutdown. A plugin that dies only ever costs the calls that were in flight to it.
/// </summary>
public sealed class PluginHost : IAsyncDisposable
{
    readonly PluginHostOptions _options;
    readonly PluginApiCallback? _api;
    readonly IPluginStateSource? _stateSource;
    readonly Dictionary<string, Plugin> _plugins = new();
    readonly CancellationTokenSource _stop = new();
    readonly object _gate = new();
    bool _shuttingDown;

    public PluginHost(PluginHostOptions options, PluginApiCallback? api = null, IPluginStateSource? stateSource = null)
    {
        _options = options;
        _api = api;
        _stateSource = stateSource;
    }

    /// <summary>A plugin ended unexpectedly. Names the culprit and says when (if ever) it comes back.</summary>
    public event Action<PluginCrashInfo>? Crashed;
    public event Action<string, PluginState>? StateChanged;
    public event Action<string>? Log;

    sealed class Plugin(PluginSpec spec)
    {
        public PluginSpec Spec { get; } = spec;
        public PluginState State;
        public Run? Current;
        public readonly List<DateTime> Crashes = [];
        public readonly Queue<string> Stderr = new();
        public long LostEvents;
        public volatile bool Awaiting;
        /// <summary>Requests of the plugin that wait for a decision of the user (edit proposals); the plugin is idle meanwhile, not hung.</summary>
        public int Deciding;
        public string? DisabledReason;
        public volatile bool Removed;
    }

    sealed class Run(Process process)
    {
        public Process Process { get; } = process;
        public JsonRpcDispatcher Rpc = null!;
        public Task StderrTask = Task.CompletedTask;
        public readonly CancellationTokenSource Cts = new();
        public readonly TaskCompletionSource<PluginCrashInfo> Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PluginEndReason? KillReason;
        public string KillDetail = "";
    }

    public void Register(PluginSpec spec)
    {
        lock (_gate)
        {
            if (!_plugins.TryAdd(spec.Id, new Plugin(spec))) throw new ArgumentException($"Plugin '{spec.Id}' is already registered.");
        }
    }

    public bool IsRegistered(string pluginId)
    {
        lock (_gate) return _plugins.ContainsKey(pluginId);
    }

    /// <summary>Ends the plugin process the way a shutdown does and forgets the plugin, so it can be registered again (a new version) or never started (disabled, uninstalled).</summary>
    public async Task UnregisterAsync(string pluginId)
    {
        Plugin? p;
        lock (_gate)
        {
            if (!_plugins.Remove(pluginId, out p)) return;
            p.Removed = true;
        }
        await ShutdownOneAsync(p).ConfigureAwait(false);
    }

    public PluginState GetState(string pluginId) => Get(pluginId).State;

    public IReadOnlyList<string> GetStderrTail(string pluginId)
    {
        var p = Get(pluginId);
        lock (p.Stderr) return [.. p.Stderr];
    }

    /// <summary>Events that could not be delivered because the plugin was down; reported to it in the next resync.</summary>
    public long GetLostEvents(string pluginId) => Interlocked.Read(ref Get(pluginId).LostEvents);

    Plugin Get(string id)
    {
        lock (_gate) return _plugins.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"Unknown plugin '{id}'.");
    }

    void SetState(Plugin p, PluginState state)
    {
        p.State = state;
        StateChanged?.Invoke(p.Spec.Id, state);
    }

    /// <summary>
    /// Starts every inactive plugin that lists <paramref name="activationEvent"/> and returns when each has finished its handshake.
    /// A failing plugin throws here (the first failure wins) after the others were started.
    /// </summary>
    public async Task<IReadOnlyList<string>> ActivateAsync(string activationEvent)
    {
        List<(Plugin Plugin, Task Started)> starting = [];
        lock (_gate)
        {
            if (_shuttingDown) throw new InvalidOperationException("The plugin host is shutting down.");
            foreach (var p in _plugins.Values)
            {
                if (p.State != PluginState.Inactive || !p.Spec.ActivationEvents.Contains(activationEvent)) continue;
                p.State = PluginState.Starting;
                var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = Task.Run(() => SuperviseAsync(p, first));
                starting.Add((p, first.Task));
            }
        }
        List<string> started = [];
        Exception? failure = null;
        foreach (var (p, task) in starting)
        {
            try { await task.ConfigureAwait(false); started.Add(p.Spec.Id); }
            catch (Exception e) { failure ??= e; }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return started;
    }

    public async Task<JsonElement> CallAsync(string pluginId, string method, ReadOnlyMemory<byte> paramsJson = default, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var p = Get(pluginId);
        var run = p.Current;
        if (p.State != PluginState.Running || run is null) throw new PluginUnavailableException(pluginId, p.State);
        try { return await run.Rpc.RequestAsync(method, paramsJson, timeout, ct).ConfigureAwait(false); }
        catch (RpcConnectionClosedException)
        {
            // Never retried: the call may already have had its effect (R10).
            var info = await run.Ended.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            throw new PluginCrashedException(info);
        }
    }

    /// <summary>Sends an event to every running plugin; plugins that are down count it as lost and hear about it in their next resync.</summary>
    public async Task PublishEventAsync(string name, ReadOnlyMemory<byte> payloadJson)
    {
        var payload = EventPayload(name, payloadJson);
        List<Plugin> all;
        lock (_gate) all = [.. _plugins.Values];
        foreach (var p in all) await SendAsync(p, payload).ConfigureAwait(false);
    }

    /// <summary>Sends an event to one plugin. False when it could not be delivered (the plugin is down); it then counts as lost.</summary>
    public Task<bool> SendEventAsync(string pluginId, string name, ReadOnlyMemory<byte> payloadJson) =>
        SendAsync(Get(pluginId), EventPayload(name, payloadJson));

    static async Task<bool> SendAsync(Plugin p, byte[] payload)
    {
        var run = p.Current;
        if (p.State == PluginState.Running && run is not null)
        {
            try { await run.Rpc.NotifyAsync(PluginMethods.Event, payload).ConfigureAwait(false); return true; }
            catch (IOException) { }
        }
        if (p.State is PluginState.Running or PluginState.Starting or PluginState.WaitingRestart)
            Interlocked.Increment(ref p.LostEvents);
        return false;
    }

    static byte[] EventPayload(string name, ReadOnlyMemory<byte> payloadJson)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WritePropertyName("payload");
            if (payloadJson.IsEmpty) w.WriteNullValue();
            else { using var d = JsonDocument.Parse(payloadJson); d.RootElement.WriteTo(w); }
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    async Task SuperviseAsync(Plugin p, TaskCompletionSource? first)
    {
        var restarted = false;
        while (true)
        {
            PluginCrashInfo info;
            try
            {
                var run = await LaunchAsync(p, restarted).ConfigureAwait(false);
                SetState(p, PluginState.Running);
                first?.TrySetResult();
                first = null;
                info = await run.Ended.Task.ConfigureAwait(false);
            }
            catch (PluginHandshakeException e)
            {
                p.DisabledReason = e.Message;
                SetState(p, PluginState.Disabled);
                Log?.Invoke(e.Message);
                first?.TrySetException(e);
                return;
            }
            catch (PluginCrashedException e)
            {
                info = e.Info;
                first?.TrySetException(e);
                first = null;
            }
            p.Current = null;
            if (_shuttingDown || p.Removed)
            {
                SetState(p, PluginState.Stopped);
                first?.TrySetCanceled();
                return;
            }

            var delay = RegisterCrash(p);
            info = info with { NextRestartIn = delay };
            Log?.Invoke($"Plugin '{info.PluginId}' crashed: {info.Reason}, exit {info.ExitCode}. {info.Detail}" + (delay is null ? " Disabled." : $" Restart in {delay}."));
            Crashed?.Invoke(info);
            if (delay is null)
            {
                p.DisabledReason = $"Crashed {_options.Restart.MaxCrashes} times within {_options.Restart.Window}.";
                SetState(p, PluginState.Disabled);
                return;
            }
            SetState(p, PluginState.WaitingRestart);
            try { await Task.Delay(delay.Value, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                SetState(p, PluginState.Stopped);
                return;
            }
            if (p.Removed)
            {
                SetState(p, PluginState.Stopped);
                return;
            }
            restarted = true;
        }
    }

    /// <summary>Records a crash and returns the backoff before the next start, or null when the crash budget is spent.</summary>
    TimeSpan? RegisterCrash(Plugin p)
    {
        var policy = _options.Restart;
        var now = DateTime.UtcNow;
        p.Crashes.Add(now);
        p.Crashes.RemoveAll(t => now - t > policy.Window);
        if (p.Crashes.Count >= policy.MaxCrashes) return null;
        var factor = Math.Pow(2, p.Crashes.Count - 1);
        return TimeSpan.FromTicks((long)Math.Min(policy.MaxDelay.Ticks, policy.BaseDelay.Ticks * factor));
    }

    async Task<Run> LaunchAsync(Plugin p, bool restarted)
    {
        SetState(p, PluginState.Starting);
        p.Awaiting = false;
        var spec = p.Spec;
        var psi = new ProcessStartInfo(spec.Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (spec.WorkingDirectory is not null) psi.WorkingDirectory = spec.WorkingDirectory;
        foreach (var a in spec.Arguments) psi.ArgumentList.Add(a);

        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("The process did not start."); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            throw new PluginCrashedException(new PluginCrashInfo(spec.Id, spec.DisplayName, PluginEndReason.HandshakeFailed, null, $"Cannot start '{spec.Executable}': {e.Message}", [], null));
        }

        var run = new Run(process);
        p.Current = run;
        run.StderrTask = Task.Run(() => DrainStderrAsync(p, process));
        run.Rpc = new JsonRpcDispatcher(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, RpcProtocols.Plugin, _options.Limits, new HostHandler(this, p));
        _ = Task.Run(() => MonitorAsync(p, run));

        try
        {
            await HandshakeAsync(p, run, restarted).ConfigureAwait(false);
        }
        catch (PluginHandshakeException)
        {
            run.KillReason = PluginEndReason.HandshakeFailed;
            Kill(run);
            await run.Ended.Task.ConfigureAwait(false);
            throw;
        }
        catch (Exception e) when (e is RpcTimeoutException or RpcConnectionClosedException)
        {
            if (e is RpcTimeoutException)
            {
                run.KillReason = PluginEndReason.HandshakeFailed;
                run.KillDetail = $"No answer to the handshake within {_options.HandshakeTimeout}.";
                Kill(run);
            }
            var info = await run.Ended.Task.ConfigureAwait(false);
            throw new PluginCrashedException(info with { Reason = PluginEndReason.HandshakeFailed, Detail = info.Detail.Length > 0 ? info.Detail : e.Message });
        }
        _ = Task.Run(() => WatchdogAsync(p, run));
        return run;
    }

    async Task HandshakeAsync(Plugin p, Run run, bool restarted)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new InitializeParams(_options.ApiVersion, restarted, "Textida"), PluginJson.Default.InitializeParams);
        InitializeResult? result;
        try
        {
            var element = await run.Rpc.RequestAsync(PluginMethods.Initialize, request, _options.HandshakeTimeout).ConfigureAwait(false);
            result = element.Deserialize(PluginJson.Default.InitializeResult);
        }
        catch (RpcException e)
        {
            throw new PluginHandshakeException(p.Spec.Id, $"Plugin '{p.Spec.DisplayName}' refused the handshake: {e.Message}");
        }
        catch (JsonException e)
        {
            throw new PluginHandshakeException(p.Spec.Id, $"Plugin '{p.Spec.DisplayName}' sent an unreadable handshake answer: {e.Message}");
        }
        if (result is null || result.ApiVersion != _options.ApiVersion)
            throw new PluginHandshakeException(p.Spec.Id, $"Plugin '{p.Spec.DisplayName}' implements plugin API version {result?.ApiVersion.ToString() ?? "(none)"}, but this editor provides version {_options.ApiVersion}.");

        await run.Rpc.NotifyAsync(PluginMethods.Initialized).ConfigureAwait(false);
        var resync = ResyncPayload(p, restarted);
        try { await run.Rpc.RequestAsync(PluginMethods.Resync, resync, _options.HandshakeTimeout).ConfigureAwait(false); }
        catch (RpcException e) { throw new PluginHandshakeException(p.Spec.Id, $"Plugin '{p.Spec.DisplayName}' failed the state resync: {e.Message}"); }
        Interlocked.Exchange(ref p.LostEvents, 0);
    }

    byte[] ResyncPayload(Plugin p, bool restarted)
    {
        var state = _stateSource?.BuildState(p.Spec.Id);
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteBoolean("restarted", restarted);
            w.WriteNumber("lostEvents", Interlocked.Read(ref p.LostEvents));
            w.WritePropertyName("state");
            if (state is null || state.Length == 0) w.WriteNullValue();
            else { using var d = JsonDocument.Parse(state); d.RootElement.WriteTo(w); }
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    async Task WatchdogAsync(Plugin p, Run run)
    {
        var missed = 0;
        try
        {
            while (!run.Ended.Task.IsCompleted)
            {
                await Task.Delay(_options.PingInterval, run.Cts.Token).ConfigureAwait(false);
                if (p.Awaiting || Volatile.Read(ref p.Deciding) > 0) { missed = 0; continue; }
                try
                {
                    await run.Rpc.RequestAsync(PluginMethods.Ping, default, _options.PingTimeout, run.Cts.Token).ConfigureAwait(false);
                    missed = 0;
                }
                catch (RpcTimeoutException)
                {
                    if (++missed < _options.MissedPingsBeforeKill) continue;
                    run.KillReason = PluginEndReason.Unresponsive;
                    run.KillDetail = $"No answer to {missed} pings in a row ({_options.PingTimeout} each).";
                    Kill(run);
                    return;
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or RpcException) { }
    }

    async Task MonitorAsync(Plugin p, Run run)
    {
        await Task.WhenAny(run.Process.WaitForExitAsync(), run.Rpc.Completion).ConfigureAwait(false);
        var reason = run.KillReason ?? PluginEndReason.Exited;
        var detail = run.KillDetail;
        if (!run.Process.HasExited)
        {
            if (run.KillReason is null)
            {
                var fault = run.Rpc.Fault;
                reason = fault is RpcFramingException or RpcException ? PluginEndReason.ProtocolViolation : PluginEndReason.Exited;
                detail = fault is null ? "The plugin closed its output." : fault.Message;
            }
            Kill(run);
        }
        await run.Process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAny(run.StderrTask, Task.Delay(1000)).ConfigureAwait(false);
        run.Cts.Cancel();
        if (!_shuttingDown) p.State = PluginState.WaitingRestart;
        List<string> tail;
        lock (p.Stderr) tail = [.. p.Stderr];
        if (detail.Length == 0) detail = tail.Count > 0 ? "Last stderr line: " + tail[^1] : "The process exited.";
        run.Ended.TrySetResult(new PluginCrashInfo(p.Spec.Id, p.Spec.DisplayName, reason, run.Process.ExitCode, detail, tail, null));
        await run.Rpc.DisposeAsync().ConfigureAwait(false);
    }

    async Task DrainStderrAsync(Plugin p, Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (line.Length > 2000) line = line[..2000];
                lock (p.Stderr)
                {
                    p.Stderr.Enqueue(line);
                    while (p.Stderr.Count > _options.StderrTailLines) p.Stderr.Dequeue();
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    static void Kill(Run run)
    {
        try { run.Process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Asks every plugin to shut down, then ends whatever is left. No process outlives this call.</summary>
    public async Task ShutdownAsync()
    {
        List<Plugin> all;
        lock (_gate)
        {
            _shuttingDown = true;
            all = [.. _plugins.Values];
        }
        _stop.Cancel();
        await Task.WhenAll(all.Select(ShutdownOneAsync)).ConfigureAwait(false);
    }

    async Task ShutdownOneAsync(Plugin p)
    {
        var run = p.Current;
        if (run is not null && !run.Ended.Task.IsCompleted)
        {
            try
            {
                await run.Rpc.RequestAsync(PluginMethods.Shutdown, default, _options.ShutdownGrace).ConfigureAwait(false);
                await run.Rpc.NotifyAsync(PluginMethods.Exit).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or RpcException or TimeoutException) { }
            try { run.Process.StandardInput.Close(); }
            catch (IOException) { }
            if (await Task.WhenAny(run.Ended.Task, Task.Delay(_options.ShutdownGrace)).ConfigureAwait(false) != run.Ended.Task)
            {
                run.KillReason = PluginEndReason.Unresponsive;
                run.KillDetail = "Did not exit after shutdown.";
                Kill(run);
            }
            await run.Ended.Task.ConfigureAwait(false);
        }
        if (p.State != PluginState.Disabled) SetState(p, PluginState.Stopped);
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);

    sealed class HostHandler(PluginHost host, Plugin plugin) : IRpcHandler
    {
        public async ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellation)
        {
            if (host._api is null) throw new RpcException(RpcErrorCodes.MethodNotFound, $"The editor has no method '{method}'.");
            if (method != Documents.DocumentMethods.EditPropose) return await host._api(plugin.Spec.Id, method, parameters, cancellation).ConfigureAwait(false);
            Interlocked.Increment(ref plugin.Deciding);
            try { return await host._api(plugin.Spec.Id, method, parameters, cancellation).ConfigureAwait(false); }
            finally { Interlocked.Decrement(ref plugin.Deciding); }
        }

        public ValueTask HandleNotificationAsync(string method, JsonElement parameters)
        {
            if (method == PluginMethods.AwaitingUser)
                plugin.Awaiting = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("awaiting", out var a) && a.ValueKind == JsonValueKind.True;
            return ValueTask.CompletedTask;
        }
    }
}
