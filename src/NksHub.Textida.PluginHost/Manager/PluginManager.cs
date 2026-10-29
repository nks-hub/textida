using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.PluginHost.Manager;

/// <summary>A ZIP that was unpacked and checked but not installed yet. Pass it to <see cref="PluginManager.CommitAsync"/> with the permissions the user agreed to, or to <see cref="PluginManager.Discard"/>.</summary>
/// <param name="NewPermissions">What the plugin asks for that the user has never been asked about; the user has to agree to every one of them.</param>
/// <param name="InstalledVersion">The version this one would replace; null for a first install.</param>
/// <param name="FromInternet">The ZIP carries the Mark-of-the-Web: it was downloaded.</param>
public sealed record PluginPackage(string Staging, LoadedPlugin Plugin, IReadOnlyList<string> Requested, IReadOnlyList<string> NewPermissions, string? InstalledVersion, bool FromInternet)
{
    public string Id => Plugin.Manifest.Id;
    public string Name => Plugin.Manifest.Name;
    public string Version => Plugin.Manifest.Version;
}

/// <summary>An installed plugin as the manager lists it. <see cref="Problem"/> is set when the active version can no longer be read; the rest then comes from the record only.</summary>
public sealed record ManagedPlugin(string Id, string Name, string Version, string? PreviousVersion, bool Enabled, bool FromInternet,
    IReadOnlyList<string> Requested, IReadOnlyList<string> Granted, string? Problem);

/// <summary>
/// Installs, updates, rolls back, enables, disables and removes third-party plugins in one folder: <c>root/&lt;id&gt;/&lt;version&gt;</c> per version and <c>installed.json</c> for what is active.
/// Nothing runs from a ZIP: it is unpacked next to the folder, checked, and only then moved in. A new version lives beside the old one, becomes active when it loads, and the old one stays for a rollback.
/// Permissions are consent records per plugin and permission, kept in <c>installed.json</c> and mirrored into <see cref="PluginPermissions"/> while the plugin is enabled.
/// Used from one thread (the UI thread); the methods do not lock.
/// </summary>
public sealed class PluginManager
{
    private const string StateFile = "installed.json";
    private const string StagingFolder = ".staging";

    private readonly string _root;
    private readonly int _apiVersion;
    private readonly long _maxGrammarBytes;
    private readonly PluginInstallLimits _limits;
    private readonly IReadOnlySet<string> _reserved;
    private readonly PluginPermissions _permissions;
    private readonly PluginHost? _host;
    private readonly Dictionary<string, LoadedPlugin> _loaded = [];

    /// <param name="root">The folder for installed plugins; it is created when needed.</param>
    /// <param name="reservedIds">Ids of the built-in plugins; a package with one of them is refused.</param>
    /// <param name="host">When given, enabled plugins are registered with it and changes stop and replace the running process.</param>
    public PluginManager(string root, int apiVersion, long maxGrammarBytes, PluginInstallLimits limits, IReadOnlySet<string> reservedIds, PluginPermissions permissions, PluginHost? host = null)
    {
        _root = Path.GetFullPath(root);
        _apiVersion = apiVersion;
        _maxGrammarBytes = maxGrammarBytes;
        _limits = limits;
        _reserved = reservedIds;
        _permissions = permissions;
        _host = host;
    }

    public string Root => _root;

    // ---- state ----

    internal sealed class Record
    {
        public string Current { get; set; } = "";
        public string? Previous { get; set; }
        public bool Enabled { get; set; } = true;
        public bool FromInternet { get; set; }

        /// <summary>Every permission the user was asked about, whether they agreed or not.</summary>
        public List<string> Known { get; set; } = [];

        public List<string> Granted { get; set; } = [];
    }

    internal sealed class State
    {
        public Dictionary<string, Record> Plugins { get; set; } = [];
    }

    private State ReadState()
    {
        try { return JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(_root, StateFile), Encoding.UTF8), PluginManagerJson.Default.State) ?? new State(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new State(); }
    }

    private void WriteState(State state)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, StateFile);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, PluginManagerJson.Default.State), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private string VersionFolder(string id, string version) => Path.Combine(_root, id, version);

    private LoadedPlugin LoadVersion(string id, string version)
    {
        var folder = VersionFolder(id, version);
        var result = PluginManifestLoader.Load(folder, _apiVersion, _maxGrammarBytes, _limits.MaxIconBytes);
        if (result.Plugin is not { } plugin) throw new PluginInstallException(PluginInstallError.InvalidManifest, $"Plugin '{id}' {version} cannot be loaded.", result.Errors);
        if (plugin.Manifest.Id != id || plugin.Manifest.Version != version)
            throw new PluginInstallException(PluginInstallError.InvalidManifest, $"The folder of '{id}' {version} holds another plugin or version.");
        return plugin;
    }

    private LoadedPlugin Active(string id, Record record)
    {
        if (_loaded.TryGetValue(id, out var cached) && cached.Manifest.Version == record.Current) return cached;
        return _loaded[id] = LoadVersion(id, record.Current);
    }

    // ---- listing ----

    public IReadOnlyList<ManagedPlugin> Installed()
    {
        var rows = new List<ManagedPlugin>();
        foreach (var (id, record) in ReadState().Plugins.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            try
            {
                var plugin = Active(id, record);
                rows.Add(new ManagedPlugin(id, plugin.Manifest.Name, record.Current, record.Previous, record.Enabled, record.FromInternet, plugin.Manifest.Permissions, Effective(record, plugin), null));
            }
            catch (PluginInstallException e)
            {
                rows.Add(new ManagedPlugin(id, id, record.Current, record.Previous, record.Enabled, record.FromInternet, [], [], string.Join("; ", e.Details.Prepend(e.Message))));
            }
        }
        return rows;
    }

    private static List<string> Effective(Record record, LoadedPlugin plugin) => [.. plugin.Manifest.Permissions.Where(record.Granted.Contains)];

    // ---- install and update ----

    /// <summary>
    /// Unpacks the ZIP into a staging folder and checks it: every path stays inside the folder, no links, size and entry quotas, the manifest is valid, the id is not a built-in one,
    /// and a plugin that is installed already gets a newer version only. Nothing of the plugin runs and nothing is installed.
    /// </summary>
    public PluginPackage Prepare(string zipPath)
    {
        var staging = Path.Combine(_root, StagingFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var fromInternet = PluginZip.IsFromInternet(zipPath);
            PluginZip.Extract(zipPath, staging, _limits);
            if (!File.Exists(Path.Combine(staging, PluginManifest.FileName)))
                throw new PluginInstallException(PluginInstallError.NoManifest, $"The ZIP has no {PluginManifest.FileName} in its root folder.");
            var result = PluginManifestLoader.Load(staging, _apiVersion, _maxGrammarBytes, _limits.MaxIconBytes);
            if (result.Plugin is not { } plugin) throw new PluginInstallException(PluginInstallError.InvalidManifest, "The manifest is refused.", result.Errors);

            var id = plugin.Manifest.Id;
            if (_reserved.Contains(id)) throw new PluginInstallException(PluginInstallError.IdCollision, $"'{id}' is the id of a built-in plugin.");
            var state = ReadState();
            state.Plugins.TryGetValue(id, out var record);
            if (record is not null && Compare(plugin.Manifest.Version, record.Current) <= 0)
                throw new PluginInstallException(PluginInstallError.NotNewer, $"Version {record.Current} of '{id}' is installed; the ZIP holds {plugin.Manifest.Version}, which is not newer.");

            var requested = plugin.Manifest.Permissions;
            return new PluginPackage(staging, plugin, requested, [.. requested.Where(p => record is null || !record.Known.Contains(p))], record?.Current, fromInternet);
        }
        catch
        {
            DeleteFolder(staging);
            throw;
        }
    }

    public void Discard(PluginPackage package) => DeleteFolder(package.Staging);

    /// <summary>
    /// Installs the package. <paramref name="approved"/> are the permissions the user agreed to; it has to hold every <see cref="PluginPackage.NewPermissions"/>. Permissions the user
    /// decided on before keep their answer. The new version is moved beside the old one and loaded from there; when that fails the old version stays active and the new folder is removed.
    /// </summary>
    public async Task CommitAsync(PluginPackage package, IReadOnlyCollection<string> approved)
    {
        var missing = package.NewPermissions.Where(p => !approved.Contains(p)).ToList();
        if (missing.Count > 0) throw new PluginInstallException(PluginInstallError.ConsentRequired, "The user has not agreed to every new permission.", missing);

        var id = package.Id;
        var state = ReadState();
        var existing = state.Plugins.TryGetValue(id, out var old);
        if (existing && Compare(package.Version, old!.Current) <= 0)
            throw new PluginInstallException(PluginInstallError.NotNewer, $"Version {old.Current} of '{id}' is installed already.");

        var target = VersionFolder(id, package.Version);
        DeleteFolder(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await MoveAsync(package.Staging, target).ConfigureAwait(false);
        LoadedPlugin loaded;
        try { loaded = LoadVersion(id, package.Version); }
        catch
        {
            DeleteFolder(target);
            throw;
        }

        var record = old ?? new Record { Enabled = true };
        var dropped = existing && record.Previous is { } previous && previous != package.Version ? previous : null;
        record.Previous = existing ? record.Current : null;
        record.Current = package.Version;
        record.FromInternet = package.FromInternet;
        foreach (var permission in package.Requested.Where(p => !record.Known.Contains(p))) record.Known.Add(permission);
        foreach (var permission in package.NewPermissions.Where(p => !record.Granted.Contains(p))) record.Granted.Add(permission);
        state.Plugins[id] = record;
        WriteState(state);
        _loaded[id] = loaded;
        if (dropped is not null) DeleteFolder(VersionFolder(id, dropped));
        await ApplyAsync(id, record, loaded).ConfigureAwait(false);
    }

    // ---- rollback, enable, uninstall ----

    /// <summary>Makes the previous version active again; the version it replaces becomes the previous one. A permission the previous version asks for and the user never decided on needs <paramref name="approved"/>.</summary>
    public async Task RollBackAsync(string id, IReadOnlyCollection<string> approved)
    {
        var state = ReadState();
        var record = Find(state, id);
        if (record.Previous is not { } previous || !Directory.Exists(VersionFolder(id, previous)))
            throw new PluginInstallException(PluginInstallError.NothingToRollBack, $"'{id}' has no previous version.");
        var loaded = LoadVersion(id, previous);
        var fresh = loaded.Manifest.Permissions.Where(p => !record.Known.Contains(p)).ToList();
        var missing = fresh.Where(p => !approved.Contains(p)).ToList();
        if (missing.Count > 0) throw new PluginInstallException(PluginInstallError.ConsentRequired, "The previous version asks for permissions the user has not decided on.", missing);

        (record.Current, record.Previous) = (previous, record.Current);
        record.Known.AddRange(fresh);
        record.Granted.AddRange(fresh.Where(p => !record.Granted.Contains(p)));
        WriteState(state);
        _loaded[id] = loaded;
        await ApplyAsync(id, record, loaded).ConfigureAwait(false);
    }

    /// <summary>The permissions the previous version would ask the user about on a rollback.</summary>
    public IReadOnlyList<string> RollBackPermissions(string id)
    {
        var record = Find(ReadState(), id);
        return record.Previous is { } previous ? [.. LoadVersion(id, previous).Manifest.Permissions.Where(p => !record.Known.Contains(p))] : [];
    }

    public async Task SetEnabledAsync(string id, bool enabled)
    {
        var state = ReadState();
        var record = Find(state, id);
        if (record.Enabled == enabled) return;
        record.Enabled = enabled;
        WriteState(state);
        await ApplyAsync(id, record, Active(id, record)).ConfigureAwait(false);
    }

    /// <summary>Grants or takes back one permission the plugin asks for. Taking it back cancels the requests of the plugin that depended on it.</summary>
    public void SetPermission(string id, string permission, bool granted)
    {
        var state = ReadState();
        var record = Find(state, id);
        var loaded = Active(id, record);
        if (!loaded.Manifest.Permissions.Contains(permission))
            throw new PluginInstallException(PluginInstallError.UnknownPlugin, $"'{id}' does not ask for '{permission}'.");
        if (granted) { if (!record.Granted.Contains(permission)) record.Granted.Add(permission); }
        else record.Granted.Remove(permission);
        WriteState(state);
        SyncPermissions(id, record, loaded);
    }

    /// <summary>Removes the plugin and all its versions. Only the folder of this plugin is deleted.</summary>
    public async Task UninstallAsync(string id)
    {
        var state = ReadState();
        Find(state, id);
        if (_host is not null) await _host.UnregisterAsync(id).ConfigureAwait(false);
        _permissions.RevokeAll(id);
        state.Plugins.Remove(id);
        WriteState(state);
        _loaded.Remove(id);
        await DeleteFolderOfStoppedPluginAsync(Path.Combine(_root, id)).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the folder of a plugin whose process has just ended. The system keeps the files of an ended program locked for a moment (longer when the machine is busy), so the delete is repeated
    /// for up to two seconds before the folder is left behind, as <see cref="DeleteFolder"/> leaves it.
    /// </summary>
    private static async Task DeleteFolderOfStoppedPluginAsync(string path)
    {
        for (int attempt = 0; attempt < 20 && Directory.Exists(path); attempt++)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await Task.Delay(100).ConfigureAwait(false); }
        }
    }

    /// <summary>The enabled plugins, loaded; their permissions are set and their processes registered with the host. Call once at startup. A plugin that cannot be read is skipped and reported.</summary>
    public async Task<(IReadOnlyList<LoadedPlugin> Plugins, IReadOnlyList<string> Problems)> StartAsync()
    {
        var plugins = new List<LoadedPlugin>();
        var problems = new List<string>();
        foreach (var (id, record) in ReadState().Plugins)
        {
            try
            {
                var loaded = Active(id, record);
                await ApplyAsync(id, record, loaded).ConfigureAwait(false);
                if (record.Enabled) plugins.Add(loaded);
            }
            catch (PluginInstallException e) { problems.Add($"{id}: {string.Join("; ", e.Details.Prepend(e.Message))}"); }
        }
        return (plugins, problems);
    }

    /// <summary>Folders of the active versions of the enabled plugins that have no process (data plugins), for the app to read their clips, converters and grammars.</summary>
    public IEnumerable<string> DataFolders() =>
        ReadState().Plugins.Where(p => p.Value.Enabled && Loaded(p.Key) is { Spec: null }).Select(p => VersionFolder(p.Key, p.Value.Current));

    /// <summary>Folders of the active versions of all enabled plugins, with a process or without, for the app to read what they contribute as data (clips, converters, external tools, grammars).</summary>
    public IEnumerable<string> ContributingFolders() =>
        ReadState().Plugins.Where(p => p.Value.Enabled && Loaded(p.Key) is not null).Select(p => VersionFolder(p.Key, p.Value.Current));

    /// <summary>The plugin as loaded, for the app to register its commands; null when it is not installed or cannot be read.</summary>
    public LoadedPlugin? Loaded(string id)
    {
        var record = ReadState().Plugins.GetValueOrDefault(id);
        if (record is null) return null;
        try { return Active(id, record); }
        catch (PluginInstallException) { return null; }
    }

    // ---- running state ----

    private async Task ApplyAsync(string id, Record record, LoadedPlugin loaded)
    {
        if (_host is not null)
        {
            await _host.UnregisterAsync(id).ConfigureAwait(false);
            if (record.Enabled && loaded.Spec is { } spec) _host.Register(spec);
        }
        SyncPermissions(id, record, loaded);
    }

    /// <summary>Makes <see cref="PluginPermissions"/> hold what the user granted and the active version still asks for, and nothing while the plugin is disabled.</summary>
    private void SyncPermissions(string id, Record record, LoadedPlugin loaded)
    {
        var wanted = record.Enabled ? Effective(record, loaded) : [];
        foreach (var grant in _permissions.GrantsOf(id).Where(g => !wanted.Contains(g.Permission))) _permissions.Revoke(id, grant.Permission, grant.Scope);
        foreach (var permission in wanted) _permissions.Grant(id, permission, GrantScope.All);
    }

    /// <summary>Moves a folder; a virus scanner holding a just written file makes the move fail for a moment, so it is tried again a few times.</summary>
    private static async Task MoveAsync(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { Directory.Move(from, to); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 5) { await Task.Delay(200 * attempt).ConfigureAwait(false); }
        }
    }

    private static Record Find(State state, string id) =>
        state.Plugins.TryGetValue(id, out var record) ? record : throw new PluginInstallException(PluginInstallError.UnknownPlugin, $"'{id}' is not installed.");

    private static void DeleteFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* left behind; the next install into it replaces it */ }
    }

    /// <summary>Orders <c>major.minor.patch[-suffix]</c>: the numbers first, then a version without suffix before one with, then the suffixes as text.</summary>
    internal static int Compare(string a, string b)
    {
        static (int[] Numbers, string Suffix) Split(string v)
        {
            int cut = v.IndexOfAny(['-', '+']);
            var core = cut < 0 ? v : v[..cut];
            return ([.. core.Split('.').Select(int.Parse)], cut < 0 ? "" : v[cut..]);
        }
        var (an, asuf) = Split(a);
        var (bn, bsuf) = Split(b);
        for (int i = 0; i < Math.Min(an.Length, bn.Length); i++)
            if (an[i] != bn[i]) return an[i].CompareTo(bn[i]);
        if (asuf == bsuf) return 0;
        if (asuf.Length == 0) return 1;
        if (bsuf.Length == 0) return -1;
        return string.CompareOrdinal(asuf, bsuf);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(PluginManager.State))]
internal sealed partial class PluginManagerJson : JsonSerializerContext;
