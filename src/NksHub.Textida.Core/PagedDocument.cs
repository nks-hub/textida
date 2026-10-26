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
    private sealed record Edit(long Offset, Piece[] Removed, Piece[] Inserted);

    private readonly PieceTree _tree = new();
    private readonly List<Edit> _undo = [];
    private readonly List<Edit> _redo = [];
    private readonly MappedFileSourceOptions _options;
    private MappedFileSource _source;
    private byte[] _add = new byte[4096];
    private int _addLength;
    private bool _saving;

    private PagedDocument(MappedFileSource source, MappedFileSourceOptions options)
    {
        _source = source;
        _options = options;
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
        if (p.Original)
        {
            long end = p.Start + p.Length;
            n = _source.CountTerminatorsBefore(end) - _source.CountTerminatorsBefore(p.Start);
            // the base index skips a CR followed by LF; read on its own the piece ends with a terminator
            if (lastCr && end + w <= _source.ByteLength && LineScanner.Unit(_source.File.GetSpan(end, w), 0, Codec) == 10) n++;
        }
        else
        {
            // shortcut: rescans the whole inserted block, fine for typing; pastes of hundreds of MiB need per-block counts (P7.1.2)
            var bytes = PieceBytes(p);
            n = LineScanner.CountTerminators(bytes, bytes.Length, Codec);
        }
        return new LineStats(n, firstLf, lastCr);
    }

    private long AfterNthTerminator(Piece p, long k)
    {
        if (!p.Original)
        {
            var bytes = PieceBytes(p);
            return LineScanner.FindNthTerminator(bytes, bytes.Length, k, Codec) + Codec.Alignment;
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
        Piece[] inserted = insert.IsEmpty ? [] : [AppendToAddBuffer(insert)];
        var removed = Apply(offset, deleteLength, inserted);
        _undo.Add(new Edit(offset, removed, inserted));
        _redo.Clear();
    }

    public bool Undo()
    {
        EnsureNotSaving();
        if (_undo.Count == 0) return false;
        var e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Apply(e.Offset, e.Inserted.Sum(p => p.Length), e.Removed);
        _redo.Add(e);
        return true;
    }

    public bool Redo()
    {
        EnsureNotSaving();
        if (_redo.Count == 0) return false;
        var e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Apply(e.Offset, e.Removed.Sum(p => p.Length), e.Inserted);
        _undo.Add(e);
        return true;
    }

    private Piece[] Apply(long offset, long removeLength, Piece[] insert)
    {
        var removed = _tree.Remove(offset, removeLength);
        _tree.Insert(offset, insert);
        return removed;
    }

    private Piece AppendToAddBuffer(ReadOnlySpan<byte> bytes)
    {
        long need = (long)_addLength + bytes.Length;
        if (need > Array.MaxLength) throw new InvalidOperationException("Add buffer limit (2 GiB of inserted text) reached.");
        if (need > _add.Length) Array.Resize(ref _add, (int)Math.Min(Array.MaxLength, Math.Max(need, _add.Length * 2L)));
        bytes.CopyTo(_add.AsSpan(_addLength));
        var piece = new Piece(false, _addLength, bytes.Length);
        _addLength += bytes.Length;
        return piece;
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
            var src = PieceBytes(piece);
            src.CopyTo(destination[done..]);
            done += src.Length;
        }
        return done;
    }

    private ReadOnlySpan<byte> PieceBytes(Piece p) =>
        p.Original ? _source.File.GetSpan(p.Start, checked((int)p.Length)) : _add.AsSpan((int)p.Start, (int)p.Length);

    public ValueTask<IReadOnlyList<string>> ReadLinesAtAsync(long byteOffset, int maxLines, CancellationToken ct = default)
    {
        if (!IsModified) return _source.ReadLinesAtAsync(byteOffset, maxLines, ct);
        return new(Task.Run<IReadOnlyList<string>>(() => ReadLinesFromTree(byteOffset, maxLines), ct));
    }

    /// <summary>Synchronous variant of <see cref="ReadLinesAtAsync"/>.</summary>
    public IReadOnlyList<string> ReadLines(long byteOffset, int maxLines) =>
        IsModified ? ReadLinesFromTree(byteOffset, maxLines) : _source.ReadLines(byteOffset, maxLines);

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
            _add = new byte[4096];
            _addLength = 0;
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
        for (int i = 0; i < slices.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            for (int k = i; k <= i + ahead; k++) Prefetch(k);
            var (piece, start, len) = slices[i];
            var span = piece.Original ? _source.File.GetSpan(start, len) : _add.AsSpan((int)start, len);
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
    }
}
