using System.Buffers;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// Text surface of a document in the paged backend. It reads only the lines on screen, so the size of the file does not matter,
/// and edits go straight to the <see cref="Document"/> (one revision and one undo step per edit or typing run): the caret is a byte
/// offset and each visible line is decoded with a character map between UTF-16 columns and bytes.
/// </summary>
/// <remarks>
/// shortcut: no selection, clipboard, inline IME preedit or block mode; a line longer than <see cref="MaxLineBytes"/> shows its first part
/// and cannot be edited; no horizontal scroll bar; saving blocks the UI thread. Product quality is the paged surface of P3.1.10.
/// </remarks>
internal sealed partial class PagedTextView : Grid
{
    public const int MaxLineBytes = 16 * 1024;
    private const int TextPadding = 4;

    private readonly OpenDocument _doc;
    private readonly LinesSurface _canvas;
    private readonly ScrollBar _bar = new() { Orientation = Orientation.Vertical, Minimum = 0, SmallChange = 1, AllowAutoHide = false };
    private readonly DispatcherTimer _indexTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Dictionary<long, LineView> _lines = [];
    private Dictionary<long, Token[]> _tokens = [];
    private EditorOptions _options;
    private long _linesRevision = -1;
    private long _caret;
    private (long Revision, long Caret, long Line) _lineMemo = (-1, -1, 0);
    private int _wanted = -1;
    private double _hScroll;
    private string? _typingKey;
    private bool _typingInsert;
    private int _run;
    private bool _own;
    private char _pendingHigh;
    private readonly InputClient _inputClient;

    public PagedTextView(OpenDocument doc, EditorOptions options)
    {
        _doc = doc;
        _options = options;
        _caret = doc.Doc.BomLength;
        Focusable = true;
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        _canvas = new LinesSurface(this);
        _inputClient = new InputClient(this);
        AddHandler(TextInputMethodClientRequestedEvent, (_, e) => e.Client = _inputClient);
        SetColumn(_bar, 1);
        Children.Add(_canvas);
        Children.Add(_bar);
        Syntax = new SyntaxFeeder(doc, ApplyTokens, ClearTokens,
            line => GetLine(line) is { Truncated: false } view ? view.Map.Text : null, () => Indexed ? LineCount : -1, FeedSyntax);
        _bar.ValueChanged += (_, _) => { FeedSyntax(); _canvas.InvalidateVisual(); RaiseViewChanged(); };
        _indexTimer.Tick += (_, _) =>
        {
            UpdateScrollRange();
            _canvas.InvalidateVisual();
            if (Doc.LineIndexState.Complete) _indexTimer.Stop();
            RaiseViewChanged();
        };
    }

    /// <summary>Raised when the first line on screen, the caret or the index state changed.</summary>
    public event Action? ViewChanged;
    private void RaiseViewChanged()
    {
        _inputClient.CaretChanged();
        ViewChanged?.Invoke();
        _mapChanged?.Invoke();
    }

    /// <summary>Raised with a localised sentence when an edit was refused.</summary>
    public event Action<string>? Refused;

    /// <summary>Keeps the syntax colors of this view current.</summary>
    public SyntaxFeeder Syntax { get; }

    private Document Doc => _doc.Doc;
    private PagedDocument Paged => ((PagedBackend)Doc.Backend).Paged;
    private bool Indexed => Doc.LineIndexState.Complete;
    private long LineCount => Doc.LineIndexState.Lines;

    /// <summary>Zero-based first line on screen.</summary>
    public long TopLine => (long)_bar.Value;
    public (long Lines, bool Complete) Index => Doc.LineIndexState;
    public long CaretOffset => _caret;

    /// <summary>When the last draw that showed a new revision, caret or first line ended (a <see cref="System.Diagnostics.Stopwatch"/> timestamp); the measurement mode reads it.</summary>
    internal long RenderedAt { get; private set; }
    /// <summary>Time the last draw that showed something new took on the UI thread.</summary>
    internal double RenderMs { get; private set; }
    private (long Revision, long Caret, long Top) _drawn = (-1, -1, -1);

    /// <summary>Zero-based line and UTF-16 column of the caret; null while the line index is not complete.</summary>
    public (long Line, long Column)? CaretLineColumn() =>
        CaretLine() is { } c ? (c.Line, c.View.Map.CharIndexOf(_caret - c.View.Start)) : null;

    public void ApplyOptions(EditorOptions o)
    {
        _options = o;
        _lines.Clear();
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        EditorAutomationHook.Provider?.CreatePeer(this, Doc) ?? base.OnCreateAutomationPeer();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Doc.Changed += OnDocChanged;
        _doc.Bookmarks.Changed += RepaintBookmarks;
        Doc.Saved += OnSaved;
        if (!Doc.LineIndexState.Complete) _indexTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Doc.Changed -= OnDocChanged;
        _doc.Bookmarks.Changed -= RepaintBookmarks;
        Doc.Saved -= OnSaved;
        _indexTimer.Stop();
    }

    private void OnDocChanged(object? sender, DocumentChangedEventArgs e)
    {
        long t = TypingTrace.Begin();
        if (!_own) _caret = Math.Clamp(_caret, Doc.BomLength, Doc.ByteLength);
        UpdateScrollRange();
        _canvas.InvalidateVisual();
        TypingTrace.End("paged.onDocChanged", t);
    }

    /// <summary>A save maps the new file again, so the line index starts over.</summary>
    private void OnSaved(object? sender, EventArgs e)
    {
        _lines.Clear();
        if (!Doc.LineIndexState.Complete) _indexTimer.Start();
    }

    public void GoToLine(long line)
    {
        _bar.Value = Math.Clamp(line - 1, 0, _bar.Maximum);
        long target = Math.Clamp(line - 1, 0, Math.Max(0, LineCount - 1));
        if (Indexed && LineStart(target) is var start and >= 0) MoveTo(start, target);
    }

    /// <summary>Token runs the view colors a line with, for tests; null when the line has none.</summary>
    internal IReadOnlyList<Token>? TokensOf(long line) => _tokens.GetValueOrDefault(line);

    /// <summary>
    /// Tells the feeder which lines and which UTF-16 columns of them are on screen. A line of at most 4096 characters is asked for whole;
    /// of a longer line only the characters between the left and the right edge of the view, found with the same measuring the caret uses.
    /// </summary>
    private void FeedSyntax()
    {
        if (!Syntax.Active) return;
        int first = int.MaxValue, last = 0;
        for (int i = 0; i <= _canvas.VisibleLines; i++)
        {
            if (!Indexed || GetLine(TopLine + i) is not { } view) break;
            int length = view.Map.Text.Length;
            if (length <= 4096)
            {
                first = 0;
                last = Math.Max(last, length);
                continue;
            }
            first = Math.Min(first, _canvas.IndexAtX(view, _hScroll));
            last = Math.Max(last, _canvas.IndexAtX(view, _hScroll + _canvas.TextWidth) + 1);
        }
        if (first == int.MaxValue) first = 0;
        Syntax.SetVisible(TopLine, TopLine + Math.Max(1, _canvas.VisibleLines), first, Math.Max(first + 1, last));
    }

    private void ApplyTokens(SyntaxViewport viewport)
    {
        if (viewport.Revision != Doc.Revision) return;
        _tokens = viewport.Lines.ToDictionary(line => line.Line, line => line.Tokens.ToArray());
        _canvas.InvalidateVisual();
    }

    private void ClearTokens()
    {
        _tokens.Clear();
        _canvas.InvalidateVisual();
    }

    private void UpdateScrollRange()
    {
        long lines = Math.Max(1, Index.Lines);
        int visible = Math.Max(1, _canvas.VisibleLines);
        _bar.Maximum = Math.Max(0, lines - visible);
        _bar.ViewportSize = visible;
        _bar.LargeChange = Math.Max(1, visible - 1);
        FeedSyntax();
        _mapChanged?.Invoke();
    }

    // ---- lines and the character map ----

    private long LineStart(long line) => line == 0 ? Doc.BomLength : Paged.TryGetLineStart(line, out long start) ? start : -1;

    /// <summary>Start, end of the text (before the line break) and start of the next line; false while the index is not complete.</summary>
    private bool TryBounds(long line, out long start, out long end, out long next)
    {
        long trace = TypingTrace.Begin();
        try { return TryBoundsCore(line, out start, out end, out next); }
        finally { TypingTrace.End("paged.tryBounds", trace); }
    }

    private bool TryBoundsCore(long line, out long start, out long end, out long next)
    {
        start = end = next = 0;
        long lines = LineCount;
        if (!Indexed || line < 0 || line >= lines || (start = LineStart(line)) < 0) return false;
        if (line + 1 >= lines)
        {
            end = next = Doc.ByteLength;
            return true;
        }
        if ((next = LineStart(line + 1)) < 0) return false;
        Span<byte> tail = stackalloc byte[2];
        int n = (int)Math.Min(2, next - start);
        Doc.Read(next - n, tail[..n]);
        end = next - (n == 2 && tail[0] == 13 && tail[1] == 10 ? 2 : 1);
        return true;
    }

    private LineView? GetLine(long line)
    {
        if (_linesRevision != Doc.Revision)
        {
            _lines.Clear();
            _linesRevision = Doc.Revision;
        }
        if (_lines.TryGetValue(line, out var cached)) return cached;
        if (_lines.Count > 400) _lines.Clear();
        FillFrom(line);
        if (_lines.TryGetValue(line, out cached)) return cached;
        if (!TryBounds(line, out long start, out long end, out _)) return null;
        return _lines[line] = Decode(start, end - start);
    }

    private LineView Decode(long start, long length)
    {
        int take = (int)Math.Min(length, MaxLineBytes);
        var bytes = new byte[take];
        Doc.Read(start, bytes);
        bool truncated = length > take;
        if (truncated) take = Doc.Codec.CompleteLength(bytes);
        return new LineView(start, truncated, Doc.Codec.WithBom(false).Decode(bytes.AsMemory(0, take)), _options.TabSize);
    }

    /// <summary>
    /// Loads the lines from <paramref name="first"/> on in one pass: one lookup of the first line start, one read, and the line breaks found in the bytes.
    /// Looking up every line start separately would scan a block of the line index for each of them.
    /// </summary>
    private void FillFrom(long first)
    {
        long t = TypingTrace.Begin();
        FillFromCore(first);
        TypingTrace.End("paged.fillLines", t);
    }

    private void FillFromCore(long first)
    {
        if (!Indexed || first < 0 || first >= LineCount) return;
        long origin = LineStart(first);
        if (origin < 0) return;
        int window = (int)Math.Min(Doc.ByteLength - origin, 128 * 1024);
        bool toEnd = origin + window >= Doc.ByteLength;
        var buffer = ArrayPool<byte>.Shared.Rent(window); // a fresh 128 KiB array per edit lands on the large object heap
        try { FillLines(first, origin, buffer, window, toEnd); }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private void FillLines(long first, long origin, byte[] buffer, int window, bool toEnd)
    {
        Doc.Read(origin, buffer.AsSpan(0, window));
        int limit = Math.Max(64, _canvas.VisibleLines + 8), pos = 0;
        for (long line = first; line < LineCount && line - first < limit; line++)
        {
            int end = pos;
            while (end < window && buffer[end] != 10 && buffer[end] != 13) end++;
            int next;
            if (end == window)
            {
                if (!toEnd) return; // the line is longer than the window: the caller decodes it on its own
                next = window;
            }
            else if (buffer[end] == 10) next = end + 1;
            else if (end + 1 < window) next = buffer[end + 1] == 10 ? end + 2 : end + 1;
            else if (toEnd) next = end + 1;
            else return; // a CR at the edge of the window may start a CRLF
            int length = end - pos;
            int take = Math.Min(length, MaxLineBytes);
            bool truncated = length > take;
            if (truncated) take = Doc.Codec.CompleteLength(buffer.AsSpan(pos, take));
            _lines[line] = new LineView(origin + pos, truncated, Doc.Codec.WithBom(false).Decode(buffer.AsSpan(pos, take).ToArray()), _options.TabSize);
            pos = next;
        }
    }

    private long LineOf(long offset)
    {
        long trace = TypingTrace.Begin();
        try { return LineOfCore(offset); }
        finally { TypingTrace.End("paged.lineOf", trace); }
    }

    private long LineOfCore(long offset)
    {
        long lo = 0, hi = LineCount - 1;
        while (lo < hi)
        {
            long mid = lo + (hi - lo + 1) / 2;
            if (LineStart(mid) <= offset) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    private (long Line, LineView View)? CaretLine()
    {
        if (!Indexed) return null;
        if (_lineMemo.Revision != Doc.Revision || _lineMemo.Caret != _caret) _lineMemo = (Doc.Revision, _caret, LineOf(_caret));
        return GetLine(_lineMemo.Line) is { } view ? (_lineMemo.Line, view) : null;
    }

    // ---- moving ----

    private void MoveTo(long caret, long? line = null, bool keepWanted = false)
    {
        _caret = caret;
        if (line is { } known) _lineMemo = (Doc.Revision, caret, known);
        if (!keepWanted) _wanted = -1;
        _typingKey = null;
        Reveal();
        RaiseViewChanged();
    }

    private void Left()
    {
        if (CaretLine() is not { } c) return;
        int i = c.View.Map.CharIndexOf(_caret - c.View.Start);
        if (i > 0) MoveTo(c.View.Start + c.View.Map.ByteOffsetOf(c.View.Map.IsCharBoundary(i - 1) ? i - 1 : i - 2), c.Line);
        else if (c.Line > 0 && TryBounds(c.Line - 1, out _, out long end, out _)) MoveTo(end, c.Line - 1);
    }

    private void Right()
    {
        if (CaretLine() is not { } c) return;
        var map = c.View.Map;
        int i = map.CharIndexOf(_caret - c.View.Start);
        if (i < map.Text.Length) MoveTo(c.View.Start + map.ByteOffsetOf(map.IsCharBoundary(i + 1) ? i + 1 : i + 2), c.Line);
        else if (!c.View.Truncated && c.Line + 1 < LineCount) MoveTo(LineStart(c.Line + 1), c.Line + 1);
    }

    private void Vertical(long delta)
    {
        if (CaretLine() is not { } c) return;
        long target = Math.Clamp(c.Line + delta, 0, LineCount - 1);
        if (target == c.Line || GetLine(target) is not { } view) return;
        if (_wanted < 0) _wanted = c.View.DisplayAt[c.View.Map.CharIndexOf(_caret - c.View.Start)];
        MoveTo(view.Start + view.Map.ByteOffsetOf(view.IndexAtDisplay(_wanted)), target, keepWanted: true);
    }

    private void LineEdge(bool end)
    {
        if (CaretLine() is not { } c) return;
        MoveTo(c.View.Start + c.View.Map.ByteOffsetOf(end ? c.View.Map.Text.Length : 0), c.Line);
    }

    /// <summary>Keeps the caret on screen: scrolls the lines, and sideways when the line is wider than the view.</summary>
    private void Reveal()
    {
        if (CaretLine() is not { } c) return;
        int visible = Math.Max(1, _canvas.VisibleLines - 1);
        if (c.Line < TopLine) _bar.Value = c.Line;
        else if (c.Line >= TopLine + visible) _bar.Value = Math.Min(_bar.Maximum, c.Line - visible + 1);
        double x = _canvas.XOf(c.View, c.View.Map.CharIndexOf(_caret - c.View.Start)), width = _canvas.TextWidth;
        if (x < _hScroll) _hScroll = Math.Max(0, x - 24);
        else if (x > _hScroll + width - 8) _hScroll = x - width + 32;
        FeedSyntax();
        _canvas.InvalidateVisual();
    }

    // ---- editing ----

    private bool CanEdit(out long line, out LineView view)
    {
        line = 0;
        view = null!;
        if (CaretLine() is not { } c) { Refused?.Invoke(Loc.Get(K.PagedIndexing)); return false; }
        if (c.View.Truncated) { Refused?.Invoke(Loc.Get(K.PagedLongLine, ("size", OpenDocument.FormatSize(MaxLineBytes)))); return false; }
        (line, view) = c;
        return true;
    }

    private bool Commit(long at, long deleted, byte[] insert, long caretAfter, long lineAfter, bool typing, bool isInsert)
    {
        string? key = null;
        if (typing)
        {
            if (_typingKey is null || _typingInsert != isInsert) _typingKey = "paged#" + ++_run;
            _typingInsert = isInsert;
            key = _typingKey;
        }
        else _typingKey = null;
        var options = new EditOptions
        {
            CoalesceKey = key,
            SelectionsBefore = [new ByteRange(_caret, _caret)],
            SelectionsAfter = [new ByteRange(caretAfter, caretAfter)],
            Source = "user",
        };
        // The handlers of Changed (status bar) read the caret while Apply runs; naming the new caret and its line first spares them a search through the line index.
        var (caretBefore, memoBefore) = (_caret, _lineMemo);
        (_caret, _lineMemo) = (caretAfter, (Doc.Revision + 1, caretAfter, lineAfter));
        _own = true;
        EditResult result = default;
        long tApply = TypingTrace.Begin();
        try { result = Doc.Apply([new ByteEdit(new ByteRange(at, at + deleted), insert)], options); }
        finally
        {
            _own = false;
            if (result is not { Applied: true }) (_caret, _lineMemo) = (caretBefore, memoBefore);
        }
        TypingTrace.End("paged.docApply", tApply);
        if (!result.Applied) return false;
        _lineMemo = (Doc.Revision, caretAfter, lineAfter);
        _wanted = -1;
        long t = TypingTrace.Begin();
        UpdateScrollRange();
        TypingTrace.End("paged.scrollRange", t);
        t = TypingTrace.Begin();
        Reveal();
        TypingTrace.End("paged.reveal", t);
        t = TypingTrace.Begin();
        RaiseViewChanged();
        TypingTrace.End("paged.viewChanged", t);
        return true;
    }

    /// <summary>Types text at the caret. A character the encoding cannot hold is refused, not replaced behind the user's back.</summary>
    public bool InsertText(string text, bool typing = true)
    {
        if (text.Length == 0 || !CanEdit(out long line, out _)) return false;
        byte[] bytes;
        try { bytes = Doc.Codec.Encode(text); }
        catch (UnrepresentableCharactersException)
        {
            Refused?.Invoke(Loc.Get(K.PagedNotEncodable, ("encoding", Doc.Codec.Name)));
            return false;
        }
        return Commit(_caret, 0, bytes, _caret + bytes.Length, line + LineBreaks(text), typing, isInsert: true);
    }

    private static int LineBreaks(string text)
    {
        int breaks = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n'))) breaks++;
        return breaks;
    }

    private void Backspace()
    {
        if (!CanEdit(out long line, out var view)) return;
        int i = view.Map.CharIndexOf(_caret - view.Start);
        if (i > 0)
        {
            long at = view.Start + view.Map.ByteOffsetOf(view.Map.IsCharBoundary(i - 1) ? i - 1 : i - 2);
            Commit(at, _caret - at, [], at, line, typing: true, isInsert: false);
        }
        else if (line > 0 && TryBounds(line - 1, out _, out long end, out long next)) Commit(end, next - end, [], end, line - 1, typing: false, isInsert: false);
    }

    private void DeleteForward()
    {
        if (!CanEdit(out long line, out var view)) return;
        var map = view.Map;
        int i = map.CharIndexOf(_caret - view.Start);
        if (i < map.Text.Length)
            Commit(_caret, view.Start + map.ByteOffsetOf(map.IsCharBoundary(i + 1) ? i + 1 : i + 2) - _caret, [], _caret, line, typing: true, isInsert: false);
        else if (line + 1 < LineCount && TryBounds(line, out _, out long end, out long next)) Commit(end, next - end, [], end, line, typing: false, isInsert: false);
    }

    private void Tab()
    {
        if (!CanEdit(out _, out var view)) return;
        int size = Math.Max(1, _options.InsertSpaces ? _options.IndentSize : _options.TabSize);
        int column = view.DisplayAt[view.Map.CharIndexOf(_caret - view.Start)];
        InsertText(_options.InsertSpaces ? new string(' ', size - column % size) : "\t");
    }

    /// <summary>Undo or redo through the document; the caret goes where the restored step left it.</summary>
    public void UndoRedo(bool redo)
    {
        var changes = new List<ChangeSet>();
        void Capture(object? s, DocumentChangedEventArgs e) => changes.Add(e.Change);
        Doc.Changed += Capture;
        UndoResult? result;
        _own = true;
        try { result = redo ? Doc.Redo() : Doc.Undo(); }
        finally { _own = false; Doc.Changed -= Capture; }
        if (result is null) return;
        long target = result.Selections.Count > 0 ? result.Selections[0].Start
            : changes.Count > 0 && changes[^1].Edits.Count > 0 ? changes[^1].Edits[0].Start + changes[^1].Edits[0].Inserted : _caret;
        UpdateScrollRange();
        MoveTo(Math.Clamp(target, Doc.BomLength, Doc.ByteLength));
    }

    // ---- input ----

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Text is not { Length: > 0 } text || text.Any(char.IsControl)) return;
        e.Handled = true;
        if (_pendingHigh != 0)
        {
            text = _pendingHigh + text;
            _pendingHigh = '\0';
        }
        if (text.Length == 1 && char.IsHighSurrogate(text[0]))
        {
            _pendingHigh = text[0];
            return;
        }
        long t = TypingTrace.Begin();
        InsertText(text);
        TypingTrace.End("paged.insertText", t);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _bar.Value = Math.Clamp(_bar.Value - e.Delta.Y * 3, 0, _bar.Maximum);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        base.OnPointerPressed(e);
        if (!Indexed) return;
        var point = e.GetPosition(_canvas);
        long line = Math.Clamp(TopLine + (long)Math.Floor(point.Y / _canvas.LineHeight), 0, LineCount - 1);
        if (GetLine(line) is not { } view) return;
        MoveTo(view.Start + view.Map.ByteOffsetOf(_canvas.IndexAtX(view, point.X - _canvas.TextLeft + _hScroll)), line);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Left: Left(); break;
            case Key.Right: Right(); break;
            case Key.Up: Vertical(-1); break;
            case Key.Down: Vertical(1); break;
            case Key.PageUp: Vertical(-Math.Max(1, _canvas.VisibleLines - 1)); break;
            case Key.PageDown: Vertical(Math.Max(1, _canvas.VisibleLines - 1)); break;
            case Key.Home when command: if (Indexed) MoveTo(Doc.BomLength, 0); break;
            case Key.End when command: if (Indexed) MoveTo(Doc.ByteLength, LineCount - 1); break;
            case Key.Home: LineEdge(end: false); break;
            case Key.End: LineEdge(end: true); break;
            case Key.Back when !command: Backspace(); break;
            case Key.Delete when !command: DeleteForward(); break;
            case Key.Enter when !command: InsertText(_doc.NewLine(_options.ConfiguredEol), typing: false); break;
            case Key.Tab when !command && !e.KeyModifiers.HasFlag(KeyModifiers.Shift): Tab(); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    // ---- input method ----

    /// <summary>
    /// What the system input method asks of the surface: where the caret is, so that its candidate window opens there, and the line around it.
    /// The composition is drawn by the system window (<see cref="SupportsPreedit"/> is false); the committed text arrives as ordinary text input.
    /// </summary>
    /// <remarks>shortcut: no inline preedit, no reconversion (the selection cannot be set); both belong to the surface of P3.</remarks>
    private sealed class InputClient(PagedTextView owner) : TextInputMethodClient
    {
        public override Visual TextViewVisual => owner._canvas;
        public override bool SupportsPreedit => false;
        public override bool SupportsSurroundingText => true;
        public override string SurroundingText => owner.CaretLine() is { } c ? c.View.Map.Text : "";
        public override Rect CursorRectangle => owner._canvas.CaretRect();

        public override Avalonia.Input.TextInput.TextSelection Selection
        {
            get
            {
                int i = owner.CaretLine() is { } c ? c.View.Map.CharIndexOf(owner._caret - c.View.Start) : 0;
                return new Avalonia.Input.TextInput.TextSelection(i, i);
            }
            set { }
        }

        public override void SetPreeditText(string? preeditText) { }

        public void CaretChanged()
        {
            RaiseCursorRectangleChanged();
            RaiseSurroundingTextChanged();
            RaiseSelectionChanged();
        }
    }

    // ---- drawing ----

    /// <summary>One decoded line: its text, the map between characters and bytes, and the text with tabs expanded for drawing.</summary>
    private sealed class LineView
    {
        public LineView(long start, bool truncated, DecodedText map, int tabSize)
        {
            Start = start;
            Truncated = truncated;
            Map = map;
            var text = map.Text;
            var display = new StringBuilder(text.Length);
            DisplayAt = new int[text.Length + 1];
            for (int i = 0; i < text.Length; i++)
            {
                DisplayAt[i] = display.Length;
                if (text[i] == '\t') display.Append(' ', Math.Max(1, tabSize) - display.Length % Math.Max(1, tabSize));
                else display.Append(text[i]);
            }
            DisplayAt[text.Length] = display.Length;
            Display = display.ToString();
        }

        public long Start { get; }
        /// <summary>The line is longer than <see cref="MaxLineBytes"/>; only its first part is here.</summary>
        public bool Truncated { get; }
        public DecodedText Map { get; }
        public string Display { get; }

        private string? _marked;
        /// <summary><see cref="Display"/> with a middle dot for every space and an arrow in the first column of every tab; the same length, so the colors of the tokens still fit.</summary>
        public string Marked => _marked ??= Mark();

        private string Mark()
        {
            var shown = Display.ToCharArray();
            var text = Map.Text;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == ' ') shown[DisplayAt[i]] = '·';
                else if (text[i] == '\t') shown[DisplayAt[i]] = '→';
            return new string(shown);
        }

        /// <summary>Column in <see cref="Display"/> of every character index (and one past the end).</summary>
        public int[] DisplayAt { get; }

        /// <summary>Last character boundary whose display column is at most <paramref name="column"/>.</summary>
        public int IndexAtDisplay(int column)
        {
            int i = 0;
            while (i < Map.Text.Length && DisplayAt[i + 1] <= column) i++;
            while (i > 0 && !Map.IsCharBoundary(i)) i--;
            return i;
        }
    }

    private sealed class LinesSurface(PagedTextView owner) : Control
    {
        private double _lineHeight = 18;
        private double _digitWidth = 8;
        private double _gutter = 40;

        public int VisibleLines => (int)Math.Ceiling(Math.Max(0, Bounds.Height) / _lineHeight);
        public double LineHeight => _lineHeight;
        public double TextLeft => _gutter + TextPadding;
        public double TextWidth => Math.Max(0, Bounds.Width - TextLeft);

        private FormattedText Format(string text, IBrush brush) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(owner._options.FullFontFamily)), owner._options.FontSize, brush);

        /// <summary>The caret in the coordinates of this surface; empty when its line is not on screen.</summary>
        public Rect CaretRect()
        {
            if (owner.CaretLine() is not { } c || c.Line < owner.TopLine || c.Line > owner.TopLine + VisibleLines) return default;
            double x = TextLeft - owner._hScroll + XOf(c.View, c.View.Map.CharIndexOf(owner._caret - c.View.Start));
            return new Rect(x, (c.Line - owner.TopLine) * _lineHeight, 1.5, _lineHeight);
        }

        public double XOf(LineView view, int index) =>
            view.DisplayAt[index] == 0 ? 0 : Format(view.Display[..view.DisplayAt[index]], Brushes.Black).WidthIncludingTrailingWhitespace;

        /// <summary>Character index (on a boundary) nearest to the horizontal position <paramref name="x"/> in the text.</summary>
        public int IndexAtX(LineView view, double x)
        {
            int lo = 0, hi = view.Map.Text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (XOf(view, mid) <= x) lo = mid; else hi = mid - 1;
            }
            if (lo < view.Map.Text.Length && XOf(view, lo + 1) - x < x - XOf(view, lo)) lo++;
            while (lo > 0 && !view.Map.IsCharBoundary(lo)) lo--;
            return lo;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var sample = Format("0", Brushes.Black);
            _lineHeight = Math.Ceiling(sample.Height);
            _digitWidth = sample.Width;
            return availableSize;
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            owner.UpdateScrollRange();
        }

        public override void Render(DrawingContext context)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Draw(context);
            long ended = System.Diagnostics.Stopwatch.GetTimestamp();
            TypingTrace.End("paged.render", started);
            var state = (owner.Doc.Revision, owner._caret, owner.TopLine);
            if (owner._drawn == state) return;
            owner._drawn = state;
            owner.RenderedAt = ended;
            owner.RenderMs = (ended - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        private void Draw(DrawingContext context)
        {
            IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;
            var back = Brush("BgEditor", Brushes.White);
            var fore = Brush("Fg", Brushes.Black);
            var numbers = Brush("LineNumbers", Brushes.Gray);
            var current = Brush("CurrentLine", Brushes.Transparent);
            var palette = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? SyntaxPalette.Dark : SyntaxPalette.Light;
            var options = owner._options;
            var dim = new ImmutableSolidColorBrush(fore is ISolidColorBrush solid ? solid.Color : Colors.Gray, 0.4);
            context.FillRectangle(back, new Rect(Bounds.Size));
            var (lines, _) = owner.Index;
            int digits = Math.Max(3, (lines > 0 ? lines : 1).ToString(CultureInfo.InvariantCulture).Length);
            _gutter = options.LineNumbers ? (digits + 1) * _digitWidth + 14 : owner._doc.Bookmarks.HasAny ? 14 : 0;
            long top = owner.TopLine;
            var caretLine = owner.CaretLine();
            using (context.PushClip(new Rect(_gutter, 0, Math.Max(0, Bounds.Width - _gutter), Bounds.Height)))
            {
                if (options.RightMargin > 0)
                {
                    double x = Math.Round(TextLeft - owner._hScroll + options.RightMargin * _digitWidth) + 0.5;
                    context.DrawLine(new Pen(new ImmutableSolidColorBrush(dim.Color, 0.5), 1), new Point(x, 0), new Point(x, Bounds.Height));
                }
                for (int i = 0; i < VisibleLines + 1; i++)
                {
                    long line = top + i;
                    if (owner.GetLine(line) is not { } view) break;
                    double y = i * _lineHeight;
                    if (options.CurrentLine && caretLine is { } cl && cl.Line == line) context.FillRectangle(current, new Rect(_gutter, y, Bounds.Width, _lineHeight));
                    string shown = options.ShowWhitespace ? view.Marked : view.Display;
                    var text = Format(shown, fore);
                    if (owner._tokens.TryGetValue(line, out var tokens))
                        foreach (var token in tokens)
                        {
                            int from = view.DisplayAt[Math.Min(token.Start, view.Map.Text.Length)], to = view.DisplayAt[Math.Min(token.End, view.Map.Text.Length)];
                            if (palette.GetBrush(token.Kind) is { } brush && to > from) text.SetForegroundBrush(brush, from, to - from);
                        }
                    if (options.ShowWhitespace)
                        for (int k = 0; k < shown.Length;)
                        {
                            if (shown[k] is not ('·' or '→')) { k++; continue; }
                            int end = k + 1;
                            while (end < shown.Length && shown[end] is '·' or '→') end++;
                            text.SetForegroundBrush(dim, k, end - k);
                            k = end;
                        }
                    context.DrawText(text, new Point(TextLeft - owner._hScroll, y));
                    if (options.ShowLineEndings && !view.Truncated && owner.LineEndingOf(line) is { Length: > 0 } ending)
                    {
                        var mark = Format(ending, dim);
                        mark.SetFontSize(options.FontSize * 0.7);
                        context.DrawText(mark, new Point(TextLeft - owner._hScroll + XOf(view, view.Map.Text.Length) + _digitWidth * 0.5, y + Math.Max(0, (_lineHeight - mark.Height) / 2)));
                    }
                }
                if (CaretRect() is { Height: > 0 } caretRect) context.FillRectangle(fore, caretRect);
            }
            for (int i = 0; options.LineNumbers && i < VisibleLines + 1; i++)
            {
                long line = top + i;
                if (line >= lines) break;
                var number = Format((line + 1).ToString(CultureInfo.InvariantCulture), numbers);
                context.DrawText(number, new Point(_gutter - 10 - number.Width, i * _lineHeight));
            }
            owner.DrawBookmarks(context, top, _lineHeight, VisibleLines + 1);
        }
    }
}
