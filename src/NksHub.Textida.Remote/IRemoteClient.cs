namespace NksHub.Textida.Remote;

/// <summary>The file operations the editor needs from a server, the same for SFTP and FTPS.</summary>
public interface IRemoteClient : IAsyncDisposable
{
    RemoteConnection Connection { get; }

    /// <summary>Connects, checks the server identity and logs in. The identity the user confirmed is available in <see cref="ConfirmedIdentity"/>.</summary>
    Task ConnectAsync(CancellationToken cancellation);

    /// <summary>Set when this connection was accepted after a confirmation (first use or a changed identity); the caller stores it in <see cref="RemoteConnection.TrustedIdentity"/>.</summary>
    string? ConfirmedIdentity { get; }

    /// <summary>The stat of a path, or null when it does not exist.</summary>
    Task<RemoteStat?> StatAsync(string path, CancellationToken cancellation);

    Task DownloadAsync(string path, Stream destination, CancellationToken cancellation);

    /// <summary>Writes the stream to <paramref name="path"/>, replacing whatever is there. Not atomic; <see cref="RemoteTransfer"/> builds the safe variant.</summary>
    Task UploadAsync(string path, Stream source, CancellationToken cancellation);

    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/>; fails when <paramref name="to"/> exists.</summary>
    Task RenameAsync(string from, string to, CancellationToken cancellation);

    /// <summary>
    /// Renames <paramref name="from"/> over an existing <paramref name="to"/> in one step on the server (SFTP posix-rename). Returns false and does nothing when the server cannot
    /// do that; <see cref="RemoteTransfer"/> then replaces the file by parking the old one under another name.
    /// </summary>
    Task<bool> TryReplaceAsync(string from, string to, CancellationToken cancellation);

    Task DeleteAsync(string path, CancellationToken cancellation);

    Task<IReadOnlyList<RemoteEntry>> ListAsync(string directory, CancellationToken cancellation);
}
