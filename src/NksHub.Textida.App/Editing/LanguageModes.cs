using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// A language mode of a document: the name shown in the status bar, the file extensions that select it and how its comments continue.
/// The mode drives the status bar, the per-language settings layer, comment continuation and the highlighting grammar.
/// </summary>
internal sealed record LanguageMode(string Id, string Name, string[] Extensions, CommentStyle Comments);

internal static class LanguageModes
{
    public const string PlainId = "plaintext";
    public const string LogId = "log";

    private static readonly CommentStyle Slash = new("//", true);
    private static readonly CommentStyle Hash = new("#", false);

    public static IReadOnlyList<LanguageMode> All { get; } =
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

    public static LanguageMode Plain => All[0];

    /// <summary>Id of the built-in grammar that colours the mode; null where none exists yet (plain text and the languages of P8.8.2).</summary>
    public static string? GrammarId(LanguageMode mode)
    {
        string id = mode.Id == "typescript" ? "javascript" : mode.Id;
        return id != PlainId && BuiltinGrammars.Registry.Find(id) is not null ? id : null;
    }

    public static LanguageMode ById(string id) => All.FirstOrDefault(l => l.Id == id) ?? Plain;

    public static LanguageMode ForPath(string? path)
    {
        var ext = path is null ? "" : Path.GetExtension(path);
        return All.FirstOrDefault(l => l.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) ?? Plain;
    }
}
