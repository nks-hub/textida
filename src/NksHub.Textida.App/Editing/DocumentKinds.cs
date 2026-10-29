using NksHub.Textida.Core.Files;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Editing;

/// <summary>What the menus and commands need to know about the kind of a document: the kind of file, the group of languages, and whether it is a package manifest. Pure rules over names; nothing is read from the disk.</summary>
internal static class DocumentKinds
{
    /// <summary>
    /// The kind of file of a document, in lower case: <c>text</c>, <c>code</c>, <c>markup</c>, <c>data</c>, <c>style</c>, <c>image</c>, <c>archive</c>, <c>binary</c> or <c>file</c>. The name of the file decides (<see cref="FileKinds"/>);
    /// a name that says nothing (a new document, a file without a known extension) falls back to the language of the document. A picture file counts as a picture in every view; the tab of a comparison or of the search results has no kind.
    /// </summary>
    public static string KindOf(OpenDocument od)
    {
        if (od.IsResults) return "";
        if (od.IsImageFile) return "image";
        string? name = od.Path is { } path ? System.IO.Path.GetFileName(path) : od.Doc.VirtualName;
        if (!string.IsNullOrEmpty(name) && FileKinds.Of(name, isDirectory: false) is var kind && kind != FileKind.File) return kind.ToString().ToLowerInvariant();
        return KindOfLanguage(od.Language.Id);
    }

    /// <summary>The kind a language implies, for a document whose name does not say.</summary>
    public static string KindOfLanguage(string languageId) => languageId switch
    {
        "html" or "xml" or "razor" or "latte" or "blade" or "twig" or "erb" or "haml" or "slim" => "markup",
        "css" => "style",
        "json" or "yaml" or "ini" or "toml" or "neon" => "data",
        "plaintext" or "markdown" or "log" => "text",
        _ => LanguageCatalog.Mode(languageId) is not null ? "code" : "text",
    };

    /// <summary>The id of the group of <see cref="LanguageCatalog"/> that has the language as a mode; empty when no group has it (plain text, XML, Markdown and the modes of data plugins).</summary>
    public static string GroupOf(string languageId) => LanguageCatalog.Groups.FirstOrDefault(g => g.Modes.Any(m => m.Id == languageId))?.Id ?? "";

    /// <summary>True when the file name is one of the package manifests of the catalog (<c>package.json</c>, <c>go.mod</c>, <c>*.csproj</c>, ...).</summary>
    public static bool IsPackageManifest(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        return LanguageCatalog.Groups.SelectMany(g => g.Manifests).Any(m => m.File.StartsWith('*') ? name.EndsWith(m.File[1..], StringComparison.OrdinalIgnoreCase) : name.Equals(m.File, StringComparison.OrdinalIgnoreCase));
    }
}
