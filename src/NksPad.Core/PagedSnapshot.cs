using NksPad.Core.Documents;

namespace NksPad.Core;

/// <summary>
/// Frozen content of a <see cref="PagedDocument"/>: the piece list plus references to the append-only add buffer
/// and the mapping. <see cref="Materialize"/> copies the bytes out when the mapping is about to go away.
/// </summary>
public sealed class PagedSnapshot : IByteSnapshot
{
    private readonly Piece[] _pieces;
    private readonly long[] _ends;
    private byte[]? _add;
    private MappedFile? _file;
    private byte[]? _copy;
    private readonly object _gate = new();

    internal PagedSnapshot(Piece[] pieces, byte[] add, MappedFile file)
    {
        _pieces = pieces;
        _add = add;
        _file = file;
        _ends = new long[pieces.Length];
        long sum = 0;
        for (int i = 0; i < pieces.Length; i++) _ends[i] = sum += pieces[i].Length;
        ByteLength = sum;
    }

    public long ByteLength { get; }

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
                var src = p.Original ? _file!.GetSpan(p.Start + skip, take) : _add.AsSpan((int)(p.Start + skip), take);
                src.CopyTo(destination[done..]);
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
            _file = null;
        }
    }
}
