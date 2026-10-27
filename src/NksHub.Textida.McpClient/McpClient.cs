using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.McpClient;

/// <summary>
/// Values the client needs and that are owner decisions, so none has a default.
/// </summary>
/// <param name="Limits">JSON-RPC quotas of the connection.</param>
/// <param name="HandshakeTimeout">Longest wait for the answer to the first request of each protocol era.</param>
/// <param name="ExitGrace">How long the client waits for the server to leave after its input is closed (then it is killed), and for its last output after it ended.</param>
/// <param name="StderrTailLines">Last lines of the server's standard error kept for the report of a crash.</param>
public sealed record McpClientOptions(RpcLimits Limits, TimeSpan HandshakeTimeout, TimeSpan ExitGrace, int StderrTailLines, string ClientName, string ClientVersion);

/// <summary>A server process as it will be started: the program (a full path), one entry per argument, extra environment variables and the values that must never be shown.</summary>
public sealed record McpLaunch(string Executable, IReadOnlyList<string> Args, string WorkingDirectory, IReadOnlyDictionary<string, string> Env, IReadOnlyList<string> SecretValues)
{
    /// <summary>Values shorter than this are not searched for in text (a one-letter value would blank out every line); display of environment variables masks them all by name.</summary>
    public const int MinMaskedLength = 4;
    public const string MaskText = "***";

    /// <summary>The command for the user's eyes: values of the environment and anything that equals one of them are replaced by <see cref="MaskText"/>.</summary>
    public string CommandLine => Mask(new Core.Tools.ToolInvocation("mcp", Executable, Args, WorkingDirectory, null, "").CommandLine);

    /// <summary>One line per environment variable, <c>NAME=***</c>; a plain <c>${REFERENCE}</c> is not a secret and stays.</summary>
    public IReadOnlyList<string> MaskedEnvironment => [.. Env.Select(e => e.Key + "=" + McpText.MaskValue(e.Value))];

    /// <summary>Replaces every secret value in <paramref name="text"/>, longest first.</summary>
    public string Mask(string text) => McpText.MaskSecrets(text, SecretValues);
}

/// <summary>Masking shared by the display of a command, the log and the snippets of a conversion.</summary>
public static class McpText
{
    public static string MaskValue(string value) => IsReference(value.StartsWith("Bearer ", StringComparison.Ordinal) ? value["Bearer ".Length..] : value) ? value : McpLaunch.MaskText;

    /// <summary>True for a text that only names another variable (<c>${NAME}</c>): it holds no secret itself.</summary>
    public static bool IsReference(string value) =>
        value.Length > 3 && value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}') && value.IndexOf('}') == value.Length - 1 && value.IndexOf('$', 1) < 0;

    public static string MaskSecrets(string text, IEnumerable<string> secrets)
    {
        foreach (var secret in secrets.Where(s => s.Length >= McpLaunch.MinMaskedLength).Distinct().OrderByDescending(s => s.Length))
            text = text.Replace(secret, McpLaunch.MaskText, StringComparison.Ordinal);
        return text;
    }

    /// <summary>A URL without the password and without the values of its query string, which often carry keys.</summary>
    public static string MaskUrl(string url)
    {
        int q = url.IndexOf('?');
        var head = q < 0 ? url : url[..q];
        int scheme = head.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            int authorityEnd = head.IndexOf('/', scheme + 3) is var slash and >= 0 ? slash : head.Length;
            int at = head.LastIndexOf('@', authorityEnd - 1, authorityEnd - scheme - 3);
            if (at >= 0) head = head[..(scheme + 3)] + McpLaunch.MaskText + head[at..];
        }
        if (q < 0) return head;
        return head + "?" + string.Join('&', url[(q + 1)..].Split('&').Select(p => p.IndexOf('=') is var i and > 0 ? p[..i] + "=" + McpLaunch.MaskText : p));
    }
}

/// <summary>A tool the server offers.</summary>
public sealed record McpTool(string Name, string? Title, string? Description, string InputSchemaJson, bool ReadOnly);

/// <summary>The answer to a tool call: the text parts joined, and whether the server says it failed.</summary>
public sealed record McpToolResult(bool IsError, string Text, string RawJson);

/// <summary>How a server process ended: its exit code (null while it was still running when the wait ran out) and the last lines of its standard error, secrets masked.</summary>
public sealed record McpServerExit(int? ExitCode, string StderrTail);

/// <summary>The server cannot be started, speaks neither protocol era or answers something that is not MCP.</summary>
public class McpClientException(string message) : Exception(message);

/// <summary>The server process ended (or closed its output) while the client was waiting for it. Reported to the caller; it never takes the editor down.</summary>
public sealed class McpServerExitedException(McpServerExit exit)
    : McpClientException("The MCP server ended" + (exit.ExitCode is { } c ? $" (exit code {c})" : "") + (exit.StderrTail.Length > 0 ? ": " + exit.StderrTail.Split('\n')[^1].Trim() : "."))
{
    public McpServerExit Exit { get; } = exit;
}

/// <summary>
/// An MCP client for a server it starts itself (stdio transport). It speaks both protocol eras of the editor's own server: it first asks <c>server/discover</c> with the
/// per-request version of 2026-07-28; a server that does not know it is greeted with <c>initialize</c> of 2025-11-25. Calls can be cancelled, which sends
/// <c>notifications/cancelled</c> (the JSON-RPC layer does it), and a server that dies is reported as <see cref="McpServerExitedException"/> and through <see cref="ServerExited"/>.
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
    public const string ModernVersion = "2026-07-28";
    public const string LegacyVersion = "2025-11-25";
    private const string VersionKey = "io.modelcontextprotocol/protocolVersion";
    private const string InfoKey = "io.modelcontextprotocol/clientInfo";
    private const string CapabilitiesKey = "io.modelcontextprotocol/clientCapabilities";

    private readonly McpClientOptions _options;
    private readonly McpLaunch _launch;
    private readonly Process _process;
    private readonly Queue<string> _tail = new();
    private readonly TaskCompletionSource<McpServerExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<string>? _onStderr;
    private JsonRpcDispatcher _rpc = null!;
    private Task _stderrPump = Task.CompletedTask, _watch = Task.CompletedTask;
    private volatile bool _disposing;
    private bool _modern;

    private McpClient(McpLaunch launch, McpClientOptions options, Process process, Action<string>? onStderr)
    {
        _launch = launch;
        _options = options;
        _process = process;
        _onStderr = onStderr;
    }

    /// <summary>The protocol era in use: <see cref="ModernVersion"/> or <see cref="LegacyVersion"/>.</summary>
    public string ProtocolVersion => _modern ? ModernVersion : LegacyVersion;
    public string? ServerName { get; private set; }
    public string? ServerVersion { get; private set; }
    public string? Instructions { get; private set; }

    /// <summary>Raised once, on a pool thread, when the server ended without the client asking for it.</summary>
    public event Action<McpServerExit>? ServerExited;

    /// <summary>Completes (never faults) when the server process has ended or closed its output.</summary>
    public Task<McpServerExit> Exited => _exit.Task;

    /// <summary>Starts the server and completes the handshake. Throws <see cref="McpClientException"/> (or <see cref="McpServerExitedException"/>) with a reason the user can read.</summary>
    public static async Task<McpClient> StartAsync(McpLaunch launch, McpClientOptions options, Action<string>? onStderr = null, CancellationToken ct = default)
    {
        ProcessStartInfo info;
        try { info = ProcessLaunch.CreateStartInfo(launch.Executable, launch.Args); }
        catch (ToolException e) { throw new McpClientException(launch.Mask(e.Message)); }
        info.WorkingDirectory = launch.WorkingDirectory;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardErrorEncoding = new UTF8Encoding(false);
        foreach (var (name, value) in launch.Env) info.Environment[name] = value;
        var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            process.Dispose();
            throw new McpClientException(launch.Mask(e.Message));
        }
        var client = new McpClient(launch, options, process, onStderr);
        client._stderrPump = Task.Run(client.PumpStderrAsync);
        client._rpc = new JsonRpcDispatcher(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, RpcProtocols.Mcp, options.Limits, new Handler());
        client._watch = client.WatchAsync();
        try
        {
            await client.HandshakeAsync(ct).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // ---- handshake ----

    private async Task HandshakeAsync(CancellationToken ct)
    {
        JsonElement discovered = default;
        bool modern = false;
        try
        {
            discovered = await Guard(() => _rpc.RequestAsync("server/discover", Params(true, null), _options.HandshakeTimeout, ct)).ConfigureAwait(false);
            modern = discovered.ValueKind == JsonValueKind.Object && discovered.TryGetProperty("supportedVersions", out var versions) && versions.ValueKind == JsonValueKind.Array
                && versions.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == ModernVersion);
        }
        catch (RpcException) { /* an older server does not know server/discover: the handshake below is for it */ }
        catch (RpcTimeoutException) { throw new McpClientException($"The server did not answer within {_options.HandshakeTimeout.TotalSeconds:0.#} s."); }

        if (modern)
        {
            _modern = true;
            ReadServerInfo(discovered);
            return;
        }
        var init = await Guard(() => _rpc.RequestAsync("initialize", Params(false, w =>
        {
            w.WriteString("protocolVersion", LegacyVersion);
            w.WriteStartObject("capabilities");
            w.WriteEndObject();
            w.WriteStartObject("clientInfo");
            w.WriteString("name", _options.ClientName);
            w.WriteString("version", _options.ClientVersion);
            w.WriteEndObject();
        }), _options.HandshakeTimeout, ct)).ConfigureAwait(false);
        var chosen = init.ValueKind == JsonValueKind.Object && init.TryGetProperty("protocolVersion", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (chosen != LegacyVersion)
            throw new McpClientException($"The server speaks MCP version {chosen ?? "(not stated)"}; this client speaks {ModernVersion} and {LegacyVersion}.");
        ReadServerInfo(init);
        await Guard(async () => { await _rpc.NotifyAsync("notifications/initialized", ct: ct).ConfigureAwait(false); return 0; }).ConfigureAwait(false);
    }

    private void ReadServerInfo(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return;
        if (result.TryGetProperty("instructions", out var i) && i.ValueKind == JsonValueKind.String) Instructions = i.GetString();
        var info = result.TryGetProperty("serverInfo", out var s) ? s
            : result.TryGetProperty("_meta", out var m) && m.ValueKind == JsonValueKind.Object && m.TryGetProperty("io.modelcontextprotocol/serverInfo", out var mi) ? mi : default;
        if (info.ValueKind != JsonValueKind.Object) return;
        ServerName = info.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        ServerVersion = info.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>The parameters of a request; in the modern era they carry the version, the client and its capabilities in <c>_meta</c>.</summary>
    private byte[] Params(bool modern, Action<Utf8JsonWriter>? body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            body?.Invoke(w);
            if (modern)
            {
                w.WriteStartObject("_meta");
                w.WriteString(VersionKey, ModernVersion);
                w.WriteStartObject(InfoKey);
                w.WriteString("name", _options.ClientName);
                w.WriteString("version", _options.ClientVersion);
                w.WriteEndObject();
                w.WriteStartObject(CapabilitiesKey);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    // ---- tools ----

    /// <summary>All tools of the server, following the paging cursor.</summary>
    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default)
    {
        var tools = new List<McpTool>();
        string? cursor = null;
        do
        {
            var result = await Guard(() => _rpc.RequestAsync("tools/list", Params(_modern, cursor is null ? null : w => w.WriteString("cursor", cursor)), ct: ct)).ConfigureAwait(false);
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("tools", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new McpClientException("The answer to tools/list has no list of tools.");
            foreach (var t in list.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
                tools.Add(new McpTool(name.GetString()!, Text(t, "title"), Text(t, "description"),
                    t.TryGetProperty("inputSchema", out var schema) ? schema.GetRawText() : "{}",
                    t.TryGetProperty("annotations", out var a) && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("readOnlyHint", out var ro) && ro.ValueKind == JsonValueKind.True));
            }
            cursor = result.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String && next.GetString() is { Length: > 0 } c ? c : null;
        }
        while (cursor is not null);
        return tools;
    }

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Calls a tool. Cancelling <paramref name="ct"/> stops the wait and tells the server with <c>notifications/cancelled</c>; the call then ends with <see cref="OperationCanceledException"/>.</summary>
    /// <param name="argumentsJson">A JSON object, or null for none.</param>
    public async Task<McpToolResult> CallToolAsync(string name, string? argumentsJson = null, CancellationToken ct = default)
    {
        var result = await Guard(() => _rpc.RequestAsync("tools/call", Params(_modern, w =>
        {
            w.WriteString("name", name);
            if (argumentsJson is not null)
            {
                using var args = JsonDocument.Parse(argumentsJson);
                w.WritePropertyName("arguments");
                args.RootElement.WriteTo(w);
            }
        }), ct: ct)).ConfigureAwait(false);
        var text = new StringBuilder();
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(part.ValueKind == JsonValueKind.Object && Text(part, "type") == "text" ? Text(part, "text") : "[" + (part.ValueKind == JsonValueKind.Object ? Text(part, "type") : null) + "]");
            }
        }
        bool failed = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;
        return new McpToolResult(failed, text.ToString(), result.GetRawText());
    }

    // ---- the process ----

    /// <summary>Turns "the connection is closed" into the report of how the server ended.</summary>
    private async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try { return await call().ConfigureAwait(false); }
        catch (RpcConnectionClosedException) { throw new McpServerExitedException(await _exit.Task.ConfigureAwait(false)); }
    }

    private async Task PumpStderrAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } raw)
            {
                var line = _launch.Mask(raw);
                lock (_tail)
                {
                    _tail.Enqueue(line);
                    while (_tail.Count > _options.StderrTailLines) _tail.Dequeue();
                }
                try { _onStderr?.Invoke(line); }
                catch (Exception) { /* a failing log sink must not stop the pump: the pipe would fill and block the server */ }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
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
            var exit = new McpServerExit(code, tail);
            _exit.TrySetResult(exit);
            if (!_disposing)
            {
                try { ServerExited?.Invoke(exit); }
                catch (Exception) { /* a handler that throws must not become an unobserved crash */ }
            }
        }
        catch (Exception e)
        {
            _exit.TrySetResult(new McpServerExit(null, e.Message));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposing = true;
        await _rpc.DisposeAsync().ConfigureAwait(false); // closes the server's input, which is its signal to leave
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

    /// <summary>What the server may ask of a client. Only <c>ping</c> is answered; sampling, roots and the rest are not offered because the client announces no capabilities.</summary>
    private sealed class Handler : IRpcHandler
    {
        public ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellation) =>
            method == "ping" ? new ValueTask<byte[]?>("{}"u8.ToArray()) : throw new RpcException(RpcErrorCodes.MethodNotFound, $"The client does not support '{method}'.");

        public ValueTask HandleNotificationAsync(string method, JsonElement parameters) => ValueTask.CompletedTask;
    }
}
