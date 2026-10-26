using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NksHub.Textida.Accessibility;
using NksHub.Textida.Accessibility.MacOS;
using NksHub.Textida.Accessibility.Windows;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The text view of the editor as <see cref="ITextAutomationSurface"/>: the document and its line index come from the
/// shared <see cref="DocumentTextSurface"/>, selection, focus and geometry from AvaloniaEdit.
/// </summary>
internal sealed class EditorTextSurface : DocumentTextSurface
{
    private readonly EditorSurface _editor;

    public EditorTextSurface(EditorSurface editor) : base(editor.Doc.Doc)
    {
        _editor = editor;
        editor.TextArea.Caret.PositionChanged += OnEditorSelection;
        editor.TextArea.SelectionChanged += OnEditorSelection;
        editor.TextArea.GotFocus += OnEditorFocus;
        editor.TextArea.LostFocus += OnEditorFocus;
        Document.Changed += OnDocumentEdited;
        SyncSelection();
    }

    private TextView TextView => _editor.TextArea.TextView;
    private double Scaling => TopLevel.GetTopLevel(_editor)?.RenderScaling ?? 1;

    // The bridges take pixels on Windows and points on macOS; Avalonia's screen coordinates are pixels on both.
    private double PixelsPerUnit => OperatingSystem.IsMacOS() ? Scaling : 1;

    private void OnEditorSelection(object? sender, EventArgs e) => SyncSelection();
    private void OnEditorFocus(object? sender, EventArgs e) => RaiseFocusChanged();

    // Runs after the base class has reloaded the text, so the caret that moved with the edit is read against the new text.
    private void OnDocumentEdited(object? sender, DocumentChangedEventArgs e) => SyncSelection();

    private void SyncSelection()
    {
        var sel = _editor.GetSelections()[0];
        Select(sel.Anchor, sel.Caret);
    }

    public override bool IsReadOnly => _editor.IsReadOnly;
    public override bool HasFocus => _editor.TextArea.IsFocused;
    public override void Focus() => _editor.TextArea.Focus();

    public override void SetSelection(int start, int length)
    {
        start = Math.Clamp(start, 0, Length);
        length = Math.Clamp(length, 0, Length - start);
        if (length == 0)
        {
            _editor.TextArea.ClearSelection();
            _editor.CaretOffset = start;
        }
        else _editor.Select(start, length);
    }

    public override void InvokeOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Invoke(action);
    }

    private ScreenRect ToScreen(Visual visual, Rect rect)
    {
        if (TopLevel.GetTopLevel(visual) is not { } top) return default;
        var origin = visual.PointToScreen(rect.TopLeft);
        double unit = 1 / PixelsPerUnit, scale = top.RenderScaling;
        return new(origin.X * unit, origin.Y * unit, rect.Width * scale * unit, rect.Height * scale * unit);
    }

    public override ScreenRect ScreenBounds => ToScreen(_editor, new Rect(_editor.Bounds.Size));

    // The visual lines are invalid between an edit and the next layout pass; build them on demand.
    private IReadOnlyList<VisualLine> ShownLines()
    {
        var view = TextView;
        if (!view.VisualLinesValid)
        {
            try { view.EnsureVisualLines(); }
            catch (VisualLinesInvalidException) { return []; }
        }
        return view.VisualLines;
    }

    public override TextSpan VisibleRange
    {
        get
        {
            var lines = ShownLines();
            if (lines.Count == 0) return default;
            int start = lines[0].FirstDocumentLine.Offset;
            var last = lines[^1].LastDocumentLine;
            return new(start, Math.Min(Length, last.Offset + last.TotalLength) - start);
        }
    }

    public override IReadOnlyList<ScreenRect> GetRangeBounds(int start, int length)
    {
        var view = TextView;
        var lines = ShownLines();
        if (lines.Count == 0) return [];
        var doc = _editor.Document;
        var from = doc.GetLocation(start);
        var to = doc.GetLocation(start + length);
        int firstShown = lines[0].FirstDocumentLine.LineNumber, lastShown = lines[^1].LastDocumentLine.LineNumber;
        var scroll = view.ScrollOffset;
        var rects = new List<ScreenRect>();
        for (int line = Math.Max(from.Line, firstShown); line <= Math.Min(to.Line, lastShown); line++)
        {
            int c0 = line == from.Line ? from.Column : 1;
            int c1 = line == to.Line ? to.Column : doc.GetLineByNumber(line).Length + 1;
            var a = view.GetVisualPosition(new TextViewPosition(line, c0), VisualYPosition.LineTop) - scroll;
            var b = view.GetVisualPosition(new TextViewPosition(line, c1), VisualYPosition.LineBottom) - scroll;
            // shortcut: a soft-wrapped line is reported as one box around its first and last visual row
            var box = new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
            rects.Add(ToScreen(view, box.Width < 1 ? box.WithWidth(1) : box));
        }
        return rects;
    }

    public override int OffsetFromPoint(double x, double y)
    {
        var view = TextView;
        if (TopLevel.GetTopLevel(view) is null) return -1;
        double unit = PixelsPerUnit;
        var local = view.PointToClient(new PixelPoint((int)Math.Round(x * unit), (int)Math.Round(y * unit)));
        if (!new Rect(view.Bounds.Size).Contains(local) || view.GetPosition(local) is not { } position) return -1;
        return _editor.Document.GetOffset(position.Location);
    }

    public override void ScrollIntoView(int start, int length, bool alignToTop)
    {
        var at = _editor.Document.GetLocation(start);
        _editor.ScrollTo(at.Line, at.Column);
    }

    public override void Dispose()
    {
        _editor.TextArea.Caret.PositionChanged -= OnEditorSelection;
        _editor.TextArea.SelectionChanged -= OnEditorSelection;
        _editor.TextArea.GotFocus -= OnEditorFocus;
        _editor.TextArea.LostFocus -= OnEditorFocus;
        Document.Changed -= OnDocumentEdited;
        base.Dispose();
    }
}

/// <summary>
/// The text area of <see cref="EditorSurface"/>. Keyboard focus sits on the text area, so UI Automation's focused element is the
/// element of this control; its peer is the native proxy the accessibility library publishes, which carries TextPattern.
/// </summary>
internal sealed class EditorTextArea : TextArea
{
    public EditorAutomationHost? Automation { get; set; }

    protected override Type StyleKeyOverride => typeof(TextArea);

    protected override AutomationPeer OnCreateAutomationPeer() => Automation?.CreateProxyPeer() ?? base.OnCreateAutomationPeer();
}

/// <summary>
/// Invisible native control next to the editor. On Windows it is a child window answering <c>WM_GETOBJECT</c> with the
/// editor's UIA element, on macOS an NSView with the NSAccessibility text methods; Avalonia puts the native control into
/// its own accessibility tree, so the element appears under the window together with the rest of the UI, which keeps
/// Avalonia's automation peers.
/// </summary>
internal sealed class EditorAutomationHost : NativeControlHost, IDisposable
{
    private readonly EditorSurface _editor;
    private UiaProxyWindow? _window;
    private nint _view;
    private EditorTextSurface? _surface;

    public EditorAutomationHost(EditorSurface editor)
    {
        _editor = editor;
        Width = 0;
        Height = 0;
        IsHitTestVisible = false;
        editor.Doc.Replaced += OnDocumentReplaced;
    }

    private string Label => Avalonia.Automation.AutomationProperties.GetName(_editor) ?? "Editor";

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (OperatingSystem.IsWindows())
        {
            if (_window is null)
            {
                _window = UiaProxyWindow.Create(parent.Handle, CreateBridge);
                _window.FocusRequested += () => _editor.TextArea.Focus();
            }
            _window.Reparent(parent.Handle);
            return new PlatformHandle(_window.Handle, "HWND");
        }
        if (OperatingSystem.IsMacOS())
        {
            if (_view == 0)
            {
                _view = MacAxBridge.CreateProxyView();
                MacAxBridge.Attach(_view, NewSurface, Label);
            }
            return new PlatformHandle(_view, "NSView");
        }
        return base.CreateNativeControlCore(parent);
    }

    // The native view outlives the attach/detach of a tab switch so the peer built from its handle stays valid.
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) base.DestroyNativeControlCore(control);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new NoneAutomationPeer(this);

    /// <summary>The automation peer of the native proxy, detached from the peer this control shows in the tree.</summary>
    internal AutomationPeer? CreateProxyPeer() => base.OnCreateAutomationPeer().GetChildren().FirstOrDefault();

    private EditorTextSurface NewSurface()
    {
        _surface?.Dispose();
        return _surface = new EditorTextSurface(_editor);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private WindowsUiaBridge CreateBridge(nint hwnd) => new(NewSurface(), hwnd, new UiaEditorInfo(Label, "textida-editor"));

    private void OnDocumentReplaced()
    {
        if (OperatingSystem.IsWindows()) _window?.ResetBridge();
        _surface?.Dispose();
        _surface = null;
        if (OperatingSystem.IsMacOS() && _view != 0) MacAxBridge.Attach(_view, NewSurface, Label);
    }

    public void Dispose()
    {
        _editor.Doc.Replaced -= OnDocumentReplaced;
        if (OperatingSystem.IsWindows()) _window?.Dispose();
        _window = null;
        if (OperatingSystem.IsMacOS() && _view != 0)
        {
            MacAxBridge.Detach(_view);
            ObjC.Msg(_view, "release");
        }
        _view = 0;
        _surface?.Dispose();
        _surface = null;
    }
}
