using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Editing;

/// <summary>How a byte of the hex grid is described for a screen reader and the status line.</summary>
internal readonly record struct HexByteInfo(long Offset, bool AtEnd, byte Value, string Character, HexCellKind Kind, bool Changed);

/// <summary>
/// Hex view of a document: offset, hex and character columns over the same <see cref="Document"/> the text view uses, so one revision, one undo history
/// and one save serve both. It reads only the rows on screen, so the size of the file does not matter. The caret and the edits are those of a
/// <see cref="HexEditor"/>; this control turns keys, the mouse and the clipboard into calls on it and draws the result.
/// </summary>
internal sealed partial class HexView : Grid
{
    private static readonly DataFormat<byte[]> BytesFormat = DataFormat.CreateBytesApplicationFormat("textida-bytes");
    private static readonly int[] RowSizes = [8, 16, 24, 32, 48, 64];

    private readonly OpenDocument _od;
    private readonly HexSurface _canvas;
    private readonly ScrollBar _bar = new() { Orientation = Avalonia.Layout.Orientation.Vertical, Minimum = 0, SmallChange = 1, AllowAutoHide = false };
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private EditorOptions _options;
    private long _firstRow;
    private bool _syncingBar;
    private IReadOnlyList<ByteRange> _changed = [];
    private long _changedRevision = -1, _changedBase = -1;
    private int _bytesPerRow = LastBytesPerRow;
    private int _group = 1;
    private bool _decimalOffsets;
    private bool _dragging;
    private bool _revealWhenSized;

    /// <summary>Choices made in one hex view are the starting point of the next one.</summary>
    internal static int LastBytesPerRow { get; private set; } = 16;
    private static HexCharset? _lastCharset;

    public HexView(OpenDocument doc, EditorOptions options)
    {
        _od = doc;
        _options = options;
        Editor = new HexEditor(doc.Doc, _lastCharset ?? HexCharset.For(doc.Doc.Codec));
        Focusable = true;
        ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto");
        _canvas = new HexSurface(this);
        Inspector = new HexInspectorPanel(this) { IsVisible = false };
        BookmarkList = new HexBookmarksPanel(this) { IsVisible = false };
        Statistics = new HexStatsPanel(this) { IsVisible = false };
        Minimap = new HexMinimap(this) { IsVisible = LastMinimap };
        SetColumn(Minimap, 1);
        SetColumn(_bar, 2);
        SetColumn(BookmarkList, 3);
        SetColumn(Statistics, 4);
        SetColumn(Inspector, 5);
        Children.Add(_canvas);
        Children.Add(Minimap);
        Children.Add(_bar);
        Children.Add(BookmarkList);
        Children.Add(Statistics);
        Children.Add(Inspector);
        _bar.ValueChanged += (_, _) =>
        {
            if (_syncingBar) return;
            _firstRow = (long)_bar.Value;
            _canvas.InvalidateVisual();
            RaiseViewChanged();
        };
        _watch.Tick += (_, _) => Dispatcher.UIThread.Post(CheckFile);
    }

    public HexEditor Editor { get; }
    public HexInspectorPanel Inspector { get; }
    /// <summary>The map of the whole file beside the scroll bar.</summary>
    internal HexMinimap Minimap { get; }

    /// <summary>The view only shows: no key, paste, cut or undo changes the bytes (the grids of a comparison).</summary>
    internal bool ReadOnly { get; set; }

    /// <summary>Stretches to tint as differences from another file, ascending; empty outside a comparison.</summary>
    internal IReadOnlyList<ByteRange> DiffMarks { get; private set; } = [];

    /// <summary>The theme brush the differences are tinted with (<c>DiffRemoved</c> on the left, <c>DiffAdded</c> on the right).</summary>
    internal string DiffBrushKey { get; private set; } = "DiffRemoved";

    internal void ShowDifferences(IReadOnlyList<ByteRange> ranges, string brushKey)
    {
        DiffMarks = ranges;
        DiffBrushKey = brushKey;
        _canvas.InvalidateVisual();
    }
    /// <summary>The histogram and the figures of the selection.</summary>
    internal HexStatsPanel Statistics { get; }
    /// <summary>The map is shown in the next hex view too once it was switched on or off in one.</summary>
    internal static bool LastMinimap { get; private set; } = true;

    /// <summary>Shows or hides the map; the choice is the starting point of the next hex view.</summary>
    internal void SetMinimapVisible(bool visible)
    {
        Minimap.IsVisible = LastMinimap = visible;
        if (visible) Minimap.Rebuild();
    }

    /// <summary>The list of bookmarks beside the grid.</summary>
    public HexBookmarksPanel BookmarkList { get; }
    /// <summary>The regions marked in this document.</summary>
    internal HexBookmarks Marks => _od.HexBookmarks;
    private Document Doc => _od.Doc;

    /// <summary>Raised when the caret, the selection, the mode or the first row changed.</summary>
    public event Action? ViewChanged;

    /// <summary>Raised with a localised sentence when an edit was refused or something else needs saying.</summary>
    public event Action<string>? Refused;

    /// <summary>The background run (checksum, export) that Esc cancels; null when none runs.</summary>
    internal CancellationTokenSource? Job { get; set; }

    /// <summary>Asks the user to pick one of the buttons (the last one cancels); set by the window. Returns the index or -1.</summary>
    public Func<string, string, IReadOnlyList<string>, Task<int>>? Choose { get; set; }

    public int BytesPerRow => _bytesPerRow;
    public int Group => _group;
    public bool DecimalOffsets => _decimalOffsets;
    public HexCharset Charset => Editor.Charset;
    public long Caret => Editor.Caret;
    public bool Insert => Editor.Insert;
    public long FirstRow => _firstRow;
    public int VisibleRows => Math.Max(1, _canvas.VisibleRows);
    internal long RenderedAt => _canvas.RenderedAt;
    internal double RenderMs => _canvas.RenderMs;
    internal void Redraw() => _canvas.InvalidateVisual();

    internal Document Document => Doc;

    /// <summary>Raised after the document changed or was saved; the minimap builds its map again.</summary>
    internal event Action? DocumentChanged;

    /// <summary>Offset of the first byte in the grid and the number of bytes the grid shows.</summary>
    internal long FirstByte => _firstRow * _bytesPerRow;
    internal long VisibleBytes => (long)VisibleRows * _bytesPerRow;

    /// <summary>Scrolls so that the row of <paramref name="offset"/> is the first one; the caret stays where it is.</summary>
    internal void ScrollToOffset(long offset)
    {
        long row = Math.Max(0, offset) / _bytesPerRow;
        _firstRow = Math.Clamp(row, 0, (long)_bar.Maximum);
        UpdateScrollRange();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    private long TotalRows => Math.Max(1, (Doc.ByteLength + _bytesPerRow) / _bytesPerRow);

    protected override AutomationPeer OnCreateAutomationPeer() => new HexAutomationPeer(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Doc.Changed += OnDocChanged;
        Doc.Saved += OnSaved;
        Marks.Changed += OnMarksChanged;
        _watch.Start();
        UpdateScrollRange();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Doc.Changed -= OnDocChanged;
        Doc.Saved -= OnSaved;
        Marks.Changed -= OnMarksChanged;
        _watch.Stop();
    }

    private void OnDocChanged(object? sender, DocumentChangedEventArgs e)
    {
        Editor.Clamp();
        UpdateScrollRange();
        _canvas.InvalidateVisual();
        Inspector.Refresh();
        RaiseViewChanged();
        DocumentChanged?.Invoke();
    }

    private void OnMarksChanged()
    {
        _canvas.InvalidateVisual();
        BookmarkList.Refresh();
        RaiseViewChanged();
    }

    private void OnSaved(object? sender, EventArgs e)
    {
        _canvas.InvalidateVisual();
        RaiseViewChanged();
        DocumentChanged?.Invoke();
    }

    private void RaiseViewChanged()
    {
        ViewChanged?.Invoke();
        (Avalonia.Automation.Peers.ControlAutomationPeer.FromElement(this) as HexAutomationPeer)?.CaretMoved();
    }

    /// <summary>A snapshot read that found the file changed behind the view ends here: the view stops showing it and the document's own check reports it.</summary>
    internal void FileChangedBehind()
    {
        Dispatcher.UIThread.Post(CheckFile);
    }

    private int _checking;

    /// <summary>
    /// The safety net behind the file watcher. A file whose size and time are as recorded is left alone, and the check itself hashes the whole file,
    /// so it runs on a worker, one at a time: on the UI thread a large file froze the window for seconds on every tick.
    /// </summary>
    private void CheckFile()
    {
        var doc = Doc;
        if (doc.FilePath is { } path && doc.Fingerprint is { } print && new FileInfo(path) is { Exists: true } info
            && info.Length == print.Size && info.LastWriteTimeUtc == print.LastWriteUtc) return;
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        Task.Run(() =>
        {
            try { doc.CheckExternalChange(); }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            finally { Volatile.Write(ref _checking, 0); }
        });
    }

    public void ApplyOptions(EditorOptions o)
    {
        _options = o;
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
    }

    internal EditorOptions Options => _options;

    // ---- layout choices ----

    public void SetBytesPerRow(int count)
    {
        if (!RowSizes.Contains(count) || count == _bytesPerRow) return;
        long caretRow = Editor.Caret / _bytesPerRow;
        _firstRow = _firstRow * _bytesPerRow / count;
        _bytesPerRow = LastBytesPerRow = count;
        _group = _group > 1 && count % _group != 0 ? 1 : _group;
        UpdateScrollRange();
        Reveal(Editor.Caret);
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    public void SetCharset(HexCharset charset)
    {
        Editor.Charset = _lastCharset = charset;
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    public void SetGroup(int group)
    {
        if (group is not (1 or 2 or 4 or 8) || _bytesPerRow % group != 0) return;
        _group = group;
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    public void ToggleOffsetFormat()
    {
        _decimalOffsets = !_decimalOffsets;
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    public void ToggleInsert()
    {
        Editor.Insert = !Editor.Insert;
        if (!Editor.Insert) Editor.MoveTo(Math.Min(Editor.Caret, Math.Max(0, Doc.ByteLength - 1)), extend: Editor.HasSelection);
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    public void ToggleColumn()
    {
        Editor.Column = Editor.Column == HexColumn.Hex ? HexColumn.Text : HexColumn.Hex;
        Editor.BreakRun();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    internal static IReadOnlyList<int> Sizes => RowSizes;

    // ---- moving ----

    private void UpdateScrollRange()
    {
        long total = TotalRows;
        int visible = VisibleRows;
        _syncingBar = true;
        _bar.Maximum = Math.Max(0, total - visible);
        _bar.ViewportSize = visible;
        _bar.LargeChange = Math.Max(1, visible - 1);
        _firstRow = Math.Clamp(_firstRow, 0, (long)_bar.Maximum);
        _bar.Value = _firstRow;
        _syncingBar = false;
        // a jump asked for before the view had a size is shown once it has one
        if (_revealWhenSized && _canvas.Bounds.Height > 40)
        {
            _revealWhenSized = false;
            CenterIfOutside(Editor.Caret);
        }
    }

    private bool Sized => _canvas.Bounds.Height > 40;

    /// <summary>Scrolls so the byte is on screen: the nearest row, not a jump to the middle.</summary>
    public void Reveal(long offset)
    {
        if (!Sized)
        {
            _revealWhenSized = true;
            return;
        }
        long row = offset / _bytesPerRow;
        int visible = VisibleRows;
        if (row < _firstRow) _firstRow = row;
        else if (row >= _firstRow + visible) _firstRow = row - visible + 1;
        else return;
        UpdateScrollRange();
        _canvas.InvalidateVisual();
    }

    /// <summary>Puts the caret on a byte (and selects up to it with <paramref name="extend"/>), scrolling it into view.</summary>
    public void MoveTo(long offset, bool extend = false)
    {
        Editor.MoveTo(offset, extend);
        Reveal(Editor.Caret);
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>Selects a range with the caret at its end and shows it.</summary>
    public void Select(long anchor, long caret)
    {
        Editor.Select(anchor, caret);
        Reveal(Editor.Caret);
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>Scrolls to the byte only when its row is off screen, then puts it in the middle: a jump to a far place.</summary>
    public void CenterIfOutside(long offset)
    {
        if (!Sized)
        {
            _revealWhenSized = true;
            return;
        }
        long row = offset / _bytesPerRow;
        if (row < _firstRow || row >= _firstRow + VisibleRows) Center(offset);
    }

    /// <summary>Shows a byte near the middle of the screen, for a jump to a far place.</summary>
    public void Center(long offset)
    {
        _firstRow = Math.Max(0, offset / _bytesPerRow - VisibleRows / 2);
        UpdateScrollRange();
        _canvas.InvalidateVisual();
    }

    private int GroupStep => _group > 1 ? _group : 4;

    internal long LastIndex => Insert ? Doc.ByteLength : Math.Max(0, Doc.ByteLength - 1);

    private void Step(long delta, bool extend)
    {
        long target = Math.Clamp(Editor.Caret + delta, 0, extend ? Doc.ByteLength : LastIndex);
        MoveTo(target, extend);
    }

    // ---- editing ----

    private void Report(HexResult result)
    {
        if (result.Applied)
        {
            Reveal(Editor.Caret);
            _canvas.InvalidateVisual();
            RaiseViewChanged();
            return;
        }
        switch (result.Issue)
        {
            case HexIssue.EndOfFile: Refused?.Invoke(Loc.Get(K.HexEndOfFile)); break;
            case HexIssue.NotEncodable: Refused?.Invoke(Loc.Get(K.HexNotEncodable, ("encoding", Editor.Charset.Name))); break;
            case HexIssue.Busy: Refused?.Invoke(Loc.Get(K.HexBusy)); break;
            case HexIssue.Rejected: Refused?.Invoke(Loc.Get(K.HexRejected)); break;
            case HexIssue.TooLarge: Refused?.Invoke(Loc.Get(K.HexTooLarge)); break;
        }
    }

    internal void Do(HexResult result) => Report(result);

    public void TypeNibble(int digit) { if (!ReadOnly) Report(Editor.TypeNibble(digit)); }

    public void TypeText(string text) { if (!ReadOnly) Report(Editor.TypeText(text)); }

    public async Task DeleteAsync(bool backspace)
    {
        if (ReadOnly) return;
        var result = backspace ? Editor.Backspace(DeleteMode.Ask) : Editor.Delete(DeleteMode.Ask);
        if (result is { Applied: false, Issue: HexIssue.NeedsChoice })
        {
            long count = Editor.HasSelection ? Editor.Selection.Length : 1;
            int answer = Choose is null ? -1 : await Choose(Loc.Get(K.HexDeleteTitle), Loc.Get(K.HexDeleteMessage, ("bytes", count)),
                [Loc.Get(K.HexDeleteZero), Loc.Get(K.HexDeleteRemove), Loc.Get(K.DialogCancel)]);
            if (answer is not (0 or 1)) return;
            result = backspace ? Editor.Backspace(answer == 0 ? DeleteMode.Zero : DeleteMode.Remove) : Editor.Delete(answer == 0 ? DeleteMode.Zero : DeleteMode.Remove);
        }
        Report(result);
    }

    public void UndoRedo(bool redo)
    {
        if (ReadOnly) return;
        if (redo ? Doc.RedoBlockedBySave : Doc.UndoBlockedBySave) { Refused?.Invoke(Loc.Get(K.HexBusy)); return; }
        if (!(redo ? Editor.Redo() : Editor.Undo())) return;
        Reveal(Editor.Caret);
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    public void SelectAll()
    {
        Editor.SelectAll();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    // ---- clipboard ----

    /// <summary>Copies the selected bytes as hex text and in a form of their own, so a paste into another hex view is exact.</summary>
    public async Task CopyAsync(bool cut)
    {
        if (!Editor.HasSelection || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        byte[] bytes = Editor.SelectedBytes();
        var item = DataTransferItem.CreateText(HexEditor.ToHexText(bytes));
        item.Set(BytesFormat, bytes);
        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
        if (cut) await DeleteAsync(backspace: false); // does nothing in a read-only view
    }

    /// <summary>Pastes bytes: the exact bytes of another hex view, else hex text, else (in the character column) text in the charset.</summary>
    public async Task PasteAsync()
    {
        if (ReadOnly) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        byte[]? bytes = null;
        if (await clipboard.TryGetDataAsync() is { } data)
        {
            foreach (var item in data.Items)
                if (item.Formats.Contains(BytesFormat) && await item.TryGetRawAsync(BytesFormat) is byte[] raw) { bytes = raw; break; }
        }
        if (bytes is null)
        {
            string? text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrEmpty(text)) { Refused?.Invoke(Loc.Get(K.HexPasteEmpty)); return; }
            if (Editor.Column == HexColumn.Hex && HexEditor.TryParseHexText(text, out var parsed)) bytes = parsed;
            else
            {
                try { bytes = Editor.Charset.Codec.Encode(text); }
                catch (NksHub.Textida.Core.Encodings.UnrepresentableCharactersException) { Refused?.Invoke(Loc.Get(K.HexNotEncodable, ("encoding", Editor.Charset.Name))); return; }
            }
        }
        await PasteBytesAsync(bytes);
    }

    /// <summary>Pastes bytes already made from the clipboard (or from a form of it); a block longer than the rest of the file asks what to do.</summary>
    public async Task PasteBytesAsync(byte[] bytes)
    {
        if (ReadOnly) return;
        var result = Editor.Paste(bytes);
        if (result is { Applied: false, Issue: HexIssue.NeedsChoice })
        {
            long rest = Doc.ByteLength - (Editor.HasSelection ? Editor.Selection.Start : Editor.Caret);
            int answer = Choose is null ? -1 : await Choose(Loc.Get(K.HexPasteLongerTitle), Loc.Get(K.HexPasteLongerMessage, ("bytes", bytes.Length), ("rest", rest)),
                [Loc.Get(K.HexPasteExtend), Loc.Get(K.HexPasteTruncate), Loc.Get(K.DialogCancel)]);
            if (answer is not (0 or 1)) return;
            result = Editor.Paste(bytes, answer == 0 ? PasteLonger.Extend : PasteLonger.Truncate);
        }
        Report(result);
    }

    // ---- description for the status line and for screen readers ----

    /// <summary>The byte under the caret.</summary>
    public HexByteInfo Describe(long offset)
    {
        long length = Doc.ByteLength;
        if (offset >= length) return new(offset, true, 0, "", HexCellKind.Normal, false);
        long start = Math.Max(0, offset - Editor.Charset.Lookbehind);
        var buf = new byte[(int)Math.Min(length - start, Editor.Charset.Lookbehind + 1 + Editor.Charset.Lookahead)];
        Doc.Read(start, buf);
        var cells = new HexCell[1];
        Editor.Charset.Map(buf, (int)(offset - start), 1, start, cells);
        return new(offset, false, buf[(int)(offset - start)], cells[0].Glyph, cells[0].Kind, IsChanged(offset));
    }

    internal bool IsChanged(long offset)
    {
        var ranges = ChangedRanges();
        int lo = 0, hi = ranges.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (offset < ranges[mid].Start) hi = mid - 1;
            else if (offset >= ranges[mid].End) lo = mid + 1;
            else return true;
        }
        return false;
    }

    /// <summary>Ranges changed since the last save (a deletion leaves none); empty when the change log no longer reaches back that far.</summary>
    internal IReadOnlyList<ByteRange> ChangedRanges()
    {
        if (_changedRevision != Doc.Revision || _changedBase != Doc.SavedRevision)
        {
            _changed = Doc.ChangedRangesSince(Doc.SavedRevision)?.Where(r => !r.IsEmpty).ToList() ?? [];
            _changedRevision = Doc.Revision;
            _changedBase = Doc.SavedRevision;
        }
        return _changed;
    }

    internal string OffsetText(long offset) => "0x" + offset.ToString("X8", CultureInfo.InvariantCulture);

    // ---- input ----

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Text is not { Length: > 0 } text || text.Any(char.IsControl)) return;
        e.Handled = true;
        if (Editor.Column == HexColumn.Text)
        {
            TypeText(text);
            return;
        }
        foreach (char c in text)
        {
            int v = c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
            if (v >= 0) TypeNibble(v);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        int page = Math.Max(1, VisibleRows - 1) * _bytesPerRow;
        switch (e.Key)
        {
            case Key.Left: Step(command ? -GroupStep : -1, shift); break;
            case Key.Right: Step(command ? GroupStep : 1, shift); break;
            case Key.Up: Step(-_bytesPerRow, shift); break;
            case Key.Down: Step(_bytesPerRow, shift); break;
            case Key.PageUp: Step(-page, shift); break;
            case Key.PageDown: Step(page, shift); break;
            case Key.Home when command: MoveTo(0, shift); break;
            case Key.End when command: MoveTo(shift ? Doc.ByteLength : LastIndex, shift); break;
            case Key.Home: MoveTo(Editor.Caret / _bytesPerRow * _bytesPerRow, shift); break;
            case Key.End: MoveTo(Math.Min(Editor.Caret / _bytesPerRow * _bytesPerRow + _bytesPerRow - 1, shift ? Doc.ByteLength : LastIndex), shift); break;
            case Key.Tab when !command: ToggleColumn(); break;
            case Key.Back when !command: _ = DeleteAsync(backspace: true); break;
            case Key.Delete when !command: _ = DeleteAsync(backspace: false); break;
            case Key.Escape:
                if (Doc.IsSaving) Doc.CancelSave();
                else if (Job is { } job) job.Cancel();
                else if (Editor.HasSelection) MoveTo(Editor.Caret);
                else return;
                break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _firstRow = Math.Clamp(_firstRow - (long)Math.Round(e.Delta.Y * 3), 0, (long)_bar.Maximum);
        UpdateScrollRange();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
        e.Handled = true;
    }

    internal void PointerPressed(Point p, bool extend)
    {
        Focus();
        if (_canvas.HitTest(p) is not { } hit) return;
        Editor.Column = hit.Column;
        _dragging = true;
        MoveTo(hit.Offset, extend);
    }

    internal void PointerMoved(Point p)
    {
        if (_dragging && _canvas.HitTest(p) is { } hit) MoveTo(hit.Offset, extend: true);
    }

    internal void PointerReleased() => _dragging = false;
}
