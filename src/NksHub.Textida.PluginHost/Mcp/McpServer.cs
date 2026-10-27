using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Mcp;

/// <summary>Limits and hooks of the MCP server. Sizes and counts are owner decisions, so none of them has a default.</summary>
public sealed record McpServerOptions
{
    /// <summary>The document API every tool goes through, so grants, hidden <c>.env</c> values, revisions and the call log are the ones plugins have.</summary>
    public required DocumentApi Documents { get; init; }
    public required ProposalLedger Proposals { get; init; }
    public required RpcLimits Limits { get; init; }
    public required string ServerName { get; init; }
    public required string ServerVersion { get; init; }
    /// <summary>Longest text one tool result may hold, in UTF-16 code units; a longer one is refused and the assistant is told to narrow the request.</summary>
    public required int MaxResultChars { get; init; }
    /// <summary>Most bytes <c>editor_read_range</c> returns.</summary>
    public required int MaxRangeBytes { get; init; }
    /// <summary>Most matches, log lines or symbols one tool call returns.</summary>
    public required int MaxItems { get; init; }
    /// <summary>Bytes of text around a match in a search result.</summary>
    public required int SnippetBytes { get; init; }
    /// <summary>Longest log line returned, in UTF-16 code units.</summary>
    public required int MaxLineChars { get; init; }
    /// <summary>Name of a document for the assistant (the tab title); the file name is used when this is missing or answers null.</summary>
    public Func<Guid, string?>? TitleOf { get; init; }
    /// <summary>Makes sure an outline of the live text exists before it is read (indexes the tab).</summary>
    public Func<Guid, CancellationToken, Task>? EnsureOutline { get; init; }
}

/// <summary>What a tool is, for the list the user sees when the server is switched on.</summary>
public sealed record McpToolInfo(string Name, string Title, string Description, bool ReadOnly);

/// <summary>
/// The editor as an MCP server. The protocol is the current revision (2026-07-28: no handshake, every request carries its version in <c>_meta</c>) and, for clients that
/// still open with <c>initialize</c>, the 2025-11-25 handshake. Tools reach documents only through <see cref="DocumentApi"/> as the client <see cref="ClientId"/>:
/// a tab the user has not shared is not listed and cannot be read, and a revoked grant stops the request that is running.
/// </summary>
public sealed class McpServer
{
    /// <summary>The id the assistant has in the permission model and the call log.</summary>
    public const string ClientId = "mcp";
    public const string ModernVersion = "2026-07-28";
    public const string LegacyVersion = "2025-11-25";
    /// <summary>Largest range <c>editor_read_bytes</c> returns.</summary>
    public const int MaxReadBytes = 64 * 1024;

    private readonly object _gate = new();
    private readonly HashSet<string> _disabled = [];
    private readonly List<McpSession> _sessions = [];

    public McpServer(McpServerOptions options)
    {
        if (options.MaxResultChars < 1 || options.MaxRangeBytes < 1 || options.MaxItems < 1 || options.SnippetBytes < 1 || options.MaxLineChars < 1)
            throw new ArgumentException("Every MCP limit must be positive.", nameof(options));
        Options = options;
    }

    internal McpServerOptions Options { get; }

    /// <summary>The set of tools changed (the user switched one on or off).</summary>
    public event Action? ToolsChanged;

    public static IReadOnlyList<McpToolInfo> AllTools { get; } = [.. McpTools.All.Select(t => new McpToolInfo(t.Name, t.Title, t.Description, t.ReadOnly))];

    public bool IsEnabled(string tool)
    {
        lock (_gate) return McpTools.Find(tool) is not null && !_disabled.Contains(tool);
    }

    public void SetEnabled(string tool, bool enabled)
    {
        if (McpTools.Find(tool) is null) throw new ArgumentException($"There is no tool '{tool}'.", nameof(tool));
        lock (_gate)
        {
            if (!(enabled ? _disabled.Remove(tool) : _disabled.Add(tool))) return;
        }
        ToolsChanged?.Invoke();
        foreach (var session in Sessions()) session.ToolsListChanged();
    }

    public int ConnectionCount
    {
        get { lock (_gate) return _sessions.Count; }
    }

    private McpSession[] Sessions()
    {
        lock (_gate) return [.. _sessions];
    }

    /// <summary>Serves one client until it closes the stream, <paramref name="cancellation"/> fires or <see cref="CloseAll"/> is called.</summary>
    public Task ServeAsync(Stream input, Stream output, CancellationToken cancellation = default) => ServeAsync(input, output, null, cancellation);

    /// <summary>As above for a client with a <paramref name="peer"/>: tools of its own and notifications the editor sends it.</summary>
    internal async Task ServeAsync(Stream input, Stream output, McpPeer? peer, CancellationToken cancellation)
    {
        var session = new McpSession(this, peer);
        peer?.Attach(session);
        await using var rpc = new JsonRpcDispatcher(input, output, RpcProtocols.Mcp, Options.Limits, session);
        session.Attach(rpc);
        lock (_gate) _sessions.Add(session);
        try
        {
            using var stop = cancellation.Register(() => _ = rpc.DisposeAsync().AsTask());
            await rpc.Completion.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) _sessions.Remove(session);
        }
    }

    /// <summary>Ends every connection (the user switched the server off).</summary>
    public void CloseAll()
    {
        foreach (var session in Sessions()) session.Close();
    }

    internal IReadOnlyList<McpToolDefinition> EnabledTools()
    {
        lock (_gate) return [.. McpTools.All.Where(t => !_disabled.Contains(t.Name))];
    }

    internal McpToolDefinition? FindEnabled(string name)
    {
        lock (_gate) return McpTools.Find(name) is { } tool && !_disabled.Contains(name) ? tool : null;
    }

    internal static string Instructions =>
        "Textida is a text editor. Only the tabs the user shared with you are visible; editor_list_documents lists them. " +
        "Positions are byte offsets of a revision of the document: a read or a change names the revision it was made for, and a change for a revision that is no longer current is refused. " +
        "You never write to a document: editor_prepare_changes and editor_prepare_byte_changes put a proposal in front of the user, who sees it as a diff and decides; read the outcome with editor_get_change_status. " +
        "Values of .env files are shown as * unless the user allowed them for that tab.";
}
