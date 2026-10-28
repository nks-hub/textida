using System.Text;
using System.Text.Json;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Data;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Files;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manager;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Workbench.Commands;
using Host = NksHub.Textida.PluginHost.PluginHost;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The plugin side of the app: the host, the document API, the UI API and the built-in plugins. Nothing starts a process until a command needs it.
/// Limits come from <see cref="PluginLimitsPlaceholders"/> only.
/// </summary>
internal sealed partial class PluginServices : IEditProposals, IWorkspaceEdits, IDocumentOwnerThread, IEditorTools, IAsyncDisposable
{
    public const string JsonPluginId = "textida.json";
    private const string JsonActivation = "onCommand:json";
    private const string SchemaCheckCommand = "schema.check";
    private const string PackageCheckCommand = "packages.check";
    public const string TextToolsPluginId = "textida.texttools";
    private const string TextToolsActivation = "onCommand:texttools";
    public const string WebDataPluginId = "textida.webdata";
    private const string WebDataActivation = "onCommand:webdata";
    public const string CodeExplorerPluginId = "textida.codeexplorer";
    private const string CodeExplorerActivation = "onCommand:codeexplorer";
    public const string EmmetPluginId = "textida.emmet";
    private const string EmmetActivation = "onCommand:emmet";
    public const string FormattersPluginId = "textida.formatters";
    private const string FormattersActivation = "onCommand:formatters";
    public const string GitPluginId = "textida.git";
    private const string GitActivation = "onCommand:git";
    /// <summary>The panel of the Git status; its items are the full paths of the files, a click opens the file.</summary>
    public const string GitStatusPanel = "git:status";
    /// <summary>The command that tells the Git plugin a document is closed, so it can stop watching a repository.</summary>
    public const string GitClosedCommand = "git.closed";
    public const string GitTreeStatusCommand = "git.treeStatus";
    public const string WikiPluginId = "textida.wiki";
    /// <summary>The activation event of every plugin that serves file sources: they register their scheme when they start, which the open-from-source command asks for first.</summary>
    public const string SourceActivation = "onSource";
    public const string LspPluginId = "textida.lsp";
    private const string LspActivation = "onCommand:lsp";
    /// <summary>The panel the language server plugin lists several places in; its items are <c>line:character:endLine:endCharacter|path</c>.</summary>
    public const string LspLocationsPanel = "lsp:locations";

    /// <summary>The outline of a document is the panel with this prefix and the document id; its items are the symbol ids.</summary>
    public const string SymbolsPanelPrefix = "symbols:";

    /// <summary>A command running on a document. A <paramref name="Background"/> one is the status bar asking for the key path: a command of the user waits for it instead of being refused.</summary>
    private sealed record Run(MainWindow Window, DocTab Tab, bool Background, TaskCompletionSource Done);

    private readonly App _app;
    private readonly Host _host;
    private readonly Dictionary<Guid, Run> _running = [];
    private readonly string _gitProgram;
    private readonly HashSet<(string Plugin, Guid Document)> _outlines = [];

    public PluginUiHost Ui { get; }
    public DocumentApi Documents { get; }
    public McpService Mcp { get; }
    public AcpService Acp { get; }
    public UiApi UiApi { get; }
    internal PluginPermissions Permissions { get; } = new();

    /// <summary><c>net/request</c> of the plugins (P5.4.4): the requests go out with the handler of the network policy, read for each one.</summary>
    internal PluginNetwork Network { get; }

    /// <summary>The plugins that come with the editor: id and name.</summary>
    internal static IReadOnlyList<(string Id, string Name)> BuiltIns { get; } =
    [
        (JsonPluginId, "JSON"), (TextToolsPluginId, "Text tools"), (WebDataPluginId, "Web data"), (CodeExplorerPluginId, "Code Explorer"),
        (EmmetPluginId, "Emmet"), (FormattersPluginId, "Formatters"), (GitPluginId, "Git"), (LspPluginId, "Language server"), (WikiPluginId, "NKS IS wiki"),
    ];

    /// <summary>Third-party plugins installed from a ZIP (P3b.4.2): install, consent, update, rollback, enable, uninstall.</summary>
    internal PluginManager Manager { get; }

    /// <summary>Installed plugins that could not be read at start; each entry names the plugin and the reason.</summary>
    internal List<string> InstalledProblems { get; }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, JsonElement> _pluginSettings = new(StringComparer.Ordinal);

    /// <summary>The installed plugins with a process whose commands, keys and settings are in the app: id and the version they came from.</summary>
    private readonly Dictionary<string, string> _boundInstalled = new(StringComparer.Ordinal);

    /// <summary>The package registries the user allowed (P8.7.3); the JSON plugin reads the same file.</summary>
    internal PackageRegistryConsent Registries { get; }

    /// <param name="app">The app whose window shows the plugin UI.</param>
    /// <param name="pluginDirectory">Folder with the built-in plugin programs; by default <c>plugins</c> next to the app.</param>
    public PluginServices(App app, string? pluginDirectory = null)
    {
        _app = app;
        Registries = new PackageRegistryConsent(Path.Combine(app.ProfileDirectory, "package-registries.json"));
        Ui = new PluginUiHost(app);
        PluginEventSender send = (plugin, name, payload) => _host!.SendEventAsync(plugin, name, payload);
        // document events wait for a slow plugin, so its backlog stays in the bounded queue of the document API; UI events (a cancelled task) do not wait
        PluginEventSender paced = (plugin, name, payload) => _host!.SendEventAsync(plugin, name, payload, paced: true);
        PluginRequestSender request = (plugin, method, payload, timeout, cancellation) => _host!.CallAsync(plugin, method, payload, timeout, cancellation);
        // the built-in formatter is trusted: it reads the selection and writes it back
        var permissions = Permissions;
        foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditPropose, DocumentPermissions.EditDirect })
            permissions.Grant(JsonPluginId, permission, GrantScope.All);
        // a registry lookup behind a proxy with a login goes through net/request, so the password stays in the editor; the plugin asks nothing before the user allowed the registry
        permissions.Grant(JsonPluginId, PluginPermissionNames.Network, GrantScope.All);
        Documents = new DocumentApi(PluginLimitsPlaceholders.Documents(this, this, permissions, new PluginCallLog(PluginLimitsPlaceholders.CallLogCapacity), id => Mcp?.LogRulesOf(id), WorkspaceEdits(), new PluginPreviewPanel(app), new PluginAgentPanel(this, app)), this, paced, request);
        Mcp = new McpService(this, App.Startup.Channel);
        Acp = new AcpService(this, app);
        UiApi = new UiApi(PluginLimitsPlaceholders.Ui(), Ui, send);
        Ui.Api = UiApi;
        var secrets = new PluginSecrets(new CredentialSecretBackend(app.Credentials), new PluginCallLog(PluginLimitsPlaceholders.CallLogCapacity));
        Network = new PluginNetwork(permissions, () => _app.Remote.Policy().CreateHttpHandler(), PluginLimitsPlaceholders.Network(), new PluginCallLog(PluginLimitsPlaceholders.CallLogCapacity));
        _host = new Host(PluginLimitsPlaceholders.Host() with
            {
                SettingsOf = id => _pluginSettings.TryGetValue(id, out var values) ? values : null,
                HoldsEditorState = id => Documents.HoldsState(id) || UiApi.HoldsState(id),
            },
            PluginApiRouter.Create(Documents.HandleAsync, ("ui/", UiApi.HandleAsync), ("secret/", secrets.HandleAsync), ("net/", Network.HandleAsync)), Documents);
        // whatever way the process ends, no plugin process is left behind
        _exitHook = (_, _) => _host.KillAll();
        AppDomain.CurrentDomain.ProcessExit += _exitHook;
        app.Workbench.SettingChanged += OnSettingChanged;
        SyncSchemaMappings(report: false);
        _host.StateChanged += (id, state) =>
        {
            if (state is PluginState.WaitingRestart or PluginState.Disabled or PluginState.Stopped or PluginState.Inactive)
                Dispatcher.UIThread.Post(() =>
                {
                    UiApi.PluginEnded(id);
                    Documents.PluginEnded(id); // its virtual tabs close: nobody is left to save them
                });
            if (id == CodeExplorerPluginId && state == PluginState.Running) Dispatcher.UIThread.Post(RefreshOutlines); // a restarted process has lost the symbols it published
        };
        _host.Crashed += info => Dispatcher.UIThread.Post(() => ReportCrash(info));
        _host.Log += line => _app.Output.Append(OutputChannel.Tasks, Loc.Get(K.PluginLogSource), line);

        var folder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "json");
        var program = Path.Combine(folder, "NksHub.Textida.Plugins.Json" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _host.Register(new PluginSpec(JsonPluginId, "JSON", program,
            ["--max-bytes", PluginLimitsPlaceholders.JsonFormatterMaxBytes.ToString(), "--indent", PluginLimitsPlaceholders.JsonFormatterIndent.ToString(),
             "--max-depth", PluginLimitsPlaceholders.JsonFormatterMaxDepth.ToString(),
             "--schema-max-bytes", PluginLimitsPlaceholders.SchemaMaxBytes.ToString(), "--schema-depth", PluginLimitsPlaceholders.SchemaMaxDepth.ToString(),
             "--schema-regex-ms", PluginLimitsPlaceholders.SchemaRegexTimeoutMilliseconds.ToString(),
             "--registry-config", Registries.FilePath, "--registry-timeout-ms", PluginLimitsPlaceholders.RegistryTimeoutMilliseconds.ToString(),
             "--registry-max-bytes", PluginLimitsPlaceholders.RegistryMaxResponseBytes.ToString(), "--registry-page-size", PluginLimitsPlaceholders.RegistryPageSize.ToString(),
             "--registry-max-lookups", PluginLimitsPlaceholders.RegistryMaxLookups.ToString(), "--registry-manifest-max-bytes", PluginLimitsPlaceholders.SchemaMaxBytes.ToString(),
             "--registry-cache-max-bytes", PluginLimitsPlaceholders.RegistryCacheMaxBytes.ToString(), "--registry-cache-seconds", PluginLimitsPlaceholders.RegistryCacheLifetimeSeconds.ToString(),
             "--registry-max-redirects", PluginLimitsPlaceholders.RegistryMaxRedirects.ToString(),
             "--schema-cache", Path.Combine(app.ProfileDirectory, "schema-cache"), "--schema-cache-max-bytes", PluginLimitsPlaceholders.SchemaCacheMaxBytes.ToString(),
             "--schema-download-max-bytes", PluginLimitsPlaceholders.SchemaDownloadMaxBytes.ToString(), "--schema-cache-max-age-seconds", PluginLimitsPlaceholders.SchemaCacheMaxAgeSeconds.ToString(),
             "--schema-retry-seconds", PluginLimitsPlaceholders.SchemaRetrySeconds.ToString(),
             "--schema-ref-depth", PluginLimitsPlaceholders.SchemaRefMaxDepth.ToString(), "--schema-ref-documents", PluginLimitsPlaceholders.SchemaRefMaxDocuments.ToString(),
             "--schema-mappings", SchemaMappingsPath],
            [JsonActivation], folder));

        // the text tools read: statistics and hashes stream from a snapshot, the rest works on a selection or typed text; only inserting an expression result writes
        permissions.Grant(TextToolsPluginId, DocumentPermissions.Read, GrantScope.All);
        permissions.Grant(TextToolsPluginId, DocumentPermissions.EditDirect, GrantScope.All);
        permissions.Grant(TextToolsPluginId, DocumentPermissions.ClipboardWrite, GrantScope.All);
        var toolsFolder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "texttools");
        var toolsProgram = Path.Combine(toolsFolder, "NksHub.Textida.Plugins.TextTools" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _host.Register(new PluginSpec(TextToolsPluginId, "Text tools", toolsProgram,
            ["--chunk-bytes", PluginLimitsPlaceholders.TextToolsChunkBytes.ToString(), "--progress-ms", PluginLimitsPlaceholders.TextToolsProgressMilliseconds.ToString(),
             "--frequency-top", PluginLimitsPlaceholders.TextToolsFrequencyTop.ToString(), "--frequency-max-words", PluginLimitsPlaceholders.TextToolsFrequencyMaxWords.ToString(),
             "--frequency-max-word-length", PluginLimitsPlaceholders.TextToolsFrequencyMaxWordLength.ToString()],
            [TextToolsActivation], toolsFolder));

        // colours and key paths read the text; the colour conversion writes the converted values back
        foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditDirect })
            permissions.Grant(WebDataPluginId, permission, GrantScope.All);
        var webFolder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "webdata");
        var webProgram = Path.Combine(webFolder, "NksHub.Textida.Plugins.WebData" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _host.Register(new PluginSpec(WebDataPluginId, "Web data", webProgram,
            ["--chunk-bytes", PluginLimitsPlaceholders.WebDataChunkBytes.ToString(), "--max-bytes", PluginLimitsPlaceholders.WebDataMaxBytes.ToString(),
             "--max-symbols", PluginLimitsPlaceholders.SymbolsPerDocument.ToString(), "--max-symbol-chars", PluginLimitsPlaceholders.SymbolTextLength.ToString()],
            [WebDataActivation], webFolder));

        // the code explorer only reads: it finds the symbols of a text from simple rules of its language
        permissions.Grant(CodeExplorerPluginId, DocumentPermissions.Read, GrantScope.All);
        var explorerFolder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "codeexplorer");
        var explorerProgram = Path.Combine(explorerFolder, "NksHub.Textida.Plugins.CodeExplorer" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _host.Register(new PluginSpec(CodeExplorerPluginId, "Code Explorer", explorerProgram,
            ["--chunk-bytes", PluginLimitsPlaceholders.CodeExplorerChunkBytes.ToString(), "--max-bytes", PluginLimitsPlaceholders.CodeExplorerMaxBytes.ToString(),
             "--max-symbols", PluginLimitsPlaceholders.SymbolsPerDocument.ToString(), "--max-symbol-chars", PluginLimitsPlaceholders.SymbolTextLength.ToString(),
             "--match-timeout-ms", PluginLimitsPlaceholders.CodeExplorerMatchTimeoutMilliseconds.ToString()],
            [CodeExplorerActivation], explorerFolder));

        // Emmet reads the text in front of the carets and writes the expansion back as one snippet; it answers the Tab key
        foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditDirect })
            permissions.Grant(EmmetPluginId, permission, GrantScope.All);
        var emmetFolder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "emmet");
        var emmetProgram = Path.Combine(emmetFolder, "NksHub.Textida.Plugins.Emmet" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _host.Register(new PluginSpec(EmmetPluginId, "Emmet", emmetProgram,
            ["--context-bytes", PluginLimitsPlaceholders.EmmetContextBytes.ToString(), "--max-chars", PluginLimitsPlaceholders.SnippetLength.ToString(),
             "--max-carets", PluginLimitsPlaceholders.SnippetCarets.ToString(), "--preview-chars", PluginLimitsPlaceholders.EmmetPreviewChars.ToString()],
            [EmmetActivation], emmetFolder));
        // the formatters read the selection and write it back (or propose it)
        foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditPropose, DocumentPermissions.EditDirect })
            permissions.Grant(FormattersPluginId, permission, GrantScope.All);
        var formattersFolder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "formatters");
        var formattersProgram = Path.Combine(formattersFolder, "NksHub.Textida.Plugins.Formatters" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var formattersArguments = new List<string>
        {
            "--max-bytes", PluginLimitsPlaceholders.FormattersMaxBytes.ToString(), "--sql-indent", PluginLimitsPlaceholders.SqlIndent.ToString(),
            "--sql-case", PluginLimitsPlaceholders.SqlKeywordCase, "--tidy-timeout-ms", PluginLimitsPlaceholders.TidyTimeoutMilliseconds.ToString(),
        };
        foreach (var argument in PluginLimitsPlaceholders.TidyArguments) formattersArguments.AddRange(["--tidy-arg", argument]);
        _host.Register(new PluginSpec(FormattersPluginId, "Formatters", formattersProgram, formattersArguments, [FormattersActivation], formattersFolder));

        // Git reads the working tree document and provides the index and HEAD versions as virtual documents; it writes the repository only when the user stages a hunk,
        // and edits the document only when the user takes a side of a merge conflict
        foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.Virtual, DocumentPermissions.EditDirect })
            permissions.Grant(GitPluginId, permission, GrantScope.All);
        var gitFolder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "git");
        var gitProgram = _gitProgram = Path.Combine(gitFolder, "NksHub.Textida.Plugins.Git" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _host.Register(new PluginSpec(GitPluginId, "Git", gitProgram,
            ["--timeout-ms", PluginLimitsPlaceholders.GitTimeoutMilliseconds.ToString(), "--max-bytes", PluginLimitsPlaceholders.VirtualDocumentMaxBytes.ToString(),
             "--max-entries", PluginLimitsPlaceholders.GitStatusEntries.ToString(), "--chunk-bytes", PluginLimitsPlaceholders.GitChunkBytes.ToString(),
             "--watch-debounce-ms", PluginLimitsPlaceholders.GitWatchDebounceMilliseconds.ToString(), "--max-tree-entries", PluginLimitsPlaceholders.GitTreeEntries.ToString()],
            [GitActivation], gitFolder));

        // the wiki adapter serves the files of the scheme nksis; the user's token reaches it from the credential store only through secret/get, and it reaches the wiki through net/request
        permissions.Grant(WikiPluginId, DocumentPermissions.Source, GrantScope.All);
        permissions.Grant(WikiPluginId, PluginPermissionNames.Network, GrantScope.All);
        var wikiFolder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "wiki");
        var wikiProgram = Path.Combine(wikiFolder, "NksHub.Textida.Plugins.Wiki" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _host.Register(new PluginSpec(WikiPluginId, "NKS IS wiki", wikiProgram,
            ["--timeout-ms", PluginLimitsPlaceholders.SourceTimeoutMilliseconds.ToString(), "--max-bytes", PluginLimitsPlaceholders.SourceMaxBytes.ToString(),
             "--max-entries", PluginLimitsPlaceholders.SourceMaxEntries.ToString(), "--language", Loc.Language],
            [SourceActivation], wikiFolder));

        // the language server client reads the documents it follows and publishes diagnostics; it writes only what a rename proposes, in the change review, and may then touch files below the workspace.
        // The shipped client holds workspace.files from the start; any other plugin gets it only from the user, on the consent screen of the plugin manager.
        foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditPropose, DocumentPermissions.WorkspaceFiles })
            permissions.Grant(LspPluginId, permission, GrantScope.All);
        var lspFolder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "lsp");
        var lspProgram = Path.Combine(lspFolder, "NksHub.Textida.Plugins.Lsp" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _host.Register(new PluginSpec(LspPluginId, "Language server", lspProgram,
            ["--max-bytes", PluginLimitsPlaceholders.LspMaxBytes.ToString(), "--start-timeout-ms", PluginLimitsPlaceholders.LspStartTimeoutMilliseconds.ToString(),
             "--request-timeout-ms", PluginLimitsPlaceholders.LspRequestTimeoutMilliseconds.ToString(), "--stop-timeout-ms", PluginLimitsPlaceholders.LspStopTimeoutMilliseconds.ToString(),
             "--max-locations", PluginLimitsPlaceholders.LspMaxLocations.ToString(), "--stderr-lines", PluginLimitsPlaceholders.LspStderrLines.ToString(),
             "--max-message-chars", PluginLimitsPlaceholders.SymbolTextLength.ToString(), "--max-diagnostics", PluginLimitsPlaceholders.DiagnosticsPerDocument.ToString(),
             "--idle-stop-ms", PluginLimitsPlaceholders.LspIdleStopMilliseconds.ToString(), "--root-search-levels", PluginLimitsPlaceholders.LspRootSearchLevels.ToString(),
             "--restart-max-attempts", PluginLimitsPlaceholders.LspRestartMaxAttempts.ToString(), "--restart-base-delay-ms", PluginLimitsPlaceholders.LspRestartBaseDelayMilliseconds.ToString(),
             "--restart-max-delay-ms", PluginLimitsPlaceholders.LspRestartMaxDelayMilliseconds.ToString(), "--restart-stable-ms", PluginLimitsPlaceholders.LspRestartStableMilliseconds.ToString()],
            [LspActivation], lspFolder));

        Manager = new PluginManager(Path.Combine(app.ProfileDirectory, "installed-plugins"), PluginLimitsPlaceholders.Host().ApiVersion, PluginLimitsPlaceholders.GrammarMaxBytes,
            PluginLimitsPlaceholders.PluginZip(), BuiltIns.Select(b => b.Id).ToHashSet(StringComparer.Ordinal), permissions, _host);
        // nothing awaits a process here: registering the enabled plugins completes at once
        var (installed, problems) = Manager.StartAsync().GetAwaiter().GetResult();
        InstalledProblems = [.. problems];
        foreach (var plugin in installed) BindInstalled(plugin);
    }

    /// <summary>
    /// Makes the app show what an installed plugin with a process contributes now: nothing when it is gone or switched off, the commands of its current version otherwise.
    /// What an older version put into the commands, menus, keys and settings is taken back first, so a removal or an update needs no restart. A data plugin is read by the clip library instead.
    /// </summary>
    internal void SyncInstalled(string id)
    {
        var loaded = Manager.Installed().Any(p => p.Id == id && p.Enabled) ? Manager.Loaded(id) : null;
        if (_boundInstalled.TryGetValue(id, out var version) && (loaded?.Spec is null || loaded.Manifest.Version != version)) UnbindInstalled(id);
        BindInstalled(loaded);
    }

    private void UnbindInstalled(string id)
    {
        _boundInstalled.Remove(id);
        InstalledProblems.RemoveAll(p => p.StartsWith(id + ":", StringComparison.Ordinal));
        UndeclarePlugin(id);
    }

    /// <summary>Takes back the commands, menu items, keys and settings of a plugin, and the setting values kept for its handshake.</summary>
    internal void UndeclarePlugin(string id)
    {
        _app.Workbench.RemovePlugin(id);
        _pluginSettings.TryRemove(id, out _);
    }

    /// <summary>
    /// Gives the commands of an installed plugin with a process a handler that starts the plugin at its first use and sends it <c>command/execute</c>; a data plugin is read by the clip library instead.
    /// The commands of one version of a plugin are registered once (see <see cref="SyncInstalled"/>).
    /// </summary>
    internal void BindInstalled(LoadedPlugin? plugin)
    {
        if (plugin?.Spec is null || !_boundInstalled.TryAdd(plugin.Manifest.Id, plugin.Manifest.Version)) return;
        var commands = _app.Workbench.Commands;
        if (commands.AddManifest(plugin.Contributions).Count > 0) return;
        var (id, display) = (plugin.Manifest.Id, plugin.Manifest.Name);
        foreach (var problem in DeclarePlugin(plugin)) InstalledProblems.Add($"{id}: {problem}");
        foreach (var command in plugin.Contributions.Commands.Where(c => !plugin.CommandData.ContainsKey(c.Id)))
        {
            var name = command.Id;
            commands.SetHandler(name, async (_, _) => await RunInstalledAsync(id, display, name));
        }
    }

    /// <summary>Puts the keybindings and settings of a plugin into the app and keeps its setting values ready for the handshake of its process.</summary>
    internal IReadOnlyList<string> DeclarePlugin(LoadedPlugin plugin)
    {
        var errors = _app.Workbench.AddPlugin(plugin.Contributions, plugin.Manifest.Name);
        RefreshSettings(plugin.Manifest.Id);
        return errors;
    }

    private void RefreshSettings(string pluginId)
    {
        if (_app.Workbench.PluginSettingValues(pluginId) is { } values) _pluginSettings[pluginId] = JsonDocument.Parse(values.ToJsonString()).RootElement.Clone();
        else _pluginSettings.TryRemove(pluginId, out _);
    }

    /// <summary>A setting of a plugin changed: a running process hears it as the event <c>settings.changed</c> with all its values; a stopped one gets them at its next start.</summary>
    private void OnSettingChanged(string key)
    {
        if (key == SchemaMappingsSetting) SyncSchemaMappings(report: true);
        if (_app.Workbench.PluginOfSetting(key) is not { } id) return;
        RefreshSettings(id);
        if (_host.IsRegistered(id) && _pluginSettings.TryGetValue(id, out var values)) _ = _host.SendEventAsync(id, "settings.changed", Encoding.UTF8.GetBytes(values.GetRawText()));
    }

    /// <summary>
    /// Runs a command of an installed plugin. A plugin that may read or edit the document of the active tab runs it like a built-in one: the request names the document
    /// and its revision, the plugin sees the document only while the command runs, a proposal opens in the change review and a direct edit is one undo step.
    /// </summary>
    private async Task<CommandOutcome> RunInstalledAsync(string pluginId, string display, string command)
    {
        if (!_host.IsRegistered(pluginId)) return CommandOutcome.NotHandled;
        if (_app.ActiveWindow is { Active: { Custom: null } tab } window
            && new[] { DocumentPermissions.Read, DocumentPermissions.EditPropose, DocumentPermissions.EditDirect }.Any(p => Permissions.IsGranted(pluginId, p, GrantScope.OfDocument(tab.Doc.Doc.Id))))
        {
            var outcome = await ExecuteAsync(window, tab, pluginId, "onCommand:" + command, display, command);
            return outcome is { Started: true, Result.ValueKind: JsonValueKind.False } ? CommandOutcome.NotHandled : CommandOutcome.Handled;
        }
        try
        {
            await _host.ActivateAsync("onCommand:" + command).ConfigureAwait(false);
            var result = await _host.CallAsync(pluginId, "command/execute", Encoding.UTF8.GetBytes($"{{\"command\":\"{JsonEncodedText.Encode(command)}\"}}")).ConfigureAwait(false);
            return result.ValueKind == JsonValueKind.False ? CommandOutcome.NotHandled : CommandOutcome.Handled;
        }
        catch (Exception e) when (e is PluginHandshakeException or PluginCrashedException or PluginUnavailableException)
        {
            Dispatcher.UIThread.Post(() => _app.ActiveWindow?.SetStatusMessage(e.Message));
            return CommandOutcome.Handled;
        }
    }

    // ---- language servers: definition, references, rename, diagnostics, completion ----

    /// <summary>
    /// Runs a language server command (<c>lsp.start</c>, <c>lsp.definition</c>, <c>lsp.references</c>, <c>lsp.rename</c>, <c>lsp.restart</c>, <c>lsp.stop</c>) for the tab. The server starts with the first command and follows the
    /// document until its tab closes. A tab without a text surface (a large file shown in pages) never reaches the plugin. A definition in another file opens that file. False when nothing was started.
    /// </summary>
    public async Task<bool> RunLspAsync(MainWindow window, DocTab tab, string command)
    {
        if (tab.Surface is not { } surface)
        {
            window.SetStatusMessage(Loc.Get(K.LspTextOnly));
            return false;
        }
        var language = tab.Doc.Language.Id;
        var declared = DeclaredServerFor(language, tab.Doc.Doc.FilePath);
        // a server of the catalog is looked for here (a path of the user, the project, the PATH) and the plugin is told where; stopping one starts nothing, so it asks nothing
        MainWindow.LspProgram? program = declared is null && command != "lsp.stop" ? await window.LocateLspProgramAsync(tab, language) : null;
        if (program is { Declined: true }) return false;
        var outcome = await ExecuteAsync(window, tab, LspPluginId, LspActivation, "Language server", command,
            $",\"lspLanguage\":\"{JsonEncodedText.Encode(declared?.LanguageFor(language) ?? language)}\",\"offset\":{CaretOf(surface) ?? 0}"
            + (declared is null ? "" : ",\"lspServer\":" + declared.ToLaunchJson(language))
            + (program is null ? "" : $",\"lspProgram\":{{\"name\":\"{JsonEncodedText.Encode(program.Name)}\",\"path\":\"{JsonEncodedText.Encode(program.Path)}\"}}"), keep: true, keepOpen: true);
        if (outcome is { Started: true, Result: { ValueKind: JsonValueKind.Object } r } && r.TryGetProperty("status", out var status) && status.GetString() == "ok"
            && r.TryGetProperty("revealed", out var revealed) && !revealed.GetBoolean() && r.GetProperty("locations") is { ValueKind: JsonValueKind.Array } places && places.GetArrayLength() == 1)
        {
            var place = places[0];
            window.GoToPlace(place.GetProperty("path").GetString()!, place.GetProperty("line").GetInt32(), place.GetProperty("character").GetInt32(),
                place.GetProperty("endLine").GetInt32(), place.GetProperty("endCharacter").GetInt32());
        }
        return outcome.Started;
    }

    /// <summary>The server a plugin declares for the document, among the plugins that hold the <c>language.server</c> permission; it wins over the servers that come with the editor.</summary>
    internal DeclaredServer? DeclaredServerFor(string languageId, string? path) =>
        LanguageServerMatcher.Find(_app.Clips.LanguageServers.Where(s => Permissions.IsGranted(s.PluginId, DocumentPermissions.LanguageServer, GrantScope.All)), languageId, path);

    /// <summary>A click on an item of the list of places: reads <c>line:character:endLine:endCharacter|path</c>, null for anything else.</summary>
    internal static (string Path, int Line, int Character, int EndLine, int EndCharacter)? ParsePlace(string item)
    {
        int bar = item.IndexOf('|');
        if (bar < 0) return null;
        var numbers = item[..bar].Split(':');
        if (numbers.Length != 4 || !int.TryParse(numbers[0], out int line) || !int.TryParse(numbers[1], out int character)
            || !int.TryParse(numbers[2], out int endLine) || !int.TryParse(numbers[3], out int endCharacter))
            return null;
        return (item[(bar + 1)..], line, character, endLine, endCharacter);
    }

    // ---- Git: status, diff against the index and HEAD, stage the hunk at the caret ----

    /// <summary>
    /// Runs a Git command (<c>git.status</c>, <c>git.diffIndex</c>, <c>git.diffHead</c>, <c>git.stageHunk</c>, <c>git.unstageHunk</c>, <c>git.conflictOurs</c>, <c>git.conflictTheirs</c>, <c>git.conflictBoth</c>,
    /// <c>git.markResolved</c>, <c>git.conflictDelete</c>) for the file of the tab. With an <paramref name="offset"/> (a byte of the document) the hunk commands act there instead of at the caret.
    /// False when nothing was started.
    /// </summary>
    public async Task<bool> RunGitAsync(MainWindow window, DocTab tab, string command, long? offset = null)
    {
        var path = JsonEncodedText.Encode(tab.Doc.Doc.FilePath ?? "");
        return (await ExecuteAsync(window, tab, GitPluginId, GitActivation, "Git", command, $",\"path\":\"{path}\"" + (offset is { } at ? $",\"offset\":{at}" : ""))).Started;
    }

    /// <summary>
    /// The changes of the file of the tab against the index and <c>HEAD</c>, for the gutter, as of <see cref="GitMarks.Revision"/> of the document. Empty for a document the gutter does not look at (no file,
    /// a secrets file, a file above <see cref="PluginLimitsPlaceholders.GitGutterMaxBytes"/>) and for a file Git has nothing to say about; null when Git could not be asked now (the document is busy with a command, the plugin does not run).
    /// </summary>
    public async Task<GitMarks?> GitMarksAsync(MainWindow window, DocTab tab)
    {
        var document = tab.Doc.Doc;
        if (document.FilePath is not { } file || SecretValues.IsSecretFile(document.SecretsPath) || document.ByteLength > PluginLimitsPlaceholders.GitGutterMaxBytes || !File.Exists(_gitProgram) || !InRepository(file))
            return new GitMarks(document.Revision, []);
        var outcome = await ExecuteAsync(window, tab, GitPluginId, GitActivation, "Git", "git.hunks", $",\"path\":\"{JsonEncodedText.Encode(file)}\"", notify: false, background: true);
        if (!outcome.Started || outcome.Result is not { ValueKind: JsonValueKind.Object } answer || !answer.TryGetProperty("hunks", out var hunks) || hunks.ValueKind != JsonValueKind.Array
            || !answer.TryGetProperty("revision", out var revision)) return null;
        var marks = new List<GitMark>(hunks.GetArrayLength());
        foreach (var h in hunks.EnumerateArray())
        {
            var kind = h.GetProperty("kind").GetString() switch { "added" => GitMarkKind.Added, "removed" => GitMarkKind.Removed, _ => GitMarkKind.Modified };
            marks.Add(new GitMark(h.GetProperty("line").GetInt32(), h.GetProperty("lines").GetInt32(), kind, h.GetProperty("staged").GetBoolean()));
        }
        return new GitMarks(revision.GetInt64(), marks);
    }

    /// <summary>Whether a <c>.git</c> folder or file stands in the folder of the file or above it. The plugin is not started for a file that cannot be in a repository.</summary>
    internal static bool InRepository(string file) => InRepositoryFolder(Path.GetDirectoryName(Path.GetFullPath(file)));

    /// <summary>The same for a folder: it or a folder above it holds <c>.git</c>.</summary>
    internal static bool InRepositoryFolder(string? folder)
    {
        for (var dir = folder is null ? null : Path.GetFullPath(folder); dir is not null; dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git"))) return true;
        return false;
    }

    /// <summary>
    /// The state Git gives the files below <paramref name="folder"/>, for the file tree. Null when Git was not asked or could not answer: the plugin is not there, the folder is in no repository
    /// (nothing is started for it), the plugin does not run. A folder in a repository without changes has a status without entries.
    /// </summary>
    public async Task<GitFileStatus?> GitTreeStatusAsync(string folder)
    {
        if (!File.Exists(_gitProgram) || !InRepositoryFolder(folder)) return null;
        try
        {
            await _host.ActivateAsync(GitActivation);
            var request = $"{{\"command\":\"{GitTreeStatusCommand}\",\"language\":\"{Loc.Language}\",\"path\":\"{JsonEncodedText.Encode(folder)}\"}}";
            var answer = (await _host.CallAsync(GitPluginId, "command/execute", Encoding.UTF8.GetBytes(request))).Clone();
            if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("status", out var status) || status.GetString() != "tree" || !answer.TryGetProperty("entries", out var entries)) return null;
            return GitFileStatus.Build(folder, entries.EnumerateArray().Select(e => (GitFileStatus.FromCode(e[0].GetString() is { Length: 1 } code ? code[0] : '?'), e[1].GetString() ?? "")));
        }
        catch (Exception e) when (e is PluginCrashedException or PluginHandshakeException or PluginUnavailableException or NksHub.Textida.Rpc.RpcException or InvalidOperationException or KeyNotFoundException)
        {
            return null; // a colour in a tree is not worth a notice; the next change asks again
        }
    }

    /// <summary>The window and tab of an open document, null when it is not open (a comparison tab names the document of its right side).</summary>
    internal (MainWindow Window, DocTab Tab)? OpenTabOf(Guid document) => TabOf(document);

    /// <summary>
    /// Runs the command of a comparison tab's button in the plugin that opened the comparison, for the document of its right side (see <see cref="DiffAction"/>): the request names the document,
    /// its revision and file, the <paramref name="offset"/> where the hunk starts and <paramref name="diff"/>, more members of the request already written as JSON (see <see cref="DiffActionPayload"/>).
    /// False when nothing was started.
    /// </summary>
    public async Task<bool> RunDiffActionAsync(MainWindow window, DocTab tab, string pluginId, string command, long? offset, string diff = "")
    {
        var path = JsonEncodedText.Encode(tab.Doc.Doc.FilePath ?? "");
        return (await ExecuteAsync(window, tab, pluginId, "onCommand:" + command, PluginName(pluginId), command, $",\"path\":\"{path}\"" + (offset is { } at ? $",\"offset\":{at}" : "") + diff)).Started;
    }

    /// <summary>
    /// Runs the command of a comparison tab's button when the right side is no open document (text or a file the plugin supplied): the request has no document, only <paramref name="diff"/>
    /// (see <see cref="DiffActionPayload"/>). The plugin learns nothing it did not give the editor. False when the plugin could not be reached.
    /// </summary>
    public async Task<bool> RunDiffActionAsync(MainWindow window, string pluginId, string command, string diff)
    {
        try
        {
            await _host.ActivateAsync("onCommand:" + command);
            var request = $"{{\"command\":\"{JsonEncodedText.Encode(command)}\",\"language\":\"{Loc.Language}\"{diff}}}";
            await _host.CallAsync(pluginId, "command/execute", Encoding.UTF8.GetBytes(request));
            return true;
        }
        catch (Exception e) when (e is PluginCrashedException or PluginHandshakeException or PluginUnavailableException or NksHub.Textida.Rpc.RpcException)
        {
            if (e is not PluginCrashedException) window.ShowNotice("plugin:" + pluginId, Loc.Get(K.PluginUnavailable, ("name", PluginName(pluginId)), ("reason", e.Message)), [], warning: true);
            return false;
        }
    }

    // ---- formatters: SQL, HTML entities, HTML Tidy ----

    /// <summary>Formats the SQL of the tab (or its selections) in the dialect of the document, as one undo step or, with <paramref name="preview"/>, as a proposal. False when nothing was started.</summary>
    public async Task<bool> RunSqlAsync(MainWindow window, DocTab tab, bool preview)
    {
        if (tab.Surface is null || tab.Doc.Language.Id != "sql") return false;
        return (await RunFormatterAsync(window, tab, preview ? "sql.preview" : "sql.format", $",\"dialect\":\"{tab.Doc.SqlDialect}\"")).Started;
    }

    /// <summary>Converts the selections (the whole text when nothing is selected) to HTML entities or back, as one undo step. False when nothing was started.</summary>
    public async Task<bool> RunHtmlEntitiesAsync(MainWindow window, DocTab tab, bool encode, bool nonAscii = false) =>
        tab.Surface is not null && (await RunFormatterAsync(window, tab, encode ? (nonAscii ? "html.encodeAll" : "html.encode") : "html.decode", "")).Started;

    /// <summary>
    /// Formats the HTML of the tab with HTML Tidy and proposes the result in the change preview; the messages of Tidy go to the output window.
    /// Without a Tidy program the plugin says where it looked. False when nothing was started.
    /// </summary>
    public async Task<bool> RunTidyAsync(MainWindow window, DocTab tab)
    {
        if (tab.Surface is null) return false;
        var path = JsonEncodedText.Encode(_app.Workbench.Settings.GetString("html.tidyPath"));
        var outcome = await RunFormatterAsync(window, tab, "html.tidy", $",\"tidyPath\":\"{path}\"");
        return outcome.Started;
    }

    private async Task<Outcome> RunFormatterAsync(MainWindow window, DocTab tab, string command, string extra)
    {
        var outcome = await ExecuteAsync(window, tab, FormattersPluginId, FormattersActivation, "Formatters", command, extra);
        if (outcome.Started && outcome.Result.ValueKind == JsonValueKind.Object && outcome.Result.TryGetProperty("diagnostics", out var diagnostics) && diagnostics.GetArrayLength() > 0)
        {
            _app.Output.Append(OutputChannel.Tasks, Loc.Get(K.TidyOutputSource), string.Join('\n', diagnostics.EnumerateArray().Select(d => d.GetString()!)));
            window.ShowOutput(OutputChannel.Tasks);
        }
        return outcome;
    }

    internal Host HostForTests => _host;

    /// <summary>A plugin process ended or was ended for not answering: a notice names the plugin and says whether it comes back, its last error output goes to the output window.</summary>
    private void ReportCrash(PluginCrashInfo info)
    {
        if (info.StderrTail.Count > 0) _app.Output.Append(OutputChannel.Tasks, info.DisplayName, string.Join('\n', info.StderrTail));
        if (_app.ActiveWindow is not { } window) return;
        // not the plugin's own notice key: the state change right after the crash dismisses the notices of the ended plugin
        window.ShowNotice("plugin.crash:" + info.PluginId, CrashMessage(info), [(Loc.Get(K.PluginCrashLog), () => window.ShowOutput(OutputChannel.Tasks))], warning: true);
    }

    internal static string CrashReasonText(PluginEndReason reason) => Loc.Get(reason switch
    {
        PluginEndReason.Unresponsive => K.PluginCrashUnresponsive,
        PluginEndReason.ProtocolViolation => K.PluginCrashProtocol,
        PluginEndReason.HandshakeFailed => K.PluginCrashStart,
        _ => K.PluginCrashExited,
    });

    internal static string CrashMessage(PluginCrashInfo info)
    {
        var reason = CrashReasonText(info.Reason);
        return info.NextRestartIn is { } delay
            ? Loc.Get(K.PluginCrashRestart, ("name", info.DisplayName), ("reason", reason), ("seconds", (int)Math.Ceiling(delay.TotalSeconds)))
            : Loc.Get(K.PluginCrashDisabled, ("name", info.DisplayName), ("reason", reason));
    }

    /// <summary>Starts the plugins that serve file sources, so their schemes are registered before the editor looks for one. A plugin that cannot start is passed over; its scheme is then simply not offered.</summary>
    public async Task ActivateSourcesAsync()
    {
        try { await _host.ActivateAsync(SourceActivation).ConfigureAwait(false); }
        catch (Exception e) when (e is PluginCrashedException or PluginHandshakeException or PluginUnavailableException or NksHub.Textida.Rpc.RpcException) { }
    }

    /// <summary>The secret storage of the plugins over the credential store of the system: one entry per plugin and name, never in a settings file.</summary>
    private sealed class CredentialSecretBackend(ICredentialStore store) : ISecretBackend
    {
        private static string Key(string pluginId, string name) => $"textida:plugin:{pluginId}:{name}";
        public string? Get(string pluginId, string name) => store.Get(Key(pluginId, name));
        public void Set(string pluginId, string name, string value) => store.Set(Key(pluginId, name), value);
        public bool Delete(string pluginId, string name) => store.Delete(Key(pluginId, name));
    }

    /// <summary>A plugin command is running (the document is exposed to plugins only meanwhile) or waits for a background query on its document to end.</summary>
    public bool IsBusy => _queued > 0 || _running.Values.Any(r => !r.Background);

    private int _queued;

    /// <summary>A plugin command is running on the document, which therefore has to stay visible to plugins until it ends.</summary>
    internal bool IsRunningOn(Guid document) => _running.ContainsKey(document);

    public Task<T> InvokeAsync<T>(Func<T> work) => Dispatcher.UIThread.InvokeAsync(work).GetTask();

    /// <summary>The window that runs the command shows the proposal in the change review and the answer waits for the decision of the user; the document is not touched until the user applies it.</summary>
    public async ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation)
    {
        if (proposal.PluginId == McpServer.ClientId) return await Mcp.Proposals.SubmitAsync(proposal, cancellation);
        var id = Guid.NewGuid().ToString("N");
        var review = await Dispatcher.UIThread.InvokeAsync<Task<ProposalDecision>>(() =>
            _running.TryGetValue(proposal.DocumentId, out var run)
                ? run.Window.ReviewPluginProposalAsync(proposal, run.Tab, id, cancellation)
                // a plugin that holds the agent panel proposes outside a command, for the tabs it was allowed to read and propose in
                : Acp.HasSession(proposal.PluginId) && TabOf(proposal.DocumentId) is { } found
                    ? found.Window.ReviewPluginProposalAsync(proposal, found.Tab, id, cancellation)
                    : Task.FromResult(new ProposalDecision(id, ProposalOutcome.Stale, 0, proposal.Edits.Count)));
        return await review.ConfigureAwait(false);
    }

    private WorkspaceEditOptions WorkspaceEdits()
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Document? OpenDocumentOf(string path) => _app.Windows.SelectMany(w => w.Documents).FirstOrDefault(d => string.Equals(d.Path, path, comparison))?.Doc;
        return new(this, () => [.. _app.Windows.SelectMany(w => w.Files?.Roots ?? [])], path => OpenDocumentOf(path) is not null,
            PluginLimitsPlaceholders.WorkspaceEditChanges, PluginLimitsPlaceholders.WorkspaceEditFileBytes, PluginLimitsPlaceholders.WorkspaceEditTotalBytes)
        { OpenDocument = OpenDocumentOf };
    }

    /// <summary>The active window shows the workspace edit in the change review and the answer waits for the decision of the user; nothing is written before.</summary>
    public async ValueTask<ProposalDecision> SubmitAsync(WorkspaceEditRequest request, CancellationToken cancellation)
    {
        var id = Guid.NewGuid().ToString("N");
        var review = await Dispatcher.UIThread.InvokeAsync<Task<ProposalDecision>>(() =>
            _app.ActiveWindow is { } window
                ? window.ReviewWorkspaceEditAsync(request.Proposal, id, cancellation)
                : Task.FromResult(new ProposalDecision(id, ProposalOutcome.Failed, 0, request.Proposal.ChangeCount)));
        return await review.ConfigureAwait(false);
    }

    /// <summary>
    /// Runs "Format JSON" (or its preview) on the text tab through the JSON plugin process. The document is visible to plugins only while the
    /// command runs. Returns false when nothing was started (no text editor, or the command is already running on this document).
    /// </summary>
    public Task<bool> RunJsonAsync(MainWindow window, DocTab tab, bool preview) => RunJsonAsync(window, tab, preview ? "json.preview" : "json.format");

    /// <summary>As above for any command of the JSON plugin on a selection: <c>json.format</c>, <c>json.preview</c>, <c>json.minify</c>, <c>json.sortKeys</c> or <c>json.fixCommas</c>.</summary>
    public Task<bool> RunJsonAsync(MainWindow window, DocTab tab, string command) =>
        tab.Surface is null ? Task.FromResult(false) : RunCommandAsync(window, tab, JsonPluginId, JsonActivation, "JSON", command);

    /// <summary>
    /// Runs one of the text tools (<c>text.stats</c>, <c>text.hash</c>, <c>text.calculate</c>, <c>text.expression</c>, <c>text.bases</c>) on the tab through the text tools plugin
    /// process. They work on text, hex and paged tabs alike and read from a snapshot, so a large file is never held in memory. Returns false when nothing was started.
    /// </summary>
    public Task<bool> RunTextToolAsync(MainWindow window, DocTab tab, string command, string extra = "") =>
        tab.Custom is not null ? Task.FromResult(false) : RunCommandAsync(window, tab, TextToolsPluginId, TextToolsActivation, "Text tools", command, extra);

    /// <summary>
    /// Hashes the selections, or the document, through the text tools plugin and shows the result in the dialog the hex view uses for its checksums (Copy, Compare), so a hash looks and
    /// works the same wherever it is asked for. The algorithm is picked from the list the hex view offers. False when nothing was started.
    /// </summary>
    public async Task<bool> RunHashDialogAsync(MainWindow window, DocTab tab)
    {
        if (tab.Custom is not null) return false;
        var outcome = await ExecuteAsync(window, tab, TextToolsPluginId, TextToolsActivation, "Text tools", "text.hash", ",\"dialog\":true");
        if (!outcome.Started) return false;
        if (outcome.Result is { ValueKind: JsonValueKind.Object } answer && answer.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array
            && answer.TryGetProperty("algorithm", out var algorithm) && algorithm.GetString() is { } name)
        {
            var parts = blocks.EnumerateArray().Select(b => (Start: b.GetProperty("start").GetInt64(), End: b.GetProperty("end").GetInt64())).ToList();
            var values = blocks.EnumerateArray().Select(b => b.GetProperty("hash").GetString()!).ToList();
            bool selection = answer.TryGetProperty("selection", out var picked) && picked.ValueKind == JsonValueKind.True;
            await window.ShowChecksumResultAsync(name, Loc.Get(K.HexSumResult, ("name", name), ("scope", MainWindow.HashScope(selection, parts))), values);
        }
        return true;
    }

    /// <summary>
    /// Copies the selected cells of the table view as Markdown, SQL INSERT, JSON or HTML (<c>table.copyAs.*</c>) through the text tools plugin, which reads them with the
    /// table API and writes the clipboard. While the values of a secrets file are hidden the plugin gets <c>*</c> in their place and nothing is asked. SQL is written in the
    /// dialect of the document (the file, its folder, the <c>sql.dialect</c> setting). False when nothing was started.
    /// </summary>
    public async Task<bool> RunTableCopyAsync(MainWindow window, DocTab tab, string command) =>
        tab is { Custom: null, TableShown: true } && (await ExecuteAsync(window, tab, TextToolsPluginId, TextToolsActivation, "Text tools", command,
            $",\"dialect\":\"{tab.Doc.SqlDialect}\"", keepHidden: tab.SecretsHidden, tooLarge: Loc.Get(K.TableCopyAsTooLarge, ("limit", PluginLimitsPlaceholders.ClipboardMaxChars)))).Started;

    // ---- web data: colours and key paths ----

    /// <summary>The syntax a key path is read in for a language mode (<c>yaml</c> or <c>json</c>); null for any other mode.</summary>
    internal static string? KeySyntax(string languageId) => languageId is "yaml" or "json" ? languageId : null;

    /// <summary>
    /// Converts the colour values in the selections, or the one at the caret, to a notation (<c>hex</c>, <c>rgb</c>, <c>hsl</c> or <c>oklch</c>) through the web data plugin,
    /// as one undo step. Returns false when nothing was started.
    /// </summary>
    public Task<bool> RunColorAsync(MainWindow window, DocTab tab, string format) =>
        tab.Surface is null ? Task.FromResult(false) : RunCommandAsync(window, tab, WebDataPluginId, WebDataActivation, "Web data", "color." + format);

    /// <summary>The key under the caret as the plugin read it: the revision and the caret it belongs to, the dotted path and the bytes of the key.</summary>
    internal sealed record KeyPathAnswer(long Revision, long Caret, string Path, ByteRange Key);

    private DispatcherTimer? _keyPathTimer;
    private (MainWindow Window, DocTab Tab)? _keyPathWanted;
    private bool _keyPathShown;

    private static long? CaretOf(EditorSurface surface) =>
        surface.ByteSelections() is { Count: > 0 } selections ? selections.Items[selections.Primary].Caret : null;

    /// <summary>The status bar is drawn for this tab: once the caret has rested, asks for the key path under it, when the tab is a YAML or JSON text. Any other tab clears it.</summary>
    public void TrackKeyPath(MainWindow window, DocTab? tab)
    {
        if (tab is not { Surface: not null } || KeySyntax(tab.Doc.Language.Id) is null)
        {
            _keyPathWanted = null;
            ShowKeyPath(null);
            return;
        }
        _keyPathWanted = (window, tab);
        if (_keyPathTimer is null)
        {
            _keyPathTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.KeyPathDebounceMilliseconds) };
            _keyPathTimer.Tick += (_, _) =>
            {
                _keyPathTimer.Stop();
                if (_keyPathWanted is { } wanted) _ = QueryKeyPathAsync(wanted.Window, wanted.Tab, background: true);
            };
        }
        _keyPathTimer.Stop();
        _keyPathTimer.Start();
    }

    private void ShowKeyPath(KeyPathAnswer? answer)
    {
        if (answer is null)
        {
            if (!_keyPathShown) return;
            _keyPathShown = false;
            Ui.SetStatus(WebDataPluginId, null);
            return;
        }
        _keyPathShown = true;
        Ui.SetStatus(WebDataPluginId, new StatusModel(answer.Path, Loc.Get(K.KeyPathTip)));
    }

    /// <summary>
    /// Asks the web data plugin for the key under the caret of a YAML or JSON tab and shows it in the status bar. The answer belongs to the revision and the caret it was asked for:
    /// when the document or the caret has moved by the time it arrives, it is dropped and the status bar keeps what is true. Null then, and when there is no key.
    /// </summary>
    internal async Task<KeyPathAnswer?> QueryKeyPathAsync(MainWindow window, DocTab tab, bool background = false)
    {
        if (tab.Surface is not { } surface || KeySyntax(tab.Doc.Language.Id) is not { } syntax || CaretOf(surface) is not { } caret) return null;
        var document = tab.Doc.Doc;
        long revision = document.Revision;
        var outcome = await ExecuteAsync(window, tab, WebDataPluginId, WebDataActivation, "Web data", "keypath.at", $",\"offset\":{caret},\"syntax\":\"{syntax}\"", notify: false, background: background);
        if (!outcome.Started)
        {
            if (_keyPathWanted is not null) _keyPathTimer?.Start(); // a command was using the document: ask again when it is free
            return null;
        }
        var r = outcome.Result;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("revision", out var answered) || answered.GetInt64() != revision) return null;
        if (!ReferenceEquals(tab.Doc.Doc, document) || document.Revision != revision || CaretOf(surface) != caret) return null;
        KeyPathAnswer? answer = null;
        if (r.GetProperty("status").GetString() == "ok")
            answer = new KeyPathAnswer(revision, caret, r.GetProperty("path").GetString()!, new ByteRange(r.GetProperty("start").GetInt64(), r.GetProperty("end").GetInt64()));
        ShowKeyPath(ReferenceEquals(window.Active, tab) ? answer : null);
        return answer;
    }

    /// <summary>Puts the key path under the caret on the clipboard.</summary>
    public async Task<bool> CopyKeyPathAsync(MainWindow window, DocTab tab)
    {
        var answer = await QueryKeyPathAsync(window, tab);
        if (answer is null)
        {
            window.SetStatusMessage(Loc.Get(K.KeyPathNone));
            return false;
        }
        if (window.Clipboard is { } clipboard) await clipboard.SetTextAsync(answer.Path);
        window.SetStatusMessage(Loc.Get(K.KeyPathCopied, ("path", answer.Path)));
        return true;
    }

    private bool _revealOutline = true;
    private bool _revealAlways;

    /// <summary>
    /// Reads the keys of a YAML or JSON tab through the web data plugin and publishes them as the symbols of the document: the outline lists them and Go to Anything
    /// (<c>@</c>) jumps to them, both to the same bytes of the key. The document stays visible to plugins until its tab closes. <paramref name="reveal"/> opens the outline.
    /// </summary>
    public async Task<bool> IndexKeysAsync(MainWindow window, DocTab tab, bool reveal)
    {
        if (tab.Surface is null || KeySyntax(tab.Doc.Language.Id) is not { } syntax) return false;
        _revealOutline = _revealAlways = reveal;
        try { return (await ExecuteAsync(window, tab, WebDataPluginId, WebDataActivation, "Web data", "keypath.index", $",\"syntax\":\"{syntax}\"", keep: true)).Started; }
        finally { _revealOutline = true; _revealAlways = false; }
    }

    // ---- code outline: symbols from simple rules of the language ----

    /// <summary>The plugin that publishes the symbols of a language mode: the web data plugin for the keys of YAML and JSON, the code explorer for code and markup; null for any other mode.</summary>
    internal static string? SymbolPlugin(string languageId) =>
        KeySyntax(languageId) is not null ? WebDataPluginId : OutlineLanguages.Contains(languageId) ? CodeExplorerPluginId : null;

    /// <summary>The language modes the code explorer has rules for (the same set as <c>Outlines.Supports</c> in the plugin).</summary>
    private static readonly HashSet<string> OutlineLanguages =
        ["html", "php", "css", "csharp", "markdown", "javascript", "typescript", "python", "rust", "dart", "c", "cpp", "shell", "cmake", "makefile", "meson"];

    /// <summary>
    /// Reads the symbols of the tab (keys, or the code outline) through their plugin and publishes them: the outline lists them and Go to Anything (<c>@</c>) jumps to them,
    /// both to the same bytes. <paramref name="reveal"/> opens the outline. False when the language has no symbols or nothing was started.
    /// </summary>
    public Task<bool> IndexSymbolsAsync(MainWindow window, DocTab tab, bool reveal) =>
        SymbolPlugin(tab.Doc.Language.Id) == CodeExplorerPluginId ? IndexOutlineAsync(window, tab, reveal, notify: reveal) : IndexKeysAsync(window, tab, reveal);

    private async Task<bool> IndexOutlineAsync(MainWindow window, DocTab tab, bool reveal, bool notify)
    {
        if ((tab.Surface is null && tab.Paged is null) || SymbolPlugin(tab.Doc.Language.Id) != CodeExplorerPluginId) return false;
        bool paged = tab.Paged is not null; // a large file: read from its start up to the limit, in the background
        _outlineWanted.Add(tab.Doc.Doc.Id);
        _revealOutline = _revealAlways = reveal;
        var document = tab.Doc.Doc;
        long revision = document.Revision;
        try
        {
            bool started = (await ExecuteAsync(window, tab, CodeExplorerPluginId, CodeExplorerActivation, "Code Explorer", "outline.index",
                $",\"outlineLanguage\":\"{tab.Doc.Language.Id}\",\"notify\":{(notify ? "true" : "false")},\"partial\":{(paged ? "true" : "false")}", keep: true, notify: notify, background: paged)).Started;
            if (started && ReferenceEquals(tab.Doc.Doc, document) && document.Revision != revision) _ = RefreshOutlineAsync(document.Id); // typed while the plugin read
            return started;
        }
        finally { _revealOutline = true; _revealAlways = false; }
    }

    private DispatcherTimer? _symbolTimer;
    private DocTab? _symbolWanted;
    private bool _symbolShown;

    /// <summary>
    /// Follow-caret of the code outline: once the caret has rested, the status bar names the symbol it is in (the path of its parents) and the outline marks it as its current item,
    /// while the tab has an outline.
    /// </summary>
    public void TrackSymbol(DocTab? tab)
    {
        if (tab is null || !_outlineWanted.Contains(tab.Doc.Doc.Id) || SymbolPlugin(tab.Doc.Language.Id) != CodeExplorerPluginId)
        {
            _symbolWanted = null;
            ShowSymbol(null);
            return;
        }
        _symbolWanted = tab;
        if (_symbolTimer is null)
        {
            _symbolTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.KeyPathDebounceMilliseconds) };
            _symbolTimer.Tick += (_, _) =>
            {
                _symbolTimer.Stop();
                if (_symbolWanted is { } wanted) FollowSymbol(wanted);
            };
        }
        _symbolTimer.Stop();
        _symbolTimer.Start();
    }

    private void ShowSymbol(string? path)
    {
        if (path is null)
        {
            if (!_symbolShown) return;
            _symbolShown = false;
            Ui.SetStatus(CodeExplorerPluginId, null);
            return;
        }
        _symbolShown = true;
        Ui.SetStatus(CodeExplorerPluginId, new StatusModel(path, Loc.Get(K.SymbolPathTip)));
    }

    private static long? CaretOf(DocTab tab) => tab.Paged is { } paged ? paged.CaretOffset : tab.Surface is { } surface ? CaretOf(surface) : null;

    /// <summary>The symbol the caret is in or after (the last one that starts at or before it); null when there is none.</summary>
    private static SymbolInfo? SymbolAt(IReadOnlyList<SymbolInfo> symbols, long caret)
    {
        SymbolInfo? found = null;
        foreach (var s in symbols)
            if (s.Range.Start <= caret && (found is null || s.Range.Start >= found.Range.Start)) found = s;
        return found;
    }

    /// <summary>The symbol the caret is in or after, from the outline of the revision the tab has now; null when that outline is not there yet or the caret is before the first symbol.</summary>
    private SymbolInfo? SymbolAtCaret(DocTab tab, out IReadOnlyList<SymbolInfo> symbols)
    {
        symbols = Documents.SymbolsOf(CodeExplorerPluginId, tab.Doc.Doc.Id, out bool current);
        return CaretOf(tab) is { } at && current ? SymbolAt(symbols, at) : null;
    }

    /// <summary>The status bar names the symbol the caret is in, with its parents, and the outline of the document marks it as its current item.</summary>
    private void FollowSymbol(DocTab tab)
    {
        var found = SymbolAtCaret(tab, out var symbols);
        ShowSymbol(found is null ? null : SymbolPath(found, symbols));
        Ui.SetCurrentItem(CodeExplorerPluginId, SymbolsPanelPrefix + tab.Doc.Doc.Id.ToString("N"), found?.Id);
    }

    private static string SymbolPath(SymbolInfo found, IReadOnlyList<SymbolInfo> symbols)
    {
        var byId = symbols.ToDictionary(s => s.Id);
        var parts = new List<string> { found.Name };
        for (var parent = found.ParentId; parent is not null && byId.TryGetValue(parent, out var p) && parts.Count < 8; parent = p.ParentId)
            parts.Insert(0, p.Name.Contains('(') ? p.Name[..p.Name.IndexOf('(')] : p.Name);
        return string.Join(" › ", parts);
    }

    private readonly HashSet<Guid> _outlineWanted = [];
    private readonly HashSet<Guid> _outlineRefreshing = [];

    /// <summary>
    /// Reads the outline of a document again after it was edited, once it has rested: the old set stays (marked outdated) until the new one replaces it. An edit that comes
    /// while the plugin reads makes it read once more, so the outline ends up at the revision the document has.
    /// </summary>
    private async Task RefreshOutlineAsync(Guid document)
    {
        if (!_outlineWanted.Contains(document) || !_outlineRefreshing.Add(document)) return;
        try
        {
            while (_outlineWanted.Contains(document))
            {
                await Task.Delay(PluginLimitsPlaceholders.OutlineRefreshMilliseconds);
                if (TabOf(document) is not { } found || !_outlineWanted.Contains(document)) break;
                Documents.SymbolsOf(CodeExplorerPluginId, document, out bool current);
                if (current) break;
                long revision = found.Tab.Doc.Doc.Revision;
                if (!await IndexOutlineAsync(found.Window, found.Tab, reveal: false, notify: false) || found.Tab.Doc.Doc.Revision == revision) break;
            }
        }
        finally { _outlineRefreshing.Remove(document); }
    }

    /// <summary>The code explorer came back from a stop: the outlines it had published are gone, so every document that wants one is read again.</summary>
    private void RefreshOutlines()
    {
        foreach (var document in _outlineWanted.ToArray()) _ = RefreshOutlineAsync(document);
    }

    /// <summary>The symbols published for the tab, and whether they are those of the revision the tab has now.</summary>
    internal IReadOnlyList<SymbolInfo> TabSymbols(DocTab tab, out bool current)
    {
        if (SymbolPlugin(tab.Doc.Language.Id) is { } plugin) return Documents.SymbolsOf(plugin, tab.Doc.Doc.Id, out current);
        current = false;
        return [];
    }

    /// <summary>Jumps to a published symbol exactly as a click in the outline does. False when the symbol is gone or the text it stood on was changed.</summary>
    public bool RevealTabSymbol(DocTab tab, string symbolId) =>
        SymbolPlugin(tab.Doc.Language.Id) is { } plugin && RevealSymbol(plugin, SymbolsPanelPrefix + tab.Doc.Doc.Id.ToString("N"), symbolId);

    /// <summary>
    /// Compares the keys of two YAML or JSON tabs, each as it is now, and writes the keys each one lacks to the output window. Both files are read at the revision the
    /// command started on; if either changed meanwhile the plugin says so and nothing is written.
    /// </summary>
    public async Task<bool> CompareKeysAsync(MainWindow window, DocTab tab, DocTab other)
    {
        if (tab.Surface is null || other.Surface is null || ReferenceEquals(tab.Doc, other.Doc)
            || KeySyntax(tab.Doc.Language.Id) is not { } syntax || KeySyntax(other.Doc.Language.Id) is not { } otherSyntax) return false;
        var outcome = await ExecuteAsync(window, tab, WebDataPluginId, WebDataActivation, "Web data", "keypath.compare",
            $",\"other\":\"{other.Doc.Doc.Id}\",\"otherRevision\":{other.Doc.Doc.Revision},\"syntax\":\"{syntax}\",\"otherSyntax\":\"{otherSyntax}\"", other);
        if (!outcome.Started) return false;
        var r = outcome.Result;
        if (r.ValueKind != JsonValueKind.Object || r.GetProperty("status").GetString() != "compared") return true;

        string nameA = tab.Doc.Title, nameB = other.Doc.Title;
        var lines = new List<string> { Loc.Get(K.KeysCompareHeader, ("a", nameA), ("b", nameB)) };
        if (r.GetProperty("errorLine").GetInt32() is > 0 and var errorA) lines.Add(Loc.Get(K.KeysCompareSyntax, ("file", nameA), ("line", errorA)));
        if (r.GetProperty("otherErrorLine").GetInt32() is > 0 and var errorB) lines.Add(Loc.Get(K.KeysCompareSyntax, ("file", nameB), ("line", errorB)));
        void Section(string property, string file)
        {
            var keys = r.GetProperty(property).EnumerateArray().Select(k => k.GetString()!).ToList();
            lines.Add(keys.Count == 0 ? Loc.Get(K.KeysCompareNoneMissing, ("file", file)) : Loc.Plural(K.KeysCompareMissing, keys.Count, ("file", file)));
            lines.AddRange(keys.Select(k => "    " + k));
        }
        Section("missingInOther", nameB);
        Section("missingInDocument", nameA);
        _app.Output.Append(OutputChannel.Tasks, Loc.Get(K.KeysCompareSource), string.Join('\n', lines));
        window.ShowOutput(OutputChannel.Tasks);
        return true;
    }

    private async Task<bool> RunCommandAsync(MainWindow window, DocTab tab, string pluginId, string activation, string name, string command, string extra = "") =>
        (await ExecuteAsync(window, tab, pluginId, activation, name, command, extra)).Started;

    /// <summary>The answer of a plugin command: whether it was started at all (not when it is already running on the document or the user refused), and what the plugin said.</summary>
    private readonly record struct Outcome(bool Started, JsonElement Result);

    /// <summary>
    /// Runs one plugin command on the tab. The document (and <paramref name="other"/>, when a command compares two) is visible to plugins only meanwhile, unless
    /// <paramref name="keep"/> asks to leave a document with published symbols exposed: the outline stays valid, and the document goes when its tab closes (<see cref="Forget"/>).
    /// <paramref name="keepOpen"/> (with <paramref name="keep"/>) leaves the document exposed whether or not the plugin published anything, for a language server that follows its edits.
    /// <paramref name="keepHidden"/> leaves the values of a secrets file hidden from the plugin without asking. <paramref name="extra"/> is more members of the request object, already written as JSON (starting with a comma). Without <paramref name="notify"/> a plugin that cannot run is passed over silently.
    /// <paramref name="tooLarge"/> is what the window says when the plugin ran but an API call refused a size over a limit; without it the notice says the plugin could not be run.
    /// </summary>
    private async Task<Outcome> ExecuteAsync(MainWindow window, DocTab tab, string pluginId, string activation, string name, string command,
        string extra = "", DocTab? other = null, bool keep = false, bool notify = true, bool background = false, bool keepHidden = false, bool keepOpen = false, string? tooLarge = null)
    {
        var document = tab.Doc.Doc;
        var otherDocument = other?.Doc.Doc;
        Run? Blocking() => _running.TryGetValue(document.Id, out var first) ? first : otherDocument is not null && _running.TryGetValue(otherDocument.Id, out var second) ? second : null;
        bool Busy() => Blocking() is not null;
        if (!background && Blocking() is { Background: true })
        {
            _queued++;
            try { while (Blocking() is { Background: true } asking) await asking.Done.Task; }
            finally { _queued--; }
        }
        if (Busy()) return default;
        // a plugin reads stars in place of the values of a secrets file unless the user allows it, for this document and this run
        bool reveal = !keepHidden && SecretValues.IsSecretFile(document.SecretsPath);
        if (reveal && !await window.PluginConfirmAsync(new ConfirmRequest(Loc.Get(K.PluginSecretsTitle), Loc.Get(K.PluginSecretsMessage, ("name", name), ("file", Path.GetFileName(document.SecretsPath)!))))) return default;
        if (Busy()) return default;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _running[document.Id] = new Run(window, tab, background, done);
        if (other is not null) _running[otherDocument!.Id] = new Run(window, other, background, done);
        if (reveal) Permissions.Grant(pluginId, DocumentPermissions.SecretsReveal, GrantScope.OfDocument(document.Id));
        bool own = !Documents.IsExposed(document.Id), ownOther = otherDocument is not null && !Documents.IsExposed(otherDocument.Id);
        if (own) Documents.Expose(document, tab.Surface is { } surface ? surface.ByteSelections : null);
        if (ownOther) Documents.Expose(otherDocument!, other!.Surface is { } otherSurface ? otherSurface.ByteSelections : null);
        JsonElement result = default;
        try
        {
            await _host.ActivateAsync(activation);
            var request = $"{{\"command\":\"{JsonEncodedText.Encode(command)}\",\"document\":\"{document.Id}\",\"revision\":{document.Revision},\"language\":\"{Loc.Language}\"{extra}}}";
            result = (await _host.CallAsync(pluginId, "command/execute", Encoding.UTF8.GetBytes(request))).Clone();
        }
        catch (Exception e) when (e is PluginCrashedException or PluginHandshakeException or PluginUnavailableException or NksHub.Textida.Rpc.RpcException)
        {
            // a crash has its own notice (ReportCrash), whether a command was running or not
            if (notify && e is not PluginCrashedException)
                window.ShowNotice("plugin:" + pluginId, tooLarge is not null && e is NksHub.Textida.Rpc.RpcException { Code: NksHub.Textida.Rpc.RpcErrorCodes.MessageTooLarge } ? tooLarge : Loc.Get(K.PluginUnavailable, ("name", name), ("reason", e.Message)), [], warning: true);
        }
        finally
        {
            if (own && keep && (keepOpen || Documents.HasSymbols(document.Id) || Documents.HasDiagnostics(document.Id))) Keep(tab.Doc, document);
            else if (own) Documents.Withdraw(document);
            if (ownOther) Documents.Withdraw(otherDocument!);
            _running.Remove(document.Id);
            if (otherDocument is not null) _running.Remove(otherDocument.Id);
            done.TrySetResult();
            if (reveal) Permissions.Revoke(pluginId, DocumentPermissions.SecretsReveal, GrantScope.OfDocument(document.Id));
        }
        return new Outcome(true, result);
    }

    // ---- documents that stay exposed for their outline ----

    private readonly Dictionary<Guid, (OpenDocument Open, Document Document)> _kept = [];

    private void Keep(OpenDocument open, Document document)
    {
        if (_kept.ContainsKey(document.Id)) return;
        _kept[document.Id] = (open, document);
        open.Replaced += () => ForgetReplaced(open);
    }

    /// <summary>The document was swapped (reload): the old one is not shown to plugins any more.</summary>
    private void ForgetReplaced(OpenDocument open)
    {
        foreach (var kept in _kept.Values.ToArray())
            if (ReferenceEquals(kept.Open, open) && !ReferenceEquals(kept.Document, open.Doc)) Forget(kept.Document);
    }

    /// <summary>The document is going away (its last tab closed): plugins stop seeing it and its symbols go. Call on the UI thread, before the document is disposed.</summary>
    public void Forget(Document document)
    {
        Mcp.Closed(document);
        Acp.Closed(document);
        _outlineWanted.Remove(document.Id);
        _schemaChecked.Remove(document.Id);
        _activatedFor.RemoveWhere(x => x.Document == document.Id);
        ReleaseGitWatch(document.Id);
        if (!_kept.Remove(document.Id) && !Documents.IsExposed(document.Id)) return;
        if (!_running.ContainsKey(document.Id)) Documents.Withdraw(document);
    }

    /// <summary>
    /// The last tab of a document closed: tells the Git plugin, if it runs, which stops watching the repository of the file when no other open file of it is left. A command that is still
    /// running on the document is waited for first, so that it cannot start the watch again afterwards.
    /// </summary>
    private void ReleaseGitWatch(Guid document)
    {
        if (!_host.IsRegistered(GitPluginId) || _host.GetState(GitPluginId) != PluginState.Running) return;
        var running = _running.TryGetValue(document, out var run) ? run.Done.Task : Task.CompletedTask;
        _ = running.ContinueWith(async _ =>
        {
            try { await _host.CallAsync(GitPluginId, "command/execute", Encoding.UTF8.GetBytes($"{{\"command\":\"{GitClosedCommand}\",\"document\":\"{document}\",\"revision\":0}}")); }
            catch (Exception e) when (e is PluginUnavailableException or PluginCrashedException or NksHub.Textida.Rpc.RpcException or IOException or TimeoutException) { /* the plugin is gone: its watches went with it */ }
        }, TaskScheduler.Default);
    }

    // ---- early tools: outline, reveal, snippets, Tab ----

    private (MainWindow Window, DocTab Tab)? TabOf(Guid document)
    {
        foreach (var window in _app.Windows)
            foreach (var tab in window.Tabs)
                if (tab.Doc.Doc.Id == document) return (window, tab);
        return null;
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>Draws the outline of a document in the plugin panel; an outdated set stays, marked as such, until a current one replaces it.</summary>
    public void SymbolsChanged(SymbolSet set, bool current) => OnUi(() =>
    {
        if (!current && set.PluginId == CodeExplorerPluginId) _ = RefreshOutlineAsync(set.Document);
        bool first = _outlines.Add((set.PluginId, set.Document));
        var byParent = set.Symbols.GroupBy(s => s.ParentId ?? "").ToDictionary(g => g.Key, g => g.ToList());
        List<PanelItem> Level(string parent) => byParent.TryGetValue(parent, out var list)
            ? [.. list.Select(s => new PanelItem(s.Id, s.Name, s.Kind.Length == 0 ? null : s.Kind, Level(s.Id)))]
            : [];
        var title = Loc.Get(current ? K.PluginSymbolsTitle : K.PluginSymbolsOutdated);
        // a new set of the code outline, also the outdated one carried over an edit, keeps the mark of the item the caret is in
        string? caretItem = set.PluginId == CodeExplorerPluginId && TabOf(set.Document) is { } found && CaretOf(found.Tab) is { } at ? SymbolAt(set.Symbols, at)?.Id : null;
        Ui.SetPanel(set.PluginId, new PanelModel(SymbolsPanelPrefix + set.Document.ToString("N"), title, PanelKind.Tree, Level(""), Reveal: (current && _revealAlways) || (first && _revealOutline), Current: caretItem));
    });

    public void SymbolsCleared(string pluginId, Guid document) => OnUi(() =>
    {
        if (_outlines.Remove((pluginId, document))) Ui.RemovePanel(pluginId, SymbolsPanelPrefix + document.ToString("N"));
    });

    public bool Reveal(Guid document, ByteRange range) => TabOf(document) is { } found && found.Window.RevealBytes(found.Tab, range);

    public void PlaceSelections(Guid document, SelectionSet selections)
    {
        if (TabOf(document) is { } found) found.Window.PlaceSelections(found.Tab, selections);
    }

    public TableSelectionData? TableSelection(Guid document) =>
        TabOf(document) is { Tab: { TableShown: true, Table: { } table } } ? table.SelectionData() : null;

    public void SetClipboard(Guid document, string text, string? html)
    {
        if (TabOf(document) is { } found) found.Window.WriteClipboard(text, html);
    }

    public Document OpenVirtual(string pluginId, string name, string? title, ReadOnlyMemory<byte> content, TextCodec codec, VirtualInfo info)
    {
        var window = _app.ActiveWindow ?? throw new InvalidOperationException("There is no window to show the document in.");
        var document = Document.CreateVirtual(name, content, AvaloniaEditBackendFactory.Instance, codec, info.Editable);
        window.OpenVirtualDocument(document, title, new VirtualBinding(pluginId, PluginName(pluginId), info));
        return document;
    }

    public void CloseVirtual(Guid document)
    {
        if (TabOf(document) is { } found) found.Window.CloseVirtualDocument(found.Tab.Doc);
    }

    /// <summary>The name a plugin goes by for the user: the one of a built-in plugin or of an installed one, else its id.</summary>
    internal string PluginName(string pluginId) =>
        BuiltIns.FirstOrDefault(b => b.Id == pluginId).Name ?? Manager.Installed().FirstOrDefault(p => p.Id == pluginId)?.Name ?? pluginId;

    public bool OpenDiff(DiffRequest request)
    {
        if (_app.ActiveWindow is not { } window) return false;
        window.OpenDiffRequest(request);
        return true;
    }

    public string? FilePath(Guid document) => TabOf(document)?.Tab.Doc.Path;

    public string IndentUnit(Guid document) =>
        TabOf(document) is { } found && found.Tab.Surface is { } surface ? surface.Settings.Indent.Unit : "    ";

    public void BeginSnippets(Guid document, IReadOnlyList<(long Start, NksHub.Textida.Core.Snippets.ParsedSnippet Snippet)> snippets)
    {
        if (TabOf(document) is not { } found || found.Tab.Surface is not { } surface || found.Tab.Doc.TextBackend is not { } backend) return;
        var placed = new List<(int, NksHub.Textida.Core.Snippets.ParsedSnippet)>(snippets.Count);
        foreach (var (start, snippet) in snippets)
            if (backend.TryToCharOffset(start, out int at)) placed.Add((at, snippet));
        surface.BeginSnippet(placed);
    }

    public string NewLine(Guid document) =>
        TabOf(document) is { } found && found.Tab.Surface is { } surface ? found.Tab.Doc.NewLine(surface.Settings.ConfiguredEol) : "\n";

    /// <summary>A click (or Enter) on an item of the outline: reveals the symbol where it is now. False when the symbol is gone or the text it stood on was changed.</summary>
    public bool RevealSymbol(string pluginId, string panelId, string symbolId) =>
        panelId.StartsWith(SymbolsPanelPrefix, StringComparison.Ordinal) && Guid.TryParseExact(panelId[SymbolsPanelPrefix.Length..], "N", out var document)
        && Documents.ResolveSymbol(pluginId, document, symbolId) is { } range && Reveal(document, range);

    // ---- language services: diagnostics and completion from plugins, the schema check of the JSON plugin ----

    private const string SchemaMappingsSetting = "schema.mappings";

    private static volatile string[] s_mappedMasks = [];

    private string SchemaMappingsPath => Path.Combine(_app.ProfileDirectory, "schema-mappings.txt");

    /// <summary>
    /// Keeps the text of the setting <c>schema.mappings</c> in the file the JSON plugin reads its rules from (it reads the file again when it changes) and remembers the masks, so a file the user mapped to a schema
    /// is checked whatever its extension. A rule that is not written as <c>MASK=SCHEMA</c> is left out and named in the status line when <paramref name="report"/>.
    /// </summary>
    private void SyncSchemaMappings(bool report)
    {
        var text = _app.Workbench.Settings.GetString(SchemaMappingsSetting);
        s_mappedMasks = [.. SchemaMappingText.Parse(text, out var rejected).Select(e => e.Glob)];
        try
        {
            Directory.CreateDirectory(_app.ProfileDirectory);
            var temp = SchemaMappingsPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, SchemaMappingsPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the plugin keeps the rules it has */ }
        if (report && rejected.Count > 0) _app.ActiveWindow?.SetStatusMessage(Loc.Get(K.SchemaMappingRejected, ("entry", rejected[0])));
    }

    /// <summary>A text tab of a JSON, JSONC, TOML or YAML file, of a Markdown file with frontmatter that has a profile, or of a file the user mapped to a schema (setting <c>schema.mappings</c>), which the JSON plugin checks against a schema. A paged (large) tab has no text surface, so it is never one.</summary>
    internal static bool IsSchemaFile(DocTab tab) =>
        tab.Surface is not null && tab.Doc.Doc.FilePath is { } path && IsSchemaPath(path);

    /// <summary>A text tab of a package manifest (<c>package.json</c>, <c>composer.json</c>, <c>requirements.txt</c>, <c>*.csproj</c>, <c>go.mod</c>, <c>Cargo.toml</c>, <c>Gemfile</c>) that the JSON plugin can ask a registry about.</summary>
    internal static bool IsManifestFile(DocTab tab) =>
        tab.Surface is not null && tab.Doc.Doc.FilePath is { } path && PackageManifests.For(path) is not null;

    /// <summary>Markdown is only checked where the plugin has a frontmatter profile: <c>SKILL.md</c>, <c>agents/*.md</c>, <c>rules/*.md</c>, <c>.claude/rules</c> and <c>.mdc</c>.</summary>
    internal static bool IsSchemaPath(string path)
    {
        if (s_mappedMasks.Any(mask => Glob.IsMatch(mask, path))) return true;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".json" or ".jsonc" or ".toml" or ".yaml" or ".yml" or ".mdc") return true;
        if (extension != ".md") return false;
        var folder = Path.GetFileName(Path.GetDirectoryName(path));
        return Path.GetFileName(path) == "SKILL.md" || folder is "agents" or "rules" || path.Replace('\\', '/').Contains("/.claude/rules/", StringComparison.Ordinal);
    }

    /// <summary>Draws the diagnostics of a document again: byte ranges of the current revision become the underlined text offsets of its tab.</summary>
    public void DiagnosticsChanged(Guid document) => OnUi(() =>
    {
        if (TabOf(document) is not { } found || found.Tab.Surface is not { } surface || found.Tab.Doc.TextBackend is not { } backend) return;
        var marks = new List<DiagnosticMark>();
        foreach (var d in Documents.DiagnosticsOf(document))
            if (backend.TryToCharOffset(d.Range.Start, out int start) && backend.TryToCharOffset(d.Range.End, out int end))
                marks.Add(new DiagnosticMark(start, end, d.Severity, d.Message));
        surface.SetDiagnostics(marks);
    });

    /// <summary>Publishes what the editor found itself (a linter run) for a tab through the store plugins publish to; the document is exposed and stays so, as for an outline. Call on the UI thread.</summary>
    public int PublishOwnDiagnostics(DocTab tab, string source, long revision, IReadOnlyList<Diagnostic> diagnostics)
    {
        var document = tab.Doc.Doc;
        if (!Documents.IsExposed(document.Id))
        {
            Documents.Expose(document, tab.Surface is { } surface ? surface.ByteSelections : null);
            Keep(tab.Doc, document);
        }
        return Documents.PublishOwn(document, source, revision, diagnostics);
    }

    /// <summary>
    /// What the schema check said about a file: <paramref name="State"/> is <c>ready</c>, <c>noConsent</c> (a schema from the network whose <paramref name="Host"/> the user has not allowed), <c>unavailable</c>,
    /// <c>tooLarge</c>, <c>invalid</c> or <c>unsupported</c> (a form of YAML the reader does not know); <paramref name="Cached"/> and <paramref name="Offline"/> say that a downloaded schema came from its copy on disk.
    /// </summary>
    internal sealed record SchemaResult(long Revision, string? Name, string State, string? Host, bool Cached, bool Offline);

    private readonly Dictionary<Guid, SchemaResult> _schemaChecked = [];
    private DispatcherTimer? _schemaTimer;
    private (MainWindow Window, DocTab Tab)? _schemaWanted;
    private bool _schemaShown;

    /// <summary>The last schema check of the file in the tab, or null when it has not been checked yet.</summary>
    internal SchemaResult? SchemaResultOf(DocTab tab) => _schemaChecked.TryGetValue(tab.Doc.Doc.Id, out var found) ? found : null;

    internal static string SchemaStatusText(SchemaResult result) => result.Name is not { } name ? Loc.Get(K.SchemaNone) : result.State switch
    {
        "noConsent" => Loc.Get(K.SchemaNoConsent, ("name", name)),
        "unavailable" or "tooLarge" or "invalid" => Loc.Get(K.SchemaUnavailable, ("name", name)),
        "unsupported" => Loc.Get(K.SchemaUnsupported, ("name", name)),
        _ => Loc.Get(K.SchemaName, ("name", name)) + (result.Offline ? " " + Loc.Get(K.SchemaOffline) : result.Cached ? " " + Loc.Get(K.SchemaCopy) : ""),
    };

    private void ShowSchema(SchemaResult? status)
    {
        if (status is not { } shown)
        {
            if (!_schemaShown) return;
            _schemaShown = false;
            Ui.SetStatus(JsonPluginId, null);
            return;
        }
        _schemaShown = true;
        Ui.SetStatus(JsonPluginId, new StatusModel(SchemaStatusText(shown), Loc.Get(shown.State == "noConsent" ? K.SchemaTipConsent : K.SchemaTip)));
    }

    /// <summary>
    /// The status bar is drawn for this tab: shows which schema the file is checked against ("no schema" when none applies) and, once the text has rested after an edit,
    /// checks it again. Any other tab clears the status item.
    /// </summary>
    public void TrackSchema(MainWindow window, DocTab? tab)
    {
        if (tab is null || !(IsSchemaFile(tab) || IsManifestFile(tab)))
        {
            _schemaWanted = null;
            ShowSchema(null);
            return;
        }
        var document = tab.Doc.Doc;
        AttachHover(tab);
        bool known = _schemaChecked.TryGetValue(document.Id, out var last);
        ShowSchema(known && IsSchemaFile(tab) ? last : null);
        if (known && last!.Revision == document.Revision)
        {
            _schemaWanted = null;
            return;
        }
        _schemaWanted = (window, tab);
        if (_schemaTimer is null)
        {
            _schemaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.SchemaCheckDebounceMilliseconds) };
            _schemaTimer.Tick += (_, _) =>
            {
                _schemaTimer.Stop();
                if (_schemaWanted is { } wanted) _ = CheckSchemaAsync(wanted.Window, wanted.Tab);
            };
        }
        _schemaTimer.Stop();
        _schemaTimer.Start();
    }

    /// <summary>
    /// Has the JSON plugin check the file against its schema; the findings come back through the diagnostics API and are underlined, the schema name goes to the status bar.
    /// A document with findings stays visible to plugins so they follow its edits. Does nothing for a tab that is not a schema file.
    /// </summary>
    internal async Task CheckSchemaAsync(MainWindow window, DocTab tab)
    {
        bool schema = IsSchemaFile(tab);
        if (!schema && !IsManifestFile(tab)) return;
        var document = tab.Doc.Doc;
        long revision = document.Revision;
        string? name = null, state = "ready", host = null;
        bool cached = false, offline = false;
        if (schema)
        {
            if (Registries.AllowsAnySchema()) SyncRegistryProxy(throttle: true);
            var outcome = await ExecuteAsync(window, tab, JsonPluginId, JsonActivation, "JSON", SchemaCheckCommand, notify: false, background: true, keep: true);
            if (!outcome.Started)
            {
                if (_schemaWanted is not null) _schemaTimer?.Start(); // a command was using the document: ask again when it is free
                return;
            }
            var r = outcome.Result;
            if (r.ValueKind != JsonValueKind.Object || r.GetProperty("status").GetString() == "stale")
            {
                if (r.ValueKind == JsonValueKind.Object && _schemaWanted is not null) _schemaTimer?.Start();
                return;
            }
            name = r.GetProperty("name") is { ValueKind: JsonValueKind.String } n ? n.GetString() : null;
            if (r.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String) state = s.GetString()!;
            if (r.TryGetProperty("host", out var h) && h.ValueKind == JsonValueKind.String) host = h.GetString();
            cached = r.TryGetProperty("cached", out var c) && c.ValueKind == JsonValueKind.True;
            offline = r.TryGetProperty("offline", out var o) && o.ValueKind == JsonValueKind.True;
        }
        if (IsManifestFile(tab) && Registries.IsAllowed(PackageManifests.For(document.FilePath!)!.Id) && await CheckPackagesAsync(window, tab, refresh: false, background: true) is null)
        {
            if (_schemaWanted is not null) _schemaTimer?.Start(); // the document was busy: ask again when it is free
            if (!schema) return;
        }
        var result = new SchemaResult(revision, name, state, host, cached, offline);
        _schemaChecked[document.Id] = result;
        if (schema && ReferenceEquals(window.Active, tab)) ShowSchema(result);
        if (ReferenceEquals(tab.Doc.Doc, document) && document.Revision != revision) TrackSchema(window, tab); // typed while the plugin read
    }

    /// <summary>
    /// Has the JSON plugin ask the registry of the manifest whether a pinned version has a newer release; the marks come back through the diagnostics API. Nothing is sent, and the plugin is not
    /// even started for this, unless the user allowed the registry. Null when it did not run (no consent, busy, not a manifest), else the answer of the plugin.
    /// </summary>
    internal async Task<JsonElement?> CheckPackagesAsync(MainWindow window, DocTab tab, bool refresh, bool background, bool force = false)
    {
        if (tab.Doc.Doc.FilePath is not { } path || PackageManifests.For(path) is not { } registry || !force && !Registries.IsAllowed(registry.Id)) return null;
        SyncRegistryProxy();
        var outcome = await ExecuteAsync(window, tab, JsonPluginId, JsonActivation, "JSON", PackageCheckCommand, extra: refresh ? ",\"refresh\":true" : "", notify: !background, background: background, keep: true);
        return outcome.Started && outcome.Result.ValueKind == JsonValueKind.Object ? outcome.Result : null;
    }

    private long _proxySyncedAt = long.MinValue;
    private DateTime _policyStamp;

    /// <summary>
    /// Writes the proxy of the network policy to the file the plugin reads its consent from. With <paramref name="throttle"/> it is not done again within <see cref="PluginLimitsPlaceholders.RegistryProxySyncMilliseconds"/>,
    /// unless the policy file changed since the last time: a proxy the user has just set must not wait for the interval, or the plugin would go around it.
    /// </summary>
    internal void SyncRegistryProxy(bool throttle = false)
    {
        var stamp = File.GetLastWriteTimeUtc(_app.Remote.NetworkPolicyPath);
        if (throttle && stamp == _policyStamp && _proxySyncedAt != long.MinValue && Environment.TickCount64 - _proxySyncedAt < PluginLimitsPlaceholders.RegistryProxySyncMilliseconds) return;
        _policyStamp = stamp;
        _proxySyncedAt = Environment.TickCount64;
        NksHub.Textida.Remote.NetworkPolicy? policy;
        try { policy = _app.Remote.Policy(); }
        catch (NksHub.Textida.Remote.RemoteException) { policy = null; }
        Registries.Sync(policy);
    }

    /// <summary>Whether Ctrl+J should ask plugins: the tab has a text surface (a paged tab never does) and a plugin answers, or the JSON plugin can be started for the file.</summary>
    public bool AsksPluginsForCompletion(DocTab tab) => tab.Surface is not null && (IsSchemaFile(tab) || IsManifestFile(tab) || Documents.HasCompletionProviders);

    /// <summary>
    /// The offers of the plugins at the caret of the tab, for the revision it has now (null when no plugin answers). The document is visible to plugins only while they answer.
    /// </summary>
    public async Task<CompletionAnswer?> RequestCompletionAsync(DocTab tab)
    {
        if (tab.Surface is not { } surface) return null;
        if (IsSchemaFile(tab) || IsManifestFile(tab) && Registries.IsAllowed(PackageManifests.For(tab.Doc.Doc.FilePath!)!.Id)) await EnsureAsync(JsonActivation);
        if (!Documents.HasCompletionProviders || CaretOf(surface) is not { } caret) return null;
        var document = tab.Doc.Doc;
        bool own = !Documents.IsExposed(document.Id);
        if (own) Documents.Expose(document, surface.ByteSelections);
        try { return await Documents.RequestCompletionAsync(document.Id, caret, default, tab.Doc.Language.Id); }
        finally { if (own) await Dispatcher.UIThread.InvokeAsync(() => Documents.Withdraw(document)); }
    }

    /// <summary>Gives the text surface of a schema file the provider that asks the JSON plugin for the description under the pointer (once per surface; it asks again whether the tab is still such a file).</summary>
    private void AttachHover(DocTab tab)
    {
        if (tab.Surface is not { HoverProvider: null } surface || !IsSchemaFile(tab)) return;
        surface.HoverProvider = offset => RequestHoverAsync(tab, offset);
    }

    /// <summary>What plugins say about the text at a text offset of the tab, for the revision it has now; null when nothing is said or the document moved on while they answered.</summary>
    private async Task<HoverText?> RequestHoverAsync(DocTab tab, int offset)
    {
        if (tab.Surface is not { } surface || !IsSchemaFile(tab) || tab.Doc.TextBackend is not { } backend || !Documents.HasHoverProviders) return null;
        var document = tab.Doc.Doc;
        long at = backend.ToByteOffsets([offset])[0];
        bool own = !Documents.IsExposed(document.Id);
        if (own) Documents.Expose(document, surface.ByteSelections);
        try
        {
            var answer = await Documents.RequestHoverAsync(document.Id, at, Loc.Language);
            if (answer is not { Stale: false, Text: { } text, Range: { } range } || answer.Revision != document.Revision) return null;
            return backend.TryToCharOffset(range.Start, out int start) && backend.TryToCharOffset(range.End, out int end) ? new HoverText(start, end, text) : null;
        }
        finally { if (own) await Dispatcher.UIThread.InvokeAsync(() => Documents.Withdraw(document)); }
    }

    /// <summary>Writes an offer of a plugin: its text and additional edits as one undo step. False when the document is not at the revision of the offer any more.</summary>
    public bool ApplyCompletion(DocTab tab, CompletionItem item)
    {
        if (tab.Surface is not { } surface) return false;
        var document = tab.Doc.Doc;
        bool own = !Documents.IsExposed(document.Id);
        if (own) Documents.Expose(document, surface.ByteSelections);
        try
        {
            Documents.ApplyCompletion(item);
            return true;
        }
        catch (NksHub.Textida.Rpc.RpcException) { return false; }
        finally { if (own) Documents.Withdraw(document); }
    }

    // ---- Emmet ----

    /// <summary>The abbreviation syntax (<c>html</c> or <c>css</c>) Emmet expands in a language mode; null for any other mode.</summary>
    internal static string? EmmetSyntax(string languageId) => languageId is "html" or "css" ? languageId : null;

    /// <summary>Whether Tab in the tab goes to Emmet: a language mode it works in and the setting Emmet on Tab on for that language.</summary>
    internal static bool EmmetOnTab(DocTab tab) => tab.Surface is { Settings.Emmet: true } && EmmetSyntax(tab.Doc.Language.Id) is not null;

    /// <summary>Starts the Emmet process when it is not running, so that it can answer the Tab key. False when it cannot run; Tab then does what it does without it.</summary>
    private Task<bool> EnsureEmmetAsync() => EnsureAsync(EmmetActivation);

    private async Task<bool> EnsureAsync(string activation)
    {
        try
        {
            await _host.ActivateAsync(activation);
            return true;
        }
        catch (Exception e) when (e is PluginCrashedException or PluginHandshakeException or PluginUnavailableException or NksHub.Textida.Rpc.RpcException) { return false; }
    }

    /// <summary>
    /// Runs an Emmet command (<c>emmet.preview</c> or <c>emmet.wrap</c>) on the tab; the plugin asks the user what it needs and changes the document as one undo step.
    /// Returns false when nothing was started (no text editor, or a language mode Emmet does not work in).
    /// </summary>
    public async Task<bool> RunEmmetAsync(MainWindow window, DocTab tab, string command)
    {
        if (tab.Surface is not { } surface || EmmetSyntax(tab.Doc.Language.Id) is not { } syntax) return false;
        string extra = $",\"syntax\":\"{syntax}\",\"indent\":\"{System.Text.Json.JsonEncodedText.Encode(surface.Settings.Indent.Unit)}\"";
        return (await ExecuteAsync(window, tab, EmmetPluginId, EmmetActivation, "Emmet", command, extra)).Started;
    }

    /// <summary>
    /// Lets the plugins that registered for Tab answer it (Emmet is started by the first Tab in an HTML or CSS file, and not asked where its setting is off).
    /// False: nobody handled it and the caller indents as it does without plugins.
    /// </summary>
    public async Task<bool> OfferTabAsync(DocTab tab)
    {
        if (tab.Surface is not { } surface) return false;
        bool emmet = EmmetOnTab(tab);
        if (emmet) await EnsureEmmetAsync();
        if (!Documents.HasTabProviders) return false;
        var document = tab.Doc.Doc;
        // a document that stays visible to plugins (an outline provider) is not hidden again when Tab is done
        bool own = !Documents.IsExposed(document.Id);
        if (own) Documents.Expose(document, surface.ByteSelections);
        try { return await Documents.OfferTabAsync(document.Id, default, tab.Doc.Language.Id, surface.Settings.Indent.Unit, skip: emmet ? null : EmmetPluginId); }
        finally { if (own) await Dispatcher.UIThread.InvokeAsync(() => Documents.Withdraw(document)); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        await Acp.DisposeAsync();
        await Mcp.DisposeAsync();
        await _host.DisposeAsync();
        AppDomain.CurrentDomain.ProcessExit -= _exitHook;
        await Documents.DisposeAsync();
    }
}
