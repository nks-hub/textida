using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.Workbench.Commands;

/// <summary>One row of the command palette. <paramref name="DisabledReason"/> says in words why a command that is listed cannot run now.</summary>
public sealed record PaletteItem(string CommandId, string Title, string? Category, string? Shortcut, bool Enabled, int Score, string? DisabledReason = null);

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
            var enabled = c.IsEnabled(context);
            items.Add(new PaletteItem(c.Id, title, category, shortcut, enabled, score.Value, enabled ? null : ReasonFor(c, context, strings)));
        }

        return items
            .OrderByDescending(i => i.Score)
            .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.CommandId, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// The condition that keeps a command from running: the context key whose opposite value would make its enablement true.
    /// Null when no single key explains it. The text is <c>reason.{key}.{true|false}</c> in the string table, with the current value of the key.
    /// </summary>
    private static string? ReasonFor(CommandDescriptor command, IWhenContext context, StringTable strings)
    {
        if (command.Enablement is not { } enablement)
        {
            return null;
        }

        // without a document the context also says "read-only"; the missing document is the real reason
        if (context.Get(ContextKeys.HasDocument) != "true" && enablement.Keys.Contains(ContextKeys.ReadOnly))
        {
            return strings.Get("reason.hasDocument.false");
        }

        foreach (var key in enablement.Keys)
        {
            if (enablement.Evaluate(new Flipped(context, key)))
            {
                var textKey = $"reason.{key}.{(context.Get(key) == "true" ? "true" : "false")}";
                return strings.Contains(textKey) ? strings.Get(textKey) : null;
            }
        }

        return null;
    }

    private sealed class Flipped(IWhenContext inner, string key) : IWhenContext
    {
        public string? Get(string name) => name == key ? (inner.Get(name) == "true" ? "false" : "true") : inner.Get(name);
    }
}
