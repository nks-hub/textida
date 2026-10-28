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

    /// <summary>Placeholder, OPEN: parts the error heatmap of the minimap cuts a log into (by byte position); the picture takes the densest bucket of each row.</summary>
    public const int LogHeatBuckets = 1024;

    /// <summary>Placeholder, OPEN: bytes the error heatmap reads per request while it counts lines in the background; also the working memory of one pass.</summary>
    public const int LogHeatChunkBytes = 1 << 20;

    /// <summary>Placeholder, OPEN: a log up to this size is read whole for the heatmap; a larger one is sampled.</summary>
    public const long LogHeatFullScanBytes = 256L << 20;

    /// <summary>Placeholder, OPEN: bytes read at the start of each bucket when the heatmap samples a log larger than <see cref="LogHeatFullScanBytes"/>.</summary>
    public const int LogHeatSampleBytesPerBucket = 128 << 10;

    /// <summary>Placeholder, OPEN: milliseconds the heatmap waits after the last edit of a log before it counts again.</summary>
    public const int LogHeatDelayMilliseconds = 1500;

    public static LogHeatLimits LogHeat() => new(LogHeatChunkBytes, LogHeatFullScanBytes, LogHeatSampleBytesPerBucket);

    /// <summary>Placeholder, OPEN: rows of consecutive lines in one strip of the sampled minimap of a large file; the picture has as many strips as such runs fit.</summary>
    public const int MinimapSampleStripRows = 8;

    /// <summary>Placeholder, OPEN: bytes from the start of a file that the CSV delimiter detection reads.</summary>
    public const long CsvDetectSampleBytes = 64 * 1024;

    /// <summary>Placeholder, OPEN: bytes a wildcard <c>*</c> or an open repeat of a hex search may stand for while the setting <c>hex.wildcardMaxBytes</c> is 0. The owner has not decided it.</summary>
    public const int HexWildcardDefaultMaxBytes = 256;

    /// <summary>Placeholder, OPEN: largest file that gets local history revisions, in bytes; a larger one gets none and the status bar says so.</summary>
    public const long HistoryRevisionMaxBytes = 32L << 20;

    /// <summary>Placeholder, OPEN: largest selection of a large file in the paged view that Copy and Cut take to the clipboard, in bytes; a larger one is refused with a message and the clipboard stays as it was. The owner has not decided it.</summary>
    public const long PagedCopyMaxBytes = 256L << 20;

    /// <summary>Placeholder, OPEN: longest line of a large file in the paged view that can be edited, in bytes; typing, deleting or pasting in a longer one is refused with a message and the document does not change. The owner has not decided it.</summary>
    public const long PagedEditLineMaxBytes = 1L << 30;

    /// <summary>Placeholder, OPEN: most UTF-16 characters of clipboard text that Paste takes into the paged view (its encoded copy and the clipboard string are both in memory at once); a longer text is refused with a message and the document does not change. The owner has not decided it.</summary>
    public const long PagedPasteMaxChars = 32L << 20;

    /// <summary>Placeholder, OPEN: milliseconds in which the largest measured paste (<see cref="PagedPasteMaxChars"/> characters into a file of 100 MiB) must end on a development PC; only the measuring test reads it. The owner has not decided it.</summary>
    public const int PagedPasteMeasureMilliseconds = 5000;
    /// <summary>Placeholder, OPEN: most carets (selections) a large file in the paged view takes; Add Next Occurrence, Select All Occurrences, Add Caret Above and Below and Alt+click stop there and the status bar says so. The owner has not decided it.</summary>
    public const int PagedMaxCarets = 1000;

    /// <summary>Placeholder, OPEN: longest selection of a large file in the paged view, in bytes, whose other occurrences Add Next Occurrence and Select All Occurrences look for; a longer one is refused with a message. The owner has not decided it.</summary>
    public const int PagedOccurrenceMaxBytes = 64 * 1024;

    /// <summary>Placeholder, OPEN: most bytes a paste inserts over all carets of a large file in the paged view together (the text times the number of carets); a larger paste is refused with a message and the document stays as it was. The owner has not decided it.</summary>
    public const long PagedMultiPasteMaxBytes = 64L << 20;

    /// <summary>Placeholder, OPEN: lines the matching bracket highlight looks through from the caret line for the partner; a partner farther away is not colored (Go to Matching Bracket still finds it).</summary>
    public const int BracketHighlightMaxLines = 5000;

    /// <summary>Placeholder, OPEN: largest file Insert from File reads, in bytes; a larger one is refused with a message.</summary>
    public const long InsertFromFileMaxBytes = 16L << 20;

    /// <summary>Placeholder, OPEN: Reformat Paragraph (Ends at a Short Line) counts a line shorter than this percent of the right margin column as the last of its paragraph.</summary>
    public const int ReformatShortLinePercent = 50;

    /// <summary>Placeholder, OPEN: size limit of one Copy as AI Context, in UTF-8 bytes of the copied text; files past it are left out and named. The owner has not decided it.</summary>
    public const long AiContextMaxBytes = 8L << 20;

    /// <summary>Placeholder, OPEN: seconds a formatter of the save actions (and a format step of a recipe) has when <c>formatTimeoutSeconds</c> in saveActions.json is not set; then the file is saved unformatted.</summary>
    public const int FormatOnSaveTimeoutSeconds = 10;

    /// <summary>Placeholder, OPEN: seconds a linter run has when <c>lintTimeoutSeconds</c> in saveActions.json is not set; then the run is stopped and reported, and the findings of the earlier run stay. The owner has not decided it.</summary>
    public const int LintTimeoutSeconds = 60;

    /// <summary>Placeholder, OPEN: milliseconds a linter run started by a save waits for a further save of the same file before it starts; a newer save replaces the waiting run. The owner has not decided it.</summary>
    public const int LintOnSaveDebounceMilliseconds = 500;

    /// <summary>Placeholder, OPEN: largest JSON range the built-in formatter handles (format, minify, sort keys, remove trailing commas); a larger one is refused with a message. Also bounded by <see cref="DocumentApiOptions.MaxReadBytes"/>. The owner has not decided it.</summary>
    public const long JsonFormatterMaxBytes = 1 << 20;

    /// <summary>Placeholder, OPEN: spaces per level of the built-in formatter. The owner has not decided it.</summary>
    public const int JsonFormatterIndent = 2;

    /// <summary>Placeholder, OPEN: deepest nesting of arrays and objects the built-in JSON formatter accepts; a deeper text is refused with a message. The formatter never goes deeper than 512 whatever is set. The owner has not decided it.</summary>
    public const int JsonFormatterMaxDepth = 64;

    /// <summary>Placeholder: bytes the text tools (statistics, hashes) read per request; the one chunk is their working memory. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int TextToolsChunkBytes = 1 << 20;

    /// <summary>Placeholder: entries in each frequency list (characters, words) of the document statistics.</summary>
    public const int TextToolsFrequencyTop = 10;

    /// <summary>Placeholder: distinct words the statistics keep for the word frequency list; further new words are left out and the list says so.</summary>
    public const int TextToolsFrequencyMaxWords = 100_000;

    /// <summary>Placeholder: characters of a word that count towards its frequency entry.</summary>
    public const int TextToolsFrequencyMaxWordLength = 64;

    /// <summary>Placeholder: milliseconds between two progress reports of a running text tool.</summary>
    public const int TextToolsProgressMilliseconds = 250;

    /// <summary>Placeholder: longest snippet or clip text, in UTF-16 code units.</summary>
    public const int SnippetLength = 64 * 1024;

    /// <summary>Placeholder: carets one snippet or clip places in each selection, and the most places Tab visits in one Emmet expansion.</summary>
    public const int SnippetCarets = 64;

    /// <summary>Placeholder, OPEN: largest grammar file of a data plugin in bytes. The manifest loader refuses a plugin with a larger one, whoever installs it.</summary>
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

    /// <summary>Placeholder, OPEN: milliseconds one outline rule may spend on a text; past it the file gets no outline.</summary>
    public const int CodeExplorerMatchTimeoutMilliseconds = 2000;

    /// <summary>Placeholder: largest file the Code Explorer reads whole to find its symbols; of a paged (large) file it reads only this many bytes from the start.</summary>
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

    /// <summary>Placeholder, OPEN: quiet time, in milliseconds, after a change in the working tree or in the index of the repository before the Git status panel and status bar are read again. The owner has not decided it.</summary>
    public const int GitWatchDebounceMilliseconds = 500;

    /// <summary>Placeholder, OPEN: quiet time, in milliseconds, after an edit, a save or a change of the repository before the change marks in the gutter of the open file are asked for again. The owner has not decided it.</summary>
    public const int GitGutterDebounceMilliseconds = 600;

    /// <summary>Placeholder, OPEN: largest document, in bytes, that gets change marks in the gutter; a larger one gets none. Must not exceed <see cref="VirtualDocumentMaxBytes"/>, which is what the Git plugin reads. The owner has not decided it.</summary>
    public const long GitGutterMaxBytes = 1L << 20;

    /// <summary>Placeholder, OPEN: most buttons a plugin may add to a comparison tab it opens. The commands <c>nks.compare.action1</c> to <c>action4</c> reach four of them, the digit keys nine. The owner has not decided it.</summary>
    public const int DiffActionsPerTab = 4;

    /// <summary>Placeholder, OPEN: longest key, command and title of a comparison tab a plugin opens, in UTF-16 units. The owner has not decided it.</summary>
    public const int DiffActionTextLength = 200;

    /// <summary>Placeholder, OPEN: largest side of a diff a plugin asks for (compute, open, stage), in bytes: a document, a snapshot, text of the plugin or a file; a larger one is refused with a quota error. The owner has not decided it.</summary>
    public const long DiffMaxSideBytes = 16L << 20;

    /// <summary>Placeholder: how long a document rests after an edit before its outline is read again.</summary>
    public const int OutlineRefreshMilliseconds = 400;

    /// <summary>Placeholder, OPEN: bytes Emmet reads in front of a caret to find the abbreviation, its line and the CSS block it stands in. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>. The owner has not decided it.</summary>
    public const int EmmetContextBytes = 64 * 1024;

    /// <summary>Placeholder, OPEN: characters of the expansion the Emmet preview shows before it cuts the rest off. Must stay below the message length of <see cref="Ui"/>. The owner has not decided it.</summary>
    public const int EmmetPreviewChars = 3000;

    /// <summary>Placeholder: most words the completion list (Ctrl+J) holds; the best ranked are kept.</summary>
    public const int CompletionMaxWords = 200;

    /// <summary>Placeholder: longest text (and HTML) one plugin write puts on the clipboard, in UTF-16 units; the same provisional bound the clipboard history uses.</summary>
    public const int ClipboardMaxChars = ClipboardHistory.MaxItemLength;

    /// <summary>Placeholder: bytes one inspection call (file facts, log query) reads before it stops and says where to go on.</summary>
    public const long InspectionMaxScanBytes = 256L << 20;

    /// <summary>Placeholder, OPEN: largest range one <c>document/readBytes</c> or <c>document/readText</c> returns, in bytes; a larger one is refused. The plugin chunk sizes above must not exceed it. The owner has not decided it.</summary>
    public const int DocumentReadMaxBytes = 1 << 20;

    /// <summary>Placeholder, OPEN: matches one <c>document/find</c> returns before it says <c>truncated</c>. The owner has not decided it.</summary>
    public const int DocumentFindMaxMatches = 1000;

    /// <summary>Placeholder, OPEN: snapshots one plugin may hold at once; a further one is refused with a quota error. The owner has not decided it.</summary>
    public const int SnapshotsPerPlugin = 4;

    /// <summary>Placeholder, OPEN: seconds a snapshot lives after its last read, then it is released. The owner has not decided it.</summary>
    public const int SnapshotLifetimeSeconds = 30;

    /// <summary>Placeholder, OPEN: bytes the snapshots of one plugin may hold in memory together (a snapshot of a mapped file counts only its piece list, a copy counts all of it); a snapshot over it is refused with a quota error. The owner has not decided it.</summary>
    public const long SnapshotBytesPerPlugin = 64L << 20;

    /// <summary>Placeholder, OPEN: ranges or positions one <c>document/mapRanges</c> or <c>document/mapPositions</c> call maps; a larger call is refused. The owner has not decided it.</summary>
    public const int MapItemsPerCall = 10_000;

    /// <summary>Placeholder: virtual documents (read only tabs a plugin supplies) one plugin keeps open at once.</summary>
    public const int VirtualDocumentsPerPlugin = 8;

    /// <summary>Placeholder: largest content of a virtual document, in bytes.</summary>
    public const long VirtualDocumentMaxBytes = 4L << 20;

    /// <summary>Placeholder, OPEN: how long the editor waits for a plugin to take the save of an editable virtual document before the tab stays unsaved. The owner has not decided it.</summary>
    public const int VirtualSaveTimeoutMilliseconds = 15_000;

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

    /// <summary>Placeholder, OPEN: longest version text a source may state about its adapter and about its service. The owner has not decided it.</summary>
    public const int SourceVersionMaxChars = 64;

    /// <summary>Placeholder, OPEN: most characters of the words a search in a source may send. The owner has not decided it.</summary>
    public const int SourceQueryMaxChars = 200;

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

    /// <summary>Placeholder: folders searched upwards for the project root of a document for a language server, the folder of the file being the first. OPEN: the owner has not decided it.</summary>
    public const int LspRootSearchLevels = 16;

    /// <summary>Placeholder: time one package registry request may take. OPEN: the owner has not decided it.</summary>
    public const int RegistryTimeoutMilliseconds = 10_000;

    /// <summary>Placeholder: largest answer of a package registry that is read; a larger one is an error. OPEN: the owner has not decided it.</summary>
    public const long RegistryMaxResponseBytes = 8L << 20;

    /// <summary>Placeholder: names asked of a registry for one name completion. OPEN: the owner has not decided it.</summary>
    public const int RegistryPageSize = 20;

    /// <summary>Placeholder: packages one check of a manifest asks the registry about; the rest stay unchecked. OPEN: the owner has not decided it.</summary>
    public const int RegistryMaxLookups = 50;

    /// <summary>Placeholder, OPEN: bytes of registry answers the cache of the JSON plugin holds; the oldest answers go first and one larger than this is not kept. The owner has not decided it.</summary>
    public const long RegistryCacheMaxBytes = 16L << 20;

    /// <summary>Placeholder, OPEN: seconds a cached registry answer is used; an older one is asked for again (Refresh Package Versions drops the cache at any time). The owner has not decided it.</summary>
    public const int RegistryCacheLifetimeSeconds = 3600;

    /// <summary>Placeholder, OPEN: milliseconds the proxy of the network policy is not copied again to the consent file the JSON plugin reads while schemas are checked. The owner has not decided it.</summary>
    public const int RegistryProxySyncMilliseconds = 30_000;

    /// <summary>Placeholder, OPEN: bytes the copies of downloaded schemas take on disk together; the oldest copies are deleted to make room. The owner has not decided it.</summary>
    public const long SchemaCacheMaxBytes = 16L << 20;

    /// <summary>Placeholder, OPEN: largest schema that is downloaded and kept; a larger answer is refused. The owner has not decided it.</summary>
    public const long SchemaDownloadMaxBytes = 4L << 20;

    /// <summary>Placeholder, OPEN: seconds a downloaded schema is used without asking its host again; after that the host is asked, and the copy is used only if the host does not answer. The owner has not decided it.</summary>
    public const int SchemaCacheMaxAgeSeconds = 7 * 24 * 3600;

    /// <summary>Placeholder, OPEN: seconds a host that did not answer for a schema is left alone before it is asked again. The owner has not decided it.</summary>
    public const int SchemaRetrySeconds = 60;

    /// <summary>Placeholder, OPEN: milliseconds the pointer rests on text before the hover description is asked for. The owner has not decided it.</summary>
    public const int HoverDelayMilliseconds = 400;

    /// <summary>Placeholder, OPEN: longest hover description taken from a plugin, in UTF-16 units; a longer one is cut. The owner has not decided it.</summary>
    public const int HoverMaxChars = 2000;

    /// <summary>Placeholder: document events that wait for one plugin; beyond it the rest of a document's events become one <c>document/eventsLost</c>. OPEN: the owner has not decided it.</summary>
    public const int EventQueueCapacity = 256;

    /// <summary>Placeholder: events a plugin may have sent to it and not handled yet before the editor holds its document events back. OPEN: the owner has not decided it.</summary>
    public const int EventWindow = 32;

    /// <summary>Placeholder, OPEN: time one <c>net/request</c> of a plugin may take, answer included. The owner has not decided it.</summary>
    public const int NetworkTimeoutMilliseconds = 30_000;

    /// <summary>Placeholder, OPEN: largest answer body of one <c>net/request</c>. It travels base64 encoded, so it stays well under the 4 MiB message limit of <see cref="Host"/>. The owner has not decided it.</summary>
    public const int NetworkMaxResponseBytes = 2 << 20;

    /// <summary>Placeholder, OPEN: seconds after which a tool with no <c>timeoutSeconds</c> of its own is ended while the setting <c>tools.timeoutSeconds</c> is 0. 0 means no limit, which is how tools ran before. The owner has not decided it.</summary>
    public const int ToolDefaultTimeoutSeconds = 0;

    /// <summary>Placeholder, OPEN: the encoding the output of a tool is read in while its definition names none and the setting <c>tools.outputEncoding</c> is empty. The owner has not decided it.</summary>
    public const string ToolOutputEncoding = "utf-8";

    /// <summary>Placeholder, OPEN: folders looked into for the nearest Makefile, counting the folder of the open file and those above it. The search also stops at the root of the project. The owner has not decided it.</summary>
    public const int MakefileSearchLevels = 8;

    /// <summary>Placeholder, OPEN: largest Makefile in bytes that is read from disk to list its targets; a larger one is refused with a message. The owner has not decided it.</summary>
    public const long MakefileMaxBytes = 4L << 20;

    public static PluginNetworkOptions Network() => new(TimeSpan.FromMilliseconds(NetworkTimeoutMilliseconds), NetworkMaxResponseBytes);

    /// <summary>Placeholder, OPEN: seconds a plugin that asked for it (idle stop) may have no work before the editor ends its process; the next activation event starts it again. The owner has not decided it.</summary>
    public const int IdleStopSeconds = 300;

    /// <summary>Placeholder, OPEN: seconds all plugins together may take to leave when the editor closes; a process still alive then is killed by its process id. The owner has not decided it.</summary>
    public const int ShutdownTotalSeconds = 4;

    /// <summary>Placeholder, OPEN: seconds the exit of the editor waits for the plugin services (agents, the MCP server, the plugins) to finish before it kills the plugin processes that are left. The owner has not decided it.</summary>
    public const int ExitWaitSeconds = 8;

    /// <summary>Placeholder, OPEN: seconds between two looks of a plugin process at the editor that started it; a plugin ends by itself when the editor is gone (the system ends the child processes of a killed editor on Windows; on macOS and Linux this is what is left). The owner has not decided it.</summary>
    public const int ParentCheckSeconds = 2;

    /// <summary>Placeholder, OPEN: milliseconds two readings of the start time of one process may differ by and still be that process. A process id whose start time is farther off belongs to another program and is never killed or waited for. The owner has not decided it.</summary>
    public const int StartTimeToleranceMilliseconds = 2000;

    /// <summary>Placeholder, OPEN: the subfolder of the profile where macOS and Linux list the child processes of the editor, so the next start can end the ones a killed editor left behind. The owner has not decided it.</summary>
    public const string ChildListFolder = "children";

    /// <summary>Placeholder, OPEN: files and folders looked at over all open folders to see whether a plugin's <c>workspaceContains</c> event fires. The owner has not decided it.</summary>
    public const int WorkspaceScanEntries = 20_000;

    /// <summary>Placeholder, OPEN: how many folders deep below an open folder the <c>workspaceContains</c> search goes. The owner has not decided it.</summary>
    public const int WorkspaceScanDepth = 6;

    /// <summary>Placeholder, OPEN: folder names the <c>workspaceContains</c> search passes over. The owner has not decided it.</summary>
    public static readonly string[] WorkspaceScanSkippedFolders = [".git", ".hg", ".svn", "node_modules"];

    public static PluginHostOptions Host() => new()
    {
        IdleStopAfter = TimeSpan.FromSeconds(IdleStopSeconds),
        ShutdownTotal = TimeSpan.FromSeconds(ShutdownTotalSeconds),
        WorkspaceScan = new WorkspaceScanLimits(WorkspaceScanEntries, WorkspaceScanDepth, WorkspaceScanSkippedFolders),
        EventWindow = EventWindow,
        Limits = new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 8, MaxPendingOutgoing: 64, MaxResultItems: 100_000, MaxSessionBytes: 64L << 20, MaxOutgoingQueueBytes: 64L << 20),
        Restart = new RestartPolicy(MaxCrashes: 3, Window: TimeSpan.FromMinutes(5), BaseDelay: TimeSpan.FromSeconds(1), MaxDelay: TimeSpan.FromSeconds(30)),
        ApiVersion = 1,
        HandshakeTimeout = TimeSpan.FromSeconds(10),
        PingInterval = TimeSpan.FromSeconds(5),
        PingTimeout = TimeSpan.FromSeconds(5),
        MissedPingsBeforeKill = 3,
        ShutdownGrace = TimeSpan.FromSeconds(3),
        StderrTailLines = 20,
        ParentCheck = new ParentCheckPolicy(TimeSpan.FromSeconds(ParentCheckSeconds), TimeSpan.FromMilliseconds(StartTimeToleranceMilliseconds)),
    };

    /// <summary>Placeholder: entries kept in the plugin call log.</summary>
    public const int CallLogCapacity = 1000;

    /// <summary>Placeholder: how long Tab waits for a plugin before it indents as without one. Settable so tests on a loaded machine do not race it.</summary>
    internal static TimeSpan TabTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Placeholder: changes (edits of a file, creates, renames, deletes) one workspace edit may hold. OPEN: the owner has not decided it.</summary>
    public const int WorkspaceEditChanges = 200;

    /// <summary>Placeholder: largest file a workspace edit may create, in bytes. OPEN: the owner has not decided it.</summary>
    public const long WorkspaceEditFileBytes = 4L << 20;

    /// <summary>Placeholder: lines of the content of a new file the change review shows; the rest is counted. OPEN: the owner has not decided it.</summary>
    public const int NewFilePreviewLines = 30;

    /// <summary>Placeholder: characters of one line of the preview of a new file; a longer line is cut. OPEN: the owner has not decided it.</summary>
    public const int NewFilePreviewLineChars = 200;

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
        DiffActions = new DiffActionOptions(DiffActionsPerTab, DiffActionTextLength),
        MaxDiffBytes = DiffMaxSideBytes,
        Sources = new FileSourceOptions(TimeSpan.FromMilliseconds(SourceTimeoutMilliseconds), SourceMaxBytes, SourceMaxEntries, SourceSchemesPerPlugin, SourceCacheEntries, SourceCacheBytes, SourceVersionMaxChars, SourceQueryMaxChars),
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
        VirtualSaveTimeout = TimeSpan.FromMilliseconds(VirtualSaveTimeoutMilliseconds),
        MaxDiagnosticsPerDocument = DiagnosticsPerDocument,
        MaxCompletionItems = CompletionItemsPerPlugin,
        MaxCompletionEdits = CompletionEditsPerItem,
        CompletionTimeout = TimeSpan.FromMilliseconds(CompletionTimeoutMilliseconds),
        MaxHoverChars = HoverMaxChars,
        Tools = tools,
        MaxReadBytes = DocumentReadMaxBytes,
        MaxFindMatches = DocumentFindMaxMatches,
        MaxSnapshotsPerPlugin = SnapshotsPerPlugin,
        SnapshotLifetime = TimeSpan.FromSeconds(SnapshotLifetimeSeconds),
        MaxSnapshotBytesPerPlugin = SnapshotBytesPerPlugin,
        MaxMapItems = MapItemsPerCall,
        EventQueueCapacity = EventQueueCapacity,
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

    /// <summary>Placeholder, OPEN: proposals of the MCP assistant that may wait for the decision of the user at once; a further one is refused until the user decides on one. The owner has not decided it.</summary>
    public const int McpMaxPendingProposals = 10;

    /// <summary>Placeholder, OPEN: most bytes of a selection that Share Selection with MCP copies into its read-only excerpt; a larger selection is refused with a message. The owner has not decided it.</summary>
    public const int McpMaxSelectionShareBytes = 1 << 20;

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
        MaxPendingProposals = McpMaxPendingProposals,
        TitleOf = titleOf,
        EnsureOutline = ensureOutline,
    };

    /// <summary>Placeholder: longest time a connection to the IDE bridge gets to send its upgrade request, in seconds.</summary>
    public const int IdeHandshakeSeconds = 5;

    /// <summary>Placeholder: largest upgrade request the IDE bridge reads, in bytes.</summary>
    public const int IdeMaxHeaderBytes = 8 * 1024;

    /// <summary>Placeholder, OPEN: longest name of a connected /ide agent that is kept for the status bar, in UTF-16 units; a longer name is cut. The owner has not decided it.</summary>
    public const int IdeMaxAgentNameChars = 40;

    public static IdeBridgeOptions Ide(McpServer server, string lockDirectory) => new()
    {
        Server = server,
        LockDirectory = lockDirectory,
        HandshakeTimeout = TimeSpan.FromSeconds(IdeHandshakeSeconds),
        MaxHeaderBytes = IdeMaxHeaderBytes,
        MaxAgentNameChars = IdeMaxAgentNameChars,
    };

    /// <summary>Placeholder: longest wait for a server tried from the list of agent servers, for its handshake and for its list of tools.</summary>
    public const int McpClientTimeoutSeconds = 30;

    /// <summary>Placeholder: time a server tried from the list gets to leave after its input is closed, before it is killed.</summary>
    public const int McpClientExitGraceSeconds = 5;

    /// <summary>Placeholder: last lines of a server's standard error kept for the report of its end.</summary>
    public const int McpClientStderrLines = 20;

    /// <summary>Placeholder, OPEN: longest wait for the answer to a list, a tool call or the read of a resource of a connected MCP server; a slower server is told to stop and the call ends with a message. The owner has not decided it.</summary>
    public const int McpClientCallTimeoutSeconds = 120;

    /// <summary>Placeholder, OPEN: most characters of the answer of an MCP tool or resource that are shown; the rest is cut off and the message says how long the answer was. The owner has not decided it.</summary>
    public const int McpClientMaxResultChars = 256 * 1024;

    public static NksHub.Textida.McpClient.McpClientOptions McpClient() => new(
        new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 100_000, MaxSessionBytes: 16L << 20, MaxOutgoingQueueBytes: 16L << 20),
        TimeSpan.FromSeconds(McpClientTimeoutSeconds), TimeSpan.FromSeconds(McpClientExitGraceSeconds), McpClientStderrLines,
        "textida", typeof(PluginLimitsPlaceholders).Assembly.GetName().Version?.ToString(3) ?? "0",
        TimeSpan.FromSeconds(McpClientCallTimeoutSeconds), McpClientMaxResultChars);

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

    /// <summary>Placeholder, OPEN: seconds an agent program gets to print its version (<c>--version</c>) before it is ended and the version counts as unknown. The owner has not decided it.</summary>
    public const int AcpVersionCheckTimeoutSeconds = 15;

    public static NksHub.Textida.Acp.AcpOptions Acp() => new(
        new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 100_000, MaxSessionBytes: 16L << 20, MaxOutgoingQueueBytes: 16L << 20),
        TimeSpan.FromSeconds(AcpStartTimeoutSeconds), TimeSpan.FromSeconds(AcpIdleTimeoutSeconds), TimeSpan.FromSeconds(AcpExitGraceSeconds), AcpStderrLines,
        "textida", typeof(PluginLimitsPlaceholders).Assembly.GetName().Version?.ToString(3) ?? "0");

    /// <summary>Placeholder, OPEN: how long a plugin has to end a task the user cancelled before the editor ends it itself. The owner has not decided it.</summary>
    public const int TaskCancelGraceMilliseconds = 5000;

    /// <summary>Placeholder, OPEN: largest file of a saved macro that is read, in bytes; a larger one is listed as damaged with the reason and not read. The owner has not decided it.</summary>
    public const int MacroMaxFileBytes = 1 << 20;

    public static UiApiOptions Ui() => new()
    {
        MaxPanelsPerPlugin = 4,
        MaxItemsPerPanel = 5000,
        MaxTreeDepth = 16,
        MaxTasksPerPlugin = 4,
        MaxTextLength = 4000,
        MaxPickItems = 500,
        TaskCancelGrace = TimeSpan.FromMilliseconds(TaskCancelGraceMilliseconds),
    };
}
