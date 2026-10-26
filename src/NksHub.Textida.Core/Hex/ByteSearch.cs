using System.Buffers;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex;

public sealed record ByteSearchOptions
{
    public int MaxResults { get; init; } = 10_000;
    public int ChunkBytes { get; init; } = 1 << 20;
    /// <summary>Matches must fit entirely in this half-open range; null searches the whole snapshot.</summary>
    public ByteRange? Range { get; init; }
}

/// <summary>Ordered, overlapping matches of one revision. LimitReached means the search stopped at MaxResults, even if no further match exists.</summary>
public sealed record ByteSearchResult(Guid DocumentId, long Revision, IReadOnlyList<ByteRange> Matches, bool LimitReached);

public static class ByteSearch
{
    /// <summary>Reads immutable bytes in blocks; call on a worker thread. Cancellation throws OperationCanceledException.</summary>
    public static ByteSearchResult Find(DocumentSnapshot snapshot, BytePattern pattern,
        ByteSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pattern);
        options ??= new ByteSearchOptions();
        if (options.MaxResults <= 0) throw new ArgumentOutOfRangeException(nameof(options), "MaxResults must be positive.");
        if (options.ChunkBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options), "ChunkBytes must be positive.");
        var range = options.Range ?? new ByteRange(0, snapshot.ByteLength);
        if (range.End > snapshot.ByteLength) throw new ArgumentOutOfRangeException(nameof(options), "The search range exceeds the snapshot.");
        cancellationToken.ThrowIfCancellationRequested();
        var matches = new List<ByteRange>();
        ByteSearchResult Result(bool limited) => new(snapshot.DocumentId, snapshot.Revision, matches.AsReadOnly(), limited);
        if (range.Length < pattern.Length) return Result(false);

        long capacity = (long)options.ChunkBytes + pattern.Length - 1;
        if (capacity > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(options), "The chunk and pattern exceed the buffer limit.");
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(capacity, range.Length));
        try
        {
            long lastStart = range.End - pattern.Length;
            for (long block = range.Start; block <= lastStart;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(capacity, range.End - block);
                int filled = 0;
                while (filled < count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = snapshot.Read(block + filled, buffer.AsSpan(filled, count - filled));
                    if (read == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
                    filled += read;
                }
                int starts = (int)Math.Min(options.ChunkBytes, lastStart - block + 1);
                for (int at = 0; at < starts; at++)
                {
                    if ((at & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (pattern.AnchorLength > 0)
                    {
                        int found = buffer.AsSpan(at + pattern.AnchorStart, starts - at + pattern.AnchorLength - 1).IndexOf(pattern.Anchor);
                        if (found < 0) break;
                        at += found;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    if (!pattern.Matches(buffer.AsSpan(at, pattern.Length))) continue;
                    matches.Add(new ByteRange(block + at, block + at + pattern.Length));
                    if (matches.Count == options.MaxResults) return Result(true);
                }
                block += starts;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return Result(false);
    }
}
