using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex;

/// <summary>Why a replacement cannot be applied as it stands.</summary>
public enum ReplaceBlock
{
    None,
    /// <summary>Overwrite mode does not change the length of the file, but the replacement and a match differ in length.</summary>
    LengthInOverwrite,
}

/// <summary>
/// The preview of "replace all" in the hex view. <see cref="Inspection"/> is the byte inspector report of the edits (old and new bytes of every match, the
/// size before and after); nothing is changed until it is confirmed and applied with <see cref="ByteReplace.Apply"/>.
/// </summary>
/// <param name="Matches">Matches that will be replaced, without those that overlap an earlier one.</param>
/// <param name="LimitReached">The search stopped at its result limit, so more matches may exist.</param>
/// <param name="LengthDelta">New file length minus old.</param>
public sealed record ByteReplacePlan(ByteInspection Inspection, int Matches, bool LimitReached, long LengthDelta, ReplaceBlock Blocked)
{
    /// <summary>The replacement is not as long as what it replaces (a warning in the preview).</summary>
    public bool ChangesLength => LengthDelta != 0;
    public long Revision => Inspection.Revision;
}

/// <summary>
/// Replace bytes (F238). The preview names every match with its old and new bytes and says when the length changes; applying it is one undo step
/// and is refused unless the document is still at the revision the preview was made for. In overwrite mode a replacement that changes the length is refused.
/// </summary>
public static class ByteReplace
{
    /// <summary>The search half of the plan; reads a snapshot, so it may run on a worker thread. Without a limit in <paramref name="options"/> every match is found.</summary>
    public static ByteSearchResult Search(DocumentSnapshot snapshot, ByteQuery query, ByteRange? within = null, ByteSearchOptions? options = null, CancellationToken ct = default) =>
        ByteSearch.Find(snapshot, query, (options ?? new ByteSearchOptions { MaxResults = int.MaxValue }) with { Range = within ?? options?.Range }, ct);

    /// <summary>
    /// Plans the replacement of the matches of <paramref name="found"/> by <paramref name="replacement"/>; call it on the thread that owns the document.
    /// Matches that overlap an earlier match are left out: the leftmost one wins. An empty replacement deletes the matches.
    /// Null when the document is no longer at the revision the search read.
    /// </summary>
    /// <param name="overwrite">Overwrite mode: the length must not change.</param>
    public static ByteReplacePlan? Plan(Document doc, ByteSearchResult found, ReadOnlyMemory<byte> replacement, bool overwrite)
    {
        if (found.DocumentId != doc.Id || found.Revision != doc.Revision) return null;
        var edits = new List<ByteEdit>();
        long end = 0, delta = 0;
        var blocked = ReplaceBlock.None;
        foreach (var match in found.Matches)
        {
            if (match.Start < end) continue;
            end = match.End;
            edits.Add(new ByteEdit(match, replacement));
            delta += replacement.Length - match.Length;
            if (overwrite && replacement.Length != match.Length) blocked = ReplaceBlock.LengthInOverwrite;
        }
        var inspection = doc.InspectEdits(edits, "hex-replace", found.Revision, rawBytes: true);
        return inspection is null ? null : new ByteReplacePlan(inspection, edits.Count, found.LimitReached, delta, blocked);
    }

    /// <summary>Search and plan in one call, on the thread that owns the document.</summary>
    public static ByteReplacePlan Plan(Document doc, ByteQuery query, ReadOnlyMemory<byte> replacement, bool overwrite, ByteRange? within = null,
        ByteSearchOptions? options = null, CancellationToken ct = default) =>
        Plan(doc, Search(doc.GetSnapshot(), query, within, options, ct), replacement, overwrite)!;

    /// <summary>
    /// Writes the planned replacements as one undo step. Refused with <see cref="ConflictReason.InvalidRange"/> while the plan is blocked, with
    /// <see cref="ConflictReason.NotConfirmed"/> before <see cref="ByteInspection.Confirm"/> and with <see cref="ConflictReason.Overlap"/> when the document has a newer
    /// revision than the preview, even if the later change touched none of the matches.
    /// </summary>
    public static EditResult Apply(Document doc, ByteReplacePlan plan)
    {
        if (plan.Blocked != ReplaceBlock.None) return EditResult.Fail(ConflictReason.InvalidRange, doc.Revision);
        return doc.Apply(plan.Inspection);
    }
}
