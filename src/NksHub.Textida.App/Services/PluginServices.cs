using System.Text;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using Host = NksHub.Textida.PluginHost.PluginHost;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The plugin side of the app: the host, the document API, the UI API and the built-in plugins. Nothing starts a process until a command needs it.
/// Limits come from <see cref="PluginLimitsPlaceholders"/> only.
/// </summary>
internal sealed class PluginServices : IEditProposals, IDocumentOwnerThread, IEditorTools, IAsyncDisposable
{
    public const string JsonPluginId = "textida.json";
    private const string JsonActivation = "onCommand:json";

    /// <summary>The outline of a document is the panel with this prefix and the document id; its items are the symbol ids.</summary>
    public const string SymbolsPanelPrefix = "symbols:";

    private readonly record struct Run(MainWindow Window, DocTab Tab);

    private readonly App _app;
    private readonly Host _host;
    private readonly Dictionary<Guid, Run> _running = [];
    private readonly HashSet<(string Plugin, Guid Document)> _outlines = [];

    public PluginUiHost Ui { get; }
    public DocumentApi Documents { get; }
    public UiApi UiApi { get; }
    internal PluginPermissions Permissions { get; } = new();

    /// <param name="app">The app whose window shows the plugin UI.</param>
    /// <param name="pluginDirectory">Folder with the built-in plugin programs; by default <c>plugins</c> next to the app.</param>
    public PluginServices(App app, string? pluginDirectory = null)
    {
        _app = app;
        Ui = new PluginUiHost(app);
        PluginEventSender send = (plugin, name, payload) => _host!.SendEventAsync(plugin, name, payload);
        PluginRequestSender request = (plugin, method, payload, timeout, cancellation) => _host!.CallAsync(plugin, method, payload, timeout, cancellation);
        // the built-in formatter is trusted: it reads the selection and writes it back
        var permissions = Permissions;
        foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditPropose, DocumentPermissions.EditDirect })
            permissions.Grant(JsonPluginId, permission, GrantScope.All);
        Documents = new DocumentApi(PluginLimitsPlaceholders.Documents(this, this, permissions, new PluginCallLog(PluginLimitsPlaceholders.CallLogCapacity)), this, send, request);
        UiApi = new UiApi(PluginLimitsPlaceholders.Ui(), Ui, send);
        Ui.Api = UiApi;
        _host = new Host(PluginLimitsPlaceholders.Host(), PluginApiRouter.Create(Documents.HandleAsync, ("ui/", UiApi.HandleAsync)), Documents);
        _host.StateChanged += (id, state) => { if (state is PluginState.WaitingRestart or PluginState.Disabled or PluginState.Stopped) Dispatcher.UIThread.Post(() => UiApi.PluginEnded(id)); };

        var folder = Path.Combine(pluginDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins"), "json");
        var program = Path.Combine(folder, "NksHub.Textida.Plugins.Json" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        _host.Register(new PluginSpec(JsonPluginId, "JSON", program,
            ["--max-bytes", PluginLimitsPlaceholders.JsonFormatterMaxBytes.ToString(), "--indent", PluginLimitsPlaceholders.JsonFormatterIndent.ToString()],
            [JsonActivation], folder));
    }

    internal Host HostForTests => _host;

    /// <summary>A plugin command is running (the document is exposed to plugins only meanwhile).</summary>
    public bool IsBusy => _running.Count > 0;

    public Task<T> InvokeAsync<T>(Func<T> work) => Dispatcher.UIThread.InvokeAsync(work).GetTask();

    /// <summary>The window that runs the command shows the proposal in the same preview as the other whole-document actions; the document is not touched until the user applies it.</summary>
    public async ValueTask<string> SubmitAsync(EditProposal proposal, CancellationToken cancellation)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_running.TryGetValue(proposal.DocumentId, out var run)) _ = run.Window.ReviewPluginProposalAsync(proposal, run.Tab);
        });
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Runs "Format JSON" (or its preview) on the text tab through the JSON plugin process. The document is visible to plugins only while the
    /// command runs. Returns false when nothing was started (no text editor, or the command is already running on this document).
    /// </summary>
    public async Task<bool> RunJsonAsync(MainWindow window, DocTab tab, bool preview)
    {
        if (tab.Surface is not { } surface) return false;
        var document = tab.Doc.Doc;
        if (_running.ContainsKey(document.Id)) return false;
        _running[document.Id] = new Run(window, tab);
        Documents.Expose(document, surface.ByteSelections);
        try
        {
            await _host.ActivateAsync(JsonActivation);
            var request = $"{{\"command\":\"{(preview ? "json.preview" : "json.format")}\",\"document\":\"{document.Id}\",\"revision\":{document.Revision},\"language\":\"{Loc.Language}\"}}";
            await _host.CallAsync(JsonPluginId, "command/execute", Encoding.UTF8.GetBytes(request));
        }
        catch (Exception e) when (e is PluginCrashedException or PluginHandshakeException or PluginUnavailableException or NksHub.Textida.Rpc.RpcException)
        {
            window.ShowNotice("plugin:" + JsonPluginId, Loc.Get(K.PluginUnavailable, ("name", "JSON"), ("reason", e.Message)), [], warning: true);
        }
        finally
        {
            Documents.Withdraw(document);
            _running.Remove(document.Id);
        }
        return true;
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
        bool first = _outlines.Add((set.PluginId, set.Document));
        var byParent = set.Symbols.GroupBy(s => s.ParentId ?? "").ToDictionary(g => g.Key, g => g.ToList());
        List<PanelItem> Level(string parent) => byParent.TryGetValue(parent, out var list)
            ? [.. list.Select(s => new PanelItem(s.Id, s.Name, s.Kind.Length == 0 ? null : s.Kind, Level(s.Id)))]
            : [];
        var title = Loc.Get(current ? K.PluginSymbolsTitle : K.PluginSymbolsOutdated);
        Ui.SetPanel(set.PluginId, new PanelModel(SymbolsPanelPrefix + set.Document.ToString("N"), title, PanelKind.Tree, Level(""), Reveal: first));
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

    public string NewLine(Guid document) =>
        TabOf(document) is { } found && found.Tab.Surface is { } surface ? found.Tab.Doc.NewLine(surface.Settings.ConfiguredEol) : "\n";

    /// <summary>A click (or Enter) on an item of the outline: reveals the symbol where it is now. False when the symbol is gone or the text it stood on was changed.</summary>
    public bool RevealSymbol(string pluginId, string panelId, string symbolId) =>
        panelId.StartsWith(SymbolsPanelPrefix, StringComparison.Ordinal) && Guid.TryParseExact(panelId[SymbolsPanelPrefix.Length..], "N", out var document)
        && Documents.ResolveSymbol(pluginId, document, symbolId) is { } range && Reveal(document, range);

    /// <summary>Lets the plugins that registered for Tab answer it. False: nobody handled it and the caller indents as it does without plugins.</summary>
    public async Task<bool> OfferTabAsync(DocTab tab)
    {
        if (!Documents.HasTabProviders || tab.Surface is not { } surface) return false;
        var document = tab.Doc.Doc;
        // a document that stays visible to plugins (an outline provider) is not hidden again when Tab is done
        bool own = !Documents.IsExposed(document.Id);
        if (own) Documents.Expose(document, surface.ByteSelections);
        try { return await Documents.OfferTabAsync(document.Id); }
        finally { if (own) await Dispatcher.UIThread.InvokeAsync(() => Documents.Withdraw(document)); }
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await Documents.DisposeAsync();
    }
}
