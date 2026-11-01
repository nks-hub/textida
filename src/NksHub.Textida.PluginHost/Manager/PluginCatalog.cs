using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.PluginHost.Manager;

/// <summary>A publisher of the catalog: the key (ECDSA P-256, SubjectPublicKeyInfo as Base64) that signs the entries it published.</summary>
public sealed record CatalogPublisher(string Id, string Name, string PublicKey)
{
    /// <summary>The SHA-256 of the key in hex, grouped in fours: what the user compares and confirms before the key is trusted.</summary>
    public string Fingerprint
    {
        get
        {
            try
            {
                string hex = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(PublicKey)));
                return string.Join(' ', Enumerable.Range(0, hex.Length / 4).Select(i => hex.Substring(i * 4, 4)));
            }
            catch (FormatException) { return ""; }
        }
    }
}

/// <summary>
/// One plugin version in the catalog. <see cref="Package"/> is a path inside the repository or an https URL of the ZIP, <see cref="Sha256"/> its hash in hex, <see cref="Permissions"/> what it asks for,
/// and <see cref="Signature"/> the publisher's ECDSA signature (Base64, IEEE P1363) over <see cref="PluginCatalog.SignedText"/>, so a changed ZIP, version or permission list invalidates it.
/// </summary>
public sealed record CatalogEntry(string Id, string Name, string Version, string Description, string Publisher, string Package, string Sha256, IReadOnlyList<string> Permissions, string Signature);

public sealed record PluginCatalogIndex(IReadOnlyList<CatalogPublisher> Publishers, IReadOnlyList<CatalogEntry> Entries, IReadOnlyList<string> Problems);

public enum CatalogCheck { Ok, UnknownPublisher, BadKey, BadSignature, BadHash }

/// <summary>
/// The plugin catalog (PZ.8.1): an index file in a Git repository lists plugin versions with the hash of their ZIP and a signature of the publisher. This class reads the index and checks hashes and signatures;
/// getting the repository, trusting a publisher key and installing stay with the callers, so nothing here touches the network or the disk.
/// An entry that is malformed is left out of the index and named in <see cref="PluginCatalogIndex.Problems"/>; the rest stays usable.
/// </summary>
public static class PluginCatalog
{
    public const string IndexFile = "index.json";
    public const long MaxIndexBytes = 2 << 20;

    private static readonly System.Text.RegularExpressions.Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{0,63}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex HashPattern = new("^[0-9a-fA-F]{64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The text a publisher signs: a fixed header, the id, the version, the hash of the ZIP and the sorted permissions, one per line.</summary>
    public static string SignedText(string id, string version, string sha256, IEnumerable<string> permissions) =>
        string.Join('\n', ["textida-plugin-catalog-v1", id, version, sha256.ToLowerInvariant(), string.Join(',', permissions.Order(StringComparer.Ordinal))]);

    public static string SignedText(CatalogEntry entry) => SignedText(entry.Id, entry.Version, entry.Sha256, entry.Permissions);

    /// <summary>Reads the index. Text that is not an index at all is a problem of the whole index (no entries).</summary>
    public static PluginCatalogIndex Parse(string json)
    {
        var problems = new List<string>();
        var publishers = new List<CatalogPublisher>();
        var entries = new List<CatalogEntry>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
                return new PluginCatalogIndex([], [], ["The index is not a plugin catalog (schemaVersion 1)."]);
            if (root.TryGetProperty("publishers", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var p in list.EnumerateArray())
                {
                    string id = Text(p, "id"), key = Text(p, "publicKey");
                    if (!IdPattern.IsMatch(id) || key.Length == 0) problems.Add($"A publisher is skipped: id '{id}' or its key is not valid.");
                    else if (publishers.Any(x => x.Id == id)) problems.Add($"The publisher '{id}' is listed twice; the second is skipped.");
                    else publishers.Add(new CatalogPublisher(id, Text(p, "name") is { Length: > 0 } n ? n : id, key));
                }
            if (root.TryGetProperty("plugins", out var plugins) && plugins.ValueKind == JsonValueKind.Array)
                foreach (var e in plugins.EnumerateArray())
                {
                    var permissions = e.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array
                        ? perms.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                        : [];
                    var entry = new CatalogEntry(Text(e, "id"), Text(e, "name"), Text(e, "version"), Text(e, "description"), Text(e, "publisher"), Text(e, "package"), Text(e, "sha256"), permissions, Text(e, "signature"));
                    string? why = Invalid(entry);
                    if (why is not null) problems.Add($"Entry '{entry.Id}' {entry.Version} is skipped: {why}.");
                    else entries.Add(entry);
                }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return new PluginCatalogIndex([], [], [$"The index cannot be read: {e.Message}"]); }
        return new PluginCatalogIndex(publishers, entries, problems);
    }

    private static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string? Invalid(CatalogEntry e)
    {
        if (!IdPattern.IsMatch(e.Id)) return "the id is not valid";
        if (e.Version.Length is 0 or > 64) return "the version is missing";
        if (!HashPattern.IsMatch(e.Sha256)) return "the hash is not a SHA-256";
        if (e.Signature.Length == 0 || e.Publisher.Length == 0) return "it is not signed";
        if (!IsSafePackage(e.Package)) return "the package is neither a path inside the repository nor an https address";
        return null;
    }

    /// <summary>True for an https URL or a relative path that stays inside the repository (no drive, no leading slash, no <c>..</c>).</summary>
    public static bool IsSafePackage(string package)
    {
        if (Uri.TryCreate(package, UriKind.Absolute, out var uri)) return uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
        if (package.Length == 0 || package.Contains('\\') || package.StartsWith('/') || package.Contains(':') || package.EndsWith('/')) return false;
        return package.Split('/').All(part => part.Length > 0 && part is not ("." or "..")) && package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the signature of the entry holds for the key of its publisher.</summary>
    public static CatalogCheck CheckSignature(CatalogEntry entry, CatalogPublisher? publisher)
    {
        if (publisher is null) return CatalogCheck.UnknownPublisher;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publisher.PublicKey), out _);
            if (key.KeySize != 256) return CatalogCheck.BadKey;
            return key.VerifyData(Encoding.UTF8.GetBytes(SignedText(entry)), Convert.FromBase64String(entry.Signature), HashAlgorithmName.SHA256) ? CatalogCheck.Ok : CatalogCheck.BadSignature;
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException) { return CatalogCheck.BadKey; }
    }

    /// <summary>True when the file has the hash the index states.</summary>
    public static CatalogCheck CheckHash(CatalogEntry entry, string zipPath)
    {
        using var stream = File.OpenRead(zipPath);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), entry.Sha256, StringComparison.OrdinalIgnoreCase) ? CatalogCheck.Ok : CatalogCheck.BadHash;
    }

    /// <summary>Signs an entry; for the publisher's tooling and for tests. The key is an ECDSA P-256 key.</summary>
    public static string Sign(ECDsa key, string id, string version, string sha256, IEnumerable<string> permissions) =>
        Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(SignedText(id, version, sha256, permissions)), HashAlgorithmName.SHA256));

    /// <summary>True when <paramref name="candidate"/> is a newer version than <paramref name="installed"/>: numbers compare as numbers, the rest as text.</summary>
    public static bool IsNewer(string candidate, string installed)
    {
        if (Version.TryParse(candidate, out var a) && Version.TryParse(installed, out var b)) return a > b;
        return string.CompareOrdinal(candidate, installed) > 0;
    }
}
