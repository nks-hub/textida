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
    /// <summary>The last uploaded revision of each file a project sent to its default server, kept apart from <see cref="Copies"/> so it never touches a copy opened from a server.</summary>
    public WorkingCopyStore Deployments { get; } = new(Path.Combine(profileDirectory, "remote", "deploy"));
    public string NetworkPolicyPath => Path.Combine(Folder, "network.json");

    /// <summary>The limits of the cache of remote copies (placeholders, OPEN).</summary>
    public static CachePolicy CachePolicy { get; } = new(PluginLimitsPlaceholders.RemoteCacheMaxBytes, TimeSpan.FromDays(PluginLimitsPlaceholders.RemoteCacheMaxAgeDays));

    /// <summary>Removes the copies over the limits of <see cref="CachePolicy"/> in the background, least recently used first; a copy with unsent changes stays. Tried again at the next start.</summary>
    public Task PruneCopiesAsync() => Task.Run(() =>
    {
        try { return Copies.Prune(CachePolicy, DateTime.UtcNow).Count; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    });

    public NetworkPolicy Policy() => NetworkPolicy.Load(NetworkPolicyPath, Secrets);

    public IRemoteClient CreateClient(RemoteConnection connection, IServerTrustPrompt trust) =>
        RemoteClients.Create(connection, Secrets, trust, Policy());
}
