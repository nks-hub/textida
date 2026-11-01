using NksHub.Textida.Core.Compare;

namespace NksHub.Textida.Remote;

/// <summary>
/// A folder on an FTP, FTPS or SFTP server as a side of a comparison, through the client the editor already uses. The folders below it are read one list at a time (at most <see cref="MaxEntries"/> files, <see cref="MaxDepth"/> levels);
/// a file is written with the safe upload of <see cref="RemoteTransfer"/>, after the folders above it have been made when they are missing.
/// </summary>
public sealed class RemoteFolderSource(IRemoteClient client, string root, string label) : IFolderSource
{
    public const int MaxEntries = 200_000;
    public const int MaxDepth = 32;

    public string Label => label;

    private string Join(string path) => root.TrimEnd('/') + "/" + path.TrimStart('/');

    public async Task<IReadOnlyList<FolderFile>> ListAsync(CancellationToken ct)
    {
        var files = new List<FolderFile>();
        var pending = new Queue<(string Relative, int Depth)>();
        pending.Enqueue(("", 0));
        while (pending.Count > 0)
        {
            var (relative, depth) = pending.Dequeue();
            foreach (var entry in await client.ListAsync(relative.Length == 0 ? root : Join(relative), ct).ConfigureAwait(false))
            {
                if (entry.Name is "." or "..") continue;
                string path = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
                if (entry.Stat.IsDirectory)
                {
                    if (depth + 1 < MaxDepth) pending.Enqueue((path, depth + 1));
                }
                else
                {
                    if (files.Count >= MaxEntries) throw new IOException($"The folder has more than {MaxEntries} files.");
                    files.Add(new FolderFile(path, entry.Stat.Size, entry.Stat.ModifiedUtc));
                }
            }
        }
        return files;
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        string temp = Path.Combine(Path.GetTempPath(), "textida-remote-" + Guid.NewGuid().ToString("N")[..8]);
        var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await client.DownloadAsync(Join(path), stream, ct).ConfigureAwait(false);
            stream.Position = 0;
            return stream;
        }
        catch (Exception e)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            if (e is RemoteException) throw new IOException(e.Message, e); // the copy records an IOException per file and goes on
            throw;
        }
    }

    public async Task WriteFromFileAsync(string path, string source, DateTime? modifiedUtc, CancellationToken ct)
    {
        string full = Join(path);
        int slash = full.LastIndexOf('/');
        string folder = slash > 0 ? full[..slash] : "/";
        try
        {
            await RemoteTransfer.EnsureDirectoryAsync(client, folder, ct).ConfigureAwait(false);
            await RemoteTransfer.UploadAtomicAsync(client, full, source, null, ct).ConfigureAwait(false);
        }
        catch (RemoteException e)
        {
            throw new IOException(e.Message, e);
        }
    }
}
