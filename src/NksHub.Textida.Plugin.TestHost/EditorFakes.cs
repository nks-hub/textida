using System.Collections.Concurrent;
using System.Net;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;

namespace NksHub.Textida.Plugin.Testing;

/// <summary>One request a plugin made to the editor: the method, the parameters as the plugin sent them and the error code when the editor refused it.</summary>
/// <param name="Method">The method name, such as <c>edit/apply</c>.</param>
/// <param name="ParamsJson">The parameters as JSON text.</param>
/// <param name="ErrorCode">The JSON-RPC error code of the answer; null when the request succeeded.</param>
public sealed record RecordedCall(string Method, string ParamsJson, int? ErrorCode);

/// <summary>An event the editor sent to a plugin that is driven by test code (see <see cref="RawPlugin"/>).</summary>
/// <param name="Name">The event name, such as <c>document/changed</c>.</param>
/// <param name="PayloadJson">The payload as JSON text.</param>
public sealed record EditorEvent(string Name, string PayloadJson);

/// <summary>Whether the user accepts the edits a plugin proposes in the change preview.</summary>
public enum ProposalPolicy
{
    /// <summary>Every proposal is accepted and written to the document as one undo step.</summary>
    Accept,
    /// <summary>Every proposal is rejected; the document stays as it is.</summary>
    Reject,
}

/// <summary>Runs work on one thread at a time, as the thread that owns the documents does in the editor.</summary>
internal sealed class OwnerLock : IDocumentOwnerThread
{
    private readonly object _gate = new();

    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        try { lock (_gate) return Task.FromResult(work()); }
        catch (Exception e) { return Task.FromException<T>(e); }
    }

    public T Run<T>(Func<T> work)
    {
        lock (_gate) return work();
    }

    public void Run(Action work)
    {
        lock (_gate) work();
    }
}

/// <summary>The change preview of the editor with a fixed decision of the user. Proposals are kept in <see cref="Items"/>.</summary>
public sealed class RecordingProposals : IEditProposals
{
    private readonly Func<Guid, Document?> _find;
    private readonly OwnerLock _owner;

    internal RecordingProposals(Func<Guid, Document?> find, OwnerLock owner) => (_find, _owner) = (find, owner);

    /// <summary>The decision of the user; it can be changed between two requests.</summary>
    public ProposalPolicy Policy { get; set; } = ProposalPolicy.Accept;

    /// <summary>The proposals the plugin made, in order.</summary>
    public List<EditProposal> Items { get; } = [];

    /// <inheritdoc/>
    public ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation)
    {
        lock (Items) Items.Add(proposal);
        var id = "proposal-" + Items.Count;
        if (Policy == ProposalPolicy.Reject) return ValueTask.FromResult(new ProposalDecision(id, ProposalOutcome.Rejected, 0, proposal.Edits.Count));
        if (_find(proposal.DocumentId) is not { } document) return ValueTask.FromResult(new ProposalDecision(id, ProposalOutcome.Failed, 0, proposal.Edits.Count));
        var result = _owner.Run(() => document.Apply(proposal.Edits, new EditOptions { BaseRevision = proposal.Revision, Source = proposal.PluginId, RawBytes = proposal.RawBytes }));
        var outcome = result.Applied ? ProposalOutcome.Applied : result.Conflict is ConflictReason.Overlap or ConflictReason.LogTruncated or ConflictReason.FutureRevision ? ProposalOutcome.Stale : ProposalOutcome.Failed;
        return ValueTask.FromResult(new ProposalDecision(id, outcome, result.Applied ? proposal.Edits.Count : 0, proposal.Edits.Count));
    }
}

/// <summary>The part of the editor that draws and moves things for the early tools, diff tabs and virtual tabs. It records what the plugin asked for and answers like an editor with a window would.</summary>
public sealed class RecordingTools : IEditorTools
{
    internal RecordingTools(Action<Document, VirtualInfo> opened, Action<Document> closed) => (_opened, _closed) = (opened, closed);

    private readonly Action<Document, VirtualInfo> _opened;
    private readonly Action<Document> _closed;

    /// <summary>Symbol sets the plugin published, with whether each was current.</summary>
    public List<(SymbolSet Set, bool Current)> Symbols { get; } = [];
    /// <summary>Ranges the plugin asked the editor to reveal.</summary>
    public List<ByteRange> Revealed { get; } = [];
    /// <summary>Selections the plugin placed in a document.</summary>
    public List<SelectionSet> Placed { get; } = [];
    /// <summary>Texts the plugin put on the clipboard.</summary>
    public List<(string Text, string? Html)> Clipboard { get; } = [];
    /// <summary>Comparison tabs the plugin opened.</summary>
    public List<DiffRequest> Diffs { get; } = [];
    /// <summary>Virtual documents the plugin opened, with the plugin id, the name and the title of the tab.</summary>
    public List<(string Plugin, string Name, string? Title, Document Document)> Virtuals { get; } = [];
    /// <summary>The virtual documents the plugin closed.</summary>
    public List<Guid> VirtualClosed { get; } = [];
    /// <summary>Documents whose diagnostics changed.</summary>
    public List<Guid> DiagnosticsNotified { get; } = [];
    /// <summary>False makes <see cref="OpenDiff"/> fail, as for an editor that cannot show the tab.</summary>
    public bool DiffShown { get; set; } = true;

    /// <inheritdoc/>
    public void SymbolsChanged(SymbolSet set, bool current) { lock (Symbols) Symbols.Add((set, current)); }
    /// <inheritdoc/>
    public void SymbolsCleared(string pluginId, Guid document) { }
    /// <inheritdoc/>
    public bool Reveal(Guid document, ByteRange range) { lock (Revealed) Revealed.Add(range); return true; }
    /// <inheritdoc/>
    public void PlaceSelections(Guid document, SelectionSet selections) { lock (Placed) Placed.Add(selections); }
    /// <inheritdoc/>
    public string NewLine(Guid document) => "\n";
    /// <inheritdoc/>
    public TableSelectionData? TableSelection(Guid document) => null;
    /// <inheritdoc/>
    public void SetClipboard(Guid document, string text, string? html) { lock (Clipboard) Clipboard.Add((text, html)); }

    /// <inheritdoc/>
    public Document OpenVirtual(string pluginId, string name, string? title, ReadOnlyMemory<byte> content, TextCodec codec, VirtualInfo info)
    {
        var document = Document.CreateVirtual(name, content, PagedBackendFactory.Instance, codec, info.Editable);
        lock (Virtuals) Virtuals.Add((pluginId, name, title, document));
        _opened(document, info);
        return document;
    }

    /// <inheritdoc/>
    public void CloseVirtual(Guid document)
    {
        lock (Virtuals)
        {
            VirtualClosed.Add(document);
            if (Virtuals.Find(v => v.Document.Id == document).Document is { } closed) _closed(closed);
        }
    }

    /// <inheritdoc/>
    public bool OpenDiff(DiffRequest request) { lock (Diffs) Diffs.Add(request); return DiffShown; }
    /// <inheritdoc/>
    public void DiagnosticsChanged(Guid document) { lock (DiagnosticsNotified) DiagnosticsNotified.Add(document); }
}

/// <summary>The UI of the editor for plugins: records panels, notices and tasks, and answers prompts the way the test says.</summary>
public sealed class RecordingUi : IPluginUi
{
    /// <summary>Panels the plugin set, in order.</summary>
    public List<PanelModel> Panels { get; } = [];
    /// <summary>Ids of the panels the plugin removed.</summary>
    public List<string> RemovedPanels { get; } = [];
    /// <summary>Status bar items the plugin set; null for a clear.</summary>
    public List<StatusModel?> Statuses { get; } = [];
    /// <summary>Notices the plugin showed.</summary>
    public List<(NoticeLevel Level, string Message)> Notices { get; } = [];
    /// <summary>Tasks the plugin started.</summary>
    public List<TaskModel> TasksStarted { get; } = [];
    /// <summary>Progress the plugin reported, by task id.</summary>
    public List<(string Task, TaskProgressModel Progress)> Progress { get; } = [];
    /// <summary>Ids of the tasks that ended.</summary>
    public List<string> TasksEnded { get; } = [];
    /// <summary>The id of the item the user picks in a quick pick; null dismisses it.</summary>
    public Func<QuickPickRequest, string?> Pick { get; set; } = _ => null;
    /// <summary>The text the user types in an input prompt; null dismisses it.</summary>
    public Func<InputRequest, string?> Input { get; set; } = _ => null;
    /// <summary>What the user answers a yes or no question.</summary>
    public Func<ConfirmRequest, bool> Confirm { get; set; } = _ => false;

    /// <inheritdoc/>
    public void SetPanel(string pluginId, PanelModel panel) { lock (Panels) Panels.Add(panel); }
    /// <inheritdoc/>
    public void RemovePanel(string pluginId, string panelId) { lock (RemovedPanels) RemovedPanels.Add(panelId); }
    /// <inheritdoc/>
    public void SetStatus(string pluginId, StatusModel? status) { lock (Statuses) Statuses.Add(status); }
    /// <inheritdoc/>
    public Task<string?> PickAsync(string pluginId, QuickPickRequest request, CancellationToken cancellation) => Task.FromResult(Pick(request));
    /// <inheritdoc/>
    public Task<string?> InputAsync(string pluginId, InputRequest request, CancellationToken cancellation) => Task.FromResult(Input(request));
    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string pluginId, ConfirmRequest request, CancellationToken cancellation) => Task.FromResult(Confirm(request));
    /// <inheritdoc/>
    public void Notify(string pluginId, NoticeLevel level, string message) { lock (Notices) Notices.Add((level, message)); }
    /// <inheritdoc/>
    public void StartTask(string pluginId, TaskModel task) { lock (TasksStarted) TasksStarted.Add(task); }
    /// <inheritdoc/>
    public void UpdateTask(string pluginId, string taskId, TaskProgressModel progress) { lock (Progress) Progress.Add((taskId, progress)); }
    /// <inheritdoc/>
    public void EndTask(string pluginId, string taskId) { lock (TasksEnded) TasksEnded.Add(taskId); }
    /// <inheritdoc/>
    public void RemovePlugin(string pluginId) { }
}

/// <summary>The credential store of the editor, in memory.</summary>
public sealed class MemorySecrets : ISecretBackend
{
    private readonly ConcurrentDictionary<(string, string), string> _values = new();

    /// <inheritdoc/>
    public string? Get(string pluginId, string name) => _values.TryGetValue((pluginId, name), out var value) ? value : null;
    /// <inheritdoc/>
    public void Set(string pluginId, string name, string value) => _values[(pluginId, name)] = value;
    /// <inheritdoc/>
    public bool Delete(string pluginId, string name) => _values.TryRemove((pluginId, name), out _);
}

/// <summary>
/// The network policy of the editor with nobody on the other end: a request of a plugin is answered by <see cref="Respond"/>, whatever the address is, and kept in <see cref="Requests"/>.
/// Nothing leaves the machine.
/// </summary>
public sealed class ScriptedNetwork
{
    /// <summary>The answer to a request, from its method and address. The default answers 200 with an empty body.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

    /// <summary>The requests that reached the network, as <c>METHOD url</c> with the body when there was one.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Set to make the network unreachable: the request fails as if the connection was refused.</summary>
    public bool Unreachable { get; set; }

    internal HttpClientHandler Handler() => new Scripted(this);

    private sealed class Scripted(ScriptedNetwork network) : HttpClientHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            lock (network.Requests) network.Requests.Add(request.Method + " " + request.RequestUri + (body.Length > 0 ? " " + body : ""));
            if (network.Unreachable) throw new HttpRequestException("The test network is unreachable.");
            return network.Respond(request);
        }
    }
}

/// <summary>A document the test opened in the editor. Text is UTF-8 without a byte order mark.</summary>
public sealed class TestDocument
{
    private readonly OwnerLock _owner;
    private SelectionSet _selections;

    internal TestDocument(OwnerLock owner, Document document, string name, string? path = null)
    {
        _owner = owner;
        Document = document;
        Name = name;
        Path = path;
        _selections = SelectionSet.Single(TextSelection.At(0));
    }

    internal Document Document { get; }

    /// <summary>The name the test gave the document; replay steps refer to it.</summary>
    public string Name { get; }

    /// <summary>The path of the file when the document was opened from one (<see cref="TestEditor.OpenFile"/>); null for a document that has no file.</summary>
    public string? Path { get; }

    /// <summary>The id plugins use for the document.</summary>
    public string Id => Document.Id.ToString();

    /// <summary>The current revision.</summary>
    public long Revision => _owner.Run(() => Document.Revision);

    /// <summary>The current text.</summary>
    public string Text => _owner.Run(() => Document.ReadText(new ByteRange(Document.BomLength, Document.ByteLength)));

    /// <summary>The user types <paramref name="text"/> at the byte offset <paramref name="offset"/>. Plugins that subscribed hear a <c>document/changed</c> event.</summary>
    public void Type(long offset, string text) => Replace(offset, offset, text);

    /// <summary>The user replaces the bytes <paramref name="start"/> to <paramref name="end"/> with <paramref name="text"/>.</summary>
    public void Replace(long start, long end, string text)
    {
        var result = _owner.Run(() => Document.Replace(new ByteRange(start, end), Document.Codec.Encode(text)));
        if (!result.Applied) throw new InvalidOperationException($"The edit was refused: {result.Conflict}.");
    }

    /// <summary>The user selects the given byte ranges (anchor, caret); the first one is the primary selection.</summary>
    public void Select(params (long Anchor, long Caret)[] selections) =>
        _selections = SelectionSet.Create([.. selections.Select(s => new TextSelection(s.Anchor, s.Caret))], 0);

    internal SelectionSet? CurrentSelections() => _selections;
}
