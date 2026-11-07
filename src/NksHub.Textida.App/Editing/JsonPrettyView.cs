using System.Globalization;
using System.Text;
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
/// One line with a long key or value at a time can be wrapped over several rows (<see cref="ToggleExpand"/>), so it can be read and edited past the cut.
/// </summary>
internal sealed class JsonPrettyView : Grid, IDisposable
{
    private const string Indent = "  ", FoldMark = " … ";

    /// <summary>The most a copy, a cut or select all takes, counted in bytes of the document.</summary>
    public const long MaxCopyBytes = 16L << 20;

    /// <summary>The limit this view applies; tests lower it to stay small.</summary>
    internal long CopyLimit { get; set; } = MaxCopyBytes;

    /// <summary>
    /// A place in the formatted text that stays put when a line is wrapped or unwrapped: in the indent before the first piece of the line (region 0, a column),
    /// inside or between its key and value, raw or comment text (region 1, a document offset) or after the last piece (region 2, a column of the tail).
    /// Lines without such a piece use region 0 only.
    /// </summary>
    internal readonly record struct Pos(long Line, int Region, long At) : IComparable<Pos>
    {
        public int CompareTo(Pos other) => Line != other.Line ? Line.CompareTo(other.Line) : Region != other.Region ? Region.CompareTo(other.Region) : At.CompareTo(other.At);
    }

    private readonly OpenDocument _od;
    private readonly JsonOutline? _outline;
    private readonly JsonFolds _folds = new();
    private readonly Surface _canvas;
    private readonly ScrollBar _vbar = new() { Orientation = Orientation.Vertical, Minimum = 0, SmallChange = 1, AllowAutoHide = false };
    private readonly ScrollBar _hbar = new() { Orientation = Orientation.Horizontal, Minimum = 0, SmallChange = 20, AllowAutoHide = false };
    private EditorOptions _options;
    private long _top, _caretLine, _caretPart, _expanded = -1;
    private int _caretColumn;
    private double _hScroll;
    private ByteRange? _mark;
    private Pos? _anchor;
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
            _outline = new JsonOutline(doc.Doc, JsonDataReader.IsJsoncFile(doc.Path));
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
    public long CaretPart => _caretPart;
    public int CaretColumn => _caretColumn;
    public long TopRow => _top;
    public int VisibleRows => _canvas.VisibleRows;

    /// <summary>The line whose long value is wrapped over several rows; -1 when none is.</summary>
    public long ExpandedLine => _expanded;

    /// <summary>Bytes of a long key or value per row while it is wrapped: as many as fit the width of the view, taken when the size or the font changes.</summary>
    internal int PartBytes { get; private set; } = 64;

    /// <summary>Takes the bytes per row for the present width; the caret stays on its character when the wrapped line flows anew.</summary>
    private void UpdatePartBytes()
    {
        int next = Math.Clamp((int)(_canvas.TextWidth / Math.Max(1, _canvas.CharWidth)) - 8, 64, JsonOutline.MaxShownBytes);
        if (next == PartBytes) return;
        Pos? caret = _expanded >= 0 ? CaretPos : null;
        PartBytes = next;
        if (caret is { } place) PlaceCaret(place);
    }

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
        Dispatcher.UIThread.Post(UpdatePartBytes, DispatcherPriority.Background); // after the measure took the new character width
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

    /// <summary>Rows the wrapped value takes beyond its first one.</summary>
    private long ExtraRows => _expanded >= 0 && LineAt(_expanded) is { } l ? JsonOutline.PartCount(l, PartBytes) - 1 : 0;

    private long RowCount => Math.Max(0, LineCount - _folds.Hidden + ExtraRows);

    /// <summary>The line and the part of a wrapped value shown in visible row <paramref name="row"/>.</summary>
    private (long Line, long Part) At(long row)
    {
        long extra = ExtraRows;
        if (extra > 0)
        {
            long first = _folds.RowOfLine(_expanded);
            if (row > first) return row <= first + extra ? (_expanded, row - first) : (_folds.LineOfRow(row - extra), 0);
        }
        return (_folds.LineOfRow(row), 0);
    }

    /// <summary>The visible row of a line (and part of the wrapped value); a hidden line gives the row of the fold that hides it.</summary>
    private long RowOf(long line, long part = 0)
    {
        long row = _folds.RowOfLine(line), extra = ExtraRows;
        if (extra > 0 && line == _expanded) row += Math.Clamp(part, 0, extra);
        else if (extra > 0 && line > _expanded) row += extra;
        return row;
    }

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

    /// <summary>What a row shows: a part of the wrapped value, or the line with the marks of a collapsed container.</summary>
    private JsonRenderedLine? RowRendered(long line, long part)
    {
        if (line == _expanded && _outline is not null && LineAt(line) is { } l) return _outline.RenderPart(l, part, PartBytes, Indent);
        return Rendered(line);
    }

    /// <summary>The row as editing sees it: without the marks of a collapsed container.</summary>
    private JsonRenderedLine? EditRendered(long line, long part)
    {
        if (_outline is null || LineAt(line) is not { } l) return null;
        return line == _expanded ? _outline.RenderPart(l, part, PartBytes, Indent) : _outline.Render(l, Indent);
    }

    /// <summary>The text of a visible row (for tests and the reader); null past the known lines.</summary>
    internal string? RowText(long row)
    {
        var (line, part) = At(row);
        return RowRendered(line, part)?.Text;
    }

    private int RowLength(long line, long part) => EditRendered(line, part)?.Text.Length ?? 0;

    private bool IsLastPart(JsonLine line, long part) => line.Number != _expanded || part + 1 >= JsonOutline.PartCount(line, PartBytes);

    // ---- places ----

    /// <summary>The pieces of a row that show document text (key, value, raw or comment text), in order.</summary>
    private static List<JsonSpan> Pieces(JsonRenderedLine r) => [.. r.Spans.Where(s => s.Kind != JsonTokenKind.Punctuation)];

    /// <summary>Where the bytes of a piece end in the document; a key or value cut by the screen goes on past its span.</summary>
    private static long PieceEnd(JsonLine l, JsonSpan s) => s.Kind == JsonTokenKind.Key ? l.KeyEnd : l.Kind == JsonLineKind.Value ? l.ValueEnd : s.End;

    /// <summary>The column after a piece and the ellipsis the screen draws when it cut the piece.</summary>
    private static int After(JsonRenderedLine r, JsonSpan s)
    {
        int end = s.Column + s.Length;
        return end < r.Text.Length && r.Text[end] == '…' && s.End < PieceEnd(r.Line, s) ? end + 1 : end;
    }

    /// <summary>The piece of an unwrapped row the screen cut right at <paramref name="column"/>, where its ellipsis starts; null when none is.</summary>
    private static JsonSpan? CutAt(JsonRenderedLine r, int column)
    {
        foreach (var s in Pieces(r))
            if (s.Column + s.Length == column && After(r, s) > column) return s;
        return null;
    }

    /// <summary>The place a column of a row shows.</summary>
    internal Pos PosOf(long line, long part, int column)
    {
        if (EditRendered(line, part) is not { } r) return new Pos(line, 0, 0);
        column = Math.Clamp(column, 0, r.Text.Length);
        var pieces = Pieces(r);
        if (pieces.Count == 0) return new Pos(line, 0, column);
        if (column < pieces[0].Column) return part == 0 ? new Pos(line, 0, column) : new Pos(line, 1, pieces[0].Source);
        for (int i = 0; i < pieces.Count; i++)
        {
            var s = pieces[i];
            if (column <= s.Column + s.Length) return new Pos(line, 1, _outline!.SourceOffsetAt(r, column));
            if (i + 1 < pieces.Count && column < pieces[i + 1].Column)
            {
                // between the key and the value: the bytes of the colon and the spaces stand for the columns of ": "
                long end = PieceEnd(r.Line, s);
                return new Pos(line, 1, Math.Clamp(end + column - After(r, s), end, Math.Max(end, pieces[i + 1].Source - 1)));
            }
        }
        var last = pieces[^1];
        if (!IsLastPart(r.Line, part)) return new Pos(line, 1, last.End);
        return new Pos(line, 2, column - After(r, last));
    }

    /// <summary>The column of row (<paramref name="line"/>, <paramref name="part"/>) where <paramref name="p"/> falls: 0 when it is before the row, the length when after.</summary>
    private int ColumnIn(Pos p, long line, long part, JsonRenderedLine r)
    {
        if (p.Line < line) return 0;
        if (p.Line > line) return r.Text.Length;
        var pieces = Pieces(r);
        if (pieces.Count == 0) return (int)Math.Clamp(p.At, 0, r.Text.Length);
        bool last = IsLastPart(r.Line, part);
        int tail = After(r, pieces[^1]);
        if (p.Region == 0) return part == 0 ? (int)Math.Min(p.At, pieces[0].Column) : 0;
        if (p.Region == 2) return last ? (int)Math.Min(r.Text.Length, tail + p.At) : r.Text.Length;
        if (p.At < pieces[0].Source) return 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            var s = pieces[i];
            if (p.At <= s.End) return Math.Max(s.Column, _outline!.ColumnOfOffset(r, p.At));
            if (i + 1 < pieces.Count && p.At < pieces[i + 1].Source)
            {
                int after = After(r, s);
                return after + (int)Math.Clamp(p.At - PieceEnd(r.Line, s), 0, Math.Max(0, pieces[i + 1].Column - after - 1));
            }
        }
        return last ? tail : r.Text.Length;
    }

    private Pos CaretPos => PosOf(_caretLine, _caretPart, _caretColumn);

    /// <summary>Puts the caret on <paramref name="p"/>, in the part of the wrapped value that shows it.</summary>
    private void PlaceCaret(Pos p, bool reveal = true)
    {
        long part = 0;
        if (p.Line == _expanded && LineAt(p.Line) is { } l)
            part = p.Region == 1 ? _outline!.PartOf(l, p.At, PartBytes) : p.Region == 2 ? JsonOutline.PartCount(l, PartBytes) - 1 : 0;
        if (RowRendered(p.Line, part) is not { } r) return;
        MoveCaret(p.Line, ColumnIn(p, p.Line, part, r), reveal, part);
    }

    /// <summary>Puts the caret on the document <paramref name="offset"/> inside a piece of the line; past the cut of a long value the value is wrapped first.</summary>
    private void CaretToOffset(long line, long offset)
    {
        if (_outline is null || LineAt(line) is not { } l) return;
        if (line != _expanded && JsonOutline.IsLong(l) && _outline.ColumnOfOffset(_outline.Render(l, Indent), offset) < 0) _expanded = line;
        long part = line == _expanded ? _outline.PartOf(l, offset, PartBytes) : 0;
        int column = EditRendered(line, part) is { } r ? _outline.ColumnOfOffset(r, offset) : -1;
        MoveCaret(line, column < 0 ? _caretColumn : column, part: part);
    }

    /// <summary>The document offset a place stands for; <paramref name="end"/> takes the comma and the space before the next line.</summary>
    private long SourceOf(Pos p, bool end)
    {
        if (_outline is null || LineAt(p.Line) is not { } l) return Doc.ByteLength;
        if (p.Region == 1) return p.At;
        if (p.Region == 2) return end ? l.End : l.Kind is JsonLineKind.Open or JsonLineKind.Empty ? l.ValueStart : l.ValueEnd;
        var r = _outline.Render(l, Indent);
        long at = _outline.SourceOffsetAt(r, (int)Math.Min(p.At, r.Text.Length));
        if (at >= 0) return at;
        if (r.Spans.Count == 0 || p.At <= r.Spans[0].Column) return l.HasKey ? l.KeyStart : l.ValueStart;
        return end ? l.End : l.ValueStart;
    }

    // ---- selection and clipboard ----

    /// <summary>The selected range in order, or null when nothing is selected.</summary>
    internal (Pos Start, Pos End)? Selection
    {
        get
        {
            if (_anchor is not { } a) return null;
            var c = CaretPos;
            if (a == c) return null;
            return a.CompareTo(c) < 0 ? (a, c) : (c, a);
        }
    }

    public bool HasSelection => Selection is not null;

    /// <summary>Selects from row <paramref name="fromLine"/> (part, column) to the caret placed at the second place; for tests and the reader.</summary>
    internal void Select(long fromLine, int fromColumn, long toLine, int toColumn, long fromPart = 0, long toPart = 0)
    {
        _anchor = PosOf(fromLine, fromPart, fromColumn);
        MoveCaret(toLine, toColumn, part: toPart);
    }

    /// <summary>Ctrl+A: selects the whole document when it is at most <see cref="CopyLimit"/> and scanned to the end.</summary>
    public bool SelectAll()
    {
        if (_outline is null) return false;
        if (Doc.ByteLength > CopyLimit) return Refuse(K.JsonPrettySelectAllTooLarge, ("limit", LimitText));
        if (!_outline.State.Complete) return Refuse(K.JsonPrettySelectAllScanning);
        _anchor = new Pos(0, 0, 0);
        MoveCaret(Math.Max(0, LineCount - 1), int.MaxValue, reveal: false, part: long.MaxValue);
        return true;
    }

    private string LimitText => CopyLimit >= 1 << 20 ? $"{CopyLimit >> 20} MiB" : $"{CopyLimit} B";

    /// <summary>
    /// The text the clipboard gets: the formatted text of the selection with the whole values even where the screen cuts them, or with
    /// <paramref name="source"/> the document text it came from. Null with a refusal when nothing is selected or the selection is over the limit.
    /// </summary>
    public string? CopyText(bool source)
    {
        if (_outline is null || Selection is not { } sel) return null;
        long from = SourceOf(sel.Start, end: false), to = SourceOf(sel.End, end: true);
        if (to - from > CopyLimit) { Refuse(K.JsonPrettyTooLarge, ("limit", LimitText)); return null; }
        if (source) return _outline.ReadText(from, to);
        var text = new StringBuilder();
        for (long n = sel.Start.Line; n <= sel.End.Line; n++)
        {
            if (LineAt(n) is not { } l) break;
            if (n > sel.Start.Line) text.Append('\n');
            AppendLine(text, l, n == sel.Start.Line ? sel.Start : null, n == sel.End.Line ? sel.End : null);
            if (text.Length > 4 * CopyLimit) { Refuse(K.JsonPrettyTooLarge, ("limit", LimitText)); return null; } // deep indents of tiny tokens
        }
        return text.ToString();
    }

    /// <summary>
    /// The selected part of a line with whole keys and values: the text before the first piece, each piece read from the document, and the text between
    /// and after the pieces as the screen shows it, so a piece of hundreds of MiB is read only as far as it is selected.
    /// </summary>
    private void AppendLine(StringBuilder text, JsonLine l, Pos? from, Pos? to)
    {
        var r = _outline!.Render(l, Indent);
        var pieces = Pieces(r);
        void Literal(int start, int end, int a, int b)
        {
            a = Math.Clamp(a, 0, end - start);
            b = Math.Clamp(b, 0, end - start);
            if (b > a) text.Append(r.Text, start + a, b - a);
        }
        if (pieces.Count == 0)
        {
            Literal(0, r.Text.Length, from is { } f ? (int)Math.Min(f.At, int.MaxValue) : 0, to is { } t ? (int)Math.Min(t.At, int.MaxValue) : int.MaxValue);
            return;
        }
        int Head(Pos? p, int none) => p is not { } q ? none : q.Region == 0 ? (int)Math.Min(q.At, int.MaxValue) : int.MaxValue;
        Literal(0, pieces[0].Column, Head(from, 0), Head(to, int.MaxValue));
        for (int i = 0; i < pieces.Count; i++)
        {
            var s = pieces[i];
            long end = PieceEnd(l, s);
            long Bytes(Pos? p, long none) => p is not { } q ? none : q.Region == 0 ? s.Source : q.Region == 1 ? Math.Clamp(q.At, s.Source, end) : end;
            long va = Bytes(from, s.Source), vb = Bytes(to, end);
            if (vb > va) text.Append(_outline.ReadText(va, vb));
            int after = After(r, s), next = i + 1 < pieces.Count ? pieces[i + 1].Column : r.Text.Length;
            int Gap(Pos? p, int none) => p is not { } q ? none
                : i + 1 == pieces.Count ? (q.Region == 2 ? (int)Math.Min(q.At, int.MaxValue) : 0)
                : q.Region == 0 || (q.Region == 1 && q.At <= end) ? 0 : q.Region == 1 && q.At < pieces[i + 1].Source ? (int)(q.At - end) : int.MaxValue;
            Literal(after, next, Gap(from, 0), Gap(to, int.MaxValue));
        }
    }

    /// <summary>The document bytes of a selection that lies inside one key, value, raw or comment piece: the only selections an edit may replace.</summary>
    private bool EditRange(out long a, out long b)
    {
        a = b = -1;
        if (_outline is null || Selection is not { } sel || sel.Start.Line != sel.End.Line || LineAt(sel.Start.Line) is not { } l) return false;
        if (sel.Start.Region != 1 || sel.End.Region != 1) return false;
        (a, b) = (sel.Start.At, sel.End.At);
        return (l.HasKey && a >= l.KeyStart && b <= l.KeyEnd) || (l.Kind is not (JsonLineKind.Open or JsonLineKind.Close or JsonLineKind.Empty) && a >= l.ValueStart && b <= l.ValueEnd);
    }

    /// <summary>True when the selection can be cut; otherwise says why.</summary>
    public bool CanCut() => HasSelection && CanEdit(out _) && (EditRange(out _, out _) || Refuse(K.JsonPrettyCutNotHere));

    /// <summary>Deletes the selection inside one key or value (the second half of a cut).</summary>
    public bool DeleteSelection() => EditRange(out long a, out long b) ? ReplaceBytes(a, b, []) : Refuse(K.JsonPrettyCutNotHere);

    /// <summary>Pastes text into the key or value under the caret, or over a selection inside one; a line break or another control character is refused.</summary>
    public bool Paste(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Any(char.IsControl)) return Refuse(K.JsonPrettyPasteControl);
        return TypeText(text);
    }

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
        if (!e.Shifted)
        {
            _folds.DropFrom(e.FirstLine);
            if (_expanded >= e.FirstLine) _expanded = -1;
        }
        long last = Math.Max(0, LineCount - 1);
        _caretLine = Math.Min(_caretLine, last);
        if (_caretLine != _expanded) _caretPart = 0;
        _caretPart = Math.Min(_caretPart, ExtraRows);
        _caretColumn = Math.Min(_caretColumn, RowLength(_caretLine, _caretPart));
        _mark = null;
        _anchor = null;
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
            long at = EditRendered(_caretLine, _caretPart) is { } r ? _outline.SourceOffsetAt(r, _caretColumn) : -1;
            return at >= 0 ? at : JsonOutline.SourceOffsetOf(line);
        }
    }

    public void MoveCaret(long line, int column, bool reveal = true, long part = 0)
    {
        long last = Math.Max(0, LineCount - 1);
        long row = RowOf(Math.Clamp(line, 0, last), part);
        (_caretLine, _caretPart) = At(row);
        _caretColumn = Math.Clamp(column, 0, RowLength(_caretLine, _caretPart));
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
        long row = Math.Clamp(RowOf(_caretLine, _caretPart) + delta, 0, Math.Max(0, RowCount - 1));
        var (line, part) = At(row);
        MoveCaret(line, _caretColumn, part: part);
    }

    /// <summary>The column where the value piece of the caret row starts: on a continuation row of a wrapped value the caret does not go into the indent.</summary>
    private int RowStart(long line, long part) => part > 0 && EditRendered(line, part) is { Spans.Count: > 0 } r ? r.Spans[0].Column : 0;

    private void StepRight()
    {
        if (_caretColumn < RowLength(_caretLine, _caretPart))
        {
            if (_caretLine != _expanded && EditRendered(_caretLine, 0) is { } r && CutAt(r, _caretColumn) is { } cut)
            {
                // at the cut of a long key or value: wrap the line and go on inside
                _expanded = _caretLine;
                CaretToOffset(_caretLine, cut.End);
            }
            MoveCaret(_caretLine, _caretColumn + 1, part: _caretPart);
            return;
        }
        MoveRows(1);
        MoveCaret(_caretLine, RowStart(_caretLine, _caretPart) + (_caretPart > 0 ? 1 : 0), part: _caretPart);
    }

    private void StepLeft()
    {
        if (_caretColumn > RowStart(_caretLine, _caretPart))
        {
            MoveCaret(_caretLine, _caretColumn - 1, part: _caretPart);
            return;
        }
        // the start of a continuation row is the end of the row above: step over that character as well
        long from = _caretPart;
        MoveRows(-1);
        int end = RowLength(_caretLine, _caretPart);
        MoveCaret(_caretLine, from > 0 ? end - 1 : end, part: _caretPart);
    }

    /// <summary>Puts the caret on the line that shows the document offset, expanding the containers that hide it, and marks the range.</summary>
    public void RevealOffset(long start, long end)
    {
        if (_outline is null) return;
        long line = _outline.LineOfOffset(start);
        while (UnfoldAround(line)) { }
        _mark = end > start ? new ByteRange(start, end) : null;
        _anchor = null;
        if (LineAt(line) is { } l && EditRendered(line, 0) is { } r && r.Spans.Any(s => s.Kind != JsonTokenKind.Punctuation && s.Source <= start))
        {
            if (JsonOutline.IsLong(l) && start >= JsonOutline.SourceOffsetOf(l)) CaretToOffset(line, Math.Min(start, l.Kind == JsonLineKind.Value ? l.ValueEnd : l.KeyEnd));
            else
            {
                int column = _outline.ColumnOfOffset(r, start);
                var last = r.Spans.Last(s => s.Kind != JsonTokenKind.Punctuation && s.Source <= start);
                MoveCaret(line, column >= 0 ? column : last.Column + last.Length);
            }
        }
        else MoveCaret(line, 0);
        _top = Math.Max(0, RowOf(_caretLine, _caretPart) - VisibleRows / 3);
        Refresh();
    }

    private bool UnfoldAround(long line)
    {
        long open = _folds.LineOfRow(_folds.RowOfLine(line));
        return open != line && _folds.Unfold(open);
    }

    // ---- folds and long values ----

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
        if (_expanded > open && _expanded <= close) _expanded = -1;
        _anchor = null;
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
        _caretPart = 0;
        return Fold();
    }

    /// <summary>
    /// Wraps the long key or value on the caret line over rows as wide as the view, so all of it can be read and edited, or unwraps it again; the caret
    /// keeps its place. One line is wrapped at a time. A line whose key and value are at most <see cref="JsonOutline.MaxShownBytes"/> has nothing to wrap.
    /// </summary>
    public bool ToggleExpand()
    {
        if (_outline is null || LineAt(_caretLine) is not { } line) return false;
        var caret = CaretPos;
        if (_expanded == _caretLine) _expanded = -1;
        else if (JsonOutline.IsLong(line)) _expanded = _caretLine;
        else return Refuse(K.JsonPrettyNotLong);
        _anchor = null;
        PlaceCaret(caret);
        return true;
    }

    // ---- editing ----

    public void UndoRedo(bool redo)
    {
        if (redo ? Doc.RedoBlockedBySave : Doc.UndoBlockedBySave) { Refused?.Invoke(Loc.Get(K.HexBusy)); return; }
        _ = redo ? Doc.Redo() : Doc.Undo();
    }

    /// <summary>Types into the key or value under the caret, or over a selection inside one; the bytes go into the document at the place the caret shows.</summary>
    public bool TypeText(string text)
    {
        if (!CanEdit(out var rendered)) return false;
        long a, b;
        if (HasSelection)
        {
            if (!EditRange(out a, out b)) return Refuse(K.JsonPrettyNotHere);
        }
        else
        {
            a = b = _outline!.SourceOffsetAt(rendered!, _caretColumn);
            if (a < 0) return Refuse(K.JsonPrettyNotHere);
        }
        byte[] bytes;
        try { bytes = Doc.Codec.Encode(text); }
        catch (UnrepresentableCharactersException) { return Refuse(K.HexNotEncodable, ("encoding", Doc.Codec.Name)); }
        return ReplaceBytes(a, b, bytes);
    }

    /// <summary>Deletes the selection inside one key or value, or the character before (Backspace) or after (Delete) the caret.</summary>
    public bool DeleteChar(bool forward)
    {
        if (!CanEdit(out var rendered)) return false;
        if (HasSelection) return EditRange(out long sa, out long sb) ? ReplaceBytes(sa, sb, []) : Refuse(K.JsonPrettyNotHere);
        int from = forward ? _caretColumn : _caretColumn - 1, to = forward ? _caretColumn + 1 : _caretColumn;
        if (from >= 0 && from < rendered!.Text.Length && char.IsLowSurrogate(rendered.Text[from])) from--;
        if (to < rendered!.Text.Length && to > 0 && char.IsLowSurrogate(rendered.Text[to])) to++;
        if (from < 0 || rendered.SpanAt(from) is not { } span || to > span.Column + span.Length || from < span.Column) return Refuse(K.JsonPrettyNotHere);
        long a = _outline!.SourceOffsetAt(rendered, from), b = _outline.SourceOffsetAt(rendered, to);
        if (a < 0 || b <= a) return Refuse(K.JsonPrettyNotHere);
        return ReplaceBytes(a, b, []);
    }

    /// <summary>Replaces the document bytes from <paramref name="a"/> to <paramref name="b"/> in one undo step that merges with typing, and puts the caret after them.</summary>
    private bool ReplaceBytes(long a, long b, byte[] bytes)
    {
        long line = _caretLine;
        var options = new EditOptions { CoalesceKey = "json.pretty.typing", Source = "user" };
        var result = b > a ? (bytes.Length > 0 ? Doc.Replace(new ByteRange(a, b), bytes, options) : Doc.Delete(new ByteRange(a, b), options)) : Doc.Insert(a, bytes, options);
        if (!result.Applied) return Refuse(K.HexRejected);
        _anchor = null;
        CaretToOffset(line, a + bytes.Length);
        return true;
    }

    private bool CanEdit(out JsonRenderedLine? rendered)
    {
        rendered = null;
        if (_outline is null || LineAt(_caretLine) is null) return false;
        if (_od.ReadOnly) return Refuse(K.HexReadOnly);
        if (Doc.IsSaving && !Doc.EditsDuringSave) return Refuse(K.HexBusy);
        rendered = EditRendered(_caretLine, _caretPart);
        return rendered is not null;
    }

    private bool Refuse(string key, params (string Name, object Value)[] args)
    {
        Refused?.Invoke(Loc.Get(key, args));
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
        _anchor = null;
        var rendered = LineAt(found) is { } l ? outline.Render(l, Indent) : null;
        MoveCaret(found, rendered?.Spans.FirstOrDefault(s => s.Kind == JsonTokenKind.Key).Column ?? 0);
        _top = Math.Max(0, RowOf(found) - VisibleRows / 3);
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
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Left or Key.Right)
        {
            if (shift) _anchor ??= CaretPos;
            else _anchor = null;
        }
        switch (e.Key)
        {
            case Key.Up: MoveRows(-1); break;
            case Key.Down: MoveRows(1); break;
            case Key.PageUp: MoveRows(-page); break;
            case Key.PageDown: MoveRows(page); break;
            case Key.Home when command: MoveCaret(0, 0); break;
            case Key.End when command: MoveCaret(LineCount - 1, int.MaxValue, part: long.MaxValue); break;
            case Key.Home:
            {
                int indent = _caretPart > 0 ? RowStart(_caretLine, _caretPart) : (LineAt(_caretLine)?.Depth ?? 0) * Indent.Length;
                MoveCaret(_caretLine, _caretColumn == indent && _caretPart == 0 ? 0 : indent, part: _caretPart);
                break;
            }
            case Key.End:
            {
                // the end of a long value is past its cut: the value is wrapped and the caret goes to its last row
                if (_caretLine != _expanded && LineAt(_caretLine) is { } l && JsonOutline.IsLong(l)) _expanded = _caretLine;
                MoveCaret(_caretLine, int.MaxValue, part: _caretLine == _expanded ? long.MaxValue : 0);
                break;
            }
            case Key.Left: StepLeft(); break;
            case Key.Right: StepRight(); break;
            case Key.OemOpenBrackets when command && shift: Fold(); break;
            case Key.OemCloseBrackets when command && shift: Unfold(); break;
            case Key.Back when !command: DeleteChar(forward: false); break;
            case Key.Delete when !command: DeleteChar(forward: true); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    private (long Line, long Part, int Column) HitTest(Point p)
    {
        long row = Math.Clamp(_top + (long)Math.Floor(p.Y / _canvas.LineHeight), 0, Math.Max(0, RowCount - 1));
        var (line, part) = At(row);
        int column = RowRendered(line, part) is { } r ? _canvas.IndexAtX(r.Text, p.X - _canvas.TextLeft + _hScroll) : 0;
        return (line, part, column);
    }

    private void Pressed(Point p, bool gutter, bool extend)
    {
        Focus();
        var (line, part, column) = HitTest(p);
        if (gutter)
        {
            if (part == 0 && LineAt(line) is { Kind: JsonLineKind.Open }) ToggleFold(line);
            return;
        }
        if (extend) _anchor ??= CaretPos;
        else _anchor = null;
        if (line != _expanded && column > 0 && EditRendered(line, 0) is { } r && CutAt(r, column - 1) is { } cut)
        {
            // a click on the ellipsis of a cut key or value wraps the line
            _expanded = line;
            CaretToOffset(line, cut.End);
        }
        else MoveCaret(line, column, part: part);
        if (!extend) _anchor = CaretPos;
    }

    private void Dragged(Point p)
    {
        var (line, part, column) = HitTest(p);
        _anchor ??= CaretPos;
        MoveCaret(line, column, part: part);
    }

    // ---- drawing ----

    private sealed class Surface(JsonPrettyView owner) : Control
    {
        private const double Pad = 6;
        private double _lineHeight = 18, _charWidth = 8, _gutter = 40;
        private bool _dragging;

        public int VisibleRows => Bounds.Height < 1 ? 30 : (int)Math.Ceiling(Bounds.Height / _lineHeight);
        public double LineHeight => _lineHeight;
        public double CharWidth => _charWidth;
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
            if (owner.RowRendered(owner._caretLine, owner._caretPart) is not { } r || TextWidth <= 0) return;
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
            owner.UpdatePartBytes();
            owner.UpdateScrollRange();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            owner.Pressed(p, p.X < _gutter, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _dragging = p.X >= _gutter;
            if (_dragging) e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            owner.Dragged(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
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
            JsonTokenKind.Comment => "comment",
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
            var selected = owner.Selection;
            double widest = 0;
            for (int i = 0; i < VisibleRows; i++)
            {
                var (line, part) = owner.At(owner._top + i);
                if (owner.RowRendered(line, part) is not { } r) break;
                double y = i * _lineHeight;
                bool caretLine = line == owner._caretLine, caretHere = caretLine && part == owner._caretPart;
                if (caretHere && owner._options.CurrentLine) context.FillRectangle(current, new Rect(0, y, Bounds.Width, _lineHeight));
                if (owner._options.LineNumbers && part == 0)
                {
                    var number = Format((line + 1).ToString(CultureInfo.InvariantCulture), caretLine ? accent : numbers);
                    context.DrawText(number, new Point(_gutter - 2 * _charWidth - number.Width, y));
                }
                if (part == 0 && r.Line.Kind == JsonLineKind.Open)
                    context.DrawText(Format(owner._folds.IsFolded(line) ? "▸" : "▾", numbers), new Point(_gutter - 1.5 * _charWidth, y));
                using (context.PushClip(new Rect(_gutter, y, Math.Max(0, Bounds.Width - _gutter), _lineHeight)))
                {
                    double left = TextLeft - owner._hScroll;
                    void Fill(int a, int b)
                    {
                        if (b > a) context.FillRectangle(selection, new Rect(left + XOf(r.Text, a), y, XOf(r.Text, b) - XOf(r.Text, a), _lineHeight));
                    }
                    if (selected is { } sel && line >= sel.Start.Line && line <= sel.End.Line)
                        Fill(owner.ColumnIn(sel.Start, line, part, r), owner.ColumnIn(sel.End, line, part, r));
                    if (owner._mark is { } mark)
                        foreach (var span in r.Spans)
                        {
                            long a = Math.Max(mark.Start, span.Source), b = Math.Min(mark.End, span.End);
                            if (span.Kind != JsonTokenKind.Punctuation && b > a) Fill(outline.ColumnOfOffset(r, a), outline.ColumnOfOffset(r, b));
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
