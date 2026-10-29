using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.History;

/// <summary>The lines a revision added to the revision before it and removed from it, as the line diff of the two counts them. <paramref name="Exact"/> is false when a bound of the diff made a block coarser than a minimal diff.</summary>
public readonly record struct RevisionChange(long Added, long Removed, bool Exact = true);

/// <summary>What the revisions of one file amount to: how many, the bytes they take on disk and the times of the oldest and the newest.</summary>
public sealed record HistorySummary(int Revisions, long StoredBytes, DateTime? OldestUtc, DateTime? NewestUtc)
{
    public static HistorySummary Of(IReadOnlyList<HistoryRevision> revisions) => new(revisions.Count, revisions.Sum(r => r.StoredBytes),
        revisions.Count == 0 ? null : revisions.Min(r => r.TimeUtc), revisions.Count == 0 ? null : revisions.Max(r => r.TimeUtc));
}

public static class HistoryStats
{
    /// <summary>The lines <paramref name="newer"/> added to <paramref name="older"/> and removed from it, by the same line diff the comparison of the two uses.</summary>
    public static RevisionChange Between(byte[] older, byte[] newer, TextCodec codec, CancellationToken ct = default)
    {
        var diff = LineDiff.Compute(Source(older, codec), Source(newer, codec), null, ct);
        long added = 0, removed = 0;
        foreach (var h in diff.Hunks)
        {
            removed += h.ACount;
            added += h.BCount;
        }
        return new RevisionChange(added, removed, diff.Exact);
    }

    private static DiffSource Source(byte[] bytes, TextCodec codec) => new(new ArraySnapshot(bytes), codec, codec.BomLengthIn(bytes));
}
