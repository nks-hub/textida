using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Remote;

public static class RemoteClients
{
    /// <summary>Creates the client for the connection's protocol. The password (or key passphrase) and the proxy password come from <paramref name="secrets"/>, never from the connection.</summary>
    public static IRemoteClient Create(RemoteConnection connection, ICredentialStore secrets, IServerTrustPrompt trust)
    {
        connection.Validate();
        string? secret = secrets.Get(connection.SecretKey);
        string? proxySecret = connection.Proxy.Type == ProxyType.None ? null : secrets.Get(connection.ProxySecretKey);
        return connection.Protocol == RemoteProtocol.Sftp
            ? new SftpRemoteClient(connection, secret, trust) { ProxyPassword = proxySecret }
            : new FtpRemoteClient(connection, secret, trust) { ProxyPassword = proxySecret };
    }
}
