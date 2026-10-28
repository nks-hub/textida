using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Acp;

// Protocol: Agent Client Protocol, version 1 (https://agentclientprotocol.com/protocol/overview).
// Pages the code follows: /protocol/transports (stdio, one message per line), /protocol/initialization, /protocol/session-setup,
// /protocol/prompt-turn (session/prompt, session/update, session/cancel), /protocol/file-system (fs/read_text_file, fs/write_text_file),
// /protocol/tool-calls (session/request_permission).

/// <summary>
/// Values the client needs and that are owner decisions, so none has a default.
/// </summary>
/// <param name="Limits">JSON-RPC quotas of the connection.</param>
/// <param name="InitializeTimeout">Longest wait for the answer to <c>initialize</c> and to <c>session/new</c>.</param>
/// <param name="IdleTimeout">A running turn in which the agent neither sends an update nor asks anything for this long ends with <see cref="AcpTimeoutException"/>; time spent waiting for the user does not count.</param>
/// <param name="ExitGrace">How long the client waits for the agent to leave after its input is closed (then it is killed), after a cancel before it gives up on the answer to the turn, and for its last output after it ended.</param>
/// <param name="StderrTailLines">Last lines of the agent's standard error kept for the report of a crash.</param>
public sealed record AcpOptions(RpcLimits Limits, TimeSpan InitializeTimeout, TimeSpan IdleTimeout, TimeSpan ExitGrace, int StderrTailLines, string ClientName, string ClientVersion);

/// <summary>An agent process as it will be started: the program (a full path), one entry per argument and the working directory.</summary>
public sealed record AcpLaunch(string Executable, IReadOnlyList<string> Args, string WorkingDirectory)
{
    /// <summary>
    /// Finds a program by name in PATH. On Windows a batch file (<c>.cmd</c>, <c>.bat</c>, the shape of the npm shims of most adapters) is found too; it is started through cmd.exe by <see cref="ProcessLaunch"/>.
    /// </summary>
    public static (string? Path, string? Problem) Find(string command, string? searchPath = null)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command) ? (command, null) : (null, $"'{command}' does not exist.");
        string[] suffixes = OperatingSystem.IsWindows() ? [".exe", ".com", ".cmd", ".bat"] : [""];
        foreach (var folder in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var suffix in suffixes)
                if (Path.Combine(folder, command + suffix) is var candidate && File.Exists(candidate)) return (candidate, null);
        return (null, $"'{command}' was not found in PATH.");
    }
}

public enum AcpUpdateKind { AgentMessage, AgentThought, ToolCall, ToolCallUpdate, Plan, Other }

/// <summary>One <c>session/update</c>. For a message or thought <paramref name="Text"/> is the chunk; for a plan it is the entries, one per line.</summary>
public sealed record AcpUpdate(string SessionId, AcpUpdateKind Kind, string? Text = null, string? ToolCallId = null, string? Title = null, string? Status = null, string? ToolKind = null);

/// <summary>An answer the agent offers to a permission request. <paramref name="Kind"/> is <c>allow_once</c>, <c>allow_always</c>, <c>reject_once</c> or <c>reject_always</c>.</summary>
public sealed record AcpPermissionOption(string Id, string Name, string Kind)
{
    public bool IsReject => Kind.StartsWith("reject", StringComparison.Ordinal);
}

/// <summary>The tool call a permission is asked for; the agent may state nothing but the id.</summary>
public sealed record AcpToolCallInfo(string? Id, string? Title, string? Kind);

/// <summary>The user's answer to a permission request: an option, or <see cref="Cancelled"/>.</summary>
public readonly record struct AcpPermissionChoice(string? OptionId)
{
    public static AcpPermissionChoice Cancelled => default;
    public bool IsCancelled => OptionId is null;
}

/// <summary>How the agent process ended: its exit code (null while it was still running when the wait ran out) and the last lines of its standard error.</summary>
public sealed record AcpExit(int? ExitCode, string StderrTail);

public sealed record AcpAgentInfo(string? Name, string? Title, string? Version, bool CanLoadSession);

/// <summary>The agent cannot be started, speaks another protocol version or answers something that is not ACP.</summary>
public class AcpException(string message, int? code = null) : Exception(message)
{
    public int? Code { get; } = code;
}

/// <summary>The agent process ended (or closed its output) while the client was waiting for it. Reported to the caller; it never takes the editor down.</summary>
public sealed class AcpAgentExitedException(AcpExit exit)
    : AcpException("The agent ended" + (exit.ExitCode is { } c ? $" (exit code {c})" : "") + (exit.StderrTail.Length > 0 ? ": " + UriMask.MaskText(exit.StderrTail.Split('\n')[^1].Trim()) : "."))
{
    public AcpExit Exit { get; } = exit;
}

/// <summary>The agent did not answer in time (<see cref="AcpOptions.InitializeTimeout"/> or <see cref="AcpOptions.IdleTimeout"/>).</summary>
public sealed class AcpTimeoutException(string what, TimeSpan after) : AcpException($"The agent did not answer {what} within {after.TotalSeconds:0.#} s.");

/// <summary>The host refuses a request of the agent; the agent gets the code and the message as a JSON-RPC error.</summary>
public sealed class AcpRefusedException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;

    /// <summary>The file does not exist for the agent (not open in the editor, or not shared with it): ACP "resource not found".</summary>
    public const int NotFound = -32002;
    /// <summary>The user or the editor declined the request.</summary>
    public const int Declined = -32001;
}

/// <summary>What the editor does when the agent asks for something. Every method may run on any thread.</summary>
public interface IAcpHost
{
    /// <summary><c>fs/read_text_file</c>. <paramref name="line"/> is 1-based. Throw <see cref="AcpRefusedException"/> to refuse.</summary>
    ValueTask<string> ReadTextFileAsync(string sessionId, string path, int? line, int? limit, CancellationToken cancellation);

    /// <summary><c>fs/write_text_file</c>. Returns when the file is written; throw <see cref="AcpRefusedException"/> when it was not.</summary>
    ValueTask WriteTextFileAsync(string sessionId, string path, string content, CancellationToken cancellation);

    /// <summary><c>session/request_permission</c>. <paramref name="cancellation"/> fires when the turn is cancelled; the answer is then "cancelled" whatever this returns.</summary>
    ValueTask<AcpPermissionChoice> RequestPermissionAsync(string sessionId, AcpToolCallInfo toolCall, IReadOnlyList<AcpPermissionOption> options, CancellationToken cancellation);
}

/// <summary>
/// An ACP client for an agent it starts itself (stdio transport). It runs <c>initialize</c>, <c>session/new</c> and prompt turns, hands the streamed
/// <c>session/update</c> notifications to <see cref="Updated"/>, answers the agent's file and permission requests through the <see cref="IAcpHost"/>, cancels a turn with
/// <c>session/cancel</c> and reports an agent that dies as <see cref="AcpAgentExitedException"/> and through <see cref="AgentExited"/>.
/// </summary>
public sealed class AcpClient : IAsyncDisposable
{
    public const int ProtocolVersion = 1;
    public const string Cancelled = "cancelled";

    private sealed class Turn(string sessionId)
    {
        public string SessionId { get; } = sessionId;
        public CancellationTokenSource Abandon { get; } = new();
    }

    private readonly AcpOptions _options;
    private readonly IAcpHost _host;
    private readonly Process _process;
    private readonly Queue<string> _tail = new();
    private readonly TaskCompletionSource<AcpExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CancellationTokenSource> _permissions = [];
    private JsonRpcDispatcher _rpc = null!;
    private Task _stderrPump = Task.CompletedTask, _watch = Task.CompletedTask;
    private volatile bool _disposing;
    private Turn? _turn;
    private int _waiting;
    private long _lastActivity = Environment.TickCount64;

    private AcpClient(AcpOptions options, IAcpHost host, Process process)
    {
        _options = options;
        _host = host;
        _process = process;
        ProcessId = process.Id;
    }

    /// <summary>The id of the agent process (for a program started through a batch file, the id of the command interpreter that runs it).</summary>
    public int ProcessId { get; }

    public AcpAgentInfo? Agent { get; private set; }

    /// <summary>One <c>session/update</c>, in the order the agent sent them. Raised on a pool thread; a handler that throws is ignored.</summary>
    public event Action<AcpUpdate>? Updated;

    /// <summary>Raised once, on a pool thread, when the agent ended without the client asking for it.</summary>
    public event Action<AcpExit>? AgentExited;

    /// <summary>Completes (never faults) when the agent process has ended or closed its output.</summary>
    public Task<AcpExit> Exited => _exit.Task;

    /// <summary>Starts the agent and completes <c>initialize</c>. Throws <see cref="AcpException"/> with a reason the user can read.</summary>
    public static async Task<AcpClient> StartAsync(AcpLaunch launch, AcpOptions options, IAcpHost host, Action<string>? onStderr = null, CancellationToken ct = default)
    {
        ProcessStartInfo info;
        try { info = ProcessLaunch.CreateStartInfo(launch.Executable, launch.Args); }
        catch (ToolException e) { throw new AcpException(e.Message); }
        info.WorkingDirectory = launch.WorkingDirectory;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardErrorEncoding = new UTF8Encoding(false);
        var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            process.Dispose();
            throw new AcpException(e.Message);
        }
        ProcessLaunch.Track(process);
        var client = new AcpClient(options, host, process);
        var stderr = ChildPipe.Error(process, new UTF8Encoding(false)); // the pipes are read on threads of their own, not on the pool
        client._stderrPump = Task.Run(() => StderrPump.RunAsync(stderr, client._tail, options.StderrTailLines, onStderr));
        client._rpc = new JsonRpcDispatcher(ChildPipe.Output(process), process.StandardInput.BaseStream, RpcProtocols.Acp, options.Limits, new Handler(client));
        client._watch = client.WatchAsync();
        try
        {
            await client.InitializeAsync(ct).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // ---- initialize and sessions ----

    private async Task InitializeAsync(CancellationToken ct)
    {
        var result = await RequestAsync("initialize", Obj(w =>
        {
            w.WriteNumber("protocolVersion", ProtocolVersion);
            w.WriteStartObject("clientCapabilities");
            w.WriteStartObject("fs");
            w.WriteBoolean("readTextFile", true);
            w.WriteBoolean("writeTextFile", true);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartObject("clientInfo");
            w.WriteString("name", _options.ClientName);
            w.WriteString("version", _options.ClientVersion);
            w.WriteEndObject();
        }), _options.InitializeTimeout, ct).ConfigureAwait(false);
        // the agent answers with the version it speaks when it does not know the one asked for; this client knows only version 1
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("protocolVersion", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != ProtocolVersion)
            throw new AcpException($"The agent speaks ACP version {(result.ValueKind == JsonValueKind.Object && result.TryGetProperty("protocolVersion", out var other) ? other.GetRawText() : "(not stated)")}; this client speaks version {ProtocolVersion}.");
        string? name = null, title = null, version = null;
        if (result.TryGetProperty("agentInfo", out var agent) && agent.ValueKind == JsonValueKind.Object)
        {
            name = Text(agent, "name");
            title = Text(agent, "title");
            version = Text(agent, "version");
        }
        bool load = result.TryGetProperty("agentCapabilities", out var caps) && caps.ValueKind == JsonValueKind.Object && caps.TryGetProperty("loadSession", out var l) && l.ValueKind == JsonValueKind.True;
        Agent = new AcpAgentInfo(name, title, version, load);
    }

    /// <summary>Creates a session working in <paramref name="cwd"/> (an absolute path) and returns its id. An agent that wants a login says so with an error, which is thrown as <see cref="AcpException"/>.</summary>
    public async Task<string> NewSessionAsync(string cwd, CancellationToken ct = default)
    {
        var result = await RequestAsync("session/new", Obj(w =>
        {
            w.WriteString("cwd", cwd);
            w.WriteStartArray("mcpServers");
            w.WriteEndArray();
        }), _options.InitializeTimeout, ct).ConfigureAwait(false);
        return result.ValueKind == JsonValueKind.Object && Text(result, "sessionId") is { Length: > 0 } id ? id : throw new AcpException("The answer to session/new has no sessionId.");
    }

    // ---- a turn ----

    /// <summary>
    /// Sends the user's message and waits for the end of the turn; the answer is the stop reason (<c>end_turn</c>, <c>max_tokens</c>, <c>max_turn_requests</c>, <c>refusal</c> or <see cref="Cancelled"/>).
    /// Cancelling <paramref name="ct"/> is <see cref="CancelAsync"/>: the turn still ends with the agent's answer, or with <see cref="Cancelled"/> when the agent does not give one within the exit grace.
    /// </summary>
    public async Task<string> PromptAsync(string sessionId, string text, CancellationToken ct = default)
    {
        var turn = new Turn(sessionId);
        if (Interlocked.CompareExchange(ref _turn, turn, null) is not null) throw new InvalidOperationException("A prompt turn is already running.");
        Touch();
        try
        {
            using var registration = ct.Register(() => _ = CancelAsync(sessionId));
            var request = Guard(() => _rpc.RequestAsync("session/prompt", Obj(w =>
            {
                w.WriteString("sessionId", sessionId);
                w.WriteStartArray("prompt");
                w.WriteStartObject();
                w.WriteString("type", "text");
                w.WriteString("text", text);
                w.WriteEndObject();
                w.WriteEndArray();
            }), null, turn.Abandon.Token));
            JsonElement result;
            try { result = await WaitIdleAsync(request, turn).ConfigureAwait(false); }
            catch (OperationCanceledException) when (turn.Abandon.IsCancellationRequested) { return Cancelled; }
            catch (RpcException e) { throw new AcpException(e.Message, e.Code); }
            return result.ValueKind == JsonValueKind.Object && Text(result, "stopReason") is { Length: > 0 } reason ? reason : throw new AcpException("The answer to session/prompt has no stopReason.");
        }
        finally
        {
            Volatile.Write(ref _turn, null);
            turn.Abandon.Dispose();
        }
    }

    private async Task<JsonElement> WaitIdleAsync(Task<JsonElement> request, Turn turn)
    {
        var poll = TimeSpan.FromMilliseconds(Math.Clamp(_options.IdleTimeout.TotalMilliseconds / 4, 10, 1000));
        while (true)
        {
            var finished = await Task.WhenAny(request, Task.Delay(poll)).ConfigureAwait(false);
            if (finished == request) return await request.ConfigureAwait(false);
            if (Volatile.Read(ref _waiting) > 0 || Environment.TickCount64 - Volatile.Read(ref _lastActivity) <= _options.IdleTimeout.TotalMilliseconds) continue;
            await CancelAsync(turn.SessionId).ConfigureAwait(false);
            _ = request.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new AcpTimeoutException("the turn", _options.IdleTimeout);
        }
    }

    /// <summary>
    /// Cancels the turn of the session: every permission request still open is answered "cancelled" (the protocol requires it), then <c>session/cancel</c> goes to the agent,
    /// which answers the turn with <see cref="Cancelled"/> after it has stopped. Does nothing when the agent has ended.
    /// </summary>
    public async Task CancelAsync(string sessionId)
    {
        CancellationTokenSource[] open;
        lock (_permissions) open = [.. _permissions];
        foreach (var cts in open)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        try { await _rpc.NotifyAsync(AcpProtocol.SessionCancelMethod, Obj(w => w.WriteString("sessionId", sessionId))).ConfigureAwait(false); }
        catch (Exception e) when (e is RpcConnectionClosedException or OperationCanceledException) { return; }
        if (Volatile.Read(ref _turn) is { } turn) _ = AbandonAfterGraceAsync(turn);
    }

    private async Task AbandonAfterGraceAsync(Turn turn)
    {
        await Task.Delay(_options.ExitGrace).ConfigureAwait(false);
        try { if (ReferenceEquals(Volatile.Read(ref _turn), turn)) turn.Abandon.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void Touch() => Volatile.Write(ref _lastActivity, Environment.TickCount64);

    // ---- the process ----

    private async Task<JsonElement> RequestAsync(string method, byte[] parameters, TimeSpan timeout, CancellationToken ct)
    {
        try { return await Guard(() => _rpc.RequestAsync(method, parameters, timeout, ct)).ConfigureAwait(false); }
        catch (RpcTimeoutException) { throw new AcpTimeoutException(method, timeout); }
        catch (RpcException e) { throw new AcpException(e.Code == AuthRequiredCode ? "The agent needs you to log in first (" + e.Message + "); log in with the agent's own program." : e.Message, e.Code); }
    }

    private const int AuthRequiredCode = -32000;

    /// <summary>Turns "the connection is closed" into the report of how the agent ended.</summary>
    private async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try { return await call().ConfigureAwait(false); }
        catch (RpcConnectionClosedException) { throw new AcpAgentExitedException(await _exit.Task.ConfigureAwait(false)); }
    }

    private async Task WatchAsync()
    {
        try
        {
            await Task.WhenAny(_rpc.Completion, _process.WaitForExitAsync()).ConfigureAwait(false);
            int? code = null;
            try
            {
                await _process.WaitForExitAsync().WaitAsync(_options.ExitGrace).ConfigureAwait(false);
                code = _process.ExitCode;
            }
            catch (TimeoutException) { }
            try { await _stderrPump.WaitAsync(_options.ExitGrace).ConfigureAwait(false); }
            catch (TimeoutException) { }
            string tail;
            lock (_tail) tail = string.Join('\n', _tail);
            var exit = new AcpExit(code, tail);
            _exit.TrySetResult(exit);
            if (!_disposing)
            {
                try { AgentExited?.Invoke(exit); }
                catch (Exception) { /* a handler that throws must not become an unobserved crash */ }
            }
        }
        catch (Exception e)
        {
            _exit.TrySetResult(new AcpExit(null, e.Message));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposing = true;
        await _rpc.DisposeAsync().ConfigureAwait(false); // closes the agent's input, which is its signal to leave
        try { await _process.WaitForExitAsync().WaitAsync(_options.ExitGrace).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { /* ended by itself in the meantime */ }
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        try { await _watch.WaitAsync(_options.ExitGrace + _options.ExitGrace).ConfigureAwait(false); }
        catch (TimeoutException) { }
        _process.Dispose();
    }

    // ---- what the agent asks of the client ----

    private static byte[] Obj(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private sealed class Handler(AcpClient client) : IRpcHandler
    {
        public async ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement p, CancellationToken cancellation)
        {
            client.Touch();
            Interlocked.Increment(ref client._waiting);
            try
            {
                return method switch
                {
                    "fs/read_text_file" => await ReadAsync(p, cancellation).ConfigureAwait(false),
                    "fs/write_text_file" => await WriteAsync(p, cancellation).ConfigureAwait(false),
                    "session/request_permission" => await PermissionAsync(p, cancellation).ConfigureAwait(false),
                    _ => throw new RpcException(RpcErrorCodes.MethodNotFound, $"The client does not support '{method}'."),
                };
            }
            catch (AcpRefusedException e) { throw new RpcException(e.Code, e.Message); }
            finally
            {
                Interlocked.Decrement(ref client._waiting);
                client.Touch();
            }
        }

        private static (string Session, string Path) Common(JsonElement p)
        {
            if (p.ValueKind != JsonValueKind.Object || Text(p, "sessionId") is not { } session) throw new RpcException(RpcErrorCodes.InvalidParams, "'sessionId' is required.");
            if (Text(p, "path") is not { Length: > 0 } path || !Path.IsPathRooted(path)) throw new RpcException(RpcErrorCodes.InvalidParams, "'path' must be an absolute path.");
            return (session, path);
        }

        private static int? OptionalInt(JsonElement p, string name, int min) =>
            !p.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null ? null
            : v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= min ? n
            : throw new RpcException(RpcErrorCodes.InvalidParams, $"'{name}' must be an integer of at least {min}.");

        private async Task<byte[]> ReadAsync(JsonElement p, CancellationToken ct)
        {
            var (session, path) = Common(p);
            var content = await client._host.ReadTextFileAsync(session, path, OptionalInt(p, "line", 1), OptionalInt(p, "limit", 0), ct).ConfigureAwait(false);
            return Obj(w => w.WriteString("content", content));
        }

        private async Task<byte[]> WriteAsync(JsonElement p, CancellationToken ct)
        {
            var (session, path) = Common(p);
            if (Text(p, "content") is not { } content) throw new RpcException(RpcErrorCodes.InvalidParams, "'content' is required.");
            await client._host.WriteTextFileAsync(session, path, content, ct).ConfigureAwait(false);
            return Obj(_ => { });
        }

        private async Task<byte[]> PermissionAsync(JsonElement p, CancellationToken ct)
        {
            if (p.ValueKind != JsonValueKind.Object || Text(p, "sessionId") is not { } session) throw new RpcException(RpcErrorCodes.InvalidParams, "'sessionId' is required.");
            if (!p.TryGetProperty("options", out var list) || list.ValueKind != JsonValueKind.Array) throw new RpcException(RpcErrorCodes.InvalidParams, "'options' must be an array.");
            var options = new List<AcpPermissionOption>();
            foreach (var o in list.EnumerateArray())
                if (o.ValueKind == JsonValueKind.Object && Text(o, "optionId") is { } id && Text(o, "name") is { } name)
                    options.Add(new AcpPermissionOption(id, name, Text(o, "kind") ?? ""));
            var tool = p.TryGetProperty("toolCall", out var t) && t.ValueKind == JsonValueKind.Object ? new AcpToolCallInfo(Text(t, "toolCallId"), Text(t, "title"), Text(t, "kind")) : new AcpToolCallInfo(null, null, null);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lock (client._permissions) client._permissions.Add(linked);
            AcpPermissionChoice choice;
            try { choice = await client._host.RequestPermissionAsync(session, tool, options, linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { choice = AcpPermissionChoice.Cancelled; }
            finally { lock (client._permissions) client._permissions.Remove(linked); }
            // a cancelled turn is answered "cancelled" whatever the user clicked meanwhile; so is an option the agent never offered
            if (linked.IsCancellationRequested || choice.OptionId is null || !options.Any(o => o.Id == choice.OptionId))
                return Obj(w => { w.WriteStartObject("outcome"); w.WriteString("outcome", "cancelled"); w.WriteEndObject(); });
            return Obj(w =>
            {
                w.WriteStartObject("outcome");
                w.WriteString("outcome", "selected");
                w.WriteString("optionId", choice.OptionId);
                w.WriteEndObject();
            });
        }

        public ValueTask HandleNotificationAsync(string method, JsonElement p)
        {
            client.Touch();
            if (method != "session/update" || p.ValueKind != JsonValueKind.Object || Text(p, "sessionId") is not { } session
                || !p.TryGetProperty("update", out var u) || u.ValueKind != JsonValueKind.Object)
                return ValueTask.CompletedTask;
            AcpUpdate update;
            switch (Text(u, "sessionUpdate"))
            {
                case "agent_message_chunk": update = new AcpUpdate(session, AcpUpdateKind.AgentMessage, ChunkText(u)); break;
                case "agent_thought_chunk": update = new AcpUpdate(session, AcpUpdateKind.AgentThought, ChunkText(u)); break;
                case "tool_call": update = new AcpUpdate(session, AcpUpdateKind.ToolCall, null, Text(u, "toolCallId"), Text(u, "title"), Text(u, "status") ?? "pending", Text(u, "kind")); break;
                case "tool_call_update": update = new AcpUpdate(session, AcpUpdateKind.ToolCallUpdate, null, Text(u, "toolCallId"), Text(u, "title"), Text(u, "status"), Text(u, "kind")); break;
                case "plan":
                    var entries = u.TryGetProperty("entries", out var e) && e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Object ? Text(x, "content") : null).OfType<string>() : [];
                    update = new AcpUpdate(session, AcpUpdateKind.Plan, string.Join('\n', entries));
                    break;
                default: update = new AcpUpdate(session, AcpUpdateKind.Other, null, null, null, null, Text(u, "sessionUpdate")); break;
            }
            try { client.Updated?.Invoke(update); }
            catch (Exception) { /* a failing view must not end the connection */ }
            return ValueTask.CompletedTask;
        }

        /// <summary>The text of a text content block; other kinds (image, audio, resource) are shown as their type in brackets.</summary>
        private static string ChunkText(JsonElement u)
        {
            if (!u.TryGetProperty("content", out var c) || c.ValueKind != JsonValueKind.Object) return "";
            return Text(c, "type") is "text" or null ? Text(c, "text") ?? "" : "[" + Text(c, "type") + "]";
        }
    }
}
