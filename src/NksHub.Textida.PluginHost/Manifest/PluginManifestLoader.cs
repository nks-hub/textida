using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.PluginHost.Manifest;

/// <summary>A data item (clip, converter, external tool) that a command runs without any process.</summary>
public sealed record DataAction(string Kind, string Id);

/// <param name="Spec">How to start the plugin process; null for a data-only plugin.</param>
/// <param name="Contributions">Commands, menus, keybindings and settings in the shape the editor registries take.</param>
/// <param name="GrammarJson">Grammar files, already checked to parse.</param>
/// <param name="CommandData">Commands that run a data item; every other command belongs to the process.</param>
public sealed record LoadedPlugin(
    string Directory,
    PluginManifest Manifest,
    ContributionManifest Contributions,
    PluginSpec? Spec,
    IReadOnlyList<string> GrammarJson,
    IReadOnlyDictionary<string, DataAction> CommandData);

public sealed record PluginLoadResult(LoadedPlugin? Plugin, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Plugin is not null;
}

/// <summary>
/// Reads and checks a plugin folder before anything of it runs: no process is started and no plugin code is touched.
/// Every problem found is reported; a manifest with any problem is refused as a whole.
/// </summary>
public static class PluginManifestLoader
{
    private static readonly Regex VersionPattern = new(@"^\d+\.\d+\.\d+([-+][0-9A-Za-z.\-+]+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex ItemIdPattern = new(@"^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex ActivationPattern = new(@"^(onCommand|onLanguage|onFileType|onPanel):\S+$", RegexOptions.CultureInvariant);

    /// <param name="directory">The plugin folder with <c>nkspad-plugin.json</c>.</param>
    /// <param name="hostApiVersion">Plugin API version the editor provides.</param>
    /// <param name="maxGrammarBytes">Largest accepted grammar file. No default: the value is an owner decision.</param>
    public static PluginLoadResult Load(string directory, int hostApiVersion, long maxGrammarBytes)
    {
        var root = Path.GetFullPath(directory);
        var file = Path.Combine(root, PluginManifest.FileName);
        if (!File.Exists(file)) return Fail($"No {PluginManifest.FileName} in '{root}'.");
        if (new FileInfo(file).Length > ManifestLoader.MaxBytes) return Fail($"{PluginManifest.FileName} is larger than {ManifestLoader.MaxBytes} bytes.");

        PluginManifest? m;
        try { m = JsonSerializer.Deserialize(File.ReadAllText(file, Encoding.UTF8), PluginManifestJson.Default.PluginManifest); }
        catch (JsonException e) { return Fail($"Invalid {PluginManifest.FileName}: {e.Message}"); }
        if (m is null) return Fail($"{PluginManifest.FileName} is empty.");

        var errors = new List<string>();
        if (m.SchemaVersion != 1) errors.Add($"Unsupported schemaVersion {m.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 128) errors.Add("name must be 1 to 128 characters.");
        if (!VersionPattern.IsMatch(m.Version)) errors.Add($"version '{m.Version}' must look like 1.2.3.");
        if (m.Api.Min < 1 || m.Api.Max < m.Api.Min) errors.Add("api needs min >= 1 and max >= min.");
        else if (hostApiVersion < m.Api.Min || hostApiVersion > m.Api.Max)
            errors.Add($"The plugin supports plugin API {m.Api.Min} to {m.Api.Max}, but this editor provides {hostApiVersion}.");

        var c = m.Contributes;
        var contributions = new ContributionManifest { Id = m.Id, Commands = c.Commands, Menus = c.Menus, Keybindings = c.Keybindings, Settings = c.Settings };
        errors.AddRange(ManifestLoader.Validate(contributions, ManifestTrust.Plugin));

        var grammars = LoadGrammars(root, c.Grammars, maxGrammarBytes, errors);
        var data = CheckDataItems(m, c, errors);
        var spec = CheckMain(root, m, errors);

        if (m.Main is null)
        {
            foreach (var cmd in c.Commands)
                if (!data.ContainsKey(cmd.Id)) errors.Add($"Command '{cmd.Id}' runs nothing: the plugin has no main and no clip, converter or tool names it.");
        }

        return errors.Count > 0 ? new PluginLoadResult(null, errors) : new PluginLoadResult(new LoadedPlugin(root, m, contributions, spec, grammars, data), []);
    }

    /// <summary>Registers commands, menu items and grammars. Nothing is registered if any command id or alias conflicts.</summary>
    public static IReadOnlyList<string> Install(this LoadedPlugin plugin, CommandRegistry commands, GrammarRegistry grammars)
    {
        var conflicts = commands.AddManifest(plugin.Contributions);
        if (conflicts.Count > 0) return conflicts;
        foreach (var json in plugin.GrammarJson) grammars.Add(json);
        return [];
    }

    /// <summary>
    /// Registers the plugin with the host and gives each command that belongs to the process a handler that starts the plugin on
    /// first use (lazy activation) and sends it <c>command/execute</c>. Does nothing for a data-only plugin.
    /// </summary>
    public static void Attach(this LoadedPlugin plugin, CommandRegistry commands, PluginHost host)
    {
        if (plugin.Spec is not { } spec) return;
        host.Register(spec);
        foreach (var cmd in plugin.Contributions.Commands.Where(x => !plugin.CommandData.ContainsKey(x.Id)))
        {
            var id = cmd.Id;
            commands.SetHandler(id, async (_, ct) =>
            {
                await host.ActivateAsync("onCommand:" + id).ConfigureAwait(false);
                var args = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["command"] = id }, PluginManifestStrings.Default.DictionaryStringString);
                var result = await host.CallAsync(spec.Id, "command/execute", args, ct: ct).ConfigureAwait(false);
                return result.ValueKind == JsonValueKind.False ? CommandOutcome.NotHandled : CommandOutcome.Handled;
            });
        }
    }

    private static List<string> LoadGrammars(string root, List<string> paths, long maxBytes, List<string> errors)
    {
        var result = new List<string>();
        var scratch = new GrammarRegistry();
        foreach (var path in paths)
        {
            if (!TryResolveInside(root, path, out var full, out var why)) { errors.Add($"Grammar '{path}': {why}"); continue; }
            if (!File.Exists(full)) { errors.Add($"Grammar '{path}' does not exist."); continue; }
            if (new FileInfo(full).Length > maxBytes) { errors.Add($"Grammar '{path}' is larger than {maxBytes} bytes."); continue; }
            var json = File.ReadAllText(full, Encoding.UTF8);
            try
            {
                var g = scratch.Add(json);
                if (BuiltinGrammars.Registry.Find(g.Id) is not null) errors.Add($"Grammar '{path}': id '{g.Id}' belongs to a built-in grammar.");
                else result.Add(json);
            }
            catch (GrammarException e) { errors.Add($"Grammar '{path}': {e.Message}"); }
        }
        return result;
    }

    private static Dictionary<string, DataAction> CheckDataItems(PluginManifest m, PluginContributions c, List<string> errors)
    {
        var data = new Dictionary<string, DataAction>(StringComparer.Ordinal);
        var commandIds = c.Commands.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        void Item(string kind, string id, string title, string? command)
        {
            if (!ItemIdPattern.IsMatch(id)) errors.Add($"{kind} id '{id}' must be lower case letters, digits and '-'.");
            if (string.IsNullOrWhiteSpace(title)) errors.Add($"{kind} '{id}' needs a title.");
            if (command is null) return;
            if (!commandIds.Contains(command)) errors.Add($"{kind} '{id}' names the unknown command '{command}'.");
            else if (!data.TryAdd(command, new DataAction(kind, id))) errors.Add($"Command '{command}' is named by more than one item.");
        }

        CheckUnique("converter", c.Converters.Select(x => x.Id), errors);
        foreach (var conv in c.Converters)
        {
            Item("converter", conv.Id, conv.Title, conv.Command);
            if (conv.Rules.Count == 0) errors.Add($"converter '{conv.Id}' has no rules.");
            foreach (var r in conv.Rules)
            {
                if (r.Find.Length == 0) errors.Add($"converter '{conv.Id}' has a rule with empty find.");
                else if (r.Regex) CheckRegex(errors, $"converter '{conv.Id}'", r.Find);
            }
        }

        CheckUnique("clip", c.Clips.Select(x => x.Id), errors);
        foreach (var clip in c.Clips) Item("clip", clip.Id, clip.Title, clip.Command);

        CheckUnique("log pattern", c.LogPatterns.Select(x => x.Id), errors);
        foreach (var p in c.LogPatterns)
        {
            Item("log pattern", p.Id, p.Title, null);
            CheckLogPattern(errors, p);
        }

        CheckUnique("external tool", c.ExternalTools.Select(x => x.Id), errors);
        foreach (var t in c.ExternalTools)
        {
            Item("external tool", t.Id, t.Title, t.Command);
            if (string.IsNullOrWhiteSpace(t.Executable)) errors.Add($"external tool '{t.Id}' needs an executable.");
            if (t.TimeoutSeconds is <= 0) errors.Add($"external tool '{t.Id}' has a time limit that is not positive.");
            if (t.LogPattern is { } pattern && !c.LogPatterns.Any(x => x.Id == pattern)) errors.Add($"external tool '{t.Id}' names the unknown log pattern '{pattern}'.");
        }

        return data;
    }

    private static PluginSpec? CheckMain(string root, PluginManifest m, List<string> errors)
    {
        if (m.Main is not { } main) return null;
        if (!ManifestLoader.IsValidId(m.Id)) return null;

        string? exe = null;
        if (string.IsNullOrWhiteSpace(main.Executable)) errors.Add("main.executable is empty.");
        else if (Path.IsPathRooted(main.Executable)) exe = main.Executable;
        else if (main.Executable.IndexOfAny(['/', '\\']) < 0) errors.Add($"main.executable '{main.Executable}' is a bare name; give the full path (a path inside the plugin folder or an absolute one).");
        else if (!TryResolveInside(root, main.Executable, out exe, out var why)) errors.Add($"main.executable: {why}");
        if (exe is not null && !File.Exists(exe)) errors.Add($"main.executable '{exe}' does not exist.");

        var cwd = root;
        if (main.Cwd is not null && !TryResolveInside(root, main.Cwd, out cwd, out var cwdWhy)) errors.Add($"main.cwd: {cwdWhy}");
        else if (!Directory.Exists(cwd)) errors.Add($"main.cwd '{main.Cwd}' does not exist.");

        var events = main.ActivationEvents ?? [.. m.Contributes.Commands.Select(c => "onCommand:" + c.Id)];
        if (events.Count == 0) errors.Add("The plugin has a main but nothing that activates it: add activationEvents or a command.");
        foreach (var e in events)
            if (!ActivationPattern.IsMatch(e)) errors.Add($"Activation event '{e}' must be onCommand:, onLanguage:, onFileType: or onPanel: followed by a value.");

        return errors.Count > 0 || exe is null ? null : new PluginSpec(m.Id, m.Name, exe, main.Args, events, cwd);
    }

    private static bool TryResolveInside(string root, string relative, out string full, out string why)
    {
        full = "";
        why = "";
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) { why = "must be a relative path."; return false; }
        full = Path.GetFullPath(Path.Combine(root, relative));
        if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { why = "leaves the plugin folder."; return false; }
        return true;
    }

    private static void CheckUnique(string what, IEnumerable<string> ids, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (!seen.Add(id)) errors.Add($"Duplicate {what} id '{id}'.");
    }

    private static void CheckLogPattern(List<string> errors, LogPatternContribution p)
    {
        var what = $"log pattern '{p.Id}'";
        if (p.Syntax is not (null or "regex" or "mask")) errors.Add($"{what}: syntax must be 'mask' or 'regex'.");
        else if (p.Pattern.Length == 0 && p.Levels is { Count: > 0 }) { /* levels only */ }
        else if (p.Syntax == "mask")
        {
            try { _ = LogPattern.Compile(p.Pattern, LogPatternSyntax.Mask); }
            catch (LogPatternException e) { errors.Add($"{what}: {e.Message}"); }
        }
        else CheckRegex(errors, what, p.Pattern);
        if (p.Levels is null) return;
        try { _ = LogLevelRules.Create(p.Levels); }
        catch (LogPatternException e) { errors.Add($"{what}: {e.Message}"); }
    }

    private static void CheckRegex(List<string> errors, string what, string pattern)
    {
        try { _ = new Regex(pattern, RegexOptions.CultureInvariant); }
        catch (ArgumentException e) { errors.Add($"{what}: invalid regular expression: {e.Message}"); }
    }

    private static PluginLoadResult Fail(string error) => new(null, [error]);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class PluginManifestStrings : System.Text.Json.Serialization.JsonSerializerContext;
