using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.Workbench.Settings;

public enum SearchHitKind { Setting, Keybinding }

/// <summary>
/// One row of the settings search. A setting row carries its effective value and where it comes from;
/// a keybinding row carries its shortcut, source and the conflicts it takes part in.
/// </summary>
public sealed record SearchHit(
    SearchHitKind Kind,
    string Id,
    string Title,
    string? Description,
    string? Category,
    string? ValueText,
    SettingSource? ValueSource,
    string? Shortcut,
    string? ShortcutSource,
    IReadOnlyList<KeyConflict> Conflicts,
    int Score)
{
    /// <summary>True when the shortcut is part of a conflict the user should resolve.</summary>
    public bool HasConflict => Conflicts.Any(c => c.IsAmbiguous);
}

/// <summary>
/// Search model for the settings page: matches settings (key, title, description, tags, current value) and
/// keybindings (command id, title, shortcut) with every word of the query. Matching ignores case and diacritics.
/// The settings view itself is not part of this library.
/// </summary>
public sealed class SettingsSearchIndex
{
    private sealed record SettingEntry(SettingDefinition Definition, string Title, string? Description, string? Category, string Key, string TitleF, string DescriptionF, string TagsF, string CategoryF);

    private readonly List<SettingEntry> _settings;
    private readonly StringTable _strings;

    public SettingsSearchIndex(SettingsRegistry registry, StringTable strings)
    {
        _strings = strings;
        _settings = registry.Definitions.Select(d =>
        {
            var title = strings.Get(d.Title);
            var description = d.Description is null ? null : strings.Get(d.Description);
            var category = d.Category is null ? null : strings.Get(d.Category);
            return new SettingEntry(d, title, description, category, TextMatching.Fold(d.Key), TextMatching.Fold(title),
                TextMatching.Fold(description), TextMatching.Fold(string.Join(' ', d.Tags)), TextMatching.Fold(category));
        }).ToList();
    }

    /// <summary>Hits for the query, best first; settings before keybindings on equal score. An empty query lists all settings.</summary>
    public IReadOnlyList<SearchHit> Search(string query, SettingsStack stack, CommandRegistry commands, Keymap keymap, string? language = null)
    {
        var words = TextMatching.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<SearchHit>();

        foreach (var e in _settings)
        {
            var resolved = stack.Resolve(e.Definition.Key, language)!;
            var valueText = resolved.ValueText;
            var valueF = TextMatching.Fold(valueText.Trim('"'));
            var score = Score(words, (e.Key, 10), (e.TitleF, 8), (e.TagsF, 6), (e.CategoryF, 4), (e.DescriptionF, 3), (valueF, 2));
            if (score is not null)
            {
                hits.Add(new SearchHit(SearchHitKind.Setting, e.Definition.Key, e.Title, e.Description, e.Category, valueText,
                    resolved.Source, null, null, [], score.Value));
            }
        }

        foreach (var b in keymap.Bindings)
        {
            if (!commands.TryGet(b.CommandId, out var command))
            {
                continue;
            }

            var title = _strings.Get(command.TitleKey);
            var category = command.CategoryKey is null ? null : _strings.Get(command.CategoryKey);
            var shortcut = b.Sequence.Display(keymap.Platform);
            var score = Score(words, (TextMatching.Fold(command.Id), 6), (TextMatching.Fold(title), 8), (TextMatching.Fold(category), 4),
                (TextMatching.Fold(shortcut), 5), (TextMatching.Fold(b.Sequence.ToString()), 5));
            if (words.Length > 0 && score is not null)
            {
                hits.Add(new SearchHit(SearchHitKind.Keybinding, command.Id, title, null, category, null, null, shortcut,
                    $"{b.Layer}: {b.Source}", keymap.ConflictsOf(b), score.Value));
            }
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Kind)
            .ThenBy(h => h.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(h => h.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static int? Score(string[] words, params (string Text, int Weight)[] fields)
    {
        var total = 0;
        foreach (var word in words)
        {
            var best = 0;
            foreach (var (text, weight) in fields)
            {
                var at = text.IndexOf(word, StringComparison.Ordinal);
                if (at >= 0)
                {
                    best = Math.Max(best, weight + (at == 0 || !char.IsLetterOrDigit(text[at - 1]) ? 2 : 0));
                }
            }

            if (best == 0)
            {
                return null;
            }

            total += best;
        }

        return total;
    }
}
