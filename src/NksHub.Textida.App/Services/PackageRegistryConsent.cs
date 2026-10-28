using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Remote;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The package registries the user allowed to be asked about the dependencies of a manifest (P8.7.3) and the hosts the user allowed schemas to be downloaded from (P8.7.1), kept in <c>package-registries.json</c> of the profile.
/// The JSON plugin reads the same file before every request, so an entry added here takes effect at once and a revoke ends the answers of its cache. The file also
/// carries the proxy of the network policy (P5.4.4) because the plugin builds its own HTTP client. A proxy with a login is not handed to the plugin (a password in this file would be visible):
/// <c>viaEditor</c> tells the plugin to send its requests through the editor, which holds the password. When that cannot work (the policy cannot be read, or the user name has no saved
/// password) <c>blocked</c> is set, <c>blockedReason</c> says why, and no lookup is made.
/// </summary>
internal sealed class PackageRegistryConsent(string path)
{
    public string FilePath { get; } = path;

    /// <param name="BlockedReason">Why lookups are stopped: <c>policy</c> (the network settings cannot be read) or <c>password</c> (the proxy has a user name but no saved password); null when they are not.</param>
    private sealed record State(List<string> Allowed, string? Proxy, bool ViaEditor, string? BlockedReason);

    private State Read()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) is JsonObject root)
            {
                bool blocked = root["blocked"]?.GetValue<bool>() ?? false;
                return new([.. (root["allowed"] as JsonArray ?? []).Select(n => n?.GetValue<string>()).OfType<string>()], root["proxy"]?.GetValue<string>(), root["viaEditor"]?.GetValue<bool>() ?? false,
                    blocked ? root["blockedReason"]?.GetValue<string>() ?? "policy" : null);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { /* nothing allowed yet */ }
        return new([], null, false, null);
    }

    private void Write(State state)
    {
        var root = new JsonObject { ["allowed"] = new JsonArray([.. state.Allowed.Select(a => (JsonNode?)JsonValue.Create(a))]), ["blocked"] = state.BlockedReason is not null };
        if (state.BlockedReason is not null) root["blockedReason"] = state.BlockedReason;
        if (state.ViaEditor) root["viaEditor"] = true;
        if (state.Proxy is not null) root["proxy"] = state.Proxy;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        string temp = FilePath + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        FileSwap.Replace(temp, FilePath);
    }

    public bool IsAllowed(string registry) => Read().Allowed.Contains(registry);

    /// <summary>The reason lookups are stopped for the proxy (<c>policy</c> or <c>password</c>), or null when they are not.</summary>
    public string? BlockedReason => Read().BlockedReason;

    /// <summary>Whether the plugin sends its requests through the editor, because the proxy needs a login.</summary>
    public bool ViaEditor => Read().ViaEditor;

    /// <summary>Whether the user allowed schemas to be downloaded from any host; only then the proxy has to be passed on to the plugin for a schema check.</summary>
    public bool AllowsAnySchema() => Read().Allowed.Any(a => a.StartsWith("schemas:", StringComparison.Ordinal));

    /// <summary>The consent id of downloading schemas from a host, kept in the same list as the registries; the JSON plugin builds the same text (<c>schemas:host</c>).</summary>
    public static string SchemaId(string host) => "schemas:" + host.ToLowerInvariant();

    public void Allow(string registry)
    {
        var state = Read();
        if (state.Allowed.Contains(registry)) return;
        state.Allowed.Add(registry);
        Write(state);
    }

    /// <summary>Forbids every registry; the plugin drops its cache when it sees that.</summary>
    public void RevokeAll()
    {
        var state = Read();
        if (state.Allowed.Count == 0) return;
        Write(state with { Allowed = [] });
    }

    /// <summary>
    /// Copies the proxy of the network policy into the file. <paramref name="policy"/> is null when the policy cannot be read: the lookups stop then rather than go around a proxy
    /// that was meant. A proxy with a user name is not passed on as such (a password on a command line or in this file would be visible): the plugin is told to send its requests through
    /// the editor, which logs in with the saved password. A user name without a saved password cannot log in, so lookups stop for it.
    /// </summary>
    public void Sync(NetworkPolicy? policy)
    {
        string? proxy = null, reason = policy is null ? "policy" : null;
        bool viaEditor = false;
        if (policy is { Proxy: { Type: not ProxyType.None } p })
        {
            viaEditor = p.UserName is not null;
            if (viaEditor && policy.ProxyPassword is null) reason = "password";
            string scheme = p.Type switch { ProxyType.Http => "http", ProxyType.Socks4 => "socks4", _ => "socks5" };
            proxy = $"{scheme}://{p.Host}:{p.Port}";
        }
        var old = Read();
        viaEditor &= reason is null;
        if (proxy != old.Proxy || reason != old.BlockedReason || viaEditor != old.ViaEditor || !File.Exists(FilePath)) Write(old with { Proxy = proxy, ViaEditor = viaEditor, BlockedReason = reason });
    }
}
