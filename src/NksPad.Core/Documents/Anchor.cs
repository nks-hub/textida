namespace NksPad.Core.Documents;

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
}
