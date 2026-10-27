using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

/// <summary>
/// Storage under a <see cref="Document"/>. The bytes are the authority; the text is only a view decoded by <see cref="TextCodec"/>.
/// <see cref="Document"/> validates ranges and character boundaries before it calls <see cref="Replace"/>, so a backend may trust its arguments.
/// There is deliberately no whole-text member: a paged backend must never build one.
/// </summary>
public interface IByteBackend : IDisposable
{
    BackendCapabilities Capabilities { get; }
    TextCodec Codec { get; }
    long ByteLength { get; }
    /// <summary>File the backend currently maps, or null.</summary>
    string? BackingPath { get; }
    /// <summary>Raised when the backend itself changed the bytes (the user typed into a text control); not raised by <see cref="Replace"/>.</summary>
    event Action<BackendEdit>? EditedByBackend;

    int Read(long offset, Span<byte> destination);
    void Replace(long offset, long deleteLength, ReadOnlySpan<byte> insert);
    /// <summary>Text of a range whose ends are character boundaries.</summary>
    string ReadText(ByteRange range);
    /// <summary>
    /// Shows the same bytes through another codec: nothing is written and the length is unchanged. A backend that cannot hold the
    /// codec throws <see cref="NotSupportedException"/> before it changes anything.
    /// </summary>
    void Reinterpret(TextCodec codec);
    /// <summary>False when <see cref="Reinterpret"/> would refuse the codec (the paged backend scans newlines per byte, so no UTF-16/32).</summary>
    bool Supports(TextCodec codec);

    /// <summary>Line count so far and whether the index covers the whole document.</summary>
    (long Lines, bool Complete) LineIndexState { get; }
    /// <summary>False when the position is unknown (line index incomplete) or invalid; never a guess.</summary>
    bool TryGetPosition(long offset, out LinePosition position);
    bool TryGetOffset(LinePosition position, out long offset);

    IByteSnapshot CreateSnapshot();

    /// <summary>
    /// Called before the changed bytes of the file this backend maps are written into it in place. The scope receives the old bytes of every
    /// range just before it is overwritten, so snapshots taken earlier keep reading what they saw; null when the backend maps no file.
    /// </summary>
    IInPlaceWriteScope? BeginInPlaceWrite() => null;

    /// <summary>True when <see cref="ReplaceRepeated"/> keeps a repeated pattern as such, in memory that does not grow with its length.</summary>
    bool RepeatsPatterns => false;
    /// <summary>
    /// Replaces <paramref name="deleteLength"/> bytes by <paramref name="length"/> bytes of <paramref name="pattern"/> repeated from its first byte.
    /// Returns what was removed, for <see cref="RestoreRemoved"/>; it is good until the backing file is written over (<see cref="BeginInPlaceWrite"/>, <see cref="ReplaceBackingFile"/>).
    /// </summary>
    object ReplaceRepeated(long offset, long deleteLength, ReadOnlySpan<byte> pattern, long length) => throw new NotSupportedException();
    /// <summary>Undoes <see cref="ReplaceRepeated"/>: the <paramref name="insertedLength"/> bytes at <paramref name="offset"/> become what was removed.</summary>
    void RestoreRemoved(long offset, long insertedLength, object removed) => throw new NotSupportedException();

    /// <summary>True when edits made while a streaming save writes its snapshot are kept as unsaved changes of the new file (<see cref="BeginStreamingSave"/>).</summary>
    bool EditsDuringStreamingSave => false;
    /// <summary>
    /// The snapshot a save writes. A backend that returns true from <see cref="EditsDuringStreamingSave"/> keeps the edits made from now until
    /// <see cref="EndStreamingSave"/>, and <see cref="ReplaceBackingFile"/> applies them to the new file; the others are not edited during a save.
    /// </summary>
    IByteSnapshot BeginStreamingSave() => CreateSnapshot();
    /// <summary>Ends the window of <see cref="BeginStreamingSave"/>, after a swap, a failure or a cancel.</summary>
    void EndStreamingSave() { }

    /// <summary>
    /// Asks the backend to keep text it holds in files on disk (a large add buffer) in files named <paramref name="namePrefix"/>-* inside
    /// <paramref name="directory"/> that survive a crash, so a journal can refer to them (<see cref="TryGetDurableBytes"/>). Affects files made later.
    /// </summary>
    void UseDurableSpill(string directory, string namePrefix) { }
    /// <summary>True when the bytes at <paramref name="offset"/> are exactly one inserted block that lives in a durable file.</summary>
    bool TryGetDurableBytes(long offset, long length, out DurableBytes bytes)
    {
        bytes = default;
        return false;
    }
    /// <summary>Name of the durable file the backend is writing to now, if any.</summary>
    string? DurableFile => null;

    void CopyTo(Stream destination);
    /// <summary>
    /// <paramref name="tempPath"/> holds exactly the current bytes. Move it over <paramref name="targetPath"/> atomically;
    /// a backend that maps the target must release the mapping first and map the result afterwards.
    /// </summary>
    void ReplaceBackingFile(string tempPath, string targetPath);
}

/// <summary>Inserted bytes that live in the durable file <see cref="File"/> at <see cref="Offset"/>; <see cref="Sync"/> makes them survive a power loss.</summary>
public readonly record struct DurableBytes(string File, long Offset, long Length, Action Sync);

/// <summary>The window in which a file mapped by a backend is overwritten in place; disposing it accepts the file as it is then (its write time included).</summary>
public interface IInPlaceWriteScope : IDisposable
{
    /// <summary>The bytes of the file at <paramref name="offset"/> before they are overwritten.</summary>
    void Preserve(long offset, ReadOnlySpan<byte> old);
}

/// <summary>
/// Bytes of one revision, unchanged by document edits and saves; reads are safe from any thread.
/// File-backed snapshots may throw SnapshotInvalidatedException if the external source changes.
/// The paged backend checks length and write time on the retained handle before and after each read;
/// an in-place write that preserves both metadata values is not detected.
/// Write times may also be delayed while an external writer remains open.
/// </summary>
public interface IByteSnapshot
{
    long ByteLength { get; }
    int Read(long offset, Span<byte> destination);
}

/// <summary>Creates backends over bytes, so a document can move between backends in memory.</summary>
public interface IBackendFactory
{
    BackendCapabilities Capabilities { get; }
    IByteBackend Create(ReadOnlyMemory<byte> bytes, TextCodec codec);
    /// <summary>Opens a file; the default reads it whole.</summary>
    IByteBackend OpenFile(string path, TextCodec codec) => Create(File.ReadAllBytes(path), codec);
    /// <summary>True when <see cref="OpenFile"/> maps the file instead of reading it.</summary>
    bool MapsFiles => false;
}

public static class FileSwap
{
    /// <summary>
    /// Atomic replace of <paramref name="target"/> by <paramref name="temp"/>. A sharing violation is retried
    /// for about 1.5 s, because virus scanners and indexers open a freshly written file for a few milliseconds.
    /// </summary>
    public static void Replace(string temp, string target)
    {
        string backup = BackupOf(temp);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(target))
                {
                    TryDelete(backup);
                    File.Replace(temp, target, backup);
                }
                else File.Move(temp, target);
                TryDelete(backup);
                return;
            }
            catch (IOException e) when (attempt < 8 && IsTransientLock(e))
            {
                Thread.Sleep(10 << Math.Min(attempt, 6));
            }
        }
    }

    /// <summary>
    /// Where <see cref="Replace"/> parks the replaced file. Windows ReplaceFile renames the target away before it moves the
    /// temp in; a crash between the two leaves the target missing and the original content under this name.
    /// </summary>
    public static string BackupOf(string temp) => Path.ChangeExtension(temp, ".old.tmp");

    /// <summary>Undoes a <see cref="Replace"/> that died halfway: puts the original back when the target is missing, then drops the backup.</summary>
    public static void RecoverInterrupted(string temp, string target)
    {
        string backup = BackupOf(temp);
        if (!File.Exists(target) && File.Exists(backup)) File.Move(backup, target);
        TryDelete(backup);
    }

    /// <summary>
    /// Cleanup after a save that died inside <see cref="Replace"/> and whose temp name is unknown: when <paramref name="target"/>
    /// is missing, the newest parked original takes its place; other parked copies of it are deleted.
    /// </summary>
    public static void RecoverAbandoned(string target)
    {
        string? dir = Path.GetDirectoryName(target);
        if (dir is null || !Directory.Exists(dir)) return;
        string prefix = Path.GetFileName(target) + ".textida-";
        // a temp file of a save that died is a whole copy of the document, tens of GiB for a large one; a save still writing holds it open, so the delete fails and leaves it
        foreach (var stale in Directory.GetFiles(dir, prefix + "*.tmp").Where(p => !p.EndsWith(".old.tmp", StringComparison.OrdinalIgnoreCase))) TryDelete(stale);
        // shortcut: a second editor instance saving the same file at this very moment would lose its parked copy; acceptable until instances share a lock
        var parked = Directory.GetFiles(dir, prefix + "*.old.tmp")
            .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        if (parked.Count == 0) return;
        if (!File.Exists(target))
        {
            File.Move(parked[0], target);
            parked.RemoveAt(0);
        }
        foreach (var p in parked) TryDelete(p);
    }

    /// <summary>Deletes the file if it exists, retrying a transient lock like <see cref="Replace"/>; any other failure leaves the file.</summary>
    public static void TryDelete(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException e) when (attempt < 8 && IsTransientLock(e))
            {
                Thread.Sleep(10 << Math.Min(attempt, 6));
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
        }
    }

    // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION, ERROR_UNABLE_TO_REMOVE_REPLACED, ERROR_UNABLE_TO_MOVE_REPLACEMENT(_2).
    private static bool IsTransientLock(IOException e) => (e.HResult & 0xFFFF) is 32 or 33 or 1175 or 1176 or 1177;
}

/// <summary>Copy of the bytes taken at snapshot time.</summary>
public sealed class ArraySnapshot(byte[] bytes) : IByteSnapshot
{
    public long ByteLength => bytes.Length;

    public int Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > bytes.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        int n = (int)Math.Min(destination.Length, bytes.Length - offset);
        bytes.AsSpan((int)offset, n).CopyTo(destination);
        return n;
    }
}
