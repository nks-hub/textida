using System.Runtime.CompilerServices;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The life of the plugin processes as the app drives it: the activation events that start a plugin (<c>onStartup</c>, <c>onLanguage</c>, <c>onFileType</c>, <c>workspaceContains</c>; <c>onCommand</c> is fired
/// by the commands themselves, <c>onPanel</c> by the list of panels), the state, start, stop and restart the plugin manager shows, and the bounded end of all processes when the editor closes.
/// </summary>
internal sealed partial class PluginServices
{
    private readonly EventHandler _exitHook;

    /// <summary>What was already announced to the plugins per document: its language and file name, so a status refresh does not fire the same event again.</summary>
    private readonly HashSet<(Guid Document, string Key)> _activatedFor = [];

    /// <summary>The document each window showed at its last look, so that showing a document again (a switch of tab) counts as a new activation event.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<MainWindow, StrongBox<Guid>> _shown = new();

    private readonly HashSet<string> _scannedFolders = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private int _startupFired;
    private int _disposed;

    /// <summary>The editor has started: the plugins that list <c>onStartup</c> start, in the background. Fires once.</summary>
    public void ActivateStartup()
    {
        if (Interlocked.Exchange(ref _startupFired, 1) == 1) return;
        _ = ActivateInBackgroundAsync([ActivationEvents.Startup], []);
    }

    /// <summary>
    /// Tells the plugins what the window shows: the language and file type of the active document (<c>onLanguage</c>, <c>onFileType</c>) and the folders it has open (<c>workspaceContains</c>).
    /// Each is announced once per document and once per folder, so calling it on every refresh of the status bar costs a few set lookups; a document that is shown again after another one
    /// (or a tab without text) is announced again, so a plugin that was stopped, or ended for being idle, starts at the next activation event as promised.
    /// </summary>
    public void TrackActivation(MainWindow window, DocTab? tab)
    {
        List<string> events = [];
        List<string> folders = [];
        if (window.Files?.Roots is { Count: > 0 } roots)
            foreach (var root in roots)
                if (_scannedFolders.Add(root)) folders.Add(root);
        var shown = _shown.GetOrCreateValue(window);
        if (tab is { Custom: null, Hex: null } && !tab.Doc.IsVirtual && !tab.Doc.IsResults)
        {
            var document = tab.Doc.Doc;
            var language = tab.Doc.Language.Id;
            bool shownAgain = shown.Value != document.Id;
            shown.Value = document.Id;
            if (_activatedFor.Add((document.Id, language + "\n" + document.FilePath)) || shownAgain)
            {
                events.Add(ActivationEvents.Language(language));
                if (ActivationEvents.FileType(document.FilePath) is { } fileType) events.Add(fileType);
            }
        }
        else shown.Value = Guid.Empty;
        if (events.Count > 0 || folders.Count > 0) _ = ActivateInBackgroundAsync(events, folders);
    }

    /// <summary>The panels that plugins list as <c>onPanel:id</c> and that none shows now; the plugin starts when the user opens one and then shows it.</summary>
    internal IReadOnlyList<(string PluginId, string Name, string PanelId)> UnshownPanels() =>
        [.. _host.DeclaredPanels().Where(d => !Ui.Panels.Any(p => p.PluginId == d.PluginId && p.Model.Id == d.PanelId))];

    /// <summary>Fires <c>onPanel:id</c>: the plugins that list it start. Null when they run, else why one does not.</summary>
    internal Task<string?> ActivatePanelAsync(string panelId) => LifecycleAsync(() => _host.ActivateAsync(ActivationEvents.Panel(panelId)));

    /// <summary>Fires the events and the folder search; a plugin that cannot start is passed over with a note in the status bar (a crash has its own notice).</summary>
    private async Task ActivateInBackgroundAsync(IReadOnlyList<string> events, IReadOnlyList<string> folders)
    {
        foreach (var activation in events)
            await ReportAsync(() => _host.ActivateAsync(activation)).ConfigureAwait(false);
        if (folders.Count > 0) await ReportAsync(() => _host.ActivateWorkspaceAsync(folders)).ConfigureAwait(false);
    }

    private async Task ReportAsync(Func<Task> start)
    {
        try { await start().ConfigureAwait(false); }
        catch (Exception e) when (e is PluginHandshakeException or PluginUnavailableException or RpcException)
        {
            var name = e is PluginHandshakeException handshake ? PluginName(handshake.PluginId) : null;
            Dispatcher.UIThread.Post(() => _app.ActiveWindow?.SetStatusMessage(name is null ? e.Message : Loc.Get(K.PluginUnavailable, ("name", name), ("reason", e.Message))));
        }
        catch (Exception e) when (e is PluginCrashedException or InvalidOperationException) { /* a crash has its own notice; a closing host takes no more events */ }
    }

    // ---- what the plugin manager shows and does ----

    /// <summary>The state of a plugin process; null for a plugin the host does not know (a data plugin has no process).</summary>
    internal PluginStatus? StatusOf(string pluginId) => _host.IsRegistered(pluginId) ? _host.GetStatus(pluginId) : null;

    /// <summary>The state in words: running, starting, not running (it starts when needed), crashed and waiting for its restart, or turned off with the reason.</summary>
    internal static string StateText(PluginStatus status) => status.State switch
    {
        PluginState.Running => Loc.Get(status.IdleStop ? K.PluginStateRunningIdle : K.PluginStateRunning),
        PluginState.Starting => Loc.Get(K.PluginStateStarting),
        PluginState.Stopping => Loc.Get(K.PluginStateStopping),
        PluginState.WaitingRestart => status.LastCrash is { } crash
            ? Loc.Get(K.PluginStateWaiting, ("reason", CrashReasonText(crash.Reason)), ("seconds", (int)Math.Ceiling((crash.NextRestartIn ?? TimeSpan.Zero).TotalSeconds)))
            : Loc.Get(K.PluginStateStarting),
        PluginState.Disabled => Loc.Get(K.PluginStateDisabled, ("reason", status.LastCrash is { } last ? CrashReasonText(last.Reason) : status.DisabledReason ?? "")),
        _ => Loc.Get(K.PluginStateInactive),
    };

    /// <summary>Starts the plugin now, whatever its activation events say; a plugin that was turned off gets a clean slate. Null when it runs, else why it does not.</summary>
    internal Task<string?> StartPluginAsync(string pluginId) => LifecycleAsync(() => _host.StartAsync(pluginId));

    /// <summary>Ends the plugin process; it starts again at its next activation event. Null when done.</summary>
    internal Task<string?> StopPluginAsync(string pluginId) => LifecycleAsync(() => _host.StopAsync(pluginId));

    /// <summary>Stops the plugin and starts it again with a new handshake. Null when it runs again, else why it does not.</summary>
    internal Task<string?> RestartPluginAsync(string pluginId) => LifecycleAsync(() => _host.RestartAsync(pluginId));

    private static async Task<string?> LifecycleAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return null;
        }
        catch (Exception e) when (e is PluginCrashedException or PluginHandshakeException or PluginUnavailableException or RpcException or InvalidOperationException) { return e.Message; }
    }

    // ---- the end ----

    /// <summary>
    /// Ends the plugin services when the editor closes and does not wait for them longer than <see cref="PluginLimitsPlaceholders.ExitWaitSeconds"/>; a plugin process that is still alive then is killed by its process id,
    /// so closing the editor leaves none behind even when a plugin does not answer the request to leave.
    /// </summary>
    internal void Shutdown()
    {
        // on the thread pool: this runs on the UI thread, and a continuation that went back to it would wait for the wait below
        try { Task.Run(() => DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(PluginLimitsPlaceholders.ExitWaitSeconds)); }
        catch (AggregateException) { /* what failed to end is killed below */ }
        _host.KillAll();
    }
}
