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
internal static partial class PluginLimitsPlaceholders
{
    /// <summary>Default: milliseconds between two looks at a followed log for new lines, truncation or rotation. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LogFollowPollMilliseconds = 1000;

    /// <summary>Default: milliseconds a watched folder stays quiet before the files that another program changed in it are looked at for review. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ReviewFolderDelayMilliseconds = 500;

    /// <summary>Default: most files one look at a watched folder takes in; more changed at once are only counted, and the status bar says so. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ReviewFolderMaxFiles = 500;

    /// <summary>Default: most files of the opened folder that wait for the review of a change from outside at once; each holds a pinned revision of the local history. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ReviewFolderMaxWaiting = 200;

    /// <summary>Default: seconds after the editor wrote files itself (Replace in Files, a batch of encodings) in which the watched folder takes no change for a change from outside. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ReviewFolderOwnWriteSeconds = 3;

    /// <summary>Default: most matches a live log view keeps in memory (about 24 bytes each); beyond it the oldest are dropped and the status bar says so. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LogFollowMaxMatches = 1_000_000;

    /// <summary>Default: milliseconds one line may spend in the highlight expression of a log view before its search is cut off. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LogHighlightMatchTimeoutMilliseconds = 50;

    /// <summary>Default: most bytes that the unsaved edits of one large file may insert for a named session to keep them in its file; a larger change is left out and the session says so. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SessionLargeEditsMaxBytes = 16 * 1024 * 1024;

    /// <summary>Default: a file longer than this many bytes opens at once and gets its SHA-256 and block digests from a pass on the thread pool; until the pass ends only size and write time vouch for the file and a save writes the whole file. A shorter file is hashed before it opens. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long OpenHashInBackgroundBytes = 64L << 20;

    /// <summary>Default: how many times an upload or a download is repeated after the connection was lost (each repeat checks the server version again and continues a partial file). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RemoteRetryCount = 3;

    /// <summary>Default: milliseconds before the first repeat of a lost upload or download; every further repeat waits twice as long. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RemoteRetryBaseDelayMilliseconds = 2000;

    /// <summary>Default: a transfer writes a line to the Remote output each time its progress passes a multiple of this many percent (the status bar shows every whole percent). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RemoteProgressLogStepPercent = 10;

    /// <summary>Default: most characters of a document that the HTML and RTF export (file or clipboard) takes; a larger one is refused with a message. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int HighlightExportMaxChars = 8 * 1024 * 1024;

    /// <summary>Default: milliseconds between two updates of the percentage shown while the colors of a large file are read for an export or a copy with formatting. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int HighlightExportProgressMilliseconds = 250;

    /// <summary>Default: parts the error heatmap of the minimap cuts a log into (by byte position); the picture takes the densest bucket of each row. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LogHeatBuckets = 1024;

    /// <summary>Default: bytes the error heatmap reads per request while it counts lines in the background; also the working memory of one pass. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LogHeatChunkBytes = 1 << 20;

    /// <summary>Default: a log up to this size is read whole for the heatmap; a larger one is sampled. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long LogHeatFullScanBytes = 256L << 20;

    /// <summary>Default: bytes read at the start of each bucket when the heatmap samples a log larger than <see cref="LogHeatFullScanBytes"/>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LogHeatSampleBytesPerBucket = 128 << 10;

    /// <summary>Default: milliseconds the heatmap waits after the last edit of a log before it counts again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LogHeatDelayMilliseconds = 1500;

    public static LogHeatLimits LogHeat() => new(LogHeatChunkBytes, LogHeatFullScanBytes, LogHeatSampleBytesPerBucket);

    /// <summary>Default: rows of consecutive lines in one strip of the sampled minimap of a large file; the picture has as many strips as such runs fit. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int MinimapSampleStripRows = 8;

    /// <summary>Default: bytes from the start of a file that the CSV delimiter detection reads. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long CsvDetectSampleBytes = 64 * 1024;

    /// <summary>Default: bytes a wildcard <c>*</c> or an open repeat of a hex search may stand for while the setting <c>hex.wildcardMaxBytes</c> is 0. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int HexWildcardDefaultMaxBytes = 256;

    /// <summary>Default: largest file that gets local history revisions, in bytes (a file of the large file view too: its first revision is a whole copy of it); a larger one gets none and the status bar says so. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long HistoryRevisionMaxBytes = 1L << 30;

    /// <summary>Default: a file larger than this, in bytes, is kept in the local history as a patch against its newest whole copy instead of another whole copy, when the patch is short enough (<see cref="HistoryPatchMaxPercent"/>). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long HistoryPatchAboveBytes = 16L << 20;

    /// <summary>Default: longest patch the local history keeps instead of a whole copy of a large file, in percent of the file; a longer one makes a new whole copy that the following patches are made against. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int HistoryPatchMaxPercent = 50;

    /// <summary>Default: largest revision, in bytes, that Compare and Restore read into memory for a text tab; a larger one is compared in the external diff tool or saved as a copy. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long HistoryTextRevisionMaxBytes = 32L << 20;

    /// <summary>Default: largest selection of a large file in the paged view that Copy and Cut take to the clipboard, in bytes; a larger one is refused with a message and the clipboard stays as it was. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long PagedCopyMaxBytes = 256L << 20;

    /// <summary>Default: longest line of a large file in the paged view that can be edited, in bytes; typing, deleting or pasting in a longer one is refused with a message and the document does not change. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long PagedEditLineMaxBytes = 1L << 30;

    /// <summary>Default: most UTF-16 characters of clipboard text that Paste takes into the paged view (its encoded copy and the clipboard string are both in memory at once); a longer text is refused with a message and the document does not change. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long PagedPasteMaxChars = 32L << 20;

    /// <summary>Default: milliseconds in which the largest measured paste (<see cref="PagedPasteMaxChars"/> characters into a file of 100 MiB) must end on a development PC; only the measuring test reads it. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PagedPasteMeasureMilliseconds = 5000;
    /// <summary>Default: most carets (selections) a large file in the paged view takes; Add Next Occurrence, Select All Occurrences, Add Caret Above and Below and Alt+click stop there and the status bar says so. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PagedMaxCarets = 1000;

    /// <summary>Default: longest selection of a large file in the paged view, in bytes, whose other occurrences Add Next Occurrence and Select All Occurrences look for; a longer one is refused with a message. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PagedOccurrenceMaxBytes = 64 * 1024;

    /// <summary>Default: most bytes a paste inserts over all carets of a large file in the paged view together (the text times the number of carets); a larger paste is refused with a message and the document stays as it was. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long PagedMultiPasteMaxBytes = 64L << 20;

    /// <summary>Default: most 1 MiB blocks of the file that showing one place of a large file in the paged view (a jump to an offset, drawn once) may read, the line index not counted; the measuring tests fail above it. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PagedViewportMaxBlocks = 6;

    /// <summary>Default: bytes (columns for a column) of a long line in the paged view that the UI thread counts to put the caret in it; a place farther than this from the nearest known point of the line is reached by a count on the thread pool, and the caret moves when it ends. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long PagedFarMoveBytes = 16L << 20;

    /// <summary>Default: bytes the paged view looks at, forward from the start of a line or back from an offset, to find the end or the start of one line of a place beyond the line index (a detour); a line longer than this is cut at that length into view lines, and the line numbers there are estimates anyway. The first draw after a jump reads this much for each line on screen. A multiple of 4. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long PagedDetourLineScanBytes = 256L << 10;

    /// <summary>Default: milliseconds within which a second jump of the paged view to lines far from the ones it showed is not colored at once but after the jumps stop (the first jump after a quiet time is colored at once); dragging the minimap makes dozens of jumps a second and each would run a slice of the grammar on the UI thread. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SyntaxJumpSettleMilliseconds = 60;

    /// <summary>Default: lines the matching bracket highlight looks through from the caret line for the partner; a partner farther away is not colored (Go to Matching Bracket still finds it). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int BracketHighlightMaxLines = 5000;

    /// <summary>Default: fewest characters a selection has for its other occurrences to be highlighted; the setting <c>editor.occurrences.minLength</c> starts at this value. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int OccurrenceMinLength = 2;

    /// <summary>Default: most characters of a selection whose other occurrences are highlighted; a longer selection is not looked for. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int OccurrenceMaxChars = 200;

    /// <summary>Default: longest line, in characters, that is searched for occurrences of the selection; a longer line is left plain. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int OccurrenceLineMaxChars = 10_000;

    /// <summary>Default: most occurrences drawn in one draw of the visible lines; beyond it the rest of the screen stays plain. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int OccurrenceMaxDrawn = 500;

    /// <summary>Default: most other occurrences counted and marked in the whole document; the scan stops there and the status bar says "over". Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int OccurrenceMaxMarks = 20_000;

    /// <summary>Default: milliseconds a selection, a text or the view stands still before the occurrences are looked for again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int OccurrenceDebounceMs = 200;

    /// <summary>Default: milliseconds between two announcements of the number of occurrences to a screen reader. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int OccurrenceAnnounceMs = 1500;

    /// <summary>Default: lines before a line of a log that are read to find the ANSI style the lines above left open; a style that stays open longer than this shows in the default style past it. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int AnsiCarryLookbackLines = 200;

    /// <summary>Default: milliseconds between two frames of smooth scrolling (the setting <c>advanced.smoothScrolling</c>). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SmoothScrollFrameMilliseconds = 16;

    /// <summary>Default: share of the way to its target that smooth scrolling covers in one frame; the larger it is, the quicker the glide ends. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const double SmoothScrollShare = 0.3;

    /// <summary>Default: lines one notch of the mouse wheel scrolls (smooth or not in the large file view; the editor's own wheel step is AvaloniaEdit's). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int WheelLinesPerNotch = 3;

    /// <summary>Default: largest file Insert from File reads, in bytes; a larger one is refused with a message. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long InsertFromFileMaxBytes = 16L << 20;

    /// <summary>Default: Reformat Paragraph (Ends at a Short Line) counts a line shorter than this percent of the right margin column as the last of its paragraph. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ReformatShortLinePercent = 50;

    /// <summary>Default: most entries (buttons and separators) the user toolbar holds; the editor refuses to add more and a <c>toolbar.json</c> with more is not read. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ToolbarMaxButtons = 40;

    /// <summary>Size limit of one Copy as AI Context, in UTF-8 bytes of the copied text; files past it are left out and named. 4 MiB is about one million tokens, the largest context window of current models.</summary>
    public const long AiContextMaxBytes = 4L << 20;

    /// <summary>A copy as AI Context larger than this many bytes (about 128 thousand tokens, the common context window) gets a warning in its preview before it is copied.</summary>
    public const long AiContextWarnBytes = 512L << 10;

    /// <summary>Default: seconds a formatter of the save actions (and a format step of a recipe) has when <c>formatTimeoutSeconds</c> in saveActions.json is not set; then the file is saved unformatted. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int FormatOnSaveTimeoutSeconds = 10;

    /// <summary>Default: seconds a linter run has when <c>lintTimeoutSeconds</c> in saveActions.json is not set; then the run is stopped and reported, and the findings of the earlier run stay. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LintTimeoutSeconds = 60;

    /// <summary>Default: milliseconds the structure panel of the hex view waits on the UI thread for the nodes of a tree to be read; a longer read shows a "Reading" row and ends later without holding the window. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int StructureReadGraceMilliseconds = 40;

    /// <summary>Default: seconds a linter's program has to print its version before the run (once per program file); then the version counts as unreadable and the run goes on. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LintVersionTimeoutSeconds = 10;

    /// <summary>Default: milliseconds a linter run started by a save waits for a further save of the same file before it starts; a newer save replaces the waiting run. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LintOnSaveDebounceMilliseconds = 500;

    /// <summary>Default: largest JSON range the built-in formatter handles (format, minify, sort keys, remove trailing commas); a larger one is refused with a message. Also bounded by <see cref="DocumentApiOptions.MaxReadBytes"/>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long JsonFormatterMaxBytes = 1 << 20;

    /// <summary>Default: spaces per level of the built-in formatter. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int JsonFormatterIndent = 2;

    /// <summary>Default: deepest nesting of arrays and objects the built-in JSON formatter accepts; a deeper text is refused with a message. The formatter never goes deeper than 512 whatever is set. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
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

    /// <summary>Default: how many placeholders of a snippet may stand inside one another (<c>${1:a ${2:b}}</c> is two); a deeper one stays in the text as it is. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SnippetNestingDepth = 8;

    /// <summary>Default: milliseconds one regular expression of a snippet transform may run on one text; a slower one stops the snippet with a message, and a mirror it feeds stays as it was. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SnippetTransformTimeoutMilliseconds = 100;

    public static NksHub.Textida.Core.Snippets.SnippetLimits Snippets() => new(SnippetNestingDepth, SnippetLength, TimeSpan.FromMilliseconds(SnippetTransformTimeoutMilliseconds));

    /// <summary>Placeholder: largest secrets file (<c>.env</c>) whose values are looked for when a plugin reads it; a larger one is hidden whole.</summary>
    public const long SecretScanMaxBytes = 16L << 20;

    // The limits of a plugin folder (grammar files, the size and the entries of a ZIP, the time the plugin validator waits) are in PluginLimitsPlaceholders.Plugins.cs,
    // which the plugin validator program compiles as well, so the editor and the validator read one set of numbers.

    /// <summary>Placeholder: bytes the web data plugin (colours, key paths) reads per request. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int WebDataChunkBytes = 1 << 20;

    /// <summary>Placeholder: largest file the web data plugin reads whole to find colours or keys.</summary>
    public const int WebDataMaxBytes = 4 << 20;

    /// <summary>Placeholder: keys of one file published as symbols (outline, Go to Anything).</summary>
    public const int SymbolsPerDocument = 5000;

    /// <summary>Default: most inlay hints, and most semantic tokens, one plugin may publish for one document. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int DecorationsPerDocument = 20000;

    /// <summary>Default: milliseconds the language server plugin waits after an edit (or after the server asked for a refresh) before it asks again for the inlay hints and semantic tokens that are shown. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspDecorationRefreshMilliseconds = 600;

    /// <summary>Placeholder: longest symbol id, name or kind, in UTF-16 units.</summary>
    public const int SymbolTextLength = 400;

    /// <summary>Placeholder: how long the caret rests before the status bar asks for the key path under it.</summary>
    public const int KeyPathDebounceMilliseconds = 150;

    /// <summary>Placeholder: bytes the Code Explorer reads per request. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int CodeExplorerChunkBytes = 1 << 20;

    /// <summary>Default: milliseconds one outline rule may spend on a text; past it the file gets no outline. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
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

    /// <summary>Default: entries (changed, new, ignored files and folders) the Git plugin answers with when the file tree asks for the state of its files; the heaviest come first and the rest has no colour. The answer is one message, so about 60 bytes an entry must stay below the message limit of the plugin. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitTreeEntries = 20_000;

    /// <summary>Placeholder: bytes the Git plugin reads per request when it takes the staged index document back. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int GitChunkBytes = 1 << 20;

    /// <summary>Default: quiet time, in milliseconds, after a change in the working tree or in the index of the repository before the Git status panel and status bar are read again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitWatchDebounceMilliseconds = 500;

    /// <summary>Default: quiet time, in milliseconds, after an edit, a save or a change of the repository before the change marks in the gutter of the open file are asked for again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitGutterDebounceMilliseconds = 600;

    /// <summary>Default: largest document, in bytes, that gets change marks in the gutter; a larger one gets none. Must not exceed <see cref="VirtualDocumentMaxBytes"/>, which is what the Git plugin reads. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long GitGutterMaxBytes = 1L << 20;

    /// <summary>Default: most old lines of one change the peek at it holds; a change with more lines cannot be reverted from the peek. Must stay below the message limit of the plugin (about 100 bytes a line is assumed). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitPeekMaxLines = 2000;

    /// <summary>Default: rows of the peek at a change that are shown at once; more are reached by scrolling inside the peek. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitPeekVisibleRows = 14;

    /// <summary>Default: time, in milliseconds, after which fetch, pull and push that have not finished are ended; 0 gives them no limit, and the user cancels them in the task row. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitNetworkTimeoutMilliseconds = 0;

    /// <summary>Default: commits the Git history panel lists for a file or a repository; the panel holds at most <see cref="Ui"/> items. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitLogEntries = 500;

    /// <summary>Default: messages of the last commits the commit panel offers again in its list of earlier messages. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitMessageHistory = 20;

    /// <summary>Default: quiet time, in milliseconds, after a change of the repository before the commit panel reads the list of changed files again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitCommitReloadMilliseconds = 400;

    /// <summary>Default: quiet time, in milliseconds, with the caret on one line before the author, time and subject of that line are asked for. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitBlameDebounceMilliseconds = 500;

    /// <summary>Default: longest subject of a commit, in characters, that the status bar shows for the line at the caret; the tooltip shows all of it. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int GitBlameSubjectChars = 60;

    /// <summary>Default: most buttons a plugin may add to a comparison tab it opens. The commands <c>nks.compare.action1</c> to <c>action4</c> reach four of them, the digit keys nine. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int DiffActionsPerTab = 4;

    /// <summary>Default: longest key, command and title of a comparison tab a plugin opens, in UTF-16 units. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int DiffActionTextLength = 200;

    /// <summary>Default: largest side of a diff a plugin asks for (compute, open, stage), in bytes: a document, a snapshot, text of the plugin or a file; a larger one is refused with a quota error. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long DiffMaxSideBytes = 16L << 20;

    /// <summary>Placeholder: how long a document rests after an edit before its outline is read again.</summary>
    public const int OutlineRefreshMilliseconds = 400;

    /// <summary>Default: bytes Emmet reads in front of a caret to find the abbreviation, its line and the CSS block it stands in. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int EmmetContextBytes = 64 * 1024;

    /// <summary>Default: characters of the expansion the Emmet preview shows before it cuts the rest off. Must stay below the message length of <see cref="Ui"/>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int EmmetPreviewChars = 3000;

    /// <summary>Placeholder: most words the completion list (Ctrl+J) holds; the best ranked are kept.</summary>
    public const int CompletionMaxWords = 200;

    /// <summary>Default: characters on each side of the caret in which the completion list (Ctrl+J) ranks the words of the document being edited by their distance from the caret; a word that occurs only farther away ranks by how often it occurs. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int CompletionNearChars = 20_000;

    /// <summary>Placeholder: longest text (and HTML) one plugin write puts on the clipboard, in UTF-16 units; the same provisional bound the clipboard history uses.</summary>
    public const int ClipboardMaxChars = ClipboardHistory.MaxItemLength;

    /// <summary>Placeholder: bytes one inspection call (file facts, log query) reads before it stops and says where to go on.</summary>
    public const long InspectionMaxScanBytes = 256L << 20;

    /// <summary>Default: largest range one <c>document/readBytes</c> or <c>document/readText</c> returns, in bytes; a larger one is refused. The plugin chunk sizes above must not exceed it. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int DocumentReadMaxBytes = 1 << 20;

    /// <summary>Default: matches one <c>document/find</c> returns before it says <c>truncated</c>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int DocumentFindMaxMatches = 1000;

    /// <summary>Default: snapshots one plugin may hold at once; a further one is refused with a quota error. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SnapshotsPerPlugin = 4;

    /// <summary>Default: seconds a snapshot lives after its last read, then it is released. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SnapshotLifetimeSeconds = 30;

    /// <summary>Default: bytes the snapshots of one plugin may hold in memory together (a snapshot of a mapped file counts only its piece list, a copy counts all of it); a snapshot over it is refused with a quota error. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long SnapshotBytesPerPlugin = 64L << 20;

    /// <summary>Default: ranges or positions one <c>document/mapRanges</c> or <c>document/mapPositions</c> call maps; a larger call is refused. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int MapItemsPerCall = 10_000;

    /// <summary>Placeholder: virtual documents (read only tabs a plugin supplies) one plugin keeps open at once.</summary>
    public const int VirtualDocumentsPerPlugin = 8;

    /// <summary>Placeholder: largest content of a virtual document, in bytes.</summary>
    public const long VirtualDocumentMaxBytes = 4L << 20;

    /// <summary>Default: how long the editor waits for a plugin to take the save of an editable virtual document before the tab stays unsaved. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int VirtualSaveTimeoutMilliseconds = 15_000;

    /// <summary>Placeholder: how long the editor waits for a file source (list, read, stat, write) before it counts the source as not reachable. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SourceTimeoutMilliseconds = 15_000;

    /// <summary>Placeholder: largest file a source serves or takes, in bytes. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long SourceMaxBytes = 16L << 20;

    /// <summary>Placeholder: entries of one folder listing taken from a source. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SourceMaxEntries = 5000;

    /// <summary>Placeholder: URI schemes one plugin may serve. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SourceSchemesPerPlugin = 4;

    /// <summary>Placeholder: files of the source cache (memory only, least recently used out first). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SourceCacheEntries = 32;

    /// <summary>Placeholder: bytes of the source cache. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long SourceCacheBytes = 64L << 20;

    /// <summary>Default: longest version text a source may state about its adapter and about its service. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SourceVersionMaxChars = 64;

    /// <summary>Default: most characters of the words a search in a source may send. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SourceQueryMaxChars = 200;

    /// <summary>Placeholder: diagnostics one checker (source) of a plugin may publish for one document.</summary>
    public const int DiagnosticsPerDocument = 2000;

    /// <summary>Placeholder: completion items taken from one plugin's answer.</summary>
    public const int CompletionItemsPerPlugin = 200;

    /// <summary>Placeholder: additional edits (imports, <c>use</c>) one completion item may carry.</summary>
    public const int CompletionEditsPerItem = 16;

    /// <summary>Placeholder: how long the completion list waits for the plugins before it shows what it has.</summary>
    public const int CompletionTimeoutMilliseconds = 1000;

    /// <summary>Placeholder: how long a document rests after an edit before the schema check runs again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SchemaCheckDebounceMilliseconds = 500;

    /// <summary>Placeholder: largest file the schema check and the schema key completion read whole. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>.</summary>
    public const int SchemaMaxBytes = 1 << 20;

    /// <summary>Placeholder: deepest nesting the schema check follows in a file and in a schema.</summary>
    public const int SchemaMaxDepth = 64;

    /// <summary>Placeholder: time one <c>pattern</c> of a schema may take on one value before it counts as not matching.</summary>
    public const int SchemaRegexTimeoutMilliseconds = 200;

    /// <summary>Default: longest chain of schema documents a <c>$ref</c> may lead through, counted from the schema of the file; 0 would follow no reference to another document. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SchemaRefMaxDepth = 4;

    /// <summary>Default: most other schema documents (files or downloads) the references of one schema may pull in together. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SchemaRefMaxDocuments = 16;

    /// <summary>Placeholder: largest file handed to a language server; a larger one never starts one. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspMaxBytes = 1 << 20;

    /// <summary>Placeholder: time a language server has to answer <c>initialize</c>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspStartTimeoutMilliseconds = 30_000;

    /// <summary>Placeholder: time a language server has to answer a definition or references request. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspRequestTimeoutMilliseconds = 10_000;

    /// <summary>Placeholder: time a language server gets to shut down and leave before it is killed. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspStopTimeoutMilliseconds = 3000;

    /// <summary>Placeholder: places listed for one definition or references request. Must not exceed the items per panel of <see cref="Ui"/>.</summary>
    public const int LspMaxLocations = 500;

    /// <summary>Placeholder: last lines of a language server's standard error kept for the report of its end.</summary>
    public const int LspStderrLines = 20;

    /// <summary>Placeholder: time a language server keeps running after the last document of its language was closed. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspIdleStopMilliseconds = 300_000;

    /// <summary>Default: how many times in a row a language server that ended unexpectedly is started again by itself before the editor gives up and leaves it to the next command; 0 never starts it by itself. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspRestartMaxAttempts = 3;

    /// <summary>Default: milliseconds before the first automatic start of a language server that ended unexpectedly; every further start waits twice as long, up to <see cref="LspRestartMaxDelayMilliseconds"/>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspRestartBaseDelayMilliseconds = 1000;

    /// <summary>Default: longest wait between two automatic starts of a language server. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspRestartMaxDelayMilliseconds = 30_000;

    /// <summary>Default: milliseconds a language server must have run before it ended for the next crash to count as a first one again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspRestartStableMilliseconds = 60_000;

    /// <summary>Placeholder: folders searched upwards for the project root of a document for a language server, the folder of the file being the first. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int LspRootSearchLevels = 16;

    /// <summary>Placeholder: time one package registry request may take. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RegistryTimeoutMilliseconds = 10_000;

    /// <summary>Placeholder: largest answer of a package registry that is read; a larger one is an error. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long RegistryMaxResponseBytes = 8L << 20;

    /// <summary>Placeholder: names asked of a registry for one name completion. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RegistryPageSize = 20;

    /// <summary>Placeholder: packages one check of a manifest asks the registry about; the rest stay unchecked. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RegistryMaxLookups = 50;

    /// <summary>Default: bytes of registry answers the cache of the JSON plugin holds; the oldest answers go first and one larger than this is not kept. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long RegistryCacheMaxBytes = 16L << 20;

    /// <summary>Default: bytes the local copies of remote files may take; at start the least recently used copies without unsent changes go until the total fits. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long RemoteCacheMaxBytes = 2L << 30;

    /// <summary>Default: days a local copy of a remote file may stay unused before it is removed at start (a copy with unsent changes never is). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RemoteCacheMaxAgeDays = 90;

    /// <summary>Default: seconds a cached registry answer is used; an older one is asked for again (Refresh Package Versions drops the cache at any time). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RegistryCacheLifetimeSeconds = 3600;

    /// <summary>Default: most redirects the JSON plugin follows for one registry or schema request that goes through the editor (a network proxy with a login); the editor passes a redirect back instead of following it. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RegistryMaxRedirects = 5;

    /// <summary>Default: milliseconds the proxy of the network policy is not copied again to the consent file the JSON plugin reads while schemas are checked. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int RegistryProxySyncMilliseconds = 30_000;

    /// <summary>Default: bytes the copies of downloaded schemas take on disk together; the oldest copies are deleted to make room. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long SchemaCacheMaxBytes = 16L << 20;

    /// <summary>Default: largest schema that is downloaded and kept; a larger answer is refused. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long SchemaDownloadMaxBytes = 4L << 20;

    /// <summary>Default: seconds a downloaded schema is used without asking its host again; after that the host is asked, and the copy is used only if the host does not answer. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SchemaCacheMaxAgeSeconds = 7 * 24 * 3600;

    /// <summary>Default: seconds a host that did not answer for a schema is left alone before it is asked again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int SchemaRetrySeconds = 60;

    /// <summary>Default: milliseconds the pointer rests on text before the hover description is asked for. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int HoverDelayMilliseconds = 400;

    /// <summary>Default: longest hover description taken from a plugin, in UTF-16 units; a longer one is cut. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int HoverMaxChars = 2000;

    /// <summary>Placeholder: document events that wait for one plugin; beyond it the rest of a document's events become one <c>document/eventsLost</c>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int EventQueueCapacity = 256;

    /// <summary>Placeholder: events a plugin may have sent to it and not handled yet before the editor holds its document events back. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int EventWindow = 32;

    /// <summary>Default: time one <c>net/request</c> of a plugin may take, answer included. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int NetworkTimeoutMilliseconds = 30_000;

    /// <summary>Default: largest answer body of one <c>net/request</c>. It travels base64 encoded, so it stays well under the 4 MiB message limit of <see cref="Host"/>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int NetworkMaxResponseBytes = 2 << 20;

    /// <summary>Default: seconds after which a tool with no <c>timeoutSeconds</c> of its own is ended while the setting <c>tools.timeoutSeconds</c> is 0. 0 means no limit, which is how tools ran before. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ToolDefaultTimeoutSeconds = 0;

    /// <summary>Default: the encoding the output of a tool is read in while its definition names none and the setting <c>tools.outputEncoding</c> is empty. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const string ToolOutputEncoding = "utf-8";

    /// <summary>Default: folders looked into for the nearest Makefile, counting the folder of the open file and those above it. The search also stops at the root of the project. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int MakefileSearchLevels = 8;

    /// <summary>Default: largest Makefile in bytes that is read from disk to list its targets; a larger one is refused with a message. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long MakefileMaxBytes = 4L << 20;

    public static PluginNetworkOptions Network() => new(TimeSpan.FromMilliseconds(NetworkTimeoutMilliseconds), NetworkMaxResponseBytes);

    /// <summary>Default: seconds a plugin that asked for it (idle stop) may have no work before the editor ends its process; the next activation event starts it again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int IdleStopSeconds = 300;

    /// <summary>Default: seconds all plugins together may take to leave when the editor closes; a process still alive then is killed by its process id. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ShutdownTotalSeconds = 4;

    /// <summary>Default: seconds the exit of the editor waits for the plugin services (agents, the MCP server, the plugins) to finish before it kills the plugin processes that are left. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ExitWaitSeconds = 8;

    /// <summary>Default: seconds between two looks of a plugin process at the editor that started it; a plugin ends by itself when the editor is gone (the system ends the child processes of a killed editor on Windows; on macOS and Linux this is what is left). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int ParentCheckSeconds = 2;

    /// <summary>Default: milliseconds two readings of the start time of one process may differ by and still be that process. A process id whose start time is farther off belongs to another program and is never killed or waited for. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int StartTimeToleranceMilliseconds = 2000;

    /// <summary>Default: the subfolder of the profile where macOS and Linux list the child processes of the editor, so the next start can end the ones a killed editor left behind. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const string ChildListFolder = "children";

    /// <summary>Default: files and folders looked at over all open folders to see whether a plugin's <c>workspaceContains</c> event fires. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int WorkspaceScanEntries = 20_000;

    /// <summary>Default: how many folders deep below an open folder the <c>workspaceContains</c> search goes. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int WorkspaceScanDepth = 6;

    /// <summary>Default: folder names the <c>workspaceContains</c> search passes over. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public static readonly string[] WorkspaceScanSkippedFolders = [".git", ".hg", ".svn", "node_modules"];

    public static PluginHostOptions Host() => new()
    {
        IdleStopAfter = TimeSpan.FromSeconds(IdleStopSeconds),
        ShutdownTotal = TimeSpan.FromSeconds(ShutdownTotalSeconds),
        WorkspaceScan = new WorkspaceScanLimits(WorkspaceScanEntries, WorkspaceScanDepth, WorkspaceScanSkippedFolders),
        EventWindow = EventWindow,
        Limits = new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 8, MaxPendingOutgoing: 64, MaxResultItems: 100_000, MaxSessionBytes: 64L << 20, MaxOutgoingQueueBytes: 64L << 20),
        Restart = new RestartPolicy(MaxCrashes: 3, Window: TimeSpan.FromMinutes(5), BaseDelay: TimeSpan.FromSeconds(1), MaxDelay: TimeSpan.FromSeconds(30)),
        ApiVersion = NksHub.Textida.PluginSdk.PluginApi.Version,
        HandshakeTimeout = HandshakeTimeout,
        PingInterval = TimeSpan.FromSeconds(5),
        PingTimeout = TimeSpan.FromSeconds(5),
        MissedPingsBeforeKill = 3,
        ShutdownGrace = TimeSpan.FromSeconds(3),
        StderrTailLines = 20,
        ParentCheck = new ParentCheckPolicy(TimeSpan.FromSeconds(ParentCheckSeconds), TimeSpan.FromMilliseconds(StartTimeToleranceMilliseconds)),
    };

    /// <summary>Placeholder: entries kept in the plugin call log.</summary>
    public const int CallLogCapacity = 1000;

    /// <summary>Placeholder: how long a started plugin process has to answer the handshake. Settable so tests on a loaded machine do not race the start of a process.</summary>
    internal static TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Placeholder: how long Tab waits for a plugin before it indents as without one. Settable so tests on a loaded machine do not race it.</summary>
    internal static TimeSpan TabTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Placeholder: changes (edits of a file, creates, renames, deletes) one workspace edit may hold. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int WorkspaceEditChanges = 200;

    /// <summary>Placeholder: largest file a workspace edit may create, in bytes. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long WorkspaceEditFileBytes = 4L << 20;

    /// <summary>Placeholder: bytes of new text (replacement texts and the content of new files) all changes of one workspace edit may bring together, so many changes cannot hold the memory of the editor. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long WorkspaceEditTotalBytes = 64L << 20;

    /// <summary>Placeholder: lines of the content of a new file the change review shows; the rest is counted. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int NewFilePreviewLines = 30;

    /// <summary>Placeholder: characters of one line of the preview of a new file; a longer line is cut. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int NewFilePreviewLineChars = 200;

    /// <summary>Placeholder: longest text of one preview a plugin shows, in UTF-16 units. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewMaxChars = 1 << 20;

    /// <summary>Placeholder: largest image of a preview, in decoded bytes of its <c>data:</c> URI or of the bytes a plugin attaches. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewImageMaxBytes = 1 << 20;

    /// <summary>Placeholder: most pictures one preview of a plugin attaches. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewImagesPerPreview = 32;

    /// <summary>Placeholder: largest total of the pictures one preview of a plugin attaches, in bytes. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewImagesTotalBytes = 8 << 20;

    /// <summary>Placeholder: largest picture the preview reads from a local file or a web address, in bytes. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewFileImageMaxBytes = 8 << 20;

    /// <summary>Placeholder: most pixels (width times height) of a picture the preview decodes, whatever its size in bytes. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long PreviewImageMaxPixels = 40_000_000;

    /// <summary>Placeholder: decoded pictures a window keeps so that drawing the preview again does not read or fetch them again. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewImageCacheEntries = 32;

    /// <summary>Placeholder: longest time one remote picture of a preview may take, in milliseconds. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewRemoteImageTimeoutMs = 10_000;

    /// <summary>Placeholder: how long a web picture that failed is left alone before drawing the preview asks for it again, in milliseconds. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long PreviewRemoteImageRetryMs = 30_000;

    /// <summary>Placeholder: time after the editor or the preview was scrolled by the other during which scrolling it again is not passed back, in milliseconds. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewSyncQuietMs = 150;

    /// <summary>Placeholder: most nodes the HTML reader of the preview creates; the rest of the text stays text. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewHtmlMaxNodes = 20_000;

    /// <summary>Placeholder: deepest nesting of HTML tags the preview reads; a deeper tag stays text. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewHtmlMaxDepth = 64;

    /// <summary>Placeholder: most nodes of a Mermaid flowchart the preview draws; a larger one is shown as source. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewMermaidMaxNodes = 60;

    /// <summary>Placeholder: most connections of a Mermaid flowchart the preview draws; a larger one is shown as source. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewMermaidMaxEdges = 120;

    /// <summary>Placeholder: quiet time after an edit before the live preview of the current file is drawn again, in milliseconds. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewLiveDebounceMs = 300;

    /// <summary>Placeholder: longest text, in characters, that the live preview of the current file draws. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int PreviewLiveMaxChars = 1 << 20;

    /// <summary>Placeholder: largest picture file the picture viewer reads and decodes, in bytes; a larger one shows a message and the bar offers text and hex. The pixel limit is <see cref="PreviewImageMaxPixels"/>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long ImageViewMaxBytes = 64L << 20;

    /// <summary>Placeholder: smallest zoom of the picture viewer, as a fraction of the real size. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const double ImageViewMinZoom = 0.02;

    /// <summary>Placeholder: largest zoom of the picture viewer, as a multiple of the real size. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const double ImageViewMaxZoom = 32;

    /// <summary>Placeholder: factor of one zoom step of the picture viewer (a key press or a notch of the wheel). Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const double ImageViewZoomStep = 1.25;

    /// <summary>Placeholder: longest text of one update, tool title or permission question a plugin sends to the agent panel, in UTF-16 units. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int AgentUpdateMaxChars = 64 * 1024;

    public static DocumentApiOptions Documents(IEditProposals proposals, IEditorTools tools, PluginPermissions permissions, PluginCallLog callLog, Func<Guid, LogLevelRules?> logRules, WorkspaceEditOptions? workspace = null,
        IPreviewPanel? preview = null, IAgentPanel? agent = null) => new()
    {
        Workspace = workspace,
        Preview = preview is null ? null : new PreviewOptions(preview, PreviewMaxChars, PreviewImagesPerPreview, PreviewImageMaxBytes, PreviewImagesTotalBytes),
        Agent = agent is null ? null : new AgentPanelOptions(agent, AgentUpdateMaxChars),
        DiffActions = new DiffActionOptions(DiffActionsPerTab, DiffActionTextLength),
        MaxDiffBytes = DiffMaxSideBytes,
        Sources = new FileSourceOptions(TimeSpan.FromMilliseconds(SourceTimeoutMilliseconds), SourceMaxBytes, SourceMaxEntries, SourceSchemesPerPlugin, SourceCacheEntries, SourceCacheBytes, SourceVersionMaxChars, SourceQueryMaxChars),
        LogRules = logRules,
        MaxSymbolsPerDocument = SymbolsPerDocument,
        MaxSymbolTextLength = SymbolTextLength,
        MaxSnippetLength = SnippetLength,
        MaxSnippetCarets = SnippetCarets,
        Snippets = Snippets(),
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
        MaxDecorationsPerDocument = DecorationsPerDocument,
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

    /// <summary>Default: proposals of the MCP assistant that may wait for the decision of the user at once; a further one is refused until the user decides on one. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int McpMaxPendingProposals = 10;

    /// <summary>Default: most bytes of a selection that Share Selection with MCP copies into its read-only excerpt; a larger selection is refused with a message. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
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

    /// <summary>Default: longest name of a connected /ide agent that is kept for the status bar, in UTF-16 units; a longer name is cut. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
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

    /// <summary>Default: longest wait for the answer to a list, a tool call or the read of a resource of a connected MCP server; a slower server is told to stop and the call ends with a message. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int McpClientCallTimeoutSeconds = 120;

    /// <summary>Default: most characters of the answer of an MCP tool or resource that are shown; the rest is cut off and the message says how long the answer was. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int McpClientMaxResultChars = 256 * 1024;

    public static NksHub.Textida.McpClient.McpClientOptions McpClient() => new(
        new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 100_000, MaxSessionBytes: 16L << 20, MaxOutgoingQueueBytes: 16L << 20),
        TimeSpan.FromSeconds(McpClientTimeoutSeconds), TimeSpan.FromSeconds(McpClientExitGraceSeconds), McpClientStderrLines,
        "textida", typeof(PluginLimitsPlaceholders).Assembly.GetName().Version?.ToString(3) ?? "0",
        TimeSpan.FromSeconds(McpClientCallTimeoutSeconds), McpClientMaxResultChars);

    /// <summary>Placeholder: time an agent has to answer <c>initialize</c> and <c>session/new</c>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int AcpStartTimeoutSeconds = 30;

    /// <summary>Placeholder: a turn in which the agent says and asks nothing for this long ends as timed out; waiting for the user does not count. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int AcpIdleTimeoutSeconds = 300;

    /// <summary>Placeholder: time an agent gets to leave after its input is closed, and to answer a cancelled turn, before it is killed or given up on. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int AcpExitGraceSeconds = 5;

    /// <summary>Placeholder: last lines of an agent's standard error kept for the report of its end.</summary>
    public const int AcpStderrLines = 20;

    /// <summary>Placeholder: largest text file (bytes of the content) handed to an agent in one <c>fs/read_text_file</c>. Must not exceed <see cref="DocumentApiOptions.MaxReadBytes"/>. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int AcpMaxReadBytes = 1 << 20;

    /// <summary>Default: seconds an agent program gets to print its version (<c>--version</c>) before it is ended and the version counts as unknown. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int AcpVersionCheckTimeoutSeconds = 15;

    public static NksHub.Textida.Acp.AcpOptions Acp() => new(
        new RpcLimits(MaxMessageBytes: 4 << 20, MaxConcurrentIncoming: 4, MaxPendingOutgoing: 8, MaxResultItems: 100_000, MaxSessionBytes: 16L << 20, MaxOutgoingQueueBytes: 16L << 20),
        TimeSpan.FromSeconds(AcpStartTimeoutSeconds), TimeSpan.FromSeconds(AcpIdleTimeoutSeconds), TimeSpan.FromSeconds(AcpExitGraceSeconds), AcpStderrLines,
        "textida", typeof(PluginLimitsPlaceholders).Assembly.GetName().Version?.ToString(3) ?? "0");

    /// <summary>Default: how long a plugin has to end a task the user cancelled before the editor ends it itself. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TaskCancelGraceMilliseconds = 5000;

    /// <summary>Default: largest file of a saved macro that is read, in bytes; a larger one is listed as damaged with the reason and not read. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int MacroMaxFileBytes = 1 << 20;

    /// <summary>Default: largest hooks file, in bytes, that the hooks editor reads; a larger one is refused with a message. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int HooksMaxFileBytes = 1 << 20;

    /// <summary>Default: deepest JSON nesting the hooks editor reads. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int HooksMaxDepth = 32;

    /// <summary>Default: milliseconds a matcher pattern of a hook may take on one value in the hooks editor before it counts as invalid. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int HooksRegexTimeoutMilliseconds = 100;

    public static NksHub.Textida.Core.Agents.Hooks.HookLimits Hooks() => new(HooksMaxDepth, TimeSpan.FromMilliseconds(HooksRegexTimeoutMilliseconds));

    /// <summary>Default: bytes of a selection in paged mode that are decoded to count its tokens for the status bar; a larger selection is counted by its bytes. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long TokenEstimateMaxSelectionBytes = 1 << 20;

    /// <summary>Default: largest line of an agent transcript, in bytes, that is read and parsed whole while the index is built; a longer one is classified from its start and read only when it is shown. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptWholeLineBytes = 1 << 20;

    /// <summary>Default: bytes from the start of a longer transcript line that decide what kind of record it is. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptPeekBytes = 64 * 1024;

    /// <summary>Default: memory, in bytes, that the index of one agent transcript may use; past it the index stops and the list says so. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long TranscriptIndexMaxBytes = 64L << 20;

    /// <summary>Default: lines of a transcript after which a turn goes on in the next one, so no turn needs more than this to be shown. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptMaxLinesPerTurn = 200;

    /// <summary>Default: characters of one row of a transcript turn (a message, a tool result, a diff) that are shown; the rest is cut. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptMaxRowChars = 20_000;

    /// <summary>Default: bytes of one transcript line that the raw view opens; a longer line is cut. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptMaxRawBytes = 8 << 20;

    /// <summary>Default: hits after which a search of a transcript stops. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptMaxSearchHits = 1000;

    /// <summary>Default: characters of a prompt that are the title of its turn. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptPreviewChars = 120;

    /// <summary>Default: deepest JSON nesting read in a transcript line. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptMaxDepth = 64;

    /// <summary>Default: shortest value of a secrets file that is hidden wherever it appears in a transcript; a shorter one would hide ordinary text. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptMinSecretLength = 6;

    /// <summary>Default: tool calls of a transcript that are remembered while they wait for their result. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptMaxPendingTools = 1000;

    /// <summary>Default: secrets files of the opened folder whose values are hidden in a transcript from the start. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptSeedMaxFiles = 20;

    /// <summary>Default: largest secrets file, in bytes, read to learn its values for a transcript. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptSeedMaxFileBytes = 64 * 1024;

    /// <summary>Default: milliseconds between two looks of the transcript window at the growing index. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const int TranscriptRefreshMilliseconds = 250;

    public static NksHub.Textida.Core.Agents.Transcripts.TranscriptLimits Transcript() => new(
        TranscriptWholeLineBytes, TranscriptPeekBytes, TranscriptIndexMaxBytes, TranscriptMaxLinesPerTurn, TranscriptMaxRowChars, TranscriptMaxRawBytes, TranscriptMaxSearchHits,
        TranscriptPreviewChars, TranscriptMaxDepth, TranscriptMinSecretLength, TranscriptMaxPendingTools);

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
