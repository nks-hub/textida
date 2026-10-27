using System.Text.Json;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Plugins.Json.Packages;

/// <summary>
/// One package registry: where its manifest's dependencies are, which URL answers a name search and a version list, and how to read those answers. The answers are
/// the public JSON (or text) of the registry's documented API. Parsing never throws on a shape it does not know: an unknown answer is a registry error.
/// </summary>
public abstract partial class RegistryAdapter(string id, string searchBase, string metaBase)
{
    public string Id { get; } = id;

    protected string SearchBase { get; private set; } = searchBase.TrimEnd('/');
    protected string MetaBase { get; private set; } = metaBase.TrimEnd('/');

    /// <summary>Sends every request of this registry to <paramref name="url"/> instead (a mirror, or the listener of a test).</summary>
    public void Override(string url) => SearchBase = MetaBase = url.TrimEnd('/');

    /// <summary>The <c>Accept</c> header the registry wants, or null for the default.</summary>
    public virtual string? Accept => null;

    public abstract IReadOnlyList<Dependency> Dependencies(string text);

    /// <summary>The URL that answers a name typed so far, or null when the typed text cannot be asked about.</summary>
    public abstract Uri? NamesUrl(string prefix, int pageSize);

    public abstract IReadOnlyList<string> Names(string body, string prefix);

    public abstract Uri VersionsUrl(string name);

    public abstract IReadOnlyList<string> VersionList(string body, string name);

    /// <summary>Whether the name can be looked up; the others (Composer's <c>php</c>, <c>ext-json</c>) are not packages.</summary>
    public virtual bool IsPackage(string name) => name.Length > 0;

    protected static string Q(string text) => Uri.EscapeDataString(text);

    protected static JsonDocument Parse(string body)
    {
        try { return JsonDocument.Parse(body); }
        catch (JsonException e) { throw new RegistryException("The registry answer is not JSON: " + e.Message, e); }
    }

    protected static IEnumerable<JsonElement> Items(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array) throw new RegistryException($"The registry answer has no '{property}' list.");
        return array.EnumerateArray();
    }

    protected static string Text(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new RegistryException($"The registry answer has no '{property}' text.");

    public static IReadOnlyList<RegistryAdapter> CreateAll() => [new NpmAdapter(), new PackagistAdapter(), new PypiAdapter(), new NuGetAdapter(), new GoAdapter(), new CratesAdapter(), new RubyGemsAdapter(), new PubAdapter()];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    protected static partial Regex SimpleName();
}

public sealed class NpmAdapter() : RegistryAdapter("npm", "https://registry.npmjs.org", "https://registry.npmjs.org")
{
    public override string Accept => "application/vnd.npm.install-v1+json";
    public override IReadOnlyList<Dependency> Dependencies(string text) => Manifests.ReadNpm(text);
    public override Uri? NamesUrl(string prefix, int pageSize) => new($"{SearchBase}/-/v1/search?text={Q(prefix)}&size={pageSize}");

    public override IReadOnlyList<string> Names(string body, string prefix)
    {
        using var doc = Parse(body);
        return [.. Items(doc.RootElement, "objects").Select(o => o.TryGetProperty("package", out var p) ? Text(p, "name") : throw new RegistryException("The registry answer has no 'package'."))];
    }

    public override Uri VersionsUrl(string name) => new($"{MetaBase}/{Q(name)}");

    public override IReadOnlyList<string> VersionList(string body, string name)
    {
        using var doc = Parse(body);
        return doc.RootElement.TryGetProperty("versions", out var v) && v.ValueKind == JsonValueKind.Object ? [.. v.EnumerateObject().Select(x => x.Name)] : throw new RegistryException("The registry answer has no 'versions'.");
    }
}

public sealed class PackagistAdapter() : RegistryAdapter("packagist", "https://packagist.org", "https://repo.packagist.org")
{
    public override IReadOnlyList<Dependency> Dependencies(string text) => Manifests.ReadComposer(text);
    public override bool IsPackage(string name) => name.Contains('/', StringComparison.Ordinal);
    public override Uri? NamesUrl(string prefix, int pageSize) => new($"{SearchBase}/search.json?q={Q(prefix)}&per_page={pageSize}");

    public override IReadOnlyList<string> Names(string body, string prefix)
    {
        using var doc = Parse(body);
        return [.. Items(doc.RootElement, "results").Select(r => Text(r, "name"))];
    }

    public override Uri VersionsUrl(string name) => new($"{MetaBase}/p2/{string.Join('/', name.Split('/').Select(Q))}.json");

    public override IReadOnlyList<string> VersionList(string body, string name)
    {
        using var doc = Parse(body);
        if (!doc.RootElement.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Object) throw new RegistryException("The registry answer has no 'packages'.");
        var list = packages.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (list.Value.ValueKind != JsonValueKind.Array) throw new RegistryException("The registry answer does not list the package.");
        return [.. list.Value.EnumerateArray().Select(v => Text(v, "version")).Where(v => !v.StartsWith("dev-", StringComparison.Ordinal) && !v.EndsWith("-dev", StringComparison.Ordinal))];
    }
}

/// <summary>PyPI has no public name search, so a name is completed only when the typed text is the exact name of a project.</summary>
public sealed class PypiAdapter() : RegistryAdapter("pypi", "https://pypi.org", "https://pypi.org")
{
    public override IReadOnlyList<Dependency> Dependencies(string text) => Manifests.ReadRequirements(text);
    public override Uri? NamesUrl(string prefix, int pageSize) => SimpleName().IsMatch(prefix) ? VersionsUrl(prefix) : null;

    public override IReadOnlyList<string> Names(string body, string prefix)
    {
        using var doc = Parse(body);
        return doc.RootElement.TryGetProperty("info", out var info) ? [Text(info, "name")] : throw new RegistryException("The registry answer has no 'info'.");
    }

    public override Uri VersionsUrl(string name) => new($"{MetaBase}/pypi/{Q(name)}/json");

    public override IReadOnlyList<string> VersionList(string body, string name)
    {
        using var doc = Parse(body);
        if (!doc.RootElement.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Object) throw new RegistryException("The registry answer has no 'releases'.");
        return [.. releases.EnumerateObject().Where(r => r.Value.ValueKind == JsonValueKind.Array && r.Value.EnumerateArray().Any(f => !(f.TryGetProperty("yanked", out var y) && y.ValueKind == JsonValueKind.True))).Select(r => r.Name)];
    }
}

public sealed class NuGetAdapter() : RegistryAdapter("nuget", "https://azuresearch-usnc.nuget.org", "https://api.nuget.org")
{
    public override IReadOnlyList<Dependency> Dependencies(string text) => Manifests.ReadNuGet(text);
    public override Uri? NamesUrl(string prefix, int pageSize) => new($"{SearchBase}/autocomplete?q={Q(prefix)}&take={pageSize}");

    public override IReadOnlyList<string> Names(string body, string prefix)
    {
        using var doc = Parse(body);
        return [.. Items(doc.RootElement, "data").Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new RegistryException("The registry answer lists a name that is not text."))];
    }

    public override Uri VersionsUrl(string name) => new($"{MetaBase}/v3-flatcontainer/{Q(name.ToLowerInvariant())}/index.json");

    public override IReadOnlyList<string> VersionList(string body, string name)
    {
        using var doc = Parse(body);
        return [.. Items(doc.RootElement, "versions").Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new RegistryException("The registry answer lists a version that is not text."))];
    }
}

/// <summary>The Go module proxy has no search, so a module path is completed only when it is typed whole and the proxy knows it. Versions are tagged releases only.</summary>
public sealed class GoAdapter() : RegistryAdapter("go", "https://proxy.golang.org", "https://proxy.golang.org")
{
    public override IReadOnlyList<Dependency> Dependencies(string text) => Manifests.ReadGo(text);

    /// <summary>The proxy writes upper case letters of a module path as <c>!</c> and the lower case letter.</summary>
    private static string Encode(string module) => string.Concat(module.Select(c => char.IsAsciiLetterUpper(c) ? "!" + char.ToLowerInvariant(c) : c.ToString()));

    private string ModulePath(string module) => string.Join('/', Encode(module).Split('/').Select(Q));

    public override Uri? NamesUrl(string prefix, int pageSize) => prefix.Contains('/', StringComparison.Ordinal) && prefix.Contains('.', StringComparison.Ordinal) ? new($"{SearchBase}/{ModulePath(prefix)}/@latest") : null;

    public override IReadOnlyList<string> Names(string body, string prefix)
    {
        using var doc = Parse(body);
        return doc.RootElement.TryGetProperty("Version", out _) ? [prefix] : throw new RegistryException("The registry answer has no 'Version'.");
    }

    public override Uri VersionsUrl(string name) => new($"{MetaBase}/{ModulePath(name)}/@v/list");

    public override IReadOnlyList<string> VersionList(string body, string name) => [.. body.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];
}

public sealed class CratesAdapter() : RegistryAdapter("crates", "https://crates.io", "https://crates.io")
{
    public override IReadOnlyList<Dependency> Dependencies(string text) => Manifests.ReadCargo(text);
    public override Uri? NamesUrl(string prefix, int pageSize) => new($"{SearchBase}/api/v1/crates?q={Q(prefix)}&per_page={pageSize}");

    public override IReadOnlyList<string> Names(string body, string prefix)
    {
        using var doc = Parse(body);
        return [.. Items(doc.RootElement, "crates").Select(c => Text(c, "name"))];
    }

    public override Uri VersionsUrl(string name) => new($"{MetaBase}/api/v1/crates/{Q(name)}");

    public override IReadOnlyList<string> VersionList(string body, string name)
    {
        using var doc = Parse(body);
        return [.. Items(doc.RootElement, "versions").Where(v => !(v.TryGetProperty("yanked", out var y) && y.ValueKind == JsonValueKind.True)).Select(v => Text(v, "num"))];
    }
}

/// <summary>pub.dev for Dart and Flutter: the package search lists names, the package document lists every version (a retracted one is left out).</summary>
public sealed class PubAdapter() : RegistryAdapter("pub", "https://pub.dev", "https://pub.dev")
{
    public override IReadOnlyList<Dependency> Dependencies(string text) => Manifests.ReadPubspec(text);
    public override Uri? NamesUrl(string prefix, int pageSize) => new($"{SearchBase}/api/search?q={Q(prefix)}");

    public override IReadOnlyList<string> Names(string body, string prefix)
    {
        using var doc = Parse(body);
        return [.. Items(doc.RootElement, "packages").Select(p => Text(p, "package"))];
    }

    public override Uri VersionsUrl(string name) => new($"{MetaBase}/api/packages/{Q(name)}");

    public override IReadOnlyList<string> VersionList(string body, string name)
    {
        using var doc = Parse(body);
        return [.. Items(doc.RootElement, "versions").Where(v => !(v.TryGetProperty("retracted", out var r) && r.ValueKind == JsonValueKind.True)).Select(v => Text(v, "version"))];
    }
}

public sealed class RubyGemsAdapter() : RegistryAdapter("rubygems", "https://rubygems.org", "https://rubygems.org")
{
    public override IReadOnlyList<Dependency> Dependencies(string text) => Manifests.ReadGems(text);
    public override Uri? NamesUrl(string prefix, int pageSize) => new($"{SearchBase}/api/v1/search.json?query={Q(prefix)}");

    public override IReadOnlyList<string> Names(string body, string prefix)
    {
        using var doc = Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new RegistryException("The registry answer is not a list.");
        return [.. doc.RootElement.EnumerateArray().Select(g => Text(g, "name"))];
    }

    public override Uri VersionsUrl(string name) => new($"{MetaBase}/api/v1/versions/{Q(name)}.json");

    public override IReadOnlyList<string> VersionList(string body, string name)
    {
        using var doc = Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new RegistryException("The registry answer is not a list.");
        return [.. doc.RootElement.EnumerateArray().Select(g => Text(g, "number"))];
    }
}
