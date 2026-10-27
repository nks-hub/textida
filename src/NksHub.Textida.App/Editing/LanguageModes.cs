using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;

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
    public const string MakefileId = "makefile";

    private static readonly CommentStyle Slash = new("//", true);
    private static readonly CommentStyle Hash = new("#", false);

    private static readonly LanguageMode[] Builtin =
    [
        new(PlainId, "", [".txt", ".text"], CommentStyle.None),
        Catalog("html", "HTML", CommentStyle.None),
        Catalog("css", "CSS", new(null, true)),
        Catalog("javascript", "JavaScript", Slash),
        Catalog("typescript", "TypeScript", Slash),
        Catalog("json", "JSON", CommentStyle.None),
        new("xml", "XML", [".xml", ".xsd", ".xaml", ".axaml", ".csproj", ".props", ".targets", ".svg", ".config"], CommentStyle.None),
        Catalog("csharp", "C#", Slash),
        Catalog("c", "C", Slash),
        Catalog("cpp", "C++", Slash),
        Catalog("java", "Java", Slash),
        Catalog("php", "PHP", Slash),
        new("razor", "Razor", [".cshtml", ".razor"], CommentStyle.None),
        Catalog("latte", "Latte", CommentStyle.None),
        Catalog("neon", "NEON", Hash),
        new("sln", "Solution", [".sln"], Hash),
        Catalog("python", "Python", Hash),
        Catalog("sql", "SQL", new("--", false)),
        new("markdown", "Markdown", [".md", ".markdown"], CommentStyle.None),
        new("yaml", "YAML", [".yml", ".yaml"], Hash),
        new("ini", "INI", [".ini", ".cfg", ".conf"], new(";", false)),
        Catalog("shell", "Shell", Hash),
        Catalog("powershell", "PowerShell", Hash),
        new("batch", "Batch", [".bat", ".cmd"], CommentStyle.None),
        Catalog("go", "Go", Slash),
        Catalog("gomod", "Go module", new("//", false)),
        Catalog("gotmpl", "Go template", CommentStyle.None),
        Catalog("rust", "Rust", Slash),
        Catalog("toml", "TOML", Hash),
        Catalog("cmake", "CMake", Hash),
        Catalog(MakefileId, "Makefile", Hash),
        Catalog("meson", "Meson", Hash),
        Catalog("ruby", "Ruby", Hash),
        Catalog("erb", "ERB", CommentStyle.None),
        Catalog("haml", "HAML", new("-#", false)),
        Catalog("slim", "Slim", new("/", false)),
        Catalog("rbs", "RBS", Hash),
        Catalog("kotlin", "Kotlin", Slash),
        Catalog("lua", "Lua", new("--", false)),
        Catalog("swift", "Swift", Slash),
        Catalog("dart", "Dart", Slash),
        Catalog("r", "R", Hash),
        new(LogId, "Log", [".log"], CommentStyle.None),
    ];

    /// <summary>A mode of a group of <see cref="LanguageCatalog"/>: the extensions and file names come from the catalog, the name and the comments are the editor's.</summary>
    private static LanguageMode Catalog(string id, string name, CommentStyle comments)
    {
        var mode = LanguageCatalog.Mode(id) ?? throw new InvalidOperationException($"The language catalog has no mode '{id}'.");
        return new(id, name, [.. mode.Extensions], comments) { FileNames = [.. mode.FileNames] };
    }

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

    /// <summary>Id of the built-in grammar that colours the mode; null where none exists (plain text and the modes without a grammar).</summary>
    public static string? GrammarId(LanguageMode mode)
    {
        string id = LanguageCatalog.Mode(mode.Id)?.Grammar ?? mode.Id;
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
        var file = path is null ? "" : Path.GetFileName(path);
        return Builtin.FirstOrDefault(l => l.FileNames.Contains(file, StringComparer.OrdinalIgnoreCase))
            ?? Builtin.FirstOrDefault(l => l.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) ?? Plain;
    }
}
