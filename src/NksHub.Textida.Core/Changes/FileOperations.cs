using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Changes;

/// <summary>
/// Carries out the file operations of one proposal and can take them back. A create is written next to its target and moved in, a rename is a move, a delete parks the
/// file under a temporary name until <see cref="Commit"/>, so its content and attributes survive until the whole proposal is done. Nothing here is journaled:
/// a process that dies in the middle leaves the parked file (<c>*.textida-*.tmp</c>) behind, not a lost one.
/// </summary>
internal sealed class FileOperationRun
{
    private readonly List<Action> _undo = [];
    private readonly List<string> _parked = [];

    private static string Sidecar(string path) => $"{path}.textida-{Guid.NewGuid().ToString("N")[..8]}.tmp";

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

    /// <exception cref="IOException">The operation failed; what ran before is still to be undone with <see cref="RollBack"/>.</exception>
    public void Do(FileChange change)
    {
        var op = change.Operation!;
        string path = change.Path!;
        switch (op.Kind)
        {
            case FileOperationKind.Create:
                {
                    string temp = Sidecar(path);
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
                    string parked = Sidecar(path);
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
