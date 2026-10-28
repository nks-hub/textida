using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Remote;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The package registries the user allowed to be asked about the dependencies of a manifest (P8.7.3) and the hosts the user allowed schemas to be downloaded from (P8.7.1), kept in <c>package-registries.json</c> of the profile.
/// The JSON plugin reads the same file before every request, so an entry added here takes effect at once and a revoke ends the answers of its cache. The file also
/// carries the proxy of the network policy (P5.4.4) because the plugin builds its own HTTP client; a proxy it cannot log in to sets <c>blocked</c> and no lookup is made.
/// </summary>
internal sealed class PackageRegistryConsent(string path)
{
    public string FilePath { get; } = path;

    private (List<string> Allowed, string? Proxy, bool Blocked) Read()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) is JsonObject root)
                return ([.. (root["allowed"] as JsonArray ?? []).Select(n => n?.GetValue<string>()).OfType<string>()], root["proxy"]?.GetValue<string>(), root["blocked"]?.GetValue<bool>() ?? false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { /* nothing allowed yet */ }
        return ([], null, false);
    }

    private void Write(List<string> allowed, string? proxy, bool blocked)
    {
        var root = new JsonObject { ["allowed"] = new JsonArray([.. allowed.Select(a => (JsonNode?)JsonValue.Create(a))]), ["blocked"] = blocked };
        if (proxy is not null) root["proxy"] = proxy;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        string temp = FilePath + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        FileSwap.Replace(temp, FilePath);
    }

    public bool IsAllowed(string registry) => Read().Allowed.Contains(registry);

    /// <summary>Whether the user allowed schemas to be downloaded from any host; only then the proxy has to be passed on to the plugin for a schema check.</summary>
    public bool AllowsAnySchema() => Read().Allowed.Any(a => a.StartsWith("schemas:", StringComparison.Ordinal));

    /// <summary>The consent id of downloading schemas from a host, kept in the same list as the registries; the JSON plugin builds the same text (<c>schemas:host</c>).</summary>
    public static string SchemaId(string host) => "schemas:" + host.ToLowerInvariant();

    public void Allow(string registry)
    {
        var (allowed, proxy, blocked) = Read();
        if (allowed.Contains(registry)) return;
        allowed.Add(registry);
        Write(allowed, proxy, blocked);
    }

    /// <summary>Forbids every registry; the plugin drops its cache when it sees that.</summary>
    public void RevokeAll()
    {
        var (allowed, proxy, blocked) = Read();
        if (allowed.Count == 0) return;
        Write([], proxy, blocked);
    }

    /// <summary>
    /// Copies the proxy of the network policy into the file. <paramref name="policy"/> is null when the policy cannot be read: the lookups stop then rather than go around a proxy
    /// that was meant. A proxy with a user name is not passed on (a password on a command line or in this file would be visible), so lookups stop for it too.
    /// </summary>
    public void Sync(NetworkPolicy? policy)
    {
        string? proxy = null;
        bool blocked = policy is null;
        if (policy is { Proxy: { Type: not ProxyType.None } p })
        {
            blocked = p.UserName is not null;
            string scheme = p.Type switch { ProxyType.Http => "http", ProxyType.Socks4 => "socks4", _ => "socks5" };
            proxy = $"{scheme}://{p.Host}:{p.Port}";
        }
        var (allowed, oldProxy, oldBlocked) = Read();
        if (proxy != oldProxy || blocked != oldBlocked || !File.Exists(FilePath)) Write(allowed, proxy, blocked);
    }
}
