using System.Security.Cryptography;

namespace NksHub.Textida.Remote;

public enum TransferStep
{
    RemovingLeftover,
    Uploading,
    /// <summary>A verified partial temporary file from an earlier attempt is continued; <see cref="TransferEvent.Bytes"/> is the offset.</summary>
    Resuming,
    Replacing,
}

/// <summary>One step of an upload; the caller words it in the language of the user interface for the transfer log.</summary>
public readonly record struct TransferEvent(TransferStep Step, string Path, long Bytes);

/// <summary>Hooks and logging of one upload.</summary>
public sealed class TransferOptions
{
    /// <summary>Called at each step of the upload, for the transfer log in the output window.</summary>
    public Action<TransferEvent>? Log { get; init; }

    /// <summary>
    /// Runs after the file is completely on the server under its temporary name and before it replaces the target. It may throw to stop the replacement;
    /// <see cref="RemoteSync"/> uses it to check the target once more.
    /// </summary>
    public Func<CancellationToken, Task>? BeforeCommit { get; init; }
}

/// <summary>
/// Uploads a file so that the server holds either the old version or the new one, whole, at the target name. The data goes to a temporary name next to the
/// target first; only a complete, size-checked temporary file is renamed over the target. A failed or cancelled upload removes the temporary file and leaves the
/// target untouched. A temporary file that a lost connection or a cancel left behind is continued where it stopped, after its bytes were compared with the local file. Nothing here retries by itself: a retry is a new call and, through <see cref="RemoteSync"/>, a new version check.
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

        // The name carries the content hash, so a leftover belongs to this very content; it is continued only when what it holds equals the start of the local file.
        long offset = 0;
        if (await client.StatAsync(temp, cancellation).ConfigureAwait(false) is { } left)
        {
            if (!left.IsDirectory && left.Size > 0 && left.Size <= size && await MatchesLocalAsync(client, temp, localPath, cancellation).ConfigureAwait(false)) offset = left.Size;
            else
            {
                options.Log?.Invoke(new TransferEvent(TransferStep.RemovingLeftover, temp, 0));
                await client.DeleteAsync(temp, cancellation).ConfigureAwait(false);
            }
        }

        bool committed = false, keep = false;
        try
        {
            if (offset < size)
            {
                options.Log?.Invoke(new TransferEvent(offset > 0 ? TransferStep.Resuming : TransferStep.Uploading, temp, offset > 0 ? offset : size));
                try
                {
                    await using var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (offset > 0) await client.ResumeUploadAsync(temp, source, offset, cancellation).ConfigureAwait(false);
                    else await client.UploadAsync(temp, source, cancellation).ConfigureAwait(false);
                }
                catch (Exception e) when (e is OperationCanceledException || e is RemoteException { Error: RemoteError.ConnectionLost })
                {
                    keep = true; // the partial file stays so the next attempt can continue it
                    throw;
                }
            }

            var uploaded = await client.StatAsync(temp, cancellation).ConfigureAwait(false);
            if (uploaded is null || uploaded.Value.Size != size)
                throw new RemoteException(RemoteError.Failed, $"The server holds {uploaded?.Size.ToString() ?? "no"} bytes of {size}; the target was not touched.");

            if (offset > 0 && offset < size && !await MatchesLocalAsync(client, temp, localPath, cancellation).ConfigureAwait(false))
                throw new RemoteException(RemoteError.Failed, "The resumed upload does not match the local file; the target was not touched.");

            if (options.BeforeCommit is { } before) await before(cancellation).ConfigureAwait(false);

            options.Log?.Invoke(new TransferEvent(TransferStep.Replacing, remotePath, size));
            await ReplaceAsync(client, temp, remotePath, cancellation).ConfigureAwait(false);
            committed = true;
        }
        finally
        {
            if (!committed && !keep) await TryDeleteAsync(client, temp).ConfigureAwait(false);
        }
    }

    /// <summary>True when the server file holds exactly the first bytes of the local file (all of it when the sizes are equal).</summary>
    private static async Task<bool> MatchesLocalAsync(IRemoteClient client, string remote, string localPath, CancellationToken cancellation)
    {
        await using var local = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var compare = new CompareStream(local);
        await client.DownloadAsync(remote, compare, cancellation).ConfigureAwait(false);
        return compare.Equal;
    }

    /// <summary>A write-only stream that compares what is written with the start of another stream.</summary>
    private sealed class CompareStream(Stream expected) : Stream
    {
        private readonly byte[] _buffer = new byte[81920];
        public bool Equal { get; private set; } = true;

        public override void Write(ReadOnlySpan<byte> data)
        {
            while (Equal && data.Length > 0)
            {
                int n = expected.Read(_buffer, 0, Math.Min(_buffer.Length, data.Length));
                if (n == 0 || !data[..n].SequenceEqual(_buffer.AsSpan(0, n))) Equal = false;
                data = data[n..];
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
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

    /// <summary>Same as <see cref="HashOfAsync"/> without a task, for callers on the UI thread.</summary>
    public static string HashOf(string localPath)
    {
        using var s = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }

    public static async Task<string> HashOfAsync(string localPath, CancellationToken cancellation)
    {
        await using var s = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, cancellation).ConfigureAwait(false));
    }
}
