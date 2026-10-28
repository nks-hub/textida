using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The preview panel (P6.2.4) under the editor, in the look of the output window: the text, Markdown or HTML a plugin supplied for one revision of a document, rendered by
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
    private DocTab? _liveTab;
    private bool _liveOutline;
    private long _liveRevision;
    private DispatcherTimer? _liveTimer;
    private PreviewImages? _previewImages;
    private List<PreviewRenderer.Anchor>? _previewAnchors;
    private bool _previewSettling;
    private long _quietPreviewUntil, _quietEditorUntil;

    public bool PreviewPanelOpen => _previewPanel.IsVisible;
    internal PreviewContent? ShownPreview => _preview;
    internal string PreviewPanelStatus => _previewStatus.Text ?? "";
    internal Control? PreviewBody => _previewScroll.Content as Control;
    internal Control PreviewPanelControl => _previewPanel;
    internal ScrollViewer PreviewScroller => _previewScroll;

    /// <summary>The pictures of the previews of this window: local files, web pictures the user allowed, and what has been decoded already.</summary>
    internal PreviewImages PreviewPictures => _previewImages ??= new PreviewImages(() => _app.Remote.Policy().CreateHttpHandler(), ConfirmImageHostAsync, () => { if (_preview is not null) RenderPreview(keepScroll: true); });

    private Task<bool> ConfirmImageHostAsync(string host) =>
        PluginConfirmAsync(new ConfirmRequest(Loc.Get(K.PreviewImageConsentTitle), Loc.Get(K.PreviewImageConsentMessage, ("host", host))));

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
        _previewScroll.KeyDown += OnPreviewKeyDown;
        _previewScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.OffsetProperty) OnPreviewScrolled();
        };
        _previewPanel.Classes.Add("outputpanel");
        HelpContext.Set(_previewPanel, "tools.markdown");
        _previewPanel.Child = new DockPanel { Children = { Dock(header, Avalonia.Controls.Dock.Top), _previewScroll } };
        return _previewPanel;
    }

    /// <summary>The page scrolls from the keyboard when the preview or anything inside it has the focus: the arrows, Page Up, Page Down, Home and End.</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.Up: _previewScroll.LineUp(); break;
            case Key.Down: _previewScroll.LineDown(); break;
            case Key.PageUp: _previewScroll.PageUp(); break;
            case Key.PageDown: _previewScroll.PageDown(); break;
            case Key.Home: _previewScroll.ScrollToHome(); break;
            case Key.End: _previewScroll.ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void ApplyPreviewTexts()
    {
        _previewClose.Content = Loc.Get(K.PreviewPanelClose);
        AutomationProperties.SetName(_previewClose, Loc.Get(K.PreviewPanelClose));
        AutomationProperties.SetName(_previewPanel, Loc.Get(K.A11yPreviewPanel));
        AutomationProperties.SetName(_previewScroll, Loc.Get(K.A11yPreviewBody));
        AutomationProperties.SetLiveSetting(_previewStatus, AutomationLiveSetting.Polite); // "outdated" appears while the user types elsewhere
        if (_liveTab is not null) RefreshLive();
        else if (_preview is not null) RenderPreview(keepScroll: true);
    }

    /// <summary>Shows what a plugin supplied. The panel opens if it was closed; the scroll position stays when the same document is shown again.</summary>
    internal void ShowPreview(PreviewContent content)
    {
        StopLive();
        ShowPreviewCore(content);
    }

    private void ShowPreviewCore(PreviewContent content)
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
        bool follow = _liveTab is not null && !_liveOutline;
        _previewAnchors = follow ? [] : null;
        _previewSettling = follow;
        string? folder = _liveTab?.Doc.Doc.FilePath is { Length: > 0 } path && Path.IsPathRooted(path) ? Path.GetDirectoryName(path) : null;
        var ctx = new PreviewRenderer.Ctx(mono, uri => _ = OpenPreviewLinkAsync(uri), PreviewPictures, folder, content.Images, _previewAnchors);
        _previewScroll.Content = _liveOutline && _liveTab is { } outlined
            ? PreviewRenderer.Outline(MarkdownTools.Headings(content.Text.ReplaceLineEndings("\n").Split('\n')), h => { outlined.GoToLine(h.Line + 1); FocusView(outlined); })
            : PreviewRenderer.Render(content.Format, content.Text, ctx);
        if (follow) Dispatcher.UIThread.Post(() => { _previewSettling = false; SyncPreviewToEditor(); }, DispatcherPriority.Background);
        else if (keepScroll) Dispatcher.UIThread.Post(() => _previewScroll.Offset = offset, DispatcherPriority.Background);
        var title = content.Title ?? Loc.Get(K.PreviewPanelTitle);
        _previewStatus.Text = _previewOutdatedBy is { } current
            ? Loc.Get(K.PreviewPanelOutdated, ("title", title), ("revision", content.Revision), ("current", current))
            : Loc.Get(K.PreviewPanelStatus,("title", title), ("revision", content.Revision));
    }

    /// <summary>Opens a link of a preview after the user confirmed it; the policy has already limited the target to the web and mail.</summary>
    internal async Task OpenPreviewLinkAsync(Uri uri)
    {
        if (PreviewPolicy.LinkTarget(uri.OriginalString) is null) return;
        if (!await PluginConfirmAsync(new ConfirmRequest(Loc.Get(K.PreviewLinkTitle), Loc.Get(K.PreviewLinkMessage, ("url", uri.AbsoluteUri))))) return;
        bool opened = _app.UriOpener is { } open ? await open(uri) : await Launcher.LaunchUriAsync(uri);
        if (!opened) SetStatusMessage(Loc.Get(K.PreviewLinkFailed, ("url", uri.AbsoluteUri)));
    }

    internal void MarkPreviewOutdated(string pluginId, Guid document, long currentRevision)
    {
        if (_preview is not { } shown || shown.PluginId != pluginId || shown.Document != document) return;
        _previewOutdatedBy = currentRevision;
        RenderPreview(keepScroll: true);
    }

    /// <summary>Closes the panel; with a plugin and document only if it shows that one.</summary>
    internal void ClosePreview(string? pluginId = null, Guid? document = null, bool focus = true)
    {
        if (_preview is not { } shown || (pluginId is not null && (shown.PluginId != pluginId || shown.Document != document))) return;
        StopLive();
        _preview = null;
        _previewScroll.Content = null;
        _previewPanel.IsVisible = false;
        if (focus && Active is { } tab) FocusView(tab);
    }

    // ---- live preview of the current file (P9.3.1) ----

    internal DocTab? LivePreviewTab => _liveTab;

    /// <summary>
    /// Shows the current Markdown or HTML file next to the editor and keeps it level with the text: every edit restarts a short timer and the panel is drawn again when it runs out, and the
    /// editor and the panel scroll together. HTML is drawn as the subset of <see cref="PreviewRenderer"/> and every other tag stays text. With <paramref name="outline"/> the panel lists the headings instead of the page.
    /// </summary>
    internal CommandOutcome ShowLivePreview(bool outline)
    {
        if (Active is not { Surface: not null } tab) return CommandOutcome.NotHandled;
        string language = tab.Doc.Language.Id;
        if (language != "markdown" && (outline || language != "html"))
        {
            SetStatusMessage(Loc.Get(outline ? K.PreviewOutlineUnsupported : K.PreviewLiveUnsupported));
            return CommandOutcome.Handled;
        }
        StopLive();
        (_liveTab, _liveOutline) = (tab, outline);
        tab.Doc.Changed += OnLiveEdited;
        tab.ViewClosed += OnLiveTabClosed;
        tab.Surface!.TextArea.TextView.ScrollOffsetChanged += OnLiveEditorScrolled;
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.PreviewLiveDebounceMs) };
        _liveTimer.Tick += (_, _) => RefreshLive();
        RefreshLive();
        return CommandOutcome.Handled;
    }

    private void OnLiveEdited()
    {
        _liveTimer?.Stop();
        _liveTimer?.Start();
    }

    private void OnLiveTabClosed() => ClosePreview(focus: false);

    private void StopLive()
    {
        if (_liveTab is { } tab)
        {
            tab.Doc.Changed -= OnLiveEdited;
            tab.ViewClosed -= OnLiveTabClosed;
            if (tab.Surface is { } surface) surface.TextArea.TextView.ScrollOffsetChanged -= OnLiveEditorScrolled;
        }
        _liveTimer?.Stop();
        (_liveTab, _liveTimer, _liveOutline, _previewAnchors, _previewSettling) = (null, null, false, null, false);
    }

    /// <summary>Draws the live preview from the text as it is now.</summary>
    internal void RefreshLive()
    {
        _liveTimer?.Stop();
        if (_liveTab is not { Surface: { } surface } tab) return;
        if (surface.Document.TextLength > PluginLimitsPlaceholders.PreviewLiveMaxChars)
        {
            ClosePreview();
            SetStatusMessage(Loc.Get(K.PreviewLiveTooLarge));
            return;
        }
        bool html = tab.Doc.Language.Id == "html";
        string title = Loc.Get(_liveOutline ? K.PreviewOutlineTitle : html ? K.PreviewLiveHtmlTitle : K.PreviewLiveTitle, ("name", tab.Title));
        ShowPreviewCore(new PreviewContent("", Guid.Empty, ++_liveRevision, html ? PreviewFormats.Html : PreviewFormats.Markdown, title, surface.Document.Text));
    }

    // ---- scrolling together (P9.3.1) ----

    /// <summary>The (fractional, zero-based) line at the top of the editor.</summary>
    internal static double TopLine(EditorSurface editor)
    {
        var view = editor.TextArea.TextView;
        if (view.GetDocumentLineByVisualTop(view.ScrollOffset.Y) is not { } line) return 0;
        double top = view.GetVisualTopByDocumentLine(line.LineNumber);
        double height = (line.LineNumber < editor.Document.LineCount ? view.GetVisualTopByDocumentLine(line.LineNumber + 1) : view.DocumentHeight) - top;
        return line.LineNumber - 1 + (height > 0 ? Math.Clamp((view.ScrollOffset.Y - top) / height, 0, 1) : 0);
    }

    private PreviewScrollMap? ScrollMap(EditorSurface editor) =>
        _previewAnchors is { Count: > 0 } anchors && _previewScroll.Content is Control body
            ? new PreviewScrollMap(anchors.Select(a => (a.Line, body.Bounds.Y + a.Control.Bounds.Y)), editor.Document.LineCount, _previewScroll.Extent.Height)
            : null;

    /// <summary>Scrolls the preview to where the top of the editor is.</summary>
    internal void SyncPreviewToEditor()
    {
        if (_liveTab?.Surface is not { } editor || ScrollMap(editor) is not { } map) return;
        double target = Math.Clamp(map.OffsetFor(TopLine(editor)), 0, Math.Max(0, _previewScroll.Extent.Height - _previewScroll.Viewport.Height));
        _quietPreviewUntil = Environment.TickCount64 + PluginLimitsPlaceholders.PreviewSyncQuietMs;
        _previewScroll.Offset = new Vector(0, target);
    }

    /// <summary>Scrolls the editor to where the top of the preview is.</summary>
    internal void SyncEditorToPreview()
    {
        if (_liveTab?.Surface is not { } editor || ScrollMap(editor) is not { } map) return;
        var view = editor.TextArea.TextView;
        double line = Math.Clamp(map.LineAt(_previewScroll.Offset.Y), 0, editor.Document.LineCount - 1);
        int number = (int)Math.Floor(line) + 1;
        double top = view.GetVisualTopByDocumentLine(number);
        double height = (number < editor.Document.LineCount ? view.GetVisualTopByDocumentLine(number + 1) : view.DocumentHeight) - top;
        _quietEditorUntil = Environment.TickCount64 + PluginLimitsPlaceholders.PreviewSyncQuietMs;
        var scrollable = (ILogicalScrollable)view;
        scrollable.Offset = new Vector(scrollable.Offset.X, top + (line - Math.Floor(line)) * height);
    }

    private void OnLiveEditorScrolled(object? sender, EventArgs e)
    {
        if (!_previewSettling && Environment.TickCount64 >= _quietEditorUntil) SyncPreviewToEditor();
    }

    private void OnPreviewScrolled()
    {
        if (_liveTab is null || _previewSettling || Environment.TickCount64 < _quietPreviewUntil) return;
        SyncEditorToPreview();
    }
}
