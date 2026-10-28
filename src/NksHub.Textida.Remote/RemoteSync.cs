using System.Security.Cryptography;

namespace NksHub.Textida.Remote;

public enum VersionCheck
{
    /// <summary>Size and modification time; the content is fetched only when they differ, to tell a real change from a touch.</summary>
    Stat,
    /// <summary>Always fetch the server content and compare it with what the copy was made from. Slower, and it also catches a change within the same second and size.</summary>
    Content,
}

public enum UploadStatus
{
    Uploaded,
    /// <summary>The local file equals the server version; nothing was sent.</summary>
    NothingToSend,
    /// <summary>The server file is not the version the copy was made from. Nothing was written; <see cref="UploadOutcome.ServerContent"/> feeds the diff.</summary>
    Conflict,
}

/// <param name="Copy">The copy after the call (rebased after an upload).</param>
/// <param name="Server">On a conflict, the server version that was found (<see cref="RemoteRevision.None"/> when it was deleted).</param>
/// <param name="ServerContent">On a conflict, the bytes of the server file for a diff; null when the file is gone.</param>
public sealed record UploadOutcome(UploadStatus Status, WorkingCopy Copy, RemoteRevision? Server = null, byte[]? ServerContent = null);

/// <summary>
/// Sends a local copy to the server only when the server file is still the version the copy was made from. If it is not, nothing is written and the caller gets the
/// server content for a diff. The guarantee has a protocol limit: between the last check and the rename that puts the new file in place the server may change
/// the file, and neither SFTP nor FTP has a conditional write to close that gap. The window is the time of one stat and one rename, after the whole upload has
/// already finished under a temporary name; see <see cref="Limit"/>.
/// </summary>
public static class RemoteSync
{
    /// <summary>Plain-language statement of the limit for the user interface.</summary>
    public const string Limit = "The check runs just before the new file replaces the old one, but the server cannot make check and replace one step. A change in that last moment is not detected.";

    /// <param name="expected">The server version the user has seen and accepted (after looking at a diff). Null means the version the copy was made from.</param>
    public static async Task<UploadOutcome> UploadAsync(IRemoteClient client, WorkingCopyStore store, WorkingCopy copy, VersionCheck mode,
        RemoteRevision? expected, TransferOptions? options, CancellationToken cancellation)
    {
        options ??= new();
        string localHash = await RemoteTransfer.HashOfAsync(copy.LocalPath, cancellation).ConfigureAwait(false);
        var expectation = expected ?? copy.Base;
        if (expected is null && localHash == copy.Base.Sha256) return new UploadOutcome(UploadStatus.NothingToSend, copy);

        var found = await FindServerVersionAsync(client, copy, expectation, mode, cancellation).ConfigureAwait(false);
        if (found.Conflict is { } conflict) return new UploadOutcome(UploadStatus.Conflict, copy, conflict.Revision, conflict.Content);
        copy = found.Copy;

        // The temporary file is complete before this runs; it narrows the unprotected window to a stat and a rename.
        var seen = found.Seen;
        var guarded = new TransferOptions
        {
            Log = options.Log,
            Progress = options.Progress,
            BeforeCommit = async ct =>
            {
                if (options.BeforeCommit is { } outer) await outer(ct).ConfigureAwait(false);
                var now = await client.StatAsync(copy.RemotePath, ct).ConfigureAwait(false);
                bool same = seen is null ? now is null : now is not null && now.Value.Size == seen.Value.Size && SameTime(now.Value.ModifiedUtc, seen.Value.ModifiedUtc);
                if (!same) throw new RemoteException(RemoteError.ChangedOnServer, "The file changed on the server during the upload; nothing was replaced.");
            },
        };
        try
        {
            await RemoteTransfer.UploadAtomicAsync(client, copy.RemotePath, copy.LocalPath, guarded, cancellation).ConfigureAwait(false);
        }
        catch (RemoteException e) when (e.Error == RemoteError.ChangedOnServer)
        {
            var latest = await ReadServerAsync(client, copy.RemotePath, cancellation).ConfigureAwait(false);
            return new UploadOutcome(UploadStatus.Conflict, copy, latest.Revision, latest.Bytes);
        }

        var stat = await client.StatAsync(copy.RemotePath, cancellation).ConfigureAwait(false);
        var revision = new RemoteRevision(stat?.Size ?? new FileInfo(copy.LocalPath).Length, stat?.ModifiedUtc, localHash);
        return new UploadOutcome(UploadStatus.Uploaded, store.Rebase(copy, revision));
    }

    private static bool SameTime(DateTime? a, DateTime? b) => a is null || b is null || RemoteRevision.Whole(a.Value) == RemoteRevision.Whole(b.Value);

    private readonly record struct Found(WorkingCopy Copy, RemoteStat? Seen, (RemoteRevision Revision, byte[]? Content)? Conflict);

    /// <summary>The server file as it is now: its revision and bytes, or <see cref="RemoteRevision.None"/> and null when it does not exist.</summary>
    public static async Task<(RemoteRevision Revision, byte[]? Bytes)> ReadServerAsync(IRemoteClient client, string path, CancellationToken cancellation)
    {
        var stat = await client.StatAsync(path, cancellation).ConfigureAwait(false);
        if (stat is null) return (RemoteRevision.None, null);
        using var ms = new MemoryStream();
        await client.DownloadAsync(path, ms, cancellation).ConfigureAwait(false);
        var bytes = ms.ToArray();
        return (new RemoteRevision(stat.Value.Size, stat.Value.ModifiedUtc, Convert.ToHexStringLower(SHA256.HashData(bytes))), bytes);
    }

    /// <summary>Looks at the server file and says whether it is still <paramref name="expected"/>; <see cref="RemoteRevision.None"/> expects no file at all.</summary>
    private static async Task<Found> FindServerVersionAsync(IRemoteClient client, WorkingCopy copy, RemoteRevision expected, VersionCheck mode, CancellationToken cancellation)
    {
        var stat = await client.StatAsync(copy.RemotePath, cancellation).ConfigureAwait(false);
        if (stat is null) return expected.IsNone ? new Found(copy, null, null) : new Found(copy, null, (RemoteRevision.None, null));
        if (expected.IsNone) return await ConflictAsync().ConfigureAwait(false);
        if (mode == VersionCheck.Stat && expected.Matches(stat.Value)) return new Found(copy, stat, null);

        var server = await ReadServerAsync(client, copy.RemotePath, cancellation).ConfigureAwait(false);
        // Touched or saved again with the same content: not a change.
        return server.Revision.Sha256 == expected.Sha256 ? new Found(copy, stat, null) : new Found(copy, stat, (server.Revision, server.Bytes));

        async Task<Found> ConflictAsync()
        {
            var s = await ReadServerAsync(client, copy.RemotePath, cancellation).ConfigureAwait(false);
            return new Found(copy, stat, (s.Revision, s.Bytes));
        }
    }
}
