using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using NksHub.Textida.Acp;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.App.Services;

internal enum AgentState { None, Starting, Ready, Working, Ended }

internal enum AgentRole { You, Agent, Tool, Note, Problem }

/// <summary>One line of the conversation as it is now; <paramref name="Text"/> is already in the language of the user.</summary>
internal sealed record AgentLine(AgentRole Role, string Text, string? Status = null);

/// <summary>
/// An agent over ACP (P6.6): one agent process at a time, started on the user's request. It sees only the tabs the user shares with it, through the same permission model as the plugins
/// (client id <see cref="ClientId"/>): a share is a grant for one document, and stopping the agent takes every share back. What it writes with <c>fs/write_text_file</c> is a proposal in
/// the change review (the same one the proposals of plugins and of the MCP assistant use) and reaches the document only when the user applies it. The permission questions of the agent are dialogs.
/// The editor controls only what goes through it: the agent's own tools can write files by themselves, and no permission mode is set on the agent.
/// </summary>
internal sealed class AcpService : IAcpHost, IAsyncDisposable
{
    public const string ClientId = "acp";

    private sealed record Shared(MainWindow Window, DocTab Tab, OpenDocument Open, Document Document, bool ExposedByUs, Action Replaced);

    private sealed class Entry(AgentRole role, string text = "", Func<string>? dynamic = null)
    {
        public AgentRole Role { get; } = role;
        public StringBuilder Text { get; } = new(text);
        public Func<string>? Dynamic { get; } = dynamic;
        public string? ToolId { get; init; }
        public string? Title { get; set; }
        public string? Status { get; set; }
    }

    private readonly PluginServices _plugins;
    private readonly App _app;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Shared> _shared = [];
    private readonly List<Entry> _entries = [];
    private AcpClient? _client;
    private string? _session;
    private AcpAdapter? _adapter;
    private CancellationTokenSource? _turn;
    private bool _stopping;

    public AcpService(PluginServices plugins, App app)
    {
        _plugins = plugins;
        _app = app;
    }

    /// <summary>The window that shows the conversation, the permission questions and the review of the agent's changes.</summary>
    public MainWindow? HostWindow { get; private set; }
    private volatile AgentState _state;

    public AgentState State
    {
        get => _state;
        private set => _state = value;
    }
    public string AgentName { get; private set; } = "";

    /// <summary>The conversation or the state changed. Raised on any thread; a view posts to the UI thread.</summary>
    public event Action? Changed;

    public bool IsRunning => State is AgentState.Starting or AgentState.Ready or AgentState.Working;

    // ---- the conversation ----

    public IReadOnlyList<AgentLine> Lines()
    {
        lock (_gate)
            return [.. _entries.Select(e => new AgentLine(e.Role, e.Dynamic?.Invoke() ?? (e.Role == AgentRole.Tool ? e.Title ?? "" : e.Text.ToString()), e.Status))];
    }

    private void Add(AgentRole role, string text)
    {
        lock (_gate) _entries.Add(new Entry(role, text));
        Changed?.Invoke();
    }

    private void Add(AgentRole role, Func<string> text)
    {
        lock (_gate) _entries.Add(new Entry(role, "", text));
        Changed?.Invoke();
    }

    private void OnUpdate(AcpUpdate u)
    {
        lock (_gate)
        {
            switch (u.Kind)
            {
                case AcpUpdateKind.AgentMessage:
                    AppendMessage(u.Text);
                    break;
                case AcpUpdateKind.ToolCall:
                    _entries.Add(new Entry(AgentRole.Tool) { ToolId = u.ToolCallId, Title = u.Title ?? u.ToolKind ?? u.ToolCallId ?? "", Status = u.Status });
                    break;
                case AcpUpdateKind.ToolCallUpdate:
                    if (!UpdateTool(u.ToolCallId, u.Title, u.Status)) return;
                    break;
                default:
                    return; // thoughts, plans and the rest are not shown yet
            }
        }
        Changed?.Invoke();
    }

    /// <summary>A chunk of the agent's message goes on the line of the message before it, unless something else came in between. Call under the lock.</summary>
    private void AppendMessage(string? text)
    {
        if (_entries.Count > 0 && _entries[^1].Role == AgentRole.Agent) _entries[^1].Text.Append(text);
        else _entries.Add(new Entry(AgentRole.Agent, text ?? ""));
    }

    private bool UpdateTool(string? id, string? title, string? status)
    {
        var tool = _entries.LastOrDefault(e => e.Role == AgentRole.Tool && e.ToolId == id);
        if (tool is null) return false;
        if (title is not null) tool.Title = title;
        if (status is not null) tool.Status = status;
        return true;
    }

    // ---- a conversation a plugin hosts (P6.2.5) ----

    /// <summary>The plugin whose conversation the panel shows; null for an agent over ACP or none.</summary>
    private volatile string? _hostPlugin;

    public bool HasSession(string pluginId) => _hostPlugin == pluginId && IsRunning;

    /// <summary>Opens the panel's conversation for a plugin: the conversation of an earlier agent is cleared. False when an agent is running or another plugin holds the panel. Call on the UI thread.</summary>
    public bool OpenPluginSession(MainWindow window, string pluginId, string name)
    {
        if (IsRunning) return false;
        lock (_gate) _entries.Clear();
        HostWindow = window;
        AgentName = name;
        _adapter = null;
        _stopping = false;
        _hostPlugin = pluginId;
        State = AgentState.Ready;
        Add(AgentRole.Note, () => Loc.Get(K.AgentPluginOpened, ("agent", name)));
        return true;
    }

    /// <summary>Takes what the plugin sent into the conversation. A turn ends with <see cref="AgentUpdateKind.TurnEnd"/>. Call on the UI thread.</summary>
    public void UpdatePluginSession(string pluginId, AgentUpdate update)
    {
        if (!HasSession(pluginId)) return;
        lock (_gate)
        {
            switch (update.Kind)
            {
                case AgentUpdateKind.Message: AppendMessage(update.Text); break;
                case AgentUpdateKind.Tool: _entries.Add(new Entry(AgentRole.Tool) { ToolId = update.ToolId, Title = update.Text, Status = update.Status }); break;
                case AgentUpdateKind.ToolUpdate: UpdateTool(update.ToolId, update.Text, update.Status); break;
                case AgentUpdateKind.Note: _entries.Add(new Entry(AgentRole.Note, update.Text ?? "")); break;
                case AgentUpdateKind.Problem: _entries.Add(new Entry(AgentRole.Problem, update.Text ?? "")); break;
                case AgentUpdateKind.TurnEnd:
                    EndPluginTurn();
                    var reason = update.Text ?? "";
                    _entries.Add(new Entry(AgentRole.Note, "", () => Loc.Get(K.AgentTurnDone, ("reason", reason))));
                    break;
            }
        }
        Changed?.Invoke();
    }

    private void EndPluginTurn()
    {
        _turn?.Dispose();
        _turn = null;
        if (State == AgentState.Working) State = AgentState.Ready;
    }

    /// <summary>The plugin leaves, or the plugin was restarted or lost its permission. Any thread.</summary>
    public void ClosePluginSession(string pluginId)
    {
        if (!HasSession(pluginId)) return;
        _hostPlugin = null; // the plugin ended it itself, so it is not told
        if (Dispatcher.UIThread.CheckAccess()) _ = StopAsync();
        else Dispatcher.UIThread.Post(() => _ = StopAsync());
    }

    /// <summary>The permission dialog for a question of the plugin, the one the ACP agents use. Null when the user cancelled it or the turn was cancelled.</summary>
    public async Task<string?> AskPluginAsync(string tool, IReadOnlyList<AgentPermissionOption> options, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _turn?.Token ?? CancellationToken.None);
        var choice = await RequestPermissionAsync("", new AcpToolCallInfo(null, tool, null), [.. options.Select(o => new AcpPermissionOption(o.Id, o.Name, o.Reject ? "reject_once" : "allow_once"))], linked.Token);
        return choice.OptionId;
    }

    // ---- start and stop ----

    /// <summary>
    /// Starts the agent and opens a session in <c>launch.WorkingDirectory</c>; the conversation of an earlier agent is cleared. False, with the reason in the conversation, when it could not be started.
    /// The caller has already told the user what the agent will be (see the start command).
    /// </summary>
    public async Task<bool> StartAsync(MainWindow window, AcpLaunch launch, string name, AcpAdapter? adapter = null)
    {
        if (IsRunning) throw new InvalidOperationException("An agent is already running.");
        lock (_gate) _entries.Clear();
        HostWindow = window;
        AgentName = name;
        _adapter = adapter;
        _hostPlugin = null;
        _stopping = false;
        State = AgentState.Starting;
        Changed?.Invoke();
        try
        {
            var client = await AcpClient.StartAsync(launch, PluginLimitsPlaceholders.Acp(), this);
            _client = client;
            client.Updated += OnUpdate;
            client.AgentExited += OnExited;
            _session = await client.NewSessionAsync(launch.WorkingDirectory);
        }
        catch (AcpException e)
        {
            await DisposeClientAsync();
            State = AgentState.Ended;
            Add(AgentRole.Problem, () => Loc.Get(K.AgentStartFailed, ("agent", name), ("reason", e.Message)));
            return false;
        }
        State = AgentState.Ready;
        Add(AgentRole.Note, () => Loc.Get(K.AgentStarted, ("agent", name)));
        return true;
    }

    private void OnExited(AcpExit exit)
    {
        if (_stopping) return;
        State = AgentState.Ended;
        var reason = new AcpAgentExitedException(exit).Message;
        Add(AgentRole.Problem, () => Loc.Get(K.AgentEnded, ("reason", reason)));
        Dispatcher.UIThread.Post(UnshareAll);
    }

    /// <summary>Ends the agent (its input is closed, then the process is killed if it stays) and takes every shared tab back. Call on the UI thread.</summary>
    public async Task StopAsync()
    {
        if (State == AgentState.None) return;
        _stopping = true;
        _turn?.Cancel();
        UnshareAll();
        await DisposeClientAsync();
        bool wasRunning = IsRunning;
        State = AgentState.None;
        if (_hostPlugin is { } host)
        {
            _hostPlugin = null;
            EndPluginTurn();
            _plugins.Documents.AgentClosed(host, HostWindow is { IsVisible: false } ? "window" : "user");
        }
        if (wasRunning) Add(AgentRole.Note, () => Loc.Get(K.AgentStopped));
        else Changed?.Invoke();
    }

    private async Task DisposeClientAsync()
    {
        var client = _client;
        _client = null;
        _session = null;
        if (client is not null) await client.DisposeAsync();
    }

    // ---- a turn ----

    /// <summary>Sends the user's message and waits for the end of the turn. False when no agent is ready for it.</summary>
    public async Task<bool> SendAsync(string text)
    {
        if (State == AgentState.Ready && _hostPlugin is { } plugin)
        {
            State = AgentState.Working;
            _turn = new CancellationTokenSource();
            Add(AgentRole.You, text);
            _plugins.Documents.AgentUserMessage(plugin, text);
            return true;
        }
        if (State != AgentState.Ready || _client is not { } client || _session is not { } session) return false;
        State = AgentState.Working;
        using var turn = _turn = new CancellationTokenSource();
        Add(AgentRole.You, text);
        try
        {
            var reason = await client.PromptAsync(session, text, turn.Token);
            Add(AgentRole.Note, () => Loc.Get(K.AgentTurnDone, ("reason", reason)));
        }
        catch (AcpTimeoutException e)
        {
            Add(AgentRole.Problem, () => Loc.Get(K.AgentTimeout, ("reason", e.Message)));
            await StopAfterFailureAsync();
        }
        catch (AcpAgentExitedException) { /* reported by OnExited */ }
        catch (AcpException e)
        {
            Add(AgentRole.Problem, () => Loc.Get(K.AgentFailed, ("reason", e.Message)));
        }
        finally
        {
            _turn = null;
            if (State == AgentState.Working) State = AgentState.Ready;
            Changed?.Invoke();
        }
        return true;
    }

    private async Task StopAfterFailureAsync()
    {
        _stopping = true;
        await Dispatcher.UIThread.InvokeAsync(UnshareAll);
        await DisposeClientAsync();
        State = AgentState.Ended;
    }

    /// <summary>Cancels the running turn: the review of a change the agent proposed is closed (nothing is written), an open permission question is answered "cancelled" and the agent is told.</summary>
    public void CancelTurn()
    {
        if (_turn is not { } turn) return;
        turn.Cancel();
        if (_hostPlugin is { } plugin) _plugins.Documents.AgentCancelled(plugin);
    }

    public bool TurnRunning => _turn is not null;

    // ---- sharing ----

    public bool IsShared(DocTab tab)
    {
        lock (_gate) return _shared.ContainsKey(tab.Doc.Doc.Id);
    }

    public bool IsSharedDocument(Guid document)
    {
        lock (_gate) return _shared.ContainsKey(document);
    }

    /// <summary>Shows the tab to the agent: it can read it (secrets stay hidden) and propose changes. Call on the UI thread.</summary>
    public void Share(MainWindow window, DocTab tab)
    {
        var document = tab.Doc.Doc;
        if (IsShared(tab)) Unshare(document.Id);
        bool own = !_plugins.Documents.IsExposed(document.Id);
        if (own) _plugins.Documents.Expose(document, tab.Surface is { } surface ? surface.ByteSelections : null);
        var scope = GrantScope.OfDocument(document.Id);
        _plugins.Permissions.Grant(ClientId, DocumentPermissions.Read, scope);
        _plugins.Permissions.Grant(ClientId, DocumentPermissions.EditPropose, scope);
        Action replaced = () => Unshare(document.Id); // a reload makes a new document, which the user has not shared
        tab.Doc.Replaced += replaced;
        lock (_gate) _shared[document.Id] = new Shared(window, tab, tab.Doc, document, own, replaced);
    }

    /// <summary>Takes the tab back: the grants go (a request in flight is stopped) and the document is hidden again unless something else shows it. Call on the UI thread.</summary>
    public bool Unshare(Guid document)
    {
        Shared? shared;
        lock (_gate)
        {
            if (!_shared.Remove(document, out shared)) return false;
        }
        shared.Open.Replaced -= shared.Replaced;
        var scope = GrantScope.OfDocument(document);
        foreach (var permission in new[] { DocumentPermissions.EditPropose, DocumentPermissions.Read })
            _plugins.Permissions.Revoke(ClientId, permission, scope);
        if (shared.ExposedByUs && _plugins.Documents.IsExposed(document) && !_plugins.IsRunningOn(document) && !_plugins.Mcp.IsSharedDocument(document)) _plugins.Documents.Withdraw(shared.Document);
        return true;
    }

    private void UnshareAll()
    {
        Guid[] ids;
        lock (_gate) ids = [.. _shared.Keys];
        foreach (var id in ids) Unshare(id);
    }

    /// <summary>The document is going away (its last tab closed). Call on the UI thread, before it is disposed.</summary>
    public void Closed(Document document) => Unshare(document.Id);

    private Shared? FindShared(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        lock (_gate)
            return _shared.Values.FirstOrDefault(s => s.Document.FilePath is { } file && string.Equals(Path.GetFullPath(file), full, comparison));
    }

    private static AcpRefusedException NotShared() =>
        new(AcpRefusedException.NotFound, "The file is not open in the editor, or the user has not shared it with the agent.");

    // ---- fs/read_text_file ----

    private async Task<JsonElement> CallAsync(string method, Guid document, Action<Utf8JsonWriter>? more, CancellationToken cancellation)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("document", document);
            more?.Invoke(w);
            w.WriteEndObject();
        }
        using var request = JsonDocument.Parse(buffer.WrittenMemory);
        var answer = await _plugins.Documents.HandleAsync(ClientId, method, request.RootElement, cancellation).ConfigureAwait(false);
        using var parsed = JsonDocument.Parse(answer!);
        return parsed.RootElement.Clone();
    }

    public async ValueTask<string> ReadTextFileAsync(string sessionId, string path, int? line, int? limit, CancellationToken cancellation)
    {
        var shared = FindShared(path) ?? throw NotShared();
        var id = shared.Document.Id;
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                var info = await CallAsync(DocumentMethods.Info, id, null, cancellation).ConfigureAwait(false);
                long bom = info.GetProperty("bomLength").GetInt64(), length = info.GetProperty("byteLength").GetInt64(), revision = info.GetProperty("revision").GetInt64();
                if (length - bom > PluginLimitsPlaceholders.AcpMaxReadBytes)
                    throw new AcpRefusedException(AcpRefusedException.Declined, $"The file is larger than {PluginLimitsPlaceholders.AcpMaxReadBytes} bytes; the editor does not hand it to the agent.");
                try
                {
                    var read = await CallAsync(DocumentMethods.ReadText, id, w =>
                    {
                        w.WriteNumber("revision", revision);
                        w.WriteNumber("start", bom);
                        w.WriteNumber("end", length);
                        w.WriteBoolean("snap", true);
                    }, cancellation).ConfigureAwait(false);
                    return Slice(read.GetProperty("text").GetString()!, line, limit);
                }
                catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict && attempt < 3) { /* the user typed meanwhile: read again */ }
            }
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.PermissionDenied or DocumentErrorCodes.UnknownDocument) { throw NotShared(); }
    }

    /// <summary>Lines from <paramref name="line"/> (1-based), at most <paramref name="limit"/>, with their line ends.</summary>
    internal static string Slice(string text, int? line, int? limit)
    {
        if (line is null && limit is null) return text;
        static int Next(string t, int i)
        {
            int j = t.IndexOfAny(['\r', '\n'], i);
            if (j < 0) return t.Length;
            return j + (t[j] == '\r' && j + 1 < t.Length && t[j + 1] == '\n' ? 2 : 1);
        }
        int start = 0;
        for (int skip = (line ?? 1) - 1; skip > 0 && start < text.Length; skip--) start = Next(text, start);
        int end = start;
        if (limit is { } count) for (; count > 0 && end < text.Length; count--) end = Next(text, end);
        else end = text.Length;
        return text[start..end];
    }

    // ---- fs/write_text_file ----

    public async ValueTask WriteTextFileAsync(string sessionId, string path, string content, CancellationToken cancellation)
    {
        var shared = FindShared(path) ?? throw NotShared();
        if (!_plugins.Permissions.IsGranted(ClientId, DocumentPermissions.EditPropose, GrantScope.OfDocument(shared.Document.Id))) throw NotShared();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _turn?.Token ?? CancellationToken.None);
        var decision = await await Dispatcher.UIThread.InvokeAsync<Task<ProposalDecision?>>(() => ProposeAsync(shared, content, linked.Token));
        switch (decision?.Outcome)
        {
            case null or ProposalOutcome.Applied:
                return;
            case ProposalOutcome.Rejected:
                throw new AcpRefusedException(AcpRefusedException.Declined, "The user rejected the change.");
            case ProposalOutcome.Partial:
                throw new AcpRefusedException(AcpRefusedException.Declined, "The user applied only some of the changes. Read the file again.");
            case ProposalOutcome.Stale:
                throw new AcpRefusedException(AcpRefusedException.Declined, "The file changed in the editor before the user decided. Read it again and propose the change again.");
            default:
                throw new AcpRefusedException(AcpRefusedException.Declined, "The editor could not apply the change.");
        }
    }

    /// <summary>Builds the change from the agent's whole new content and shows it in the change review. Null when the content is the same as the text of the document. UI thread.</summary>
    private Task<ProposalDecision?> ProposeAsync(Shared shared, string content, CancellationToken cancellation)
    {
        var doc = shared.Document;
        if (SecretValues.IsSecretFile(doc.SecretsPath)) throw new AcpRefusedException(AcpRefusedException.Declined, "This file holds secret values that were hidden from the agent; it cannot be written through the editor.");
        var codec = doc.Codec;
        byte[] bytes;
        try { bytes = codec.Encode(content); }
        catch (UnrepresentableCharactersException) { throw new AcpRefusedException(RpcErrorCodes.InvalidParams, $"The text cannot be written in the encoding of the file ({codec.Name})."); }
        if (codec.Bom.Length > 0 && !bytes.AsSpan().StartsWith(codec.Bom)) bytes = [.. codec.Bom, .. bytes];

        var current = doc.GetSnapshot();
        var same = current.ByteLength == bytes.Length && current.ReadBytes(new ByteRange(0, current.ByteLength)).AsSpan().SequenceEqual(bytes);
        if (same) return Task.FromResult<ProposalDecision?>(null);
        var change = ExternalReview.BuildRevert(doc, bytes) ?? throw new AcpRefusedException(AcpRefusedException.Declined, "The change cannot be shown as hunks (bytes that are not valid text in the file's encoding).");
        return Review(shared, change, cancellation);

        async Task<ProposalDecision?> Review(Shared s, FileChange c, CancellationToken ct) =>
            await s.Window.ShowProposalAsync(Guid.NewGuid().ToString("N"), Loc.Get(K.AgentProposal, ("agent", AgentName), ("file", s.Tab.Title)), "acp:" + (_adapter?.Id ?? "agent"), s.Tab, c, ct);
    }

    // ---- session/request_permission ----

    public async ValueTask<AcpPermissionChoice> RequestPermissionAsync(string sessionId, AcpToolCallInfo toolCall, IReadOnlyList<AcpPermissionOption> options, CancellationToken cancellation)
    {
        var window = HostWindow;
        if (window is null || options.Count == 0) return AcpPermissionChoice.Cancelled;
        var tool = toolCall.Title ?? toolCall.Kind ?? toolCall.Id ?? "?";
        var firstReject = options.ToList().FindIndex(o => o.IsReject);
        var buttons = options.Select((o, i) => new DialogButton(o.Name, IsDefault: i == firstReject)).Append(new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)).ToList();
        var request = new ChoiceRequest(Loc.Get(K.AgentPermissionTitle, ("agent", AgentName)), Loc.Get(K.AgentPermissionMessage, ("agent", AgentName), ("tool", tool)), buttons);
        int answer = await await Dispatcher.UIThread.InvokeAsync<Task<int>>(() => _app.Dialogs.ChooseAsync(window, request, cancellation));
        return answer >= 0 && answer < options.Count ? new AcpPermissionChoice(options[answer].Id) : AcpPermissionChoice.Cancelled;
    }

    public async ValueTask DisposeAsync()
    {
        if (State != AgentState.None) await StopAsync();
    }
}
