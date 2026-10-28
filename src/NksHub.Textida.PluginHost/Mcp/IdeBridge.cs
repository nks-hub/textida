using System.Buffers;
using System.Diagnostics;
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

/// <summary>What a connected client of the bridge has: the tools only it gets, the documents it may reach, who it says it is, and a way to send it notifications and to cut it off.</summary>
internal sealed class McpPeer(IReadOnlyList<McpToolDefinition> extraTools, Func<Guid, bool>? scope, Action abort)
{
    private volatile McpSession? _session;

    public IReadOnlyList<McpToolDefinition> ExtraTools { get; } = extraTools;

    /// <summary>The documents this client may reach; null: every shared one.</summary>
    public Func<Guid, bool>? Scope { get; } = scope;

    /// <summary>What the client called itself in <c>initialize</c> (<c>clientInfo</c>); null until it did, and for a client that sent none.</summary>
    public string? ClientName { get; private set; }
    public string? ClientVersion { get; private set; }

    /// <summary>The client has said who it is.</summary>
    public event Action? Identified;

    /// <summary>The client has finished the handshake (<c>notifications/initialized</c>) and can take notifications.</summary>
    public event Action? Initialized;

    /// <summary>The client does something the protocol as the bridge knows it does not have. The argument says what.</summary>
    public event Action<string>? Incompatible;

    /// <summary>Tools the community description of the protocol lists and the bridge leaves out; calling one is an error for the agent but not a sign that the protocol changed.</summary>
    private static readonly string[] NotOffered = ["openFile", "getOpenEditors", "getWorkspaceFolders", "checkDocumentDirty", "saveDocument", "executeCode"];

    public void Attach(McpSession session) => _session = session;

    /// <summary>The text to answer a call of a tool the bridge does not have: a message for a tool that is documented but not offered; null for one nobody documents, which is reported as an incompatibility.</summary>
    public string? Decline(string tool)
    {
        if (NotOffered.Contains(tool)) return $"The editor does not offer {tool} through the /ide bridge.";
        ReportIncompatible($"the agent called the tool '{tool}', which the bridge does not know");
        return null;
    }

    public void ReportIncompatible(string reason) => Incompatible?.Invoke(reason);

    public void Identify(string? name, string? version)
    {
        ClientName = name;
        ClientVersion = version;
        Identified?.Invoke();
    }

    public void Ready() => Initialized?.Invoke();

    /// <summary>Cuts the connection; no closing handshake.</summary>
    public void Abort() => abort();

    /// <summary>Sends a notification, but only after the client has initialized; before that nothing is sent.</summary>
    public Task NotifyAsync(string method, byte[] payload) =>
        _session is { IsInitialized: true } session ? session.SendNotificationAsync(method, payload) : Task.CompletedTask;
}

/// <summary>An agent that is connected to the bridge, as it introduced itself. <paramref name="Name"/> is null when it did not say.</summary>
public sealed record IdeAgent(string? Name, string? Version);

/// <summary>A diff the agent asks the editor to show: the file it changes, the whole text it wants the file to have, and the name it gave the diff.</summary>
public sealed record IdeDiffRequest(string OldPath, string? NewPath, string NewContents, string TabName);

/// <summary>How the user decided a diff. <see cref="Saved"/>: all of it was applied. <see cref="Partial"/>: some hunks were applied, the rest rejected.</summary>
public enum IdeDiffOutcome { Saved, Rejected, Partial, Stale, Failed }

/// <summary>The editor cannot show this diff (the file is not open, not shared or cannot be written back); the message is for the agent.</summary>
public sealed class IdeDiffRefusedException(string message) : Exception(message);

/// <summary>What the bridge needs from the editor that it cannot decide itself. The numbers are owner decisions, so none of them has a default.</summary>
public sealed record IdeBridgeOptions
{
    public required McpServer Server { get; init; }
    /// <summary>Directory of the lock file (see <see cref="IdeBridge.DefaultLockDirectory"/>).</summary>
    public required string LockDirectory { get; init; }
    /// <summary>Longest time a connection gets to send its HTTP upgrade request before it is dropped.</summary>
    public required TimeSpan HandshakeTimeout { get; init; }
    /// <summary>Largest HTTP upgrade request, in bytes.</summary>
    public required int MaxHeaderBytes { get; init; }
    /// <summary>Longest name of an agent kept for display, in UTF-16 code units; a longer one is cut.</summary>
    public required int MaxAgentNameChars { get; init; }
    /// <summary>The folders the lock file lists. Claude Code offers the editor under <c>/ide</c> when its working directory is inside one of them.</summary>
    public IReadOnlyList<string> WorkspaceFolders { get; init; } = [];
    /// <summary>The documents this bridge may show to its agents (one window of the editor); null: every shared one.</summary>
    public Func<Guid, bool>? Scope { get; init; }
    /// <summary>
    /// Shows a diff in the change review and answers when the user has decided. It must return as soon as the token is cancelled (the diff was closed, the agent cancelled or went away),
    /// then with <see cref="IdeDiffOutcome.Rejected"/>. Null: the bridge refuses every <c>openDiff</c>.
    /// </summary>
    public Func<IdeDiffRequest, CancellationToken, Task<IdeDiffOutcome>>? ShowDiff { get; init; }
    /// <summary>Tells whether a process is running; used to tell a lock file of a crashed editor from one of a live editor. Null: ask the operating system.</summary>
    public Func<int, bool>? IsProcessAlive { get; init; }
}

/// <summary>
/// The editor as the IDE a CLI agent connects to with <c>/ide</c>. It is the MCP server of the editor (<see cref="McpServer"/>) on a second door: a WebSocket on the loopback
/// interface, found through a lock file. What the agent's public documentation says (https://code.claude.com/docs/en/ide-integrations, "The built-in IDE MCP server"):
/// the server binds to 127.0.0.1 on a random port in 10000-65535; the transport is unencrypted ws://; every activation makes a fresh random token and writes it to
/// <c>~/.claude/ide/&lt;port&gt;.lock</c> (<c>$CLAUDE_CONFIG_DIR/ide/</c> when that is set; mode 0600 in a 0700 directory); the CLI presents the token in the
/// <c>X-Claude-Code-Ide-Authorization</c> header; the CLI includes the selection and the path of the active file as context, opens diffs and reads diagnostics through it.
/// The official page names only <c>getDiagnostics</c> and <c>executeCode</c> and calls the rest internal RPC. The names and shapes of those calls are taken from the community
/// description of the protocol, reverse engineered from the VS Code extension (https://github.com/coder/claudecode.nvim, PROTOCOL.md): the fields of the lock file, the
/// <c>selection_changed</c> notification and the tools <c>openDiff</c>, <c>close_tab</c>, <c>closeAllDiffTabs</c>, <c>getCurrentSelection</c>, <c>getLatestSelection</c> and <c>getDiagnostics</c>.
/// That description is not a specification and no version of the CLI is confirmed against it (see <c>TODO.md</c>, P6.5.1). Left out: <c>openFile</c>, <c>getOpenEditors</c>,
/// <c>getWorkspaceFolders</c>, <c>checkDocumentDirty</c>, <c>saveDocument</c> (the bridge never saves), <c>executeCode</c> (notebooks) and the <c>at_mentioned</c> notification;
/// a call of one of those is answered with an error. A call of a tool that nobody documents, or of one of the bridge's own tools with arguments it cannot read, means the agent changed under the
/// bridge: <see cref="Incompatible"/> is raised.
/// </summary>
public sealed partial class IdeBridge : IAsyncDisposable
{
    public const string AuthorizationHeader = "X-Claude-Code-Ide-Authorization";
    public const int MinPort = 10000;
    public const int MaxPort = 65535;
    /// <summary>The name the editor gives in the lock file; the editor deletes a lock file of a dead process only when it carries this name.</summary>
    public const string IdeName = "Textida";
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
    private readonly McpToolDefinition[] _tools;
    private IReadOnlyList<string> _folders;

    /// <summary>Removes the lock files of crashed editors, binds the port and writes the lock file. Throws <see cref="IOException"/> when the port or the file cannot be had.</summary>
    public IdeBridge(IdeBridgeOptions options)
    {
        _options = options;
        _folders = options.WorkspaceFolders;
        _tools = BuildTools();
        AuthToken = Convert.ToHexStringLower(_token);
        RemoveStaleLocks();
        _listener = Bind(out var port);
        Port = port;
        LockPath = Path.Combine(options.LockDirectory, port + ".lock");
        try { WriteLock(); }
        catch { _listener.Stop(); throw; }
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        _accepting = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }
    public string AuthToken { get; }
    public string LockPath { get; }

    /// <summary>The address the bridge listens on; always the loopback interface.</summary>
    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    /// <summary><c>$CLAUDE_CONFIG_DIR/ide</c>, or <c>~/.claude/ide</c> when that variable is not set.</summary>
    public static string DefaultLockDirectory() =>
        Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir
            ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "ide");

    public int ConnectionCount
    {
        get { lock (_gate) return _peers.Count; }
    }

    /// <summary>The agents that are connected now.</summary>
    public IReadOnlyList<IdeAgent> Agents
    {
        get
        {
            lock (_gate) return [.. _peers.Select(p => new IdeAgent(Clean(p.ClientName), Clean(p.ClientVersion)))];
        }
    }

    /// <summary>An agent connected, said who it is, or went away. Raised on a thread of the bridge.</summary>
    public event Action? AgentsChanged;

    /// <summary>
    /// The agent called something the protocol as the bridge knows it does not have (a tool nobody documents, or a documented one with other arguments): the agent has changed under the bridge.
    /// Raised once, on a thread of the bridge, with the reason; the owner is expected to switch the bridge off and say so.
    /// </summary>
    public event Action<string>? Incompatible;

    private int _incompatibleReported;

    private void ReportIncompatible(string reason)
    {
        if (Interlocked.Exchange(ref _incompatibleReported, 1) == 1) return;
        try { Incompatible?.Invoke(reason); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { /* a listener that went away must not stop the bridge */ }
    }

    private string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = new string([.. text.Where(c => !char.IsControl(c))]).Trim();
        return clean.Length > _options.MaxAgentNameChars ? clean[.._options.MaxAgentNameChars] : clean;
    }

    /// <summary>Cuts every connected agent off. The port stays open, so the next <c>/ide</c> of the agent connects again. Returns how many were connected.</summary>
    public int DisconnectAgents()
    {
        McpPeer[] peers;
        lock (_gate) peers = [.. _peers];
        foreach (var peer in peers) peer.Abort();
        return peers.Length;
    }

    private void RaiseAgentsChanged()
    {
        try { AgentsChanged?.Invoke(); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { /* a listener that went away must not stop the bridge */ }
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

    /// <summary>
    /// Deletes the lock files that a crashed editor left behind: ours by name, written by a process that is no longer running. A lock file of another IDE, or of an editor
    /// that is still running, is never touched.
    /// </summary>
    private void RemoveStaleLocks()
    {
        var alive = _options.IsProcessAlive ?? ProcessIsAlive;
        string[] files;
        try { files = Directory.GetFiles(_options.LockDirectory, "*.lock"); }
        catch (Exception e) when (e is DirectoryNotFoundException or IOException or UnauthorizedAccessException) { return; }
        foreach (var file in files)
        {
            try
            {
                using var content = JsonDocument.Parse(File.ReadAllBytes(file));
                var root = content.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ideName", out var name) || name.GetString() != IdeName) continue;
                if (!root.TryGetProperty("pid", out var pid) || !pid.TryGetInt32(out var id) || id == Environment.ProcessId || alive(id)) continue;
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { /* not ours, or being used: leave it */ }
        }
    }

    private static bool ProcessIsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
    }

    private void WriteLock()
    {
        var dir = _options.LockDirectory;
        if (!Directory.Exists(dir))
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
            else Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // Fields as the community description of the protocol lists them (see the class remarks).
        var folders = _folders;
        var content = DocumentApi.Obj(w =>
        {
            w.WriteNumber("pid", Environment.ProcessId);
            w.WriteStartArray("workspaceFolders");
            foreach (var folder in folders) w.WriteStringValue(folder);
            w.WriteEndArray();
            w.WriteString("ideName", IdeName);
            w.WriteString("transport", "ws");
            w.WriteString("authToken", AuthToken);
        });
        if (OperatingSystem.IsWindows())
        {
            // Created empty, restricted to the current user, and only then filled, so the token is never readable by anyone else.
            if (!File.Exists(LockPath))
            {
                using (File.Create(LockPath)) { }
                RestrictToCurrentUser(LockPath);
            }
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

    /// <summary>Changes the folders the lock file lists (the token stays). Returns false when the file could not be written; the old content stays then.</summary>
    public bool UpdateWorkspaceFolders(IReadOnlyList<string> folders)
    {
        var before = _folders;
        if (before.SequenceEqual(folders, StringComparer.Ordinal)) return true;
        _folders = folders;
        try
        {
            WriteLock();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _folders = before;
            return false;
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

    /// <summary>The process ends without the bridge being disposed (an exit from code that does not wait for it): the lock file must not outlive the editor.</summary>
    private void OnProcessExit(object? sender, EventArgs e)
    {
        try { File.Delete(LockPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
        var peer = new McpPeer(_tools, _options.Scope, socket.Abort);
        peer.Identified += RaiseAgentsChanged;
        peer.Initialized += () => _ = PushActiveSelectionAsync(peer);
        peer.Incompatible += ReportIncompatible;
        lock (_gate) _peers.Add(peer);
        RaiseAgentsChanged();
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
            RaiseAgentsChanged();
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

    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested) return;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try { File.Delete(LockPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Task[] connections;
        lock (_gate) connections = [.. _connections];
        await Task.WhenAll(connections.Append(_accepting)).ConfigureAwait(false);
        _stop.Dispose();
    }
}
