using System.Text;

namespace NksPad.Core;

/// <summary>
/// Editable document over a memory-mapped file: a piece tree references the original file and an
/// append-only add buffer, so edits cost O(log pieces) and never copy the file. Offsets are bytes
/// in the file encoding and must fall on character boundaries. Not thread safe.
/// </summary>
/// <remarks>
/// Line numbers are not tracked for an edited document: <see cref="TryGetLineStart"/> works only while
/// the document is unmodified. <see cref="ReadLinesAtAsync"/> works in both states.
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

    /// <summary>The unmodified file underneath; searches run against it.</summary>
    public MappedFileSource Source => _source;
    public long ByteLength => _tree.Length;
    public Encoding Encoding => _source.Encoding;
    public bool IsModified => _undo.Count > 0;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int PieceCount => _tree.PieceCount();
    public event EventHandler? IndexProgress;

    /// <summary>Base file line state; <c>Complete</c> is false once edits exist because lines of the edited text are not indexed.</summary>
    public (long Lines, bool Complete) LineIndexState
    {
        get
        {
            var (lines, complete) = _source.LineIndexState;
            return (lines, complete && !IsModified);
        }
    }

    private void OnSourceProgress(object? sender, EventArgs e) => IndexProgress?.Invoke(this, e);

    public bool TryGetLineStart(long line, out long byteOffset)
    {
        byteOffset = 0;
        return !IsModified && _source.TryGetLineStart(line, out byteOffset);
    }

    public void Insert(long offset, string text) => Replace(offset, 0, Encoding.GetBytes(text));

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

    private List<string> ReadLinesFromTree(long byteOffset, int maxLines)
    {
        int bom = _source.Detected.BomLength;
        long from = byteOffset == 0 ? Math.Min(bom, ByteLength) : byteOffset;
        int window = 1 << 16;
        while (true)
        {
            var buf = new byte[(int)Math.Min(window, Math.Max(0, ByteLength - from))];
            int n = Read(from, buf);
            bool atEof = from + n >= ByteLength;
            var lines = LineScanner.Decode(buf.AsSpan(0, n), Encoding, maxLines, atEof);
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
        string tmp = target + ".nkspad-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.SequentialScan,
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
            try { File.Replace(tmp, target, null); }
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
            _addLength = 0;
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>Writes the whole document to <paramref name="stream"/> in 4 MiB slices.</summary>
    public void WriteTo(Stream stream, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        const int slice = 4 << 20;
        long written = 0;
        foreach (var piece in _tree.EnumerateRange(0, ByteLength))
        {
            for (long done = 0; done < piece.Length; done += slice)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(slice, piece.Length - done);
                var span = piece.Original
                    ? _source.File.GetSpan(piece.Start + done, n)
                    : _add.AsSpan((int)(piece.Start + done), n);
                stream.Write(span);
                written += n;
            }
            progress?.Report(written);
        }
    }

    public void Dispose()
    {
        _source.IndexProgress -= OnSourceProgress;
        _source.Dispose();
    }
}
