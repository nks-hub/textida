using System.Text.Json.Serialization;

namespace NksHub.Textida.Remote;

public enum RemoteProtocol
{
    Sftp,
    Ftps,
    /// <summary>Unencrypted FTP; usable only when <see cref="RemoteConnection.AllowPlainFtp"/> is set.</summary>
    Ftp,
}

public enum FtpsMode
{
    /// <summary>Plain connection upgraded with AUTH TLS.</summary>
    Explicit,
    /// <summary>TLS from the first byte (usually port 990).</summary>
    Implicit,
}

public enum SftpAuth
{
    Password,
    PrivateKey,
}

public enum ProxyType
{
    None,
    Http,
    Socks4,
    Socks5,
}

/// <summary>A proxy for one connection. Its password, when there is one, lives in the credential store under <see cref="RemoteConnection.ProxySecretKey"/>.</summary>
public sealed record ProxySettings(ProxyType Type = ProxyType.None, string Host = "", int Port = 0, string? UserName = null);

/// <summary>
/// One saved connection. This record is what gets written to disk and into backups, so it holds no password and no token: the password
/// is read from the credential store by <see cref="SecretKey"/>.
/// </summary>
public sealed record RemoteConnection
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public RemoteProtocol Protocol { get; init; } = RemoteProtocol.Sftp;
    public string Host { get; init; } = "";
    /// <summary>0 means the default port of the protocol.</summary>
    public int Port { get; init; }
    public string UserName { get; init; } = "";

    public SftpAuth Auth { get; init; } = SftpAuth.Password;
    /// <summary>Path of a private key file (SFTP with <see cref="SftpAuth.PrivateKey"/>); its passphrase is in the credential store.</summary>
    public string? PrivateKeyPath { get; init; }

    public FtpsMode FtpsMode { get; init; } = FtpsMode.Explicit;
    /// <summary>The user chose to allow unencrypted FTP for this connection. Without it <see cref="RemoteProtocol.Ftp"/> is refused.</summary>
    public bool AllowPlainFtp { get; init; }

    /// <summary>
    /// The identity the user trusted: for SFTP the SHA-256 fingerprint of the host key, for FTPS the SHA-256 hash of the certificate (hex).
    /// Null until the first confirmed connection. A different identity never connects without a new confirmation.
    /// </summary>
    public string? TrustedIdentity { get; init; }

    public ProxySettings Proxy { get; init; } = new();

    /// <summary>Connect timeout in seconds; null leaves the library default. The product has not decided a value.</summary>
    public int? ConnectTimeoutSeconds { get; init; }

    [JsonIgnore] public string SecretKey => $"textida:remote:{Id}";
    [JsonIgnore] public string ProxySecretKey => $"textida:remote-proxy:{Id}";
    [JsonIgnore] public int EffectivePort => Port != 0 ? Port : Protocol switch
    {
        RemoteProtocol.Sftp => 22,
        RemoteProtocol.Ftps => FtpsMode == FtpsMode.Implicit ? 990 : 21,
        _ => 21,
    };

    /// <summary>Throws <see cref="RemoteException"/> with <see cref="RemoteError.UnsupportedCombination"/> when the settings cannot work together.</summary>
    public void Validate()
    {
        void Bad(string message) => throw new RemoteException(RemoteError.UnsupportedCombination, message);
        if (string.IsNullOrWhiteSpace(Host)) Bad("The connection has no host.");
        if (Port is < 0 or > 65535) Bad($"Port {Port} is not valid.");
        if (Protocol == RemoteProtocol.Ftp && !AllowPlainFtp)
            Bad("Plain FTP sends the password and the files unencrypted. Choose FTPS or SFTP, or allow plain FTP explicitly for this connection.");
        if (Protocol != RemoteProtocol.Sftp && Auth == SftpAuth.PrivateKey) Bad("Key authentication is available for SFTP only.");
        if (Protocol == RemoteProtocol.Sftp && Auth == SftpAuth.PrivateKey && string.IsNullOrWhiteSpace(PrivateKeyPath)) Bad("Key authentication needs a private key file.");
        if (Protocol != RemoteProtocol.Ftps && FtpsMode != FtpsMode.Explicit) Bad("The implicit TLS mode is available for FTPS only.");
        if (Proxy.Type != ProxyType.None && (string.IsNullOrWhiteSpace(Proxy.Host) || Proxy.Port is <= 0 or > 65535)) Bad("The proxy needs a host and a port.");
        if (!Enum.IsDefined(Proxy.Type) || !Enum.IsDefined(Protocol)) Bad("Unknown protocol or proxy type.");
    }
}

public enum RemoteError
{
    UnsupportedCombination,
    CredentialMissing,
    AuthenticationFailed,
    /// <summary>The server identity is not trusted (unknown, or different from the stored one) and nobody confirmed it.</summary>
    UntrustedServer,
    ConnectionFailed,
    ConnectionLost,
    NotFound,
    Failed,
}

public sealed class RemoteException(RemoteError error, string message, Exception? inner = null) : Exception(message, inner)
{
    public RemoteError Error { get; } = error;
}

/// <summary>Size and time of a remote file; the time may be missing when the server cannot tell.</summary>
public readonly record struct RemoteStat(long Size, DateTime? ModifiedUtc, bool IsDirectory = false);

public sealed record RemoteEntry(string Name, string FullPath, RemoteStat Stat);

public enum IdentityKind
{
    SshHostKey,
    TlsCertificate,
}

/// <summary>What the server presented. <see cref="Fingerprint"/> is the value compared with <see cref="RemoteConnection.TrustedIdentity"/>.</summary>
public sealed record ServerIdentity(IdentityKind Kind, string Fingerprint, string Detail);

public enum TrustReason
{
    /// <summary>Nothing was trusted before.</summary>
    FirstUse,
    /// <summary>The server presents another identity than the one the user trusted.</summary>
    Changed,
    /// <summary>The certificate does not validate (untrusted issuer, wrong name, expired) and nothing was pinned.</summary>
    Invalid,
}

/// <summary>Asks the user whether to trust a server identity. Implementations show a dialog; without one every unknown or changed identity is refused.</summary>
public interface IServerTrustPrompt
{
    ValueTask<bool> ConfirmAsync(RemoteConnection connection, ServerIdentity presented, TrustReason reason, CancellationToken cancellation);
}

/// <summary>Refuses every identity that is not already trusted.</summary>
public sealed class DenyUnknownServers : IServerTrustPrompt
{
    public static readonly DenyUnknownServers Instance = new();
    public ValueTask<bool> ConfirmAsync(RemoteConnection connection, ServerIdentity presented, TrustReason reason, CancellationToken cancellation) => ValueTask.FromResult(false);
}
