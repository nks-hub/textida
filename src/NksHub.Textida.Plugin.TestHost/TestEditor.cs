using System.Buffers;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Testing;

/// <summary>What a <see cref="TestEditor"/> grants and how it behaves. Every limit it uses is a value for tests; the limits of the editor are not these.</summary>
public sealed record TestEditorOptions
{
    /// <summary>The id the editor knows the plugin by; it is the id of the manifest.</summary>
    public string PluginId { get; init; } = "test.plugin";

    /// <summary>The permissions the user granted the plugin, for all documents (names such as <see cref="DocumentPermissions.Read"/>).</summary>
    public IReadOnlyList<string> Grants { get; init; } = [];

    /// <summary>The plugin API version the editor offers in the handshake.</summary>
    public int ApiVersion { get; init; } = PluginApi.Version;

    /// <summary>The language of the interface sent with every command (cs, en, sk, de).</summary>
    public string Language { get; init; } = "en";

    /// <summary>How long a request to the plugin may take.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Changes the limits of the document API, for a test of what happens at a limit.</summary>
    public Func<DocumentApiOptions, DocumentApiOptions>? TuneDocuments { get; init; }

    /// <summary>Changes the limits of the UI API.</summary>
    public Func<UiApiOptions, UiApiOptions>? TuneUi { get; init; }
}

/// <summary>
/// The editor without a window, for tests of a plugin. It runs the real document, UI, network and secret APIs of the editor over in-memory documents, so a plugin meets the same
/// revisions, permissions and errors as in the editor; what the editor would draw or ask the user is recorded and can be answered by the test.
/// The plugin is a <see cref="PluginBase"/> in this process (<see cref="StartAsync"/>), a program the editor starts (<see cref="StartProcessAsync"/>), or the test itself (<see cref="StartRawAsync"/>).
/// </summary>
public sealed class TestEditor : IAsyncDisposable
{
    private static readonly RpcLimits ConnectionLimits = new(4 << 20, 16, 64, 100_000, 64L << 20, 64L << 20);

    private readonly OwnerLock _owner = new();
    private readonly Dictionary<Guid, TestDocument> _documents = [];
    private readonly Dictionary<string, TestDocument> _byName = [];
    private readonly List<RecordedCall> _calls = [];
    private IPluginLink _link = null!;
    private string? _folder;

    private TestEditor(TestEditorOptions options)
    {
        Options = options;
        Calls = new CallList(this);
        foreach (var grant in options.Grants) Permissions.Grant(options.PluginId, grant, GrantScope.All);
        Proposals = new RecordingProposals(id => { lock (_documents) return _documents.TryGetValue(id, out var d) ? d.Document : null; }, _owner);
        Tools = new RecordingTools(OnVirtualOpened, OnVirtualClosed);
        CallLog = new PluginCallLog(1000);
        var documentOptions = new DocumentApiOptions
        {
            MaxReadBytes = 1 << 20, MaxFindMatches = 1000, MaxSnapshotsPerPlugin = 4, SnapshotLifetime = TimeSpan.FromSeconds(30), EventQueueCapacity = 256, MaxRememberedBatches = 64,
            Permissions = Permissions, CallLog = CallLog, Proposals = Proposals, Tools = Tools,
            MaxSymbolsPerDocument = 1000, MaxSymbolTextLength = 400, MaxSnippetLength = 64 * 1024, MaxSnippetCarets = 64, TabTimeout = TimeSpan.FromMilliseconds(500),
            MaxSecretScanBytes = 16L << 20, MaxClipboardChars = 1 << 20, MaxScanBytes = 256L << 20, MaxVirtualDocumentsPerPlugin = 8, MaxVirtualBytes = 4L << 20,
            VirtualSaveTimeout = TimeSpan.FromSeconds(5), MaxDiagnosticsPerDocument = 2000, MaxCompletionItems = 200, MaxCompletionEdits = 16, CompletionTimeout = TimeSpan.FromSeconds(2),
            DiffActions = new DiffActionOptions(4, 200), MaxDiffBytes = 16L << 20,
        };
        if (options.TuneDocuments is not null) documentOptions = options.TuneDocuments(documentOptions);
        Documents = new DocumentApi(documentOptions, _owner, (plugin, name, payload) => _link.SendEventAsync(plugin, name, payload),
            (plugin, method, payload, timeout, ct) => _link.RequestAsync(plugin, method, payload, timeout, ct));

        var uiOptions = new UiApiOptions
        {
            MaxPanelsPerPlugin = 4, MaxItemsPerPanel = 5000, MaxTreeDepth = 16, MaxTasksPerPlugin = 4, MaxTextLength = 4000, MaxPickItems = 500, TaskCancelGrace = TimeSpan.FromSeconds(5),
        };
        if (options.TuneUi is not null) uiOptions = options.TuneUi(uiOptions);
        UiApi = new UiApi(uiOptions, Ui, (plugin, name, payload) => _link.SendEventAsync(plugin, name, payload));

        var secrets = new PluginSecrets(Secrets, CallLog);
        var network = new PluginNetwork(Permissions, Network.Handler, new PluginNetworkOptions(TimeSpan.FromSeconds(10), 1 << 20), CallLog);
        var gate = new PluginCommandGate(Permissions, CallLog, (target, command, args, ct) =>
            target == options.PluginId ? _link.RequestAsync(target, PluginServiceMethods.CommandExecute, args, options.RequestTimeout, ct) : throw new RpcException(RpcErrorCodes.InvalidParams, $"The test editor has no plugin '{target}'."));
        var router = PluginApiRouter.Create(Documents.HandleAsync, ("ui/", UiApi.HandleAsync), ("secret/", secrets.HandleAsync), ("net/", network.HandleAsync), ("command/", gate.HandleAsync));
        Api = async (plugin, method, parameters, cancellation) =>
        {
            var json = parameters.ValueKind == JsonValueKind.Undefined ? "" : parameters.GetRawText();
            try
            {
                var result = await router(plugin, method, parameters, cancellation).ConfigureAwait(false);
                lock (_calls) _calls.Add(new RecordedCall(method, json, null));
                return result;
            }
            catch (RpcException e)
            {
                lock (_calls) _calls.Add(new RecordedCall(method, json, e.Code));
                throw;
            }
        };
    }

    private PluginApiCallback Api { get; }

    /// <summary>The options the editor was started with.</summary>
    public TestEditorOptions Options { get; }

    /// <summary>The id of the plugin under test.</summary>
    public string PluginId => Options.PluginId;

    /// <summary>The grants of the plugin. A test can revoke one to see how the plugin copes, or grant one the options did not.</summary>
    public PluginPermissions Permissions { get; } = new();

    /// <summary>The log of calls the editor keeps (method, outcome, document; never content).</summary>
    public PluginCallLog CallLog { get; }

    /// <summary>The real document API of the editor.</summary>
    public DocumentApi Documents { get; }

    /// <summary>The real UI API of the editor.</summary>
    public UiApi UiApi { get; }

    /// <summary>What the UI API drew; answer prompts through its properties.</summary>
    public RecordingUi Ui { get; } = new();

    /// <summary>What the editor did for the early tools, diff tabs and virtual tabs.</summary>
    public RecordingTools Tools { get; }

    /// <summary>The change preview; set <see cref="RecordingProposals.Policy"/> to decide what the user does.</summary>
    public RecordingProposals Proposals { get; }

    /// <summary>The credential store of the plugin.</summary>
    public MemorySecrets Secrets { get; } = new();

    /// <summary>The network; set <see cref="ScriptedNetwork.Respond"/> to answer requests.</summary>
    public ScriptedNetwork Network { get; } = new();

    /// <summary>The plugin driven by test code; null when the plugin is a <see cref="PluginBase"/> or a program.</summary>
    public RawPlugin? Raw { get; private set; }

    /// <summary>Every request the plugin made to the editor, in order, with the error code when the editor refused it.</summary>
    public IReadOnlyList<RecordedCall> Calls { get; }

    /// <summary>The permissions the plugin needed for the requests it made, from <see cref="PluginMethodPermissions.For"/>. A request the editor refused counts: the plugin tried to use it.</summary>
    public IReadOnlyList<string> UsedPermissions() =>
        [.. Calls.Select(c => PluginMethodPermissions.For(c.Method)).Where(p => p is not null).Select(p => p!).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>Starts the editor with a plugin that runs in this process.</summary>
    public static async Task<TestEditor> StartAsync(PluginBase plugin, TestEditorOptions? options = null)
    {
        var editor = new TestEditor(options ?? new TestEditorOptions());
        await editor.AttachAsync(async () => await InProcessLink.StartAsync(editor.Api, editor.PluginId, plugin, null, editor.Options.ApiVersion, () => editor.Documents.BuildState(editor.PluginId), ConnectionLimits, editor.Options.RequestTimeout).ConfigureAwait(false)).ConfigureAwait(false);
        return editor;
    }

    /// <summary>Starts the editor with no plugin: the test sends requests as the plugin would, through <see cref="Raw"/>.</summary>
    public static async Task<TestEditor> StartRawAsync(TestEditorOptions? options = null)
    {
        var editor = new TestEditor(options ?? new TestEditorOptions()) { Raw = new RawPlugin() };
        await editor.AttachAsync(async () => await InProcessLink.StartAsync(editor.Api, editor.PluginId, null, editor.Raw, editor.Options.ApiVersion, () => editor.Documents.BuildState(editor.PluginId), ConnectionLimits, editor.Options.RequestTimeout).ConfigureAwait(false)).ConfigureAwait(false);
        return editor;
    }

    /// <summary>Starts the editor and a plugin program through the plugin host of the editor: the process, the handshake, pings and shutdown are the real ones.</summary>
    /// <param name="executable">Full path of the program (a bare name is not accepted, as in a manifest).</param>
    /// <param name="arguments">Arguments of the program.</param>
    /// <param name="options">Grants and options; <see cref="TestEditorOptions.PluginId"/> names the plugin.</param>
    /// <param name="workingDirectory">The folder the program starts in; the folder of the program when absent.</param>
    public static async Task<TestEditor> StartProcessAsync(string executable, IEnumerable<string>? arguments = null, TestEditorOptions? options = null, string? workingDirectory = null)
    {
        var editor = new TestEditor(options ?? new TestEditorOptions());
        var spec = new PluginSpec(editor.PluginId, editor.PluginId, executable, [.. arguments ?? []], ["onStartup"], workingDirectory ?? (Path.IsPathRooted(executable) ? Path.GetDirectoryName(executable) : null));
        var hostOptions = new PluginHostOptions
        {
            Limits = ConnectionLimits,
            Restart = new RestartPolicy(1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
            ApiVersion = editor.Options.ApiVersion,
            HandshakeTimeout = TimeSpan.FromSeconds(30),
            PingInterval = TimeSpan.FromSeconds(5),
            PingTimeout = TimeSpan.FromSeconds(30),
            MissedPingsBeforeKill = 3,
            ShutdownGrace = TimeSpan.FromSeconds(5),
            StderrTailLines = 20,
            DefaultCallTimeout = editor.Options.RequestTimeout,
        };
        await editor.AttachAsync(async () => await ProcessLink.StartAsync(editor.Api, editor.Documents, spec, hostOptions).ConfigureAwait(false)).ConfigureAwait(false);
        return editor;
    }

    private async Task AttachAsync(Func<Task<IPluginLink>> start)
    {
        try { _link = await start().ConfigureAwait(false); }
        catch
        {
            await Documents.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The last lines the plugin program wrote to its standard error; empty for a plugin that does not run as a program.</summary>
    public IReadOnlyList<string> StderrTail => (_link as ProcessLink)?.StderrTail ?? [];

    /// <summary>Opens a document that plugins may see. <paramref name="name"/> is how the test and recordings refer to it.</summary>
    public TestDocument OpenDocument(string name, string text)
    {
        var document = Document.FromBytes(TextCodec.Utf8.Encode(text), PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        var opened = new TestDocument(_owner, document, name);
        lock (_documents)
        {
            _documents[document.Id] = opened;
            _byName[name] = opened;
        }
        _owner.Run(() => Documents.Expose(document, opened.CurrentSelections));
        return opened;
    }

    /// <summary>
    /// Opens a document from a file the test host writes in a folder of its own (removed when the editor is disposed). Use it where the name of the file matters to the editor: a plugin
    /// reads the values of a <c>.env</c> file as stars unless the user allowed it to see them.
    /// </summary>
    public TestDocument OpenFile(string fileName, string text)
    {
        _folder ??= Directory.CreateTempSubdirectory("textida-testhost-").FullName;
        var path = Path.Combine(_folder, fileName);
        File.WriteAllBytes(path, TextCodec.Utf8.Encode(text));
        var document = Document.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        var opened = new TestDocument(_owner, document, fileName, path);
        lock (_documents)
        {
            _documents[document.Id] = opened;
            _byName[fileName] = opened;
        }
        _owner.Run(() => Documents.Expose(document, opened.CurrentSelections));
        return opened;
    }

    /// <summary>A document the test opened, by the name it gave it.</summary>
    public TestDocument GetDocument(string name)
    {
        lock (_documents) return _byName.TryGetValue(name, out var found) ? found : throw new KeyNotFoundException($"The test editor has no document '{name}'.");
    }

    /// <summary>
    /// The user runs a command of the plugin (<c>command/execute</c>), on <paramref name="document"/> at its current revision when there is one. The members of
    /// <paramref name="argumentsJson"/> (a JSON object) are added to the request. The answer is the result; an error of the plugin is thrown as <see cref="RpcException"/>.
    /// <paramref name="revision"/> names another revision than the current one, as a command does when the document moved on before the plugin got the request.
    /// </summary>
    public async Task<JsonElement> ExecuteCommandAsync(string command, TestDocument? document = null, string? argumentsJson = null, long? revision = null, CancellationToken ct = default)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("command", command);
            if (document is not null)
            {
                w.WriteString("document", document.Id);
                w.WriteNumber("revision", revision ?? document.Revision);
            }
            w.WriteString("language", Options.Language);
            if (argumentsJson is not null)
            {
                using var arguments = JsonDocument.Parse(argumentsJson);
                foreach (var member in arguments.RootElement.EnumerateObject()) member.WriteTo(w);
            }
            w.WriteEndObject();
        }
        return await _link.RequestAsync(PluginId, PluginBase.CommandMethod, buffer.WrittenMemory, Options.RequestTimeout, ct).ConfigureAwait(false);
    }

    /// <summary>The editor sends the plugin an event (<c>$/event</c>) with the given payload (JSON text; empty for none).</summary>
    public Task<bool> SendEventAsync(string name, string payloadJson = "") =>
        _link.SendEventAsync(PluginId, name, System.Text.Encoding.UTF8.GetBytes(payloadJson));

    /// <summary>Sends the plugin a request of the editor (such as <c>completion/request</c>) and returns its answer.</summary>
    public Task<JsonElement> RequestPluginAsync(string method, string paramsJson = "{}", CancellationToken ct = default) =>
        _link.RequestAsync(PluginId, method, System.Text.Encoding.UTF8.GetBytes(paramsJson), Options.RequestTimeout, ct);

    private void OnVirtualOpened(Document document, VirtualInfo info)
    {
        var opened = new TestDocument(_owner, document, info.Key);
        lock (_documents) _documents[document.Id] = opened;
    }

    private void OnVirtualClosed(Document document)
    {
        lock (_documents) _documents.Remove(document.Id);
    }

    /// <summary>Ends the plugin the way the editor does and releases everything.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_link is not null) await _link.DisposeAsync().ConfigureAwait(false);
        await Documents.DisposeAsync().ConfigureAwait(false);
        lock (_documents)
            foreach (var document in _documents.Values) document.Document.Dispose();
        if (_folder is not null)
        {
            try { Directory.Delete(_folder, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class CallList(TestEditor editor) : IReadOnlyList<RecordedCall>
    {
        private List<RecordedCall> Snapshot() { lock (editor._calls) return [.. editor._calls]; }

        public RecordedCall this[int index] => Snapshot()[index];
        public int Count => Snapshot().Count;
        public IEnumerator<RecordedCall> GetEnumerator() => Snapshot().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
