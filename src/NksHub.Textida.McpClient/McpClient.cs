using System.Buffers;
using System.Diagnostics;
using System.Globalization;
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
/// <param name="CallTimeout">Longest wait for the answer to a list, a tool call or a read of a resource; a server that is slower is told to stop (<c>notifications/cancelled</c>) and the call ends with <see cref="McpTimeoutException"/>.</param>
/// <param name="MaxResultChars">Most characters of text a tool result or a resource keeps; the rest is cut off and the result says so.</param>
public sealed record McpClientOptions(RpcLimits Limits, TimeSpan HandshakeTimeout, TimeSpan ExitGrace, int StderrTailLines, string ClientName, string ClientVersion, TimeSpan CallTimeout, int MaxResultChars);

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

/// <summary>A server reached by an address (Streamable HTTP): the address, the headers sent with every message and the values that must never be shown.</summary>
public sealed record McpRemote(Uri Url, IReadOnlyDictionary<string, string> Headers, IReadOnlyList<string> SecretValues)
{
    /// <summary>The address for the user's eyes: password and the values of the query string hidden.</summary>
    public string MaskedUrl => McpText.MaskUrl(Url.OriginalString);

    /// <summary>One line per header, <c>Name: ***</c>.</summary>
    public IReadOnlyList<string> MaskedHeaders => [.. Headers.Select(h => h.Key + ": " + McpText.MaskValue(h.Value))];

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

/// <summary>The answer to a tool call: the text parts joined (cut to <see cref="McpClientOptions.MaxResultChars"/>; <paramref name="FullLength"/> is the length before the cut), and whether the server says it failed.</summary>
public sealed record McpToolResult(bool IsError, string Text, string RawJson, int FullLength)
{
    public bool Truncated => Text.Length < FullLength;
}

/// <summary>A resource the server offers, by the address it names it with.</summary>
public sealed record McpResource(string Uri, string Name, string? Title, string? Description, string? MimeType);

/// <summary>The content of a resource as text (cut like <see cref="McpToolResult.Text"/>); binary parts appear as a line that names their size.</summary>
public sealed record McpResourceContent(string Text, int FullLength, string? MimeType)
{
    public bool Truncated => Text.Length < FullLength;
}

/// <summary>How a server process ended: its exit code (null while it was still running when the wait ran out) and the last lines of its standard error, secrets masked.</summary>
public sealed record McpServerExit(int? ExitCode, string StderrTail);

/// <summary>The server cannot be started, speaks neither protocol era or answers something that is not MCP.</summary>
public class McpClientException(string message) : Exception(message);

/// <summary>The server did not answer in time; it was told to stop the request.</summary>
public sealed class McpTimeoutException(TimeSpan timeout) : McpClientException(string.Create(CultureInfo.InvariantCulture, $"The server did not answer within {timeout.TotalSeconds:0.#} s."))
{
    public TimeSpan Timeout { get; } = timeout;
}

/// <summary>The server at an address refused the key it was sent (HTTP 401 or 403), so the caller can drop it and ask for a new one.</summary>
public sealed class McpAuthException(string message) : McpClientException(message);

/// <summary>The server process ended (or closed its output) while the client was waiting for it. Reported to the caller; it never takes the editor down.</summary>
public sealed class McpServerExitedException(McpServerExit exit)
    : McpClientException("The MCP server ended" + (exit.ExitCode is { } c ? $" (exit code {c})" : "") + (exit.StderrTail.Length > 0 ? ": " + exit.StderrTail.Split('\n')[^1].Trim() : "."))
{
    public McpServerExit Exit { get; } = exit;
}

/// <summary>
/// An MCP client for a server it starts itself (stdio transport) or reaches by an address (Streamable HTTP, <see cref="StartRemoteAsync"/>). A started server is first asked
/// <c>server/discover</c> with the per-request version of 2026-07-28; one that does not know it is greeted with <c>initialize</c> of 2025-11-25, and a server at an address always is.
/// Calls can be cancelled, which sends <c>notifications/cancelled</c> (the JSON-RPC layer does it); a call that takes longer than <see cref="McpClientOptions.CallTimeout"/> ends
/// with <see cref="McpTimeoutException"/> in the same way. A server that dies is reported as <see cref="McpServerExitedException"/> and through <see cref="ServerExited"/>.
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
    public const string ModernVersion = "2026-07-28";
    public const string LegacyVersion = "2025-11-25";
    private const string VersionKey = "io.modelcontextprotocol/protocolVersion";
    private const string InfoKey = "io.modelcontextprotocol/clientInfo";
    private const string CapabilitiesKey = "io.modelcontextprotocol/clientCapabilities";

    private readonly McpClientOptions _options;
    private readonly Func<string, string> _mask;
    private readonly Process? _process;
    private readonly McpHttpTransport? _http;
    private readonly Queue<string> _tail = new();
    private readonly TaskCompletionSource<McpServerExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<string>? _onStderr;
    private JsonRpcDispatcher _rpc = null!;
    private Task _stderrPump = Task.CompletedTask, _watch = Task.CompletedTask;
    private volatile bool _disposing;
    private bool _modern;

    private McpClient(Func<string, string> mask, McpClientOptions options, Process? process, McpHttpTransport? http, Action<string>? onStderr)
    {
        _mask = mask;
        _options = options;
        _process = process;
        _http = http;
        _onStderr = onStderr;
    }

    /// <summary>The protocol era in use: <see cref="ModernVersion"/> or <see cref="LegacyVersion"/>.</summary>
    public string ProtocolVersion => _modern ? ModernVersion : LegacyVersion;
    public string? ServerName { get; private set; }
    public string? ServerVersion { get; private set; }
    public string? Instructions { get; private set; }

    /// <summary>Whether the server says it offers resources; without it <see cref="ListResourcesAsync"/> asks nothing.</summary>
    public bool HasResources { get; private set; }

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
        var client = new McpClient(launch.Mask, options, process, null, onStderr);
        client._stderrPump = Task.Run(client.PumpStderrAsync);
        client._rpc = new JsonRpcDispatcher(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, RpcProtocols.Mcp, options.Limits, new Handler());
        client._watch = client.WatchAsync();
        return await client.FinishStartAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects to a server at an address (Streamable HTTP) and completes the handshake of <see cref="LegacyVersion"/>; the client does not ask such a server for <c>server/discover</c>.
    /// <paramref name="http"/> carries the proxy of the editor and must not time out by itself (the timeouts of <paramref name="options"/> decide). The client disposes it.
    /// A key the server refuses (HTTP 401 or 403) is <see cref="McpAuthException"/>.
    /// </summary>
    public static async Task<McpClient> StartRemoteAsync(McpRemote remote, McpClientOptions options, HttpClient http, CancellationToken ct = default)
    {
        var transport = new McpHttpTransport(remote, http, options.Limits.MaxMessageBytes, options.ExitGrace);
        var client = new McpClient(remote.Mask, options, null, transport, null);
        client._rpc = new JsonRpcDispatcher(transport.Input, transport.Output, RpcProtocols.Mcp, options.Limits, new Handler());
        client._watch = client.WatchAsync();
        return await client.FinishStartAsync(ct).ConfigureAwait(false);
    }

    private async Task<McpClient> FinishStartAsync(CancellationToken ct)
    {
        try
        {
            await HandshakeAsync(ct).ConfigureAwait(false);
            return this;
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // ---- handshake ----

    private async Task HandshakeAsync(CancellationToken ct)
    {
        JsonElement discovered = default;
        bool modern = false;
        if (_http is null)
        {
            try
            {
                discovered = await Guard(() => _rpc.RequestAsync("server/discover", Params(true, null), _options.HandshakeTimeout, ct), _options.HandshakeTimeout).ConfigureAwait(false);
                modern = discovered.ValueKind == JsonValueKind.Object && discovered.TryGetProperty("supportedVersions", out var versions) && versions.ValueKind == JsonValueKind.Array
                    && versions.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == ModernVersion);
            }
            catch (RpcException) { /* an older server does not know server/discover: the handshake below is for it */ }
        }

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
        }), _options.HandshakeTimeout, ct), _options.HandshakeTimeout).ConfigureAwait(false);
        var chosen = init.ValueKind == JsonValueKind.Object && init.TryGetProperty("protocolVersion", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (chosen != LegacyVersion)
            throw new McpClientException(_http is null
                ? $"The server speaks MCP version {chosen ?? "(not stated)"}; this client speaks {ModernVersion} and {LegacyVersion}."
                : $"The server speaks MCP version {chosen ?? "(not stated)"}; this client speaks {LegacyVersion} over an address.");
        ReadServerInfo(init);
        if (_http is not null) _http.ProtocolVersion = LegacyVersion;
        await Guard(async () => { await _rpc.NotifyAsync("notifications/initialized", ct: ct).ConfigureAwait(false); return 0; }, _options.HandshakeTimeout).ConfigureAwait(false);
    }

    private void ReadServerInfo(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return;
        if (result.TryGetProperty("instructions", out var i) && i.ValueKind == JsonValueKind.String) Instructions = i.GetString();
        HasResources = result.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Object && capabilities.TryGetProperty("resources", out var resources) && resources.ValueKind == JsonValueKind.Object;
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

    // ---- tools and resources ----

    /// <summary>All tools of the server, following the paging cursor.</summary>
    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default)
    {
        var tools = new List<McpTool>();
        await foreach (var page in PagesAsync("tools/list", "tools", ct).ConfigureAwait(false))
        {
            foreach (var t in page)
            {
                if (t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
                tools.Add(new McpTool(name.GetString()!, Text(t, "title"), Text(t, "description"),
                    t.TryGetProperty("inputSchema", out var schema) ? schema.GetRawText() : "{}",
                    t.TryGetProperty("annotations", out var a) && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("readOnlyHint", out var ro) && ro.ValueKind == JsonValueKind.True));
            }
        }
        return tools;
    }

    /// <summary>All resources of the server (none, without asking, when it does not say it has any), following the paging cursor.</summary>
    public async Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken ct = default)
    {
        var resources = new List<McpResource>();
        if (!HasResources) return resources;
        await foreach (var page in PagesAsync("resources/list", "resources", ct).ConfigureAwait(false))
        {
            foreach (var r in page)
            {
                if (r.ValueKind != JsonValueKind.Object || Text(r, "uri") is not { Length: > 0 } uri) continue;
                resources.Add(new McpResource(uri, Text(r, "name") ?? uri, Text(r, "title"), Text(r, "description"), Text(r, "mimeType")));
            }
        }
        return resources;
    }

    /// <summary>The items of every page of a list request, in order.</summary>
    private async IAsyncEnumerable<List<JsonElement>> PagesAsync(string method, string member, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string? cursor = null;
        do
        {
            var result = await Guard(() => _rpc.RequestAsync(method, Params(_modern, cursor is null ? null : w => w.WriteString("cursor", cursor)), _options.CallTimeout, ct), _options.CallTimeout).ConfigureAwait(false);
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(member, out var list) || list.ValueKind != JsonValueKind.Array)
                throw new McpClientException($"The answer to {method} has no list of {member}.");
            yield return [.. list.EnumerateArray()];
            cursor = result.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String && next.GetString() is { Length: > 0 } c ? c : null;
        }
        while (cursor is not null);
    }

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The text cut to <see cref="McpClientOptions.MaxResultChars"/> (never between the two halves of a surrogate pair).</summary>
    private string Limit(string text)
    {
        int max = _options.MaxResultChars;
        if (text.Length <= max) return text;
        return text[..(max > 0 && char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)];
    }

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
        }), _options.CallTimeout, ct), _options.CallTimeout).ConfigureAwait(false);
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
        return new McpToolResult(failed, Limit(text.ToString()), result.GetRawText(), text.Length);
    }

    /// <summary>Reads a resource. Text parts are joined; a binary part is shown as one line with its type and size.</summary>
    public async Task<McpResourceContent> ReadResourceAsync(string uri, CancellationToken ct = default)
    {
        var result = await Guard(() => _rpc.RequestAsync("resources/read", Params(_modern, w => w.WriteString("uri", uri)), _options.CallTimeout, ct), _options.CallTimeout).ConfigureAwait(false);
        var text = new StringBuilder();
        string? mime = null;
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in contents.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object) continue;
                mime ??= Text(part, "mimeType");
                if (text.Length > 0) text.Append('\n');
                if (Text(part, "text") is { } body) text.Append(body);
                else if (Text(part, "blob") is { } blob) text.Append($"[binary data, {Text(part, "mimeType") ?? "type not stated"}, about {blob.Length / 4 * 3} bytes]");
            }
        }
        return new McpResourceContent(Limit(text.ToString()), text.Length, mime);
    }

    // ---- the process ----

    /// <summary>Turns "the connection is closed" into the report of how the server ended, a refused key and a transport failure into their own exceptions, and an elapsed deadline into <see cref="McpTimeoutException"/>.</summary>
    private async Task<T> Guard<T>(Func<Task<T>> call, TimeSpan timeout)
    {
        try { return await call().ConfigureAwait(false); }
        catch (RpcConnectionClosedException) { throw new McpServerExitedException(await _exit.Task.ConfigureAwait(false)); }
        catch (RpcTimeoutException) { throw new McpTimeoutException(timeout); }
        catch (RpcException e) when (e.Code == McpHttpTransport.AuthErrorCode) { throw new McpAuthException(e.Message); }
        catch (RpcException e) when (e.Code == McpHttpTransport.FailedErrorCode) { throw new McpClientException(e.Message); }
    }

    private async Task PumpStderrAsync()
    {
        try
        {
            while (await _process!.StandardError.ReadLineAsync().ConfigureAwait(false) is { } raw)
            {
                var line = _mask(raw);
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
            await (_process is null ? _rpc.Completion : Task.WhenAny(_rpc.Completion, _process.WaitForExitAsync())).ConfigureAwait(false);
            int? code = null;
            if (_process is not null)
            {
                try
                {
                    await _process.WaitForExitAsync().WaitAsync(_options.ExitGrace).ConfigureAwait(false);
                    code = _process.ExitCode;
                }
                catch (TimeoutException) { }
            }
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
        if (_process is not null)
        {
            try { await _process.WaitForExitAsync().WaitAsync(_options.ExitGrace).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { /* ended by itself in the meantime */ }
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        if (_http is not null) await _http.DisposeAsync().ConfigureAwait(false);
        try { await _watch.WaitAsync(_options.ExitGrace + _options.ExitGrace).ConfigureAwait(false); }
        catch (TimeoutException) { }
        _process?.Dispose();
    }

    /// <summary>What the server may ask of a client. Only <c>ping</c> is answered; sampling, roots and the rest are not offered because the client announces no capabilities.</summary>
    private sealed class Handler : IRpcHandler
    {
        public ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellation) =>
            method == "ping" ? new ValueTask<byte[]?>("{}"u8.ToArray()) : throw new RpcException(RpcErrorCodes.MethodNotFound, $"The client does not support '{method}'.");

        public ValueTask HandleNotificationAsync(string method, JsonElement parameters) => ValueTask.CompletedTask;
    }
}
