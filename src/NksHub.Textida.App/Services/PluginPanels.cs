using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Services;

/// <summary>The preview panel of the window that is active when a plugin shows a preview (P6.2.4).</summary>
internal sealed class PluginPreviewPanel(App app) : IPreviewPanel
{
    private MainWindow? _window;

    public void Show(PreviewContent content)
    {
        var window = app.ActiveWindow ?? throw new InvalidOperationException("There is no window to show the preview in.");
        if (_window is not null && _window != window) _window.ClosePreview();
        _window = window;
        window.ShowPreview(content);
    }

    public void Outdated(string pluginId, Guid document, long currentRevision) => _window?.MarkPreviewOutdated(pluginId, document, currentRevision);

    public void Close(string pluginId, Guid document)
    {
        if (_window is not { } window) return;
        if (Dispatcher.UIThread.CheckAccess()) window.ClosePreview(pluginId, document);
        else Dispatcher.UIThread.Post(() => window.ClosePreview(pluginId, document));
    }
}

/// <summary>The agent panel of the active window as the conversation of a plugin (P6.2.5): the panel, the permission dialog and the review are the ones of the agents over ACP.</summary>
internal sealed class PluginAgentPanel(PluginServices plugins, App app) : IAgentPanel
{
    public bool Open(string pluginId, string name)
    {
        var window = app.ActiveWindow ?? throw new InvalidOperationException("There is no window to show the agent panel in.");
        if (!plugins.Acp.OpenPluginSession(window, pluginId, name)) return false;
        window.ToggleAgentPanel(true);
        return true;
    }

    public bool HasSession(string pluginId) => plugins.Acp.HasSession(pluginId);

    public void Update(string pluginId, AgentUpdate update) => plugins.Acp.UpdatePluginSession(pluginId, update);

    public void Close(string pluginId) => plugins.Acp.ClosePluginSession(pluginId);

    public Task<string?> AskAsync(string pluginId, string tool, IReadOnlyList<AgentPermissionOption> options, CancellationToken cancellation) =>
        plugins.Acp.HasSession(pluginId) ? plugins.Acp.AskPluginAsync(tool, options, cancellation) : Task.FromResult<string?>(null);
}
