using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Changes;

/// <summary>
/// Carries out the file operations of one proposal and can take them back. A create is written next to its target and moved in, a rename is a move, a delete parks the
/// file under a temporary name until <see cref="Commit"/>, so its content and attributes survive until the whole proposal is done. The applier journals every operation
/// before the first one runs (see <see cref="OperationRecovery"/>), so a process that dies in the middle is finished or rolled back at the next start.
/// </summary>
internal sealed class FileOperationRun
{
    private readonly List<Action> _undo = [];
    private readonly List<string> _parked = [];

    internal static string Sidecar(string path) => $"{path}.textida-{Guid.NewGuid().ToString("N")[..8]}.tmp";

    /// <summary>Why the operation cannot be done now, or null when it can. Checked for every operation before the first one runs.</summary>
    public static (ChangeConflict Reason, string Message)? Check(FileChange change)
    {
        var op = change.Operation!;
        string path = change.Path!;
        switch (op.Kind)
        {
            case FileOperationKind.Create:
                if (File.Exists(path) || Directory.Exists(path)) return (ChangeConflict.ExternalChange, "The file already exists.");
                if (Path.GetDirectoryName(path) is not { Length: > 0 } parent || !Directory.Exists(parent)) return (ChangeConflict.Missing, "The folder of the new file is gone.");
                return null;
            case FileOperationKind.Rename:
                {
                    if (op.NewPath is not { Length: > 0 } target) throw new ArgumentException($"Rename '{change.Key}' has no target.");
                    if (!File.Exists(path)) return (ChangeConflict.Missing, "The file is gone.");
                    if (Differs(change)) return (ChangeConflict.ExternalChange, "The file was changed by another program after the preview; make a new preview.");
                    bool sameFile = string.Equals(path, target, StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows();
                    if (!sameFile && (File.Exists(target) || Directory.Exists(target))) return (ChangeConflict.ExternalChange, "The new name is taken.");
                    if (Path.GetDirectoryName(target) is not { Length: > 0 } folder || !Directory.Exists(folder)) return (ChangeConflict.Missing, "The folder of the new name is gone.");
                    return null;
                }
            default:
                if (!File.Exists(path)) return (ChangeConflict.Missing, "The file is gone.");
                if (change.BaseFingerprint is null) throw new ArgumentException($"Delete '{change.Key}' has no fingerprint.");
                return Differs(change) ? (ChangeConflict.ExternalChange, "The file was changed by another program after the preview; make a new preview.") : null;
        }
    }

    private static bool Differs(FileChange change) =>
        change.BaseFingerprint is { } expected && (FileFingerprint.TryRead(change.Path!) is not { } now || !now.SameContent(expected));

    /// <param name="change">The operation to do.</param>
    /// <param name="sidecar">The temporary name a create writes to, or the name a delete parks the file under; chosen when omitted. The journal names it so recovery can find it.</param>
    /// <exception cref="IOException">The operation failed; what ran before is still to be undone with <see cref="RollBack"/>.</exception>
    public void Do(FileChange change, string? sidecar = null)
    {
        var op = change.Operation!;
        string path = change.Path!;
        switch (op.Kind)
        {
            case FileOperationKind.Create:
                {
                    string temp = sidecar ?? Sidecar(path);
                    try
                    {
                        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                        {
                            fs.Write(op.Content ?? []);
                            fs.Flush(true);
                        }
                        File.Move(temp, path);
                    }
                    finally
                    {
                        FileSwap.TryDelete(temp);
                    }
                    _undo.Add(() => File.Delete(path));
                    break;
                }
            case FileOperationKind.Rename:
                {
                    string target = op.NewPath!;
                    File.Move(path, target);
                    _undo.Add(() => File.Move(target, path));
                    break;
                }
            default:
                {
                    string parked = sidecar ?? Sidecar(path);
                    File.Move(path, parked);
                    _parked.Add(parked);
                    _undo.Add(() => File.Move(parked, path));
                    break;
                }
        }
    }

    /// <summary>Undoes what was done, last first. The text of any problem that stopped an undo is returned; the rest is still undone.</summary>
    public string? RollBack()
    {
        string? problem = null;
        for (int i = _undo.Count - 1; i >= 0; i--)
        {
            try { _undo[i](); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { problem ??= e.Message; }
        }
        _undo.Clear();
        _parked.Clear();
        return problem;
    }

    /// <summary>The proposal is done: parked files are deleted for good.</summary>
    public void Commit()
    {
        foreach (var path in _parked) FileSwap.TryDelete(path);
        _parked.Clear();
        _undo.Clear();
    }
}

/// <summary>
/// Finishes or rolls back the file operations of a batch whose process died. What was done is told from the files, not from the journal: a create is done when its target holds the planned content,
/// a rename when the old name is gone and the new one is there, a delete when the file is gone (parked under its sidecar name until the batch ended, or deleted for good). Everything is safe to repeat.
/// </summary>
internal static class OperationRecovery
{
    public static BatchFileState Classify(BatchOperationPlan o)
    {
        switch (o.Kind)
        {
            case FileOperationKind.Create:
                {
                    if (!File.Exists(o.Path)) return Directory.Exists(o.Path) ? BatchFileState.Diverged : BatchFileState.NotStarted;
                    var now = FileFingerprint.TryRead(o.Path);
                    return now is not null && now.Size == o.Content!.Length && now.Sha256 == o.ContentSha ? BatchFileState.Applied : BatchFileState.Diverged;
                }
            case FileOperationKind.Rename:
                {
                    string target = o.NewPath!;
                    bool from = File.Exists(o.Path), to = File.Exists(target);
                    if (OperatingSystem.IsWindows() && string.Equals(o.Path, target, StringComparison.OrdinalIgnoreCase))
                        return !from ? BatchFileState.Missing : HasName(target) ? BatchFileState.Applied : BatchFileState.NotStarted;
                    if (from && to) return BatchFileState.Diverged;
                    if (!from && !to) return BatchFileState.Missing;
                    return !Matches(o, from ? o.Path : target) ? BatchFileState.Diverged : from ? BatchFileState.NotStarted : BatchFileState.Applied;
                }
            default:
                {
                    if (File.Exists(o.Path)) return Matches(o, o.Path) ? BatchFileState.NotStarted : BatchFileState.Diverged;
                    if (File.Exists(o.Sidecar)) return Matches(o, o.Sidecar) ? BatchFileState.Applied : BatchFileState.Diverged;
                    return BatchFileState.Applied;
                }
        }
    }

    /// <summary>The name the file has on disk, which tells a rename that only changed the case from the file it started from.</summary>
    private static bool HasName(string path) =>
        Path.GetDirectoryName(path) is { } folder && Directory.EnumerateFiles(folder, Path.GetFileName(path)).Any(f => string.Equals(Path.GetFileName(f), Path.GetFileName(path), StringComparison.Ordinal));

    private static bool Matches(BatchOperationPlan o, string path)
    {
        if (o.BaseSha is null) return true;
        var now = FileFingerprint.TryRead(path);
        return now is not null && now.Size == o.BaseSize && now.Sha256 == o.BaseSha;
    }

    /// <summary>Carries out one operation of the interrupted batch (<paramref name="finish"/>) or takes back what it had done.</summary>
    public static FileOutcome Run(BatchOperationPlan o, bool finish)
    {
        FileOutcome Make(ChangeStatus status, ChangeConflict conflict, string? message = null) =>
            new(o.Path, o.Path, status, conflict, status == ChangeStatus.Applied ? 1 : 0, status == ChangeStatus.Applied ? 0 : 1, message);
        try
        {
            // the temporary file of a create that died before it was moved in
            if (o.Kind == FileOperationKind.Create) FileSwap.TryDelete(o.Sidecar);
            switch (Classify(o), finish)
            {
                case (BatchFileState.NotStarted, true):
                    {
                        var run = new FileOperationRun();
                        run.Do(new FileChange(o.Path, o.Path, null, null, null, TextCodec.Utf8, [], new FileOperation(o.Kind, o.NewPath, o.Content)), o.Sidecar);
                        run.Commit();
                        return Make(ChangeStatus.Applied, ChangeConflict.None, "finished after the interruption");
                    }
                case (BatchFileState.Applied, true):
                    if (o.Kind == FileOperationKind.Delete) FileSwap.TryDelete(o.Sidecar);
                    return Make(ChangeStatus.Applied, ChangeConflict.None, "was already applied");
                case (BatchFileState.Applied, false):
                    switch (o.Kind)
                    {
                        case FileOperationKind.Create:
                            File.Delete(o.Path);
                            break;
                        case FileOperationKind.Rename:
                            File.Move(o.NewPath!, o.Path);
                            break;
                        default:
                            if (!File.Exists(o.Sidecar)) return Make(ChangeStatus.Conflict, ChangeConflict.Missing, "was deleted for good; it cannot be restored");
                            File.Move(o.Sidecar, o.Path);
                            break;
                    }
                    return Make(ChangeStatus.Rejected, ChangeConflict.None, "restored to the original state");
                case (BatchFileState.NotStarted, false):
                    return Make(ChangeStatus.Rejected, ChangeConflict.None, "was never changed");
                case (BatchFileState.Missing, _):
                    return Make(ChangeStatus.Conflict, ChangeConflict.Missing);
                default:
                    return Make(ChangeStatus.Conflict, ChangeConflict.ExternalChange, "changed by someone else; left as it is");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Make(ChangeStatus.Failed, ChangeConflict.None, e.Message);
        }
    }
}
