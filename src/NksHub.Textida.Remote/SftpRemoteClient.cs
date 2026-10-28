using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;
using Renci.SshNet.Security;
using Renci.SshNet.Sftp;

namespace NksHub.Textida.Remote;

/// <summary>SFTP over SSH.NET. The host key is checked against <see cref="RemoteConnection.TrustedIdentity"/>; a key that differs is connected only after the user confirmed it.</summary>
public sealed class SftpRemoteClient(RemoteConnection connection, string? secret, IServerTrustPrompt trust) : IRemoteClient
{
    private SftpClient? _client;

    public RemoteConnection Connection { get; } = connection;
    public string? ConfirmedIdentity { get; private set; }

    /// <summary>The fingerprint format of OpenSSH: <c>SHA256:</c> and the unpadded base64 hash.</summary>
    public static string FormatFingerprint(string sha256Base64) => "SHA256:" + sha256Base64;

    /// <summary>The SHA-1 signature of RSA keys, which is the same name for a host key and for a login key. Weak; used only when the connection allows it.</summary>
    internal static readonly string[] Sha1Algorithms = ["ssh-rsa", "ssh-rsa-cert-v01@openssh.com"];

    /// <summary>The signature algorithms offered for a private key: the SHA-2 ones first, the SHA-1 <c>ssh-rsa</c> last and only when <paramref name="allowSha1"/> is set.</summary>
    internal static IReadOnlyCollection<HostAlgorithm> SignatureAlgorithms(IEnumerable<HostAlgorithm> all, bool allowSha1)
    {
        var list = all.ToList();
        return [.. list.Where(a => !Sha1Algorithms.Contains(a.Name)), .. list.Where(a => allowSha1 && Sha1Algorithms.Contains(a.Name))];
    }

    /// <summary>A private key whose signature algorithms are chosen here instead of by the library, which would try <c>ssh-rsa</c> (SHA-1) first.</summary>
    private sealed class KeySource(PrivateKeyFile file, bool allowSha1) : IPrivateKeySource
    {
        public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms { get; } = SignatureAlgorithms(file.HostKeyAlgorithms, allowSha1);
    }

    public async Task ConnectAsync(CancellationToken cancellation)
    {
        Connection.Validate();
        var info = BuildInfo();
        if (!Connection.AllowSshRsaSha1) foreach (var name in Sha1Algorithms) info.HostKeyAlgorithms.Remove(name);
        if (Connection.ConnectTimeoutSeconds is { } t) info.Timeout = TimeSpan.FromSeconds(t);
        var client = new SftpClient(info);
        bool rejected = false;
        client.HostKeyReceived += (_, e) =>
        {
            var identity = new ServerIdentity(IdentityKind.SshHostKey, FormatFingerprint(e.FingerPrintSHA256), e.HostKeyName);
            if (identity.Fingerprint == Connection.TrustedIdentity)
            {
                e.CanTrust = true;
                return;
            }
            var reason = Connection.TrustedIdentity is null ? TrustReason.FirstUse : TrustReason.Changed;
            bool ok = trust.ConfirmAsync(Connection, identity, reason, cancellation).AsTask().GetAwaiter().GetResult();
            e.CanTrust = ok;
            if (ok) ConfirmedIdentity = identity.Fingerprint; else rejected = true;
        };
        try
        {
            // on the pool: the host key callback waits for the user and must never run on the caller's (UI) thread
            await Task.Run(() => client.ConnectAsync(cancellation), cancellation).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            client.Dispose();
            if (rejected) throw new RemoteException(RemoteError.UntrustedServer, "The server identity was not confirmed, so the connection was not made.", e);
            throw e switch
            {
                SshAuthenticationException => new RemoteException(RemoteError.AuthenticationFailed, "The server refused the login.", e),
                SshConnectionException { DisconnectReason: DisconnectReason.KeyExchangeFailed } when !Connection.AllowSshRsaSha1 =>
                    new RemoteException(RemoteError.NoCommonAlgorithm, "The server and the editor share no key algorithm: " + e.Message, e),
                _ => new RemoteException(RemoteError.ConnectionFailed, "Cannot connect: " + e.Message, e),
            };
        }
        _client = client;
    }

    private ConnectionInfo BuildInfo()
    {
        var methods = new List<AuthenticationMethod>();
        if (Connection.Auth == SftpAuth.PrivateKey)
        {
            PrivateKeyFile key;
            try { key = secret is { Length: > 0 } ? new PrivateKeyFile(Connection.PrivateKeyPath!, secret) : new PrivateKeyFile(Connection.PrivateKeyPath!); }
            catch (Exception e) when (e is SshException or IOException) { throw new RemoteException(RemoteError.AuthenticationFailed, "The private key cannot be read: " + e.Message, e); }
            methods.Add(new PrivateKeyAuthenticationMethod(Connection.UserName, new KeySource(key, Connection.AllowSshRsaSha1)));
        }
        else
        {
            if (secret is null) throw new RemoteException(RemoteError.CredentialMissing, "There is no saved password for this connection.");
            methods.Add(new PasswordAuthenticationMethod(Connection.UserName, secret));
            var interactive = new KeyboardInteractiveAuthenticationMethod(Connection.UserName);
            interactive.AuthenticationPrompt += (_, e) =>
            {
                foreach (var p in e.Prompts) p.Response = secret;
            };
            methods.Add(interactive);
        }
        var proxy = Connection.Proxy;
        if (proxy.Type == ProxyType.None) return new ConnectionInfo(Connection.Host, Connection.EffectivePort, Connection.UserName, [.. methods]);
        var type = proxy.Type switch
        {
            ProxyType.Http => ProxyTypes.Http,
            ProxyType.Socks4 => ProxyTypes.Socks4,
            ProxyType.Socks5 => ProxyTypes.Socks5,
            _ => throw new RemoteException(RemoteError.UnsupportedCombination, $"Proxy type {proxy.Type} is not supported."),
        };
        return new ConnectionInfo(Connection.Host, Connection.EffectivePort, Connection.UserName, type, proxy.Host, proxy.Port, proxy.UserName, ProxyPassword, [.. methods]);
    }

    /// <summary>Password of the proxy, supplied by the factory from the credential store.</summary>
    public string? ProxyPassword { get; init; }

    private SftpClient Client => _client ?? throw new RemoteException(RemoteError.ConnectionLost, "Not connected.");

    private static async Task<T> Guard<T>(Func<Task<T>> action, CancellationToken cancellation = default)
    {
        // on the pool: the libraries are not written for a caller with a synchronization context (the UI thread)
        try { return await Task.Run(action).ConfigureAwait(false); }
        catch (Exception) when (cancellation.IsCancellationRequested) { throw new OperationCanceledException(cancellation); } // a cancelled transfer fails in many ways; none of them is a lost connection
        catch (SftpPathNotFoundException e) { throw new RemoteException(RemoteError.NotFound, e.Message, e); }
        catch (Exception e) when (e is SshConnectionException or SocketException or IOException or ObjectDisposedException or InvalidOperationException or SshOperationTimeoutException)
        {
            throw new RemoteException(RemoteError.ConnectionLost, "The connection to the server was lost: " + e.Message, e);
        }
        catch (Exception e) when (e is SshException)
        {
            throw new RemoteException(RemoteError.Failed, e.Message, e);
        }
    }

    private static async Task Guard(Func<Task> action, CancellationToken cancellation = default) =>
        await Guard(async () => { await action().ConfigureAwait(false); return 0; }, cancellation).ConfigureAwait(false);

    public Task<RemoteStat?> StatAsync(string path, CancellationToken cancellation) => Guard(async () =>
    {
        try
        {
            var a = await Client.GetAttributesAsync(path, cancellation).ConfigureAwait(false);
            return (RemoteStat?)new RemoteStat(a.Size, a.LastWriteTimeUtc, a.IsDirectory);
        }
        catch (SftpPathNotFoundException) { return null; }
    });

    public Task DownloadAsync(string path, Stream destination, CancellationToken cancellation) =>
        Guard(() => Client.DownloadFileAsync(path, destination, cancellation), cancellation);

    public Task ResumeDownloadAsync(string path, Stream destination, long offset, CancellationToken cancellation) => Guard(async () =>
    {
        await using var file = await Client.OpenAsync(path, FileMode.Open, FileAccess.Read, cancellation).ConfigureAwait(false);
        file.Seek(offset, SeekOrigin.Begin);
        await file.CopyToAsync(destination, cancellation).ConfigureAwait(false);
    }, cancellation);

    public Task UploadAsync(string path, Stream source, CancellationToken cancellation) =>
        Guard(() => Client.UploadFileAsync(source, path, true, null!, cancellation), cancellation);

    public Task ResumeUploadAsync(string path, Stream source, long offset, CancellationToken cancellation) => Guard(async () =>
    {
        await using var file = Client.Open(path, FileMode.Open, FileAccess.Write);
        file.Seek(offset, SeekOrigin.Begin);
        source.Seek(offset, SeekOrigin.Begin);
        await source.CopyToAsync(file, cancellation).ConfigureAwait(false);
    }, cancellation);

    public Task RenameAsync(string from, string to, CancellationToken cancellation) =>
        Guard(() => Client.RenameFileAsync(from, to, cancellation));

    public Task<bool> TryReplaceAsync(string from, string to, CancellationToken cancellation) => Guard(async () =>
    {
        try
        {
            await Task.Run(() => Client.RenameFile(from, to, true), cancellation).ConfigureAwait(false);
            return true;
        }
        catch (NotSupportedException) { return false; }
    });

    public Task DeleteAsync(string path, CancellationToken cancellation) => Guard(() => Client.DeleteFileAsync(path, cancellation));

    public Task<IReadOnlyList<RemoteEntry>> ListAsync(string directory, CancellationToken cancellation) => Guard(async () =>
    {
        var list = new List<RemoteEntry>();
        await foreach (var f in Client.ListDirectoryAsync(directory, cancellation).ConfigureAwait(false))
        {
            if (f.Name is "." or "..") continue;
            list.Add(new RemoteEntry(f.Name, f.FullName, new RemoteStat(f.Length, f.LastWriteTimeUtc, f.IsDirectory)));
        }
        return (IReadOnlyList<RemoteEntry>)list;
    });

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _client = null;
        return ValueTask.CompletedTask;
    }
}
