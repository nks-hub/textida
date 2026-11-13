namespace NksHub.Textida.Core.Documents;

public sealed partial class Document
{
    private const int StreamUndoThreshold = 1 << 20;
    private readonly List<SpillFile> preparedUndoFiles = [];
    // Test seam for a read failure after an earlier history span has already been restored.
    internal Func<IByteSnapshot, IByteSnapshot>? StreamRestoreSource;

    /// <summary>A prepared replacement owns its scratch file until applied. Dispose it also when the result is stale or cancelled.</summary>
    public sealed class PreparedReplacement : IDisposable
    {
        internal PreparedReplacement(Guid documentId, long revision, ByteRange range, UndoBytes removed, UndoBytes inserted, SpillFile? file)
        {
            DocumentId = documentId;
            Revision = revision;
            Range = range;
            Removed = removed;
            Inserted = inserted;
            File = file;
        }

        public Guid DocumentId { get; }
        public long Revision { get; }
        public ByteRange Range { get; }
        internal UndoBytes Removed { get; }
        internal UndoBytes Inserted { get; }
        internal SpillFile? File { get; private set; }
        internal bool Finished { get; private set; }

        internal SpillFile? TakeFile()
        {
            Finished = true;
            var file = File;
            File = null;
            return file;
        }

        public void Dispose()
        {
            Finished = true;
            File?.Dispose();
            File = null;
        }
    }

    /// <summary>Reads only an immutable snapshot; run this preparation off the document thread for large ranges.</summary>
    public static PreparedReplacement PrepareReplacement(IByteSnapshot source, Guid documentId, long revision, ByteEdit edit,
        string? spillDirectory = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ct.ThrowIfCancellationRequested();
        if (revision < 0 || edit.Range.End > source.ByteLength) throw new ArgumentOutOfRangeException(nameof(edit));
        SpillFile? file = null;
        long end = 0;
        try
        {
            var removed = PrepareUndoRange(source, edit.Range.Start, edit.Range.Length, ref file, ref end, spillDirectory, ct);
            var inserted = PrepareUndoRange(new MemorySource(edit.Insert), 0, edit.Insert.Length, ref file, ref end, spillDirectory, ct);
            ct.ThrowIfCancellationRequested();
            return new(documentId, revision, edit.Range, removed, inserted, file);
        }
        catch { file?.Dispose(); throw; }
    }

    /// <summary>Rechecks the source revision and character joins before applying one prepared replacement as one undo step.</summary>
    public EditResult ApplyPreparedReplacement(PreparedReplacement prepared, EditOptions? options = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(prepared);
        ObjectDisposedException.ThrowIf(prepared.Finished, prepared);
        if (prepared.DocumentId != Id) throw new ArgumentException("The replacement belongs to another document.", nameof(prepared));
        options ??= new();
        if (options.BaseRevision.HasValue && options.BaseRevision.Value != prepared.Revision)
            throw new ArgumentException("The base revision differs from the prepared snapshot.", nameof(options));
        if (_saving && !_editsDuringSave) return EditResult.Fail(ConflictReason.Saving, _revision);
        var mapped = MapRange(prepared.Range, prepared.Revision);
        if (!mapped.Ok) return EditResult.Fail(mapped.Conflict, _revision);
        if (mapped.Range.End > ByteLength || !options.RawBytes && mapped.Range.Start < _bomLength)
            return EditResult.Fail(ConflictReason.InvalidRange, _revision);
        if (mapped.Range.IsEmpty && prepared.Inserted.Length == 0) return new(true, _revision, ConflictReason.None);

        if (_backend is not PagedBackend)
        {
            var result = Apply([new ByteEdit(prepared.Range, prepared.Inserted.Read())], new EditOptions
            {
                BaseRevision = prepared.Revision, Source = options.Source, RawBytes = options.RawBytes,
                CoalesceKey = options.CoalesceKey, SelectionsBefore = options.SelectionsBefore, SelectionsAfter = options.SelectionsAfter,
            });
            if (result.Applied) prepared.Dispose();
            return result;
        }
        var edits = new[] { new AppliedEdit(mapped.Range.Start, prepared.Removed, prepared.Inserted) };
        if (!ExecuteStreamed(edits, !options.RawBytes)) return EditResult.Fail(ConflictReason.InvalidRange, _revision);
        if (prepared.TakeFile() is { } file) preparedUndoFiles.Add(file);
        CommitStreamed(edits, options);
        return new(true, _revision, ConflictReason.None);
    }

    private EditResult ApplyStreamed(List<Pending> work, EditOptions options)
    {
        var source = _backend.CreateSnapshot();
        SpillFile? file = null;
        long end = 0;
        try
        {
            var edits = new AppliedEdit[work.Count];
            for (int i = 0; i < work.Count; i++)
            {
                var edit = work[i];
                var removed = PrepareUndoRange(source, edit.Start, edit.Deleted, ref file, ref end, _backend.SpillDirectory, default, spillSmall: true);
                var inserted = PrepareUndoRange(new MemorySource(edit.Insert), 0, edit.Insert.Length, ref file, ref end, _backend.SpillDirectory, default, spillSmall: true);
                edits[i] = new(edit.Start, removed, inserted);
            }
            if (!ExecuteStreamed(edits, !options.RawBytes)) return EditResult.Fail(ConflictReason.InvalidRange, _revision);
            if (file is not null) { preparedUndoFiles.Add(file); file = null; }
            CommitStreamed(edits, options);
            return new(true, _revision, ConflictReason.None);
        }
        finally { file?.Dispose(); }
    }

    private void CommitStreamed(AppliedEdit[] edits, EditOptions options)
    {
        for (int i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            var removed = edit.Removed.Memory is { } before ? Keep(before) with { Reference = edit.Removed.Reference } : edit.Removed;
            var inserted = edit.Inserted.Memory is { } after ? Keep(after) with { Reference = edit.Inserted.Reference } : edit.Inserted;
            edits[i] = new(edit.Offset, removed, inserted);
        }
        var spans = edits.Select(edit => new ChangeSet.Span(edit.Offset, edit.Removed.Length, edit.Inserted.Length)).ToArray();
        Commit(ChangeKind.Edit, options.Source, spans, new Step(edits, options.SelectionsBefore, options.SelectionsAfter, Streamed: true), options.CoalesceKey);
    }

    private bool ExecuteStreamed(AppliedEdit[] edits, bool validate)
    {
        var paged = (PagedBackend)_backend;
        int firstApplied = edits.Length;
        try
        {
            for (int i = edits.Length - 1; i >= 0; i--)
            {
                var edit = edits[i];
                if (validate && !StreamKeepsBoundaries(edit.Offset, edit.Removed.Length, edit.Inserted))
                {
                    Rollback();
                    return false;
                }
                var removed = edit.Removed with { Reference = paged.CaptureRange(edit.Offset, edit.Removed.Length) };
                RestoreRange(edit.Offset, edit.Removed.Length, edit.Inserted);
                firstApplied = i;
                edits[i] = new(edit.Offset, removed, edit.Inserted with { Reference = paged.CaptureRange(edit.Offset, edit.Inserted.Length) });
            }
            return true;
        }
        catch { Rollback(); throw; }

        void Rollback()
        {
            for (int i = firstApplied; i < edits.Length; i++)
                if (!paged.RestoreCapturedRange(edits[i].Offset, edits[i].Inserted.Length, edits[i].Removed.Reference))
                    throw new InvalidOperationException("The backend changed during edit rollback.");
            firstApplied = edits.Length;
        }
    }

    private void RestoreStreamed(Step step, bool undo)
    {
        var spans = new ChangeSet.Span[step.Edits.Length];
        var originals = new object[spans.Length];
        var paged = (PagedBackend)_backend;
        long shift = 0;
        for (int i = 0; i < step.Edits.Length; i++)
        {
            var edit = step.Edits[i];
            spans[i] = undo ? new(edit.Offset + shift, edit.Inserted.Length, edit.Removed.Length)
                : new(edit.Offset, edit.Removed.Length, edit.Inserted.Length);
            shift += edit.Inserted.Length - edit.Removed.Length;
            originals[i] = paged.CaptureRange(spans[i].Start, spans[i].Deleted);
        }
        int firstApplied = spans.Length;
        try
        {
            for (int i = spans.Length - 1; i >= 0; i--)
            {
                var span = spans[i];
                RestoreRange(span.Start, span.Deleted, undo ? step.Edits[i].Removed : step.Edits[i].Inserted);
                firstApplied = i;
            }
        }
        catch
        {
            // Restore lower offsets first, so each higher span returns to its original position.
            // These tokens refer to the current backend and do not read the failed spill or append history.
            for (int i = firstApplied; i < spans.Length; i++)
                if (!paged.RestoreCapturedRange(spans[i].Start, spans[i].Inserted, originals[i]))
                    throw new InvalidOperationException("The backend changed during history rollback.");
            throw;
        }
        Commit(undo ? ChangeKind.Undo : ChangeKind.Redo, null, spans, null, null);
    }

    private void RestoreRange(long offset, long deleted, UndoBytes bytes)
    {
        if (_backend is PagedBackend paged)
        {
            if (paged.RestoreRange(offset, deleted, bytes.Reference)) return;
            IByteSnapshot source = new UndoSource(bytes);
            if (StreamRestoreSource is { } decorate) source = decorate(source);
            paged.ReplaceFromSnapshot(offset, deleted, source);
            return;
        }
        _backend.Replace(offset, deleted, bytes.Read().Span);
    }

    private bool StreamKeepsBoundaries(long start, long deleted, UndoBytes inserted)
    {
        long end = start + deleted;
        if (!IsCharBoundary(start) || !IsCharBoundary(end) || inserted.Length % _codec.Alignment != 0) return false;
        if (inserted.Length <= 8) return EditKeepsBoundaries(new(start, deleted, inserted.Read()));
        var source = new UndoSource(inserted);
        var (left, leftAt) = Window(start, 4, 0);
        var (right, _) = Window(end, 0, 4);
        var firstJoin = new byte[leftAt + 4];
        left.CopyTo(firstJoin, 0);
        ReadSnapshotExactly(source, 0, firstJoin.AsSpan(leftAt, 4), default);
        if (!_codec.IsBoundary(firstJoin, leftAt)) return false;
        int context = Math.Max(4, _codec.ContextBefore(inserted.Length, 0, (at, buffer) => ReadSnapshotExactly(source, at, buffer, default)));
        long tailAt = Math.Max(0, inserted.Length - context);
        tailAt -= tailAt % _codec.Alignment;
        int tailLength = checked((int)(inserted.Length - tailAt));
        var lastJoin = new byte[tailLength + right.Length];
        ReadSnapshotExactly(source, tailAt, lastJoin.AsSpan(0, tailLength), default);
        right.CopyTo(lastJoin, tailLength);
        return _codec.IsBoundary(lastJoin, tailLength);
    }

    private static UndoBytes PrepareUndoRange(IByteSnapshot source, long offset, long length, ref SpillFile? file,
        ref long fileEnd, string? directory, CancellationToken ct, bool spillSmall = false)
    {
        if (length == 0) return new([], null, 0, 0, false);
        if (!spillSmall && length <= StreamUndoThreshold)
        {
            var bytes = new byte[checked((int)length)];
            ReadSnapshotExactly(source, offset, bytes, ct);
            return new(bytes, null, 0, length, HasLineBreakByte(bytes));
        }
        file ??= SpillFile.Create(directory, "undo");
        long start = fileEnd;
        bool lineBreak = false;
        var buffer = new byte[64 * 1024];
        for (long done = 0; done < length;)
        {
            ct.ThrowIfCancellationRequested();
            int take = (int)Math.Min(buffer.Length, length - done);
            ReadSnapshotExactly(source, offset + done, buffer.AsSpan(0, take), ct);
            lineBreak |= HasLineBreakByte(buffer.AsSpan(0, take));
            file.Write(start + done, buffer.AsSpan(0, take));
            done += take;
        }
        ct.ThrowIfCancellationRequested();
        fileEnd = checked(start + length);
        return new(null, file, start, length, lineBreak);
    }

    private static void ReadSnapshotExactly(IByteSnapshot source, long offset, Span<byte> destination, CancellationToken ct)
    {
        while (!destination.IsEmpty)
        {
            ct.ThrowIfCancellationRequested();
            int take = Math.Min(64 * 1024, destination.Length);
            int read = source.Read(offset, destination[..take]);
            if (read <= 0 || read > take) throw new EndOfStreamException("The replacement source range is incomplete.");
            destination = destination[read..];
            offset += read;
        }
    }

    private sealed class MemorySource(ReadOnlyMemory<byte> bytes) : IByteSnapshot
    {
        public long ByteLength => bytes.Length;
        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, ByteLength - offset);
            bytes.Span.Slice(checked((int)offset), count).CopyTo(destination);
            return count;
        }
    }

    private sealed class UndoSource(UndoBytes bytes) : IByteSnapshot
    {
        public long ByteLength => bytes.Length;
        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, ByteLength - offset);
            if (bytes.Memory is { } memory) memory.AsSpan(checked((int)(bytes.At + offset)), count).CopyTo(destination);
            else bytes.File!.ReadExactly(bytes.At + offset, destination[..count]);
            return count;
        }
    }

    private void DisposePreparedUndo()
    {
        foreach (var file in preparedUndoFiles) file.Dispose();
        preparedUndoFiles.Clear();
    }
}
