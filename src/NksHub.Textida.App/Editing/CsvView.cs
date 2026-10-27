using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Csv;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Editing;

/// <summary>Cells of the table view that are selected: data rows (-1 is the header row) and columns, both ends included.</summary>
internal readonly record struct CsvCellRange(int FirstRow, int LastRow, int FirstColumn, int LastColumn)
{
    public int RowCount => LastRow - FirstRow + 1;
    public int ColumnCount => LastColumn - FirstColumn + 1;
    public bool IsSingleCell => RowCount == 1 && ColumnCount == 1;
}

/// <summary>
/// Table view of a document read as delimited text (CSV, TSV): cells in rows and columns over the same <see cref="Document"/> the text view uses,
/// so one revision, one undo history and one save serve both. It reads only the rows on screen through a <see cref="CsvTable"/>, which keeps no
/// cells; the rows are counted by a sequential scan in short slices, so a file of any size opens at once and the scroll range grows while it is read.
/// A cell edit replaces only the bytes of that cell. The view turns keys and the mouse into calls on the table and draws the result.
/// </summary>
internal sealed partial class CsvView : Grid, IDisposable
{
    private const int MaxColumnChars = 40, MinColumnChars = 4, DefaultColumnChars = 12, SampleRows = 100, SliceMs = 8, SliceRows = 256;

    private readonly OpenDocument _od;
    private readonly CsvSurface _canvas;
    private readonly ScrollBar _vbar = new() { Orientation = Orientation.Vertical, Minimum = 0, SmallChange = 1, AllowAutoHide = false };
    private readonly ScrollBar _hbar = new() { Orientation = Orientation.Horizontal, Minimum = 0, SmallChange = 1, AllowAutoHide = false };
    private readonly TextBox _edit = new() { AcceptsReturn = true, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(4, 0) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private EditorOptions _options;
    private CsvTable _table;
    private CsvDialect _dialect;
    private ByteRange? _region;
    private int[] _widths = [];
    private int _firstRow, _firstCol, _row, _col, _anchorRow, _anchorCol;
    private bool _syncing, _endPending, _dragging, _editing;
    private CsvFilter? _filter;
    private List<int>? _matches;
    private IEnumerator<int>? _scan;
    private bool _scanDone;

    /// <param name="region">When given, only these bytes are the table and the rest of the document is neither read nor changed.</param>
    /// <param name="dialect">Own dialect of a region; without it the one of the document.</param>
    public CsvView(OpenDocument doc, EditorOptions options, ByteRange? region = null, CsvDialect? dialect = null)
    {
        _od = doc;
        _options = options;
        _region = region;
        _dialect = dialect ?? doc.TableDialect;
        _table = new CsvTable(doc.Doc, _dialect, _region);
        _table.Invalidated += OnInvalidated;
        Focusable = true;
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        RowDefinitions = new RowDefinitions("*,Auto");
        _canvas = new CsvSurface(this);
        SetColumn(_vbar, 1);
        SetRow(_hbar, 1);
        SetRowSpan(_canvas, 1);
        Children.Add(_canvas);
        Children.Add(_vbar);
        Children.Add(_hbar);
        Children.Add(_edit);
        _vbar.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            _firstRow = (int)_vbar.Value;
            EnsureData(_firstRow + VisibleRows);
            Refresh(scrollRange: false);
        };
        _hbar.ValueChanged += (_, _) =>
        {
            if (_syncing) return;
            _firstCol = (int)_hbar.Value;
            Refresh(scrollRange: false);
        };
        _edit.AddHandler(KeyDownEvent, EditKeyDown, RoutingStrategies.Tunnel);
        _edit.LostFocus += (_, _) => CommitEdit();
        _tick.Tick += (_, _) => Tick();
        Reload();
    }

    public CsvTable Table => _table;
    public CsvDialect Dialect => _dialect;
    /// <summary>True when the table is a selected part of the document and not the whole of it.</summary>
    public bool IsRegion => _region is not null;
    public OpenDocument Document => _od;
    private Core.Documents.Document Doc => _od.Doc;

    /// <summary>Raised when the current cell, the selection, the first row, the rows found or the filter changed.</summary>
    public event Action? ViewChanged;

    /// <summary>Raised with a localised sentence when an edit was refused.</summary>
    public event Action<string>? Refused;

    /// <summary>The current cell: a data row (-1 is the header) and a column.</summary>
    public int Row => _row;
    public int Column => _col;
    public int FirstRow => _firstRow;
    public int FirstColumn => _firstCol;
    public bool IsEditing => _editing;
    public CsvFilter? Filter => _filter;
    public bool HasHeader => HeaderRows > 0;
    public int VisibleRows => _canvas.VisibleRows;
    public int ColumnCount => Math.Max(1, _table.ColumnCount);
    internal long RenderedAt => _canvas.RenderedAt;

    /// <summary>The text in the cell editor while a cell is edited.</summary>
    public string EditText
    {
        get => _edit.Text ?? "";
        set => _edit.Text = value;
    }

    // ---- rows ----

    private int HeaderRows => _dialect.HasHeader && _table.EnsureRows(1) ? 1 : 0;
    private int MinRow => HeaderRows > 0 ? -1 : 0;

    /// <summary>Data rows found so far (the matching ones with a filter); more may follow until <see cref="RowsComplete"/>.</summary>
    public int DataRowCount => _matches?.Count ?? Math.Max(0, _table.RowCount - HeaderRows);

    public bool RowsComplete => _matches is null ? _table.IsComplete : _scanDone;

    /// <summary>The row of the table behind a data row; the header row is -1.</summary>
    public int DocRow(int dataRow) => dataRow < 0 ? 0 : _matches is null ? dataRow + HeaderRows : _matches[dataRow];

    /// <summary>1-based number of the row in the file, as the row column shows it (the header is not counted).</summary>
    public int RowNumber(int dataRow) => dataRow < 0 ? 0 : DocRow(dataRow) - HeaderRows + 1;

    private bool EnsureData(int count)
    {
        if (_matches is null) _table.EnsureRows(HeaderRows + count);
        else
        {
            while (_matches.Count < count && PullMatch()) { }
        }
        return DataRowCount >= count;
    }

    private bool PullMatch()
    {
        if (_scan is null || _scanDone) return false;
        if (_scan.MoveNext())
        {
            _matches!.Add(_scan.Current);
            return true;
        }
        _scanDone = true;
        return false;
    }

    /// <summary>The values of one data row (the header is -1); a short row gives fewer.</summary>
    public string[] RowValues(int dataRow) => _table.Row(DocRow(dataRow));

    public string CellText(int dataRow, int column) => _table.Cell(DocRow(dataRow), column);

    /// <summary>Name of a column from the header; empty without a header.</summary>
    public string ColumnName(int column) => HeaderRows > 0 ? _table.Cell(0, column) : "";

    /// <summary>Name of a column for a status line or a reader: the header, or the spreadsheet letter (A, B, ... AA) when there is none.</summary>
    public string ColumnLabel(int column)
    {
        string name = ColumnName(column);
        if (name.Length > 0) return name;
        var letters = new System.Text.StringBuilder();
        for (int n = column + 1; n > 0; n = (n - 1) / 26) letters.Insert(0, (char)('A' + (n - 1) % 26));
        return letters.ToString();
    }

    internal string[] Preview(int dataRow, int first, int count)
    {
        var fields = _table.Fields(DocRow(dataRow));
        var cells = new string[count];
        for (int i = 0; i < count; i++) cells[i] = first + i < fields.Count ? _table.Preview(fields[first + i]) : "";
        return cells;
    }

    internal int WidthOf(int column) => column < _widths.Length ? _widths[column] : DefaultColumnChars;

    // ---- life ----

    protected override AutomationPeer OnCreateAutomationPeer() => new CsvAutomationPeer(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
        Kick();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tick.Stop();
    }

    public void Dispose()
    {
        _tick.Stop();
        _table.Invalidated -= OnInvalidated;
        _table.Dispose();
    }

    /// <summary>The document under the view was swapped (reload): the view reads the new one.</summary>
    public void Rebind()
    {
        _table.Invalidated -= OnInvalidated;
        _table.Dispose();
        _region = null;
        _table = new CsvTable(Doc, _dialect);
        _table.Invalidated += OnInvalidated;
        Reload();
        Refresh();
    }

    /// <summary>The delimiter, the quote or the header choice of the document changed; a table of a selected part keeps its own.</summary>
    public void ApplyDialect()
    {
        if (_region is null) SetDialect(_od.TableDialect);
    }

    /// <summary>Reads the same text with another delimiter, quote or header choice.</summary>
    public void SetDialect(CsvDialect dialect)
    {
        if (dialect == _dialect) return;
        CancelEdit();
        _dialect = dialect;
        _table.Invalidated -= OnInvalidated;
        _table.Dispose();
        _table = new CsvTable(Doc, _dialect, _region);
        _table.Invalidated += OnInvalidated;
        Reload();
        Refresh();
    }

    public void ApplyOptions(EditorOptions options)
    {
        _options = options;
        _canvas.InvalidateMeasure();
        _canvas.InvalidateVisual();
    }

    internal EditorOptions Options => _options;

    private void Reload()
    {
        _firstRow = _firstCol = _row = _col = _anchorRow = _anchorCol = 0;
        _filter = null;
        _matches = null;
        _scan = null;
        _scanDone = false;
        _endPending = false;
        _table.EnsureRows(SampleRows + 1);
        ComputeWidths();
        _row = _anchorRow = MinRow == -1 && DataRowCount == 0 ? -1 : 0;
        Kick();
    }

    private void ComputeWidths()
    {
        var widths = new int[ColumnCount];
        Array.Fill(widths, MinColumnChars);
        int last = Math.Min(_table.RowCount, SampleRows + HeaderRows);
        for (int r = 0; r < last; r++)
        {
            var fields = _table.Fields(r);
            for (int c = 0; c < fields.Count && c < widths.Length; c++)
            {
                string text = _table.Preview(fields[c], 256);
                int line = text.IndexOfAny(['\r', '\n']);
                widths[c] = Math.Max(widths[c], Math.Min(MaxColumnChars, (line < 0 ? text.Length : line + 1) + 2));
            }
        }
        _widths = widths;
    }

    private void OnInvalidated(object? sender, EventArgs e)
    {
        if (_filter is not null) StartFilter(_filter);
        EnsureData(Math.Max(_row + 1, _firstRow + VisibleRows));
        ClampCaret();
        if (_editing) CancelEdit();
        Refresh();
        Kick();
    }

    private void ClampCaret()
    {
        int max = DataRowCount - 1;
        _row = Math.Clamp(_row, MinRow, Math.Max(MinRow, max));
        _anchorRow = Math.Clamp(_anchorRow, MinRow, Math.Max(MinRow, max));
        _col = Math.Clamp(_col, 0, ColumnCount - 1);
        _anchorCol = Math.Clamp(_anchorCol, 0, ColumnCount - 1);
        _firstRow = Math.Clamp(_firstRow, 0, Math.Max(0, DataRowCount - 1));
        _firstCol = Math.Clamp(_firstCol, 0, ColumnCount - 1);
    }

    private void Kick()
    {
        if (!_tick.IsEnabled && VisualRoot is not null) _tick.Start();
    }

    /// <summary>Reads on in slices while rows (or matches) are missing, so the scroll range grows without blocking the window.</summary>
    internal void Tick()
    {
        var watch = Stopwatch.StartNew();
        bool more = true, grew = false;
        while (more && watch.ElapsedMilliseconds < SliceMs)
        {
            if (_matches is not null) more = PullMatch();
            else if (!_table.IsComplete)
            {
                int before = _table.RowCount;
                _table.EnsureRows(before + SliceRows);
                more = !_table.IsComplete;
            }
            else more = false;
            grew = true;
        }
        if (_endPending && RowsComplete)
        {
            _endPending = false;
            MoveTo(DataRowCount - 1, _col, extend: false);
        }
        if (grew)
        {
            UpdateScrollRange();
            _canvas.InvalidateVisual();
            ViewChanged?.Invoke();
        }
        if (!more && !_endPending) _tick.Stop();
    }

    // ---- scrolling and moving ----

    internal void UpdateScrollRange()
    {
        int visible = VisibleRows;
        _syncing = true;
        _vbar.Maximum = Math.Max(0, DataRowCount - visible);
        _vbar.ViewportSize = visible;
        _vbar.LargeChange = Math.Max(1, visible - 1);
        _firstRow = Math.Clamp(_firstRow, 0, (int)_vbar.Maximum);
        _vbar.Value = _firstRow;
        _hbar.Maximum = Math.Max(0, ColumnCount - 1);
        _hbar.ViewportSize = 1;
        _hbar.LargeChange = 1;
        _firstCol = Math.Clamp(_firstCol, 0, (int)_hbar.Maximum);
        _hbar.Value = _firstCol;
        _hbar.IsVisible = ColumnCount > 1;
        _syncing = false;
    }

    private void Refresh(bool scrollRange = true)
    {
        if (scrollRange) UpdateScrollRange();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    private void RaiseViewChanged()
    {
        ViewChanged?.Invoke();
        (ControlAutomationPeer.FromElement(this) as CsvAutomationPeer)?.CaretMoved();
    }

    public CsvCellRange Selection => new(Math.Min(_row, _anchorRow), Math.Max(_row, _anchorRow), Math.Min(_col, _anchorCol), Math.Max(_col, _anchorCol));

    /// <summary>Puts the current cell on a data row (-1 is the header) and a column, scrolls it into view and, with <paramref name="extend"/>, selects up to it.</summary>
    public void MoveTo(int row, int column, bool extend = false)
    {
        EnsureData(Math.Max(0, row) + 1);
        _row = Math.Clamp(row, MinRow, Math.Max(MinRow, DataRowCount - 1));
        _col = Math.Clamp(column, 0, ColumnCount - 1);
        if (!extend) (_anchorRow, _anchorCol) = (_row, _col);
        Reveal();
        Refresh();
    }

    /// <summary>Selects every cell between two cells.</summary>
    public void Select(int anchorRow, int anchorColumn, int row, int column)
    {
        MoveTo(row, column);
        (_anchorRow, _anchorCol) = (Math.Clamp(anchorRow, MinRow, Math.Max(MinRow, DataRowCount - 1)), Math.Clamp(anchorColumn, 0, ColumnCount - 1));
        Refresh();
    }

    /// <summary>Selects a column over every data row and the header; reads the rest of the file first, because the selection must cover all of it.</summary>
    public void SelectColumn(int column)
    {
        EnsureAllRows();
        _col = _anchorCol = Math.Clamp(column, 0, ColumnCount - 1);
        _anchorRow = MinRow;
        _row = Math.Max(MinRow, DataRowCount - 1);
        Reveal();
        Refresh();
    }

    private void EnsureAllRows()
    {
        for (int before = -1; !RowsComplete && DataRowCount > before;)
        {
            before = DataRowCount;
            EnsureData(before + 65536);
        }
    }

    public void SelectRow(int row)
    {
        EnsureData(Math.Max(0, row) + 1);
        _row = _anchorRow = Math.Clamp(row, MinRow, Math.Max(MinRow, DataRowCount - 1));
        _anchorCol = 0;
        _col = ColumnCount - 1;
        Reveal();
        Refresh();
    }

    public void SelectAll()
    {
        _anchorRow = MinRow;
        _anchorCol = 0;
        _row = Math.Max(MinRow, DataRowCount - 1);
        _col = ColumnCount - 1;
        Refresh();
    }

    private void Reveal()
    {
        int visible = VisibleRows;
        if (_row >= 0)
        {
            if (_row < _firstRow) _firstRow = _row;
            else if (_row >= _firstRow + visible) _firstRow = _row - visible + 1;
        }
        if (_col < _firstCol) _firstCol = _col;
        else while (_firstCol < _col && !_canvas.ColumnFits(_firstCol, _col)) _firstCol++;
    }

    // ---- editing ----

    /// <summary>Opens the editor over the current cell, with its value or with <paramref name="initial"/>.</summary>
    public void BeginEdit(string? initial = null)
    {
        if (_editing) return;
        if (Doc.IsSaving) { Refused?.Invoke(Loc.Get(K.HexBusy)); return; }
        _anchorRow = _row;
        _anchorCol = _col;
        _editing = true;
        _edit.Text = initial ?? (DataRowCount == 0 && _row >= 0 ? "" : CellText(_row, _col));
        if (_canvas.CellRect(_row, _col) is { } rect)
        {
            _edit.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            _edit.MinWidth = rect.Width;
            _edit.MinHeight = rect.Height;
            _edit.MaxHeight = Math.Max(rect.Height, Bounds.Height - rect.Y);
        }
        _edit.IsVisible = true;
        _edit.Focus();
        _edit.CaretIndex = _edit.Text.Length;
    }

    /// <summary>Writes the editor text into the cell (one undo step) and closes the editor; a refused value leaves the cell and says why.</summary>
    public bool CommitEdit()
    {
        if (!_editing) return false;
        string text = EditText;
        _editing = false;
        _edit.IsVisible = false;
        bool applied = SetCell(_row, _col, text);
        Focus();
        return applied;
    }

    public void CancelEdit()
    {
        if (!_editing) return;
        _editing = false;
        _edit.IsVisible = false;
        Focus();
    }

    public bool SetCell(int row, int column, string text) =>
        Run(() => _table.SetCell(DocRow(row), column, text));

    /// <summary>Clears every selected cell as one undo step.</summary>
    public bool ClearSelection()
    {
        var sel = Selection;
        if (sel.IsSingleCell) return SetCell(sel.FirstRow, sel.FirstColumn, "");
        using var group = Doc.BeginUndoGroup();
        bool all = true;
        for (int r = sel.FirstRow; r <= sel.LastRow && all; r++)
            for (int c = sel.FirstColumn; c <= sel.LastColumn && all; c++)
                all = SetCell(r, c, "");
        return all;
    }

    /// <summary>Adds an empty row after the current one; with a filter it is refused, because the new row would not match.</summary>
    public bool InsertRow()
    {
        if (_filter is not null) { Refused?.Invoke(Loc.Get(K.TableFilterActive)); return false; }
        int at = _row < 0 ? 1 : DocRow(_row) + 1;
        if (!Run(() => _table.InsertRow(at))) return false;
        MoveTo(_row + 1, _col);
        return true;
    }

    /// <summary>Deletes the rows of the selection (not the header).</summary>
    public bool DeleteRows()
    {
        var sel = Selection;
        int first = Math.Max(0, sel.FirstRow);
        if (sel.LastRow < first) return false;
        EnsureData(sel.LastRow + 1);
        var rows = Enumerable.Range(first, sel.LastRow - first + 1).Where(r => r < DataRowCount).Select(DocRow).ToList();
        if (rows.Count == 0) return false;
        int caret = first;
        if (!Run(() => _table.DeleteRows(rows))) return false;
        MoveTo(caret, _col);
        return true;
    }

    /// <summary>Adds a column after the current one; the header row gets <paramref name="name"/>.</summary>
    public bool InsertColumn(string name)
    {
        int at = _col + 1;
        if (!Run(() => _table.InsertColumn(at, name))) return false;
        _widths = [.. _widths.Take(at), Math.Max(MinColumnChars, Math.Min(MaxColumnChars, name.Length + 2)), .. _widths.Skip(at)];
        MoveTo(_row, at);
        return true;
    }

    /// <summary>Deletes the columns of the selection.</summary>
    public bool DeleteColumns()
    {
        var sel = Selection;
        int first = sel.FirstColumn, count = sel.ColumnCount;
        if (!Run(() => _table.DeleteColumns(first, count))) return false;
        if (_widths.Length >= first + count) _widths = [.. _widths.Take(first), .. _widths.Skip(first + count)];
        MoveTo(_row, first);
        return true;
    }

    private bool Run(Func<EditResult> change)
    {
        if (Doc.IsSaving)
        {
            Refused?.Invoke(Loc.Get(K.HexBusy));
            return false;
        }
        try
        {
            if (change().Applied) return true;
            Refused?.Invoke(Loc.Get(K.HexRejected));
        }
        catch (CsvFormatException)
        {
            Refused?.Invoke(Loc.Get(K.TableNoQuote));
        }
        catch (UnrepresentableCharactersException)
        {
            Refused?.Invoke(Loc.Get(K.HexNotEncodable, ("encoding", Doc.Codec.Name)));
        }
        return false;
    }

    public void UndoRedo(bool redo)
    {
        if (Doc.IsSaving) { Refused?.Invoke(Loc.Get(K.HexBusy)); return; }
        _ = redo ? Doc.Redo() : Doc.Undo();
    }

    // ---- selection as a model ----

    /// <summary>
    /// The selected cells for a plugin: the columns, the header of those columns and the selected data rows read one by one (a filtered view gives the rows it shows).
    /// Call on the UI thread and read the rows before the document changes.
    /// </summary>
    internal TableSelectionData SelectionData()
    {
        var sel = Selection;
        EnsureData(sel.LastRow + 1);
        TableCell CellOf(int row, IReadOnlyList<CsvField> fields, int column) =>
            column < fields.Count ? new TableCell(_table.Text(fields[column]), new ByteRange(fields[column].Start, fields[column].End)) : new TableCell("", _table.CellRange(row, column));
        IReadOnlyList<TableCell>? header = null;
        if (HeaderRows > 0)
        {
            var fields = _table.Fields(0);
            header = [.. Enumerable.Range(sel.FirstColumn, sel.ColumnCount).Select(c => CellOf(0, fields, c))];
        }
        IEnumerable<TableRow> Rows()
        {
            for (int r = Math.Max(0, sel.FirstRow); r <= sel.LastRow && r < DataRowCount; r++)
            {
                int docRow = DocRow(r);
                var fields = _table.Fields(docRow);
                yield return new TableRow(RowNumber(r), [.. Enumerable.Range(sel.FirstColumn, sel.ColumnCount).Select(c => CellOf(docRow, fields, c))]);
            }
        }
        return new TableSelectionData(sel.FirstColumn, sel.ColumnCount, header, Rows());
    }

    // ---- clipboard text ----

    /// <summary>
    /// The selected cells as tab-separated text, one row per line; a value with a tab, a quote or a line break is quoted, as spreadsheets do. A cell that touches
    /// one of the <paramref name="hidden"/> byte ranges (the values of a secrets file that are hidden) gives the placeholder and sets <paramref name="masked"/>.
    /// </summary>
    public string SelectedText(IReadOnlyList<ByteRange>? hidden, out bool masked)
    {
        var sel = Selection;
        var text = new StringBuilder();
        masked = false;
        for (int r = sel.FirstRow; r <= sel.LastRow && (r < 0 || r < DataRowCount); r++)
        {
            var fields = _table.Fields(DocRow(r));
            if (r > sel.FirstRow) text.Append('\n');
            for (int c = sel.FirstColumn; c <= sel.LastColumn; c++)
            {
                if (c > sel.FirstColumn) text.Append('\t');
                string value = "";
                if (c < fields.Count)
                {
                    bool secret = hidden is not null && SecretValues.Overlaps(hidden, fields[c].Start, fields[c].End);
                    masked |= secret;
                    value = secret ? SecretValues.Placeholder : _table.Text(fields[c]);
                }
                text.Append(value.AsSpan().IndexOfAny("\t\r\n\"") >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value);
            }
        }
        return text.ToString();
    }

    public string SelectedText() => SelectedText(null, out _);

    /// <summary>
    /// Writes tab-separated text into the cells from the first selected cell on, as one undo step. Rows past the last one are added;
    /// with a filter only the rows shown are written. Stops at the first value that cannot be written.
    /// </summary>
    public bool PasteText(string text)
    {
        using var source = Core.Documents.Document.FromBytes(Encoding.UTF8.GetBytes(text), new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
        using var parsed = new CsvTable(source, new CsvDialect('\t', '"', false));
        parsed.EnsureAll();
        if (parsed.RowCount == 0) return false;
        var sel = Selection;
        using var group = Doc.BeginUndoGroup();
        for (int i = 0; i < parsed.RowCount; i++)
        {
            int row = sel.FirstRow + i;
            if (row >= 0 && !EnsureData(row + 1))
            {
                if (_filter is not null) break;
                EnsureAllRows();
                if (!Run(() => _table.InsertRow(_table.RowCount))) return false;
                EnsureData(row + 1);
            }
            var values = parsed.Row(i);
            for (int j = 0; j < values.Length; j++)
                if (!SetCell(row, sel.FirstColumn + j, values[j])) return false;
        }
        MoveTo(sel.FirstRow, sel.FirstColumn);
        return true;
    }

    // ---- filter ----

    /// <summary>Shows only the data rows whose value in <paramref name="column"/> holds <paramref name="text"/>; empty text shows all rows. The file is not changed.</summary>
    public void ApplyFilter(string text, int column)
    {
        CancelEdit();
        if (text.Length == 0)
        {
            _filter = null;
            _matches = null;
            _scan = null;
            _scanDone = false;
        }
        else StartFilter(new CsvFilter(text, column));
        _firstRow = 0;
        _row = _anchorRow = Math.Min(_row, Math.Max(MinRow, DataRowCount - 1));
        ClampCaret();
        Refresh();
        Kick();
    }

    private void StartFilter(CsvFilter filter)
    {
        _filter = filter;
        _matches = [];
        _scanDone = false;
        _scan = _table.Matches(filter).GetEnumerator();
        EnsureData(VisibleRows + 1);
    }

    // ---- input ----

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_editing || e.Text is not { Length: > 0 } text || text.Any(char.IsControl)) return;
        e.Handled = true;
        BeginEdit(text);
    }

    private void EditKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                e.Handled = true;
                CommitEdit();
                MoveTo(_row + 1, _col);
                break;
            case Key.Tab:
                e.Handled = true;
                CommitEdit();
                MoveTo(_row, _col + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1));
                break;
            case Key.Escape:
                e.Handled = true;
                CancelEdit();
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        int page = Math.Max(1, VisibleRows - 1);
        switch (e.Key)
        {
            case Key.Left: MoveTo(_row, _col - 1, shift); break;
            case Key.Right: MoveTo(_row, _col + 1, shift); break;
            case Key.Up: MoveTo(_row - 1, _col, shift); break;
            case Key.Down: MoveTo(_row + 1, _col, shift); break;
            case Key.PageUp: MoveTo(_row - page, _col, shift); break;
            case Key.PageDown: MoveTo(_row + page, _col, shift); break;
            case Key.Home when command: MoveTo(MinRow, 0, shift); break;
            case Key.Home: MoveTo(_row, 0, shift); break;
            case Key.End when command: GoToLastRow(shift); break;
            case Key.End: MoveTo(_row, ColumnCount - 1, shift); break;
            case Key.Tab when !command: MoveTo(_row, _col + (shift ? -1 : 1)); break;
            case Key.Enter or Key.F2: BeginEdit(); break;
            case Key.Delete or Key.Back when !command: ClearSelection(); break;
            case Key.Escape:
                if (!Selection.IsSingleCell) MoveTo(_row, _col);
                else return;
                break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    private void GoToLastRow(bool extend)
    {
        if (RowsComplete)
        {
            MoveTo(DataRowCount - 1, _col, extend);
            return;
        }
        _endPending = true;
        Kick();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _firstCol = Math.Clamp(_firstCol - (int)Math.Round(e.Delta.Y * 3), 0, (int)_hbar.Maximum);
        else
        {
            _firstRow = Math.Max(0, _firstRow - (int)Math.Round(e.Delta.Y * 3));
            EnsureData(_firstRow + VisibleRows);
        }
        Refresh();
        e.Handled = true;
    }

    internal void CellPressed(Point p, bool extend, int clicks)
    {
        Focus();
        if (_canvas.HitTest(p) is not { } hit) return;
        if (hit.Gutter) { SelectRow(hit.Row); return; }
        if (hit.Header) { SelectColumn(hit.Column); return; }
        _dragging = true;
        MoveTo(hit.Row, hit.Column, extend);
        if (clicks == 2) BeginEdit();
    }

    internal void CellMoved(Point p)
    {
        if (_dragging && _canvas.HitTest(p) is { Gutter: false } hit) MoveTo(hit.Row, hit.Column, extend: true);
    }

    internal void CellReleased() => _dragging = false;

    // ---- description for the status line and for screen readers ----

    private string RowsText => RowsComplete ? DataRowCount.ToString() : DataRowCount + "+";

    /// <summary>Position text for the status line.</summary>
    public string StatusText()
    {
        string columns = ColumnCount.ToString();
        string name = ColumnLabel(_col);
        string text = _row < 0
            ? Loc.Get(K.TableStatusHeader, ("column", _col + 1), ("columns", columns), ("name", name))
            : DataRowCount == 0
                ? Loc.Get(K.TableStatusEmpty)
                : Loc.Get(K.TableStatus, ("row", RowNumber(_row)), ("rows", RowsText), ("column", _col + 1), ("columns", columns), ("name", name));
        if (_filter is { } f)
            text += " · " + Loc.Get(K.TableFilterStatus, ("text", f.Text), ("column", (f.Column ?? 0) + 1), ("rows", RowsText));
        if (_region is not null) text += " · " + Loc.Get(K.TableRegionStatus);
        return text;
    }

    /// <summary>The cell for a screen reader: its row, column, header and value.</summary>
    public string DescribeCell()
    {
        string value = DataRowCount == 0 && _row >= 0 ? "" : CellText(_row, _col);
        if (value.Length == 0) value = Loc.Get(K.TableA11yEmpty);
        string name = ColumnLabel(_col);
        if (_row < 0) return Loc.Get(K.TableA11yHeaderCell, ("column", _col + 1), ("value", value));
        return Loc.Get(K.TableA11yCell, ("row", RowNumber(_row)), ("column", _col + 1), ("name", name), ("value", value));
    }

    /// <summary>The row of the current cell for a screen reader: its number and all its values.</summary>
    public string DescribeRow()
    {
        if (DataRowCount == 0 && _row >= 0) return Loc.Get(K.TableStatusEmpty);
        string values = string.Join(", ", RowValues(_row));
        return _row < 0 ? Loc.Get(K.TableA11yHeaderRow, ("values", values)) : Loc.Get(K.TableA11yRow, ("row", RowNumber(_row)), ("rows", RowsText), ("values", values));
    }

    /// <summary>The column of the current cell for a screen reader: its number and its name.</summary>
    public string DescribeColumn() =>
        Loc.Get(K.TableA11yColumn, ("column", _col + 1), ("columns", ColumnCount), ("name", ColumnLabel(_col)));
}
