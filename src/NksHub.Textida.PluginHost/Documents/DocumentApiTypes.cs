using System.Text.Json;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>Method names of the document API v1 (requests from the plugin to the editor).</summary>
public static class DocumentMethods
{
    public const string List = "document/list";
    public const string Info = "document/info";
    public const string Subscribe = "document/subscribe";
    public const string Unsubscribe = "document/unsubscribe";
    public const string ReadBytes = "document/readBytes";
    public const string ReadText = "document/readText";
    public const string Find = "document/find";
    public const string Selections = "document/selections";
    public const string OffsetToPosition = "document/offsetToPosition";
    public const string PositionToOffset = "document/positionToOffset";
    public const string MapRanges = "document/mapRanges";
    public const string SnapshotCreate = "snapshot/create";
    public const string SnapshotRelease = "snapshot/release";
    public const string EditApply = "edit/apply";
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
    /// <summary>Byte pattern search (hex with <c>?</c> wildcards) over a range of one revision.</summary>
    public const string FindBytes = "document/findBytes";
    /// <summary>Lines of a given log level, found block by block from a snapshot.</summary>
    public const string FindLog = "document/findLog";
    /// <summary>The symbols plugins published for a document.</summary>
    public const string SymbolsList = "symbols/list";
    /// <summary>The line diff of two revisions (documents at their current revision or snapshots): hunks as lines and byte ranges.</summary>
    public const string DiffCompute = "diff/compute";
    /// <summary>Shows the diff of two revisions in a comparison tab.</summary>
    public const string DiffOpen = "diff/open";
    /// <summary>Takes one hunk of a diff into a virtual document the plugin provides (the index of a repository), unless that document moved on.</summary>
    public const string DiffStage = "diff/stage";
    /// <summary>Opens a read only document the plugin supplies as a view of its data, or returns the one already open under the same key.</summary>
    public const string VirtualOpen = "virtual/open";
    /// <summary>Gives a virtual document new content (a new revision), optionally only if it is still at the revision the plugin last saw.</summary>
    public const string VirtualUpdate = "virtual/update";
    /// <summary>The plugin publishes the diagnostics of one source for one revision of a document; an empty list takes that source's diagnostics away.</summary>
    public const string DiagnosticsPublish = "diagnostics/publish";
    /// <summary>The diagnostics stored for a document (plugins and the editor's own linter), with ranges in the current revision; those touching a hidden value are left out.</summary>
    public const string DiagnosticsList = "diagnostics/list";
    /// <summary>The plugin offers (or stops offering) to answer completion requests (<see cref="DocumentRequests.Completion"/>).</summary>
    public const string CompletionRegister = "editor/completionProvider";
    /// <summary>One batch of text edits in several documents or files and of file operations (create, rename, delete), shown in the change review and applied all or nothing.</summary>
    public const string WorkspaceEdit = "workspace/edit";
    /// <summary>The plugin offers (or withdraws) a file source for a URI scheme: it serves list, read, stat and write for such URIs (<see cref="DocumentRequests.SourceRead"/>).</summary>
    public const string SourceRegister = "source/register";
    /// <summary>Shows a controlled preview (text or Markdown the plugin supplies) of one revision of a document in the editor's preview panel. HTML is not rendered yet.</summary>
    public const string PreviewShow = "preview/show";
    /// <summary>Takes the preview of a document back.</summary>
    public const string PreviewClose = "preview/close";
    /// <summary>The plugin hosts its agent conversation in the editor's agent panel (one conversation at a time, shared with agents started over ACP).</summary>
    public const string AgentOpen = "agent/open";
    /// <summary>Adds to the conversation: a chunk of the agent's message, a tool and its status, a note or a problem, or the end of the turn (<see cref="AgentUpdateKind"/>).</summary>
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
    /// <summary>File source: params <c>uri</c> of a folder; the answer is <c>{"entries":[{"uri","name","isDirectory","size"?}]}</c>.</summary>
    public const string SourceList = "source/list";
    /// <summary>File source: params <c>uri</c>; the answer holds the <c>revision</c> (an opaque non-empty string) and the content as <c>text</c> (UTF-8) or <c>bytes</c> (base64).</summary>
    public const string SourceRead = "source/read";
    /// <summary>File source: params <c>uri</c>; the answer is <c>{"exists":false}</c> or <c>{"revision","size"}</c>.</summary>
    public const string SourceStat = "source/stat";
    /// <summary>
    /// File source: params <c>uri</c>, <c>expectedRevision</c> (a string, or null to create a file that must not exist yet) and the content as <c>bytes</c> (base64); the answer holds the new <c>revision</c>.
    /// A source that declared <c>atomicWrite</c> writes only if the file is still at the expected revision, in one step, and otherwise answers <see cref="DocumentErrorCodes.SourceConflict"/>.
    /// </summary>
    public const string SourceWrite = "source/write";
}

/// <summary>Names of the events (<c>$/event</c> payloads) the editor sends for subscribed documents. They carry ranges and revisions, never text.</summary>
public static class DocumentEvents
{
    public const string Changed = "document/changed";
    /// <summary>Events of one document were dropped (the plugin was too slow); the payload holds the current revision to read again from.</summary>
    public const string EventsLost = "document/eventsLost";
    public const string Withdrawn = "document/withdrawn";
    /// <summary>The user sent a message in the agent panel of this plugin: <c>{"text"}</c>. The turn lasts until the plugin sends <see cref="AgentUpdateKind.TurnEnd"/>.</summary>
    public const string AgentMessage = "agent/message";
    /// <summary>The user cancelled the turn; open permission questions of it were answered as cancelled. The plugin should stop and end the turn.</summary>
    public const string AgentCancel = "agent/cancel";
    /// <summary>The conversation ended on the editor's side (the user stopped it, the window closed): <c>{"reason"}</c>.</summary>
    public const string AgentClosed = "agent/closed";
}

public static class DocumentPermissions
{
    /// <summary>Lists, reads, searches, maps positions, takes snapshots and subscribes. The scope is a document or all of them.</summary>
    public const string Read = "document.read";
    public const string EditPropose = "document.edit.propose";
    public const string EditDirect = "document.edit.direct";
    /// <summary>
    /// Plain values of a secrets file (<c>.env</c>). Without it reads return <c>*</c> in place of every value, a search skips matches that touch a value and an
    /// edit that touches a value is refused. Only the user grants it, for one document.
    /// </summary>
    public const string SecretsReveal = "document.secrets.reveal";
    /// <summary>Replaces the clipboard content. What a plugin writes is its own text; a hidden value of a secrets file only reaches it when <see cref="SecretsReveal"/> was granted.</summary>
    public const string ClipboardWrite = "clipboard.write";
    /// <summary>Provides documents of its own (read only tabs) and changes their content and revision. It never reaches a document it did not provide.</summary>
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

/// <summary>JSON-RPC error codes of the document API (implementation-defined range -32000..-32099; <c>error.data</c> carries details).</summary>
public static class DocumentErrorCodes
{
    /// <summary>The revision is not the current one (data: <c>reason</c>, <c>currentRevision</c>), or the change log cannot map a position unambiguously.</summary>
    public const int RevisionConflict = -32020;
    public const int SnapshotExpired = -32021;
    public const int SnapshotQuotaExceeded = -32022;
    public const int PermissionDenied = -32023;
    public const int UnknownDocument = -32024;
    /// <summary>Outside the document, inside the BOM or not on a character boundary.</summary>
    public const int InvalidRange = -32025;
    /// <summary>The line index does not know the position yet, or the offset is not a position (data: <c>lines</c>, <c>complete</c>).</summary>
    public const int PositionUnknown = -32026;
    public const int EditsOverlap = -32027;
    public const int SymbolsQuotaExceeded = -32028;
    /// <summary>The document has no text view to take selections from or to show a range in.</summary>
    public const int NoTextView = -32029;
    /// <summary>The document is not shown as a table, so there are no selected cells.</summary>
    public const int NoTableView = -32030;
    /// <summary>The document is a virtual document: it takes no edit from a plugin or an assistant (data: <c>reason</c> = <c>Virtual</c>).</summary>
    public const int ReadOnlyDocument = -32031;
    /// <summary>The document is not one this plugin provided, so it cannot update it or stage into it.</summary>
    public const int NotProvider = -32032;
    public const int VirtualQuotaExceeded = -32033;
    public const int DiagnosticsQuotaExceeded = -32034;
    public const int WorkspaceQuotaExceeded = -32035;
    /// <summary>A source refused a write because the file is not at the expected revision (data: <c>currentRevision</c>, a string, or null when the file is gone).</summary>
    public const int SourceConflict = -32036;
    /// <summary>A source cannot reach its data now (no connection); the editor may serve the cached copy.</summary>
    public const int SourceUnavailable = -32037;
    public const int SourceNotFound = -32038;
    /// <summary>The scheme is already served by another plugin.</summary>
    public const int SourceSchemeTaken = -32039;
    /// <summary>The agent panel holds another conversation (an agent over ACP or another plugin).</summary>
    public const int AgentPanelBusy = -32040;
    /// <summary>The plugin has no conversation in the agent panel (never opened, or the user or the editor ended it).</summary>
    public const int NoAgentSession = -32041;
    /// <summary>A <c>net/request</c> did not get an answer: no connection, the proxy refused it, a timeout, an answer over the limit, or a network policy the editor cannot read.</summary>
    public const int NetworkUnavailable = -32042;
}

/// <summary>
/// Quotas and policy of the document API. Everything is required: sizes, counts and lifetimes are owner decisions, not code defaults.
/// </summary>
public sealed record DocumentApiOptions
{
    /// <summary>Largest range returned by one read.</summary>
    public required int MaxReadBytes { get; init; }
    public required int MaxFindMatches { get; init; }
    public required int MaxSnapshotsPerPlugin { get; init; }
    /// <summary>How long a snapshot lives without being read; each read starts the time again.</summary>
    public required TimeSpan SnapshotLifetime { get; init; }
    /// <summary>Events waiting for one plugin; beyond it the events of a document are dropped and replaced by one <see cref="DocumentEvents.EventsLost"/>.</summary>
    public required int EventQueueCapacity { get; init; }
    /// <summary>Applied batch ids remembered per plugin, so a repeated batch is answered and not applied twice.</summary>
    public required int MaxRememberedBatches { get; init; }
    /// <summary>Grants of the plugins (see <see cref="DocumentPermissions"/>); revoking one stops new requests and cancels pending ones.</summary>
    public required PluginPermissions Permissions { get; init; }
    public required PluginCallLog CallLog { get; init; }
    public required IEditProposals Proposals { get; init; }
    public required IEditorTools Tools { get; init; }
    /// <summary>Symbols one plugin may publish for one document.</summary>
    public required int MaxSymbolsPerDocument { get; init; }
    /// <summary>Longest id, name or kind of a symbol, in UTF-16 code units.</summary>
    public required int MaxSymbolTextLength { get; init; }
    /// <summary>Longest snippet text, in UTF-16 code units.</summary>
    public required int MaxSnippetLength { get; init; }
    /// <summary>Carets one snippet places in each selection.</summary>
    public required int MaxSnippetCarets { get; init; }
    /// <summary>How long a plugin has to answer the Tab key before the editor indents as without it.</summary>
    public required TimeSpan TabTimeout { get; init; }
    /// <summary>Largest secrets file whose values are looked for; a larger one is hidden whole, because looking would mean holding it in memory.</summary>
    public required long MaxSecretScanBytes { get; init; }
    /// <summary>Longest text (and longest HTML) one clipboard write holds, in UTF-16 code units.</summary>
    public required int MaxClipboardChars { get; init; }
    /// <summary>Bytes one inspection call (<see cref="DocumentMethods.Facts"/>, <see cref="DocumentMethods.FindLog"/>) reads before it stops and says where to go on.</summary>
    public required long MaxScanBytes { get; init; }
    /// <summary>Log level rules of a document; null (or a null answer) means the default rules.</summary>
    public Func<Guid, LogLevelRules?>? LogRules { get; init; }
    /// <summary>Virtual documents one plugin may keep open at the same time.</summary>
    public required int MaxVirtualDocumentsPerPlugin { get; init; }
    /// <summary>Largest content of a virtual document, in bytes; also the largest an update may make it.</summary>
    public required long MaxVirtualBytes { get; init; }
    /// <summary>Diagnostics one plugin may publish for one source of one document.</summary>
    public required int MaxDiagnosticsPerDocument { get; init; }
    /// <summary>Completion items taken from one plugin's answer; the rest is dropped.</summary>
    public required int MaxCompletionItems { get; init; }
    /// <summary>Additional edits one completion item may carry.</summary>
    public required int MaxCompletionEdits { get; init; }
    /// <summary>How long the editor waits for a plugin's completion answer; a plugin that is later contributes nothing.</summary>
    public required TimeSpan CompletionTimeout { get; init; }
    /// <summary>Workspace edits (<see cref="DocumentMethods.WorkspaceEdit"/>); null means the editor takes none.</summary>
    public WorkspaceEditOptions? Workspace { get; init; }
    /// <summary>File sources for URI schemes (<see cref="DocumentMethods.SourceRegister"/>); null means the editor takes none.</summary>
    public FileSourceOptions? Sources { get; init; }
    /// <summary>Previews (<see cref="DocumentMethods.PreviewShow"/>); null means the editor takes none.</summary>
    public PreviewOptions? Preview { get; init; }
    /// <summary>The agent panel for plugins (<see cref="DocumentMethods.AgentOpen"/>); null means the editor takes none.</summary>
    public AgentPanelOptions? Agent { get; init; }
}

/// <summary>Formats of <see cref="DocumentMethods.PreviewShow"/>. HTML is left out until the renderer and its policy (P6.2.7) are chosen.</summary>
public static class PreviewFormats
{
    public const string Text = "text";
    public const string Markdown = "markdown";
}

/// <summary>What a preview shows: the <paramref name="Text"/> a plugin supplied for <paramref name="Revision"/> of a document, as plain <see cref="PreviewFormats.Text"/> or Markdown.</summary>
public sealed record PreviewContent(string PluginId, Guid Document, long Revision, string Format, string? Title, string Text);

/// <summary>
/// The preview panel of the editor. It owns the panel and renders the content itself; the plugin only supplies data. Calls come on the thread that owns the documents,
/// except <see cref="Close"/>, which also comes from any thread when a plugin loses its permission or restarts.
/// </summary>
public interface IPreviewPanel
{
    /// <summary>Shows the content (it is for the current revision of its document) and opens the panel.</summary>
    void Show(PreviewContent content);
    /// <summary>The document moved past the shown revision: mark the preview as outdated until the plugin shows the new revision.</summary>
    void Outdated(string pluginId, Guid document, long currentRevision);
    /// <summary>The preview of this plugin for this document is gone; the panel closes if it shows it.</summary>
    void Close(string pluginId, Guid document);
}

/// <param name="MaxChars">Longest preview text, in UTF-16 code units.</param>
public sealed record PreviewOptions(IPreviewPanel Panel, int MaxChars);

public enum AgentUpdateKind { Message, Tool, ToolUpdate, Note, Problem, TurnEnd }

/// <summary>
/// One change of a conversation a plugin hosts. <paramref name="Text"/> is the chunk of a message, the title of a tool, a note, a problem or the reason a turn ended;
/// <paramref name="ToolId"/> and <paramref name="Status"/> (<c>pending</c>, <c>in_progress</c>, <c>completed</c>, <c>failed</c>, <c>cancelled</c>) belong to the tool kinds.
/// </summary>
public sealed record AgentUpdate(AgentUpdateKind Kind, string? Text, string? ToolId = null, string? Status = null);

/// <summary>An option of a permission question; <paramref name="Reject"/> marks the one that refuses and is the default of the dialog.</summary>
public sealed record AgentPermissionOption(string Id, string Name, bool Reject);

/// <summary>
/// The agent panel of the editor, shared with agents over ACP. Calls come on the thread that owns the documents, except <see cref="Close"/> (any thread)
/// and <see cref="AskAsync"/> (its dialog runs on the UI thread). A message of the user or a cancelled turn reaches the plugin through <see cref="DocumentApi.AgentUserMessage"/> and the like.
/// </summary>
public interface IAgentPanel
{
    /// <summary>Opens the conversation and shows the panel; false when it holds another one.</summary>
    bool Open(string pluginId, string name);
    bool HasSession(string pluginId);
    void Update(string pluginId, AgentUpdate update);
    void Close(string pluginId);
    /// <summary>Asks in the permission dialog; null when the user cancelled it or <paramref name="cancellation"/> was signalled (the turn was cancelled).</summary>
    Task<string?> AskAsync(string pluginId, string tool, IReadOnlyList<AgentPermissionOption> options, CancellationToken cancellation);
}

/// <param name="MaxUpdateChars">Longest text of one update, tool title or question, in UTF-16 code units.</param>
public sealed record AgentPanelOptions(IAgentPanel Panel, int MaxUpdateChars);

/// <summary>
/// What a workspace edit needs from the editor: the review that shows and applies it, the workspace folders (paths outside them are refused), whether a file is open in a tab
/// (a text edit by path of such a file is refused; a rename or delete is not, the tab follows it), and the limits. <paramref name="Roots"/> and <paramref name="IsOpen"/> run on the thread that owns the documents.
/// </summary>
public sealed record WorkspaceEditOptions(IWorkspaceEdits Service, Func<IReadOnlyList<string>> Roots, Func<string, bool> IsOpen, int MaxChanges, long MaxFileBytes);

/// <summary>A workspace edit made into one proposal of the change service: every part is checked against its revision or hash, and it is <see cref="ChangeProposal.Atomic"/>.</summary>
public sealed record WorkspaceEditRequest(string PluginId, ChangeProposal Proposal);

/// <summary>The change review for workspace edits: shows the proposal, applies what the user accepted and answers once the user has decided.</summary>
public interface IWorkspaceEdits
{
    ValueTask<ProposalDecision> SubmitAsync(WorkspaceEditRequest request, CancellationToken cancellation);
}

/// <summary>One cell of the table selection: its value as the table shows it and the bytes it occupies in the document (empty at the end of a short row).</summary>
public sealed record TableCell(string Value, ByteRange Range);

/// <summary>A selected data row; <paramref name="Number"/> is its 1-based number in the file, the header not counted.</summary>
public sealed record TableRow(int Number, IReadOnlyList<TableCell> Cells);

/// <summary>
/// The selected cells of a table view: <paramref name="Columns"/> columns from the 0-based <paramref name="FirstColumn"/>, the <paramref name="Header"/>
/// of those columns (null without a header) and the selected <paramref name="Rows"/>, read lazily on the thread that owns the document.
/// </summary>
public sealed record TableSelectionData(int FirstColumn, int Columns, IReadOnlyList<TableCell>? Header, IEnumerable<TableRow> Rows);

/// <summary>One symbol of an outline: a named range of one revision. <paramref name="ParentId"/> names an earlier symbol or is null.</summary>
public sealed record SymbolInfo(string Id, string Name, string Kind, string? ParentId, ByteRange Range);

/// <summary>The symbols a plugin published for <paramref name="Revision"/> of a document.</summary>
public sealed record SymbolSet(string PluginId, Guid Document, long Revision, IReadOnlyList<SymbolInfo> Symbols);

public enum DiagnosticSeverity { Error, Warning, Information, Hint }

/// <summary>A finding a plugin published: a range of the revision it was published for.</summary>
public sealed record Diagnostic(DiagnosticSeverity Severity, string Message, string? Code, ByteRange Range);

/// <summary>The diagnostics of one <paramref name="Source"/> (a checker of the plugin) for <paramref name="Revision"/> of a document; the ranges are bytes of that revision.</summary>
public sealed record DiagnosticSet(string PluginId, Guid Document, string Source, long Revision, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>A diagnostic with its range moved to the current revision of the document.</summary>
public sealed record PlacedDiagnostic(string PluginId, string Source, DiagnosticSeverity Severity, string Message, string? Code, ByteRange Range);

/// <summary>An edit that goes with a completion elsewhere in the file (an import, a <c>use</c>): the bytes of <paramref name="Range"/> of the answered revision become <paramref name="Text"/>.</summary>
public sealed record CompletionEdit(ByteRange Range, string Text);

/// <summary>One offer of a plugin, valid for <paramref name="Revision"/> only: <paramref name="Text"/> replaces <paramref name="Range"/>, the <paramref name="AdditionalEdits"/> go in the same undo step.</summary>
public sealed record CompletionItem(string PluginId, Guid Document, long Revision, string Label, string? Detail, string? Kind, ByteRange Range, string Text, IReadOnlyList<CompletionEdit> AdditionalEdits);

/// <summary>The plugins' offers for <paramref name="Revision"/>. <paramref name="Stale"/>: the document moved on while the plugins answered, so nothing is offered.</summary>
public sealed record CompletionAnswer(long Revision, bool Stale, IReadOnlyList<CompletionItem> Items);

/// <summary>
/// What the editor does for the early tools. The API checks revisions and ranges first; an implementation only draws and moves.
/// Calls are made on the thread that owns the documents, except <see cref="SymbolsCleared"/> after a plugin restart (any thread).
/// </summary>
public interface IEditorTools
{
    /// <summary>A symbol set was published, or the document moved past its revision (<paramref name="current"/> false: show it as outdated).</summary>
    void SymbolsChanged(SymbolSet set, bool current);
    /// <summary>The symbols of a plugin for a document are gone (document withdrawn, plugin restarted).</summary>
    void SymbolsCleared(string pluginId, Guid document);
    /// <summary>Scrolls to the range and selects it. False when the document has no text view.</summary>
    bool Reveal(Guid document, ByteRange range);
    /// <summary>Puts these selections (bytes of the current revision) into the text view of the document.</summary>
    void PlaceSelections(Guid document, SelectionSet selections);
    /// <summary>The line break a new line of the document gets.</summary>
    string NewLine(Guid document);
    /// <summary>The path of the file the document is saved as; null for a document that is not saved.</summary>
    string? FilePath(Guid document) => null;
    /// <summary>One level of indentation in the document: a tab or the spaces of the indent size.</summary>
    string IndentUnit(Guid document) => "    ";
    /// <summary>
    /// Snippets with tab stops were written (each at its byte offset in the current revision, its text as written): starts the session that walks through the stops.
    /// </summary>
    void BeginSnippets(Guid document, IReadOnlyList<(long Start, NksHub.Textida.Core.Snippets.ParsedSnippet Snippet)> snippets) { }
    /// <summary>The cells selected in the table view of the document; null when it is not shown as a table.</summary>
    TableSelectionData? TableSelection(Guid document);
    /// <summary>Replaces the clipboard: <paramref name="text"/> and, when given, <paramref name="html"/> as the rich form of it.</summary>
    void SetClipboard(Guid document, string text, string? html);
    /// <summary>Opens a tab for a new virtual document and returns it. <paramref name="title"/> is the text of the tab; the name decides the language and the hiding of secrets.</summary>
    Document OpenVirtual(string pluginId, string name, string? title, ReadOnlyMemory<byte> content, TextCodec codec);
    /// <summary>Shows the comparison of two revisions in a tab of the editor; false when it could not be shown.</summary>
    bool OpenDiff(DiffRequest request);
    /// <summary>
    /// The diagnostics of a document changed (published, moved by an edit, dropped, or the document was withdrawn): draw <see cref="DocumentApi.DiagnosticsOf"/> again.
    /// Called on the thread that owns the documents.
    /// </summary>
    void DiagnosticsChanged(Guid document);
}

/// <summary>One side of a comparison a plugin asked for: the bytes of one revision, from <paramref name="Bom"/> on, headed by <paramref name="Title"/> (null: the editor names it after the document and revision).</summary>
public sealed record DiffInput(DocumentSnapshot Snapshot, int Bom, string? Title);

/// <summary>The comparison of two revisions the editor shows as a tab. <paramref name="Title"/> is null when the editor names it after its sides.</summary>
public sealed record DiffRequest(string? Title, DiffInput Left, DiffInput Right);

/// <summary>Sends a request to a plugin and waits for its answer (<see cref="PluginHost.CallAsync"/> with the timeout).</summary>
public delegate Task<JsonElement> PluginRequestSender(string pluginId, string method, ReadOnlyMemory<byte> paramsJson, TimeSpan timeout, CancellationToken cancellation);

/// <summary>Runs work on the thread that owns the documents (the UI thread in the editor); documents are not thread safe.</summary>
public interface IDocumentOwnerThread
{
    Task<T> InvokeAsync<T>(Func<T> work);
}

/// <summary>Delivers one event to one plugin; false when the plugin could not take it. <see cref="PluginHost.SendEventAsync"/> fits.</summary>
public delegate Task<bool> PluginEventSender(string pluginId, string eventName, ReadOnlyMemory<byte> payloadJson);

/// <summary>An edit batch a plugin proposes; the user sees it before anything changes. <paramref name="Revision"/> is the revision the edits refer to.</summary>
/// <param name="RawBytes">The edits are byte level (hex): they may start inside a character or the BOM, and the user reviews them as bytes.</param>
public sealed record EditProposal(string PluginId, Guid DocumentId, long Revision, IReadOnlyList<ByteEdit> Edits, string? Title, bool RawBytes = false);

/// <summary>How the user decided a proposal in the change review.</summary>
public enum ProposalOutcome
{
    /// <summary>Every change was accepted and written as one undo step.</summary>
    Applied,
    /// <summary>Some changes were accepted and written as one undo step, the rest rejected.</summary>
    Partial,
    /// <summary>Nothing was written: the user rejected every change, or closed the review.</summary>
    Rejected,
    /// <summary>The document moved on from the revision of the proposal; nothing was written.</summary>
    Stale,
    /// <summary>The document refused the changes (invalid range, encoding); nothing was written.</summary>
    Failed,
    /// <summary>Taken for a later decision: the assistant over MCP gets the id at once and reads the outcome with editor_get_change_status.</summary>
    Pending,
}

/// <summary>The answer to a proposal, given after the user has decided: how many of the <paramref name="Total"/> changes were written.</summary>
public sealed record ProposalDecision(string Id, ProposalOutcome Outcome, int Accepted, int Total);

/// <summary>The change review (P6.1). It takes proposals the plugin made against a checked, current revision and answers once the user has decided.</summary>
public interface IEditProposals
{
    /// <summary>Shows the proposal and returns when the user has decided, the review was closed or <paramref name="cancellation"/> was signalled (then nothing is written).</summary>
    ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation);
}
