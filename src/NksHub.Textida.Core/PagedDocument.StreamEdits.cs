using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core;

public sealed partial class PagedDocument
{
    internal object CaptureRange(long offset, long length) => _tree.EnumerateRange(offset, length).ToArray();

    internal void ReplaceCapturedRange(long offset, long deleted, object pieces, bool recordHistory)
    {
        EnsureNotSaving();
        var inserted = (Piece[])pieces;
        if (recordHistory)
        {
            // History may spill. Prepare it before changing the bytes.
            bool wasModified = IsModified;
            _undo.Push(new PieceEdit(offset, (Piece[])CaptureRange(offset, deleted), inserted));
            _redo.Clear();
            if (!wasModified) _editedColumns.CopyBefore(_source.Columns, offset);
        }
        // Measuring a split piece can read the add file. Rebuild line counts on demand after the transaction.
        _tree.StopTrackingLines();
        Apply(offset, deleted, inserted);
        _window?.Add((offset, deleted, inserted));
    }

    internal void ReplaceFromSnapshot(long offset, long deleted, IByteSnapshot source, long sourceOffset, long length, CancellationToken ct)
    {
        EnsureNotSaving();
        if (offset < 0 || deleted < 0 || deleted > ByteLength - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        if (sourceOffset < 0 || length < 0 || sourceOffset > source.ByteLength || length > source.ByteLength - sourceOffset)
            throw new ArgumentOutOfRangeException(nameof(sourceOffset));
        // Prepare the complete add block before changing the tree. A failed read or write leaves the document intact.
        Piece[] inserted;
        if (length == 0) inserted = [];
        else if (length <= 64 * 1024)
        {
            var bytes = new byte[(int)length];
            int done = 0;
            while (done < bytes.Length)
            {
                ct.ThrowIfCancellationRequested();
                int read = source.Read(sourceOffset + done, bytes.AsSpan(done));
                if (read <= 0 || read > bytes.Length - done) throw new EndOfStreamException("The inserted snapshot range is incomplete.");
                done += read;
            }
            inserted = [new Piece(false, _add.Append(bytes), length)];
        }
        else inserted = [new Piece(false, _add.Append(source, sourceOffset, length, ct), length)];
        ct.ThrowIfCancellationRequested();
        ReplaceCapturedRange(offset, deleted, inserted, recordHistory: true);
    }
}
