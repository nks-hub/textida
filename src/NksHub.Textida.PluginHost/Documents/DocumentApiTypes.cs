using System.Text.Json;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Snippets;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.PluginHost.Documents;

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
    /// <summary>Memory the snapshots of one plugin may hold together (<see cref="IByteSnapshot.RetainedBytes"/>); null sets no byte budget, only the count.</summary>
    public long? MaxSnapshotBytesPerPlugin { get; init; }
    /// <summary>Ranges or positions one <see cref="DocumentMethods.MapRanges"/> or <see cref="DocumentMethods.MapPositions"/> call maps; null leaves it to the size of the message.</summary>
    public int? MaxMapItems { get; init; }
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
    /// <summary>Nesting depth, text size and transform time of the snippets a plugin sends; null keeps <see cref="SnippetLimits.Fallback"/>.</summary>
    public SnippetLimits? Snippets { get; init; }
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
    /// <summary>Largest content of a virtual document, in bytes; also the largest an update may make it and the largest a save sends to the plugin.</summary>
    public required long MaxVirtualBytes { get; init; }
    /// <summary>How long the editor waits for a plugin to take the save of an editable virtual document; null means the editor takes no editable virtual documents.</summary>
    public TimeSpan? VirtualSaveTimeout { get; init; }
    /// <summary>Diagnostics one plugin may publish for one source of one document.</summary>
    public required int MaxDiagnosticsPerDocument { get; init; }
    /// <summary>Completion items taken from one plugin's answer; the rest is dropped.</summary>
    public required int MaxCompletionItems { get; init; }
    /// <summary>Additional edits one completion item may carry.</summary>
    public required int MaxCompletionEdits { get; init; }
    /// <summary>How long the editor waits for a plugin's completion answer (and its hover answer); a plugin that is later contributes nothing.</summary>
    public required TimeSpan CompletionTimeout { get; init; }
    /// <summary>Longest hover text taken from a plugin, in UTF-16 code units; a longer one is cut. Zero means the editor takes no hover text.</summary>
    public int MaxHoverChars { get; init; }
    /// <summary>Workspace edits (<see cref="DocumentMethods.WorkspaceEdit"/>); null means the editor takes none.</summary>
    public WorkspaceEditOptions? Workspace { get; init; }
    /// <summary>File sources for URI schemes (<see cref="DocumentMethods.SourceRegister"/>); null means the editor takes none.</summary>
    public FileSourceOptions? Sources { get; init; }
    /// <summary>Previews (<see cref="DocumentMethods.PreviewShow"/>); null means the editor takes none.</summary>
    public PreviewOptions? Preview { get; init; }
    /// <summary>The agent panel for plugins (<see cref="DocumentMethods.AgentOpen"/>); null means the editor takes none.</summary>
    public AgentPanelOptions? Agent { get; init; }
    /// <summary>Actions and the key of a comparison tab a plugin opens (<see cref="DocumentMethods.DiffOpen"/>); null means the editor takes neither and opens the tab without them.</summary>
    public DiffActionOptions? DiffActions { get; init; }
    /// <summary>Largest side of a diff (<see cref="DocumentMethods.DiffCompute"/>, <see cref="DocumentMethods.DiffOpen"/>, <see cref="DocumentMethods.DiffStage"/>), in bytes; null sets no limit.</summary>
    public long? MaxDiffBytes { get; init; }
}

/// <summary>Limits of the actions a plugin offers in a comparison tab: how many, and the longest key, command and title, in UTF-16 code units.</summary>
public sealed record DiffActionOptions(int MaxActions, int MaxTextLength);

/// <summary>Formats of <see cref="DocumentMethods.PreviewShow"/>. HTML is the subset the editor's own renderer draws (P6.2.7): headings, paragraphs, lists, tables, code, links, images and emphasis; every other tag stays text.</summary>
public static class PreviewFormats
{
    public const string Text = "text";
    public const string Markdown = "markdown";
    public const string Html = "html";
}

/// <summary>
/// What a preview shows: the <paramref name="Text"/> a plugin supplied for <paramref name="Revision"/> of a document, as plain <see cref="PreviewFormats.Text"/>, Markdown or HTML.
/// <paramref name="Images"/> are the pictures the plugin attached, by the name the text refers to as <c>preview:name</c>; null when there are none.
/// </summary>
public sealed record PreviewContent(string PluginId, Guid Document, long Revision, string Format, string? Title, string Text, IReadOnlyDictionary<string, byte[]>? Images = null);

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
/// <param name="MaxImages">Most pictures one preview may attach.</param>
/// <param name="MaxImageBytes">Largest attached picture, in bytes.</param>
/// <param name="MaxImagesBytes">Largest total of the pictures of one preview, in bytes.</param>
public sealed record PreviewOptions(IPreviewPanel Panel, int MaxChars, int MaxImages, int MaxImageBytes, int MaxImagesBytes);

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

/// <summary>What a plugin said about the text at a position of <paramref name="Revision"/>: <paramref name="Text"/> describes <paramref name="Range"/>. <paramref name="Stale"/>: the document moved on, so nothing is shown; a null text means there is nothing to say.</summary>
public sealed record HoverAnswer(long Revision, bool Stale, string? Text, ByteRange? Range, string? PluginId);

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
    Document OpenVirtual(string pluginId, string name, string? title, ReadOnlyMemory<byte> content, TextCodec codec, VirtualInfo info);
    /// <summary>Closes the tab of a virtual document without asking (the plugin closed it or ended). Closing the tab withdraws the document. Called on the thread that owns the documents.</summary>
    void CloseVirtual(Guid document) { }
    /// <summary>Shows the comparison of two revisions in a tab of the editor; false when it could not be shown.</summary>
    bool OpenDiff(DiffRequest request);
    /// <summary>
    /// The diagnostics of a document changed (published, moved by an edit, dropped, or the document was withdrawn): draw <see cref="DocumentApi.DiagnosticsOf"/> again.
    /// Called on the thread that owns the documents.
    /// </summary>
    void DiagnosticsChanged(Guid document);
}

/// <summary>What <see cref="DocumentMethods.VirtualOpen"/> says about a virtual document besides its content: the <paramref name="Key"/> the plugin chose, its <paramref name="Uri"/> (null when it gave none) and whether the user may edit it.</summary>
public sealed record VirtualInfo(string Key, string? Uri, bool Editable);

/// <summary>A file a side was read from, as it was then: the editor refuses an action once the file on disk is not like this any more.</summary>
public sealed record DiffFile(string Path, long Length, DateTime WriteTimeUtc);

/// <summary>
/// One side of a comparison a plugin asked for: the bytes of one revision, from <paramref name="Bom"/> on, headed by <paramref name="Title"/> (null: the editor names it after the document and revision).
/// A <paramref name="Detached"/> side is content the plugin supplied or a <paramref name="File"/> read for it; its snapshot belongs to no open document.
/// A <paramref name="Pinned"/> side is a snapshot the plugin holds: it stays what it is when its document moves on (an old revision to compare with), where a side that is not pinned goes out of date with it.
/// </summary>
public sealed record DiffInput(DocumentSnapshot Snapshot, int Bom, string? Title, DiffFile? File = null, bool Detached = false, bool Pinned = false);

/// <summary>
/// A button the plugin that opened a comparison adds to it. Pressed on a hunk, the editor sends the plugin <c>command/execute</c> with <paramref name="Command"/>: the <c>diff</c> object names the
/// comparison (<c>key</c>, <c>hunk</c> counted from 0 as the tab shows them, <c>hunkCount</c>) and both sides (<c>document</c> and <c>revision</c> of a side that is a document, and for the hunk
/// <c>startLine</c>, <c>lines</c>, <c>start</c>, <c>end</c>). When the right side is an open document the request is for it as well (its <c>document</c>, <c>revision</c>, <c>path</c> and the <c>offset</c> where the hunk starts there);
/// otherwise it has no document. Nothing is sent once the right side, or a left side that is not pinned, is a document at another revision than the comparison was made for, or a file side differs on disk. When the command went through, the editor sends
/// <paramref name="Refresh"/> (null: nothing) the same way, without the hunk.
/// </summary>
public sealed record DiffAction(string Command, string Title, string? Refresh);

/// <summary>
/// The comparison of two revisions the editor shows as a tab. <paramref name="Title"/> is null when the editor names it after its sides. A tab opened with a <paramref name="Key"/> takes the place of
/// the tab the same plugin opened with that key before. <paramref name="PluginId"/> is the plugin that asked, <paramref name="Actions"/> the buttons it adds.
/// </summary>
public sealed record DiffRequest(string? Title, DiffInput Left, DiffInput Right, string? PluginId = null, string? Key = null, IReadOnlyList<DiffAction>? Actions = null);

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
