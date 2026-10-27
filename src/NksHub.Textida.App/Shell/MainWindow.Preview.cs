using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The preview panel (P6.2.4) under the editor, in the look of the output window: the text or Markdown a plugin supplied for one revision of a document, rendered by
/// <see cref="PreviewRenderer"/>. The editor owns the panel; the plugin only supplies the data. When the document moves on, the panel says the preview is outdated until the
/// plugin shows the new revision. Showing a preview does not take the focus, because a plugin shows a new one while the user types.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Border _previewPanel = new() { IsVisible = false, Height = 280 };
    private readonly TextBlock _previewStatus = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button _previewClose = new();
    private readonly ScrollViewer _previewScroll = new() { Focusable = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private PreviewContent? _preview;
    private long? _previewOutdatedBy;

    public bool PreviewPanelOpen => _previewPanel.IsVisible;
    internal PreviewContent? ShownPreview => _preview;
    internal string PreviewPanelStatus => _previewStatus.Text ?? "";
    internal Control? PreviewBody => _previewScroll.Content as Control;
    internal Control PreviewPanelControl => _previewPanel;

    private Control BuildPreviewPanel()
    {
        _previewStatus.Classes.Add("findcount");
        _previewClose.Classes.Add("outputaction");
        var header = new DockPanel { Height = 30, LastChildFill = false };
        header.Children.Add(Dock(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), Children = { _previewClose } }, Avalonia.Controls.Dock.Right));
        header.Children.Add(Dock(_previewStatus, Avalonia.Controls.Dock.Left));
        _previewClose.Click += (_, _) => ClosePreview();
        _previewPanel.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            ClosePreview();
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _previewPanel.Classes.Add("outputpanel");
        _previewPanel.Child = new DockPanel { Children = { Dock(header, Avalonia.Controls.Dock.Top), _previewScroll } };
        return _previewPanel;
    }

    private void ApplyPreviewTexts()
    {
        _previewClose.Content = Loc.Get(K.PreviewPanelClose);
        AutomationProperties.SetName(_previewClose, Loc.Get(K.PreviewPanelClose));
        AutomationProperties.SetName(_previewPanel, Loc.Get(K.A11yPreviewPanel));
        AutomationProperties.SetName(_previewScroll, Loc.Get(K.A11yPreviewBody));
        if (_preview is not null) RenderPreview(keepScroll: true);
    }

    /// <summary>Shows what a plugin supplied. The panel opens if it was closed; the scroll position stays when the same document is shown again.</summary>
    internal void ShowPreview(PreviewContent content)
    {
        bool same = _preview is { } shown && shown.PluginId == content.PluginId && shown.Document == content.Document;
        _preview = content;
        _previewOutdatedBy = null;
        _previewPanel.IsVisible = true;
        RenderPreview(same);
    }

    private void RenderPreview(bool keepScroll)
    {
        if (_preview is not { } content) return;
        var offset = _previewScroll.Offset;
        var mono = new FontFamily(_app.Workbench.Settings.GetString("editor.fontFamily"));
        _previewScroll.Content = PreviewRenderer.Render(content.Format, content.Text, mono);
        if (keepScroll) Dispatcher.UIThread.Post(() => _previewScroll.Offset = offset, DispatcherPriority.Background);
        var title = content.Title ?? Loc.Get(K.PreviewPanelTitle);
        _previewStatus.Text = _previewOutdatedBy is { } current
            ? Loc.Get(K.PreviewPanelOutdated, ("title", title), ("revision", content.Revision), ("current", current))
            : Loc.Get(K.PreviewPanelStatus,("title", title), ("revision", content.Revision));
    }

    internal void MarkPreviewOutdated(string pluginId, Guid document, long currentRevision)
    {
        if (_preview is not { } shown || shown.PluginId != pluginId || shown.Document != document) return;
        _previewOutdatedBy = currentRevision;
        RenderPreview(keepScroll: true);
    }

    /// <summary>Closes the panel; with a plugin and document only if it shows that one.</summary>
    internal void ClosePreview(string? pluginId = null, Guid? document = null)
    {
        if (_preview is not { } shown || (pluginId is not null && (shown.PluginId != pluginId || shown.Document != document))) return;
        _preview = null;
        _previewScroll.Content = null;
        _previewPanel.IsVisible = false;
        if (Active is { } tab) FocusView(tab);
    }
}
