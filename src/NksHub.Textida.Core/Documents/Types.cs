using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

/// <summary>Half-open byte range <c>[Start, End)</c> of one document revision.</summary>
public readonly record struct ByteRange
{
    public ByteRange(long start, long end)
    {
        if (start < 0 || end < start) throw new ArgumentOutOfRangeException(nameof(start), $"Invalid range [{start}, {end}).");
        Start = start;
        End = end;
    }

    public long Start { get; }
    public long End { get; }
    public long Length => End - Start;
    public bool IsEmpty => Start == End;
    public override string ToString() => $"[{Start}, {End})";
}

/// <summary>Zero-based line and UTF-16 column of the text view.</summary>
public readonly record struct LinePosition(long Line, long Column);

/// <summary>Replaces <see cref="Range"/> with <see cref="Insert"/>; both refer to the revision the edit is applied against.</summary>
public readonly record struct ByteEdit(ByteRange Range, ReadOnlyMemory<byte> Insert)
{
    public static ByteEdit InsertAt(long offset, ReadOnlyMemory<byte> bytes) => new(new(offset, offset), bytes);
    public static ByteEdit Delete(ByteRange range) => new(range, default);
}

/// <summary>An edit the backend applied on its own (the user typed into a text control); the offset is in the revision before it.</summary>
public readonly record struct BackendEdit(long Offset, byte[] Removed, byte[] Inserted);

public enum ConflictReason
{
    None,
    /// <summary>A later change touched the range, or the range is ambiguous (insertion inside it, empty range at a change).</summary>
    Overlap,
    /// <summary>The change log no longer reaches back to the requested revision.</summary>
    LogTruncated,
    FutureRevision,
    /// <summary>Two edits of one transaction overlap.</summary>
    EditsOverlap,
    /// <summary>Outside the document, inside the BOM, or not on a character boundary (also after the edit).</summary>
    InvalidRange,
    /// <summary>The change would lose characters and no replacement was confirmed.</summary>
    LossNotConfirmed,
    /// <summary>A save is running on a snapshot of the current revision; the document takes no change until it ends.</summary>
    Saving,
    /// <summary>An inspected action was applied before the user confirmed what it changes.</summary>
    NotConfirmed,
}

public readonly record struct EditResult(bool Applied, long Revision, ConflictReason Conflict)
{
    public static EditResult Fail(ConflictReason reason, long revision) => new(false, revision, reason);
}

public readonly record struct RangeMapResult(bool Ok, ByteRange Range, ConflictReason Conflict);

/// <summary>
/// Where a position of an older revision stands now. When <see cref="Ok"/> is false, <see cref="ConflictReason.Overlap"/> means a later change
/// deleted the text the position was in; <see cref="ConflictReason.FutureRevision"/> and <see cref="ConflictReason.LogTruncated"/> mean the log cannot say.
/// </summary>
public readonly record struct PointMapResult(bool Ok, long Offset, ConflictReason Conflict);

/// <summary>Result of undo or redo: the document revision after it and the selections the step restored.</summary>
public sealed record UndoResult(long Revision, IReadOnlyList<ByteRange> Selections);

/// <summary>Options of one edit transaction.</summary>
public sealed class EditOptions
{
    /// <summary>Revision the edit ranges refer to; null means the current one. Older revisions are mapped through the change log.</summary>
    public long? BaseRevision { get; init; }
    /// <summary>Consecutive edits with the same key (and no save in between) share one undo step.</summary>
    public string? CoalesceKey { get; init; }
    public IReadOnlyList<ByteRange>? SelectionsBefore { get; init; }
    public IReadOnlyList<ByteRange>? SelectionsAfter { get; init; }
    /// <summary>Names who edited (user, recipe, MCP); kept in the change log.</summary>
    public string? Source { get; init; }
    /// <summary>Byte-level edit (hex view): skips the BOM and character boundary checks, so any byte can be written anywhere.</summary>
    public bool RawBytes { get; init; }
}

/// <summary><c>View</c> changed how the bytes are read (re-read as another encoding) and no byte.</summary>
public enum ChangeKind { Edit, Undo, Redo, Backend, View }

/// <summary>One change as stored in the log: edits in the coordinates of the previous revision, ascending and disjoint.</summary>
public sealed record ChangeSet(long Revision, ChangeKind Kind, string? Source, IReadOnlyList<ChangeSet.Span> Edits)
{
    /// <summary>False when no byte that was inserted or removed is a CR or LF byte, so the line endings of the document cannot have changed; true when unknown.</summary>
    public bool TouchesLineBreaks { get; init; } = true;

    public readonly record struct Span(long Start, long Deleted, long Inserted)
    {
        /// <summary>When set, the inserted bytes are this pattern repeated from its first byte (<see cref="Document.Fill"/>); a journal then needs no copy of them.</summary>
        public byte[]? Pattern { get; init; }
    }
}

public sealed class DocumentChangedEventArgs(ChangeSet change) : EventArgs
{
    public ChangeSet Change { get; } = change;

    /// <summary>The undo step of the change while its handlers run (null when it has none); the journal takes the inserted bytes from it instead of reading them back.</summary>
    internal object? Step { get; init; }
}

public sealed class DocumentOptions
{
    /// <summary>Number of change log entries kept; a request based on an older revision is a conflict.</summary>
    public int ChangeLogCapacity { get; init; } = 4096;
    /// <summary>Forces the encoding instead of detecting it.</summary>
    public TextCodec? Format { get; init; }
}

public enum AnchorGravity
{
    /// <summary>Text inserted exactly at the anchor goes after it; the anchor keeps its offset.</summary>
    Left,
    /// <summary>Text inserted exactly at the anchor goes before it; the anchor moves behind the new text.</summary>
    Right,
}

/// <summary>What a backend can and cannot do; the mode picker (P1.2.2) reads this instead of hard limits.</summary>
public sealed record BackendCapabilities(string Name, long MaxByteLength, bool SupportsUtf16, bool PositionsMayBeUnknown, bool MaterializesText);
