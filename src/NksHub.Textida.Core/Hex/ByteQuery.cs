using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex;

/// <summary>What <see cref="ByteSearch"/> looks for: a <see cref="BytePattern"/> of one length or a <see cref="ByteRegex"/> whose matches may differ in length.</summary>
public abstract class ByteQuery
{
    internal ByteQuery() { }

    /// <summary>Length of the shortest match; at least 1.</summary>
    public abstract int MinLength { get; }

    /// <summary>Length of the longest match; a search reads this much past a block so a match that starts inside it is seen whole.</summary>
    public abstract int MaxLength { get; }

    /// <summary>
    /// Adds the matches that start in the first <paramref name="starts"/> bytes of <paramref name="window"/> (offset <paramref name="block"/> of the snapshot), in order.
    /// Returns true when <paramref name="maxResults"/> matches are collected.
    /// </summary>
    internal abstract bool Scan(ReadOnlySpan<byte> window, int starts, long block, List<ByteRange> matches, int maxResults, CancellationToken ct);
}
