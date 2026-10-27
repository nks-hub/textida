using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Remote;

public static class RemoteClients
{
    /// <summary>
    /// Creates the client for the connection's protocol. The password (or key passphrase) and the proxy password come from <paramref name="secrets"/>, never from the
    /// connection. The proxy is the connection's own, or the one of <paramref name="policy"/> when the connection has none.
    /// </summary>
    public static IRemoteClient Create(RemoteConnection connection, ICredentialStore secrets, IServerTrustPrompt trust, NetworkPolicy? policy = null)
    {
        connection.Validate();
        var (proxy, proxySecret) = (policy ?? NetworkPolicy.Direct).Resolve(connection, secrets);
        NetworkPolicy.Validate(proxy);
        connection = connection with { Proxy = proxy };
        connection.Validate();
        string? secret = secrets.Get(connection.SecretKey);
        return connection.Protocol == RemoteProtocol.Sftp
            ? new SftpRemoteClient(connection, secret, trust) { ProxyPassword = proxySecret }
            : new FtpRemoteClient(connection, secret, trust) { ProxyPassword = proxySecret };
    }
}
