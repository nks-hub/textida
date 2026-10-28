using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core;

/// <summary>
/// Frozen content of a <see cref="PagedDocument"/>: the piece list plus references to the append-only add buffer
/// and a lease on the original file handle. Closing or saving the document does not copy the snapshot into memory.
/// External changes to the original file's length or write time invalidate reads; writes preserving both are not detected.
/// </summary>
public sealed class PagedSnapshot : IByteSnapshot
{
    private readonly Piece[] _pieces;
    private readonly long[] _ends;
    private AddBuffer? _add;
    private MappedFile.SnapshotLease? _source;
    private byte[]? _copy;
    private readonly object _gate = new();

    internal PagedSnapshot(Piece[] pieces, AddBuffer add, MappedFile file)
    {
        _pieces = pieces;
        _add = add;
        _ends = new long[pieces.Length];
        long sum = 0;
        for (int i = 0; i < pieces.Length; i++) _ends[i] = sum += pieces[i].Length;
        ByteLength = sum;
        _source = file.RetainSnapshot();
    }

    public long ByteLength { get; }

    public long RetainedBytes => _copy?.Length ?? (long)_pieces.Length * (System.Runtime.CompilerServices.Unsafe.SizeOf<Piece>() + sizeof(long));

    public int Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > ByteLength) throw new ArgumentOutOfRangeException(nameof(offset));
        lock (_gate)
        {
            if (_copy is not null)
            {
                int n = (int)Math.Min(destination.Length, ByteLength - offset);
                _copy.AsSpan((int)offset, n).CopyTo(destination);
                return n;
            }
            int done = 0;
            int i = Array.BinarySearch(_ends, offset);
            i = i < 0 ? ~i : i + 1;
            while (done < destination.Length && i < _pieces.Length)
            {
                var p = _pieces[i];
                long skip = offset + done - (_ends[i] - p.Length);
                int take = (int)Math.Min(p.Length - skip, destination.Length - done);
                if (p.Repeated) p.CopyRepeated(skip, destination.Slice(done, take));
                else if (p.Original) _source!.Read(p.Start + skip, destination.Slice(done, take));
                else _add!.Read(p.Start + skip, destination.Slice(done, take));
                done += take;
                i++;
            }
            return done;
        }
    }

    /// <summary>Copies the content to private memory and drops the references to the mapping; limited to 2 GiB.</summary>
    public void Materialize()
    {
        lock (_gate)
        {
            if (_copy is not null) return;
            var copy = new byte[checked((int)ByteLength)];
            Read(0, copy);
            _copy = copy;
            _add = null;
            _source?.Dispose();
            _source = null;
        }
    }
}
