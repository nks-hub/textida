using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.Json;
using NksHub.Textida.Plugins.Json.Packages;
using NksHub.Textida.Plugins.Json.Schema;
using NksHub.Textida.Rpc;

// Built-in JSON plugin: a separate process speaking the plugin protocol on stdin/stdout.
// Required options (no defaults, the editor decides): --max-bytes N, --indent N, --max-depth N.
// Schema checks and key completion need all of: --schema-max-bytes N, --schema-depth N, --schema-regex-ms N; without them the plugin only formats.
// Package registry lookups need all of: --registry-config FILE (the consent the editor keeps), --registry-timeout-ms N, --registry-max-bytes N, --registry-page-size N,
// --registry-max-lookups N, --registry-manifest-max-bytes N; --registry-url ID=URL (repeatable) sends one registry's requests elsewhere (a mirror, a test);
// --registry-cache-max-bytes N and --registry-cache-seconds N bound the cache of registry answers (without them it is not bounded and does not expire).
// Schemas from the network (a $schema address, the catalog of known schemas) need the registry options and all of: --schema-cache DIR, --schema-cache-max-bytes N, --schema-download-max-bytes N,
// --schema-cache-max-age-seconds N, --schema-retry-seconds N; without them only carried schemas and schema files on disk are used.
long maxBytes = -1;
int indent = -1, maxDepth = -1;
long schemaMaxBytes = -1;
int schemaDepth = -1, schemaRegexMs = -1;
string? registryConfig = null;
int registryTimeout = -1, registryPage = -1, registryLookups = -1;
long registryBytes = -1, registryManifestBytes = -1, registryCacheBytes = 0;
int registryCacheSeconds = 0;
string? schemaCache = null;
long schemaCacheBytes = -1, schemaDownloadBytes = -1;
int schemaCacheAge = -1, schemaRetry = -1;
var registryUrls = new Dictionary<string, string>();
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--max-bytes") maxBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--indent") indent = int.Parse(args[i + 1]);
    else if (args[i] == "--max-depth") maxDepth = int.Parse(args[i + 1]);
    else if (args[i] == "--schema-max-bytes") schemaMaxBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--schema-depth") schemaDepth = int.Parse(args[i + 1]);
    else if (args[i] == "--schema-regex-ms") schemaRegexMs = int.Parse(args[i + 1]);
    else if (args[i] == "--registry-config") registryConfig = args[i + 1];
    else if (args[i] == "--registry-timeout-ms") registryTimeout = int.Parse(args[i + 1]);
    else if (args[i] == "--registry-max-bytes") registryBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--registry-page-size") registryPage = int.Parse(args[i + 1]);
    else if (args[i] == "--registry-max-lookups") registryLookups = int.Parse(args[i + 1]);
    else if (args[i] == "--registry-manifest-max-bytes") registryManifestBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--registry-cache-max-bytes") registryCacheBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--registry-cache-seconds") registryCacheSeconds = int.Parse(args[i + 1]);
    else if (args[i] == "--schema-cache") schemaCache = args[i + 1];
    else if (args[i] == "--schema-cache-max-bytes") schemaCacheBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--schema-download-max-bytes") schemaDownloadBytes = long.Parse(args[i + 1]);
    else if (args[i] == "--schema-cache-max-age-seconds") schemaCacheAge = int.Parse(args[i + 1]);
    else if (args[i] == "--schema-retry-seconds") schemaRetry = int.Parse(args[i + 1]);
    else if (args[i] == "--registry-url" && args[i + 1].Split('=', 2) is [var registryId, var registryUrl]) registryUrls[registryId] = registryUrl;
}
if (maxBytes < 1 || indent < 0 || maxDepth < 1)
{
    Console.Error.WriteLine("json plugin: --max-bytes, --indent and --max-depth are required.");
    return 2;
}

RegistryClient? registryClient = null;
Func<JsonRpcDispatcher, PackageService>? packages = null;
if (registryConfig is not null && registryTimeout > 0 && registryBytes > 0 && registryPage > 0 && registryLookups > 0 && registryManifestBytes > 0)
{
    var registrySettings = new RegistrySettings(registryConfig, registryTimeout, registryBytes, registryPage, registryLookups, registryManifestBytes, Math.Max(registryCacheBytes, 0), Math.Max(registryCacheSeconds, 0));
    var adapters = RegistryAdapter.CreateAll();
    foreach (var adapter in adapters) if (registryUrls.TryGetValue(adapter.Id, out var url)) adapter.Override(url);
    registryClient = new RegistryClient(registrySettings);
    packages = rpc => new PackageService(rpc, registryClient, registrySettings, adapters);
}
Func<JsonRpcDispatcher, SchemaService>? schema = null;
if (schemaMaxBytes > 0 && schemaDepth > 0 && schemaRegexMs > 0)
{
    var schemaSettings = new SchemaSettings(schemaMaxBytes, schemaDepth, schemaRegexMs);
    var cache = schemaCache is not null && schemaCacheBytes > 0 && schemaDownloadBytes > 0 && schemaCacheAge >= 0 && schemaRetry >= 0
        ? new SchemaCacheSettings(schemaCache, schemaCacheBytes, schemaDownloadBytes, TimeSpan.FromSeconds(schemaCacheAge), TimeSpan.FromSeconds(schemaRetry))
        : null;
    schema = rpc => new SchemaService(rpc, schemaSettings, new SchemaCatalog([]), new SchemaLoader(registryClient, cache, schemaSettings));
}

var plugin = new JsonPlugin(new FormatSettings(maxBytes, indent, maxDepth), schema, packages);
var exit = await PluginRunner.RunAsync(plugin, new RpcLimits(4 * 1024 * 1024, 8, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024));
registryClient?.Dispose();
return exit;
