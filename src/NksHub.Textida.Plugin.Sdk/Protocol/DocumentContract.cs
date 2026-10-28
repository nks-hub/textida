namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// Method names of the document API v1 (requests from the plugin to the editor). Every read names a revision or a snapshot; a revision that is not the current one
/// is a <see cref="DocumentErrorCodes.RevisionConflict"/>. Positions are 64-bit byte offsets; lines and columns count UTF-16 units. The permission each method needs is in
/// <see cref="NksHub.Textida.PluginHost.Permissions.PluginMethodPermissions"/>.
/// </summary>
public static class DocumentMethods
{
    /// <summary>The documents the plugin may read, each as <see cref="Info"/>.</summary>
    public const string List = "document/list";
    /// <summary>Revision, <c>oldestMappableRevision</c> (the oldest revision <see cref="MapRanges"/> and <see cref="MapPositions"/> can still map from), length in bytes, encoding, BOM length, path and the state of the line index.</summary>
    public const string Info = "document/info";
    /// <summary>The plugin asks for the <see cref="DocumentEvents"/> of a document (ranges and revisions, never text).</summary>
    public const string Subscribe = "document/subscribe";
    /// <summary>The plugin stops hearing about the events of a document. It needs no permission.</summary>
    public const string Unsubscribe = "document/unsubscribe";
    /// <summary>
    /// Bytes <c>[start, end)</c> of one revision as base64, at most the read limit of the editor. The revision must be the current one, or one the plugin holds a snapshot of;
    /// otherwise the answer is a <see cref="DocumentErrorCodes.RevisionConflict"/>. Or <c>{"snapshot":id}</c> instead of document and revision.
    /// </summary>
    public const string ReadBytes = "document/readBytes";
    /// <summary>
    /// The same range decoded, on character boundaries (<c>snap</c> grows it to them instead of refusing). Bytes that are not valid in the encoding come out as U+FFFD and their
    /// indexes in <c>text</c> (UTF-16 units) are listed in <c>invalid</c>; the bytes themselves are what <see cref="ReadBytes"/> returns, so they can be written back unchanged.
    /// </summary>
    public const string ReadText = "document/readText";
    /// <summary>
    /// Literal case-sensitive search over <c>[start, end)</c>. A call looks at match starts in at most the scan limit of the editor; when it stopped there
    /// <c>truncated</c> is true and <c>next</c> is the <c>start</c> to go on from. At most the match limit of the editor is returned.
    /// </summary>
    public const string Find = "document/find";
    /// <summary>The selections of a document (anchor and caret as byte offsets), with the revision they belong to.</summary>
    public const string Selections = "document/selections";
    /// <summary>Byte offset to <c>line</c> and <c>column</c> (0-based, the column in UTF-16 units) of the current revision; <see cref="DocumentErrorCodes.PositionUnknown"/> where the index does not reach or the offset is not a position.</summary>
    public const string OffsetToPosition = "document/offsetToPosition";
    /// <summary>Line and column (0-based, the column in UTF-16 units) to the byte offset in the current revision.</summary>
    public const string PositionToOffset = "document/positionToOffset";
    /// <summary>
    /// Ranges of an older revision (<c>fromRevision</c>, or <c>snapshot</c> for the revision of a held snapshot) moved to the current one through the change log. One range that a later
    /// change touched fails the whole call with a <see cref="DocumentErrorCodes.RevisionConflict"/> naming it (<c>index</c>). The number of ranges is limited by the editor.
    /// </summary>
    public const string MapRanges = "document/mapRanges";
    /// <summary>
    /// Single positions (<c>offset</c>, <c>gravity</c> <c>left</c> or <c>right</c>: which side text inserted exactly there goes) of an older revision moved to the current one. Each answer is
    /// <c>mapped</c> with its new <c>offset</c>, or <c>deleted</c> when a later change removed the text the position was in; one position never fails the others.
    /// A log that no longer reaches back, or a future revision, is a conflict for the whole call.
    /// </summary>
    public const string MapPositions = "document/mapPositions";
    /// <summary>Pins the current revision of a document for a while (each read starts the time again), within the count and byte budget of the plugin.</summary>
    public const string SnapshotCreate = "snapshot/create";
    /// <summary>Releases a snapshot before its time is up. It needs no permission.</summary>
    public const string SnapshotRelease = "snapshot/release";
    /// <summary>Applies a batch of edits to one revision as one undo step; none is applied unless all are. A batch id the editor already took is answered, not applied twice.</summary>
    public const string EditApply = "edit/apply";
    /// <summary>Offers a batch of edits as a proposal the user reviews in the change preview; the document stays as it is until then.</summary>
    public const string EditPropose = "edit/propose";
    /// <summary>Early tools: the plugin publishes the symbols of one revision (outline).</summary>
    public const string SymbolsPublish = "symbols/publish";
    /// <summary>Early tools: jump to a range and highlight it, as a click in the outline does.</summary>
    public const string Reveal = "editor/reveal";
    /// <summary>Early tools: insert a snippet with carets into every selection as one undo step.</summary>
    public const string SnippetInsert = "snippet/insert";
    /// <summary>Early tools: the plugin offers (or stops offering) to answer the Tab key.</summary>
    public const string TabRegister = "editor/tabHandler";
    /// <summary>The cells selected in the table view of a document, with the header and the revision they belong to.</summary>
    public const string TableSelection = "table/selection";
    /// <summary>Puts text, and optionally HTML, on the clipboard.</summary>
    public const string ClipboardWrite = "clipboard/write";
    /// <summary>Encoding, byte order mark, line endings, invalid sequences and storage mode of one revision.</summary>
    public const string Facts = "document/facts";
    /// <summary>Byte pattern search (hex with <c>?</c> wildcards) over a range of one revision, bounded like <see cref="Find"/> (<c>truncated</c>, <c>next</c>).</summary>
    public const string FindBytes = "document/findBytes";
    /// <summary>Lines of a given log level, found block by block from a snapshot.</summary>
    public const string FindLog = "document/findLog";
    /// <summary>The symbols plugins published for a document.</summary>
    public const string SymbolsList = "symbols/list";
    /// <summary>
    /// The line diff of two sides: each is a document at its current revision, a snapshot, <c>{"text"|"bytes", "encoding"?, "name"?}</c> the plugin supplies, or <c>{"file":"absolute path", "encoding"?}</c> below
    /// the workspace (the <see cref="DocumentPermissions.WorkspaceFiles"/> grant). Hunks come as lines and byte ranges. A side over the diff limit of the editor is a <see cref="DocumentErrorCodes.DiffTooLarge"/>.
    /// </summary>
    public const string DiffCompute = "diff/compute";
    /// <summary>
    /// Shows the diff of two sides (as <see cref="DiffCompute"/>; a side of <c>text</c> or <c>bytes</c> needs the <see cref="DocumentPermissions.Virtual"/> grant, because the editor shows the plugin's own content) in a comparison tab.
    /// Optional: <c>key</c>, so that opening the comparison again takes the place of the tab and keeps the difference the user was on, and <c>actions</c>, buttons (<c>command</c>, <c>title</c>, optional
    /// <c>refresh</c>) that run the plugin's command on the difference the tab is on.
    /// </summary>
    public const string DiffOpen = "diff/open";
    /// <summary>Takes one hunk of a diff into a virtual document the plugin provides (the index of a repository), unless that document moved on.</summary>
    public const string DiffStage = "diff/stage";
    /// <summary>
    /// Opens a document the plugin supplies as a view of its data, or returns the one already open under the same key. It is read only unless the plugin says <c>editable</c>:
    /// the user then edits it (with undo), the plugin hears the edits as <see cref="DocumentEvents.Changed"/> and a save is sent to the plugin as <see cref="DocumentRequests.VirtualSave"/>.
    /// The optional <c>uri</c> (<c>scheme://...</c>, not <c>file</c>) is the address the tab shows; without a <c>key</c> it is the key.
    /// </summary>
    public const string VirtualOpen = "virtual/open";
    /// <summary>Gives a virtual document new content (a new revision), optionally only if it is still at the revision the plugin last saw. It replaces the edits of the user that were not saved yet.</summary>
    public const string VirtualUpdate = "virtual/update";
    /// <summary>The plugin closes a virtual document it provided; the tab closes without asking and the key is free again.</summary>
    public const string VirtualClose = "virtual/close";
    /// <summary>The plugin publishes the diagnostics of one source for one revision of a document; an empty list takes that source's diagnostics away.</summary>
    public const string DiagnosticsPublish = "diagnostics/publish";
    /// <summary>The diagnostics stored for a document (plugins and the editor's own linter), with ranges in the current revision; those touching a hidden value are left out.</summary>
    public const string DiagnosticsList = "diagnostics/list";
    /// <summary>The plugin offers (or stops offering) to answer completion requests (<see cref="DocumentRequests.Completion"/>).</summary>
    public const string CompletionRegister = "editor/completionProvider";
    /// <summary>The plugin offers (or stops offering) to answer hover requests (<see cref="DocumentRequests.Hover"/>).</summary>
    public const string HoverRegister = "editor/hoverProvider";
    /// <summary>One batch of text edits in several documents or files and of file operations (create, rename, delete), shown in the change review and applied all or nothing.</summary>
    public const string WorkspaceEdit = "workspace/edit";
    /// <summary>
    /// The plugin offers (or withdraws) a file source for a URI scheme: it serves list, read, stat and write for such URIs (<see cref="DocumentRequests.SourceRead"/>), and search when it declares <c>search</c>.
    /// Optional <c>adapterVersion</c> and <c>serviceVersion</c> say which adapter and which service the user is talking to.
    /// </summary>
    public const string SourceRegister = "source/register";
    /// <summary>Shows a controlled preview (text or Markdown the plugin supplies) of one revision of a document in the editor's preview panel. HTML is not rendered yet.</summary>
    public const string PreviewShow = "preview/show";
    /// <summary>Takes the preview of a document back.</summary>
    public const string PreviewClose = "preview/close";
    /// <summary>The plugin hosts its agent conversation in the editor's agent panel (one conversation at a time, shared with agents started over ACP).</summary>
    public const string AgentOpen = "agent/open";
    /// <summary>Adds to the conversation: a chunk of the agent's message, a tool and its status, a note or a problem, or the end of the turn.</summary>
    public const string AgentUpdate = "agent/update";
    /// <summary>Asks the user, in the editor's permission dialog, to choose one of the options; answers after the user has decided.</summary>
    public const string AgentPermission = "agent/permission";
    /// <summary>The plugin leaves the agent panel.</summary>
    public const string AgentClose = "agent/close";
}

/// <summary>Requests the editor sends to a plugin (the plugin answers them).</summary>
public static class DocumentRequests
{
    /// <summary>Tab was pressed in a document; the answer is <c>{"handled":bool}</c>. An error, a timeout or <c>false</c> leaves Tab to the editor.</summary>
    public const string Tab = "editor/tab";
    /// <summary>Completion at a caret: params <c>document</c>, <c>revision</c>, <c>offset</c> (byte), optional <c>language</c>; the answer holds the <c>revision</c> it was computed for and the <c>items</c>.</summary>
    public const string Completion = "completion/request";
    /// <summary>
    /// Hover at a position: params <c>document</c>, <c>revision</c>, <c>offset</c> (byte), optional <c>language</c> (the language of the editor's interface, for texts the plugin writes);
    /// the answer holds the <c>revision</c> it was computed for and, when it has something to say, <c>text</c> with the byte range <c>start</c>, <c>end</c> the text describes.
    /// </summary>
    public const string Hover = "hover/request";
    /// <summary>File source: params <c>uri</c> of a folder; the answer is <c>{"entries":[{"uri","name","isDirectory","size"?}]}</c>.</summary>
    public const string SourceList = "source/list";
    /// <summary>File source that declared <c>search</c>: params <c>uri</c> (the root of the scheme) and <c>query</c> (words); the answer has the shape of <see cref="SourceList"/>.</summary>
    public const string SourceSearch = "source/search";
    /// <summary>File source: params <c>uri</c>; the answer holds the <c>revision</c> (an opaque non-empty string) and the content as <c>text</c> (UTF-8) or <c>bytes</c> (base64).</summary>
    public const string SourceRead = "source/read";
    /// <summary>File source: params <c>uri</c>; the answer is <c>{"exists":false}</c> or <c>{"revision","size"}</c>.</summary>
    public const string SourceStat = "source/stat";
    /// <summary>
    /// File source: params <c>uri</c>, <c>expectedRevision</c> (a string, or null to create a file that must not exist yet) and the content as <c>bytes</c> (base64); the answer holds the new <c>revision</c>.
    /// A source that declared <c>atomicWrite</c> writes only if the file is still at the expected revision, in one step, and otherwise answers <see cref="DocumentErrorCodes.SourceConflict"/>.
    /// </summary>
    public const string SourceWrite = "source/write";
    /// <summary>
    /// Virtual document: the user saved an editable one. Params <c>document</c>, <c>key</c>, <c>uri</c> (null when it has none), <c>revision</c> and the content as <c>bytes</c> (base64).
    /// An answer is a save; an error leaves the document unsaved and the user sees its message. Nothing is written to a file.
    /// </summary>
    public const string VirtualSave = "virtual/save";
}

/// <summary>Names of the events (<c>$/event</c> payloads) the editor sends for subscribed documents. They carry ranges and revisions, never text.</summary>
public static class DocumentEvents
{
    /// <summary>A document changed: the revision it has now and the changed ranges.</summary>
    public const string Changed = "document/changed";
    /// <summary>Events of one document were dropped (the plugin was too slow); the payload holds the current revision to read again from.</summary>
    public const string EventsLost = "document/eventsLost";
    /// <summary>The editor stopped showing the document to plugins (it was closed).</summary>
    public const string Withdrawn = "document/withdrawn";
    /// <summary>The user sent a message in the agent panel of this plugin: <c>{"text"}</c>. The turn lasts until the plugin sends the end of the turn.</summary>
    public const string AgentMessage = "agent/message";
    /// <summary>The user cancelled the turn; open permission questions of it were answered as cancelled. The plugin should stop and end the turn.</summary>
    public const string AgentCancel = "agent/cancel";
    /// <summary>The conversation ended on the editor's side (the user stopped it, the window closed): <c>{"reason"}</c>.</summary>
    public const string AgentClosed = "agent/closed";
}

/// <summary>Names of the permissions of the document API. A manifest asks for the ones in <see cref="NksHub.Textida.PluginHost.Permissions.PluginPermissionNames.Declarable"/>; the user grants each one in the plugin manager.</summary>
public static class DocumentPermissions
{
    /// <summary>Lists, reads, searches, maps positions, takes snapshots and subscribes. The scope is a document or all of them.</summary>
    public const string Read = "document.read";
    /// <summary>Offers edits as proposals the user reviews before anything changes.</summary>
    public const string EditPropose = "document.edit.propose";
    /// <summary>Applies edits directly, as one undo step, without a review.</summary>
    public const string EditDirect = "document.edit.direct";
    /// <summary>
    /// Plain values of a secrets file (<c>.env</c>). Without it reads return <c>*</c> in place of every value, a search skips matches that touch a value and an
    /// edit that touches a value is refused. Only the user grants it, for one document.
    /// </summary>
    public const string SecretsReveal = "document.secrets.reveal";
    /// <summary>Replaces the clipboard content. What a plugin writes is its own text; a hidden value of a secrets file only reaches it when <see cref="SecretsReveal"/> was granted.</summary>
    public const string ClipboardWrite = "clipboard.write";
    /// <summary>Provides documents of its own (read only tabs, or tabs the user edits and saves to the plugin), changes their content and revision and closes them. It never reaches a document it did not provide.</summary>
    public const string Virtual = "document.virtual";
    /// <summary>Edits files that are not open and creates, renames and deletes files, below the workspace folders. Only the user grants it, for all of them.</summary>
    public const string WorkspaceFiles = "workspace.files";
    /// <summary>Serves files of a URI scheme the user opens in tabs; the editor sends it the URIs, with their secrets, and takes the content it answers.</summary>
    public const string Source = "document.source";
    /// <summary>Shows previews (text or Markdown the plugin supplies) of documents it may read. The editor renders them with no script, no network and no local file.</summary>
    public const string Preview = "document.preview";
    /// <summary>Hosts a conversation in the agent panel. Questions go through the permission dialog and file changes through the change review, as for any agent.</summary>
    public const string AgentPanel = "agent.panel";
    /// <summary>Lets the language server client start the programs the plugin names in <c>languageServers</c> and hand them the text of documents it may read.</summary>
    public const string LanguageServer = "language.server";
}

/// <summary>JSON-RPC error codes of the document API (implementation-defined range -32020..-32049; <c>error.data</c> carries details).</summary>
public static class DocumentErrorCodes
{
    /// <summary>The revision is not the current one (data: <c>reason</c>, <c>currentRevision</c>), or the change log cannot map a position unambiguously.</summary>
    public const int RevisionConflict = -32020;
    /// <summary>The snapshot ran out of time, was released, or its plugin or document ended.</summary>
    public const int SnapshotExpired = -32021;
    /// <summary>The plugin holds as many snapshots, or as many bytes in them, as it may (data: <c>reason</c> <c>Count</c> with <c>limit</c>, or <c>Bytes</c> with <c>limitBytes</c> and <c>retainedBytes</c>).</summary>
    public const int SnapshotQuotaExceeded = -32022;
    /// <summary>The plugin does not hold the permission the call needs for this target (data: <c>permission</c>, <c>reason</c> <c>NotGranted</c>, <c>Revoked</c> or <c>Delegated</c>).</summary>
    public const int PermissionDenied = -32023;
    /// <summary>The document is not open or not available to plugins.</summary>
    public const int UnknownDocument = -32024;
    /// <summary>Outside the document, inside the BOM or not on a character boundary.</summary>
    public const int InvalidRange = -32025;
    /// <summary>The line index does not know the position yet, or the offset is not a position (data: <c>lines</c>, <c>complete</c>).</summary>
    public const int PositionUnknown = -32026;
    /// <summary>Two edits of one batch touch the same bytes.</summary>
    public const int EditsOverlap = -32027;
    /// <summary>The plugin published more symbols than it may.</summary>
    public const int SymbolsQuotaExceeded = -32028;
    /// <summary>The document has no text view to take selections from or to show a range in.</summary>
    public const int NoTextView = -32029;
    /// <summary>The document is not shown as a table, so there are no selected cells.</summary>
    public const int NoTableView = -32030;
    /// <summary>The document is a virtual document that is read only: it takes no edit from a plugin or an assistant (data: <c>reason</c> = <c>Virtual</c>).</summary>
    public const int ReadOnlyDocument = -32031;
    /// <summary>The document is not one this plugin provided, so it cannot update it or stage into it.</summary>
    public const int NotProvider = -32032;
    /// <summary>The plugin holds as many virtual documents as it may.</summary>
    public const int VirtualQuotaExceeded = -32033;
    /// <summary>The plugin published more diagnostics than it may.</summary>
    public const int DiagnosticsQuotaExceeded = -32034;
    /// <summary>A workspace edit holds more changes than the editor takes.</summary>
    public const int WorkspaceQuotaExceeded = -32035;
    /// <summary>A source refused a write because the file is not at the expected revision (data: <c>currentRevision</c>, a string, or null when the file is gone).</summary>
    public const int SourceConflict = -32036;
    /// <summary>A source cannot reach its data now (no connection); the editor may serve the cached copy.</summary>
    public const int SourceUnavailable = -32037;
    /// <summary>The source has no such file.</summary>
    public const int SourceNotFound = -32038;
    /// <summary>The scheme is already served by another plugin.</summary>
    public const int SourceSchemeTaken = -32039;
    /// <summary>The agent panel holds another conversation (an agent over ACP or another plugin).</summary>
    public const int AgentPanelBusy = -32040;
    /// <summary>The plugin has no conversation in the agent panel (never opened, or the user or the editor ended it).</summary>
    public const int NoAgentSession = -32041;
    /// <summary>A <c>net/request</c> did not get an answer: no connection, the proxy refused it, a timeout, an answer over the limit, or a network policy the editor cannot read.</summary>
    public const int NetworkUnavailable = -32042;
    /// <summary>A side of a diff is larger than the editor compares for a plugin (data: <c>limitBytes</c>, <c>bytes</c>).</summary>
    public const int DiffTooLarge = -32043;
}
