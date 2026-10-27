using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// A language mode of a document: the name shown in the status bar, the file extensions that select it and how its comments continue.
/// The mode drives the status bar, the per-language settings layer, comment continuation and the highlighting grammar.
/// </summary>
internal sealed record LanguageMode(string Id, string Name, string[] Extensions, CommentStyle Comments)
{
    /// <summary>Exact file names that select the mode (of a highlighter from a data plugin).</summary>
    public string[] FileNames { get; init; } = [];
}

internal static class LanguageModes
{
    public const string PlainId = "plaintext";
    public const string LogId = "log";

    private static readonly CommentStyle Slash = new("//", true);
    private static readonly CommentStyle Hash = new("#", false);

    private static readonly LanguageMode[] Builtin =
    [
        new(PlainId, "", [".txt", ".text"], CommentStyle.None),
        new("html", "HTML", [".html", ".htm", ".xhtml"], CommentStyle.None),
        new("css", "CSS", [".css"], new(null, true)),
        new("javascript", "JavaScript", [".js", ".mjs", ".cjs", ".jsx"], Slash),
        new("typescript", "TypeScript", [".ts", ".tsx"], Slash),
        new("json", "JSON", [".json", ".jsonc"], CommentStyle.None),
        new("xml", "XML", [".xml", ".xsd", ".xaml", ".axaml", ".csproj", ".props", ".targets", ".svg", ".config"], CommentStyle.None),
        new("csharp", "C#", [".cs"], Slash),
        new("cpp", "C/C++", [".c", ".h", ".cpp", ".hpp", ".cc"], Slash),
        new("java", "Java", [".java"], Slash),
        new("php", "PHP", [".php"], Slash),
        new("razor", "Razor", [".cshtml", ".razor"], CommentStyle.None),
        new("latte", "Latte", [".latte"], CommentStyle.None),
        new("neon", "NEON", [".neon"], Hash),
        new("sln", "Solution", [".sln"], Hash),
        new("python", "Python", [".py"], Hash),
        new("sql", "SQL", [".sql"], new("--", false)),
        new("markdown", "Markdown", [".md", ".markdown"], CommentStyle.None),
        new("yaml", "YAML", [".yml", ".yaml"], Hash),
        new("ini", "INI", [".ini", ".cfg", ".conf"], new(";", false)),
        new("shell", "Shell", [".sh", ".bash", ".zsh"], Hash),
        new("powershell", "PowerShell", [".ps1", ".psm1"], Hash),
        new("batch", "Batch", [".bat", ".cmd"], CommentStyle.None),
        new(LogId, "Log", [".log"], CommentStyle.None),
    ];

    private static LanguageMode[] _user = [];
    private static GrammarRegistry _grammars = BuiltinGrammars.Registry;

    /// <summary>The built-in modes followed by the ones of the highlighters of data plugins.</summary>
    public static IReadOnlyList<LanguageMode> All { get; private set; } = Builtin;

    /// <summary>Grammars the modes colour with: the built-in ones and those of the data plugins.</summary>
    public static GrammarRegistry Grammars => _grammars;

    public static LanguageMode Plain => Builtin[0];

    /// <summary>Makes the highlighters of <paramref name="registry"/> (built on top of the built-in grammars) language modes selected by their extensions and file names.</summary>
    public static void UseUserGrammars(GrammarRegistry registry)
    {
        _grammars = registry;
        _user = [.. registry.All.OrderBy(g => g.Id, StringComparer.Ordinal).Select(g =>
            new LanguageMode(g.Id, g.Name, [.. g.Extensions], new CommentStyle(g.LineComment, false)) { FileNames = [.. g.FileNames] })];
        All = [.. Builtin, .. _user];
    }

    public static Tokenizer Tokenizer(string grammarId) =>
        new(_grammars.Find(grammarId) ?? throw new KeyNotFoundException($"No grammar '{grammarId}'."));

    /// <summary>Id of the built-in grammar that colours the mode; null where none exists yet (plain text and the languages of P8.8.2).</summary>
    public static string? GrammarId(LanguageMode mode)
    {
        string id = mode.Id == "typescript" ? "javascript" : mode.Id;
        return id != PlainId && _grammars.Find(id) is not null ? id : null;
    }

    /// <summary>The grammar of a document: SQL takes the grammar of its dialect, every other mode the one of the mode.</summary>
    public static string? GrammarId(OpenDocument document) =>
        document.Language.Id == "sql" ? SqlDialects.GrammarId(document.SqlDialect) : GrammarId(document.Language);

    public static LanguageMode ById(string id) => All.FirstOrDefault(l => l.Id == id) ?? Plain;

    public static LanguageMode ForPath(string? path)
    {
        if (path is not null && Path.GetFileName(path) is { Length: > 0 } name
            && _user.FirstOrDefault(m => m.FileNames.Contains(name, StringComparer.OrdinalIgnoreCase) || m.Extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))) is { } custom)
            return custom;
        var ext = path is null ? "" : Path.GetExtension(path);
        return Builtin.FirstOrDefault(l => l.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) ?? Plain;
    }
}
