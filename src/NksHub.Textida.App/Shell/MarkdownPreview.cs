using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The rendered page of a Markdown document inside a tab (P9.3.4): it fills the tab in the reading mode and stands beside the editor in the editing mode, where it follows every edit after a short pause and
/// scrolls together with the editor. It draws with the renderer and the safety policy of the live preview (P9.3.1, P6.2.7), so a script, a remote picture and a link to a local file do nothing here either.
/// It shows the text of the document as it is, saved or not.
/// </summary>
internal sealed class MarkdownPreview : ScrollViewer, IDisposable
{
    private readonly MainWindow _window;
    private readonly OpenDocument _doc;
    private readonly EditorSurface? _editor;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.PreviewLiveDebounceMs) };
    private List<PreviewRenderer.Anchor> _anchors = [];
    private long _renderedRevision = -1;
    private bool _settling;
    private long _quietPreviewUntil, _quietEditorUntil;
    private double? _pendingLine;

    /// <param name="editor">The editor of the editing mode, whose scrolling this page follows; null in the reading mode.</param>
    public MarkdownPreview(MainWindow window, OpenDocument doc, EditorSurface? editor = null)
    {
        _window = window;
        _doc = doc;
        _editor = editor;
        Focusable = true;
        Classes.Add("mdpreview");
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HelpContext.Set(this, "view.markdownModes");
        _timer.Tick += (_, _) => Refresh();
        doc.Changed += OnDocumentChanged;
        if (editor is not null) editor.TextArea.TextView.ScrollOffsetChanged += OnEditorScrolled;
        Refresh();
    }

    /// <summary>The scrolled view takes the look of every scroll viewer.</summary>
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    /// <summary>Where the selection of the editor was when this page took its place, as byte offsets; the editor gets the selection back when the user returns to it.</summary>
    public (long Anchor, long Caret) Selection { get; set; }

    private string Text => _editor?.Document.Text ?? _doc.TextBackend?.TextDocument.Text ?? "";

    private int LineCount => _editor?.Document.LineCount ?? _doc.TextBackend?.TextDocument.LineCount ?? 1;

    private void OnDocumentChanged()
    {
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Draws the page again from the text as it is now; nothing is done when the document is still at the revision that was drawn.</summary>
    public void Refresh(bool force = false)
    {
        _timer.Stop();
        long revision = _doc.Doc.Revision;
        if (!force && revision == _renderedRevision) return;
        _renderedRevision = revision;
        var offset = Offset;
        string text = Text;
        if (text.Length > PluginLimitsPlaceholders.PreviewLiveMaxChars)
        {
            _anchors = [];
            Content = new TextBlock { Text = Loc.Get(K.PreviewLiveTooLarge), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(12, 6) };
            return;
        }
        var anchors = _anchors = [];
        _settling = _editor is not null;
        Content = PreviewRenderer.Render(PreviewFormats.Markdown, text, _window.PreviewContext(_doc, anchors));
        UpdateName();
        if (_editor is not null) Dispatcher.UIThread.Post(() => { _settling = false; SyncPreviewToEditor(); }, DispatcherPriority.Background);
        else Dispatcher.UIThread.Post(() => Offset = offset, DispatcherPriority.Background);
        if (_pendingLine is not null) ApplyPendingLine();
    }

    /// <summary>The names follow the interface language.</summary>
    public void Retranslate() => Refresh(force: true);

    private void UpdateName() => AutomationProperties.SetName(this, Loc.Get(_editor is null ? K.ViewModeReadA11y : K.A11yPreviewBody, ("title", _doc.Title)));

    // ---- position ----

    private PreviewScrollMap? Map() =>
        _anchors is { Count: > 0 } anchors && Content is Control body
            ? new PreviewScrollMap(anchors.Select(a => (a.Line, body.Margin.Top + a.Control.Bounds.Y)), LineCount, Extent.Height)
            : null;

    /// <summary>The (fractional, zero-based) line of the source at the top of the page; 0 while nothing is laid out.</summary>
    public double TopLine => Map()?.LineAt(Offset.Y) ?? 0;

    /// <summary>Scrolls the page to a (fractional, zero-based) line of the source; when the page is not laid out yet, as soon as it is.</summary>
    public void ScrollToLine(double line)
    {
        _pendingLine = line;
        Dispatcher.UIThread.Post(ApplyPendingLine, DispatcherPriority.Background);
    }

    private void ApplyPendingLine()
    {
        if (_pendingLine is not { } line) return;
        if (Extent.Height <= 0 || Viewport.Height <= 0)
        {
            LayoutUpdated -= OnLayoutForPending;
            LayoutUpdated += OnLayoutForPending;
            return;
        }
        _pendingLine = null;
        if (Map() is not { } map) return; // a page without blocks has nowhere to scroll to
        Offset = new Vector(0, Math.Clamp(map.OffsetFor(line), 0, Math.Max(0, Extent.Height - Viewport.Height)));
    }

    private void OnLayoutForPending(object? sender, EventArgs e)
    {
        LayoutUpdated -= OnLayoutForPending;
        ApplyPendingLine();
    }

    // ---- scrolling together with the editor ----

    /// <summary>Scrolls the editor so that the (fractional, zero-based) line is at its top.</summary>
    internal static void ScrollEditorTo(EditorSurface editor, double line)
    {
        var view = editor.TextArea.TextView;
        line = Math.Clamp(line, 0, editor.Document.LineCount - 1);
        int number = (int)Math.Floor(line) + 1;
        double top = view.GetVisualTopByDocumentLine(number);
        double height = (number < editor.Document.LineCount ? view.GetVisualTopByDocumentLine(number + 1) : view.DocumentHeight) - top;
        var scrollable = (ILogicalScrollable)view;
        scrollable.Offset = new Vector(scrollable.Offset.X, top + (line - Math.Floor(line)) * height);
    }

    private void SyncPreviewToEditor()
    {
        if (_editor is null || Map() is not { } map) return;
        double target = Math.Clamp(map.OffsetFor(MainWindow.TopLine(_editor)), 0, Math.Max(0, Extent.Height - Viewport.Height));
        _quietPreviewUntil = Environment.TickCount64 + PluginLimitsPlaceholders.PreviewSyncQuietMs;
        Offset = new Vector(0, target);
    }

    private void SyncEditorToPreview()
    {
        if (_editor is null || Map() is not { } map) return;
        _quietEditorUntil = Environment.TickCount64 + PluginLimitsPlaceholders.PreviewSyncQuietMs;
        ScrollEditorTo(_editor, map.LineAt(Offset.Y));
    }

    private void OnEditorScrolled(object? sender, EventArgs e)
    {
        if (!_settling && Environment.TickCount64 >= _quietEditorUntil) SyncPreviewToEditor();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OffsetProperty && _editor is not null && !_settling && Environment.TickCount64 >= _quietPreviewUntil) SyncEditorToPreview();
    }

    // ---- keyboard ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.Up: LineUp(); break;
            case Key.Down: LineDown(); break;
            case Key.PageUp: PageUp(); break;
            case Key.PageDown: PageDown(); break;
            case Key.Home: ScrollToHome(); break;
            case Key.End: ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    public void Dispose()
    {
        _timer.Stop();
        LayoutUpdated -= OnLayoutForPending;
        _doc.Changed -= OnDocumentChanged;
        if (_editor is not null) _editor.TextArea.TextView.ScrollOffsetChanged -= OnEditorScrolled;
    }
}
