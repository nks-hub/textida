using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Startup;
using NksHub.Textida.Core.Documents;
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
    private sealed record Shared(MainWindow Window, DocTab Tab, OpenDocument Open, Document Document, bool ExposedByUs, Action Replaced);

    private readonly PluginServices _plugins;
    private readonly string _channel;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Shared> _shared = [];
    private IpcServer? _endpoint;

    public McpService(PluginServices plugins, string mainChannel)
    {
        _plugins = plugins;
        _channel = IpcEndpoint.McpChannel(mainChannel);
        Ledger = new ProposalLedger(PluginLimitsPlaceholders.McpTrackedProposals);
        Proposals = new McpProposals(Ledger, this);
        Server = new McpServer(PluginLimitsPlaceholders.Mcp(plugins.Documents, Ledger, TitleOf, EnsureOutlineAsync));
    }

    public McpServer Server { get; }
    public ProposalLedger Ledger { get; }
    public McpProposals Proposals { get; }

    public bool Enabled => _endpoint is not null;

    /// <summary>The program an MCP client is told to start; next to the editor.</summary>
    public static string BridgeProgram => Path.Combine(AppContext.BaseDirectory, "mcp", "textida-mcp" + (OperatingSystem.IsWindows() ? ".exe" : ""));

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
        Server.CloseAll();
        foreach (var id in SharedIds()) Unshare(id);
        if (endpoint is not null) await endpoint.DisposeAsync();
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
        foreach (var permission in new[] { DocumentPermissions.SecretsReveal, DocumentPermissions.EditPropose, DocumentPermissions.Read })
            _plugins.Permissions.Revoke(McpServer.ClientId, permission, scope);
        if (shared.ExposedByUs && _plugins.Documents.IsExposed(document) && !_plugins.IsRunningOn(document) && !_plugins.Acp.IsSharedDocument(document)) _plugins.Documents.Withdraw(shared.Document);
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

    /// <summary>The user sees the proposal in the change preview of the window that shows the tab, and decides.</summary>
    /// <summary>The proposal goes through the same change review as the proposals of plugins; a partly accepted one counts as applied.</summary>
    public async ValueTask<ReviewOutcome> ReviewAsync(EditProposal proposal, CancellationToken cancellation)
    {
        Shared? shared;
        lock (_gate) _shared.TryGetValue(proposal.DocumentId, out shared);
        if (shared is null) return new ReviewOutcome(ProposalStatus.Stale);
        var decision = await Dispatcher.UIThread.InvokeAsync(() => shared.Window.ReviewPluginProposalAsync(proposal, shared.Tab, Guid.NewGuid().ToString("N"), cancellation));
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
