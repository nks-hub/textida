using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Tools;
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
    /// <summary>Process ids of the plugin processes that have not ended, with their start time (a process id can be reused; the time tells ours from a stranger's).</summary>
    readonly Dictionary<int, DateTime> _live = new();
    bool _shuttingDown;

    public PluginHost(PluginHostOptions options, PluginApiCallback? api = null, IPluginStateSource? stateSource = null)
    {
        if (options.EventWindow is < 1) throw new ArgumentException("The event window must be positive.", nameof(options));
        if (options.IdleStopAfter <= TimeSpan.Zero || options.ShutdownTotal <= TimeSpan.Zero) throw new ArgumentException("The idle stop time and the total shutdown time must be positive.", nameof(options));
        if (options.WorkspaceScan is { MaxEntries: < 1 } or { MaxDepth: < 0 }) throw new ArgumentException("The workspace search needs a positive number of entries and a depth of 0 or more.", nameof(options));
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
        /// <summary>Requests of the plugin that wait for a decision of the user (<see cref="UserDecisions"/>); the plugin is idle meanwhile, not hung.</summary>
        public int Deciding;
        public string? DisabledReason;
        public volatile bool Removed;
        /// <summary>The loop that starts the process, waits for its end and starts it again after a crash; completes when the plugin is back to <see cref="PluginState.Inactive"/> or <see cref="PluginState.Disabled"/>.</summary>
        public Task Supervisor = Task.CompletedTask;
        /// <summary>Completes with the first handshake of the current supervisor (faults when it fails), so a second activation waits for the first.</summary>
        public Task Started = Task.CompletedTask;
        /// <summary>Cancelled by a stop: ends the wait before a restart.</summary>
        public CancellationTokenSource? Control;
        /// <summary>The user or the idle timer wants the process gone; the supervisor then ends in <see cref="PluginState.Inactive"/> and records no crash.</summary>
        public volatile bool StopRequested;
        public PluginCrashInfo? LastCrash;
        /// <summary><see cref="Environment.TickCount64"/> of the last work: a call, a request of the plugin, an activation event.</summary>
        public long LastActivity;
        /// <summary>Calls to the plugin and requests of the plugin that are in progress.</summary>
        public int Busy;
    }

    sealed class Run(Process process)
    {
        public Process Process { get; } = process;
        /// <summary>The plugin declared <see cref="InitializeResult.IdleStop"/>.</summary>
        public bool IdleStop;
        /// <summary>The process is being ended on purpose (a stop), so a call that finds it gone is not told of a crash.</summary>
        public volatile bool Intentional;
        public JsonRpcDispatcher Rpc = null!;
        public Task StderrTask = Task.CompletedTask;
        public readonly CancellationTokenSource Cts = new();
        public readonly TaskCompletionSource<PluginCrashInfo> Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PluginEndReason? KillReason;
        public string KillDetail = "";
        /// <summary>The plugin declared <see cref="InitializeResult.EventAcks"/>; <see cref="Unacked"/> counts its events sent and not handled yet.</summary>
        public bool AcksEvents;
        public int Unacked;
        public readonly SemaphoreSlim Acked = new(0);
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

    /// <summary>What the plugin manager shows about a plugin.</summary>
    public PluginStatus GetStatus(string pluginId) => StatusOf(Get(pluginId));

    public IReadOnlyList<PluginStatus> GetStatuses()
    {
        lock (_gate) return [.. _plugins.Values.Select(StatusOf)];
    }

    static PluginStatus StatusOf(Plugin p)
    {
        int? pid = null;
        if (p.Current is { } run && !run.Ended.Task.IsCompleted)
        {
            try { pid = run.Process.Id; }
            catch (InvalidOperationException) { }
        }
        return new PluginStatus(p.Spec.Id, p.Spec.DisplayName, p.State, pid, p.LastCrash, p.DisabledReason, p.Crashes.Count, p.Current?.IdleStop == true);
    }

    /// <summary>Process ids of the plugin processes that are alive.</summary>
    public IReadOnlyList<int> ProcessIds()
    {
        lock (_gate) return [.. _live.Keys];
    }

    /// <summary>
    /// Ends every plugin process that is still alive, by process id and without asking: the last resort when a shutdown ran out of time or the editor is going away.
    /// A process id that now belongs to a process that started later is left alone. Returns how many processes were killed.
    /// </summary>
    public int KillAll()
    {
        KeyValuePair<int, DateTime>[] live;
        lock (_gate) live = [.. _live];
        var killed = 0;
        foreach (var (pid, startedAt) in live)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited || Math.Abs((process.StartTime.ToUniversalTime() - startedAt).TotalSeconds) > 2) continue;
                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        }
        return killed;
    }

    Plugin Get(string id)
    {
        lock (_gate) return _plugins.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"Unknown plugin '{id}'.");
    }

    void SetState(Plugin p, PluginState state)
    {
        p.State = state;
        StateChanged?.Invoke(p.Spec.Id, state);
    }

    static void Touch(Plugin p) => Volatile.Write(ref p.LastActivity, Environment.TickCount64);

    /// <summary>
    /// Starts every inactive plugin that lists <paramref name="activationEvent"/> and returns when each has finished its handshake.
    /// A failing plugin throws here (the first failure wins) after the others were started.
    /// </summary>
    public async Task<IReadOnlyList<string>> ActivateAsync(string activationEvent)
    {
        List<(Plugin Plugin, Task Started, bool Fresh)> starting = [];
        lock (_gate)
        {
            if (_shuttingDown) throw new InvalidOperationException("The plugin host is shutting down.");
            foreach (var p in _plugins.Values)
            {
                if (!ActivationEvents.Lists(p.Spec.ActivationEvents, activationEvent)) continue;
                Touch(p);
                // a plugin that is starting for another event, or ending, is waited for: the caller needs it running when this returns
                switch (p.State)
                {
                    case PluginState.Inactive: starting.Add((p, BeginLocked(p), true)); break;
                    case PluginState.Starting: starting.Add((p, Joinable(p), false)); break;
                    case PluginState.Stopping: starting.Add((p, RestartAfterStopAsync(p), true)); break;
                }
            }
        }
        List<string> started = [];
        Exception? failure = null;
        foreach (var (p, task, fresh) in starting)
        {
            try { await task.ConfigureAwait(false); if (fresh) started.Add(p.Spec.Id); }
            catch (Exception e) { failure ??= e; }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return started;
    }

    /// <summary>
    /// Fires <c>workspaceContains:glob</c> for every glob that a file under one of the <paramref name="folders"/> matches (see <see cref="WorkspaceScan"/>) and starts the plugins that list it.
    /// Only plugins that are inactive and list such an event cause a search. Returns the ids of the plugins this started.
    /// </summary>
    public async Task<IReadOnlyList<string>> ActivateWorkspaceAsync(IReadOnlyList<string> folders)
    {
        if (_options.WorkspaceScan is not { } limits || folders.Count == 0) return [];
        List<string> globs;
        lock (_gate)
            globs = [.. _plugins.Values.Where(p => p.State == PluginState.Inactive).SelectMany(p => ActivationEvents.WorkspaceGlobs(p.Spec.ActivationEvents)).Distinct(StringComparer.Ordinal)];
        if (globs.Count == 0) return [];
        var matched = await Task.Run(() => WorkspaceScan.Find(folders, globs, limits.MaxEntries, limits.MaxDepth, limits.SkipFolders)).ConfigureAwait(false);
        List<string> started = [];
        Exception? failure = null;
        foreach (var glob in matched)
        {
            try { started.AddRange(await ActivateAsync(ActivationEvents.WorkspaceContainsPrefix + glob).ConfigureAwait(false)); }
            catch (Exception e) { failure ??= e; }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return started;
    }

    /// <summary>The first handshake of a plugin that is starting, for a second activation to wait for; nothing to wait for once that handshake is over (a restart after a crash goes on by itself).</summary>
    static Task Joinable(Plugin p) => p.Started.IsCompleted ? Task.CompletedTask : p.Started;

    /// <summary>Marks an inactive plugin as starting and runs its supervisor. Call with <c>_gate</c> held. The task completes at the first handshake, or faults.</summary>
    Task BeginLocked(Plugin p)
    {
        p.State = PluginState.Starting;
        p.StopRequested = false;
        p.Control = new CancellationTokenSource();
        Touch(p);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        p.Started = first.Task;
        p.Supervisor = Task.Run(() => SuperviseAsync(p, first));
        return first.Task;
    }

    /// <summary>The plugin is on its way out; once its supervisor has put it back to inactive it is started again for the event that needs it.</summary>
    async Task RestartAfterStopAsync(Plugin p)
    {
        await p.Supervisor.ConfigureAwait(false);
        Task started;
        lock (_gate)
        {
            if (_shuttingDown || p.Removed) throw new InvalidOperationException("The plugin host is shutting down.");
            started = p.State == PluginState.Inactive ? BeginLocked(p) : Joinable(p);
        }
        await started.ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a plugin at the request of the user, whatever its activation events say. A plugin that is waiting for a restart is started at once; one that was turned off
    /// after repeated crashes or a refused handshake gets a clean slate. Returns when the handshake is done; throws as <see cref="ActivateAsync"/> does.
    /// </summary>
    public async Task StartAsync(string pluginId)
    {
        var p = Get(pluginId);
        if (p.State == PluginState.WaitingRestart) await StopCoreAsync(p, null).ConfigureAwait(false);
        Task started;
        lock (_gate)
        {
            if (_shuttingDown || p.Removed) throw new InvalidOperationException("The plugin host is shutting down.");
            switch (p.State)
            {
                case PluginState.Disabled:
                    p.Crashes.Clear();
                    p.DisabledReason = null;
                    started = BeginLocked(p);
                    break;
                case PluginState.Inactive or PluginState.Stopped:
                    started = BeginLocked(p);
                    break;
                case PluginState.Starting:
                    started = Joinable(p);
                    break;
                case PluginState.Stopping:
                    started = RestartAfterStopAsync(p);
                    break;
                default:
                    return; // running, or a restart that began meanwhile
            }
        }
        await started.ConfigureAwait(false);
    }

    /// <summary>
    /// Ends the plugin process the way a shutdown does (a request to leave, then the end of its input, then a kill after the grace time) and leaves the plugin inactive: no crash is recorded,
    /// no restart follows, and the next activation event starts it again. A plugin that was turned off becomes inactive without a process to end.
    /// </summary>
    public Task StopAsync(string pluginId) => StopCoreAsync(Get(pluginId), null);

    public async Task RestartAsync(string pluginId)
    {
        await StopAsync(pluginId).ConfigureAwait(false);
        await StartAsync(pluginId).ConfigureAwait(false);
    }

    /// <summary>
    /// The stop behind <see cref="StopAsync"/> and the idle stop. With <paramref name="idleAfter"/> the plugin is stopped only if it has been quiet that long, has nothing in progress and is running;
    /// the check and the change of state happen together, so a call that arrives meanwhile is not cut off. Returns whether a stop was started or already on its way.
    /// </summary>
    async Task<bool> StopCoreAsync(Plugin p, TimeSpan? idleAfter, Run? only = null)
    {
        Run? run = null;
        Task supervisor;
        PluginState? raise = null;
        lock (_gate)
        {
            if (idleAfter is { } idle && (p.State != PluginState.Running || !ReferenceEquals(p.Current, only) || Volatile.Read(ref p.Busy) > 0 || p.Awaiting || Volatile.Read(ref p.Deciding) > 0
                || Environment.TickCount64 - Volatile.Read(ref p.LastActivity) < idle.TotalMilliseconds)) return false;
            switch (p.State)
            {
                case PluginState.Inactive or PluginState.Stopped:
                    return false;
                case PluginState.Stopping:
                    supervisor = p.Supervisor;
                    break;
                case PluginState.Disabled:
                    p.Crashes.Clear();
                    p.DisabledReason = null;
                    p.State = PluginState.Inactive;
                    raise = PluginState.Inactive;
                    supervisor = Task.CompletedTask;
                    break;
                default:
                    p.StopRequested = true;
                    p.State = PluginState.Stopping;
                    p.Control?.Cancel();
                    run = p.Current;
                    supervisor = p.Supervisor;
                    raise = PluginState.Stopping;
                    break;
            }
        }
        if (raise is { } state) StateChanged?.Invoke(p.Spec.Id, state);
        if (run is not null)
        {
            run.Intentional = true;
            await ShutdownRunAsync(run).ConfigureAwait(false);
        }
        await supervisor.ConfigureAwait(false);
        return true;
    }

    public async Task<JsonElement> CallAsync(string pluginId, string method, ReadOnlyMemory<byte> paramsJson = default, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Plugin p;
        Run? run;
        lock (_gate)
        {
            p = _plugins.TryGetValue(pluginId, out var found) ? found : throw new KeyNotFoundException($"Unknown plugin '{pluginId}'.");
            run = p.Current;
            if (p.State != PluginState.Running || run is null) throw new PluginUnavailableException(pluginId, p.State);
            Interlocked.Increment(ref p.Busy); // inside the lock, so an idle stop that checked before this call does not end the process under it
        }
        try { return await run.Rpc.RequestAsync(method, paramsJson, timeout ?? _options.DefaultCallTimeout, ct).ConfigureAwait(false); }
        catch (RpcConnectionClosedException)
        {
            // Never retried: the call may already have had its effect (R10).
            var info = await run.Ended.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            if (run.Intentional) throw new PluginUnavailableException(pluginId, PluginState.Stopping);
            throw new PluginCrashedException(info);
        }
        finally
        {
            Touch(p);
            Interlocked.Decrement(ref p.Busy);
        }
    }

    /// <summary>
    /// Sends an event to every running plugin at once, so a plugin that does not read holds back only its own copy; plugins that are down
    /// count it as lost and hear about it in their next resync.
    /// </summary>
    public async Task PublishEventAsync(string name, ReadOnlyMemory<byte> payloadJson)
    {
        var payload = EventPayload(name, payloadJson);
        List<Plugin> all;
        lock (_gate) all = [.. _plugins.Values];
        await Task.WhenAll(all.Select(p => SendAsync(p, payload))).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an event to one plugin. False when it could not be delivered (the plugin is down); it then counts as lost. A <paramref name="paced"/> event first waits
    /// while the plugin has <see cref="PluginHostOptions.EventWindow"/> events unhandled, so the backlog of a slow plugin stays with the caller.
    /// </summary>
    public Task<bool> SendEventAsync(string pluginId, string name, ReadOnlyMemory<byte> payloadJson, bool paced = false) =>
        SendAsync(Get(pluginId), EventPayload(name, payloadJson), paced ? _options.EventWindow : null);

    static async Task<bool> SendAsync(Plugin p, byte[] payload, int? window = null)
    {
        var run = p.Current;
        if (p.State == PluginState.Running && run is not null)
        {
            try
            {
                if (run.AcksEvents)
                {
                    while (window is { } w && Volatile.Read(ref run.Unacked) >= w) await run.Acked.WaitAsync(run.Cts.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref run.Unacked);
                }
                try { await run.Rpc.NotifyAsync(PluginMethods.Event, payload).ConfigureAwait(false); }
                catch (Exception) when (run.AcksEvents)
                {
                    Interlocked.Decrement(ref run.Unacked); // never sent, so never acknowledged
                    throw;
                }
                return true;
            }
            catch (IOException) { }
            catch (OperationCanceledException) { } // the run ended while the event waited
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
            if (_shuttingDown || p.Removed)
            {
                // the host went down (or the plugin was forgotten) before the process was started: nothing to end
                SetState(p, PluginState.Stopped);
                first?.TrySetCanceled();
                return;
            }
            if (p.StopRequested)
            {
                // stopped before the process was started (or while a restart was due): nothing to end
                first?.TrySetException(new PluginUnavailableException(p.Spec.Id, PluginState.Stopping));
                SetState(p, PluginState.Inactive);
                return;
            }
            PluginCrashInfo info;
            try
            {
                var run = await LaunchAsync(p, restarted).ConfigureAwait(false);
                if (p.StopRequested || _shuttingDown)
                {
                    // a stop, or the end of the host, came while the plugin started: it never counts as running
                    run.Intentional = true;
                    await ShutdownRunAsync(run).ConfigureAwait(false);
                }
                else
                {
                    SetState(p, PluginState.Running);
                    _stateSource?.Resumed(p.Spec.Id);
                    first?.TrySetResult();
                    first = null;
                }
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
            if (p.StopRequested)
            {
                // the user or the idle timer ended it: no crash, no restart; the next activation event starts it again
                first?.TrySetException(new PluginUnavailableException(p.Spec.Id, PluginState.Stopping));
                SetState(p, PluginState.Inactive);
                return;
            }

            var delay = RegisterCrash(p);
            info = info with { NextRestartIn = delay };
            p.LastCrash = info;
            Log?.Invoke($"Plugin '{info.PluginId}' crashed: {info.Reason}, exit {info.ExitCode}. {info.Detail}" + (delay is null ? " Disabled." : $" Restart in {delay}."));
            Crashed?.Invoke(info);
            if (delay is null)
            {
                p.DisabledReason = $"Crashed {_options.Restart.MaxCrashes} times within {_options.Restart.Window}.";
                SetState(p, PluginState.Disabled);
                return;
            }
            SetState(p, PluginState.WaitingRestart);
            try
            {
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, p.Control!.Token);
                await Task.Delay(delay.Value, wake.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                SetState(p, _stop.IsCancellationRequested ? PluginState.Stopped : PluginState.Inactive); // a shutdown ends it for good, a stop of the user only until the next event
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
        ChildProcessGuard.PassTo(psi);
        if (_options.ParentCheck is { } check) ParentWatch.Apply(psi, check.Interval, check.StartTolerance);

        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("The process did not start."); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            throw new PluginCrashedException(new PluginCrashInfo(spec.Id, spec.DisplayName, PluginEndReason.HandshakeFailed, null, $"Cannot start '{spec.Executable}': {e.Message}", [], null));
        }
        ProcessLaunch.Track(process);

        var run = new Run(process);
        DateTime startedAt;
        try { startedAt = process.StartTime.ToUniversalTime(); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { startedAt = DateTime.UtcNow; } // it ended already
        lock (_gate) _live[process.Id] = startedAt;
        run.StderrTask = Task.Run(() => StderrPump.RunAsync(process.StandardError, p.Stderr, _options.StderrTailLines, maxLineChars: 2000));
        run.Rpc = new JsonRpcDispatcher(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, RpcProtocols.Plugin, _options.Limits, new HostHandler(this, p, run));
        p.Current = run; // seen by a stop only once the connection exists
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
        if (run.IdleStop && _options.IdleStopAfter is { } idleAfter) _ = Task.Run(() => IdleAsync(p, run, idleAfter));
        return run;
    }

    async Task HandshakeAsync(Plugin p, Run run, bool restarted)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new InitializeParams(_options.ApiVersion, restarted, "Textida", _options.SettingsOf?.Invoke(p.Spec.Id)), PluginJson.Default.InitializeParams);
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
        run.AcksEvents = result.EventAcks;
        run.IdleStop = result.IdleStop;

        await run.Rpc.NotifyAsync(PluginMethods.Initialized).ConfigureAwait(false);
        var lost = Interlocked.Read(ref p.LostEvents);
        var resync = ResyncPayload(p, restarted, lost);
        try { await run.Rpc.RequestAsync(PluginMethods.Resync, resync, _options.HandshakeTimeout).ConfigureAwait(false); }
        catch (RpcException e) { throw new PluginHandshakeException(p.Spec.Id, $"Plugin '{p.Spec.DisplayName}' failed the state resync: {e.Message}"); }
        // events that failed while the resync was on its way were not in it: they stay counted
        Interlocked.Add(ref p.LostEvents, -lost);
    }

    byte[] ResyncPayload(Plugin p, bool restarted, long lostEvents)
    {
        var state = _stateSource?.BuildState(p.Spec.Id);
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteBoolean("restarted", restarted);
            w.WriteNumber("lostEvents", lostEvents);
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

    /// <summary>Ends a plugin that asked for it (<see cref="InitializeResult.IdleStop"/>) once it has had no work for <paramref name="after"/>.</summary>
    async Task IdleAsync(Plugin p, Run run, TimeSpan after)
    {
        try
        {
            while (!run.Ended.Task.IsCompleted)
            {
                var quiet = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - Volatile.Read(ref p.LastActivity)));
                if (_options.HoldsEditorState?.Invoke(p.Spec.Id) == true)
                {
                    Touch(p);
                    quiet = TimeSpan.Zero;
                }
                if (quiet >= after && await StopCoreAsync(p, after, run).ConfigureAwait(false))
                {
                    Log?.Invoke($"Plugin '{p.Spec.DisplayName}' ended after {after} without work; it starts again when it is needed.");
                    return;
                }
                // not idle enough, or busy: look again when the quiet time could be over
                await Task.Delay(quiet >= after ? after : after - quiet, run.Cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or RpcException) { }
    }

    async Task MonitorAsync(Plugin p, Run run)
    {
        var pid = run.Process.Id;
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
        lock (_gate) _live.Remove(pid);
        await Task.WhenAny(run.StderrTask, Task.Delay(1000)).ConfigureAwait(false);
        run.Cts.Cancel();
        if (!_shuttingDown && !p.StopRequested) p.State = PluginState.WaitingRestart;
        List<string> tail;
        lock (p.Stderr) tail = [.. p.Stderr];
        if (detail.Length == 0) detail = tail.Count > 0 ? "Last stderr line: " + tail[^1] : "The process exited.";
        run.Ended.TrySetResult(new PluginCrashInfo(p.Spec.Id, p.Spec.DisplayName, reason, run.Process.ExitCode, detail, tail, null));
        await run.Rpc.DisposeAsync().ConfigureAwait(false);
    }

    static void Kill(Run run)
    {
        try { run.Process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>
    /// Asks every plugin to shut down at once, then ends whatever is left. No process outlives this call: with <see cref="PluginHostOptions.ShutdownTotal"/> set, a plugin
    /// that has not gone by then is killed by its process id.
    /// </summary>
    public async Task ShutdownAsync()
    {
        List<Plugin> all;
        lock (_gate)
        {
            _shuttingDown = true;
            all = [.. _plugins.Values];
        }
        _stop.Cancel();
        var work = Task.WhenAll(all.Select(ShutdownOneAsync));
        if (_options.ShutdownTotal is { } total && await Task.WhenAny(work, Task.Delay(total)).ConfigureAwait(false) != work)
        {
            KillAll();
            await Task.WhenAny(work, Task.Delay(_options.ShutdownGrace)).ConfigureAwait(false);
            return;
        }
        await work.ConfigureAwait(false);
    }

    async Task ShutdownOneAsync(Plugin p)
    {
        if (p.Current is { } run) await ShutdownRunAsync(run).ConfigureAwait(false);
        if (p.State != PluginState.Disabled) SetState(p, PluginState.Stopped);
    }

    /// <summary>Asks the process to leave (a request, then the end of its input) and kills it when it has not gone after the grace time. Returns when the process has ended.</summary>
    async Task ShutdownRunAsync(Run run)
    {
        if (run.Ended.Task.IsCompleted) return;
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

    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);

    /// <summary>Editor methods that answer only when the user has decided (a change review, a dialog). While one is open the watchdog does not count missed pings.</summary>
    internal static readonly HashSet<string> UserDecisions =
    [
        Documents.DocumentMethods.EditPropose, Documents.DocumentMethods.WorkspaceEdit, Documents.DocumentMethods.AgentPermission,
        Ui.UiMethods.QuickPick, Ui.UiMethods.Input, Ui.UiMethods.Confirm,
    ];

    sealed class HostHandler(PluginHost host, Plugin plugin, Run run) : IRpcHandler
    {
        public async ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellation)
        {
            if (host._api is null) throw new RpcException(RpcErrorCodes.MethodNotFound, $"The editor has no method '{method}'.");
            Interlocked.Increment(ref plugin.Busy);
            try
            {
                if (!UserDecisions.Contains(method)) return await host._api(plugin.Spec.Id, method, parameters, cancellation).ConfigureAwait(false);
                Interlocked.Increment(ref plugin.Deciding);
                try { return await host._api(plugin.Spec.Id, method, parameters, cancellation).ConfigureAwait(false); }
                finally { Interlocked.Decrement(ref plugin.Deciding); }
            }
            finally
            {
                Touch(plugin);
                Interlocked.Decrement(ref plugin.Busy);
            }
        }

        public ValueTask HandleNotificationAsync(string method, JsonElement parameters)
        {
            if (method == PluginMethods.AwaitingUser)
                plugin.Awaiting = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("awaiting", out var a) && a.ValueKind == JsonValueKind.True;
            else if (method == PluginMethods.EventsHandled && run.AcksEvents && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var count) && count > 0)
                Acknowledge(count);
            return ValueTask.CompletedTask;
        }

        /// <summary>More acknowledgements than events sent would open the window wider than allowed, so the count stops at zero.</summary>
        void Acknowledge(int count)
        {
            int seen;
            do seen = Volatile.Read(ref run.Unacked);
            while (Interlocked.CompareExchange(ref run.Unacked, Math.Max(0, seen - count), seen) != seen);
            // one waiting send is enough to wake: it checks the count again before it goes on
            if (run.Acked.CurrentCount == 0) run.Acked.Release();
        }
    }
}
