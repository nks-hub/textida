using System.Text;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.App.Help;

/// <summary>One help page. <paramref name="Commands"/> are the registry commands it documents; <paramref name="Lines"/> are the body, one paragraph, step or bullet per line.</summary>
internal sealed record HelpTopic(string Id, string Title, IReadOnlyList<string> Commands, IReadOnlyList<string> Lines)
{
    /// <summary>Title and body folded for searching.</summary>
    internal string Haystack { get; } = TextMatching.Fold(Title + "\n" + string.Join('\n', Lines));
}

/// <summary>
/// Offline help. One embedded file per UI language (<c>Help/help.{tag}.md</c>); a topic starts with <c>## [id] Title</c>,
/// an optional <c>commands: id, id</c> line follows, then one line per paragraph. A line starting with <c>### </c> is a subheading,
/// <c>- </c> a bullet, <c>1. </c> a numbered step. A missing language falls back to the default one, like the UI texts.
/// </summary>
internal static class HelpCatalog
{
    private static readonly Dictionary<string, IReadOnlyList<HelpTopic>> Pages = StringTable.UiLanguages.ToDictionary(t => t, Load, StringComparer.Ordinal);

    /// <summary>Topic that opens when nothing more specific is known.</summary>
    public const string StartTopic = "start";

    public static IReadOnlyList<HelpTopic> Topics(string language) => Pages.TryGetValue(language, out var topics) ? topics : Pages[Loc.DefaultLanguage];

    public static HelpTopic? Find(string language, string id) => Topics(language).FirstOrDefault(t => t.Id == id);

    public static HelpTopic? ForCommand(string language, string commandId) => Topics(language).FirstOrDefault(t => t.Commands.Contains(commandId));

    /// <summary>Topics containing every word of the query (accents and case ignored), title hits first; all topics for an empty query.</summary>
    public static IReadOnlyList<HelpTopic> Search(string language, string query)
    {
        var words = TextMatching.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var all = Topics(language);
        if (words.Length == 0) return all;
        return [.. all.Where(t => words.All(w => t.Haystack.Contains(w, StringComparison.Ordinal)))
            .OrderByDescending(t => words.All(w => TextMatching.Fold(t.Title).Contains(w, StringComparison.Ordinal)))];
    }

    internal static IReadOnlyList<HelpTopic> Parse(string text)
    {
        var topics = new List<HelpTopic>();
        string? id = null, title = null;
        var commands = new List<string>();
        var lines = new List<string>();

        void Flush()
        {
            if (id is not null) topics.Add(new HelpTopic(id, title!, [.. commands], [.. lines]));
            commands.Clear();
            lines.Clear();
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').TrimEnd();
            if (line.StartsWith("## [", StringComparison.Ordinal) && line.IndexOf("] ", StringComparison.Ordinal) is var close and > 4)
            {
                Flush();
                id = line[4..close];
                title = line[(close + 2)..].Trim();
            }
            else if (id is null || line.Length == 0) continue;
            else if (line.StartsWith("commands:", StringComparison.Ordinal) && lines.Count == 0)
                commands.AddRange(line["commands:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            else lines.Add(line);
        }
        Flush();
        return topics;
    }

    private static IReadOnlyList<HelpTopic> Load(string tag)
    {
        var name = $"NksHub.Textida.App.Help.help.{tag}.md";
        using var stream = typeof(HelpCatalog).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException($"help file {name} is not embedded");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }
}
