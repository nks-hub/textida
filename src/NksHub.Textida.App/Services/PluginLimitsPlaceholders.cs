using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
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

    /// <summary>Placeholder: how long a document rests after an edit before its outline is read again.</summary>
    public const int OutlineRefreshMilliseconds = 400;

    /// <summary>Placeholder: bytes Emmet reads in front of a caret to find the abbreviation, its line and the CSS block it stands in. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int EmmetContextBytes = 64 * 1024;

    /// <summary>Placeholder: most words the completion list (Ctrl+J) holds; the best ranked are kept.</summary>
    public const int CompletionMaxWords = 200;

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

    public static DocumentApiOptions Documents(IEditProposals proposals, IEditorTools tools, PluginPermissions permissions, PluginCallLog callLog) => new()
    {
        MaxSymbolsPerDocument = SymbolsPerDocument,
        MaxSymbolTextLength = SymbolTextLength,
        MaxSnippetLength = SnippetLength,
        MaxSnippetCarets = SnippetCarets,
        TabTimeout = TabTimeout,
        MaxSecretScanBytes = SecretScanMaxBytes,
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
