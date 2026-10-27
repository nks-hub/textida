using System.Security.Cryptography;

namespace NksHub.Textida.Remote;

/// <summary>Hooks and logging of one upload.</summary>
public sealed class TransferOptions
{
    /// <summary>One line per step ("uploading", "verifying", "replacing"), for the transfer log in the output window.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>
    /// Runs after the file is completely on the server under its temporary name and before it replaces the target. It may throw to stop the replacement;
    /// <see cref="RemoteSync"/> uses it to check the target once more.
    /// </summary>
    public Func<CancellationToken, Task>? BeforeCommit { get; init; }
}

/// <summary>
/// Uploads a file so that the server holds either the old version or the new one, whole, at the target name. The data goes to a temporary name next to the
/// target first; only a complete, size-checked temporary file is renamed over the target. A failed or cancelled upload removes the temporary file and leaves the
/// target untouched. Nothing here retries by itself: a retry is a new call and, through <see cref="RemoteSync"/>, a new version check.
/// </summary>
public static class RemoteTransfer
{
    private const string Marker = ".textida-";

    /// <summary>The temporary name of a target for a given content.</summary>
    public static string TempNameOf(string remotePath, string sha256Hex) => remotePath + Marker + sha256Hex[..12] + ".part";

    public static async Task UploadAtomicAsync(IRemoteClient client, string remotePath, string localPath, TransferOptions? options, CancellationToken cancellation)
    {
        options ??= new();
        string hash = await HashOfAsync(localPath, cancellation).ConfigureAwait(false);
        long size = new FileInfo(localPath).Length;
        string temp = TempNameOf(remotePath, hash);

        // A leftover of an earlier interrupted attempt is never resumed: its bytes were not verified.
        if (await client.StatAsync(temp, cancellation).ConfigureAwait(false) is not null)
        {
            options.Log?.Invoke($"removing the leftover {temp}");
            await client.DeleteAsync(temp, cancellation).ConfigureAwait(false);
        }

        bool committed = false;
        try
        {
            options.Log?.Invoke($"uploading {size} bytes to {temp}");
            await using (var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                await client.UploadAsync(temp, source, cancellation).ConfigureAwait(false);

            var uploaded = await client.StatAsync(temp, cancellation).ConfigureAwait(false);
            if (uploaded is null || uploaded.Value.Size != size)
                throw new RemoteException(RemoteError.Failed, $"The server holds {uploaded?.Size.ToString() ?? "no"} bytes of {size}; the target was not touched.");

            if (options.BeforeCommit is { } before) await before(cancellation).ConfigureAwait(false);

            options.Log?.Invoke($"replacing {remotePath}");
            await ReplaceAsync(client, temp, remotePath, cancellation).ConfigureAwait(false);
            committed = true;
        }
        finally
        {
            if (!committed) await TryDeleteAsync(client, temp).ConfigureAwait(false);
        }
    }

    private static async Task ReplaceAsync(IRemoteClient client, string temp, string target, CancellationToken cancellation)
    {
        if (await client.TryReplaceAsync(temp, target, cancellation).ConfigureAwait(false)) return;
        if (await client.StatAsync(target, cancellation).ConfigureAwait(false) is null)
        {
            await client.RenameAsync(temp, target, cancellation).ConfigureAwait(false);
            return;
        }

        // No atomic replace on this server: park the old file, move the new one in, drop the old one. A failure puts the old file back.
        string parked = target + Marker + Guid.NewGuid().ToString("N")[..8] + ".bak";
        await client.RenameAsync(target, parked, cancellation).ConfigureAwait(false);
        try
        {
            await client.RenameAsync(temp, target, cancellation).ConfigureAwait(false);
        }
        catch
        {
            try { await client.RenameAsync(parked, target, CancellationToken.None).ConfigureAwait(false); } catch (RemoteException) { /* RecoverAsync finishes this later */ }
            throw;
        }
        await TryDeleteAsync(client, parked).ConfigureAwait(false);
    }

    /// <summary>
    /// Cleans up after an upload that died with the connection: when <paramref name="remotePath"/> is missing and a parked old version is next to it, the old
    /// version comes back; leftover temporary files of the target are deleted. Returns true when it restored the target.
    /// </summary>
    public static async Task<bool> RecoverAsync(IRemoteClient client, string remotePath, CancellationToken cancellation)
    {
        int slash = remotePath.LastIndexOf('/');
        string dir = slash <= 0 ? "/" : remotePath[..slash];
        string prefix = remotePath[(slash + 1)..] + Marker;
        var leftovers = (await client.ListAsync(dir, cancellation).ConfigureAwait(false)).Where(e => e.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        bool restored = false;
        if (await client.StatAsync(remotePath, cancellation).ConfigureAwait(false) is null && leftovers.FirstOrDefault(e => e.Name.EndsWith(".bak", StringComparison.Ordinal)) is { } parked)
        {
            await client.RenameAsync(parked.FullPath, remotePath, cancellation).ConfigureAwait(false);
            leftovers.Remove(parked);
            restored = true;
        }
        foreach (var e in leftovers) await TryDeleteAsync(client, e.FullPath).ConfigureAwait(false);
        return restored;
    }

    private static async Task TryDeleteAsync(IRemoteClient client, string path)
    {
        try { await client.DeleteAsync(path, CancellationToken.None).ConfigureAwait(false); }
        catch (RemoteException) { /* the connection is gone or the file is; RecoverAsync cleans up on the next connect */ }
    }

    public static async Task<string> HashOfAsync(string localPath, CancellationToken cancellation)
    {
        await using var s = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, cancellation).ConfigureAwait(false));
    }
}
