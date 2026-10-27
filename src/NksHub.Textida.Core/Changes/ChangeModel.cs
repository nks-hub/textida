using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Changes;

/// <summary>
/// One replacement the user can accept or reject. <see cref="Range"/> is in bytes of the base content the file change names.
/// <see cref="LineBefore"/> and <see cref="LineAfter"/> are the rest of the first and last line around the range, so a view can
/// show the removed text in red and <see cref="NewText"/> in green with their context. A hunk with a <see cref="Problem"/> is shown but never applied.
/// </summary>
public sealed record Hunk(int Id, ByteRange Range, string OldText, string NewText, int Line, string LineBefore, string LineAfter, string? Problem = null)
{
    public bool CanApply => Problem is null;
}

public enum FileOperationKind { Create, Rename, Delete }

/// <summary>
/// A change of the file system instead of the text of a file. The <see cref="FileChange"/> that carries it names the file (the new file of a create, the old file of a
/// rename or a delete). <paramref name="NewPath"/> is the target of a rename; <paramref name="Content"/> is what a create writes.
/// </summary>
public sealed record FileOperation(FileOperationKind Kind, string? NewPath = null, byte[]? Content = null);

/// <summary>
/// The changes proposed for one target. A document target names the revision the ranges refer to; a file target names the
/// fingerprint (size and SHA-256) of the content they refer to. Anything else than that exact base is stale. A change with an <paramref name="Operation"/>
/// has no hunks: it is accepted or rejected as one change (a rename or delete is based on the fingerprint when it has one).
/// </summary>
public sealed record FileChange(string Key, string? Path, Guid? DocumentId, long? BaseRevision, FileFingerprint? BaseFingerprint, TextCodec Codec, IReadOnlyList<Hunk> Hunks, FileOperation? Operation = null)
{
    public bool IsDocument => DocumentId is not null;
    /// <summary>What the user can accept: the hunks, or one for a file operation.</summary>
    public int Changes => Operation is null ? Hunks.Count : 1;
    public string DisplayName => Path ?? Key;
}

/// <summary>
/// The single model of a proposed change: Replace in Files, recipes, plugins and agents all produce one and the same
/// preview/accept/apply path consumes it. <see cref="Origin"/> names the producer ("replace", "recipe:name", "agent:name");
/// it is kept in the change log of the documents it edits.
/// </summary>
public sealed record ChangeProposal(Guid Id, string Title, string Origin, IReadOnlyList<FileChange> Files)
{
    public int HunkCount => Files.Sum(f => f.Hunks.Count);
    /// <summary>Hunks and file operations together.</summary>
    public int ChangeCount => Files.Sum(f => f.Changes);
    /// <summary>
    /// All or nothing: when any accepted change is stale or cannot be done, nothing of the proposal is applied. File operations are undone if one of them fails.
    /// Without it each target stands alone (Replace in Files).
    /// </summary>
    public bool Atomic { get; init; }
}

/// <summary>What the user decided in the preview. Everything is accepted until rejected.</summary>
public sealed class ChangeSelection
{
    private readonly HashSet<string> _files = [];
    private readonly HashSet<(string, int)> _hunks = [];

    public ChangeSelection RejectFile(FileChange file)
    {
        _files.Add(file.Key);
        return this;
    }

    public ChangeSelection RejectHunk(FileChange file, int hunkId)
    {
        _hunks.Add((file.Key, hunkId));
        return this;
    }

    public ChangeSelection AcceptFile(FileChange file)
    {
        _files.Remove(file.Key);
        _hunks.RemoveWhere(h => h.Item1 == file.Key);
        return this;
    }

    public bool IsAccepted(FileChange file) => !_files.Contains(file.Key);
    public bool IsAccepted(FileChange file, Hunk hunk) => IsAccepted(file) && !_hunks.Contains((file.Key, hunk.Id));

    /// <summary>Hunks of the file that will be applied: accepted and without a problem.</summary>
    public IReadOnlyList<Hunk> Selected(FileChange file) => [.. file.Hunks.Where(h => h.CanApply && IsAccepted(file, h))];
}

public enum ChangeStatus
{
    /// <summary>All selected hunks are in.</summary>
    Applied,
    /// <summary>The user rejected the file, or no hunk was left to apply.</summary>
    Rejected,
    /// <summary>The base is stale; nothing of this file was changed. Build the preview again.</summary>
    Conflict,
    /// <summary>The file could not be written (read-only, locked, disk full); nothing of it was changed.</summary>
    Failed,
}

public enum ChangeConflict
{
    None,
    /// <summary>The document moved on to another revision than the one the proposal was made for.</summary>
    StaleRevision,
    /// <summary>The proposal names a document that is not open.</summary>
    DocumentUnavailable,
    /// <summary>The file differs from the content the proposal was made for.</summary>
    ExternalChange,
    Missing,
    /// <summary>A range is outside the content, overlaps another or does not fall on character boundaries.</summary>
    InvalidRange,
    /// <summary>A replacement text has characters the file's encoding cannot hold.</summary>
    EncodingLoss,
}

public sealed record FileOutcome(string Key, string DisplayName, ChangeStatus Status, ChangeConflict Conflict, int HunksApplied, int HunksSkipped, string? Message = null)
{
    /// <summary>True when the proposal is out of date and the producer should make a new preview of this file.</summary>
    public bool NeedsRepreview => Conflict is ChangeConflict.StaleRevision or ChangeConflict.ExternalChange or ChangeConflict.Missing or ChangeConflict.DocumentUnavailable;
}

/// <param name="BatchId">Id of the crash-recovery journal of the disk files; null when no journal directory was given or no disk file was touched.</param>
public sealed record ApplyReport(Guid ProposalId, IReadOnlyList<FileOutcome> Files, Guid? BatchId)
{
    public int Applied => Files.Count(f => f.Status == ChangeStatus.Applied);
    public int Rejected => Files.Count(f => f.Status == ChangeStatus.Rejected);
    public int Conflicts => Files.Count(f => f.Status == ChangeStatus.Conflict);
    public int Failed => Files.Count(f => f.Status == ChangeStatus.Failed);
    public int HunksApplied => Files.Sum(f => f.HunksApplied);

    public string Summary => $"{Applied} applied ({HunksApplied} replacements), {Rejected} skipped, {Conflicts} conflicts, {Failed} failed";
}
