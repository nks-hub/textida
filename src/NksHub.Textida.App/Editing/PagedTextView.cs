using System.Buffers;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
/// shortcut: one selection only (no extra carets, no block mode); a line longer than <see cref="MaxLineBytes"/> shows its first part
/// and is edited through the bytes around the caret (the caret column and the selection come from the column index of the document, never from decoding the line); its columns beyond that part are shown through a bar that counts fixed cells (the line length in characters bounds the range once a read has reached the end of the line, its bytes before; no tokens there, a selection only when it covers the rest of the line); saving blocks the UI thread. Product quality is the paged surface of P3.1.10.
/// </remarks>
internal sealed partial class PagedTextView : Grid
{
    public const int MaxLineBytes = 16 * 1024;
    private const int TextPadding = 4;
    /// <summary>Cells the sideways bar scrolls past the end of a long line while line endings are shown, so the CRLF mark fits on screen.</summary>
    private const int LineEndMarkColumns = 4;

    private readonly OpenDocument _doc;
    private readonly LinesSurface _canvas;
    private readonly ScrollBar _bar = new() { Orientation = Orientation.Vertical, Minimum = 0, SmallChange = 1, AllowAutoHide = false };
    private readonly DispatcherTimer _indexTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Dictionary<long, LineView> _lines = [];
    private Dictionary<long, Token[]> _tokens = [];
    private EditorOptions _options;
    private long _linesRevision = -1;
    private long _caret;
    private long _anchor = -1;
    private long _dragFrom = -1;
    private bool _extend;
    private (long Revision, long Caret, long Line) _lineMemo = (-1, -1, 0);
    private int _wanted = -1;
    private double _hScroll;
    private readonly ScrollBar _hBar = new() { Orientation = Orientation.Horizontal, Minimum = 0, SmallChange = 1, AllowAutoHide = false, IsVisible = false };
    private long _truncatedColumns;
    private readonly Dictionary<long, (long Revision, long Column, int Columns, string Text)> _windows = [];
    private readonly HashSet<long> _indexing = [];
    private readonly Dictionary<long, (CancellationTokenSource Cts, long Revision, long Column, int Columns)> _pending = [];
    private CancellationTokenSource? _indexCts;
    private string? _typingKey;
    private bool _typingInsert;
    private int _run;
    private bool _own;
    private char _pendingHigh;
    private readonly InputClient _inputClient;
    private Detour? _detour;
    private string _preedit = "";
    private PagedDocument? _watched;

    public PagedTextView(OpenDocument doc, EditorOptions options)
    {
        _doc = doc;
        _options = options;
        EditorFontFallbacks.Shared.Use(options.FullFontFamily);
        _caret = doc.Doc.BomLength;
        Focusable = true;
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        RowDefinitions = new RowDefinitions("*,Auto");
        _canvas = new LinesSurface(this);
        _inputClient = new InputClient(this);
        AddHandler(TextInputMethodClientRequestedEvent, (_, e) => e.Client = _inputClient);
        SetColumn(_bar, 1);
        SetRow(_hBar, 1);
        Children.Add(_canvas);
        Children.Add(_bar);
        Children.Add(_hBar);
        _hBar.ValueChanged += (_, _) =>
        {
            _hScroll = _hBar.Value * _canvas.CharWidth;
            _canvas.InvalidateVisual();
        };
        Syntax = new SyntaxFeeder(doc, ApplyTokens, ClearTokens,
            line => GetLine(line) is { Truncated: false } view ? view.Map.Text : null, () => Indexed ? LineCount : -1, FeedSyntax);
        _bar.ValueChanged += (_, _) => { FeedSyntax(); _canvas.InvalidateVisual(); RaiseViewChanged(); };
        _indexTimer.Tick += (_, _) =>
        {
            LeaveDetourWhenIndexed();
            UpdateScrollRange();
            _canvas.InvalidateVisual();
            if (Doc.LineIndexState.Complete) _indexTimer.Stop();
            RaiseViewChanged();
        };
    }

    private void RepaintLog() => _canvas.InvalidateVisual();

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
    /// <summary>Raised with the text that Copy or Cut put on the clipboard.</summary>
    public event Action<string>? Copied;

    /// <summary>Keeps the syntax colors of this view current.</summary>
    public SyntaxFeeder Syntax { get; }

    private Document Doc => _doc.Doc;
    private PagedDocument Paged => ((PagedBackend)Doc.Backend).Paged;
    private bool Indexed => Doc.LineIndexState.Complete;
    /// <summary>Lines of the document; while the index is being built the lines counted so far, in a detour the estimate.</summary>
    private long LineCount => _detour is { } d ? Math.Max(EstimatedLines(d), d.BaseLine + d.Starts.Count) : Doc.LineIndexState.Lines;
    /// <summary>Lines whose start and end are known: the last line of a partial index is still open.</summary>
    private long ShownLines => LineCount - (Indexed || _detour is not null ? 0 : 1);
    /// <summary>False while the line numbers are estimates (after a jump beyond the indexed part); they are marked with "≈".</summary>
    public bool LinesExact => _detour is null;
    private bool CanLocate(long offset) => _detour is not null || Indexed || offset < Paged.IndexedBytes;

    /// <summary>Zero-based first line on screen.</summary>
    public long TopLine => (long)_bar.Value;
    public (long Lines, bool Complete) Index => Doc.LineIndexState;
    public long CaretOffset => _caret;

    /// <summary>True while a range of bytes is selected.</summary>
    public bool HasSelection => _anchor >= 0 && _anchor != _caret;

    /// <summary>The selected bytes of the current revision, whichever way the selection was made; empty at the caret.</summary>
    public ByteRange SelectionRange => HasSelection ? new ByteRange(Math.Min(_anchor, _caret), Math.Max(_anchor, _caret)) : new ByteRange(_caret, _caret);

    /// <summary>Lines a Page Up or Page Down moves.</summary>
    internal int PageLines => Math.Max(1, _canvas.VisibleLines - 1);

    /// <summary>The selected text exactly as the bytes of the current revision decode, line breaks included.</summary>
    public string SelectedText() => HasSelection ? Doc.Codec.WithBom(false).Decode(Doc.ReadBytes(SelectionRange)).Text : "";

    /// <summary>When the last draw that showed a new revision, caret or first line ended (a <see cref="System.Diagnostics.Stopwatch"/> timestamp); the measurement mode reads it.</summary>
    internal long RenderedAt { get; private set; }
    /// <summary>Time the last draw that showed something new took on the UI thread.</summary>
    internal double RenderMs { get; private set; }
    private (long Revision, long Caret, long Top, double Side, bool Pending) _drawn = (-1, -1, -1, -1, false);

    /// <summary>True while a horizontal window of a long line is being read off the UI thread; the draw shows a placeholder for it.</summary>
    internal bool WindowsPending => _pending.Count > 0;

    /// <summary>Zero-based line and UTF-16 column of the caret; null while the line index is not complete.</summary>
    public (long Line, long Column)? CaretLineColumn() =>
        CaretSite() is { } c ? (c.Line, c.Column) : null;

    public void ApplyOptions(EditorOptions o)
    {
        _options = o;
        EditorFontFallbacks.Shared.Use(o.FullFontFamily);
        _lines.Clear();
        _truncatedColumns = -1; // the next draw sets the sideways range again: the cell width or the room for the line end mark may have changed
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
    }

    /// <summary>Creates the invisible native control that publishes this surface to UI Automation; the caller puts it into the visual tree next to the view.</summary>
    public EditorAutomationHost CreateAutomationHost() => Automation = new EditorAutomationHost(
        _doc, () => new TextSurface(this), () => Focus(), () => Avalonia.Automation.AutomationProperties.GetName(this) ?? "Editor");

    private EditorAutomationHost? Automation { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        Automation?.CreateProxyPeer() ?? EditorAutomationHook.Provider?.CreatePeer(this, Doc) ?? base.OnCreateAutomationPeer();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Doc.Changed += OnDocChanged;
        _doc.Edited += KeepUntouchedLines;
        _doc.Bookmarks.Changed += RepaintBookmarks;
        Doc.Saved += OnSaved;
        _doc.LanguageChanged += RepaintLog;
        _doc.LogLevelsChanged += RepaintLog;
        _doc.LogHighlightChanged += RepaintLog;
        WatchIndex();
        if (!Doc.LineIndexState.Complete) _indexTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Doc.Changed -= OnDocChanged;
        _doc.Edited -= KeepUntouchedLines;
        _doc.Bookmarks.Changed -= RepaintBookmarks;
        Doc.Saved -= OnSaved;
        _doc.LanguageChanged -= RepaintLog;
        _doc.LogLevelsChanged -= RepaintLog;
        _doc.LogHighlightChanged -= RepaintLog;
        if (_watched is not null) _watched.IndexProgress -= OnIndexProgress;
        _watched = null;
        _indexTimer.Stop();
        _indexCts?.Cancel();
        _indexCts = null;
        _indexing.Clear();
        CancelWindows();
    }

    private void OnDocChanged(object? sender, DocumentChangedEventArgs e)
    {
        long t = TypingTrace.Begin();
        _preedit = "";
        if (!_own)
        {
            _caret = Math.Clamp(_caret, Doc.BomLength, Doc.ByteLength);
            _anchor = -1;
        }
        UpdateScrollRange();
        _canvas.InvalidateVisual();
        TypingTrace.End("paged.onDocChanged", t);
    }

    /// <summary>True when a line ends a few bytes (room for its line break) before the first changed byte.</summary>
    internal static bool LineSurvives(long start, long byteLength, long firstChange) => start + byteLength + 4 <= firstChange;

    /// <summary>
    /// Lines that end well before the first changed byte keep their text and map. When one edit lies inside a line and adds or removes no line break,
    /// the lines behind it keep their text too and only move, so typing decodes again just the edited line.
    /// Anything else (a different kind of change, a revision that was skipped) leaves the clearing to <see cref="GetLine"/>.
    /// </summary>
    private void KeepUntouchedLines(ChangeSet change)
    {
        if (change.Kind is not (ChangeKind.Edit or ChangeKind.Undo or ChangeKind.Redo) || change.Edits.Count == 0 || _linesRevision != change.Revision - 1) return;
        var edit = change.Edits[0];
        long first = edit.Start, editEnd = edit.Start + edit.Deleted;
        bool insideLine = change.Edits.Count == 1 && !change.TouchesLineBreaks && _lines.Values.Any(v => v.Start <= first && editEnd <= v.Start + v.ByteLength);
        foreach (var (line, view) in _lines.ToArray())
        {
            if (LineSurvives(view.Start, view.ByteLength, first)) continue;
            if (LongLineAfterEdit(view, change) is { } resized) _lines[line] = resized;
            else if (insideLine && view.Start > editEnd) _lines[line] = view.WithStart(view.Start + edit.Inserted - edit.Deleted);
            else _lines.Remove(line);
        }
        _linesRevision = change.Revision;
    }

    /// <summary>
    /// The view of a truncated line whose one edit lies behind the part that was decoded and stays inside the line without a line break:
    /// the decoded part is still right and only the length of the line changed. Null for any other edit.
    /// </summary>
    private static LineView? LongLineAfterEdit(LineView view, ChangeSet change)
    {
        if (!view.Truncated || change.TouchesLineBreaks || change.Edits.Count != 1) return null;
        var edit = change.Edits[0];
        if (edit.Start < view.Start + MaxLineBytes + 4 || edit.Start + edit.Deleted > view.Start + view.ByteLength) return null;
        return view.WithByteLength(view.ByteLength - edit.Deleted + edit.Inserted);
    }

    /// <summary>A save maps the new file again, so the line index starts over.</summary>
    private void OnSaved(object? sender, EventArgs e)
    {
        _lines.Clear();
        CancelWindows();
        _detour = null;
        WatchIndex();
        if (!Doc.LineIndexState.Complete) _indexTimer.Start();
    }

    /// <summary>
    /// Goes to a one-based line. While the index is being built a line inside the indexed part is exact; a later one cannot be found yet and
    /// the call returns false. With a complete index a number past the end goes to the last line.
    /// </summary>
    /// <summary>The index reports from its own thread; the view catches up on the UI thread, so a detour ends as soon as the index reaches it.</summary>
    private void WatchIndex()
    {
        if (ReferenceEquals(_watched, Paged)) return;
        if (_watched is not null) _watched.IndexProgress -= OnIndexProgress;
        _watched = Paged;
        _watched.IndexProgress += OnIndexProgress;
    }

    private void OnIndexProgress(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_watched is null) return; // the view was closed while the notice was on its way
        if (_detour is null && Doc.LineIndexState.Complete) return;
        LeaveDetourWhenIndexed();
        UpdateScrollRange();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    });

    public bool GoToLine(long line)
    {
        long target = Math.Max(0, line - 1);
        if (Indexed) target = Math.Min(target, Math.Max(0, LineCount - 1));
        long start = target == 0 ? Doc.BomLength : Paged.TryGetLineStart(target, out long s) ? s : -1;
        if (start < 0) return false;
        LeaveDetour(target);
        UpdateScrollRange();
        _bar.Value = Math.Clamp(target, 0, _bar.Maximum);
        MoveTo(start, target);
        return true;
    }

    /// <summary>
    /// Puts the caret on a byte offset (on a character boundary) and shows its line. Inside the indexed part the line number is exact; beyond it
    /// the view continues from the start of that line and numbers the lines by estimate until the index catches up.
    /// </summary>
    public void GoToOffset(long offset)
    {
        offset = Math.Clamp(offset, Doc.BomLength, Doc.ByteLength);
        if (!Indexed && offset >= Paged.IndexedBytes)
        {
            EnterDetour(offset);
            return;
        }
        _detour = null;
        _lines.Clear();
        _lineMemo = (-1, -1, 0);
        long line = LineOf(offset);
        UpdateScrollRange();
        _bar.Value = Math.Clamp(line, 0, _bar.Maximum);
        MoveTo(offset, line);
    }

    /// <summary>A view that starts at a byte offset beyond the indexed part: its lines are found by scanning forward from there.</summary>
    private sealed class Detour(long baseStart, long baseLine, double bytesPerLine)
    {
        public long BaseStart { get; } = baseStart;
        public long BaseLine { get; } = baseLine;
        public double BytesPerLine { get; } = bytesPerLine;
        public List<long> Starts { get; } = [baseStart];
        public bool AtEnd { get; set; }
    }

    private long EstimatedLines(Detour d) => d.BaseLine + (long)((Doc.ByteLength - d.BaseStart) / d.BytesPerLine);

    /// <summary>Start of the next line, or -1 when the line is the last one.</summary>
    private long NextLineStart(long start)
    {
        long end = Paged.Source.FindLineEnd(start);
        if (end >= Doc.ByteLength) return -1;
        int w = Doc.Codec.Alignment;
        Span<byte> two = stackalloc byte[8];
        int n = Doc.Read(end, two[..(2 * w)]);
        return end + (n == 2 * w && LineScanner.Unit(two, 0, Doc.Codec) == 13 && LineScanner.Unit(two, w, Doc.Codec) == 10 ? 2 * w : w);
    }

    private long DetourStart(Detour d, long line)
    {
        long index = line - d.BaseLine;
        if (index < 0) return -1;
        while (d.Starts.Count <= index && !d.AtEnd)
        {
            long next = NextLineStart(d.Starts[^1]);
            if (next < 0) d.AtEnd = true; else d.Starts.Add(next);
        }
        return index < d.Starts.Count ? d.Starts[(int)index] : -1;
    }

    private long DetourLineOf(Detour d, long offset)
    {
        while (!d.AtEnd && d.Starts[^1] <= offset) DetourStart(d, d.BaseLine + d.Starts.Count);
        int i = d.Starts.BinarySearch(offset);
        return d.BaseLine + Math.Max(0, i >= 0 ? i : ~i - 1);
    }

    private void EnterDetour(long offset)
    {
        long start = Math.Max(Doc.BomLength, Paged.Source.AlignToLineStart(offset));
        var sample = new byte[(int)Math.Min(Doc.ByteLength, 64 * 1024)];
        long from = Math.Clamp(start - sample.Length / 2, 0, Doc.ByteLength - sample.Length);
        Doc.Read(from, sample);
        int breaks = sample.AsSpan().Count((byte)10);
        double perLine = breaks > 0 ? Math.Max(1.0, (double)sample.Length / breaks) : Math.Max(1.0, sample.Length);
        // the gap between the indexed part and the target is counted with the mean of the density at both ends
        var (prefixLines, _) = Doc.LineIndexState;
        long indexed = Paged.IndexedBytes;
        double gapPerLine = prefixLines > 1 && indexed > 0 ? (perLine + (double)indexed / (prefixLines - 1)) / 2 : perLine;
        long baseLine = Math.Max(0, prefixLines - 1) + (long)Math.Max(0, (start - indexed) / gapPerLine);
        _detour = new Detour(start, baseLine, perLine);
        _lines.Clear();
        _lineMemo = (-1, -1, 0);
        _anchor = -1;
        _wanted = -1;
        _typingKey = null;
        _caret = offset;
        UpdateScrollRange();
        _bar.Value = DetourLineOf(_detour, offset);
        Reveal();
        RaiseViewChanged();
    }

    /// <summary>Drops the detour; the first line on screen keeps its text and takes its exact number.</summary>
    private void LeaveDetour(long? top = null)
    {
        if (_detour is not { } d) return;
        double old = _bar.Value;
        _detour = null;
        long exact = CanLocate(d.BaseStart) ? LineOf(d.BaseStart) : 0;
        _lines.Clear();
        _lineMemo = (-1, -1, 0);
        UpdateScrollRange();
        _bar.Value = Math.Clamp(top ?? (long)old - d.BaseLine + exact, 0, _bar.Maximum);
    }

    private void LeaveDetourWhenIndexed()
    {
        if (_detour is { } d && (Indexed || d.BaseStart < Paged.IndexedBytes)) LeaveDetour();
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
            if (view.Truncated) continue; // the feeder tokenizes it by columns from the document, not from this view
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
        long lines = Math.Max(1, ShownLines);
        int visible = Math.Max(1, _canvas.VisibleLines);
        long min = _detour?.BaseLine ?? 0;
        _bar.Minimum = min;
        _bar.Maximum = Math.Max(min, lines - visible);
        _bar.ViewportSize = visible;
        _bar.LargeChange = Math.Max(1, visible - 1);
        FeedSyntax();
        _mapChanged?.Invoke();
    }

    // ---- lines and the character map ----

    private long LineStart(long line)
    {
        if (_detour is { } d) return DetourStart(d, line);
        return line == 0 ? Doc.BomLength : Paged.TryGetLineStart(line, out long start) ? start : -1;
    }

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
        if (line < 0 || (start = LineStart(line)) < 0) return false;
        if ((next = LineStart(line + 1)) < 0)
        {
            if (!Indexed && _detour is null) return false; // the end of the last indexed line is not known yet
            end = next = Doc.ByteLength;
            return true;
        }
        int w = Doc.Codec.Alignment;
        Span<byte> tail = stackalloc byte[8];
        int n = (int)Math.Min(2 * w, next - start);
        Doc.Read(next - n, tail[..n]);
        end = next - (n == 2 * w && LineScanner.Unit(tail, 0, Doc.Codec) == 13 && LineScanner.Unit(tail, w, Doc.Codec) == 10 ? 2 * w : w);
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
        return new LineView(start, truncated, length, Doc.Codec.WithBom(false).Decode(bytes.AsMemory(0, take)), _options.TabSize);
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
        if (first < 0) return;
        long origin = LineStart(first);
        if (origin < 0 || !Indexed && _detour is null && first >= ShownLines) return;
        int window = (int)Math.Min(Doc.ByteLength - origin, 128 * 1024);
        bool toEnd = origin + window >= Doc.ByteLength;
        var buffer = ArrayPool<byte>.Shared.Rent(window); // a fresh 128 KiB array per edit lands on the large object heap
        try { FillLines(first, origin, buffer, window, toEnd); }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private void FillLines(long first, long origin, byte[] buffer, int window, bool toEnd)
    {
        Doc.Read(origin, buffer.AsSpan(0, window));
        int limit = Math.Max(64, _canvas.VisibleLines + 8), pos = 0, w = Doc.Codec.Alignment;
        var view = buffer.AsSpan(0, window);
        for (long line = first; line - first < limit; line++)
        {
            if (line > first && _lines.ContainsKey(line)) return; // lines kept after an edit stand behind the first one that was missing
            int end = LineScanner.FindTerminator(view, pos, Doc.Codec);
            int next;
            bool last = false;
            if (end < 0)
            {
                if (!toEnd) return; // the line is longer than the window: the caller decodes it on its own
                end = next = window;
                last = true;
            }
            else if (end + w > window) return; // a partial unit at the edge of the window
            else
            {
                next = end + w;
                if (LineScanner.Unit(view, end, Doc.Codec) == 13 && end + w >= window && !toEnd) return; // a CR at the edge of the window may start a CRLF
                if (LineScanner.Unit(view, end, Doc.Codec) == 10 && end >= pos + w && LineScanner.Unit(view, end - w, Doc.Codec) == 13) end -= w;
            }
            int length = end - pos;
            int take = Math.Min(length, MaxLineBytes);
            bool truncated = length > take;
            if (truncated) take = Doc.Codec.CompleteLength(buffer.AsSpan(pos, take));
            _lines[line] = new LineView(origin + pos, truncated, length, Doc.Codec.WithBom(false).Decode(buffer.AsSpan(pos, take).ToArray()), _options.TabSize);
            if (last) return;
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
        if (_detour is { } d) return DetourLineOf(d, offset);
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
        if (!CanLocate(_caret)) return null;
        if (_lineMemo.Revision != Doc.Revision || _lineMemo.Caret != _caret) _lineMemo = (Doc.Revision, _caret, LineOf(_caret));
        return GetLine(_lineMemo.Line) is { } view ? (_lineMemo.Line, view) : null;
    }

    /// <summary>
    /// The caret line for moving and editing: <see cref="View"/> holds the whole line, or for a truncated line only the characters around the caret
    /// (its <c>Start</c> is where they begin), and <see cref="Column"/> is the UTF-16 column of the caret in the whole line.
    /// </summary>
    private readonly record struct Site(long Line, LineView View, long Column, bool Long);

    private Site? CaretSite()
    {
        if (CaretLine() is not { } c) return null;
        if (!c.View.Truncated) return new Site(c.Line, c.View, c.View.Map.CharIndexOf(_caret - c.View.Start), false);
        return new Site(c.Line, LocalView(c.View, _caret), ColumnOfOffset(c.View, _caret), true);
    }

    private const int LocalBytes = 64;

    /// <summary>Decodes the bytes of a truncated line within <see cref="LocalBytes"/> of an offset, so that editing next to the caret never reads the rest of the line.</summary>
    private LineView LocalView(LineView full, long offset)
    {
        var codec = Doc.Codec;
        int unit = codec.Alignment;
        long end = full.Start + full.ByteLength;
        offset = Math.Clamp(offset, full.Start, end);
        long from = full.Start + (Math.Max(full.Start, offset - LocalBytes - 3) - full.Start) / unit * unit;
        long to = Math.Min(end, offset + LocalBytes + 4);
        var bytes = new byte[to - from];
        Doc.Read(from, bytes);
        int first = 0;
        if (from > full.Start)
            for (first = (3 + unit - 1) / unit * unit; first < bytes.Length && !codec.IsBoundary(bytes, first); first += unit) { }
        int last = to < end ? first + codec.CompleteLength(bytes.AsSpan(first)) : bytes.Length;
        return new LineView(from + first, false, last - first, codec.WithBom(false).Decode(bytes.AsMemory(first, last - first)), _options.TabSize);
    }

    private long _columnsRevision = -1;
    private readonly Dictionary<long, long> _columnMemo = [];
    private readonly Dictionary<long, long> _offsetMemo = [];

    private void ForgetColumns()
    {
        if (_columnsRevision == Doc.Revision && _columnMemo.Count <= 16 && _offsetMemo.Count <= 16) return;
        _columnMemo.Clear();
        _offsetMemo.Clear();
        _columnsRevision = Doc.Revision;
    }

    /// <summary>UTF-16 column of a byte offset of the truncated line <paramref name="line"/>; it counts from the nearest column point, not from the start of the line.</summary>
    private long ColumnOfOffset(LineView line, long offset)
    {
        ForgetColumns();
        if (_columnMemo.TryGetValue(offset, out long known)) return known;
        return _columnMemo[offset] = offset <= line.Start ? 0 : Paged.ColumnOf(line.Start, offset);
    }

    /// <summary>Byte offset of a UTF-16 column of the truncated line <paramref name="line"/>; the end of the line when it is shorter.</summary>
    private long OffsetOfColumn(LineView line, long column)
    {
        ForgetColumns();
        if (_offsetMemo.TryGetValue(column, out long known)) return known;
        return _offsetMemo[column] = Paged.OffsetOfColumn(line.Start, column);
    }

    /// <summary>Remembers the column of an offset that an edit has just put the caret on, so that the next move does not count it again.</summary>
    private void NoteColumn(long offset, long column)
    {
        ForgetColumns();
        _columnMemo[offset] = column;
    }

    /// <summary>UTF-16 column of the caret in the line shown by <paramref name="view"/>.</summary>
    private long CaretColumn(LineView view) => view.Truncated ? ColumnOfOffset(view, _caret) : view.Map.CharIndexOf(_caret - view.Start);

    // ---- moving ----

    private void MoveTo(long caret, long? line = null, bool keepWanted = false)
    {
        if (!_extend) _anchor = -1;
        else if (_anchor < 0) _anchor = _caret;
        _caret = caret;
        _preedit = "";
        if (_anchor == _caret) _anchor = -1;
        if (line is { } known) _lineMemo = (Doc.Revision, caret, known);
        if (!keepWanted) _wanted = -1;
        _typingKey = null;
        Reveal();
        RaiseViewChanged();
    }

    private void Left()
    {
        if (!_extend && HasSelection) { MoveTo(SelectionRange.Start); return; }
        if (CaretSite() is not { } c) return;
        int i = c.View.Map.CharIndexOf(_caret - c.View.Start);
        if (i > 0) MoveTo(c.View.Start + c.View.Map.ByteOffsetOf(c.View.Map.IsCharBoundary(i - 1) ? i - 1 : i - 2), c.Line);
        else if (c.Line > 0 && TryBounds(c.Line - 1, out _, out long end, out _)) MoveTo(end, c.Line - 1);
    }

    private void Right()
    {
        if (!_extend && HasSelection) { MoveTo(SelectionRange.End); return; }
        if (CaretSite() is not { } c) return;
        var map = c.View.Map;
        int i = map.CharIndexOf(_caret - c.View.Start);
        if (i < map.Text.Length) MoveTo(c.View.Start + map.ByteOffsetOf(map.IsCharBoundary(i + 1) ? i + 1 : i + 2), c.Line);
        else if (!c.View.Truncated && LineStart(c.Line + 1) is >= 0 and var next) MoveTo(next, c.Line + 1);
    }

    private void Vertical(long delta)
    {
        if (CaretSite() is not { } c) return;
        long target = Math.Clamp(c.Line + delta, 0, LineCount - 1);
        if (target == c.Line || GetLine(target) is not { } view) return;
        if (_wanted < 0) _wanted = c.Long ? (int)Math.Min(c.Column, int.MaxValue) : c.View.DisplayAt[c.View.Map.CharIndexOf(_caret - c.View.Start)];
        long to = view.Truncated && _wanted >= view.Display.Length ? OffsetOfColumn(view, _wanted) : view.Start + view.Map.ByteOffsetOf(view.IndexAtDisplay(_wanted));
        MoveTo(to, target, keepWanted: true);
    }

    private void LineEdge(bool end)
    {
        if (CaretLine() is not { } c) return;
        MoveTo(end ? c.View.Start + c.View.ByteLength : c.View.Start, c.Line);
    }

    /// <summary>Keeps the caret on screen: scrolls the lines, and sideways when the line is wider than the view.</summary>
    private void Reveal()
    {
        if (CaretLine() is not { } c) return;
        int visible = Math.Max(1, _canvas.VisibleLines - 1);
        if (c.Line < TopLine) _bar.Value = c.Line;
        else if (c.Line >= TopLine + visible) _bar.Value = Math.Min(_bar.Maximum, c.Line - visible + 1);
        double x = _canvas.XOfColumn(c.View, CaretColumn(c.View)), width = _canvas.TextWidth;
        if (x < _hScroll) _hScroll = Math.Max(0, x - 24);
        else if (x > _hScroll + width - 8) _hScroll = x - width + 32;
        if (_hBar.IsVisible) _hBar.Value = Math.Clamp(_hScroll / _canvas.CharWidth, 0, _hBar.Maximum);
        FeedSyntax();
        _canvas.InvalidateVisual();
    }

    // ---- long lines ----

    /// <summary>
    /// Sets the range of the horizontal bar from the widest truncated line drawn last. The bar counts columns of the fixed cell width;
    /// the bound of a line is its length in characters once a read has reached its end, else its byte length.
    /// </summary>
    private void UpdateSideways()
    {
        double cell = _canvas.CharWidth, columns = _canvas.TextWidth / cell;
        _hBar.IsVisible = _truncatedColumns > 0;
        _hBar.Maximum = Math.Max(0, _truncatedColumns + (_options.ShowLineEndings ? LineEndMarkColumns : 0) - columns);
        _hBar.ViewportSize = columns;
        _hBar.LargeChange = Math.Max(1, columns - 1);
    }

    private long ColumnBound(LineView view) => Paged.TryGetLineColumns(view.Start, out long columns) ? columns : view.ByteLength;

    /// <summary>Scrolls the long lines sideways to a column, as the bar does; for tests and measurements.</summary>
    internal void ScrollToColumn(double column) => _hBar.Value = Math.Clamp(column, 0, _hBar.Maximum);

    /// <summary>Text of the window kept for the line starting at <paramref name="lineStart"/> in the current revision, or null.</summary>
    internal string? CachedWindow(long lineStart) => _windows.TryGetValue(lineStart, out var known) && known.Revision == Doc.Revision ? known.Text : null;

    /// <summary>Largest column the horizontal bar scrolls to.</summary>
    internal double HorizontalMaximum => _hBar.Maximum;

    /// <summary>Replaces the read of windows that run off the UI thread, so that tests can hold a read back.</summary>
    internal Func<long, long, int, CancellationToken, string>? WindowRead { get; set; }

    /// <summary>
    /// Text of the columns [<paramref name="column"/>, +<paramref name="columns"/>) of a truncated line, read from the document without decoding the rest of the line.
    /// The window is kept for the revision it was read in. A read that would walk far (no column point near, document not edited) runs on the thread pool:
    /// this returns null at once and the surface draws the window when the read ends; a newer window of the same line cancels the older read.
    /// </summary>
    private string? WindowOf(LineView view, long column, int columns)
    {
        if (_windows.TryGetValue(view.Start, out var known) && known.Revision == Doc.Revision && known.Column == column && known.Columns == columns) return known.Text;
        if (Paged.IsModified || (WindowRead is null && Paged.Source.IsColumnNear(view.Start, column)))
        {
            if (_windows.Count > 16) _windows.Clear();
            string text = Paged.ReadColumns(view.Start, column, columns);
            _windows[view.Start] = (Doc.Revision, column, columns, text);
            BuildColumnIndexInBackground(view.Start);
            return text;
        }
        if (_pending.TryGetValue(view.Start, out var pending))
        {
            if (pending.Revision == Doc.Revision && pending.Column == column && pending.Columns == columns) return null;
            pending.Cts.Cancel();
        }
        var cts = new CancellationTokenSource();
        _pending[view.Start] = (cts, Doc.Revision, column, columns);
        _ = LoadWindow(view.Start, column, columns, cts);
        return null;
    }

    private async Task LoadWindow(long lineStart, long column, int columns, CancellationTokenSource cts)
    {
        long revision = Doc.Revision;
        var source = Paged.Source;
        var read = WindowRead;
        string? text = null;
        try { text = await Task.Run(() => read is null ? source.ReadColumns(lineStart, column, columns, cts.Token) : read(lineStart, column, columns, cts.Token), cts.Token); }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException) { }
        bool current = _pending.TryGetValue(lineStart, out var now) && now.Cts == cts;
        if (current) _pending.Remove(lineStart);
        if (!current) return;
        if (text is not null && !cts.IsCancellationRequested && revision == Doc.Revision)
        {
            if (_windows.Count > 16) _windows.Clear();
            _windows[lineStart] = (revision, column, columns, text);
            BuildColumnIndexInBackground(lineStart);
        }
        _canvas.InvalidateVisual();
    }

    private void CancelWindows()
    {
        foreach (var (_, pending) in _pending) pending.Cts.Cancel();
        _pending.Clear();
    }

    /// <summary>
    /// Notes the column points of a long line on the thread pool, so that later jumps along it resume from a known point, and its length in characters for the bar.
    /// It reads the mapped original, so it only starts while the document is unedited; the index belongs to that file and survives later edits of other parts.
    /// </summary>
    private void BuildColumnIndexInBackground(long lineStart)
    {
        if (Paged.IsModified || !_indexing.Add(lineStart)) return;
        var source = Paged.Source;
        var token = (_indexCts ??= new()).Token;
        _ = Task.Run(() =>
        {
            try { source.ReadColumns(lineStart, long.MaxValue, 0, token); }
            catch (OperationCanceledException) { return; }
            Dispatcher.UIThread.Post(_canvas.InvalidateVisual);
        });
    }

    // ---- editing ----

    /// <summary>Set while the file is locked by another program or window: edits are refused with this text.</summary>
    public string? ReadOnlyReason { get; set; }

    private bool CanEdit(out Site site)
    {
        site = default;
        if (ReadOnlyReason is { } locked) { Refused?.Invoke(locked); return false; }
        if (!Indexed) { Refused?.Invoke(Loc.Get(K.PagedIndexing)); return false; }
        if (CaretSite() is not { } c) { Refused?.Invoke(Loc.Get(K.PagedIndexing)); return false; }
        site = c;
        return true;
    }

    private bool Commit(long at, long deleted, byte[] insert, long caretAfter, long lineAfter, bool typing, bool isInsert, long columnAfter = -1)
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
            SelectionsBefore = [HasSelection ? SelectionRange : new ByteRange(_caret, _caret)],
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
        _anchor = -1;
        _lineMemo = (Doc.Revision, caretAfter, lineAfter);
        if (columnAfter >= 0) NoteColumn(caretAfter, columnAfter);
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
        if (text.Length == 0 || !CanEdit(out var site)) return false;
        byte[] bytes;
        try { bytes = Doc.Codec.Encode(text); }
        catch (UnrepresentableCharactersException)
        {
            Refused?.Invoke(Loc.Get(K.PagedNotEncodable, ("encoding", Doc.Codec.Name)));
            return false;
        }
        if (!HasSelection)
        {
            int breaks = LineBreaks(text);
            return Commit(_caret, 0, bytes, _caret + bytes.Length, site.Line + breaks, typing, isInsert: true, columnAfter: site.Long && breaks == 0 ? site.Column + text.Length : -1);
        }
        var (from, to) = (SelectionRange.Start, SelectionRange.End);
        return Commit(from, to - from, bytes, from + bytes.Length, LineOf(from) + LineBreaks(text), typing: false, isInsert: true);
    }

    /// <summary>Deletes the selection as one undo step; false when nothing is selected or the edit is refused.</summary>
    private bool DeleteSelection()
    {
        if (!HasSelection || !CanEdit(out _)) return false;
        var (from, to) = (SelectionRange.Start, SelectionRange.End);
        return Commit(from, to - from, [], from, LineOf(from), typing: false, isInsert: false);
    }

    // ---- clipboard ----

    /// <summary>Copies the selected text; with <paramref name="cut"/> also deletes it as one undo step.</summary>
    public async Task CopyAsync(bool cut)
    {
        if (!HasSelection || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || cut && !CanEdit(out _)) return;
        string text = SelectedText();
        await clipboard.SetTextAsync(text);
        Copied?.Invoke(text);
        if (cut) DeleteSelection();
    }

    /// <summary>Pastes the clipboard text over the selection as one undo step, with the line breaks of the document.</summary>
    public async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text) PasteText(text);
    }

    internal bool PasteText(string text) =>
        InsertText(AvaloniaEdit.Document.TextUtilities.NormalizeNewLines(text, _doc.NewLine(_options.ConfiguredEol)), typing: false);

    public void SelectAll()
    {
        if (!Indexed) return;
        _anchor = Doc.BomLength;
        _extend = true;
        try { MoveTo(Doc.ByteLength, LineCount - 1); }
        finally { _extend = false; }
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
        if (HasSelection) { DeleteSelection(); return; }
        if (!CanEdit(out var site)) return;
        var (line, view) = (site.Line, site.View);
        int i = view.Map.CharIndexOf(_caret - view.Start);
        if (i > 0)
        {
            int j = view.Map.IsCharBoundary(i - 1) ? i - 1 : i - 2;
            long at = view.Start + view.Map.ByteOffsetOf(j);
            Commit(at, _caret - at, [], at, line, typing: true, isInsert: false, columnAfter: site.Long ? site.Column - (i - j) : -1);
        }
        else if (line > 0 && TryBounds(line - 1, out _, out long end, out long next)) Commit(end, next - end, [], end, line - 1, typing: false, isInsert: false);
    }

    private void DeleteForward()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (!CanEdit(out var site)) return;
        var (line, view) = (site.Line, site.View);
        var map = view.Map;
        int i = map.CharIndexOf(_caret - view.Start);
        if (i < map.Text.Length)
            Commit(_caret, view.Start + map.ByteOffsetOf(map.IsCharBoundary(i + 1) ? i + 1 : i + 2) - _caret, [], _caret, line, typing: true, isInsert: false, columnAfter: site.Long ? site.Column : -1);
        else if (line + 1 < LineCount && TryBounds(line, out _, out long end, out long next)) Commit(end, next - end, [], end, line, typing: false, isInsert: false);
    }

    private void Tab()
    {
        if (!CanEdit(out var site)) return;
        int size = Math.Max(1, _options.InsertSpaces ? _options.IndentSize : _options.TabSize);
        int column = site.Long ? (int)Math.Min(site.Column, int.MaxValue / 2) : site.View.DisplayAt[site.View.Map.CharIndexOf(_caret - site.View.Start)];
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
        _preedit = "";
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
        if (e.Delta.X != 0 || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            _hBar.Value = Math.Clamp(_hBar.Value - (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 3 * _canvas.WindowColumns / 8, 0, _hBar.Maximum);
        else _bar.Value = Math.Clamp(_bar.Value - e.Delta.Y * 3, 0, _bar.Maximum);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Place(e.GetPosition(_canvas), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _dragFrom = _caret;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom < 0) return;
        Place(e.GetPosition(_canvas), extend: true);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom < 0) return;
        _dragFrom = -1;
        e.Pointer.Capture(null);
    }

    private void Place(Point point, bool extend)
    {
        long line = Math.Clamp(TopLine + (long)Math.Floor(point.Y / _canvas.LineHeight), 0, LineCount - 1);
        if (GetLine(line) is not { } view) return;
        double x = point.X - _canvas.TextLeft + _hScroll;
        long to = view.Truncated && x / _canvas.CharWidth >= view.Display.Length
            ? OffsetOfColumn(view, (long)Math.Round(x / _canvas.CharWidth))
            : view.Start + view.Map.ByteOffsetOf(_canvas.IndexAtX(view, x));
        _extend = extend;
        try { MoveTo(to, line); }
        finally { _extend = false; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        _extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;
        try
        {
        switch (e.Key)
        {
            case Key.Left: Left(); break;
            case Key.Right: Right(); break;
            case Key.Up: Vertical(-1); break;
            case Key.Down: Vertical(1); break;
            case Key.PageUp: Vertical(-PageLines); break;
            case Key.PageDown: Vertical(PageLines); break;
            case Key.Home when command: GoToOffset(Doc.BomLength); break;
            case Key.End when command: GoToOffset(Doc.ByteLength); break;
            case Key.Home: LineEdge(end: false); break;
            case Key.End: LineEdge(end: true); break;
            case Key.Back when !command: Backspace(); break;
            case Key.Delete when !command: DeleteForward(); break;
            case Key.Enter when !command: InsertText(_doc.NewLine(_options.ConfiguredEol), typing: false); break;
            case Key.Tab when !command && !e.KeyModifiers.HasFlag(KeyModifiers.Shift): Tab(); break;
            default: base.OnKeyDown(e); return;
        }
        }
        finally { _extend = false; }
        e.Handled = true;
    }

    // ---- input method ----

    private void SetPreedit(string text)
    {
        if (text == _preedit) return;
        _preedit = text;
        _canvas.InvalidateVisual();
        _inputClient.PreeditChanged();
    }

    /// <summary>Selects the characters between two UTF-16 indexes of the caret line (the surrounding text the input method sees).</summary>
    private void SelectInCaretLine(int start, int end)
    {
        if (CaretSite() is not { } c) return;
        var map = c.View.Map;
        int a = Math.Clamp(start, 0, map.Text.Length), b = Math.Clamp(end, 0, map.Text.Length);
        while (a > 0 && !map.IsCharBoundary(a)) a--;
        while (b > 0 && !map.IsCharBoundary(b)) b--;
        SelectBytes(c.View.Start + map.ByteOffsetOf(a), c.View.Start + map.ByteOffsetOf(b), c.Line);
    }

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="caret"/>, both byte offsets on character boundaries.</summary>
    internal void SelectBytes(long anchor, long caret, long? line = null)
    {
        _extend = true;
        _anchor = anchor;
        try { MoveTo(caret, line); }
        finally { _extend = false; }
    }


    /// <summary>
    /// What the system input method asks of the surface: where the caret is, so that its candidate window opens there, the line around it, and
    /// the composition, which is drawn at the caret and underlined without touching the document. The committed text arrives as ordinary text
    /// input and replaces the composition; reconversion sets the selection inside the caret line.
    /// </summary>
    private sealed class InputClient(PagedTextView owner) : TextInputMethodClient
    {
        public override Visual TextViewVisual => owner._canvas;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => true;
        public override string SurroundingText => owner.CaretSite() is { } c ? c.View.Map.Text : "";
        public override Rect CursorRectangle => owner._canvas.CaretRect();

        public override Avalonia.Input.TextInput.TextSelection Selection
        {
            get
            {
                if (owner.CaretSite() is not { } c) return new Avalonia.Input.TextInput.TextSelection(0, 0);
                var map = c.View.Map;
                int caret = map.CharIndexOf(owner._caret - c.View.Start);
                if (!owner.HasSelection) return new Avalonia.Input.TextInput.TextSelection(caret, caret);
                // a selection that leaves the line (or the excerpt of a long line) is cut at its edge, so reconversion sees only text it can read
                var range = owner.SelectionRange;
                int from = range.Start <= c.View.Start ? 0 : map.CharIndexOf(Math.Min(range.Start, c.View.Start + c.View.ByteLength) - c.View.Start);
                int to = range.End >= c.View.Start + c.View.ByteLength ? map.Text.Length : map.CharIndexOf(range.End - c.View.Start);
                return new(from, to);
            }
            set => owner.SelectInCaretLine(value.Start, value.End);
        }

        public override void SetPreeditText(string? preeditText) => owner.SetPreedit(preeditText ?? "");

        public void PreeditChanged() => RaiseCursorRectangleChanged();

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
        public LineView(long start, bool truncated, long byteLength, DecodedText map, int tabSize)
        {
            Start = start;
            Truncated = truncated;
            ByteLength = byteLength;
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

        private LineView(LineView other, long start, long byteLength)
        {
            Start = start;
            Truncated = other.Truncated;
            ByteLength = byteLength;
            Map = other.Map;
            DisplayAt = other.DisplayAt;
            Display = other.Display;
            _marked = other._marked;
        }

        /// <summary>The same decoded part of a line whose length changed behind it.</summary>
        public LineView WithByteLength(long byteLength) => new(this, Start, byteLength);

        /// <summary>The same line after an edit before it moved it.</summary>
        public LineView WithStart(long start) => new(this, start, ByteLength);

        public long Start { get; }
        /// <summary>Bytes of the whole line without its line break, also when only the first part was decoded.</summary>
        public long ByteLength { get; }
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
        /// <summary>Width of one cell of the fixed grid that horizontal windows of truncated lines use.</summary>
        public double CharWidth => _digitWidth;
        /// <summary>Columns that fit in the text area, with one more cell at each edge.</summary>
        public int WindowColumns => (int)Math.Ceiling(TextWidth / _digitWidth) + 2;
        public double DigitWidth => _digitWidth;
        public double TextLeft => _gutter + TextPadding;
        public double TextWidth => Math.Max(0, Bounds.Width - TextLeft);

        private FormattedText Format(string text, IBrush brush) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(owner._options.FullFontFamily)), owner._options.FontSize, brush);

        /// <summary>The caret in the coordinates of this surface; empty when its line is not on screen.</summary>
        public Rect CaretRect()
        {
            if (owner.CaretLine() is not { } c || c.Line < owner.TopLine || c.Line > owner.TopLine + VisibleLines) return default;
            double x = TextLeft - owner._hScroll + XOfColumn(c.View, owner.CaretColumn(c.View));
            if (owner._preedit.Length > 0) x += Format(owner._preedit, Brushes.Black).WidthIncludingTrailingWhitespace;
            return new Rect(x, (c.Line - owner.TopLine) * _lineHeight, 1.5, _lineHeight);
        }

        /// <summary>Horizontal position of a UTF-16 column: measured inside the decoded part of the line, on the fixed grid of the windows beyond it.</summary>
        public double XOfColumn(LineView view, long column) =>
            column <= view.Map.Text.Length ? XOf(view, (int)column) : column * _digitWidth;

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
            var state = (owner.Doc.Revision, owner._caret, owner.TopLine, owner._hScroll, owner.WindowsPending);
            if (owner._drawn == state) return;
            owner._drawn = state;
            owner.RenderedAt = ended;
            owner.RenderMs = (ended - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>Fills the part of the selection that lies in the window of <paramref name="length"/> columns from column <paramref name="column"/> of a truncated line.</summary>
        private void DrawWindowSelection(DrawingContext context, LineView view, double y, double x, long column, int length, IBrush brush)
        {
            var (from, to) = (owner.SelectionRange.Start, owner.SelectionRange.End);
            if (to <= view.Start || from > view.Start + view.ByteLength) return;
            long windowStart = owner.OffsetOfColumn(view, column), windowEnd = owner.OffsetOfColumn(view, column + length);
            if (to <= windowStart || from >= windowEnd) return;
            long a = from <= windowStart ? column : owner.ColumnOfOffset(view, from);
            long b = to >= windowEnd ? column + length : owner.ColumnOfOffset(view, to);
            if (b > a) context.FillRectangle(brush, new Rect(x + (a - column) * _digitWidth, y, (b - a) * _digitWidth, _lineHeight));
        }

        private void DrawSelection(DrawingContext context, LineView view, double y, IBrush brush)
        {
            var (from, to) = (owner.SelectionRange.Start, owner.SelectionRange.End);
            long textEnd = view.Start + view.Map.ByteOffsetOf(view.Map.Text.Length);
            if (to <= view.Start || from > textEnd) return;
            int a = from <= view.Start ? 0 : view.Map.CharIndexOf(from - view.Start), b = to >= textEnd ? view.Map.Text.Length : view.Map.CharIndexOf(to - view.Start);
            double x1 = XOf(view, a), x2 = XOf(view, b) + (to > textEnd ? _digitWidth : 0);
            if (x2 > x1) context.FillRectangle(brush, new Rect(TextLeft - owner._hScroll + x1, y, x2 - x1, _lineHeight));
        }

        /// <summary>Dims the space and tab marks of <paramref name="shown"/>, so they stand back from the text.</summary>
        private static void DimMarks(FormattedText text, string shown, IBrush dim)
        {
            for (int k = 0; k < shown.Length;)
            {
                if (shown[k] is not ('·' or '→')) { k++; continue; }
                int end = k + 1;
                while (end < shown.Length && shown[end] is '·' or '→') end++;
                text.SetForegroundBrush(dim, k, end - k);
                k = end;
            }
        }

        /// <summary>Draws CR, LF or CRLF after the last character of <paramref name="line"/>, which ends at <paramref name="x"/>; nothing for the last line.</summary>
        private void DrawLineEnding(DrawingContext context, long line, double x, double y, IBrush dim)
        {
            if (LineEndingRenderer.Label(owner.LineEndingOf(line)) is not { } label) return;
            var mark = Format(label, dim);
            mark.SetFontSize(owner._options.FontSize * 0.7);
            context.DrawText(mark, new Point(x + _digitWidth * 0.5, y + Math.Max(0, (_lineHeight - mark.Height) / 2)));
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
            var levels = owner._doc.IsLog ? owner._doc.LogLevels : null;
            var selection = Brush("SelectionBg", Brushes.LightBlue);
            var dim = new ImmutableSolidColorBrush(fore is ISolidColorBrush solid ? solid.Color : Colors.Gray, 0.4);
            context.FillRectangle(back, new Rect(Bounds.Size));
            long lines = owner.LineCount;
            string approx = owner.LinesExact ? "" : "≈";
            int digits = Math.Max(3, (lines > 0 ? lines : 1).ToString(CultureInfo.InvariantCulture).Length + approx.Length);
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
                long widest = 0;
                for (int i = 0; i < VisibleLines + 1; i++)
                {
                    long line = top + i;
                    if (owner.GetLine(line) is not { } view) break;
                    double y = i * _lineHeight;
                    if (options.CurrentLine && caretLine is { } cl && cl.Line == line) context.FillRectangle(current, new Rect(_gutter, y, Bounds.Width, _lineHeight));
                    if (owner.HasSelection) DrawSelection(context, view, y, selection);
                    string shown = options.ShowWhitespace ? view.Marked : view.Display;
                    if (view.Truncated)
                    {
                        widest = Math.Max(widest, owner.ColumnBound(view));
                        if (owner._hScroll / _digitWidth >= view.Display.Length)
                        {
                            long column = (long)Math.Floor(owner._hScroll / _digitWidth);
                            double x = TextLeft + column * _digitWidth - owner._hScroll;
                            if (owner.WindowOf(view, column, WindowColumns) is not { } window) context.DrawText(Format("…", dim), new Point(x, y));
                            else
                            {
                                if (owner.HasSelection) DrawWindowSelection(context, view, y, x, column, window.Length, selection);
                                string drawn = options.ShowWhitespace ? window.Replace(' ', '·').Replace('\t', '→') : window.Replace('\t', ' ');
                                var windowText = Format(drawn, fore);
                                if (options.ShowWhitespace) DimMarks(windowText, drawn, dim);
                                context.DrawText(windowText, new Point(x, y));
                                // a window shorter than asked for holds the end of the line; an empty one starts after it
                                if (options.ShowLineEndings && window.Length > 0 && window.Length < WindowColumns) DrawLineEnding(context, line, x + window.Length * _digitWidth, y, dim);
                            }
                            continue;
                        }
                    }
                    int insertAt = -1, insertLength = 0;
                    if (owner._preedit.Length > 0 && caretLine is { } pl && pl.Line == line)
                    {
                        insertAt = view.DisplayAt[view.Map.CharIndexOf(owner._caret - view.Start)];
                        insertLength = owner._preedit.Length;
                        shown = shown.Insert(insertAt, owner._preedit);
                    }
                    var text = Format(shown, fore);
                    if (levels?.Classify(view.Map.Text) is { } level && palette.GetBrush(LogLevelKinds.Of(level)) is { } levelBrush) text.SetForegroundBrush(levelBrush);
                    if (owner._tokens.TryGetValue(line, out var tokens))
                        foreach (var token in tokens)
                        {
                            int from = view.DisplayAt[Math.Min(token.Start, view.Map.Text.Length)], to = view.DisplayAt[Math.Min(token.End, view.Map.Text.Length)];
                            if (insertLength > 0)
                            {
                                if (from >= insertAt) from += insertLength;
                                if (to > insertAt) to += insertLength;
                            }
                            if (palette.GetBrush(token.Kind) is { } brush && to > from) text.SetForegroundBrush(brush, from, to - from);
                        }
                    if (insertLength > 0) text.SetTextDecorations(TextDecorations.Underline, insertAt, insertLength);
                    if (options.ShowWhitespace) DimMarks(text, shown, dim);
                    if (owner._doc.LogHighlight is { } highlight && palette.GetBrush(LogHighlightColorizer.Kind) is { } highlightBrush)
                    {
                        string searched = view.Map.Text.Length > LogLevelColorizer.MaxClassifiedChars ? view.Map.Text[..LogLevelColorizer.MaxClassifiedChars] : view.Map.Text;
                        foreach (var (start, length) in LogHighlightColorizer.Spans(highlight, searched))
                        {
                            double left = XOf(view, start);
                            context.FillRectangle(highlightBrush, new Rect(TextLeft - owner._hScroll + left, y, XOf(view, start + length) - left, _lineHeight));
                        }
                    }
                    context.DrawText(text, new Point(TextLeft - owner._hScroll, y));
                    if (options.ShowLineEndings && !view.Truncated) DrawLineEnding(context, line, TextLeft - owner._hScroll + XOf(view, view.Map.Text.Length), y, dim);
                }
                if (CaretRect() is { Height: > 0 } caretRect) context.FillRectangle(fore, caretRect);
                if (widest != owner._truncatedColumns)
                {
                    owner._truncatedColumns = widest;
                    Dispatcher.UIThread.Post(owner.UpdateSideways);
                }
            }
            for (int i = 0; options.LineNumbers && i < VisibleLines + 1; i++)
            {
                long line = top + i;
                if (line >= lines || owner.GetLine(line) is null) break;
                var number = Format(approx + (line + 1).ToString(CultureInfo.InvariantCulture), numbers);
                context.DrawText(number, new Point(_gutter - 10 - number.Width, i * _lineHeight));
            }
            owner.DrawBookmarks(context, top, _lineHeight, VisibleLines + 1);
        }
    }
}
