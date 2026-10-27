using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Lsp;

/// <summary>Limits of the plugin; the editor passes them on the command line, so no number is fixed in the plugin.</summary>
/// <param name="MaxBytes">Largest file handed to a language server; a larger one is never read. Must not exceed what one read of the document API returns.</param>
/// <param name="StartTimeoutMs">Time the server has to answer <c>initialize</c>.</param>
/// <param name="RequestTimeoutMs">Time the server has to answer a definition or references request.</param>
/// <param name="StopTimeoutMs">Time the server gets to answer <c>shutdown</c> and to leave after <c>exit</c> before it is killed.</param>
/// <param name="MaxLocations">Places listed for one definition or references request.</param>
/// <param name="StderrLines">Last lines of the standard error of a server kept for the report of its end.</param>
/// <param name="MaxMessageChars">Longest diagnostic message or list label, in UTF-16 units; a longer one is cut.</param>
/// <param name="MaxDiagnostics">Diagnostics published per document; the rest is dropped.</param>
/// <param name="IdleStopMs">Time a server keeps running after the last document of its project was closed; 0 keeps it until the plugin ends.</param>
/// <param name="RootSearchLevels">Folders searched upwards for the project root of a document, the folder of the file being the first; 1 takes the folder of the file.</param>
public sealed record LspSettings(long MaxBytes, int StartTimeoutMs, int RequestTimeoutMs, int StopTimeoutMs, int MaxLocations, int StderrLines, int MaxMessageChars, int MaxDiagnostics, int IdleStopMs = 0, int RootSearchLevels = 1);

public sealed class ServerStartException(string message) : Exception(message);

/// <summary>
/// One running language server: the process, its JSON-RPC connection (LSP framing) and what it said it can do. A server that ends without being asked to
/// raises <see cref="Ended"/> once, with its exit code and the last lines of its standard error.
/// </summary>
public sealed class LspServer : IRpcHandler, IAsyncDisposable
{
    private readonly Process _process;
    private readonly JsonRpcDispatcher _rpc;
    private readonly LspSettings _settings;
    private readonly Queue<string> _stderr = new();
    private readonly Func<string, JsonElement, ValueTask> _onNotification;
    private readonly string _rootUri;
    private readonly string? _options;
    private int _stopping;
    private int _ended;

    /// <summary>Raised once when the server went away without a <see cref="StopAsync"/>; the text says how.</summary>
    public event Action<string>? Ended;

    /// <summary>The server wants text sent as ranges (<c>TextDocumentSyncKind.Incremental</c>) and not whole.</summary>
    public bool Incremental { get; private set; }
    public bool Definition { get; private set; }
    public bool References { get; private set; }
    public bool Completion { get; private set; }
    public bool Rename { get; private set; }
    public bool Alive => Volatile.Read(ref _ended) == 0 && Volatile.Read(ref _stopping) == 0 && !_rpc.IsClosed;

    private LspServer(Process process, Stream input, Stream output, LspSettings settings, string rootUri, string? options, Func<string, JsonElement, ValueTask> onNotification)
    {
        _process = process;
        _settings = settings;
        _rootUri = rootUri;
        _options = options;
        _onNotification = onNotification;
        // a document of the largest size, written as JSON, can be six times its text
        var limits = new RpcLimits((int)Math.Min(int.MaxValue / 2, Math.Max(4L << 20, settings.MaxBytes * 6 + (64 << 10))), 8, 16, 100_000, 64L << 20, 64L << 20);
        _rpc = new JsonRpcDispatcher(input, output, RpcProtocols.Lsp, limits, this);
        _ = DrainErrorsAsync();
        _ = WatchAsync();
    }

    /// <summary>Starts the program and performs the <c>initialize</c> handshake. The process is ended when the handshake fails.</summary>
    public static async Task<LspServer> StartAsync(ServerLaunch launch, string root, LspSettings settings, Func<string, JsonElement, ValueTask> onNotification, CancellationToken ct)
    {
        ProcessStartInfo info;
        try { info = ProcessLaunch.CreateStartInfo(launch.Program, launch.Arguments); }
        catch (ToolException e) { throw new ServerStartException(e.Message); }
        (info.RedirectStandardInput, info.RedirectStandardOutput, info.RedirectStandardError, info.UseShellExecute, info.CreateNoWindow, info.WorkingDirectory) = (true, true, true, false, true, root);
        foreach (var (name, value) in launch.Environment) info.Environment[name] = value;
        Process process;
        try { process = Process.Start(info) ?? throw new ServerStartException("The process did not start."); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { throw new ServerStartException(e.Message); }
        var rootUri = PathUri.FromPath(root);
        var server = new LspServer(process, process.StandardOutput.BaseStream, process.StandardInput.BaseStream, settings, rootUri, launch.InitializationOptions, onNotification);
        try
        {
            await server.InitializeAsync(root, launch.InitializationOptions, ct).ConfigureAwait(false);
            return server;
        }
        catch (Exception e) when (e is RpcException or RpcTimeoutException or IOException)
        {
            var reason = e is RpcTimeoutException ? "initialize timed out" : e.Message + server.StderrTail();
            await server.DisposeAsync().ConfigureAwait(false);
            throw new ServerStartException(reason);
        }
    }

    private async Task InitializeAsync(string root, string? initializationOptions, CancellationToken ct)
    {
        var json = new StringBuilder("{\"processId\":").Append(Environment.ProcessId)
            .Append(initializationOptions is null ? "" : ",\"initializationOptions\":" + initializationOptions)
            .Append(",\"clientInfo\":{\"name\":\"Textida\"},\"rootUri\":\"").Append(Encode(_rootUri)).Append("\",\"rootPath\":\"").Append(Encode(root))
            .Append("\",\"workspaceFolders\":[{\"uri\":\"").Append(Encode(_rootUri)).Append("\",\"name\":\"").Append(Encode(Path.GetFileName(root.TrimEnd('/', '\\')))).Append("\"}]")
            .Append(",\"capabilities\":{\"general\":{\"positionEncodings\":[\"utf-16\"]},\"textDocument\":{\"synchronization\":{\"dynamicRegistration\":false,\"didSave\":false},")
            .Append("\"definition\":{\"linkSupport\":true},\"references\":{},\"completion\":{\"completionItem\":{\"snippetSupport\":false}},\"rename\":{\"prepareSupport\":false},")
            .Append("\"publishDiagnostics\":{\"versionSupport\":true}},\"workspace\":{\"workspaceFolders\":true,\"configuration\":true,\"workspaceEdit\":{\"documentChanges\":true,\"resourceOperations\":[\"create\",\"rename\",\"delete\"]}}}}");
        var answer = await RequestAsync("initialize", json.ToString(), _settings.StartTimeoutMs, ct).ConfigureAwait(false);
        if (answer.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object)
        {
            if (caps.TryGetProperty("textDocumentSync", out var sync))
            {
                int kind = sync.ValueKind == JsonValueKind.Number ? sync.GetInt32()
                    : sync.ValueKind == JsonValueKind.Object && sync.TryGetProperty("change", out var change) && change.ValueKind == JsonValueKind.Number ? change.GetInt32() : 1;
                Incremental = kind == 2;
            }
            Definition = Provides(caps, "definitionProvider");
            References = Provides(caps, "referencesProvider");
            Completion = Provides(caps, "completionProvider");
            Rename = Provides(caps, "renameProvider");
        }
        await _rpc.NotifyAsync("initialized", "{}"u8.ToArray(), ct).ConfigureAwait(false);
    }

    private static bool Provides(JsonElement caps, string name) =>
        caps.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.Object;

    public static string Encode(string text) => JsonEncodedText.Encode(text).ToString();

    public async Task<JsonElement> RequestAsync(string method, string paramsJson, int timeoutMs, CancellationToken ct) =>
        await _rpc.RequestAsync(method, Encoding.UTF8.GetBytes(paramsJson), TimeSpan.FromMilliseconds(timeoutMs), ct).ConfigureAwait(false);

    public Task NotifyAsync(string method, string paramsJson) => _rpc.NotifyAsync(method, Encoding.UTF8.GetBytes(paramsJson));

    // ---- what the server sends to us ----

    /// <summary>Requests of the server. The ones a client must answer to let the server go on are answered with nothing; the rest are not known.</summary>
    ValueTask<byte[]?> IRpcHandler.HandleRequestAsync(string method, JsonElement p, CancellationToken ct)
    {
        switch (method)
        {
            case "client/registerCapability":
            case "client/unregisterCapability":
            case "window/workDoneProgress/create":
            case "window/showMessageRequest":
                return ValueTask.FromResult<byte[]?>(null);
            case "workspace/configuration":
                return ValueTask.FromResult<byte[]?>(Encoding.UTF8.GetBytes(Configuration(p)));
            case "workspace/workspaceFolders":
                return ValueTask.FromResult<byte[]?>(Encoding.UTF8.GetBytes("[{\"uri\":\"" + Encode(_rootUri) + "\",\"name\":\"root\"}]"));
            default:
                throw new RpcException(RpcErrorCodes.MethodNotFound, $"The client has no method '{method}'.");
        }
    }

    /// <summary>
    /// The answer to <c>workspace/configuration</c>: one value per requested item, the part of the <c>initializationOptions</c> its <c>section</c> names (a dot goes one object deeper; no section: all of them),
    /// null where the options have no such part.
    /// </summary>
    private string Configuration(JsonElement p)
    {
        if (!p.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return "[]";
        using var options = _options is null ? null : JsonDocument.Parse(_options);
        return "[" + string.Join(',', items.EnumerateArray().Select(item =>
            options is null ? "null" : SectionOf(options.RootElement,
                item.ValueKind == JsonValueKind.Object && item.TryGetProperty("section", out var section) && section.ValueKind == JsonValueKind.String ? section.GetString() : null))) + "]";
    }

    private static string SectionOf(JsonElement node, string? section)
    {
        if (!string.IsNullOrEmpty(section))
            foreach (var part in section.Split('.'))
                if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out node)) return "null";
        return node.GetRawText();
    }

    ValueTask IRpcHandler.HandleNotificationAsync(string method, JsonElement p) => _onNotification(method, p.Clone());

    // ---- the end of the process ----

    private async Task DrainErrorsAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                lock (_stderr)
                {
                    _stderr.Enqueue(line);
                    while (_stderr.Count > _settings.StderrLines) _stderr.Dequeue();
                }
        }
        catch (IOException) { }
    }

    private string StderrTail()
    {
        lock (_stderr) return _stderr.Count == 0 ? "" : ": " + string.Join(" | ", _stderr);
    }

    private async Task WatchAsync()
    {
        await _rpc.Completion.ConfigureAwait(false);
        if (Volatile.Read(ref _stopping) == 1 || Interlocked.Exchange(ref _ended, 1) == 1) return;
        string detail;
        try
        {
            using var wait = new CancellationTokenSource(_settings.StopTimeoutMs);
            await _process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            detail = $"exit code {_process.ExitCode}";
        }
        catch (OperationCanceledException)
        {
            detail = "the connection was lost";
            await KillAsync().ConfigureAwait(false);
        }
        Ended?.Invoke(detail + StderrTail());
    }

    /// <summary>Asks the server to shut down (<c>shutdown</c>, <c>exit</c>) and ends the process when it does not leave within the stop time.</summary>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 1) return;
        try
        {
            if (!_rpc.IsClosed)
            {
                await _rpc.RequestAsync("shutdown", default, TimeSpan.FromMilliseconds(_settings.StopTimeoutMs)).ConfigureAwait(false);
                await _rpc.NotifyAsync("exit").ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is RpcException or RpcTimeoutException or IOException) { /* a server that does not answer is ended below */ }
        try
        {
            using var wait = new CancellationTokenSource(_settings.StopTimeoutMs);
            await _process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { await KillAsync().ConfigureAwait(false); }
        await _rpc.DisposeAsync().ConfigureAwait(false);
    }

    private async Task KillAsync()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stopping, 1);
        await KillAsync().ConfigureAwait(false);
        await _rpc.DisposeAsync().ConfigureAwait(false);
        _process.Dispose();
    }
}

/// <summary>File paths and <c>file:</c> URIs, as language servers write them.</summary>
public static class PathUri
{
    public static string FromPath(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    /// <summary>The local path of a <c>file:</c> URI, or null for any other URI.</summary>
    public static string? ToPath(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
