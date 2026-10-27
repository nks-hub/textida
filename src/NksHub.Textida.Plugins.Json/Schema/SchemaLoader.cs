using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Data;
using NksHub.Textida.Plugins.Json.Packages;

namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>Why there is no schema text: <see cref="NoConsent"/> (the host is not allowed, or downloads are not set up), <see cref="Unavailable"/> (no answer and no saved copy), <see cref="TooLarge"/> or <see cref="Invalid"/> (not a JSON schema).</summary>
public enum SchemaState { Ready, NoConsent, Unavailable, TooLarge, Invalid }

/// <summary>
/// The text of a schema, or the reason there is none. <see cref="FromCache"/>: it came from the copy on disk and no request was made; <see cref="Offline"/>: the copy is older than allowed and was used
/// because the host did not answer. <see cref="Host"/> is the host of a remote schema (the one the user has to allow); <see cref="Reason"/> is English text for logs.
/// </summary>
public sealed record SchemaText(string? Text, SchemaState State, string? Host = null, bool FromCache = false, bool Offline = false, string? Reason = null);

/// <param name="Directory">Where the copies of downloaded schemas are kept.</param>
/// <param name="MaxBytes">Most bytes the copies take together; the oldest are deleted to make room.</param>
/// <param name="MaxSchemaBytes">Largest schema that is downloaded or kept; a larger answer is refused.</param>
/// <param name="MaxAge">How long a copy is used without asking the host again; after it the host is asked, and the copy is used only when the host does not answer.</param>
/// <param name="RetryAfter">How long a host that did not answer is left alone before it is asked again.</param>
public sealed record SchemaCacheSettings(string Directory, long MaxBytes, long MaxSchemaBytes, TimeSpan MaxAge, TimeSpan RetryAfter);

/// <summary>The copies of downloaded schemas on disk: one file per address, named after its host, with the time of the download as the time of the file. Everything is best effort; a folder that cannot be used means no cache.</summary>
internal sealed class SchemaCache(SchemaCacheSettings settings)
{
    private readonly object _gate = new();

    private static string HostPart(string host)
    {
        var sb = new StringBuilder();
        foreach (var c in host.ToLowerInvariant()) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_');
        return sb.ToString();
    }

    private string PathOf(Uri url) => Path.Combine(settings.Directory, HostPart(url.Host) + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)))[..16] + ".json");

    /// <summary>The copy of <paramref name="url"/> and when it was downloaded, or null.</summary>
    public (string Text, DateTime WrittenUtc)? Read(Uri url)
    {
        lock (_gate)
        {
            try
            {
                var file = new FileInfo(PathOf(url));
                if (!file.Exists || file.Length > settings.MaxSchemaBytes) return null;
                return (File.ReadAllText(file.FullName, Encoding.UTF8), file.LastWriteTimeUtc);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }
    }

    /// <summary>Keeps a copy and deletes the oldest ones while the copies take more than the limit. A text larger than the limit of one schema is not kept.</summary>
    public void Write(Uri url, string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > settings.MaxSchemaBytes) return;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(settings.Directory);
                var path = PathOf(url);
                var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                File.Move(temp, path, overwrite: true);
                Trim();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the schema is used from memory this time */ }
        }
    }

    /// <summary>Deletes every copy that came from <paramref name="host"/>.</summary>
    public void Clear(string host)
    {
        lock (_gate)
        {
            try
            {
                if (!Directory.Exists(settings.Directory)) return;
                foreach (var file in Directory.EnumerateFiles(settings.Directory, HostPart(host) + ".*.json")) File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a copy that stays is not used without consent */ }
        }
    }

    /// <summary>Total size of the copies.</summary>
    public long Bytes()
    {
        lock (_gate)
        {
            try { return Directory.Exists(settings.Directory) ? new DirectoryInfo(settings.Directory).EnumerateFiles("*.json").Sum(f => f.Length) : 0; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
        }
    }

    private void Trim()
    {
        var files = new DirectoryInfo(settings.Directory).EnumerateFiles("*.json").OrderBy(f => f.LastWriteTimeUtc).ToList();
        long total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= settings.MaxBytes) break;
            total -= file.Length;
            file.Delete();
        }
    }
}

/// <summary>
/// Gets the text of a schema. A carried schema is read from the plugin; a schema at a file path is read from disk; a schema at an http(s) address is downloaded only when the user allowed that host
/// (<see cref="ConsentId"/>, kept in the same consent file as the package registries and checked on every call, so a revoke takes effect at once), through <see cref="RegistryClient"/> and so with the proxy of
/// the editor. A download is kept on disk (<see cref="SchemaCache"/>) and used while it is younger than the limit, and also when the host does not answer. Without consent no request is made and the copies of that host
/// are deleted. The completion and the hover never wait for the network: they use a copy when there is one.
/// </summary>
public sealed class SchemaLoader
{
    private const string Accept = "application/schema+json, application/json;q=0.9, */*;q=0.1";

    private readonly RegistryClient? _client;
    private readonly SchemaCacheSettings? _cacheSettings;
    private readonly SchemaCache? _cache;
    private readonly SchemaSettings _settings;
    private readonly Dictionary<string, long> _failed = [];

    /// <param name="client">Null when the plugin was started without registry options: no schema is downloaded.</param>
    /// <param name="cache">Null when no cache folder was given: no schema is downloaded.</param>
    public SchemaLoader(RegistryClient? client, SchemaCacheSettings? cache, SchemaSettings settings)
    {
        _client = client;
        _cacheSettings = cache;
        _cache = cache is null ? null : new SchemaCache(cache);
        _settings = settings;
    }

    /// <summary>The consent id of a host, as it is written into the consent file.</summary>
    public static string ConsentId(string host) => "schemas:" + host.ToLowerInvariant();

    /// <summary>The bytes the copies on disk take now.</summary>
    public long CachedBytes => _cache?.Bytes() ?? 0;

    /// <param name="documentPath">The file the schema is for; a relative file path of a schema is read from its folder.</param>
    /// <param name="network">False for completion and hover: only a copy on disk (any age) is used.</param>
    public async Task<SchemaText> LoadAsync(SchemaEntry entry, string? documentPath, bool network, CancellationToken ct = default)
    {
        if (entry.IsBundled) return new SchemaText(SchemaCatalog.LoadSchemaText(entry), SchemaState.Ready);
        string address = entry.Url ?? "";
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return await LoadRemoteAsync(uri, network, ct);
        return LoadFile(address, documentPath);
    }

    private SchemaText LoadFile(string address, string? documentPath)
    {
        string path;
        // a path to another computer (UNC) is a request to that computer, which a file may not make on its own: it needs the consent of a download
        const string OtherComputer = "A schema on another computer is not read; allow it as a download from its address instead.";
        if (address.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(address, UriKind.Absolute, out var fileUri))
        {
            if (fileUri.IsUnc || fileUri.Host.Length > 0) return new SchemaText(null, SchemaState.Unavailable, Reason: OtherComputer);
            path = fileUri.LocalPath;
        }
        else if (address.StartsWith(@"\\", StringComparison.Ordinal) || address.StartsWith("//", StringComparison.Ordinal)) return new SchemaText(null, SchemaState.Unavailable, Reason: OtherComputer);
        else if (Path.IsPathRooted(address)) path = address;
        else if (documentPath is { Length: > 0 } && Path.GetDirectoryName(documentPath) is { } folder) path = Path.Combine(folder, address);
        else return new SchemaText(null, SchemaState.Unavailable, Reason: "The schema file has no folder to be found in.");
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return new SchemaText(null, SchemaState.Unavailable, Reason: $"The schema file '{path}' does not exist.");
            if (file.Length > _settings.MaxBytes) return new SchemaText(null, SchemaState.TooLarge, Reason: "The schema file is too large.");
            var text = File.ReadAllText(file.FullName, Encoding.UTF8);
            return IsSchema(text) ? new SchemaText(text, SchemaState.Ready) : new SchemaText(null, SchemaState.Invalid, Reason: "The file is not a JSON schema.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return new SchemaText(null, SchemaState.Unavailable, Reason: e.Message); }
    }

    private bool IsSchema(string text) =>
        SchemaCatalog.TryParse(text, DataDialect.Json, _settings.MaxDepth, out var root, out _) && root is { Kind: DataKind.Object or DataKind.Boolean };

    private async Task<SchemaText> LoadRemoteAsync(Uri url, bool network, CancellationToken ct)
    {
        string host = url.Host.ToLowerInvariant();
        if (_client is null || _cache is null || _cacheSettings is null) return new SchemaText(null, SchemaState.NoConsent, host, Reason: "Schema downloads are not set up.");
        if (!_client.IsAllowed(ConsentId(host)))
        {
            _cache.Clear(host);
            return new SchemaText(null, SchemaState.NoConsent, host, Reason: $"Downloads from '{host}' are not allowed.");
        }
        var copy = _cache.Read(url);
        var age = copy is { } c ? DateTime.UtcNow - c.WrittenUtc : TimeSpan.MaxValue;
        if (copy is { } hit && (!network || age < _cacheSettings.MaxAge)) return new SchemaText(hit.Text, SchemaState.Ready, host, FromCache: true);
        if (!network) return new SchemaText(null, SchemaState.Unavailable, host, Reason: "The schema has not been downloaded yet.");
        if (RecentlyFailed(url)) return copy is { } old ? new SchemaText(old.Text, SchemaState.Ready, host, FromCache: true, Offline: true) : new SchemaText(null, SchemaState.Unavailable, host, Reason: "The host did not answer a moment ago.");
        try
        {
            var body = await _client.FetchAsync(ConsentId(host), Accept, url, _cacheSettings.MaxSchemaBytes, ct);
            if (!IsSchema(body)) throw new RegistryException($"{host} did not answer with a JSON schema.");
            _cache.Write(url, body);
            Forget(url);
            return new SchemaText(body, SchemaState.Ready, host);
        }
        catch (ConsentRequiredException) { return new SchemaText(null, SchemaState.NoConsent, host, Reason: $"Downloads from '{host}' are not allowed."); }
        catch (RegistryException e)
        {
            Fail(url);
            if (copy is { } stale) return new SchemaText(stale.Text, SchemaState.Ready, host, FromCache: true, Offline: true, Reason: e.Message);
            return new SchemaText(null, e.Message.Contains("more than", StringComparison.Ordinal) ? SchemaState.TooLarge : SchemaState.Unavailable, host, Reason: e.Message);
        }
    }

    private bool RecentlyFailed(Uri url)
    {
        lock (_failed) return _failed.TryGetValue(url.AbsoluteUri, out var at) && Environment.TickCount64 - at < _cacheSettings!.RetryAfter.TotalMilliseconds;
    }

    private void Fail(Uri url)
    {
        lock (_failed) _failed[url.AbsoluteUri] = Environment.TickCount64;
    }

    private void Forget(Uri url)
    {
        lock (_failed) _failed.Remove(url.AbsoluteUri);
    }
}
