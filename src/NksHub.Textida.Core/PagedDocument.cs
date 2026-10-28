using System.Buffers;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core;

/// <summary>
/// Editable document over a memory-mapped file: a piece tree references the original file and an
/// append-only add buffer, so edits cost O(log pieces) and never copy the file. Offsets are bytes
/// in the file encoding and must fall on character boundaries. Not thread safe.
/// </summary>
/// <remarks>
/// Line numbers of an edited document come from terminator counts kept in the piece tree. They start
/// once the base file index is complete; until then <see cref="TryGetLineStart"/> returns false for an
/// edited document. <see cref="ReadLinesAtAsync"/> works in all states.
/// </remarks>
public sealed class PagedDocument : IPagedTextSource
{
    private readonly PieceTree _tree = new();
    private readonly EditStack _undo;
    private readonly EditStack _redo;
    private readonly MappedFileSourceOptions _options;
    private MappedFileSource _source;
    private AddBuffer _add;
    private bool _saving;
    private List<(long Offset, long Removed, Piece[] Inserted)>? _window;
    private DurableSpill? _durable;
    private int _durableCount;
    private readonly ColumnIndex _editedColumns = new();
    private long _columnsVersion; // changes whenever the points of _editedColumns may stop describing the text

    private PagedDocument(MappedFileSource source, MappedFileSourceOptions options)
    {
        _source = source;
        _options = options;
        _add = NewAddBuffer();
        _undo = new EditStack(options.SpillBudgetBytes, options.SpillDirectory);
        _redo = new EditStack(options.SpillBudgetBytes, options.SpillDirectory);
        _source.IndexProgress += OnSourceProgress;
        _tree.Reset(new Piece(true, 0, source.ByteLength));
    }

    public static PagedDocument Open(string path, MappedFileSourceOptions? options = null)
    {
        options ??= new();
        return new PagedDocument(MappedFileSource.Open(path, options), options);
    }

    /// <summary>Opens a document over a copy of <paramref name="bytes"/> held in pinned memory; nothing is written to disk and saving in place is not possible.</summary>
    public static PagedDocument OpenMemory(ReadOnlySpan<byte> bytes, MappedFileSourceOptions? options = null)
    {
        options ??= new();
        return new PagedDocument(MappedFileSource.Open(MappedFile.FromMemory(bytes), options), options);
    }

    /// <summary>Forbidden: the paged document never materializes its whole text. Read ranges instead.</summary>
    [Obsolete("PagedDocument has no Text: read a byte range or lines (PLAN 5.1).", error: true)]
    public string Text => throw new NotSupportedException();

    private AddBuffer NewAddBuffer() => new(_options.SpillBudgetBytes, _options.SpillDirectory, NextDurable());

    private DurableSpill? NextDurable() => _durable is { } d ? d with { Name = $"{d.Name}-add-{_durableCount++}.payload" } : null;

    /// <summary>
    /// From now on the add buffer spills into a file named <paramref name="namePrefix"/>-add-N.payload in <paramref name="directory"/> that survives a crash,
    /// so a journal can refer to inserted text instead of copying it. A buffer that is already on disk in a scratch file keeps it.
    /// </summary>
    public void UseDurableSpill(string directory, string namePrefix)
    {
        _durable = new DurableSpill(directory, namePrefix);
        _add.TryMakeDurable(NextDurable()!);
    }

    /// <summary>Name of the durable file the add buffer lives in, or null.</summary>
    public string? DurableFile => _add.DurableName;

    /// <summary>True when the bytes at <paramref name="offset"/> are one inserted block that lives in a durable file.</summary>
    public bool TryGetDurableBytes(long offset, long length, out DurableBytes bytes)
    {
        bytes = default;
        if (length <= 0 || offset < 0 || offset + length > ByteLength) return false;
        Piece? only = null;
        foreach (var p in _tree.EnumerateRange(offset, length))
        {
            if (only is not null) return false;
            only = p;
        }
        return only is { Original: false, Repeated: false } piece && piece.Length == length && _add.TryDescribe(piece.Start, length, out bytes);
    }

    /// <summary>Immutable view of the current content; cheap (pieces only) and safe to read from other threads.</summary>
    public PagedSnapshot CreateSnapshot() => new([.. _tree.EnumerateRange(0, ByteLength)], _add, _source.File);

    /// <summary>The unmodified file underneath; searches run against it.</summary>
    public MappedFileSource Source => _source;
    public long ByteLength => _tree.Length;
    public TextCodec Codec => _source.Codec;

    /// <summary>Shows the same bytes through another codec; the line index is rebuilt when the code unit size or byte order differs.</summary>
    public void Reinterpret(TextCodec codec)
    {
        bool reindex = !MappedFileSource.SharesLineIndex(_source.Codec, codec);
        _source.SetCodec(codec);
        _editedColumns.Clear();
        _columnsVersion++;
        _options.Codec = codec;
        if (reindex) _tree.StopTrackingLines();
    }
    public bool IsModified => _undo.Count > 0;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int PieceCount => _tree.PieceCount();
    public event EventHandler? IndexProgress;

    /// <summary>Line state of the current text; for an edited document <c>Complete</c> stays false until the base file index is done.</summary>
    public (long Lines, bool Complete) LineIndexState
    {
        get
        {
            if (IsModified && EnsureLineTracking()) return (_tree.Terminators + 1, true);
            var (lines, complete) = _source.LineIndexState;
            return (lines, complete && !IsModified);
        }
    }

    /// <summary>Bytes from the start whose line numbers are known: the indexed prefix of an unedited file, everything once the index is complete, nothing for an edited file still being indexed.</summary>
    public long IndexedBytes => IsModified ? (LineIndexState.Complete ? ByteLength : 0) : _source.IndexedBytes;

    private void OnSourceProgress(object? sender, EventArgs e) => IndexProgress?.Invoke(this, e);

    public bool TryGetLineStart(long line, out long byteOffset)
    {
        byteOffset = 0;
        if (!IsModified) return _source.TryGetLineStart(line, out byteOffset);
        if (line < 0 || !EnsureLineTracking() || line > _tree.Terminators) return false;
        if (line > 0) byteOffset = _tree.OffsetAfterTerminator(line, AfterNthTerminator);
        return true;
    }

    /// <summary>
    /// Zero-based line that holds <paramref name="offset"/> without searching for it through the starts of lines (which reads a block per step): for a document that is not
    /// edited the block table and one block tell it. False for an edited document, and while the index does not reach the offset; <see cref="TryGetLineStart"/> then finds it by search.
    /// </summary>
    public bool TryGetLineOfOffset(long offset, out long line)
    {
        line = 0;
        return !IsModified && _source.TryGetLineOfOffset(offset, out line);
    }

    private bool EnsureLineTracking()
    {
        if (_tree.TracksLines) return true;
        if (!_source.LineIndexState.Complete) return false;
        _tree.TrackLines(Measure);
        return true;
    }

    private LineStats Measure(Piece p)
    {
        int w = Codec.Alignment;
        bool whole = p.Length % w == 0; // only a partial unit at the very end of the file can break this
        bool firstLf = whole && LineScanner.Unit(PieceBytes(p with { Length = w }), 0, Codec) == 10;
        bool lastCr = whole && LineScanner.Unit(PieceBytes(p with { Start = p.Start + p.Length - w, Length = w }), 0, Codec) == 13;
        long n;
        if (p.Repeated && p.Pattern.AsSpan().IndexOfAny((byte)10, (byte)13) < 0) n = 0;
        else if (p.Original)
        {
            long end = p.Start + p.Length;
            n = _source.CountTerminatorsBefore(end) - _source.CountTerminatorsBefore(p.Start);
            // the base index skips a CR followed by LF; read on its own the piece ends with a terminator
            if (lastCr && end + w <= _source.ByteLength && LineScanner.Unit(_source.File.GetSpan(end, w), 0, Codec) == 10) n++;
        }
        else
        {
            n = 0;
            foreach (var (_, buffer, length) in PieceChunks(p)) n += LineScanner.CountTerminators(buffer.AsSpan(0, length), length, Codec);
        }
        return new LineStats(n, firstLf, lastCr);
    }

    /// <summary>
    /// A repeated or inserted piece in blocks for the line scanner. A block never ends between a CR and an LF, so the terminators of the blocks add up
    /// to those of the piece; the buffer is reused between blocks and comes from the pool, so the split of a piece the size of a paste (one per edit inside it)
    /// does not leave a megabyte of garbage on the large object heap each time. The buffer may be longer than the block: the length says how much of it is.
    /// </summary>
    private IEnumerable<(long At, byte[] Buffer, int Length)> PieceChunks(Piece p)
    {
        int w = Codec.Alignment;
        int chunk = (1 << 20) - (1 << 20) % w;
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(chunk, p.Length)); // a keystroke makes a piece of a few bytes: a fixed 1 MiB array per edit would be wasteful
        try
        {
            var pair = new byte[2 * w];
            for (long at = 0; at < p.Length;)
            {
                int n = (int)Math.Min(chunk, p.Length - at);
                if (at + n < p.Length)
                {
                    CopyPiece(p, at + n - w, pair);
                    if (LineScanner.Unit(pair, 0, Codec) == 13 && LineScanner.Unit(pair, w, Codec) == 10) n -= w;
                }
                CopyPiece(p, at, buffer.AsSpan(0, n));
                yield return (at, buffer, n);
                at += n;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Copies bytes from <paramref name="skip"/> into a repeated or inserted piece, whatever its length.</summary>
    private void CopyPiece(Piece p, long skip, Span<byte> destination)
    {
        if (p.Repeated) p.CopyRepeated(skip, destination);
        else _add.Read(p.Start + skip, destination);
    }

    private long AfterNthTerminator(Piece p, long k)
    {
        if (!p.Original)
        {
            foreach (var (at, buffer, length) in PieceChunks(p))
            {
                var block = buffer.AsSpan(0, length);
                long here = LineScanner.CountTerminators(block, length, Codec);
                if (k <= here) return at + LineScanner.FindNthTerminator(block, length, k, Codec) + Codec.Alignment;
                k -= here;
            }
            return p.Length;
        }
        long g = _source.CountTerminatorsBefore(p.Start) + k;
        if (_source.TryGetLineStart(g, out long start) && start <= p.Start + p.Length) return start - p.Start;
        return p.Length; // the trailing CR, a terminator only because the next piece does not start with LF
    }

    public void Insert(long offset, string text) => Replace(offset, 0, Codec.EncodeLossy(text));

    public void Insert(long offset, ReadOnlySpan<byte> bytes) => Replace(offset, 0, bytes);

    public void Delete(long offset, long length) => Replace(offset, length, default);

    public void Replace(long offset, long deleteLength, ReadOnlySpan<byte> insert)
    {
        EnsureNotSaving();
        if (offset < 0 || deleteLength < 0 || offset + deleteLength > ByteLength) throw new ArgumentOutOfRangeException(nameof(offset));
        if (deleteLength == 0 && insert.IsEmpty) return;
        Piece[] inserted = insert.IsEmpty ? [] : [new Piece(false, _add.Append(insert), insert.Length)];
        var removed = Apply(offset, deleteLength, inserted);
        _window?.Add((offset, deleteLength, inserted));
        _undo.Push(new PieceEdit(offset, removed, inserted));
        _redo.Clear();
    }

    public bool Undo()
    {
        EnsureNotSaving();
        EnsureNoWindow();
        if (_undo.Count == 0) return false;
        var e = _undo.Pop();
        Apply(e.Offset, e.Inserted.Sum(p => p.Length), e.Removed);
        _redo.Push(e);
        return true;
    }

    public bool Redo()
    {
        EnsureNotSaving();
        EnsureNoWindow();
        if (_redo.Count == 0) return false;
        var e = _redo.Pop();
        Apply(e.Offset, e.Removed.Sum(p => p.Length), e.Inserted);
        _undo.Push(e);
        return true;
    }

    private Piece[] Apply(long offset, long removeLength, Piece[] insert)
    {
        _columnsVersion++;
        if (IsModified) _editedColumns.DropFrom(offset);
        else _editedColumns.CopyBefore(_source.Columns, offset);
        var removed = _tree.Remove(offset, removeLength);
        _tree.Insert(offset, insert);
        return removed;
    }

    private void EnsureNoWindow()
    {
        if (_window is not null) throw new InvalidOperationException("Undo and redo refer to the file that a streaming save is about to replace.");
    }

    private void EnsureNotSaving()
    {
        if (_saving) throw new InvalidOperationException("The document cannot be edited while it is being saved.");
    }

    /// <summary>Copies document bytes starting at <paramref name="offset"/>; returns the number copied.</summary>
    public int Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > ByteLength) throw new ArgumentOutOfRangeException(nameof(offset));
        long want = Math.Min(destination.Length, ByteLength - offset);
        int done = 0;
        foreach (var piece in _tree.EnumerateRange(offset, want))
        {
            if (piece.Repeated)
            {
                piece.CopyRepeated(0, destination.Slice(done, (int)piece.Length));
                done += (int)piece.Length;
                continue;
            }
            var src = PieceBytes(piece);
            src.CopyTo(destination[done..]);
            done += src.Length;
        }
        return done;
    }

    /// <summary>
    /// Replaces <paramref name="deleteLength"/> bytes at <paramref name="offset"/> by <paramref name="length"/> bytes of <paramref name="pattern"/>
    /// repeated from its first byte. The new bytes are a piece of their own: no memory, no add buffer and no copying, whatever the length.
    /// Returns what was removed, for <see cref="RestoreRemoved"/>; it refers to the file as mapped now, so it is good until the file is saved over.
    /// </summary>
    public object ReplaceRepeated(long offset, long deleteLength, ReadOnlySpan<byte> pattern, long length)
    {
        EnsureNotSaving();
        if (offset < 0 || deleteLength < 0 || offset + deleteLength > ByteLength) throw new ArgumentOutOfRangeException(nameof(offset));
        if (pattern.IsEmpty || length < 0) throw new ArgumentException("A pattern needs at least one byte and a length of zero or more.");
        Piece[] inserted = length == 0 ? [] : [new Piece(false, 0, length, pattern.ToArray())];
        var removed = Apply(offset, deleteLength, inserted);
        _window?.Add((offset, deleteLength, inserted));
        _undo.Push(new PieceEdit(offset, removed, inserted));
        _redo.Clear();
        return removed;
    }

    /// <summary>Puts back what <see cref="ReplaceRepeated"/> removed over the <paramref name="insertedLength"/> bytes at <paramref name="offset"/>.</summary>
    public void RestoreRemoved(long offset, long insertedLength, object removed)
    {
        EnsureNotSaving();
        EnsureNoWindow();
        var pieces = (Piece[])removed;
        var gone = Apply(offset, insertedLength, pieces);
        _undo.Push(new PieceEdit(offset, gone, pieces));
        _redo.Clear();
    }

    /// <summary>The bytes of a piece; a repeated piece is expanded into a new array, so ask only for short ones.</summary>
    private ReadOnlySpan<byte> PieceBytes(Piece p)
    {
        int length = checked((int)p.Length);
        if (p.Original) return _source.File.GetSpan(p.Start, length);
        if (!p.Repeated && _add.TryGetSpan(p.Start, length, out var span)) return span;
        var copy = new byte[length];
        CopyPiece(p, 0, copy);
        return copy;
    }

    public ValueTask<IReadOnlyList<string>> ReadLinesAtAsync(long byteOffset, int maxLines, CancellationToken ct = default)
    {
        if (!IsModified) return _source.ReadLinesAtAsync(byteOffset, maxLines, ct);
        return new(Task.Run<IReadOnlyList<string>>(() => ReadLinesFromTree(byteOffset, maxLines), ct));
    }

    /// <summary>Synchronous variant of <see cref="ReadLinesAtAsync"/>.</summary>
    public IReadOnlyList<string> ReadLines(long byteOffset, int maxLines) =>
        IsModified ? ReadLinesFromTree(byteOffset, maxLines) : _source.ReadLines(byteOffset, maxLines);

    /// <summary>
    /// At most <paramref name="maxColumns"/> UTF-16 columns of the line that starts at <paramref name="lineStart"/>, from column
    /// <paramref name="startColumn"/>, without decoding the rest of the line. An edit drops the column points noted for the edited text.
    /// </summary>
    public string ReadColumns(long lineStart, long startColumn, int maxColumns, CancellationToken ct = default)
    {
        if (!IsModified) return _source.ReadColumns(lineStart, startColumn, maxColumns, ct);
        return ColumnReader.Read(EditedWindow, ByteLength, Codec, _source.BomLength, _editedColumns, lineStart, startColumn, maxColumns, ct);
    }

    /// <summary>Length in UTF-16 columns of a line, when a read has reached its end since the last edit.</summary>
    public bool TryGetLineColumns(long lineStart, out long columns) =>
        IsModified ? _editedColumns.TryGetLength(lineStart, out columns) : _source.TryGetLineColumns(lineStart, out columns);

    /// <summary>True when <see cref="ReadColumns"/> at <paramref name="column"/> resumes from a point noted at most <see cref="ColumnIndex.Spacing"/> columns before it, so it is short.</summary>
    public bool IsColumnNear(long lineStart, long column) =>
        IsModified ? _editedColumns.IsNear(lineStart, column) : _source.IsColumnNear(lineStart, column);

    /// <summary>True when <see cref="ReadColumns"/> at <paramref name="column"/> resumes from a point noted, or the line start, at most <paramref name="within"/> columns before it.</summary>
    public bool IsColumnNear(long lineStart, long column, long within) =>
        IsModified ? _editedColumns.IsNear(lineStart, column, within) : _source.IsColumnNear(lineStart, column, within);

    /// <summary>True when <see cref="ColumnOf"/> at <paramref name="offset"/> resumes from a point noted, or the line start, at most <paramref name="within"/> bytes before it.</summary>
    public bool IsOffsetNear(long lineStart, long offset, long within) =>
        IsModified ? _editedColumns.IsNearPosition(lineStart, offset, within) : _source.IsOffsetNear(lineStart, offset, within);

    /// <summary>
    /// A reader of the columns of the current revision for another thread, so that a far read does not hold up this one. The points it notes on an
    /// edited document come back with <see cref="KeepColumns"/>; on an unedited one they go straight to the index of the file.
    /// </summary>
    public ColumnSnapshot SnapshotColumns()
    {
        if (!IsModified) return new ColumnSnapshot(_source, null, _source.Columns, _columnsVersion);
        var index = new ColumnIndex();
        index.Merge(_editedColumns);
        return new ColumnSnapshot(_source, CreateSnapshot(), index, _columnsVersion);
    }

    /// <summary>Takes over the column points that <paramref name="columns"/> noted, unless the document changed after the snapshot was made.</summary>
    public void KeepColumns(ColumnSnapshot columns)
    {
        if (columns.Edited && columns.Version == _columnsVersion && IsModified) _editedColumns.Merge(columns.Index);
    }

    /// <summary>
    /// UTF-16 columns from the start of the line that starts at <paramref name="lineStart"/> to <paramref name="offset"/>, a character boundary of that line,
    /// without decoding the line. The count resumes from the nearest point noted by earlier reads; edits keep the points before them.
    /// </summary>
    public long ColumnOf(long lineStart, long offset, CancellationToken ct = default) =>
        IsModified
            ? ColumnReader.ColumnOf(EditedWindow, ByteLength, Codec, _source.BomLength, _editedColumns, lineStart, offset, ct)
            : ColumnReader.ColumnOf((pos, max) => _source.File.GetSpanClamped(pos, max), _source.File.Length, Codec, _source.BomLength, _source.Columns, lineStart, offset, ct);

    /// <summary>Byte offset of the UTF-16 column <paramref name="column"/> of the line that starts at <paramref name="lineStart"/>; the end of the line when the line is shorter.</summary>
    public long OffsetOfColumn(long lineStart, long column, CancellationToken ct = default) =>
        IsModified
            ? ColumnReader.OffsetOf(EditedWindow, ByteLength, Codec, _source.BomLength, _editedColumns, lineStart, column, ct)
            : ColumnReader.OffsetOf((pos, max) => _source.File.GetSpanClamped(pos, max), _source.File.Length, Codec, _source.BomLength, _source.Columns, lineStart, column, ct);

    private byte[] _windowBuffer = [];

    /// <summary>
    /// Bytes at <paramref name="position"/> straight from a piece when one holds them all, else copied across pieces into a buffer that the next call overwrites.
    /// </summary>
    private ReadOnlySpan<byte> EditedWindow(long position, long maxLength)
    {
        long want = Math.Min(maxLength, ByteLength - position);
        if (want <= 0) return default;
        foreach (var piece in _tree.EnumerateRange(position, want))
        {
            if (piece.Length == want) return PieceBytes(piece);
            break;
        }
        if (_windowBuffer.Length < want) _windowBuffer = new byte[Math.Max(want, 64 * 1024)];
        var copy = _windowBuffer.AsSpan(0, (int)want);
        Read(position, copy);
        return copy;
    }

    private List<string> ReadLinesFromTree(long byteOffset, int maxLines)
    {
        int bom = _source.Codec.BomLengthIn(_source.File.GetSpanClamped(0, 4));
        long from = byteOffset == 0 ? Math.Min(bom, ByteLength) : byteOffset;
        int window = 1 << 16;
        while (true)
        {
            var buf = new byte[(int)Math.Min(window, Math.Max(0, ByteLength - from))];
            int n = Read(from, buf);
            bool atEof = from + n >= ByteLength;
            var lines = LineScanner.Decode(buf.AsSpan(0, n), Codec, maxLines, atEof);
            if (lines.Count == maxLines || atEof) return lines;
            window = checked(window * 4);
        }
    }

    /// <summary>True between <see cref="BeginStreamingSave"/> and <see cref="EndStreamingSave"/> or <see cref="CommitStreamingSave"/>.</summary>
    public bool InStreamingSave => _window is not null;

    /// <summary>
    /// Freezes the current content as the snapshot a save writes and starts to note the edits made from now on. The document stays editable
    /// (insert, delete, replace); <see cref="CommitStreamingSave"/> puts those edits on top of the saved file, so they stay unsaved.
    /// </summary>
    public PagedSnapshot BeginStreamingSave()
    {
        EnsureNotSaving();
        if (_window is not null) throw new InvalidOperationException("A streaming save is already open.");
        var snapshot = CreateSnapshot();
        _window = [];
        return snapshot;
    }

    /// <summary>Drops the notes of <see cref="BeginStreamingSave"/>; the edits stay in the document.</summary>
    public void EndStreamingSave() => _window = null;

    /// <summary>
    /// Moves <paramref name="temp"/>, which holds exactly the snapshot of <see cref="BeginStreamingSave"/>, over the open file, maps the result and
    /// replays the edits made since on top of it. The undo history refers to the replaced file and ends; the replayed edits are the new history.
    /// When the swap fails the document keeps the old file and all its edits.
    /// </summary>
    public void CommitStreamingSave(string temp, string target)
    {
        var window = _window ?? throw new InvalidOperationException("No streaming save is open.");
        _source.IndexProgress -= OnSourceProgress;
        _source.Dispose();
        try { Documents.FileSwap.Replace(temp, target); }
        catch
        {
            _source = MappedFileSource.Open(target, _options);
            _source.IndexProgress += OnSourceProgress;
            throw;
        }
        _source = MappedFileSource.Open(target, _options);
        _source.IndexProgress += OnSourceProgress;
        _window = null;
        _tree.Reset(new Piece(true, 0, _source.ByteLength));
        _undo.Clear();
        _redo.Clear();
        _editedColumns.Clear();
        // the inserted pieces point into the add buffer, which is kept, never into the replaced file
        foreach (var (offset, removeLength, inserted) in window)
            _undo.Push(new PieceEdit(offset, Apply(offset, removeLength, inserted), inserted));
    }

    /// <summary>
    /// Streams the document to a temporary file next to the target and atomically moves it into place.
    /// Saving over the open file remaps the result and clears undo history; saving elsewhere leaves the document bound to its file.
    /// </summary>
    public Task SaveAsync(string? path = null, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureNotSaving();
        _saving = true;
        return Task.Run(() =>
        {
            try { SaveCore(path, progress, ct); }
            finally { _saving = false; }
        }, CancellationToken.None);
    }

    private void SaveCore(string? path, IProgress<long>? progress, CancellationToken ct)
    {
        string target = System.IO.Path.GetFullPath(path ?? _source.Path);
        bool inPlace = string.Equals(target, System.IO.Path.GetFullPath(_source.Path), StringComparison.OrdinalIgnoreCase);
        string tmp = target + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                PreallocationSize = ByteLength,
                BufferSize = 0,
            }))
            {
                WriteTo(fs, progress, ct);
                fs.Flush(true);
            }

            if (!inPlace)
            {
                File.Move(tmp, target, overwrite: true);
                return;
            }

            _source.IndexProgress -= OnSourceProgress;
            _source.Dispose();
            try { Documents.FileSwap.Replace(tmp, target); }
            catch
            {
                _source = MappedFileSource.Open(target, _options);
                _source.IndexProgress += OnSourceProgress;
                throw;
            }
            _source = MappedFileSource.Open(target, _options);
            _source.IndexProgress += OnSourceProgress;
            _tree.Reset(new Piece(true, 0, _source.ByteLength));
            _undo.Clear();
            _redo.Clear();
            _add = NewAddBuffer(); // snapshots keep the old buffer; its spill file goes with the last reference
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// Writes the whole document to <paramref name="stream"/> in 8 MiB slices straight from the mapping.
    /// A single thread faulting in mapped pages tops out near 2.5 GB/s, so upcoming slices are touched
    /// in parallel on the thread pool while the current one is written.
    /// </summary>
    public void WriteTo(Stream stream, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        const int slice = 8 << 20, ahead = 3;
        var slices = new List<(Piece Piece, long Start, int Length)>();
        foreach (var piece in _tree.EnumerateRange(0, ByteLength))
            for (long done = 0; done < piece.Length; done += slice)
                slices.Add((piece, piece.Start + done, (int)Math.Min(slice, piece.Length - done)));

        var prefetch = new Task?[slices.Count];
        void Prefetch(int i)
        {
            if (i >= slices.Count || prefetch[i] is not null || !slices[i].Piece.Original) return;
            var (_, start, len) = slices[i];
            prefetch[i] = Task.Run(() => Parallel.For(0, 4, k =>
            {
                int part = len / 4, from = k * part, n = k == 3 ? len - from : part;
                var span = _source.File.GetSpan(start + from, n);
                byte x = 0;
                for (int o = 0; o < span.Length; o += 4096) x ^= span[o];
                GC.KeepAlive(x);
            }));
        }

        long written = 0;
        byte[]? scratch = null;
        for (int i = 0; i < slices.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            for (int k = i; k <= i + ahead; k++) Prefetch(k);
            var (piece, start, len) = slices[i];
            ReadOnlySpan<byte> span;
            if (piece.Original) span = _source.File.GetSpan(start, len);
            else if (!piece.Repeated && _add.TryGetSpan(start, len, out var inMemory)) span = inMemory;
            else
            {
                scratch ??= new byte[slice];
                if (piece.Repeated) piece.CopyRepeated(start - piece.Start, scratch.AsSpan(0, len));
                else _add.Read(start, scratch.AsSpan(0, len));
                span = scratch.AsSpan(0, len);
            }
            stream.Write(span);
            written += len;
            if (i + 1 == slices.Count || slices[i + 1].Piece != piece) progress?.Report(written);
        }
        foreach (var t in prefetch) t?.Wait();
    }

    public void Dispose()
    {
        _source.IndexProgress -= OnSourceProgress;
        _source.Dispose();
        _undo.Dispose();
        _redo.Dispose();
    }
}
