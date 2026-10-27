using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Remote;

namespace NksHub.Textida.App.Services;

/// <summary>
/// What the remote files need from the profile: the saved connections (no passwords), the local copies of remote files with their server revision, the proxy
/// policy, and the credential store the passwords live in.
/// </summary>
internal sealed class RemoteServices(string profileDirectory, ICredentialStore secrets)
{
    private string Folder => Path.Combine(profileDirectory, "remote");

    public ICredentialStore Secrets { get; } = secrets;
    public ConnectionStore Connections { get; } = new(Path.Combine(profileDirectory, "remote", "connections.json"));
    public WorkingCopyStore Copies { get; } = new(Path.Combine(profileDirectory, "remote", "cache"));
    public string NetworkPolicyPath => Path.Combine(Folder, "network.json");

    public NetworkPolicy Policy() => NetworkPolicy.Load(NetworkPolicyPath, Secrets);

    public IRemoteClient CreateClient(RemoteConnection connection, IServerTrustPrompt trust) =>
        RemoteClients.Create(connection, Secrets, trust, Policy());
}
