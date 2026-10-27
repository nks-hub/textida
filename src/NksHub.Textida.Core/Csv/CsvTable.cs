using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Csv;

/// <summary>Keeps the rows whose cell holds <see cref="Text"/>; looks at one column or at all of them. Never changes the document.</summary>
public sealed record CsvFilter(string Text, int? Column = null, bool IgnoreCase = true);

/// <summary>
/// Rows and cells of a document (or of one byte range of it) read as delimited text. The bytes stay the authority: nothing is converted
/// into a cell store. The table keeps only a row count and the offset of every 64th row, found by one sequential scan that runs as far
/// as a caller asks (<see cref="EnsureRows"/>); a row is parsed when it is asked for. A cell edit replaces the bytes of that one field,
/// so everything else (other quoting, delimiters, line endings, BOM, a missing final newline) stays as it was.
/// A line ending ends a row except inside a quoted field; a row may hold fewer or more fields than the header. Not thread safe, like <see cref="Document"/>.
/// </summary>
public sealed class CsvTable : IDisposable
{
    private const int BlockRows = 64, BufferSize = 1 << 16;
    private const int Start = 0, Unquoted = 1, Quoted = 2, QuoteSeen = 3, After = 4;

    private readonly record struct RowInfo(long Start, long ContentEnd, long TermEnd, int Fields);
    private sealed class Block(long start)
    {
        public long Start { get; } = start;
        public int MaxFields { get; set; }
    }

    private readonly Document _doc;
    private readonly Anchor? _from, _to;
    private readonly List<Block> _blocks = [];
    private readonly byte[] _buf = new byte[BufferSize];
    private int _bufLen, _unit = 1, _maxFields, _rows;
    private long _bufStart, _next, _limit, _cursorRow = -1, _cursorStart;
    private bool _bigEndian, _stuck;

    /// <summary>The whole document after its BOM.</summary>
    public CsvTable(Document document, CsvDialect dialect) : this(document, dialect, null) { }

    /// <summary>
    /// The bytes of <paramref name="region"/> only; the rest of the document is not read and is never touched by an edit. The ends follow the
    /// document through edits, so text added by the table at the end of the region stays inside it.
    /// </summary>
    public CsvTable(Document document, CsvDialect dialect, ByteRange? region)
    {
        dialect.Validate();
        _doc = document;
        Dialect = dialect;
        if (region is { } r)
        {
            _from = document.CreateAnchor(Math.Max(r.Start, document.BomLength), AnchorGravity.Left);
            _to = document.CreateAnchor(Math.Max(r.End, document.BomLength), AnchorGravity.Right);
        }
        Reset();
        document.Changed += OnChanged;
    }

    public CsvDialect Dialect { get; }
    public bool HasHeader => Dialect.HasHeader;
    /// <summary>Raised after a document change made the scanned rows stale, so a view must ask again.</summary>
    public event EventHandler? Invalidated;

    public Document Document => _doc;
    public long RegionStart => _from?.Offset ?? _doc.BomLength;
    public long RegionEnd => _to?.Offset ?? _doc.ByteLength;
    /// <summary>Rows found so far; the final count once <see cref="IsComplete"/>.</summary>
    public int RowCount => _rows;
    public bool IsComplete => _stuck || _next >= _limit;
    /// <summary>The most fields in one scanned row.</summary>
    public int ColumnCount => _maxFields;

    public void Dispose()
    {
        _doc.Changed -= OnChanged;
    }

    // ---- scan ----

    /// <summary>Scans on until <paramref name="count"/> rows are known or the text ends; false when it has fewer rows.</summary>
    public bool EnsureRows(int count)
    {
        while (!IsComplete && _rows < count) ScanNext();
        return _rows >= count;
    }

    public void EnsureAll() => EnsureRows(int.MaxValue);

    private void ScanNext()
    {
        if (_rows % BlockRows == 0) _blocks.Add(new Block(_next));
        var info = ScanRow(_next, null);
        if (info.TermEnd <= _next) { _stuck = true; return; }
        var block = _blocks[^1];
        block.MaxFields = Math.Max(block.MaxFields, info.Fields);
        _maxFields = Math.Max(_maxFields, info.Fields);
        _rows++;
        _next = info.TermEnd;
    }

    private RowInfo Locate(int row, List<CsvField>? fields)
    {
        if (row < 0 || !EnsureRows(row + 1)) throw new ArgumentOutOfRangeException(nameof(row));
        long start, at;
        if (_cursorRow >= 0 && row >= _cursorRow && row - _cursorRow < BlockRows) (start, at) = (_cursorStart, _cursorRow);
        else (start, at) = (_blocks[row / BlockRows].Start, (long)row / BlockRows * BlockRows);
        RowInfo info;
        for (; ; )
        {
            fields?.Clear();
            info = ScanRow(start, fields);
            if (at == row) break;
            start = info.TermEnd;
            at++;
        }
        (_cursorRow, _cursorStart) = (row, info.Start);
        return info;
    }

    /// <summary>The one state machine of the format: ends the row at a line ending outside a quoted field and, when asked, lists the fields.</summary>
    private RowInfo ScanRow(long start, List<CsvField>? fields)
    {
        int delimiter = Dialect.Delimiter, quote = Dialect.HasQuote ? Dialect.Quote : -2;
        long p = start, fieldStart = start, valueStart = start, valueEnd = start;
        int state = Start, count = 0;
        for (; ; )
        {
            int u = UnitAt(p);
            bool lineEnd = u < 0 || (state != Quoted && (u == '\n' || u == '\r'));
            if (lineEnd || (u == delimiter && state != Quoted))
            {
                count++;
                fields?.Add(state switch
                {
                    Start => new CsvField(p, p, false, p, p, false),
                    Unquoted => new CsvField(fieldStart, p, false, fieldStart, p, false),
                    Quoted => new CsvField(fieldStart, p, true, valueStart, p, false),
                    _ => new CsvField(fieldStart, p, true, valueStart, valueEnd, true),
                });
                if (!lineEnd)
                {
                    p += _unit;
                    fieldStart = p;
                    state = Start;
                    continue;
                }
                long term = p;
                if (u >= 0)
                {
                    term += _unit;
                    if (u == '\r' && UnitAt(term) == '\n') term += _unit;
                }
                return new RowInfo(start, p, term, count);
            }
            switch (state)
            {
                case Start:
                    if (u == quote) { state = Quoted; valueStart = p + _unit; }
                    else state = Unquoted;
                    break;
                case Quoted:
                    if (u == quote) { state = QuoteSeen; valueEnd = p; }
                    break;
                case QuoteSeen:
                    state = u == quote ? Quoted : After;
                    break;
            }
            p += _unit;
        }
    }

    /// <summary>The unit at <paramref name="offset"/> (a code unit for UTF-16 and UTF-32), or -1 at the end; values over 127 never equal a delimiter or a line ending.</summary>
    private int UnitAt(long offset)
    {
        if (offset + _unit > _limit) return -1;
        if (offset < _bufStart || offset + _unit > _bufStart + _bufLen)
        {
            _bufStart = offset;
            _bufLen = _doc.Read(offset, _buf.AsSpan(0, (int)Math.Min(BufferSize, _limit - offset)));
            if (_bufLen < _unit) return -1;
        }
        int i = (int)(offset - _bufStart);
        switch (_unit)
        {
            case 1: return _buf[i];
            case 2: return _bigEndian ? _buf[i] << 8 | _buf[i + 1] : _buf[i + 1] << 8 | _buf[i];
            default:
                int hi = _bigEndian ? _buf[i] | _buf[i + 1] | _buf[i + 2] : _buf[i + 1] | _buf[i + 2] | _buf[i + 3];
                int lo = _bigEndian ? _buf[i + 3] : _buf[i];
                return hi != 0 ? 0x7FFFFFFF : lo;
        }
    }

    private void Reset()
    {
        _unit = _doc.Codec.Alignment;
        _bigEndian = _doc.Codec.BigEndian;
        _limit = RegionEnd;
        _bufLen = 0;
        _cursorRow = -1;
        _blocks.Clear();
        _rows = _maxFields = 0;
        _stuck = false;
        _next = RegionStart;
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        var change = e.Change;
        long first = change.Kind == ChangeKind.View || change.Edits.Count == 0 ? long.MinValue : change.Edits.Min(s => s.Start);
        int keep = _blocks.FindLastIndex(b => b.Start < first);
        if (keep <= 0 && (keep < 0 || first <= _blocks[0].Start) || _doc.Codec.Alignment != _unit || _doc.Codec.BigEndian != _bigEndian) Reset();
        else
        {
            _limit = RegionEnd;
            _bufLen = 0;
            _cursorRow = -1;
            _next = _blocks[keep].Start;
            _rows = keep * BlockRows;
            _blocks.RemoveRange(keep, _blocks.Count - keep);
            _maxFields = _blocks.Count == 0 ? 0 : _blocks.Max(b => b.MaxFields);
            _stuck = false;
        }
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    // ---- reading ----

    public IReadOnlyList<CsvField> Fields(int row)
    {
        var fields = new List<CsvField>();
        Locate(row, fields);
        return fields;
    }

    /// <summary>The row with its line ending; the last row may have none.</summary>
    public ByteRange RowRange(int row)
    {
        var info = Locate(row, null);
        return new ByteRange(info.Start, info.TermEnd);
    }

    /// <summary>Raw bytes of a cell; past the last field of a short row an empty range at the end of the row.</summary>
    public ByteRange CellRange(int row, int column)
    {
        var fields = new List<CsvField>();
        var info = Locate(row, fields);
        return column < fields.Count ? new ByteRange(fields[column].Start, fields[column].End) : new ByteRange(info.ContentEnd, info.ContentEnd);
    }

    /// <summary>The value of a field: unquoted, with doubled quotes made single; an unterminated quote runs to the end of the text.</summary>
    public string Text(CsvField field)
    {
        if (!field.Quoted) return ReadText(field.Start, field.End);
        string quote = Dialect.Quote.ToString();
        string value = ReadText(field.ValueStart, field.ValueEnd).Replace(quote + quote, quote);
        return field.Closed ? value + ReadText(field.ValueEnd + _unit, field.End) : value;
    }

    private string ReadText(long from, long to) => from >= to ? "" : _doc.ReadText(new ByteRange(from, to));

    /// <summary>Values of a row; a short row gives fewer.</summary>
    public string[] Row(int row) => [.. Fields(row).Select(Text)];

    /// <summary>One cell; empty past the end of a short row.</summary>
    public string Cell(int row, int column)
    {
        var fields = Fields(row);
        return column < fields.Count ? Text(fields[column]) : "";
    }

    /// <summary>Names of the columns, or null when the dialect has no header or the text no row.</summary>
    public string[]? Header() => Dialect.HasHeader && EnsureRows(1) ? Row(0) : null;

    /// <summary>Indexes of the data rows (not the header) that match, found lazily one by one so a caller can stop or work in slices. A change of the document ends the walk with wrong rows: ask again.</summary>
    public IEnumerable<int> Matches(CsvFilter filter, CancellationToken cancel = default)
    {
        var comparison = filter.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fields = new List<CsvField>();
        for (int row = Dialect.HasHeader ? 1 : 0; EnsureRows(row + 1); row++)
        {
            cancel.ThrowIfCancellationRequested();
            Locate(row, fields);
            bool hit = filter.Text.Length == 0;
            for (int c = 0; !hit && c < fields.Count; c++)
                hit = (filter.Column is null || filter.Column == c) && Text(fields[c]).Contains(filter.Text, comparison);
            if (hit) yield return row;
        }
    }

    /// <summary>All matches of <see cref="Matches"/>; scans the whole text.</summary>
    public List<int> Filter(CsvFilter filter, CancellationToken cancel = default) => [.. Matches(filter, cancel)];

    /// <summary>The start of a cell for display: the whole value, or about <paramref name="maxBytes"/> of a longer one followed by an ellipsis.</summary>
    public string Preview(CsvField field, int maxBytes = 2048)
    {
        if (field.End - field.Start <= maxBytes) return Text(field);
        long end = field.Start + maxBytes;
        while (end > field.Start && !_doc.IsCharBoundary(end)) end--;
        string head = ReadText(field.Quoted ? field.Start + _unit : field.Start, end);
        return (field.Quoted ? head.Replace(Dialect.Quote.ToString() + Dialect.Quote, Dialect.Quote.ToString()) : head) + "…";
    }

    // ---- editing: every method below is one transaction, so one undo step ----

    private EditResult Run(IReadOnlyList<ByteEdit> edits) => _doc.Apply(edits, new EditOptions { Source = "csv" });
    private EditResult Unchanged() => new(true, _doc.Revision, ConflictReason.None);

    /// <summary>Replaces the value of one cell. A quoted cell stays quoted; others are quoted only when the value needs it. The same value is no edit.</summary>
    public EditResult SetCell(int row, int column, string value)
    {
        var fields = new List<CsvField>();
        var info = Locate(row, fields);
        if (column < fields.Count)
        {
            var f = fields[column];
            if (Text(f) == value) return Unchanged();
            return Run([new ByteEdit(new ByteRange(f.Start, f.End), Format(value, f.Quoted))]);
        }
        if (value.Length == 0) return Unchanged();
        byte[] pad = _doc.Codec.Encode(new string(Dialect.Delimiter, column - fields.Count + 1));
        return Run([ByteEdit.InsertAt(info.ContentEnd, Cat(pad, Format(value, false)))]);
    }

    /// <summary>Inserts a row before <paramref name="at"/> (the row count appends); the new row gets the line ending of the first row, else LF.</summary>
    public EditResult InsertRow(int at, IReadOnlyList<string>? values = null)
    {
        bool exists = EnsureRows(at + 1);
        if (!exists && at != _rows) throw new ArgumentOutOfRangeException(nameof(at));
        values ??= new string[_rows == 0 ? 0 : Locate(0, null).Fields];
        byte[] record = Join(values.Select(v => v ?? ""));
        byte[] eol = _doc.Codec.Encode(_rows == 0 ? "\n" : FirstLineEnding());
        if (exists) return Run([ByteEdit.InsertAt(Locate(at, null).Start, Cat(record, eol))]);
        if (_rows == 0) return Run([ByteEdit.InsertAt(_next, Cat(record, eol))]);
        var last = Locate(_rows - 1, null);
        return last.TermEnd > last.ContentEnd
            ? Run([ByteEdit.InsertAt(last.TermEnd, Cat(record, eol))])
            : Run([ByteEdit.InsertAt(last.TermEnd, Cat(eol, record))]);
    }

    /// <summary>Deletes a row with its line ending; the last row without one takes the line ending before it along, so the text still ends without one.</summary>
    public EditResult DeleteRow(int row) => DeleteRows([row]);

    /// <summary>Deletes rows as one transaction.</summary>
    public EditResult DeleteRows(IEnumerable<int> rows)
    {
        var ranges = new List<(long From, long To)>();
        int runStart = -1, previous = -2;
        foreach (int row in rows.Distinct().Order())
        {
            var info = Locate(row, null);
            if (row != previous + 1) runStart = row;
            previous = row;
            long from = info.Start;
            if (info.TermEnd == info.ContentEnd && runStart > 0) from = Locate(runStart - 1, null).ContentEnd;
            if (ranges.Count > 0 && ranges[^1].To >= info.Start) ranges[^1] = (Math.Min(ranges[^1].From, from), info.TermEnd);
            else ranges.Add((from, info.TermEnd));
        }
        return Run([.. ranges.Select(r => ByteEdit.Delete(new ByteRange(r.From, r.To)))]);
    }

    /// <summary>Inserts a column before <paramref name="at"/> in every row; the header row gets <paramref name="header"/>, the others an empty cell. Rows shorter than <paramref name="at"/> stay as they are.</summary>
    public EditResult InsertColumn(int at, string header = "")
    {
        EnsureAll();
        var edits = new List<ByteEdit>();
        var fields = new List<CsvField>();
        byte[] delimiter = _doc.Codec.Encode(Dialect.Delimiter.ToString());
        for (int row = 0; row < _rows; row++)
        {
            var info = Locate(row, fields);
            string value = row == 0 && Dialect.HasHeader ? header : "";
            if (at > fields.Count && value.Length == 0) continue;
            byte[] cell = Format(value, false);
            if (at < fields.Count) edits.Add(ByteEdit.InsertAt(fields[at].Start, Cat(cell, delimiter)));
            else
            {
                byte[] pad = _doc.Codec.Encode(new string(Dialect.Delimiter, at - fields.Count + 1));
                edits.Add(ByteEdit.InsertAt(info.ContentEnd, Cat(pad, cell)));
            }
        }
        return Run(edits);
    }

    /// <summary>Deletes a column from every row that has it, with one delimiter each.</summary>
    public EditResult DeleteColumn(int column) => DeleteColumns(column, 1);

    /// <summary>Deletes <paramref name="count"/> neighbouring columns as one transaction; a row that stops inside the span loses what it has of it.</summary>
    public EditResult DeleteColumns(int first, int count)
    {
        EnsureAll();
        var edits = new List<ByteEdit>();
        var fields = new List<CsvField>();
        for (int row = 0; row < _rows; row++)
        {
            Locate(row, fields);
            if (first >= fields.Count) continue;
            int last = Math.Min(fields.Count, first + count) - 1;
            long from = fields[first].Start, to = fields[last].End;
            if (last + 1 < fields.Count) to = fields[last + 1].Start;
            else if (first > 0) from = fields[first - 1].End;
            edits.Add(ByteEdit.Delete(new ByteRange(from, to)));
        }
        return Run(edits);
    }

    private static byte[] Cat(byte[] a, byte[] b) => [.. a, .. b];

    private string FirstLineEnding()
    {
        var info = Locate(0, null);
        return info.TermEnd == info.ContentEnd ? "\n" : _doc.Codec.GetString(_doc.ReadBytes(new ByteRange(info.ContentEnd, info.TermEnd)));
    }

    private byte[] Join(IEnumerable<string> values)
    {
        var bytes = new List<byte>();
        byte[] delimiter = _doc.Codec.Encode(Dialect.Delimiter.ToString());
        bool first = true;
        foreach (var v in values)
        {
            if (!first) bytes.AddRange(delimiter);
            bytes.AddRange(Format(v, false));
            first = false;
        }
        return [.. bytes];
    }

    /// <summary>The bytes of a field holding <paramref name="value"/>; throws when the dialect has no quoting and the value would need it.</summary>
    private byte[] Format(string value, bool forceQuote)
    {
        bool needs = value.AsSpan().IndexOfAny(Dialect.Delimiter, '\r', '\n') >= 0 || (Dialect.HasQuote && value.Contains(Dialect.Quote));
        if (!forceQuote && !needs) return _doc.Codec.Encode(value);
        if (!Dialect.HasQuote) throw new CsvFormatException("The value holds a delimiter or a line break and this format has no quoting.");
        string q = Dialect.Quote.ToString();
        return _doc.Codec.Encode(q + value.Replace(q, q + q) + q);
    }
}
