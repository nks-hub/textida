using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Json.Packages;

/// <summary>Texts of the package check, in the languages of the editor (cs, en, sk, de); the editor passes the language with the command.</summary>
internal sealed record PackageTexts(string Newer, string Cached, string[] Proxy)
{
    public string NewerVersion(string latest, string pinned, bool cached) => Newer.Replace("{latest}", latest).Replace("{pinned}", pinned) + (cached ? " " + Cached : "");

    /// <summary>What the user is told when the network proxy is why a lookup did not happen; the English text of the exception for a problem without a text here.</summary>
    public string Failure(RegistryException e) => e.Problem switch
    {
        RegistryProblem.ProxyNeedsLogin => Proxy[0],
        RegistryProblem.ProxyLoginRefused => Proxy[1],
        RegistryProblem.ProxyUnavailable => Proxy[2],
        RegistryProblem.ProxyNoPassword => Proxy[3],
        _ => e.Message,
    };

    public static PackageTexts For(string language) => language switch
    {
        "cs" => new("Je dostupná novější verze: {latest} (soubor má {pinned}).", "(odpověď z cache)",
        [
            "Proxy žádá přihlášení. Zadejte uživatelské jméno a heslo v nastavení síťové proxy editoru.",
            "Proxy přihlášení odmítla. Zkontrolujte uživatelské jméno a heslo v nastavení síťové proxy editoru.",
            "Síťové nastavení editoru nelze použít, proto se žádný dotaz neposílá.",
            "Proxy vyžaduje přihlášení, ale její heslo není uložené. Zadejte síťovou proxy znovu.",
        ]),
        "sk" => new("Je dostupná novšia verzia: {latest} (súbor má {pinned}).", "(odpoveď z cache)",
        [
            "Proxy žiada prihlásenie. Zadajte používateľské meno a heslo v nastaveniach sieťového proxy editora.",
            "Proxy odmietol prihlásenie. Skontrolujte používateľské meno a heslo v nastaveniach sieťového proxy editora.",
            "Sieťové nastavenia editora nemožno použiť, preto sa žiadny dotaz neposiela.",
            "Proxy vyžaduje prihlásenie, ale jeho heslo nie je uložené. Zadajte sieťový proxy znova.",
        ]),
        "de" => new("Neuere Version verfügbar: {latest} (die Datei legt {pinned} fest).", "(Antwort aus dem Cache)",
        [
            "Der Proxy verlangt eine Anmeldung. Geben Sie Benutzername und Passwort in den Netzwerk-Proxy-Einstellungen des Editors ein.",
            "Der Proxy hat die Anmeldung abgelehnt. Prüfen Sie Benutzername und Passwort in den Netzwerk-Proxy-Einstellungen des Editors.",
            "Die Netzwerkeinstellungen des Editors können nicht verwendet werden, deshalb wird keine Abfrage gesendet.",
            "Der Proxy verlangt eine Anmeldung, aber sein Passwort ist nicht gespeichert. Geben Sie den Netzwerk-Proxy erneut ein.",
        ]),
        _ => new("A newer version is available: {latest} (the file pins {pinned}).", "(answer from cache)",
        [
            "The proxy asks for a login. Enter the user name and password in the network proxy settings of the editor.",
            "The proxy refused the login. Check the user name and password in the network proxy settings of the editor.",
            "The network settings of the editor cannot be used, so no lookup is made.",
            "The proxy needs a login, but its password is not saved. Enter the network proxy settings again.",
        ]),
    };
}

/// <summary>
/// Package manifests (P8.7.3, P8.7.5): <c>completion/request</c> offers package names and versions from the registry of the manifest, and <c>packages.check</c> marks a pinned
/// version that has a newer release with an information diagnostic of the source <see cref="Source"/>. Every request goes through <see cref="RegistryClient"/>, so nothing
/// is asked without the user's consent; a registry that fails gives no offers and a check that says so.
/// </summary>
public sealed class PackageService(JsonRpcDispatcher rpc, RegistryClient client, RegistrySettings settings, IReadOnlyList<RegistryAdapter> adapters)
{
    public const string Check = "packages.check";
    public const string Source = "packages";

    /// <summary>Shown after the registry name in the detail of an offer whose list came from the cache.</summary>
    public const string CachedMark = "cache";

    private readonly DocumentAccess _doc = new(rpc);

    private RegistryAdapter? AdapterFor(string? path) => path is not null && PackageManifests.For(path) is { } r ? adapters.FirstOrDefault(a => a.Id == r.Id) : null;

    private static string Detail(RegistryAdapter adapter, bool cached) => cached ? $"{adapter.Id} · {CachedMark}" : adapter.Id;

    /// <summary>
    /// Answers <c>completion/request</c>. In the name of an entry it offers the names the registry finds for the typed text; in the version of an entry it offers the versions
    /// of that package, newest first, that start with the typed text. Everywhere else, without consent, in a file that is too large and when the registry fails, it offers nothing.
    /// </summary>
    public async Task<byte[]> CompleteAsync(JsonElement p)
    {
        string id = p.GetProperty("document").GetString()!;
        long revision = p.GetProperty("revision").GetInt64(), offset = p.GetProperty("offset").GetInt64();
        var items = new StringBuilder();
        try
        {
            var d = await _doc.InfoAsync(id, revision);
            if (AdapterFor(d.Path) is { } adapter && d.Length <= settings.ManifestMaxBytes && offset >= d.Bom && offset <= d.Length
                && await _doc.ReadAsync(d) is { } text && await _doc.ReadAsync(d, offset) is { } before)
            {
                int caret = before.Length;
                var dependencies = adapter.Dependencies(text);
                var (names, start, end, cached) = await OffersAsync(adapter, dependencies, text, caret);
                var bytes = DocumentAccess.ToBytes(d, text, [start, end]);
                foreach (var (label, detail) in names)
                {
                    if (items.Length > 0) items.Append(',');
                    items.Append($"{{\"label\":\"{DocumentAccess.Quote(label)}\",\"detail\":\"{DocumentAccess.Quote(detail ?? Detail(adapter, cached))}\",\"kind\":\"{(detail is null ? "module" : "value")}\",\"start\":{bytes[start]},\"end\":{bytes[end]},\"text\":\"{DocumentAccess.Quote(label)}\"}}");
                }
            }
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.RevisionConflict or DocumentErrorCodes.InvalidRange) { items.Clear(); }
        return Encoding.UTF8.GetBytes($"{{\"revision\":{revision},\"items\":[{items}]}}");
    }

    private async Task<(List<(string Label, string? Detail)> Items, int Start, int End, bool Cached)> OffersAsync(RegistryAdapter adapter, IReadOnlyList<Dependency> dependencies, string text, int caret)
    {
        try
        {
            if (dependencies.FirstOrDefault(x => x.NameStart <= caret && caret <= x.NameEnd) is { } named)
            {
                string prefix = text[named.NameStart..caret];
                if (prefix.Length == 0 || adapter.NamesUrl(prefix, settings.PageSize) is not { } url) return ([], 0, 0, false);
                var answer = await client.GetAsync(adapter, url);
                return ([.. adapter.Names(answer.Body, prefix).Where(n => n.Length > 0).Take(settings.PageSize).Select(n => (n, (string?)null))], named.NameStart, named.NameEnd, answer.Cached);
            }
            if (dependencies.FirstOrDefault(x => x.VersionStart >= 0 && x.VersionStart <= caret && caret <= x.VersionEnd) is { } versioned && adapter.IsPackage(versioned.Name))
            {
                string prefix = text[versioned.VersionStart..caret];
                var answer = await client.GetAsync(adapter, adapter.VersionsUrl(versioned.Name));
                var versions = adapter.VersionList(answer.Body, versioned.Name).Where(v => v.StartsWith(prefix, StringComparison.Ordinal)).Distinct().ToList();
                versions.Sort((a, b) => Versions.Compare(b, a));
                string detail = Detail(adapter, answer.Cached);
                return ([.. versions.Select(v => (v, (string?)(Versions.IsPrerelease(v) ? detail + " · pre" : detail)))], versioned.VersionStart, versioned.VersionEnd, answer.Cached);
            }
        }
        catch (Exception e) when (e is ConsentRequiredException or RegistryException) { /* no offers: the check reports why */ }
        return ([], 0, 0, false);
    }

    /// <summary>
    /// Handles <c>packages.check</c> for a manifest at one revision: every pinned entry whose registry lists a newer release gets an information diagnostic on its version.
    /// The answer is <c>{"status":..., "registry":id|null, "issues":n, "cached":bool, "message":text|null}</c>; <c>status</c> is <c>ok</c>, <c>noConsent</c>, <c>error</c> (the registry failed;
    /// <c>message</c> says how), <c>notManifest</c>, <c>tooLarge</c>, <c>invalidBytes</c> or <c>stale</c>. <c>refresh</c> drops the cached answers of the registry first.
    /// </summary>
    public async Task<byte[]> CheckAsync(JsonElement p)
    {
        string id = p.GetProperty("document").GetString()!;
        long revision = p.GetProperty("revision").GetInt64();
        var texts = PackageTexts.For(p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en");
        bool refresh = p.TryGetProperty("refresh", out var r) && r.ValueKind == JsonValueKind.True;
        string status = "ok", registry = "null", message = "null";
        int issues = 0;
        bool anyCached = false;
        try
        {
            var d = await _doc.InfoAsync(id, revision);
            var adapter = AdapterFor(d.Path);
            string? text = null;
            var found = new List<(int Start, int End, string Message)>();
            if (adapter is null) status = "notManifest";
            else
            {
                registry = $"\"{adapter.Id}\"";
                if (d.Length > settings.ManifestMaxBytes) status = "tooLarge";
                else if ((text = await _doc.ReadAsync(d)) is null) status = "invalidBytes";
                else
                {
                    if (refresh) client.Refresh(adapter.Id);
                    var pinned = adapter.Dependencies(text).Where(x => x.Pinned && adapter.IsPackage(x.Name)).ToList();
                    var latest = new Dictionary<string, (string? Version, bool Cached)>(StringComparer.Ordinal);
                    foreach (var dependency in pinned)
                    {
                        if (!latest.TryGetValue(dependency.Name, out var known))
                        {
                            if (latest.Count >= settings.MaxLookups) continue;
                            try
                            {
                                var answer = await client.GetAsync(adapter, adapter.VersionsUrl(dependency.Name));
                                known = (Versions.Latest(adapter.VersionList(answer.Body, dependency.Name)), answer.Cached);
                            }
                            catch (RegistryException e) when (e.NotFound) { known = (null, false); }
                            latest[dependency.Name] = known;
                        }
                        if (known.Version is { } newest && Versions.Compare(dependency.Version!, newest) < 0)
                        {
                            anyCached |= known.Cached;
                            found.Add((dependency.VersionStart, dependency.VersionEnd, texts.NewerVersion(newest, dependency.Version!, known.Cached)));
                        }
                    }
                }
            }
            await PublishAsync(d, id, revision, text, found);
            issues = found.Count;
        }
        catch (ConsentRequiredException) { status = "noConsent"; await PublishEmptyAsync(id, revision); }
        catch (RegistryException e)
        {
            status = "error";
            message = $"\"{DocumentAccess.Quote(texts.Failure(e))}\"";
            await PublishEmptyAsync(id, revision);
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict) { status = "stale"; }
        return Encoding.UTF8.GetBytes($"{{\"status\":\"{status}\",\"registry\":{registry},\"issues\":{issues},\"cached\":{(anyCached ? "true" : "false")},\"message\":{message}}}");
    }

    private async Task PublishAsync(DocumentAccess.Doc d, string id, long revision, string? text, List<(int Start, int End, string Message)> found)
    {
        var bytes = text is null ? [] : DocumentAccess.ToBytes(d, text, found.SelectMany(f => new[] { f.Start, f.End }));
        var json = new StringBuilder($"{{\"document\":\"{id}\",\"revision\":{revision},\"source\":\"{Source}\",\"diagnostics\":[");
        for (int n = 0; n < found.Count; n++)
        {
            if (n > 0) json.Append(',');
            json.Append($"{{\"start\":{bytes[found[n].Start]},\"end\":{bytes[found[n].End]},\"severity\":\"information\",\"message\":\"{DocumentAccess.Quote(found[n].Message)}\",\"code\":\"newer-version\"}}");
        }
        json.Append("]}");
        await _doc.CallAsync(DocumentMethods.DiagnosticsPublish, json.ToString());
    }

    private async Task PublishEmptyAsync(string id, long revision)
    {
        try { await _doc.CallAsync(DocumentMethods.DiagnosticsPublish, $"{{\"document\":\"{id}\",\"revision\":{revision},\"source\":\"{Source}\",\"diagnostics\":[]}}"); }
        catch (RpcException) { /* the document moved on; the next check publishes again */ }
    }

    /// <summary>Offers the editor to ask this plugin for completion; sent on every start, so a restarted process answers again.</summary>
    public async Task RegisterAsync() => await _doc.CallAsync(DocumentMethods.CompletionRegister, "{\"enabled\":true}");
}
