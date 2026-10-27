namespace NksHub.Textida.Core.Documents;

/// <summary>
/// A byte position that follows the document through edits, undo and redo. Text inserted exactly at the anchor is placed by
/// <see cref="Gravity"/>. When a change deletes the anchor from inside, or the position cannot be recovered
/// (change log too short, future revision), the anchor becomes invalid; its position is never guessed.
/// </summary>
public sealed class Anchor
{
    internal Anchor(long offset, AnchorGravity gravity, bool valid)
    {
        Offset = offset;
        Gravity = gravity;
        IsValid = valid;
    }

    public long Offset { get; internal set; }
    public bool IsValid { get; internal set; }
    public AnchorGravity Gravity { get; }
}

/// <summary>Pure mapping of ranges and points through one change set.</summary>
internal static class ChangeMap
{
    /// <summary>
    /// Maps <c>[start, end)</c> through the change. A range survives only when no edit touches its content:
    /// edits fully before it shift it, edits at or after its end do not matter, anything else is an overlap.
    /// An empty range next to an insertion is ambiguous and is an overlap too.
    /// </summary>
    public static bool TryMapRange(ref long start, ref long end, ChangeSet change)
    {
        long shift = 0;
        bool empty = start == end;
        foreach (var (a, d, i) in change.Edits)
        {
            if (d == 0)
            {
                if (a < start) shift += i;
                else if (a == start) { if (empty) return false; shift += i; }
                else if (a < end) return false;
            }
            else
            {
                if (a + d <= start) shift += i - d;
                else if (a >= end) { }
                else return false;
            }
        }
        start += shift;
        end += shift;
        return true;
    }

    /// <summary>Maps one point; false when the change deletes it from inside.</summary>
    public static bool TryMapPoint(ref long pos, AnchorGravity gravity, ChangeSet change)
    {
        long shift = 0;
        foreach (var (a, d, i) in change.Edits)
        {
            if (pos < a) break;
            if (d == 0)
            {
                if (pos == a)
                {
                    if (gravity == AnchorGravity.Right) shift += i;
                    break;
                }
                shift += i;
            }
            else
            {
                if (pos == a) break;
                if (pos < a + d) return false;
                shift += i - d;
            }
        }
        pos += shift;
        return true;
    }

    /// <summary>
    /// Maps the region <c>[start, end)</c> through the change for a marker that must survive edits (hex bookmarks). Bytes inserted at the start go before the region,
    /// bytes inserted at the end go after it, bytes inserted inside make it longer. An edit that covers an edge of the region is taken into the region: the start moves
    /// to the start of what replaced the bytes, the end to its end. False when nothing of the region is left.
    /// </summary>
    public static bool TryMapRegion(ref long start, ref long end, ChangeSet change)
    {
        long s = MapEdge(start, isEnd: false, change), e = MapEdge(end, isEnd: true, change);
        if (e <= s) return false;
        (start, end) = (s, e);
        return true;
    }

    private static long MapEdge(long pos, bool isEnd, ChangeSet change)
    {
        long shift = 0;
        foreach (var (a, d, i) in change.Edits)
        {
            if (pos < a) break;
            if (d == 0)
            {
                if (pos == a)
                {
                    if (!isEnd) shift += i;
                    break;
                }
                shift += i;
            }
            else
            {
                if (pos == a) break;
                if (pos < a + d) return isEnd ? a + shift + i : a + shift;
                shift += i - d;
            }
        }
        return pos + shift;
    }

    /// <summary>Running sum of <c>Inserted - Deleted</c> over the edits of a change: entry k is the shift of the first k edits.</summary>
    public static long[] ShiftPrefix(ChangeSet change)
    {
        var edits = change.Edits;
        var prefix = new long[edits.Count + 1];
        for (int k = 0; k < edits.Count; k++) prefix[k + 1] = prefix[k] + edits[k].Inserted - edits[k].Deleted;
        return prefix;
    }

    /// <summary>
    /// Same result as <see cref="TryMapPoint(ref long, AnchorGravity, ChangeSet)"/> in O(log n) for the edits of one change (ascending and disjoint),
    /// given <see cref="ShiftPrefix"/> of that change; many anchors through one big change need this.
    /// </summary>
    public static bool TryMapPoint(ref long pos, AnchorGravity gravity, ChangeSet change, long[] prefix)
    {
        var edits = change.Edits;
        int lo = 0, hi = edits.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (edits[mid].Start < pos) lo = mid + 1; else hi = mid;
        }
        if (lo > 0 && edits[lo - 1] is { Deleted: > 0 } last && pos < last.Start + last.Deleted) return false;
        long shift = prefix[lo];
        if (lo < edits.Count && edits[lo] is { Deleted: 0 } at && at.Start == pos && gravity == AnchorGravity.Right) shift += at.Inserted;
        pos += shift;
        return true;
    }
}
