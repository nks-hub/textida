using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Startup;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The editor as an MCP server for the assistant of the user. It is off until the user switches it on; then a local pipe (no network port) accepts the <c>textida-mcp</c> program.
/// The assistant sees only the tabs the user shares, through the same permission model as the plugins (client id <see cref="McpServer.ClientId"/>): a share is a grant for one
/// document, withdrawing it or switching the server off revokes the grants, which stops the request in flight. It never writes; its proposals are shown in the change preview.
/// </summary>
internal sealed class McpService : IProposalReviewer, IAsyncDisposable
{
    /// <param name="Withdrawn">Fires when the share ends; a proposal still waiting for the user is then withdrawn from the change review.</param>
    private sealed record Shared(MainWindow Window, DocTab Tab, OpenDocument Open, Document Document, bool ExposedByUs, Action Replaced, CancellationTokenSource Withdrawn);

    private readonly PluginServices _plugins;
    private readonly string _channel;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Shared> _shared = [];
    private IpcServer? _endpoint;

    public McpService(PluginServices plugins, string mainChannel, string profileDirectory)
    {
        _plugins = plugins;
        _channel = IpcEndpoint.McpChannel(mainChannel);
        Ledger = new ProposalLedger(PluginLimitsPlaceholders.McpTrackedProposals);
        Proposals = new McpProposals(Ledger, this);
        Clients = new McpClients(Path.Combine(profileDirectory, "mcp-clients.json"));
        Server = new McpServer(PluginLimitsPlaceholders.Mcp(plugins.Documents, Ledger, TitleOf, EnsureOutlineAsync) with { ClientGate = Clients.IsAllowed });
        Clients.Asked += name => Dispatcher.UIThread.Post(() => ClientAsked?.Invoke(name));
    }

    /// <summary>The permissions of the clients (who may use the editor, by the name they give).</summary>
    public McpClients Clients { get; }

    /// <summary>A client without a decision connected and sees nothing until the user decides. Raised on the UI thread.</summary>
    public event Action<string>? ClientAsked;

    /// <summary>The user decided about a client: its connections hear that the tools changed, and a client that is no longer allowed is disconnected.</summary>
    public void ClientDecided(string name, bool allowed)
    {
        Clients.Decide(name, allowed);
        if (!allowed) Server.DisconnectClient(name);
        else Server.ClientsChanged();
    }

    /// <summary>The user took back the decision about a client: it is disconnected and asked about again the next time it connects.</summary>
    public void ClientForgotten(string name)
    {
        Clients.Forget(name);
        Server.DisconnectClient(name);
    }

    public McpServer Server { get; }
    public ProposalLedger Ledger { get; }
    public McpProposals Proposals { get; }

    public bool Enabled => _endpoint is not null;

    /// <summary>The program an MCP client is told to start; next to the editor.</summary>
    public static string BridgeProgram => Path.Combine(ProgramFolders.Files, "mcp", "textida-mcp" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    // ---- on and off ----

    /// <summary>Starts listening on the local pipe. Throws <see cref="IOException"/> when the pipe cannot be made.</summary>
    public void Enable()
    {
        if (_endpoint is not null) return;
        _endpoint = IpcServer.ForStreams(_channel, (stream, ct) => Server.ServeAsync(stream, stream, ct));
    }

    /// <summary>Closes every connection, takes every shared tab back and stops listening. Call on the UI thread.</summary>
    public async Task DisableAsync()
    {
        var endpoint = _endpoint;
        _endpoint = null;
        await DisableIdeAsync();
        Server.CloseAll();
        foreach (var id in SharedIds()) Unshare(id);
        if (endpoint is not null) await endpoint.DisposeAsync();
    }

    // ---- the /ide bridge ----

    private readonly Dictionary<MainWindow, IdeBridge> _ide = [];

    /// <summary>An agent connected to the bridge of a window, said who it is or went away, or the bridge was switched off. Raised on the UI thread.</summary>
    public event Action<MainWindow>? IdeChanged;

    /// <summary>The agent of a window does something the bridge does not know, so the bridge of that window was switched off; the text says what. Raised on the UI thread, after the bridge is closed.</summary>
    public event Action<MainWindow, string>? IdeIncompatible;

    public bool IdeEnabled(MainWindow window) => IdeOf(window) is not null;

    /// <summary>The bridge of a window; every window has its own port, token and lock file. Null when the user has not switched it on there.</summary>
    public IdeBridge? IdeOf(MainWindow window)
    {
        lock (_gate) return _ide.GetValueOrDefault(window);
    }

    /// <summary>Where the lock file of the bridge goes; tests point it at a folder of their own.</summary>
    public string IdeLockDirectory { get; set; } = IdeBridge.DefaultLockDirectory();

    /// <summary>
    /// Opens the port of the bridge of <paramref name="window"/> and writes its lock file. The bridge serves the tabs of that window that are shared with MCP, so the MCP server must be on.
    /// Throws <see cref="IOException"/> when it cannot start. Call on the UI thread.
    /// </summary>
    public void EnableIde(MainWindow window)
    {
        if (IdeOf(window) is not null) return;
        if (!Enabled) throw new InvalidOperationException("The MCP server is off.");
        var bridge = new IdeBridge(PluginLimitsPlaceholders.Ide(Server, IdeLockDirectory) with
        {
            WorkspaceFolders = window.IdeWorkspaceFolders(),
            Scope = id => WindowOf(id) == window,
            ShowDiff = (request, cancellation) => ShowIdeDiffAsync(window, request, cancellation),
            Editors = async _ => await Dispatcher.UIThread.InvokeAsync(() => IdeEditorsOf(window)),
            OpenFile = async (request, _) => await Dispatcher.UIThread.InvokeAsync(() => window.IdeOpenFile(request)),
        });
        bridge.AgentsChanged += () => Dispatcher.UIThread.Post(() => IdeChanged?.Invoke(window));
        bridge.Incompatible += reason => Dispatcher.UIThread.Post(async () =>
        {
            if (IdeOf(window) != bridge) return;
            await DisableIdeAsync(window);
            IdeIncompatible?.Invoke(window, reason);
        });
        lock (_gate) _ide[window] = bridge;
        ActiveTabChanged(window, window.Active);
    }

    /// <summary>Closes the bridge of a window and deletes its lock file. Call on the UI thread.</summary>
    public async Task DisableIdeAsync(MainWindow window)
    {
        IdeBridge? bridge;
        lock (_gate) _ide.Remove(window, out bridge);
        if (bridge is null) return;
        await bridge.DisposeAsync();
        IdeChanged?.Invoke(window);
    }

    private async Task DisableIdeAsync()
    {
        MainWindow[] windows;
        lock (_gate) windows = [.. _ide.Keys];
        foreach (var window in windows) await DisableIdeAsync(window);
    }

    private MainWindow? WindowOf(Guid document)
    {
        lock (_gate) return _shared.TryGetValue(document, out var shared) ? shared.Window : null;
    }

    private IdeBridge? BridgeOf(Guid document)
    {
        lock (_gate) return _shared.TryGetValue(document, out var shared) ? _ide.GetValueOrDefault(shared.Window) : null;
    }

    /// <summary>The lock file of the window lists its folders again after a tab was shared or taken back in it. Call on the UI thread.</summary>
    private void RefreshIdeFolders(MainWindow window) => IdeOf(window)?.UpdateWorkspaceFolders(window.IdeWorkspaceFolders());

    /// <summary>The selection of a tab moved; a connected agent hears about it only when the tab is shared. Call on the UI thread.</summary>
    public void SelectionChanged(DocTab tab)
    {
        if (_ide.Count == 0 || BridgeOf(tab.Doc.Doc.Id) is not { } bridge || tab.Surface?.ByteSelections() is not { } selections) return; // the common case, on every caret move, costs one read
        var selection = selections.PrimarySelection;
        _ = PublishSelectionAsync(bridge, tab.Doc.Doc.Id, selection.Start, selection.End);
    }

    /// <summary>
    /// The user moved to another tab of <paramref name="window"/>. A shared tab becomes the one the agent hears about (its selection is the open file, as far as the agent is concerned);
    /// any other tab leaves the agent without an active editor. Call on the UI thread.
    /// </summary>
    public void ActiveTabChanged(MainWindow window, DocTab? tab)
    {
        if (IdeOf(window) is not { } bridge) return;
        if (tab is null || !IsShared(tab) || tab.Surface?.ByteSelections() is not { } selections)
        {
            bridge.ClearActive();
            return;
        }
        var selection = selections.PrimarySelection;
        _ = PublishSelectionAsync(bridge, tab.Doc.Doc.Id, selection.Start, selection.End);
    }

    private static async Task PublishSelectionAsync(IdeBridge ide, Guid document, long start, long end)
    {
        try { await ide.PublishSelectionAsync(document, start, end); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException) { /* the bridge was switched off meanwhile */ }
    }

    // ---- getOpenEditors ----

    /// <summary>The tabs of the window that are shared, in the order of the tab strip. UI thread.</summary>
    private IReadOnlyList<IdeEditor> IdeEditorsOf(MainWindow window)
    {
        HashSet<Guid> mine;
        lock (_gate) mine = [.. _shared.Where(p => p.Value.Window == window).Select(p => p.Key)];
        return [.. window.Tabs.Where(t => mine.Remove(t.Doc.Doc.Id)).Select(t => new IdeEditor(t.Doc.Doc.Id, t.Doc.Path, t.Title, t.Doc.Language.Id, t.Doc.HasUnsavedWork))]; // Remove: one entry per document
    }

    /// <summary>The user hands the agent of the window a tab as context: its file, and the lines of the selection when there is one. False when no agent is connected or the tab is not shared. Call on the UI thread.</summary>
    public Task<bool> MentionAsync(MainWindow window, DocTab tab)
    {
        if (IdeOf(window) is not { } bridge || !IsShared(tab)) return Task.FromResult(false);
        var selection = tab.Surface?.ByteSelections()?.PrimarySelection;
        return bridge.PublishAtMentionAsync(tab.Doc.Doc.Id, selection?.Start ?? 0, selection?.End ?? 0);
    }

    // ---- openDiff ----

    /// <summary>The agent asks to show a diff: the whole text it wants the file to have goes to the change review of the window, and the answer is the decision of the user.</summary>
    private async Task<IdeDiffOutcome> ShowIdeDiffAsync(MainWindow window, IdeDiffRequest request, CancellationToken cancellation)
    {
        var decision = await await Dispatcher.UIThread.InvokeAsync<Task<ProposalDecision?>>(() => ProposeIdeDiffAsync(window, request, cancellation));
        return decision?.Outcome switch
        {
            null or ProposalOutcome.Applied => IdeDiffOutcome.Saved,
            ProposalOutcome.Rejected => IdeDiffOutcome.Rejected,
            ProposalOutcome.Partial => IdeDiffOutcome.Partial,
            ProposalOutcome.Stale => IdeDiffOutcome.Stale,
            _ => IdeDiffOutcome.Failed,
        };
    }

    /// <summary>Builds the change from the agent's whole new text and shows it. Null when the text is the one the tab has. Only a tab of this window that the user shared for changes is touched. UI thread.</summary>
    private Task<ProposalDecision?> ProposeIdeDiffAsync(MainWindow window, IdeDiffRequest request, CancellationToken cancellation)
    {
        var shared = FindShared(window, request.OldPath) ?? (request.NewPath is { } other ? FindShared(window, other) : null);
        if (shared is null || !_plugins.Permissions.IsGranted(McpServer.ClientId, DocumentPermissions.EditPropose, GrantScope.OfDocument(shared.Document.Id)))
            throw new IdeDiffRefusedException("The file is not open in this window of the editor, or the user has not shared it with the agent.");
        var doc = shared.Document;
        if (SecretValues.IsSecretFile(doc.SecretsPath)) throw new IdeDiffRefusedException("This file holds secret values that were hidden from the agent; it cannot be changed through the editor.");
        var codec = doc.Codec;
        byte[] bytes;
        try { bytes = codec.Encode(request.NewContents); }
        catch (UnrepresentableCharactersException) { throw new IdeDiffRefusedException($"The text cannot be written in the encoding of the file ({codec.Name})."); }
        if (codec.Bom.Length > 0 && !bytes.AsSpan().StartsWith(codec.Bom)) bytes = [.. codec.Bom, .. bytes];

        var current = doc.GetSnapshot();
        if (current.ByteLength == bytes.Length && current.ReadBytes(new ByteRange(0, current.ByteLength)).AsSpan().SequenceEqual(bytes)) return Task.FromResult<ProposalDecision?>(null);
        var change = ExternalReview.BuildRevert(doc, bytes) ?? throw new IdeDiffRefusedException("The change cannot be shown as hunks (bytes that are not valid text in the file's encoding).");
        return Review(shared, change);

        async Task<ProposalDecision?> Review(Shared s, FileChange c) =>
            await s.Window.ShowProposalAsync(Guid.NewGuid().ToString("N"), Loc.Get(K.IdeProposal, ("file", s.Tab.Title)), "ide:claude", s.Tab, c, cancellation);
    }

    private Shared? FindShared(MainWindow window, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        lock (_gate)
            return _shared.Values.FirstOrDefault(s => s.Window == window && s.Document.FilePath is { } file && string.Equals(Path.GetFullPath(file), full, comparison));
    }

    // ---- sharing ----

    private Guid[] SharedIds()
    {
        lock (_gate) return [.. _shared.Keys];
    }

    public bool IsShared(DocTab tab)
    {
        lock (_gate) return _shared.ContainsKey(tab.Doc.Doc.Id);
    }

    public bool IsSharedDocument(Guid document)
    {
        lock (_gate) return _shared.ContainsKey(document);
    }

    /// <summary>Shows the tab to the assistant: it can read it and propose changes. <paramref name="revealSecrets"/> also lets it see the values of a secrets file. Call on the UI thread.</summary>
    public void Share(MainWindow window, DocTab tab, bool revealSecrets)
    {
        var document = tab.Doc.Doc;
        if (IsShared(tab)) Unshare(document.Id);
        bool own = !_plugins.Documents.IsExposed(document.Id);
        if (own) _plugins.Documents.Expose(document, tab.Surface is { } surface ? surface.ByteSelections : null);
        var scope = GrantScope.OfDocument(document.Id);
        _plugins.Permissions.Grant(McpServer.ClientId, DocumentPermissions.Read, scope);
        _plugins.Permissions.Grant(McpServer.ClientId, DocumentPermissions.EditPropose, scope);
        if (revealSecrets) _plugins.Permissions.Grant(McpServer.ClientId, DocumentPermissions.SecretsReveal, scope);
        Action replaced = () => Unshare(document.Id); // a reload makes a new document, which the user has not shared
        tab.Doc.Replaced += replaced;
        lock (_gate) _shared[document.Id] = new Shared(window, tab, tab.Doc, document, own, replaced, new CancellationTokenSource());
        RefreshIdeFolders(window);
        if (window.Active == tab) ActiveTabChanged(window, tab);
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
        foreach (var permission in new[] { DocumentPermissions.SecretsReveal, DocumentPermissions.EditPropose, DocumentPermissions.Read })
            _plugins.Permissions.Revoke(McpServer.ClientId, permission, scope);
        shared.Withdrawn.Cancel(); // not disposed: a review that is just starting may still read its token
        if (shared.ExposedByUs && _plugins.Documents.IsExposed(document) && !_plugins.IsRunningOn(document) && !_plugins.Acp.IsSharedDocument(document)) _plugins.Documents.Withdraw(shared.Document);
        RefreshIdeFolders(shared.Window);
        return true;
    }

    /// <summary>The document is going away (its last tab closed). Call on the UI thread, before it is disposed.</summary>
    public void Closed(Document document) => Unshare(document.Id);

    // ---- hooks of the server ----

    private string? TitleOf(Guid document)
    {
        lock (_gate) return _shared.TryGetValue(document, out var shared) ? shared.Open.Title : null;
    }

    public LogLevelRules? LogRulesOf(Guid document)
    {
        lock (_gate) return _shared.TryGetValue(document, out var shared) ? shared.Open.LogLevels : null;
    }

    private async Task EnsureOutlineAsync(Guid document, CancellationToken cancellation)
    {
        Shared? shared;
        lock (_gate) _shared.TryGetValue(document, out shared);
        if (shared is null) return;
        await Dispatcher.UIThread.InvokeAsync(() => _plugins.IndexSymbolsAsync(shared.Window, shared.Tab, reveal: false)).WaitAsync(cancellation);
    }

    /// <summary>
    /// The user sees the proposal in the change review of the window that shows the tab, the same one the proposals of plugins use, and decides; a partly accepted one counts as applied.
    /// Taking the tab back or switching the server off withdraws a proposal that is still waiting: nothing is written and it counts as rejected.
    /// </summary>
    public async ValueTask<ReviewOutcome> ReviewAsync(EditProposal proposal, CancellationToken cancellation)
    {
        Shared? shared;
        lock (_gate) _shared.TryGetValue(proposal.DocumentId, out shared);
        if (shared is null) return new ReviewOutcome(ProposalStatus.Stale);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, shared.Withdrawn.Token);
        var decision = await Dispatcher.UIThread.InvokeAsync(() => shared.Window.ReviewPluginProposalAsync(proposal, shared.Tab, Guid.NewGuid().ToString("N"), linked.Token));
        return decision.Outcome switch
        {
            ProposalOutcome.Applied or ProposalOutcome.Partial => new ReviewOutcome(ProposalStatus.Applied, await Dispatcher.UIThread.InvokeAsync(() => shared.Tab.Doc.Doc.Revision)),
            ProposalOutcome.Rejected => new ReviewOutcome(ProposalStatus.Rejected),
            ProposalOutcome.Stale => new ReviewOutcome(ProposalStatus.Stale),
            _ => new ReviewOutcome(ProposalStatus.Failed),
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_endpoint is not null) await DisableAsync();
    }
}
