using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Data;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The formatted (pretty) view of a JSON document (P3.1.14): every key, value and bracket on a line of its own, indented by depth, drawn from a
/// <see cref="JsonOutline"/> that reads only the lines on screen and scans the rest in the background. Nothing is reformatted in the file: the view
/// lies over the text view of the tab like the table view, and a key or value typed here replaces only its own bytes of the same document.
/// </summary>
internal sealed class JsonPrettyView : Grid, IDisposable
{
    private const string Indent = "  ", FoldMark = " … ";

    private readonly OpenDocument _od;
    private readonly JsonOutline? _outline;
    private readonly JsonFolds _folds = new();
    private readonly Surface _canvas;
    private readonly ScrollBar _vbar = new() { Orientation = Orientation.Vertical, Minimum = 0, SmallChange = 1, AllowAutoHide = false };
    private readonly ScrollBar _hbar = new() { Orientation = Orientation.Horizontal, Minimum = 0, SmallChange = 20, AllowAutoHide = false };
    private EditorOptions _options;
    private long _top, _caretLine;
    private int _caretColumn;
    private double _hScroll;
    private ByteRange? _mark;
    private bool _syncing, _refreshPosted;
    private CancellationTokenSource? _keySearch;

    public JsonPrettyView(OpenDocument doc, EditorOptions options)
    {
        HelpContext.Set(this, "json.pretty");
        _od = doc;
        _options = options;
        Focusable = true;
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        RowDefinitions = new RowDefinitions("*,Auto");
        _canvas = new Surface(this);
        SetColumn(_vbar, 1);
        SetRow(_hbar, 1);
        Children.Add(_canvas);
        Children.Add(_vbar);
        Children.Add(_hbar);
        _vbar.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            _top = (long)_vbar.Value;
            Refresh();
        };
        _hbar.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            _hScroll = _hbar.Value;
            _canvas.InvalidateVisual();
        };
        GotFocus += (_, _) => _canvas.InvalidateVisual();
        LostFocus += (_, _) => _canvas.InvalidateVisual();
        if (JsonOutline.Supports(doc.Doc.Codec))
        {
            _outline = new JsonOutline(doc.Doc);
            _outline.Progress += (_, _) => PostRefresh();
            _outline.Edited += OnEdited;
            _outline.Start();
        }
    }

    /// <summary>Raised when the caret, the first row or what the scan knows changed, for the status line.</summary>
    public event Action? ViewChanged;

    /// <summary>Raised with a localised sentence when an edit or a jump could not be done.</summary>
    public event Action<string>? Refused;

    public OpenDocument Document => _od;
    private Document Doc => _od.Doc;
    internal JsonOutline? Outline => _outline;
    internal JsonFolds Folds => _folds;
    public long CaretLine => _caretLine;
    public int CaretColumn => _caretColumn;
    public long TopRow => _top;
    public int VisibleRows => _canvas.VisibleRows;

    public void Dispose()
    {
        _keySearch?.Cancel();
        _outline?.Dispose();
    }

    public void ApplyOptions(EditorOptions options)
    {
        _options = options;
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
    }

    // ---- lines and rows ----

    /// <summary>Lines known so far: those the scan counted, or more when the segment after it was decoded for the screen.</summary>
    private long LineCount
    {
        get
        {
            if (_outline is null) return 0;
            var (lines, complete) = _outline.State;
            if (complete) return lines;
            return lines + _outline.GetLines(lines, JsonOutline.SegmentLines).Count;
        }
    }

    private long RowCount => Math.Max(0, LineCount - _folds.Hidden);

    private JsonLine? LineAt(long line) => line < 0 ? null : _outline?.GetLine(line);

    /// <summary>The rendered text of a line; a collapsed container shows its closing bracket after an ellipsis.</summary>
    internal JsonRenderedLine? Rendered(long line)
    {
        if (_outline is null || LineAt(line) is not { } l) return null;
        var rendered = _outline.Render(l, Indent);
        long close = _folds.CloseOf(line);
        if (close >= 0 && LineAt(close) is { } c)
        {
            string tail = FoldMark + (l.Frame?.IsObject == true ? "}" : "]") + (c.Comma ? "," : "");
            string text = rendered.Text.EndsWith('{') || rendered.Text.EndsWith('[') ? rendered.Text + tail : rendered.Text;
            rendered = rendered with { Text = text };
        }
        return rendered;
    }

    /// <summary>The text of a visible row (for tests and the reader); null past the known lines.</summary>
    internal string? RowText(long row) => Rendered(_folds.LineOfRow(row))?.Text;

    /// <summary>The length of the editable part of a line: the rendered text without the marks of a collapsed container.</summary>
    private int TextLength(long line) => _outline is not null && LineAt(line) is { } l ? _outline.Render(l, Indent).Text.Length : 0;

    // ---- scanning and edits ----

    private void PostRefresh()
    {
        if (_refreshPosted) return;
        _refreshPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshPosted = false;
            Refresh();
        }, DispatcherPriority.Background);
    }

    private void OnEdited(object? sender, JsonOutlineEdit e)
    {
        if (!e.Shifted) _folds.DropFrom(e.FirstLine);
        long last = Math.Max(0, LineCount - 1);
        _caretLine = Math.Min(_caretLine, last);
        _caretColumn = Math.Min(_caretColumn, TextLength(_caretLine));
        _mark = null;
        Refresh();
    }

    internal void Refresh()
    {
        UpdateScrollRange();
        _canvas.InvalidateVisual();
        ViewChanged?.Invoke();
    }

    internal void UpdateScrollRange()
    {
        _syncing = true;
        try
        {
            long rows = RowCount;
            _top = Math.Clamp(_top, 0, Math.Max(0, rows - 1));
            _vbar.Maximum = Math.Max(0, rows - 1);
            _vbar.ViewportSize = Math.Max(1, VisibleRows);
            _vbar.LargeChange = Math.Max(1, VisibleRows - 1);
            _vbar.Value = _top;
            _hbar.Maximum = Math.Max(0, _canvas.WidestLine - _canvas.TextWidth / 2);
            _hbar.ViewportSize = Math.Max(1, _canvas.TextWidth);
            _hbar.LargeChange = Math.Max(1, _canvas.TextWidth / 2);
            _hbar.Value = Math.Min(_hScroll, _hbar.Maximum);
        }
        finally
        {
            _syncing = false;
        }
    }

    // ---- caret ----

    /// <summary>The document offset the caret stands on: inside a key or value its byte, elsewhere the start of the key or value of the line.</summary>
    public long SourceOffset
    {
        get
        {
            if (_outline is null || LineAt(_caretLine) is not { } line) return Doc.BomLength;
            long at = _outline.SourceOffsetAt(_outline.Render(line, Indent), _caretColumn);
            return at >= 0 ? at : JsonOutline.SourceOffsetOf(line);
        }
    }

    public void MoveCaret(long line, int column, bool reveal = true)
    {
        long last = Math.Max(0, LineCount - 1);
        _caretLine = Math.Clamp(line, 0, last);
        long row = _folds.RowOfLine(_caretLine);
        _caretLine = _folds.LineOfRow(row);
        _caretColumn = Math.Clamp(column, 0, TextLength(_caretLine));
        if (reveal)
        {
            int visible = Math.Max(1, VisibleRows);
            if (row < _top) _top = row;
            else if (row >= _top + visible) _top = row - visible + 1;
            _canvas.RevealColumn();
        }
        Refresh();
    }

    private void MoveRows(long delta)
    {
        long row = Math.Clamp(_folds.RowOfLine(_caretLine) + delta, 0, Math.Max(0, RowCount - 1));
        MoveCaret(_folds.LineOfRow(row), _caretColumn);
    }

    /// <summary>Puts the caret on the line that shows the document offset, expanding the containers that hide it, and marks the range.</summary>
    public void RevealOffset(long start, long end)
    {
        if (_outline is null) return;
        long line = _outline.LineOfOffset(start);
        while (UnfoldAround(line)) { }
        int column = 0;
        if (LineAt(line) is { } l)
        {
            var rendered = _outline.Render(l, Indent);
            foreach (var span in rendered.Spans.Where(s => s.Kind != JsonTokenKind.Punctuation && s.Source <= start).TakeLast(1))
            {
                column = span.Column + span.Length;
                for (int c = span.Column; c <= span.Column + span.Length; c++)
                    if (_outline.SourceOffsetAt(rendered, c) >= start) { column = c; break; }
            }
        }
        _mark = end > start ? new ByteRange(start, end) : null;
        MoveCaret(line, column);
        _top = Math.Max(0, _folds.RowOfLine(_caretLine) - VisibleRows / 3);
        Refresh();
    }

    private bool UnfoldAround(long line)
    {
        long row = _folds.RowOfLine(line);
        long open = _folds.LineOfRow(row);
        return open != line && _folds.Unfold(open);
    }

    // ---- folds ----

    /// <summary>Collapses the container opened on the caret line (or the one the caret is in); false when there is none or its end is not scanned yet.</summary>
    public bool Fold()
    {
        if (_outline is null || LineAt(_caretLine) is not { } line) return false;
        long open = _caretLine;
        if (line.Kind != JsonLineKind.Open)
        {
            // the container the caret line belongs to: the nearest line above with a smaller depth
            int depth = line.Kind == JsonLineKind.Close ? line.Depth : line.Depth - 1;
            if (depth < 0) return false;
            open = -1;
            for (long n = _caretLine - 1; n >= 0 && n >= _caretLine - 100_000; n--)
                if (LineAt(n) is { } up && up.Depth == depth && up.Kind == JsonLineKind.Open) { open = n; break; }
            if (open < 0) return false;
        }
        if (_outline.FindClose(open) is not { } close)
        {
            Refused?.Invoke(Loc.Get(K.JsonPrettyNotScanned));
            return false;
        }
        _folds.Fold(open, close);
        MoveCaret(open, _caretColumn);
        return true;
    }

    public bool Unfold()
    {
        if (!_folds.Unfold(_caretLine)) return false;
        Refresh();
        return true;
    }

    public bool ToggleFold(long line)
    {
        if (_folds.Unfold(line)) { Refresh(); return true; }
        _caretLine = line;
        return Fold();
    }

    // ---- editing ----

    public void UndoRedo(bool redo)
    {
        if (redo ? Doc.RedoBlockedBySave : Doc.UndoBlockedBySave) { Refused?.Invoke(Loc.Get(K.HexBusy)); return; }
        _ = redo ? Doc.Redo() : Doc.Undo();
    }

    /// <summary>Types into the key or value under the caret; the bytes go into the document at the place the caret shows.</summary>
    public bool TypeText(string text)
    {
        if (!CanEdit(out var rendered)) return false;
        long at = _outline!.SourceOffsetAt(rendered!, _caretColumn);
        if (at < 0) return Refuse(K.JsonPrettyNotHere);
        byte[] bytes;
        try { bytes = Doc.Codec.Encode(text); }
        catch (UnrepresentableCharactersException) { return Refuse(K.HexNotEncodable, Doc.Codec.Name); }
        long line = _caretLine;
        int column = _caretColumn + text.Length;
        if (!Doc.Insert(at, bytes, new EditOptions { CoalesceKey = "json.pretty.typing", Source = "user" }).Applied) return Refuse(K.HexRejected);
        MoveCaret(line, column);
        return true;
    }

    /// <summary>Deletes the character before (Backspace) or after (Delete) the caret inside a key or value.</summary>
    public bool DeleteChar(bool forward)
    {
        if (!CanEdit(out var rendered)) return false;
        int from = forward ? _caretColumn : _caretColumn - 1, to = forward ? _caretColumn + 1 : _caretColumn;
        if (from >= 0 && from < rendered!.Text.Length && char.IsLowSurrogate(rendered.Text[from])) from--;
        if (to < rendered!.Text.Length && to > 0 && char.IsLowSurrogate(rendered.Text[to])) to++;
        if (from < 0 || rendered.SpanAt(from) is not { } span || to > span.Column + span.Length || from < span.Column) return Refuse(K.JsonPrettyNotHere);
        long a = _outline!.SourceOffsetAt(rendered, from), b = _outline.SourceOffsetAt(rendered, to);
        if (a < 0 || b <= a) return Refuse(K.JsonPrettyNotHere);
        long line = _caretLine;
        if (!Doc.Delete(new ByteRange(a, b), new EditOptions { CoalesceKey = "json.pretty.typing", Source = "user" }).Applied) return Refuse(K.HexRejected);
        MoveCaret(line, from);
        return true;
    }

    private bool CanEdit(out JsonRenderedLine? rendered)
    {
        rendered = null;
        if (_outline is null || LineAt(_caretLine) is not { } line) return false;
        if (_od.ReadOnly) return Refuse(K.HexReadOnly);
        if (Doc.IsSaving && !Doc.EditsDuringSave) return Refuse(K.HexBusy);
        rendered = _outline.Render(line, Indent);
        return true;
    }

    private bool Refuse(string key, string? encoding = null)
    {
        Refused?.Invoke(encoding is null ? Loc.Get(key) : Loc.Get(key, ("encoding", encoding)));
        return false;
    }

    // ---- keys ----

    /// <summary>Finds the next key named <paramref name="key"/> after the caret on a background thread and moves the caret there; false when there is none.</summary>
    public async Task<bool> GoToKeyAsync(string key)
    {
        if (_outline is null || key.Length == 0) return false;
        _keySearch?.Cancel();
        var cts = _keySearch = new CancellationTokenSource();
        long after = _caretLine;
        var outline = _outline;
        long found;
        try { found = await Task.Run(() => outline.FindKey(key, after, cts.Token), cts.Token); }
        catch (OperationCanceledException) { return false; }
        if (cts.IsCancellationRequested) return false;
        if (found < 0)
        {
            Refused?.Invoke(Loc.Get(K.JsonGoToKeyNotFound, ("key", key)));
            return false;
        }
        // the containers that hide the line open, as after a search
        while (UnfoldAround(found)) { }
        var rendered = LineAt(found) is { } l ? outline.Render(l, Indent) : null;
        MoveCaret(found, rendered?.Spans.FirstOrDefault(s => s.Kind == JsonTokenKind.Key).Column ?? 0);
        _top = Math.Max(0, _folds.RowOfLine(found) - VisibleRows / 3);
        Refresh();
        return true;
    }

    // ---- status ----

    /// <summary>The position for the status line: the line, the path to the value and, while scanning, how far the scan got or where the JSON is broken.</summary>
    public string StatusText()
    {
        if (_outline is null) return Loc.Get(K.JsonPrettyEncoding);
        string path = LineAt(_caretLine) is { } line ? _outline.PathOf(line) ?? "" : "";
        string text = Loc.Get(K.JsonPrettyStatus, ("line", (_caretLine + 1).ToString("N0", CultureInfo.CurrentCulture)), ("path", path));
        if (_outline.Error is { } error) text += " · " + Loc.Get(K.JsonPrettyInvalid, ("offset", error.Offset.ToString("N0", CultureInfo.CurrentCulture)));
        else if (!_outline.State.Complete && Doc.ByteLength > 0)
            text += " · " + Loc.Get(K.JsonPrettyScanning, ("percent", (int)(100.0 * _outline.ScannedBytes / Doc.ByteLength)));
        return text;
    }

    // ---- input ----

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Text is not { Length: > 0 } text || text.Any(char.IsControl)) return;
        e.Handled = true;
        TypeText(text);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        int page = Math.Max(1, VisibleRows - 1);
        switch (e.Key)
        {
            case Key.Up: MoveRows(-1); break;
            case Key.Down: MoveRows(1); break;
            case Key.PageUp: MoveRows(-page); break;
            case Key.PageDown: MoveRows(page); break;
            case Key.Home when command: MoveCaret(0, 0); break;
            case Key.End when command: MoveCaret(LineCount - 1, int.MaxValue); break;
            case Key.Home:
            {
                int indent = Rendered(_caretLine)?.Line.Depth * Indent.Length ?? 0;
                MoveCaret(_caretLine, _caretColumn == indent ? 0 : indent);
                break;
            }
            case Key.End: MoveCaret(_caretLine, int.MaxValue); break;
            case Key.Left when _caretColumn > 0: MoveCaret(_caretLine, _caretColumn - 1); break;
            case Key.Left: MoveRows(-1); MoveCaret(_caretLine, int.MaxValue); break;
            case Key.Right when _caretColumn < TextLength(_caretLine): MoveCaret(_caretLine, _caretColumn + 1); break;
            case Key.Right: MoveRows(1); MoveCaret(_caretLine, 0); break;
            case Key.OemOpenBrackets when command && shift: Fold(); break;
            case Key.OemCloseBrackets when command && shift: Unfold(); break;
            case Key.Back when !command: DeleteChar(forward: false); break;
            case Key.Delete when !command: DeleteChar(forward: true); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    private void Pressed(Point p, bool gutter)
    {
        Focus();
        long row = _top + (long)Math.Floor(Math.Max(0, p.Y) / _canvas.LineHeight);
        long line = _folds.LineOfRow(Math.Min(row, Math.Max(0, RowCount - 1)));
        if (gutter)
        {
            if (LineAt(line) is { Kind: JsonLineKind.Open }) ToggleFold(line);
            return;
        }
        MoveCaret(line, Rendered(line) is { } r ? _canvas.IndexAtX(r.Text, p.X - _canvas.TextLeft + _hScroll) : 0);
    }

    // ---- drawing ----

    private sealed class Surface(JsonPrettyView owner) : Control
    {
        private const double Pad = 6;
        private double _lineHeight = 18, _charWidth = 8, _gutter = 40;

        public int VisibleRows => Bounds.Height < 1 ? 30 : (int)Math.Ceiling(Bounds.Height / _lineHeight);
        public double LineHeight => _lineHeight;
        public double TextLeft => _gutter + Pad;
        public double TextWidth => Math.Max(0, Bounds.Width - TextLeft);
        public double WidestLine { get; private set; }

        private FormattedText Format(string text, IBrush brush) =>
            new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily(owner._options.FullFontFamily)), owner._options.FontSize, brush);

        private double XOf(string text, int index) => index <= 0 ? 0 : Format(text[..Math.Min(index, text.Length)], Brushes.Black).WidthIncludingTrailingWhitespace;

        public int IndexAtX(string text, double x)
        {
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (XOf(text, mid) <= x) lo = mid; else hi = mid - 1;
            }
            if (lo < text.Length && XOf(text, lo + 1) - x < x - XOf(text, lo)) lo++;
            return lo;
        }

        /// <summary>Scrolls sideways so the caret column is in view.</summary>
        public void RevealColumn()
        {
            if (owner.Rendered(owner._caretLine) is not { } r || TextWidth <= 0) return;
            double x = XOf(r.Text, owner._caretColumn);
            if (x < owner._hScroll) owner._hScroll = Math.Max(0, x - 4 * _charWidth);
            else if (x > owner._hScroll + TextWidth - _charWidth) owner._hScroll = x - TextWidth + 4 * _charWidth;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var sample = Format("0", Brushes.Black);
            _lineHeight = Math.Ceiling(sample.Height);
            _charWidth = sample.Width;
            return availableSize;
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            owner.UpdateScrollRange();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            owner.Pressed(p, p.X < _gutter);
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (e.Delta.Y != 0) owner._top = Math.Clamp(owner._top - (long)Math.Round(e.Delta.Y * 3), 0, Math.Max(0, owner.RowCount - 1));
            if (e.Delta.X != 0) owner._hScroll = Math.Max(0, owner._hScroll - e.Delta.X * 3 * _charWidth);
            owner.Refresh();
            e.Handled = true;
        }

        private static string? KindOf(JsonTokenKind kind) => kind switch
        {
            JsonTokenKind.Key => "property",
            JsonTokenKind.String => "string",
            JsonTokenKind.Number => "number",
            JsonTokenKind.Literal => "constant",
            _ => null,
        };

        public override void Render(DrawingContext context)
        {
            try { Draw(context); }
            catch (IOException) { }
        }

        private void Draw(DrawingContext context)
        {
            IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;
            var back = Brush("BgEditor", Brushes.White);
            var fore = Brush("Fg", Brushes.Black);
            var numbers = Brush("LineNumbers", Brushes.Gray);
            var current = Brush("CurrentLine", Brushes.Transparent);
            var selection = Brush("SelectionBg", Brushes.LightBlue);
            var accent = Brush("Accent", Brushes.Teal);
            var palette = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? SyntaxPalette.Dark : SyntaxPalette.Light;
            var dim = new ImmutableSolidColorBrush(fore is ISolidColorBrush solid ? solid.Color : Colors.Gray, 0.5);
            context.FillRectangle(back, new Rect(Bounds.Size));
            if (owner._outline is not { } outline)
            {
                context.DrawText(Format(Loc.Get(K.JsonPrettyEncoding), fore), new Point(Pad, Pad));
                return;
            }
            long lines = owner.LineCount;
            int digits = Math.Max(3, lines.ToString(CultureInfo.InvariantCulture).Length);
            _gutter = owner._options.LineNumbers ? (digits + 2) * _charWidth + Pad : 2 * _charWidth;
            double widest = 0;
            for (int i = 0; i < VisibleRows; i++)
            {
                long line = owner._folds.LineOfRow(owner._top + i);
                if (owner.Rendered(line) is not { } r) break;
                double y = i * _lineHeight;
                bool caretHere = line == owner._caretLine;
                if (caretHere && owner._options.CurrentLine) context.FillRectangle(current, new Rect(0, y, Bounds.Width, _lineHeight));
                if (owner._options.LineNumbers)
                {
                    var number = Format((line + 1).ToString(CultureInfo.InvariantCulture), caretHere ? accent : numbers);
                    context.DrawText(number, new Point(_gutter - 2 * _charWidth - number.Width, y));
                }
                if (r.Line.Kind == JsonLineKind.Open)
                    context.DrawText(Format(owner._folds.IsFolded(line) ? "▸" : "▾", numbers), new Point(_gutter - 1.5 * _charWidth, y));
                using (context.PushClip(new Rect(_gutter, y, Math.Max(0, Bounds.Width - _gutter), _lineHeight)))
                {
                    double left = TextLeft - owner._hScroll;
                    if (owner._mark is { } mark)
                        foreach (var span in r.Spans)
                        {
                            if (span.Kind == JsonTokenKind.Punctuation) continue;
                            int a = -1, b = -1;
                            for (int c = span.Column; c <= span.Column + span.Length; c++)
                            {
                                long at = outline.SourceOffsetAt(r, c);
                                if (a < 0 && at >= mark.Start) a = c;
                                if (at <= mark.End) b = c;
                            }
                            if (a >= 0 && b > a) context.FillRectangle(selection, new Rect(left + XOf(r.Text, a), y, XOf(r.Text, b) - XOf(r.Text, a), _lineHeight));
                        }
                    var text = Format(r.Text, r.Line.Kind == JsonLineKind.Raw ? dim : fore);
                    foreach (var span in r.Spans)
                        if (KindOf(span.Kind) is { } kind && palette.GetBrush(kind) is { } brush && span.Length > 0) text.SetForegroundBrush(brush, span.Column, span.Length);
                    context.DrawText(text, new Point(left, y));
                    widest = Math.Max(widest, text.WidthIncludingTrailingWhitespace);
                    if (caretHere && owner.IsFocused) context.FillRectangle(fore, new Rect(left + XOf(r.Text, owner._caretColumn), y, 1.5, _lineHeight));
                }
            }
            if (Math.Abs(widest - WidestLine) > 1)
            {
                WidestLine = widest;
                Dispatcher.UIThread.Post(owner.UpdateScrollRange, DispatcherPriority.Background);
            }
            if (outline.Error is { } error && Bounds.Height > 2 * _lineHeight)
            {
                var note = Format(Loc.Get(K.JsonPrettyInvalid, ("offset", error.Offset.ToString("N0", CultureInfo.CurrentCulture))), fore);
                var box = new Rect(Bounds.Width - note.Width - 3 * Pad, Bounds.Height - _lineHeight - 2 * Pad, note.Width + 2 * Pad, _lineHeight + Pad);
                context.FillRectangle(Brush("AccentSoft", Brushes.LightYellow), box);
                context.DrawText(note, new Point(box.X + Pad, box.Y + Pad / 2));
            }
        }
    }
}
