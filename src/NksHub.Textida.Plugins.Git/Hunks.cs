namespace NksHub.Textida.Plugins.Git;

/// <summary>A changed part of the working tree document as the diff API lists it: the byte range on the tree side.</summary>
public sealed record HunkSpan(long Index, long Start, long End);

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
}
