using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Remote;

[JsonSerializable(typeof(ProxySettings))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
internal sealed partial class NetworkPolicyJson : JsonSerializerContext;

/// <summary>
/// The proxy every network service of the editor uses: FTPS, SFTP, the update check and the network requests of plugins. A connection with its own proxy overrides
/// it; a connection without one follows it. The proxy password lives in the credential store under <see cref="SecretKey"/>, not in the policy file.
/// </summary>
public sealed class NetworkPolicy(ProxySettings proxy, string? proxyPassword)
{
    public const string SecretKey = "textida:network-proxy";

    public static NetworkPolicy Direct { get; } = new(new ProxySettings(), null);

    public ProxySettings Proxy { get; } = proxy;
    public string? ProxyPassword { get; } = proxyPassword;

    public static NetworkPolicy Load(string path, ICredentialStore secrets)
    {
        if (!File.Exists(path)) return Direct;
        ProxySettings proxy;
        try { proxy = JsonSerializer.Deserialize(File.ReadAllText(path), NetworkPolicyJson.Default.ProxySettings) ?? new(); }
        catch (JsonException e) { throw new RemoteException(RemoteError.UnsupportedCombination, "The proxy settings are not valid (supported types: http, socks4, socks5): " + e.Message, e); }
        Validate(proxy);
        return new NetworkPolicy(proxy, proxy.Type == ProxyType.None ? null : secrets.Get(SecretKey));
    }

    public static void Save(string path, ProxySettings proxy, string? password, ICredentialStore secrets)
    {
        Validate(proxy);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(proxy, NetworkPolicyJson.Default.ProxySettings));
        FileSwap.Replace(temp, path);
        if (password is not null) secrets.Set(SecretKey, password); else secrets.Delete(SecretKey);
    }

    public static void Validate(ProxySettings proxy)
    {
        if (!Enum.IsDefined(proxy.Type)) throw new RemoteException(RemoteError.UnsupportedCombination, $"Proxy type {proxy.Type} is not supported (supported: http, socks4, socks5).");
        if (proxy.Type != ProxyType.None && (string.IsNullOrWhiteSpace(proxy.Host) || proxy.Port is <= 0 or > 65535))
            throw new RemoteException(RemoteError.UnsupportedCombination, "The proxy needs a host and a port.");
    }

    /// <summary>The proxy and password a connection uses: its own when it has one, else the policy's.</summary>
    public (ProxySettings Proxy, string? Password) Resolve(RemoteConnection connection, ICredentialStore secrets) =>
        connection.Proxy.Type != ProxyType.None ? (connection.Proxy, secrets.Get(connection.ProxySecretKey)) : (Proxy, ProxyPassword);

    /// <summary>A handler for <see cref="HttpClient"/> that goes through the proxy; the update check and plugin requests build their client from it. Loopback addresses are not exempt.</summary>
    public HttpClientHandler CreateHttpHandler()
    {
        var handler = new HttpClientHandler();
        if (Proxy.Type == ProxyType.None) { handler.UseProxy = false; return handler; }
        string scheme = Proxy.Type switch { ProxyType.Http => "http", ProxyType.Socks4 => "socks4", ProxyType.Socks5 => "socks5", _ => throw new RemoteException(RemoteError.UnsupportedCombination, $"Proxy type {Proxy.Type} is not supported.") };
        var webProxy = new WebProxy(new Uri($"{scheme}://{Proxy.Host}:{Proxy.Port}"), false)
        {
            Credentials = Proxy.UserName is null ? null : new NetworkCredential(Proxy.UserName, ProxyPassword),
        };
        handler.Proxy = webProxy;
        handler.UseProxy = true;
        return handler;
    }
}
