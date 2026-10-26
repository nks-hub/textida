using NksPad.Workbench.Keys;
using NksPad.Workbench.Matching;

namespace NksPad.Workbench.Commands;

/// <summary>One row of the command palette.</summary>
public sealed record PaletteItem(string CommandId, string Title, string? Category, string? Shortcut, bool Enabled, int Score);

/// <summary>Data source of the command palette: fuzzy match over category and title, with shortcuts.</summary>
public static class CommandPalette
{
    /// <summary>
    /// Available, non-hidden commands matching <paramref name="query"/>, best first. An empty query lists all of them by title.
    /// Disabled commands are listed with <see cref="PaletteItem.Enabled"/> false.
    /// </summary>
    public static IReadOnlyList<PaletteItem> Query(
        CommandRegistry registry, Keymap keymap, StringTable strings, IWhenContext context, string query, int limit = 50)
    {
        var items = new List<PaletteItem>();
        foreach (var c in registry.Commands)
        {
            if (c.Hidden || !c.IsAvailable(context))
            {
                continue;
            }

            var title = strings.Get(c.TitleKey);
            var category = c.CategoryKey is null ? null : strings.Get(c.CategoryKey);
            var label = category is null ? title : category + ": " + title;
            var score = TextMatching.FuzzyScore(query, label) ?? TextMatching.FuzzyScore(query, c.Id);
            if (score is null)
            {
                continue;
            }

            var shortcut = keymap.ShortcutsFor(c.Id).FirstOrDefault();
            items.Add(new PaletteItem(c.Id, title, category, shortcut, c.IsEnabled(context), score.Value));
        }

        return items
            .OrderByDescending(i => i.Score)
            .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.CommandId, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }
}
