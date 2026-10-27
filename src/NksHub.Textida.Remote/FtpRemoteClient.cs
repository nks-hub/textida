using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentFTP;
using FluentFTP.Exceptions;
using FluentFTP.Proxy.AsyncProxy;

namespace NksHub.Textida.Remote;

/// <summary>
/// FTPS (explicit or implicit TLS) over FluentFTP, and plain FTP when the connection allows it. The certificate is accepted when it matches the trusted
/// hash, or when it validates against the system roots and nothing was pinned; every other certificate is shown to the user first.
/// </summary>
public sealed class FtpRemoteClient(RemoteConnection connection, string? secret, IServerTrustPrompt trust) : IRemoteClient
{
    private AsyncFtpClient? _client;

    public RemoteConnection Connection { get; } = connection;
    public string? ConfirmedIdentity { get; private set; }

    /// <summary>Password of the proxy, supplied by the factory from the credential store.</summary>
    public string? ProxyPassword { get; init; }

    public static string FormatFingerprint(X509Certificate certificate) =>
        Convert.ToHexString(new X509Certificate2(certificate).GetCertHash(HashAlgorithmName.SHA256));

    public async Task ConnectAsync(CancellationToken cancellation)
    {
        Connection.Validate();
        if (secret is null) throw new RemoteException(RemoteError.CredentialMissing, "There is no saved password for this connection.");
        var client = CreateClient();
        client.Config.EncryptionMode = Connection.Protocol switch
        {
            RemoteProtocol.Ftp => FtpEncryptionMode.None,
            _ => Connection.FtpsMode == FtpsMode.Implicit ? FtpEncryptionMode.Implicit : FtpEncryptionMode.Explicit,
        };
        // Explicit mode normally degrades silently to plain FTP when the server refuses TLS; here that must be an error.
        client.Config.DataConnectionEncryption = Connection.Protocol == RemoteProtocol.Ftps;
        if (Connection.ConnectTimeoutSeconds is { } t) client.Config.ConnectTimeout = t * 1000;
        bool rejected = false;
        client.ValidateCertificate += (_, e) =>
        {
            var identity = new ServerIdentity(IdentityKind.TlsCertificate, FormatFingerprint(e.Certificate!), e.Certificate!.Subject);
            if (identity.Fingerprint.Equals(Connection.TrustedIdentity, StringComparison.OrdinalIgnoreCase)) { e.Accept = true; return; }
            if (Connection.TrustedIdentity is null && e.PolicyErrors == SslPolicyErrors.None) { e.Accept = true; return; }
            var reason = Connection.TrustedIdentity is null ? TrustReason.Invalid : TrustReason.Changed;
            bool ok = trust.ConfirmAsync(Connection, identity, reason, cancellation).AsTask().GetAwaiter().GetResult();
            e.Accept = ok;
            if (ok) ConfirmedIdentity = identity.Fingerprint; else rejected = true;
        };
        try
        {
            await client.Connect(cancellation).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            client.Dispose();
            if (rejected) throw new RemoteException(RemoteError.UntrustedServer, "The server certificate was not confirmed, so the connection was not made.", e);
            throw e switch
            {
                FtpAuthenticationException => new RemoteException(RemoteError.AuthenticationFailed, "The server refused the login.", e),
                FtpSecurityNotAvailableException => new RemoteException(RemoteError.ConnectionFailed, "The server does not offer TLS, so the encrypted connection was not made.", e),
                _ => new RemoteException(RemoteError.ConnectionFailed, "Cannot connect: " + e.Message, e),
            };
        }
        _client = client;
    }

    private AsyncFtpClient CreateClient()
    {
        var proxy = Connection.Proxy;
        string host = Connection.Host;
        int port = Connection.EffectivePort;
        var credentials = new NetworkCredential(Connection.UserName, secret);
        if (proxy.Type == ProxyType.None) return new AsyncFtpClient(host, credentials, port);
        var profile = new FtpProxyProfile
        {
            ProxyHost = proxy.Host,
            ProxyPort = proxy.Port,
            ProxyCredentials = proxy.UserName is null ? null : new NetworkCredential(proxy.UserName, ProxyPassword),
            FtpHost = host,
            FtpPort = port,
            FtpCredentials = credentials,
        };
        return proxy.Type switch
        {
            ProxyType.Http => new AsyncFtpClientHttp11Proxy(profile),
            ProxyType.Socks4 => new AsyncFtpClientSocks4Proxy(profile),
            ProxyType.Socks5 => new AsyncFtpClientSocks5Proxy(profile),
            _ => throw new RemoteException(RemoteError.UnsupportedCombination, $"Proxy type {proxy.Type} is not supported."),
        };
    }

    private AsyncFtpClient Client => _client ?? throw new RemoteException(RemoteError.ConnectionLost, "Not connected.");

    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try { return await action().ConfigureAwait(false); }
        catch (Exception e) when (e is FtpException or IOException or SocketException or TimeoutException or ObjectDisposedException)
        {
            if (e is FtpCommandException c && c.CompletionCode is { Length: > 0 } code && code[0] == '5') throw new RemoteException(RemoteError.Failed, e.Message, e);
            throw new RemoteException(RemoteError.ConnectionLost, "The connection to the server was lost: " + e.Message, e);
        }
    }

    private static async Task Guard(Func<Task> action) => await Guard(async () => { await action().ConfigureAwait(false); return 0; }).ConfigureAwait(false);

    public Task<RemoteStat?> StatAsync(string path, CancellationToken cancellation) => Guard(async () =>
    {
        var item = await Client.GetObjectInfo(path, true, cancellation).ConfigureAwait(false);
        if (item is null) return null;
        DateTime? modified = item.Modified == default ? null : DateTime.SpecifyKind(item.Modified, DateTimeKind.Utc);
        return (RemoteStat?)new RemoteStat(item.Size, modified, item.Type == FtpObjectType.Directory);
    });

    public Task DownloadAsync(string path, Stream destination, CancellationToken cancellation) => Guard(async () =>
    {
        if (!await Client.DownloadStream(destination, path, 0, null, cancellation).ConfigureAwait(false))
            throw new RemoteException(RemoteError.Failed, $"The download of '{path}' failed.");
    });

    public Task UploadAsync(string path, Stream source, CancellationToken cancellation) => Guard(async () =>
    {
        var status = await Client.UploadStream(source, path, FtpRemoteExists.Overwrite, false, null, cancellation).ConfigureAwait(false);
        if (status != FtpStatus.Success) throw new RemoteException(RemoteError.Failed, $"The upload of '{path}' failed.");
    });

    public Task RenameAsync(string from, string to, CancellationToken cancellation) => Guard(async () =>
    {
        if (await Client.FileExists(to, cancellation).ConfigureAwait(false)) throw new RemoteException(RemoteError.Failed, $"'{to}' already exists.");
        if (!await Client.MoveFile(from, to, FtpRemoteExists.Skip, cancellation).ConfigureAwait(false))
            throw new RemoteException(RemoteError.Failed, $"Cannot rename '{from}' to '{to}'.");
    });

    // RNTO onto an existing name is left to the server's mood in FTP; there is no portable atomic replace.
    public Task<bool> TryReplaceAsync(string from, string to, CancellationToken cancellation) => Task.FromResult(false);

    public Task DeleteAsync(string path, CancellationToken cancellation) => Guard(() => Client.DeleteFile(path, cancellation));

    public Task<IReadOnlyList<RemoteEntry>> ListAsync(string directory, CancellationToken cancellation) => Guard(async () =>
    {
        var items = await Client.GetListing(directory, cancellation).ConfigureAwait(false);
        return (IReadOnlyList<RemoteEntry>)items
            .Select(i => new RemoteEntry(i.Name, i.FullName, new RemoteStat(i.Size, i.Modified == default ? null : DateTime.SpecifyKind(i.Modified, DateTimeKind.Utc), i.Type == FtpObjectType.Directory)))
            .ToList();
    });

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _client = null;
        return ValueTask.CompletedTask;
    }
}
