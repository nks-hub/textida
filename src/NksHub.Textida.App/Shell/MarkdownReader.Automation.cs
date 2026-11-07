using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.Accessibility;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The reader for screen readers (P9.3.4): the text of the page as an <see cref="ITextAutomationSurface"/>, published by the same native bridges as the editor (UIA TextPattern on Windows,
/// the NSAccessibility text methods on macOS), so a screen reader reads it by character, word and line and follows the selection and the caret.
/// </summary>
internal sealed partial class MarkdownReader : IDisposable
{
    private EditorAutomationHost? _automation;

    /// <summary>The label of the page for a screen reader: its own name (the link Tab is on), else the name of a preview body.</summary>
    internal string AutomationLabel => AutomationProperties.GetName(this) is { Length: > 0 } name ? name : Loc.Get(K.A11yPreviewBody);

    /// <summary>
    /// Creates the invisible native control that publishes the text to the platform screen reader and makes it a child of the page; the owner of the page disposes the page when it drops it.
    /// </summary>
    internal void CreateAutomationHost()
    {
        if (_automation is not null) return;
        _automation = new EditorAutomationHost(null, () => new TextSurface(this), () => Focus(), () => AutomationLabel);
        LogicalChildren.Add(_automation);
        VisualChildren.Add(_automation);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => _automation?.CreateProxyPeer() ?? new MarkdownReaderAutomationPeer(this);

    public void Dispose()
    {
        if (_automation is null) return;
        VisualChildren.Remove(_automation);
        LogicalChildren.Remove(_automation);
        _automation.Dispose();
        _automation = null;
    }

    /// <summary>
    /// The text of the page as the screen reader sees it: <see cref="MarkdownReaderDocument.PlainText"/>, the blocks one per line, so its offsets are the positions of the selection.
    /// A line is a block (a paragraph wrapped on screen is one line, as a soft-wrapped line of the editor is); words are found in it by <see cref="TextUnits"/>. Read-only.
    /// </summary>
    internal sealed class TextSurface : ITextAutomationSurface, IDisposable
    {
        private readonly MarkdownReader _owner;
        private MarkdownReaderDocument _doc = null!;
        private int[] _starts = [0, 0];
        private long _revision;
        private (int, int, int) _seen;

        public TextSurface(MarkdownReader owner)
        {
            _owner = owner;
            Load();
            _seen = SelectionKey();
            owner.ViewChanged += OnViewChanged;
            owner.GotFocus += OnFocus;
            owner.LostFocus += OnFocus;
        }

        private void Load()
        {
            _doc = _owner._doc;
            string text = _doc.PlainText;
            var starts = new List<int> { 0 };
            for (int i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1)) starts.Add(i + 1);
            starts.Add(text.Length);
            _starts = [.. starts];
        }

        /// <summary>The page shows another rendering: the text is read again.</summary>
        private bool Ensure()
        {
            if (ReferenceEquals(_doc, _owner._doc)) return false;
            Load();
            _revision++;
            return true;
        }

        public long Revision { get { Ensure(); return _revision; } }
        public int Length { get { Ensure(); return _doc.Length; } }
        public int LineCount { get { Ensure(); return _starts.Length - 1; } }
        public bool IsReadOnly => true;
        public bool HasFocus => _owner.IsFocused;
        public void Focus() => _owner.Focus();

        public event Action<TextChange>? TextChanged;
        public event Action? SelectionChanged;
        public event Action? FocusChanged;

        public string GetText(int start, int length)
        {
            Ensure();
            if (start < 0 || length < 0 || start + length > _doc.Length) throw new ArgumentOutOfRangeException(nameof(start));
            return _doc.PlainText.Substring(start, length);
        }

        public int LineStart(int line)
        {
            Ensure();
            if (line < 0 || line >= _starts.Length) throw new ArgumentOutOfRangeException(nameof(line));
            return _starts[line];
        }

        public int LineOfOffset(int offset)
        {
            Ensure();
            offset = Math.Clamp(offset, 0, _doc.Length);
            int i = Array.BinarySearch(_starts, 0, _starts.Length - 1, offset);
            return i >= 0 ? i : ~i - 1;
        }

        public TextSpan Selection
        {
            get
            {
                var (start, end) = _owner.SelectionRange;
                return new(start, end - start);
            }
        }

        public int CaretOffset => Math.Clamp(_owner._caret, 0, _owner._doc.Length);

        public void SetSelection(int start, int length)
        {
            start = Math.Clamp(start, 0, _owner._doc.Length);
            _owner.Select(start, Math.Clamp(start + length, start, _owner._doc.Length));
        }

        private (int, int, int) SelectionKey() => (Selection.Start, Selection.Length, CaretOffset);

        // ---- geometry: only what is in view is drawn ----

        public ScreenRect ScreenBounds => AutomationScreen.ToScreen(_owner, _owner.View);

        public TextSpan VisibleRange
        {
            get
            {
                Ensure();
                if (_doc.Blocks.Count == 0) return default;
                var view = _owner.View;
                var first = _doc.Blocks[_owner.BlockAtY(view.Top)];
                var last = _doc.Blocks[_owner.BlockAtY(view.Bottom)];
                return new(first.DocStart, last.DocStart + last.Text.Length - first.DocStart);
            }
        }

        public IReadOnlyList<ScreenRect> GetRangeBounds(int start, int length)
        {
            Ensure();
            var rects = new List<ScreenRect>();
            if (_doc.Blocks.Count == 0) return rects;
            var view = _owner.View;
            if (length == 0)
            {
                if (_owner.CaretBounds(start) is { } caret && caret.Intersects(view)) rects.Add(AutomationScreen.ToScreen(_owner, caret));
                return rects;
            }
            int end = start + length;
            for (int i = _doc.BlockAt(start); i < _doc.Blocks.Count && _doc.Blocks[i].DocStart < end; i++)
            {
                var b = _doc.Blocks[i];
                int from = Math.Clamp(start - b.DocStart, 0, b.Text.Length), to = Math.Clamp(end - b.DocStart, 0, b.Text.Length);
                foreach (var r in _owner.RangeBounds(i, from, to))
                    if (r.Translate(new Vector(-_owner.ScrollOf(b), 0)) is var shown && shown.Intersects(view)) rects.Add(AutomationScreen.ToScreen(_owner, shown));
            }
            return rects;
        }

        public int OffsetFromPoint(double x, double y)
        {
            if (TopLevel.GetTopLevel(_owner) is null) return -1;
            double unit = AutomationScreen.PixelsPerUnit(_owner);
            var local = _owner.PointToClient(new PixelPoint((int)Math.Round(x * unit), (int)Math.Round(y * unit)));
            return _owner.View.Contains(local) ? _owner.PositionAt(local) : -1;
        }

        public void ScrollIntoView(int start, int length, bool alignToTop)
        {
            Ensure();
            if (_doc.Blocks.Count == 0 || _owner.FindAncestorOfType<ScrollViewer>() is not { } scroller) return;
            var bounds = _owner.BlockBounds(_doc.BlockAt(start));
            var view = _owner.View;
            if (alignToTop || bounds.Top < view.Top || bounds.Top > view.Bottom - 20)
                scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, alignToTop ? bounds.Top : bounds.Top - scroller.Viewport.Height / 3));
        }

        public void InvokeOnUiThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Invoke(action);
        }

        // ---- events ----

        private void OnFocus(object? sender, RoutedEventArgs e) => FocusChanged?.Invoke();

        private void OnViewChanged()
        {
            int before = _doc.Length;
            if (Ensure()) TextChanged?.Invoke(new TextChange(0, before, _doc.Length, _revision));
            var key = SelectionKey();
            if (key == _seen) return;
            _seen = key;
            SelectionChanged?.Invoke();
        }

        public void Dispose()
        {
            _owner.ViewChanged -= OnViewChanged;
            _owner.GotFocus -= OnFocus;
            _owner.LostFocus -= OnFocus;
        }
    }
}

/// <summary>
/// The reader in Avalonia's automation tree, where the native bridge of <see cref="MarkdownReader.CreateAutomationHost"/> does not stand in for it (no host, Linux, before the native control exists):
/// a document named like its host, whose value is the selection or the text in view; a link reached with Tab gives the page its name. <see cref="Text"/> is the same text surface the bridges publish.
/// </summary>
internal sealed class MarkdownReaderAutomationPeer(MarkdownReader owner) : ControlAutomationPeer(owner), IValueProvider
{
    private MarkdownReader.TextSurface? _text;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(MarkdownReader);

    protected override string? GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : Loc.Get(K.A11yPreviewBody);

    public bool IsReadOnly => true;

    public string? Value => owner.VisibleText();

    public void SetValue(string? value) { }

    /// <summary>The text of the page with its selection, caret and geometry, for reading by character, word and line.</summary>
    public ITextAutomationSurface Text => _text ??= new MarkdownReader.TextSurface(owner);
}
