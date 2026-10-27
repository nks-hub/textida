using System.Globalization;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Clips;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.App.Services;

internal sealed record ClipEntry(string PluginId, string PluginName, ClipContribution Clip);

internal sealed record TemplateEntry(string PluginId, string PluginName, TemplateContribution Template);

internal sealed record ConverterEntry(string PluginId, string PluginName, ConverterContribution Converter);

/// <summary>A place that explains a language, as a data plugin states it; <paramref name="Source"/> is ready to resolve (a file target is a full path).</summary>
internal sealed record HelpEntry(string PluginId, string PluginName, HelpSource Source);

/// <summary><see cref="Converters"/> are the converters of the same plugin, the ones a <c>convert</c> step of the recipe may name.</summary>
internal sealed record RecipeEntry(string PluginId, string PluginName, RecipeContribution Recipe, IReadOnlyList<ConverterContribution> Converters);

internal enum SaveClipResult { Saved, TooLong, TooLarge, Damaged, IoError }

/// <summary>
/// The clips, file templates, converters and edit recipes of the data plugins in the <c>plugins</c> folder of the profile: one subfolder per plugin with a
/// <c>nkspad-plugin.json</c> and no process. The user's own library is such a plugin too (<see cref="UserFolder"/>); "save selection as clip" writes into it.
/// A plugin with a problem is left out and the reason is kept in <see cref="Problems"/>.
/// </summary>
internal sealed class ClipLibrary
{
    public const string UserPluginId = "user.clips";
    public const string UserFolder = "user-clips";

    private readonly string _directory;
    private readonly CommandRegistry _commands;
    private readonly HashSet<string> _registered = new(StringComparer.Ordinal);
    private readonly List<ClipEntry> _clips = [];
    private readonly List<TemplateEntry> _templates = [];
    private readonly List<ConverterEntry> _converters = [];
    private readonly List<RecipeEntry> _recipes = [];
    private readonly List<HelpEntry> _helps = [];
    private readonly List<(string Command, string PluginId, DataAction Action)> _targets = [];
    private readonly List<string> _problems = [];

    private readonly Func<IEnumerable<string>>? _installedFolders;

    /// <param name="installedFolders">Folders of the enabled plugins the plugin manager installed; the data plugins among them are read like the ones in <paramref name="pluginsDirectory"/>.</param>
    public ClipLibrary(string pluginsDirectory, CommandRegistry commands, Func<IEnumerable<string>>? installedFolders = null)
    {
        _directory = pluginsDirectory;
        _commands = commands;
        _installedFolders = installedFolders;
    }

    public IReadOnlyList<ClipEntry> Clips => _clips;
    public IReadOnlyList<TemplateEntry> Templates => _templates;
    public IReadOnlyList<ConverterEntry> Converters => _converters;
    public IReadOnlyList<RecipeEntry> Recipes => _recipes;
    public IReadOnlyList<HelpEntry> Helps => _helps;
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The highlighters of the data plugins, on top of the built-in grammars (which they may include and embed). Replaced by every <see cref="Load"/>.</summary>
    public GrammarRegistry Grammars { get; private set; } = new(BuiltinGrammars.Registry);

    /// <summary>Commands that run a clip, template, converter or recipe, with the plugin and the item they name; the app gives each a handler.</summary>
    public IReadOnlyList<(string Command, string PluginId, DataAction Action)> CommandTargets => _targets;

    public string UserManifestPath => Path.Combine(_directory, UserFolder, PluginManifest.FileName);

    /// <summary>Reads every plugin folder again. Commands of a plugin are registered the first time it is seen.</summary>
    public void Load()
    {
        _clips.Clear();
        _templates.Clear();
        _converters.Clear();
        _recipes.Clear();
        _helps.Clear();
        _targets.Clear();
        _problems.Clear();
        var grammars = Grammars = new GrammarRegistry(BuiltinGrammars.Registry);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string> own = Directory.Exists(_directory) ? Directory.EnumerateDirectories(_directory).Order(StringComparer.OrdinalIgnoreCase) : [];
        var installed = (_installedFolders?.Invoke() ?? []).ToHashSet();
        foreach (var folder in own.Concat(installed))
        {
            if (!File.Exists(Path.Combine(folder, PluginManifest.FileName))) continue;
            var name = installed.Contains(folder) ? Path.GetFileName(Path.GetDirectoryName(folder)) ?? folder : Path.GetFileName(folder);
            var result = PluginManifestLoader.Load(folder, PluginLimitsPlaceholders.Host().ApiVersion, PluginLimitsPlaceholders.GrammarMaxBytes);
            if (result.Plugin is not { } plugin) { _problems.Add($"{name}: {string.Join("; ", result.Errors)}"); continue; }
            if (plugin.Manifest.Main is not null)
            {
                if (!installed.Contains(folder)) _problems.Add($"{name}: only plugins without a process are read from this folder.");
                continue;
            }
            if (!seen.Add(plugin.Manifest.Id)) { _problems.Add($"{name}: the plugin id '{plugin.Manifest.Id}' is already used by another folder."); continue; }
            if (plugin.GrammarJson.Select(json => Grammar.Parse(json, grammars).Id).FirstOrDefault(g => grammars.All.Any(x => string.Equals(x.Id, g, StringComparison.OrdinalIgnoreCase))) is { } taken)
            {
                _problems.Add($"{name}: the highlighter id '{taken}' is already used by another plugin.");
                continue;
            }
            if (!_registered.Contains(plugin.Manifest.Id))
            {
                var conflicts = _commands.AddManifest(plugin.Contributions);
                if (conflicts.Count > 0) { _problems.Add($"{name}: {string.Join("; ", conflicts)}"); continue; }
                _registered.Add(plugin.Manifest.Id);
            }
            var id = plugin.Manifest.Id;
            foreach (var json in plugin.GrammarJson) grammars.Add(json);
            foreach (var clip in plugin.Manifest.Contributes.Clips) _clips.Add(new ClipEntry(id, plugin.Manifest.Name, clip));
            foreach (var template in plugin.Manifest.Contributes.Templates) _templates.Add(new TemplateEntry(id, plugin.Manifest.Name, template));
            foreach (var converter in plugin.Manifest.Contributes.Converters) _converters.Add(new ConverterEntry(id, plugin.Manifest.Name, converter));
            foreach (var recipe in plugin.Manifest.Contributes.Recipes) _recipes.Add(new RecipeEntry(id, plugin.Manifest.Name, recipe, plugin.Manifest.Contributes.Converters));
            foreach (var h in plugin.Manifest.Contributes.LanguageHelp)
                _helps.Add(new HelpEntry(id, plugin.Manifest.Name, new HelpSource(h.Id, h.Title, h.Language, h.Kind == "file" ? HelpKind.File : HelpKind.Url, h.Kind == "file" ? Path.GetFullPath(Path.Combine(plugin.Directory, h.Target)) : h.Target)));
            foreach (var (command, action) in plugin.CommandData.Where(d => d.Value.Kind is "clip" or "template" or "converter" or "recipe")) _targets.Add((command, id, action));
        }
    }

    public ClipEntry? FindClip(string pluginId, string clipId) => _clips.FirstOrDefault(c => c.PluginId == pluginId && c.Clip.Id == clipId);

    public ConverterEntry? FindConverter(string pluginId, string converterId) => _converters.FirstOrDefault(c => c.PluginId == pluginId && c.Converter.Id == converterId);

    public RecipeEntry? FindRecipe(string pluginId, string recipeId) => _recipes.FirstOrDefault(r => r.PluginId == pluginId && r.Recipe.Id == recipeId);

    public TemplateEntry? FindTemplate(string pluginId, string templateId) => _templates.FirstOrDefault(t => t.PluginId == pluginId && t.Template.Id == templateId);

    /// <summary>
    /// Adds a clip with <paramref name="text"/> taken literally (a dollar sign in it stays one) to the user's library, creating the library on the first use,
    /// and reads the library again so the clip can be inserted at once.
    /// </summary>
    public SaveClipResult AddUserClip(string title, string text, out ClipContribution? clip, out string detail)
    {
        clip = null;
        detail = "";
        var literal = Variables.Escape(text);
        if (literal.Length > PluginLimitsPlaceholders.SnippetLength) return SaveClipResult.TooLong;
        try
        {
            PluginManifest manifest;
            if (File.Exists(UserManifestPath))
            {
                if (new FileInfo(UserManifestPath).Length > ManifestLoader.MaxBytes) return SaveClipResult.TooLarge;
                try { manifest = JsonSerializer.Deserialize(File.ReadAllText(UserManifestPath, Encoding.UTF8), PluginManifestJson.Default.PluginManifest) ?? throw new JsonException("empty"); }
                catch (JsonException e) { detail = e.Message; return SaveClipResult.Damaged; }
                if (manifest.Id != UserPluginId || manifest.Main is not null) { detail = "not the library of the user"; return SaveClipResult.Damaged; }
            }
            else
            {
                int api = PluginLimitsPlaceholders.Host().ApiVersion;
                manifest = new PluginManifest { Id = UserPluginId, Name = "My clips", Version = "1.0.0", Api = new ApiRange { Min = api, Max = api } };
            }

            var ids = manifest.Contributes.Clips.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            string baseId = Slug(title), id = baseId;
            for (int n = 2; !ids.Add(id); n++) id = baseId + "-" + n.ToString(CultureInfo.InvariantCulture);
            clip = new ClipContribution { Id = id, Title = title.Trim(), Text = literal };
            manifest.Contributes.Clips.Add(clip);

            var json = JsonSerializer.Serialize(manifest, PluginManifestJson.Default.PluginManifest);
            if (Encoding.UTF8.GetByteCount(json) > ManifestLoader.MaxBytes) { clip = null; return SaveClipResult.TooLarge; }
            Directory.CreateDirectory(Path.GetDirectoryName(UserManifestPath)!);
            var temp = UserManifestPath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, UserManifestPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            clip = null;
            detail = e.Message;
            return SaveClipResult.IoError;
        }
        Load();
        return SaveClipResult.Saved;
    }

    /// <summary>Lower-case ASCII letters, digits and hyphens made from a title; accents are dropped. Never empty and always starts with a letter.</summary>
    internal static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetter(c)) sb.Append(char.ToLowerInvariant(c));
            else if (char.IsAsciiDigit(c)) sb.Append(c);
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return slug.Length == 0 ? "clip" : char.IsAsciiLetter(slug[0]) ? slug : "clip-" + slug;
    }
}
