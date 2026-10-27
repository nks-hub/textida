using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Mcp;

/// <summary>What a connected client of the bridge has: the tools only it gets, and a way to send it notifications.</summary>
internal sealed class McpPeer(IReadOnlyList<McpToolDefinition> extraTools)
{
    private volatile McpSession? _session;

    public IReadOnlyList<McpToolDefinition> ExtraTools { get; } = extraTools;

    public void Attach(McpSession session) => _session = session;

    /// <summary>Sends a notification, but only after the client has initialized; before that nothing is sent.</summary>
    public Task NotifyAsync(string method, byte[] payload) =>
        _session is { IsInitialized: true } session ? session.SendNotificationAsync(method, payload) : Task.CompletedTask;
}

/// <summary>What the bridge needs from the editor that it cannot decide itself. Both numbers are owner decisions, so neither has a default.</summary>
public sealed record IdeBridgeOptions
{
    public required McpServer Server { get; init; }
    /// <summary>Directory of the lock file (see <see cref="IdeBridge.DefaultLockDirectory"/>).</summary>
    public required string LockDirectory { get; init; }
    /// <summary>Longest time a connection gets to send its HTTP upgrade request before it is dropped.</summary>
    public required TimeSpan HandshakeTimeout { get; init; }
    /// <summary>Largest HTTP upgrade request, in bytes.</summary>
    public required int MaxHeaderBytes { get; init; }
}

/// <summary>
/// The editor as the IDE a CLI agent connects to with <c>/ide</c>. It is the MCP server of the editor (<see cref="McpServer"/>) on a second door: a WebSocket on the loopback
/// interface, found through a lock file. What the agent's public documentation says (https://code.claude.com/docs/en/ide-integrations, "The built-in IDE MCP server"):
/// the server binds to 127.0.0.1 on a random port in 10000-65535; the transport is unencrypted ws://; every activation makes a fresh random token and writes it to
/// <c>~/.claude/ide/&lt;port&gt;.lock</c> (<c>$CLAUDE_CONFIG_DIR/ide/</c> when that is set; mode 0600 in a 0700 directory); the CLI presents the token in the
/// <c>X-Claude-Code-Ide-Authorization</c> header. The page names <c>getDiagnostics</c> as a tool the model sees.
/// NOT documented anywhere readable, so left open: the field names inside the lock file, the WebSocket subprotocol, the name and shape of the selection notification,
/// the arguments and result of <c>openDiff</c> and of the other internal calls. Where this class has to put something there it is marked "provisional" and must be checked
/// against the pinned CLI version before anyone calls the bridge compatible.
/// Edits of the agent take the path of every other assistant: <c>editor_prepare_changes</c> puts a proposal in the change review and nothing is written without the user.
/// </summary>
public sealed class IdeBridge : IAsyncDisposable
{
    public const string AuthorizationHeader = "X-Claude-Code-Ide-Authorization";
    public const int MinPort = 10000;
    public const int MaxPort = 65535;
    /// <summary>Provisional: the notification that tells the agent where the selection is. Params: document, path, revision, start, end (bytes), text (masked like any read).</summary>
    public const string SelectionMethod = "textida/selection_changed";
    private const int PortAttempts = 50;
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly IdeBridgeOptions _options;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly List<McpPeer> _peers = [];
    private readonly List<Task> _connections = [];
    private readonly byte[] _token = RandomNumberGenerator.GetBytes(32);
    private readonly TcpListener _listener;
    private readonly Task _accepting;

    /// <summary>Binds the port and writes the lock file. Throws <see cref="IOException"/> when either cannot be done.</summary>
    public IdeBridge(IdeBridgeOptions options)
    {
        _options = options;
        AuthToken = Convert.ToHexStringLower(_token);
        _listener = Bind(out var port);
        Port = port;
        LockPath = Path.Combine(options.LockDirectory, port + ".lock");
        try { WriteLock(); }
        catch { _listener.Stop(); throw; }
        _accepting = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }
    public string AuthToken { get; }
    public string LockPath { get; }

    /// <summary><c>$CLAUDE_CONFIG_DIR/ide</c>, or <c>~/.claude/ide</c> when that variable is not set.</summary>
    public static string DefaultLockDirectory() =>
        Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir
            ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "ide");

    public int ConnectionCount
    {
        get { lock (_gate) return _peers.Count; }
    }

    private static TcpListener Bind(out int port)
    {
        for (int attempt = 0; ; attempt++)
        {
            port = RandomNumberGenerator.GetInt32(MinPort, MaxPort + 1);
            var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
            try
            {
                listener.Start();
                return listener;
            }
            catch (SocketException e)
            {
                listener.Stop();
                if (attempt + 1 >= PortAttempts) throw new IOException("No free port for the IDE bridge.", e);
            }
        }
    }

    // ---- lock file ----

    private void WriteLock()
    {
        var dir = _options.LockDirectory;
        if (!Directory.Exists(dir))
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
            else Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // Field names are provisional (see the class remarks); the token is the only one the page above requires to be in the file.
        var content = DocumentApi.Obj(w =>
        {
            w.WriteNumber("pid", Environment.ProcessId);
            w.WriteString("authToken", AuthToken);
            w.WriteString("ideName", "Textida");
            w.WriteString("transport", "ws");
        });
        if (OperatingSystem.IsWindows())
        {
            // Created empty, restricted to the current user, and only then filled, so the token is never readable by anyone else.
            using (File.Create(LockPath)) { }
            RestrictToCurrentUser(LockPath);
            File.WriteAllBytes(LockPath, content);
        }
        else
        {
            using var stream = new FileStream(LockPath, new FileStreamOptions
            {
                Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
            stream.Write(content);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUser(string path)
    {
        var info = new FileInfo(path);
        var security = new System.Security.AccessControl.FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User ?? throw new IOException("The current user is not known.");
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(user, System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    // ---- connections ----

    private async Task AcceptLoopAsync()
    {
        var ct = _stop.Token;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            var task = HandleAsync(client);
            lock (_gate)
            {
                _connections.RemoveAll(t => t.IsCompleted);
                _connections.Add(task);
            }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            WebSocket? socket;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(_options.HandshakeTimeout);
                socket = await UpgradeAsync(stream, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or InvalidDataException or SocketException or ObjectDisposedException) { return; }
            if (socket is null) return;
            try { await ServeSocketAsync(socket).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or OperationCanceledException or WebSocketException or ObjectDisposedException) { }
        }
    }

    private async Task<WebSocket?> UpgradeAsync(NetworkStream stream, CancellationToken ct)
    {
        var headers = await ReadHeadAsync(stream, ct).ConfigureAwait(false);
        if (headers is null) return await RefuseAsync(stream, "400 Bad Request", ct).ConfigureAwait(false);
        // A web page can reach a loopback port too: it sends an Origin and may use another Host name. Neither is accepted.
        if (headers.ContainsKey("Origin") || !HostIsLoopback(headers.GetValueOrDefault("Host")))
            return await RefuseAsync(stream, "403 Forbidden", ct).ConfigureAwait(false);
        if (!TokenMatches(headers.GetValueOrDefault(AuthorizationHeader))) return await RefuseAsync(stream, "401 Unauthorized", ct).ConfigureAwait(false);
        if (!headers.TryGetValue("Sec-WebSocket-Key", out var key) || !headers.GetValueOrDefault("Upgrade", "").Equals("websocket", StringComparison.OrdinalIgnoreCase))
            return await RefuseAsync(stream, "400 Bad Request", ct).ConfigureAwait(false);

        // The subprotocol is not documented; the first one the client offers is echoed back, as a server that does not care which one it is must.
        var subProtocol = headers.GetValueOrDefault("Sec-WebSocket-Protocol")?.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(subProtocol)) subProtocol = null;
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
        var response = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n"
            + (subProtocol is null ? "" : $"Sec-WebSocket-Protocol: {subProtocol}\r\n") + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct).ConfigureAwait(false);
        return WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, SubProtocol = subProtocol });
    }

    private bool TokenMatches(string? presented)
    {
        if (presented is null) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(AuthToken));
    }

    private bool HostIsLoopback(string? host) => host is not null && (host == $"127.0.0.1:{Port}" || host == $"localhost:{Port}" || host == $"[::1]:{Port}");

    private static async Task<WebSocket?> RefuseAsync(NetworkStream stream, string status, CancellationToken ct)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"), ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>Reads the request head byte by byte, so nothing after it is consumed; null when it is not a GET or is malformed.</summary>
    private async Task<Dictionary<string, string>?> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, ct).ConfigureAwait(false) == 0) throw new IOException("The client closed the connection.");
            bytes.Add(one[0]);
            if (bytes.Count > _options.MaxHeaderBytes) throw new InvalidDataException("The request head is too large.");
            int n = bytes.Count;
            if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n') break;
        }
        var lines = Encoding.ASCII.GetString([.. bytes]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("GET ", StringComparison.Ordinal)) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) return null;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return headers;
    }

    /// <summary>Joins the socket to the MCP server: one text message is one line of JSON-RPC each way.</summary>
    private async Task ServeSocketAsync(WebSocket socket)
    {
        var inbound = new Pipe();
        var outbound = new Pipe();
        var peer = new McpPeer([DiagnosticsTool]);
        lock (_gate) _peers.Add(peer);
        Task receive = Task.CompletedTask, send = Task.CompletedTask, serve = Task.CompletedTask;
        try
        {
            serve = _options.Server.ServeAsync(inbound.Reader.AsStream(), outbound.Writer.AsStream(), peer, _stop.Token);
            receive = ReceiveAsync(socket, inbound.Writer);
            send = SendAsync(socket, outbound.Reader);
            await Task.WhenAny(serve, receive).ConfigureAwait(false);
        }
        finally
        {
            socket.Abort();
            await outbound.Writer.CompleteAsync().ConfigureAwait(false);
            foreach (var task in new[] { serve, receive, send })
            {
                try { await task.ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or OperationCanceledException or WebSocketException or ObjectDisposedException) { }
            }
            lock (_gate) _peers.Remove(peer);
            socket.Dispose();
        }
    }

    private async Task ReceiveAsync(WebSocket socket, PipeWriter writer)
    {
        long limit = _options.Server.Options.Limits.MaxMessageBytes;
        var buffer = new byte[16 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                long size = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), _stop.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType != WebSocketMessageType.Text) { await CloseAsync(socket, WebSocketCloseStatus.InvalidMessageType); return; }
                    size += result.Count;
                    if (size > limit) { await CloseAsync(socket, WebSocketCloseStatus.MessageTooBig); return; }
                    // A raw line break inside a message can only be white space between JSON tokens; it must not split the line framing.
                    for (int i = 0; i < result.Count; i++) if (buffer[i] is (byte)'\n' or (byte)'\r') buffer[i] = (byte)' ';
                    await writer.WriteAsync(buffer.AsMemory(0, result.Count), _stop.Token).ConfigureAwait(false);
                }
                while (!result.EndOfMessage);
                await writer.WriteAsync("\n"u8.ToArray(), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException) { }
        finally { await writer.CompleteAsync().ConfigureAwait(false); }
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status)
    {
        try { await socket.CloseOutputAsync(status, null, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException) { }
    }

    private async Task SendAsync(WebSocket socket, PipeReader reader)
    {
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(_stop.Token).ConfigureAwait(false);
                var buffer = read.Buffer;
                while (buffer.PositionOf((byte)'\n') is { } end)
                {
                    var line = buffer.Slice(0, end);
                    if (line.Length > 0) await socket.SendAsync(line.ToArray(), WebSocketMessageType.Text, true, _stop.Token).ConfigureAwait(false);
                    buffer = buffer.Slice(buffer.GetPosition(1, end));
                }
                reader.AdvanceTo(buffer.Start, buffer.End);
                if (read.IsCompleted) return;
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException) { }
        finally { await reader.CompleteAsync().ConfigureAwait(false); }
    }

    // ---- tools of the bridge ----

    /// <summary>The tool the documentation says the model sees. There are no language servers yet, so the list is empty (P8 fills it). Result shape is provisional.</summary>
    private static readonly McpToolDefinition DiagnosticsTool = new("getDiagnostics", "Diagnostics",
        "Returns the errors and warnings of the language servers, for one file when 'uri' is given. Textida has no language server connected yet, so the list is empty.",
        "{\"type\":\"object\",\"properties\":{\"uri\":{\"type\":\"string\"}},\"additionalProperties\":false}", true,
        (_, _) => Task.FromResult("[]"));

    // ---- selection ----

    /// <summary>
    /// Tells every connected agent where the selection is in <paramref name="document"/>. The text is read as the assistant of the user (<see cref="McpServer.ClientId"/>), so a document
    /// the user has not shared, a withdrawn share and hidden <c>.env</c> values are handled by the same rules as any other read; for a document that is not shared nothing is sent.
    /// Returns whether a notification went out.
    /// </summary>
    public async Task<bool> PublishSelectionAsync(Guid document, long start, long end, CancellationToken cancellation = default)
    {
        McpPeer[] peers;
        lock (_gate) peers = [.. _peers];
        if (peers.Length == 0) return false;
        var context = new McpToolContext(_options.Server, cancellation);
        byte[] payload;
        try
        {
            var info = await context.CallAsync(DocumentMethods.Info, document).ConfigureAwait(false);
            long revision = info.GetProperty("revision").GetInt64();
            long length = info.GetProperty("byteLength").GetInt64();
            start = Math.Clamp(start, 0, length);
            end = Math.Clamp(end, start, Math.Min(length, start + _options.Server.Options.MaxRangeBytes));
            var read = await context.CallAsync(DocumentMethods.ReadText, document, w =>
            {
                w.WriteNumber("revision", revision);
                w.WriteNumber("start", start);
                w.WriteNumber("end", end);
                w.WriteBoolean("snap", true);
            }).ConfigureAwait(false);
            payload = DocumentApi.Obj(w =>
            {
                w.WriteString("document", document);
                w.WritePropertyName("path");
                info.GetProperty("path").WriteTo(w);
                w.WriteNumber("revision", revision);
                w.WriteNumber("start", read.GetProperty("start").GetInt64());
                w.WriteNumber("end", read.GetProperty("end").GetInt64());
                w.WriteString("text", read.GetProperty("text").GetString());
            });
        }
        catch (RpcException) { return false; } // not shared, withdrawn, gone or the text moved on: the agent learns nothing
        await Task.WhenAll(peers.Select(p => p.NotifyAsync(SelectionMethod, payload))).ConfigureAwait(false);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try { File.Delete(LockPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Task[] connections;
        lock (_gate) connections = [.. _connections];
        await Task.WhenAll(connections.Append(_accepting)).ConfigureAwait(false);
        _stop.Dispose();
    }
}
