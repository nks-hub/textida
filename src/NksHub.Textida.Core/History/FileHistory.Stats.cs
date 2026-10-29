using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.History;

public sealed partial class FileHistory
{
    /// <summary>
    /// Raised with the path of a file when its revisions changed: one was recorded, a label or a pin was set, or retention deleted some. It is raised after the lock of the store is let go, on
    /// the thread that made the change (a save runs on the pool), so a listener that touches a window posts to its thread.
    /// </summary>
    public event Action<string>? FileChanged;

    private void RaiseChanged(string path) => FileChanged?.Invoke(path);

    /// <summary>
    /// Counts, for the revisions of a file that <paramref name="wanted"/> asks for (it gets a revision and the one before it), the lines the revision added to the one before it and removed
    /// from it, and reports each as soon as it is known, newest first. A revision is read once, from its own blob (a patch against its whole copy), never through the list of the store, so a
    /// long history costs one read per revision. A revision above <paramref name="maxRevisionBytes"/>, or one that cannot be read, is reported without a change, and so is the oldest revision,
    /// which has nothing before it. Runs on the thread that calls it; <paramref name="report"/> is called there too.
    /// </summary>
    public void CountChanges(string path, TextCodec codec, long maxRevisionBytes, Func<HistoryRevision, HistoryRevision?, bool> wanted, Action<HistoryRevision, RevisionChange?> report, CancellationToken ct = default)
    {
        IReadOnlyList<HistoryRevision> revisions;
        string folder;
        lock (_gate)
        {
            EnsureRoot();
            folder = FolderOf(path);
            revisions = ListFolder(folder);
        }
        (string Id, byte[]? Bytes)? last = null; // the revision read last is the one before the next one to count, so each is read once
        byte[]? Bytes(HistoryRevision r)
        {
            if (last is { } known && known.Id == r.Id) return known.Bytes;
            var bytes = r.Size <= maxRevisionBytes ? TryMaterialize(folder, r) : null;
            last = (r.Id, bytes);
            return bytes;
        }
        for (int i = 0; i < revisions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var revision = revisions[i];
            var before = i + 1 < revisions.Count ? revisions[i + 1] : null;
            if (!wanted(revision, before)) continue;
            if (before is null)
            {
                report(revision, null);
                continue;
            }
            var newer = Bytes(revision);
            var older = newer is null ? null : Bytes(before);
            report(revision, newer is not null && older is not null ? HistoryStats.Between(older, newer, codec, ct) : null);
        }
    }

    private byte[]? TryMaterialize(string folder, HistoryRevision revision)
    {
        try
        {
            using var ms = new MemoryStream((int)Math.Min(revision.Size, Array.MaxLength));
            Materialize(folder, revision, ms);
            return ms.ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }
}
