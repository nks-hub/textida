using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The one place where the app gives the plugin host its limits, timeouts and quotas. EVERY NUMBER HERE IS A PLACEHOLDER waiting for the
/// owner's decision (the host and the APIs have no defaults on purpose); none of them is a decided value.
/// </summary>
internal static class PluginLimitsPlaceholders
{
    /// <summary>Placeholder, OPEN: milliseconds between two looks at a followed log for new lines, truncation or rotation. The owner has not decided it.</summary>
    public const int LogFollowPollMilliseconds = 1000;

    /// <summary>Placeholder, OPEN: milliseconds one line may spend in the highlight expression of a log view before its search is cut off.</summary>
    public const int LogHighlightMatchTimeoutMilliseconds = 50;

    /// <summary>Placeholder, OPEN: how many times an upload is repeated after the connection was lost (each repeat checks the server version again and continues a partial temporary file).</summary>
    public const int RemoteRetryCount = 3;

    /// <summary>Placeholder, OPEN: milliseconds before the first repeat of a lost upload; every further repeat waits twice as long.</summary>
    public const int RemoteRetryBaseDelayMilliseconds = 2000;

    /// <summary>Placeholder, OPEN: most characters of a document that the HTML and RTF export (file or clipboard) takes; a larger one is refused with a message.</summary>
    public const int HighlightExportMaxChars = 8 * 1024 * 1024;

    /// <summary>Placeholder: largest JSON range the built-in formatter handles; also bounded by <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const long JsonFormatterMaxBytes = 1 << 20;

    /// <summary>Placeholder: spaces per level of the built-in formatter.</summary>
    public const int JsonFormatterIndent = 2;

    /// <summary>Placeholder: bytes the text tools (statistics, hashes) read per request; the one chunk is their working memory. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int TextToolsChunkBytes = 1 << 20;

    /// <summary>Placeholder: milliseconds between two progress reports of a running text tool.</summary>
    public const int TextToolsProgressMilliseconds = 250;

    /// <summary>Placeholder: longest snippet or clip text, in UTF-16 code units.</summary>
    public const int SnippetLength = 64 * 1024;

    /// <summary>Placeholder: carets one snippet or clip places in each selection.</summary>
    public const int SnippetCarets = 64;

    /// <summary>Placeholder: largest grammar file of a data plugin.</summary>
    public const long GrammarMaxBytes = 1 << 20;
    /// <summary>Placeholder: largest secrets file (<c>.env</c>) whose values are looked for when a plugin reads it; a larger one is hidden whole.</summary>
    public const long SecretScanMaxBytes = 16L << 20;

    /// <summary>Placeholder: largest size a plugin ZIP may unpack to, in bytes. OPEN: the owner has not decided it.</summary>
    public const long PluginZipMaxUnpackedBytes = 256L << 20;

    /// <summary>Placeholder: most entries a plugin ZIP may hold. OPEN: the owner has not decided it.</summary>
    public const int PluginZipMaxEntries = 2000;

    public static NksHub.Textida.PluginHost.Manager.PluginInstallLimits PluginZip() => new(PluginZipMaxUnpackedBytes, PluginZipMaxEntries);

    /// <summary>Placeholder: bytes the web data plugin (colours, key paths) reads per request. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int WebDataChunkBytes = 1 << 20;

    /// <summary>Placeholder: largest file the web data plugin reads whole to find colours or keys.</summary>
    public const int WebDataMaxBytes = 4 << 20;

    /// <summary>Placeholder: keys of one file published as symbols (outline, Go to Anything).</summary>
    public const int SymbolsPerDocument = 5000;

    /// <summary>Placeholder: longest symbol id, name or kind, in UTF-16 units.</summary>
    public const int SymbolTextLength = 400;

    /// <summary>Placeholder: how long the caret rests before the status bar asks for the key path under it.</summary>
    public const int KeyPathDebounceMilliseconds = 150;

    /// <summary>Placeholder: bytes the Code Explorer reads per request. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int CodeExplorerChunkBytes = 1 << 20;

    /// <summary>Placeholder: largest file the Code Explorer reads whole to find its symbols.</summary>
    public const int CodeExplorerMaxBytes = 4 << 20;

    /// <summary>Placeholder: largest range the formatters (SQL layout, HTML entities, HTML Tidy) work on; also bounded by <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const long FormattersMaxBytes = 1 << 20;

    /// <summary>Placeholder: spaces per level of the SQL formatter.</summary>
    public const int SqlIndent = 4;

    /// <summary>Placeholder: letter case of SQL keywords after formatting (<c>upper</c>, <c>lower</c> or <c>preserve</c>).</summary>
    public const string SqlKeywordCase = "upper";

    /// <summary>Placeholder: time after which a Tidy run that has not finished is ended.</summary>
    public const int TidyTimeoutMilliseconds = 10_000;

    /// <summary>Placeholder: the arguments HTML Tidy is started with. The text goes to its standard input.</summary>
    public static readonly string[] TidyArguments = ["-quiet", "-utf8", "--tidy-mark", "no"];

    /// <summary>Placeholder: time after which a git command that has not finished is ended.</summary>
    public const int GitTimeoutMilliseconds = 20_000;

    /// <summary>Placeholder: files listed in the Git status panel; more are counted and named in a notice. Must not exceed <see cref="Ui"/> items per panel.</summary>
    public const int GitStatusEntries = 2000;

    /// <summary>Placeholder: bytes the Git plugin reads per request when it takes the staged index document back. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int GitChunkBytes = 1 << 20;

    /// <summary>Placeholder: how long a document rests after an edit before its outline is read again.</summary>
    public const int OutlineRefreshMilliseconds = 400;

    /// <summary>Placeholder: bytes Emmet reads in front of a caret to find the abbreviation, its line and the CSS block it stands in. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int EmmetContextBytes = 64 * 1024;

    /// <summary>Placeholder: most words the completion list (Ctrl+J) holds; the best ranked are kept.</summary>
    public const int CompletionMaxWords = 200;

    /// <summary>Placeholder: longest text (and HTML) one plugin write puts on the clipboard, in UTF-16 units; the same provisional bound the clipboard history uses.</summary>
    public const int ClipboardMaxChars = ClipboardHistory.MaxItemLength;

    /// <summary>Placeholder: bytes one inspection call (file facts, log query) reads before it stops and says where to go on.</summary>
    public const long InspectionMaxScanBytes = 256L << 20;

    /// <summary>Placeholder: virtual documents (read only tabs a plugin supplies) one plugin keeps open at once.</summary>
    public const int VirtualDocumentsPerPlugin = 8;

    /// <summary>Placeholder: largest content of a virtual document, in bytes.</summary>
    public const long VirtualDocumentMaxBytes = 4L << 20;

    /// <summary>Placeholder: how long the editor waits for a file source (list, read, stat, write) before it counts the source as not reachable. OPEN: the owner has not decided it.</summary>
    public const int SourceTimeoutMilliseconds = 15_000;

    /// <summary>Placeholder: largest file a source serves or takes, in bytes. OPEN: the owner has not decided it.</summary>
    public const long SourceMaxBytes = 16L << 20;

    /// <summary>Placeholder: entries of one folder listing taken from a source. OPEN: the owner has not decided it.</summary>
    public const int SourceMaxEntries = 5000;

    /// <summary>Placeholder: URI schemes one plugin may serve. OPEN: the owner has not decided it.</summary>
    public const int SourceSchemesPerPlugin = 4;

    /// <summary>Placeholder: files of the source cache (memory only, least recently used out first). OPEN: the owner has not decided it.</summary>
    public const int SourceCacheEntries = 32;

    /// <summary>Placeholder: bytes of the source cache. OPEN: the owner has not decided it.</summary>
    public const long SourceCacheBytes = 64L << 20;

    /// <summary>Placeholder: diagnostics one checker (source) of a plugin may publish for one document.</summary>
    public const int DiagnosticsPerDocument = 2000;

    /// <summary>Placeholder: completion items taken from one plugin's answer.</summary>
    public const int CompletionItemsPerPlugin = 200;

    /// <summary>Placeholder: additional edits (imports, <c>use</c>) one completion item may carry.</summary>
    public const int CompletionEditsPerItem = 16;

    /// <summary>Placeholder: how long the completion list waits for the plugins before it shows what it has.</summary>
    public const int CompletionTimeoutMilliseconds = 1000;

    /// <summary>Placeholder: how long a document rests after an edit before the schema check runs again. OPEN: the owner has not decided it.</summary>
    public const int SchemaCheckDebounceMilliseconds = 500;

    /// <summary>Placeholder: largest file the schema check and the schema key completion read whole. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int SchemaMaxBytes = 1 << 20;

    /// <summary>Placeholder: deepest nesting the schema check follows in a file and in a schema.</summary>
    public const int SchemaMaxDepth = 64;

    /// <summary>Placeholder: time one <c>pattern</c> of a schema may take on one value before it counts as not matching.</summary>
    public const int SchemaRegexTimeoutMilliseconds = 200;

    /// <summary>Placeholder: largest file handed to a language server; a larger one never starts one. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>. OPEN: the owner has not decided it.</summary>
    public const int LspMaxBytes = 1 << 20;

    /// <summary>Placeholder: time a language server has to answer <c>initialize</c>. OPEN: the owner has not decided it.</summary>
    public const int LspStartTimeoutMilliseconds = 30_000;

    /// <summary>Placeholder: time a language server has to answer a definition or references request. OPEN: the owner has not decided it.</summary>
    public const int LspRequestTimeoutMilliseconds = 10_000;

    /// <summary>Placeholder: time a language server gets to shut down and leave before it is killed. OPEN: the owner has not decided it.</summary>
    public const int LspStopTimeoutMilliseconds = 3000;

    /// <summary>Placeholder: places listed for one definition or references request. Must not exceed the items per panel of <see cref="Ui"/>.</summary>
    public const int LspMaxLocations = 500;

    /// <summary>Placeholder: last lines of a language server's standard error kept for the report of its end.</summary>
    public const int LspStderrLines = 20;

    /// <summary>Placeholder: time a language server keeps running after the last document of its language was closed. OPEN: the owner has not decided it; a crashed server is not restarted by itself, the next command starts it.</summary>
    public const int LspIdleStopMilliseconds = 300_000;

    /// <summary>Placeholder: time one package registry request may take. OPEN: the owner has not decided it.</summary>
    public const int RegistryTimeoutMilliseconds = 10_000;

    /// <summary>Placeholder: largest answer of a package registry that is read; a larger one is an error. OPEN: the owner has not decided it.</summary>
    public const long RegistryMaxResponseBytes = 8L << 20;

    /// <summary>Placeholder: names asked of a registry for one name completion. OPEN: the owner has not decided it.</summary>
    public const int RegistryPageSize = 20;

    /// <summary>Placeholder: packages one check of a manifest asks the registry about; the rest stay unchecked. OPEN: the owner has not decided it. The lifetime of cached answers is open too: the cache has none, only Refresh Package Versions drops it.</summary>
    public const int RegistryMaxLookups = 50;

    public static PluginHostOptions Host() => new()
    {
        Limits = new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 8, MaxPendingOutgoing: 64, MaxResultItems: 100_000, MaxSessionBytes: 64L << 20, MaxOutgoingQueueBytes: 64L << 20),
        Restart = new RestartPolicy(MaxCrashes: 3, Window: TimeSpan.FromMinutes(5), BaseDelay: TimeSpan.FromSeconds(1), MaxDelay: TimeSpan.FromSeconds(30)),
        ApiVersion = 1,
        HandshakeTimeout = TimeSpan.FromSeconds(10),
        PingInterval = TimeSpan.FromSeconds(5),
        PingTimeout = TimeSpan.FromSeconds(5),
        MissedPingsBeforeKill = 3,
        ShutdownGrace = TimeSpan.FromSeconds(3),
        StderrTailLines = 20,
    };

    /// <summary>Placeholder: entries kept in the plugin call log.</summary>
    public const int CallLogCapacity = 1000;

    /// <summary>Placeholder: how long Tab waits for a plugin before it indents as without one. Settable so tests on a loaded machine do not race it.</summary>
    internal static TimeSpan TabTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Placeholder: changes (edits of a file, creates, renames, deletes) one workspace edit may hold. OPEN: the owner has not decided it.</summary>
    public const int WorkspaceEditChanges = 200;

    /// <summary>Placeholder: largest file a workspace edit may create, in bytes. OPEN: the owner has not decided it.</summary>
    public const long WorkspaceEditFileBytes = 4L << 20;

    /// <summary>Placeholder: longest text of one preview a plugin shows, in UTF-16 units. OPEN: the owner has not decided it.</summary>
    public const int PreviewMaxChars = 1 << 20;

    /// <summary>Placeholder: largest image of a preview, in decoded bytes of its <c>data:</c> URI. OPEN: the owner has not decided it.</summary>
    public const int PreviewImageMaxBytes = 1 << 20;

    /// <summary>Placeholder: quiet time after an edit before the live preview of the current file is drawn again, in milliseconds. OPEN: the owner has not decided it.</summary>
    public const int PreviewLiveDebounceMs = 300;

    /// <summary>Placeholder: longest text, in characters, that the live preview of the current file draws. OPEN: the owner has not decided it.</summary>
    public const int PreviewLiveMaxChars = 1 << 20;

    /// <summary>Placeholder: longest text of one update, tool title or permission question a plugin sends to the agent panel, in UTF-16 units. OPEN: the owner has not decided it.</summary>
    public const int AgentUpdateMaxChars = 64 * 1024;

    public static DocumentApiOptions Documents(IEditProposals proposals, IEditorTools tools, PluginPermissions permissions, PluginCallLog callLog, Func<Guid, LogLevelRules?> logRules, WorkspaceEditOptions? workspace = null,
        IPreviewPanel? preview = null, IAgentPanel? agent = null) => new()
    {
        Workspace = workspace,
        Preview = preview is null ? null : new PreviewOptions(preview, PreviewMaxChars),
        Agent = agent is null ? null : new AgentPanelOptions(agent, AgentUpdateMaxChars),
        Sources = new FileSourceOptions(TimeSpan.FromMilliseconds(SourceTimeoutMilliseconds), SourceMaxBytes, SourceMaxEntries, SourceSchemesPerPlugin, SourceCacheEntries, SourceCacheBytes),
        LogRules = logRules,
        MaxSymbolsPerDocument = SymbolsPerDocument,
        MaxSymbolTextLength = SymbolTextLength,
        MaxSnippetLength = SnippetLength,
        MaxSnippetCarets = SnippetCarets,
        TabTimeout = TabTimeout,
        MaxSecretScanBytes = SecretScanMaxBytes,
        MaxClipboardChars = ClipboardMaxChars,
        MaxScanBytes = InspectionMaxScanBytes,
        MaxVirtualDocumentsPerPlugin = VirtualDocumentsPerPlugin,
        MaxVirtualBytes = VirtualDocumentMaxBytes,
        MaxDiagnosticsPerDocument = DiagnosticsPerDocument,
        MaxCompletionItems = CompletionItemsPerPlugin,
        MaxCompletionEdits = CompletionEditsPerItem,
        CompletionTimeout = TimeSpan.FromMilliseconds(CompletionTimeoutMilliseconds),
        Tools = tools,
        MaxReadBytes = 1 << 20,
        MaxFindMatches = 1000,
        MaxSnapshotsPerPlugin = 4,
        SnapshotLifetime = TimeSpan.FromSeconds(30),
        EventQueueCapacity = 256,
        MaxRememberedBatches = 64,
        Permissions = permissions,
        CallLog = callLog,
        Proposals = proposals,
    };

    /// <summary>Placeholder: proposals of the MCP assistant the editor remembers the outcome of.</summary>
    public const int McpTrackedProposals = 100;

    /// <summary>Placeholder: longest text one MCP tool result holds, in UTF-16 units.</summary>
    public const int McpMaxResultChars = 256 * 1024;

    /// <summary>Placeholder: most bytes of text one MCP read returns.</summary>
    public const int McpMaxRangeBytes = 64 * 1024;

    /// <summary>Placeholder: most matches, log lines or symbols one MCP tool call returns.</summary>
    public const int McpMaxItems = 200;

    /// <summary>Placeholder: bytes of text around a match in an MCP search result.</summary>
    public const int McpSnippetBytes = 160;

    /// <summary>Placeholder: longest log line an MCP result carries, in UTF-16 units.</summary>
    public const int McpMaxLineChars = 2000;

    public static McpServerOptions Mcp(DocumentApi documents, ProposalLedger ledger, Func<Guid, string?> titleOf, Func<Guid, CancellationToken, Task> ensureOutline) => new()
    {
        Documents = documents,
        Proposals = ledger,
        Limits = new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 8, MaxPendingOutgoing: 8, MaxResultItems: 100_000, MaxSessionBytes: 16L << 20, MaxOutgoingQueueBytes: 16L << 20),
        ServerName = "textida",
        ServerVersion = typeof(PluginLimitsPlaceholders).Assembly.GetName().Version?.ToString(3) ?? "0",
        MaxResultChars = McpMaxResultChars,
        MaxRangeBytes = McpMaxRangeBytes,
        MaxItems = McpMaxItems,
        SnippetBytes = McpSnippetBytes,
        MaxLineChars = McpMaxLineChars,
        TitleOf = titleOf,
        EnsureOutline = ensureOutline,
    };

    /// <summary>Placeholder: longest time a connection to the IDE bridge gets to send its upgrade request, in seconds.</summary>
    public const int IdeHandshakeSeconds = 5;

    /// <summary>Placeholder: largest upgrade request the IDE bridge reads, in bytes.</summary>
    public const int IdeMaxHeaderBytes = 8 * 1024;

    public static IdeBridgeOptions Ide(McpServer server, string lockDirectory) => new()
    {
        Server = server,
        LockDirectory = lockDirectory,
        HandshakeTimeout = TimeSpan.FromSeconds(IdeHandshakeSeconds),
        MaxHeaderBytes = IdeMaxHeaderBytes,
    };

    /// <summary>Placeholder: longest wait for a server tried from the list of agent servers, for its handshake and for its list of tools.</summary>
    public const int McpClientTimeoutSeconds = 30;

    /// <summary>Placeholder: time a server tried from the list gets to leave after its input is closed, before it is killed.</summary>
    public const int McpClientExitGraceSeconds = 5;

    /// <summary>Placeholder: last lines of a server's standard error kept for the report of its end.</summary>
    public const int McpClientStderrLines = 20;

    public static NksHub.Textida.McpClient.McpClientOptions McpClient() => new(
        new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 100_000, MaxSessionBytes: 16L << 20, MaxOutgoingQueueBytes: 16L << 20),
        TimeSpan.FromSeconds(McpClientTimeoutSeconds), TimeSpan.FromSeconds(McpClientExitGraceSeconds), McpClientStderrLines,
        "textida", typeof(PluginLimitsPlaceholders).Assembly.GetName().Version?.ToString(3) ?? "0");

    /// <summary>Placeholder: time an agent has to answer <c>initialize</c> and <c>session/new</c>. OPEN: the owner has not decided it.</summary>
    public const int AcpStartTimeoutSeconds = 30;

    /// <summary>Placeholder: a turn in which the agent says and asks nothing for this long ends as timed out; waiting for the user does not count. OPEN: the owner has not decided it.</summary>
    public const int AcpIdleTimeoutSeconds = 300;

    /// <summary>Placeholder: time an agent gets to leave after its input is closed, and to answer a cancelled turn, before it is killed or given up on. OPEN: the owner has not decided it.</summary>
    public const int AcpExitGraceSeconds = 5;

    /// <summary>Placeholder: last lines of an agent's standard error kept for the report of its end.</summary>
    public const int AcpStderrLines = 20;

    /// <summary>Placeholder: largest text file (bytes of the content) handed to an agent in one <c>fs/read_text_file</c>. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>. OPEN: the owner has not decided it.</summary>
    public const int AcpMaxReadBytes = 1 << 20;

    public static NksHub.Textida.Acp.AcpOptions Acp() => new(
        new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 100_000, MaxSessionBytes: 16L << 20, MaxOutgoingQueueBytes: 16L << 20),
        TimeSpan.FromSeconds(AcpStartTimeoutSeconds), TimeSpan.FromSeconds(AcpIdleTimeoutSeconds), TimeSpan.FromSeconds(AcpExitGraceSeconds), AcpStderrLines,
        "textida", typeof(PluginLimitsPlaceholders).Assembly.GetName().Version?.ToString(3) ?? "0");

    public static UiApiOptions Ui() => new()
    {
        MaxPanelsPerPlugin = 4,
        MaxItemsPerPanel = 5000,
        MaxTreeDepth = 16,
        MaxTasksPerPlugin = 4,
        MaxTextLength = 4000,
        MaxPickItems = 500,
    };
}
