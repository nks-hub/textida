namespace NksHub.Textida.Plugins.Git;

/// <summary>A changed part of the working tree document as the diff API lists it: the byte range on the tree side.</summary>
public sealed record HunkSpan(long Index, long Start, long End);

/// <summary>A hunk with its byte range on both sides of the diff.</summary>
public sealed record HunkPair(long Index, long LeftStart, long LeftEnd, long RightStart, long RightEnd)
{
    public HunkSpan Left => new(Index, LeftStart, LeftEnd);
    public HunkSpan Right => new(Index, RightStart, RightEnd);
}

public static class Hunks
{
    /// <summary>
    /// The hunk the caret stands in: inside its byte range, a caret at the very end counts only at the end of the document. A hunk that removes lines (empty on the tree
    /// side) is the one at the caret that stands exactly where the lines were. Null when the caret is in no change.
    /// </summary>
    public static HunkSpan? At(IEnumerable<HunkSpan> hunks, long caret, long documentLength)
    {
        foreach (var h in hunks)
        {
            if (h.Start == h.End ? caret == h.Start : caret >= h.Start && (caret < h.End || (caret == h.End && h.End == documentLength))) return h;
        }
        return null;
    }

    /// <summary>
    /// Moves a caret of the right document of a diff to the left document: a caret in a change goes to the start of what the change replaced, a caret after changes is moved by
    /// how much they added or removed. Hunks are in file order.
    /// </summary>
    public static long ToLeft(IEnumerable<HunkPair> hunks, long caret)
    {
        long delta = 0;
        foreach (var h in hunks)
        {
            if (caret < h.RightStart) break;
            if (caret < h.RightEnd || (h.RightStart == h.RightEnd && caret == h.RightStart)) return h.LeftStart;
            delta += (h.LeftEnd - h.LeftStart) - (h.RightEnd - h.RightStart);
        }
        return caret + delta;
    }
}
