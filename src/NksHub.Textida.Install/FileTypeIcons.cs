using System.Security;
using System.Text;

namespace NksHub.Textida.Install;

/// <summary>One icon of a document type: a page with a short label, shared by every extension in <see cref="Extensions"/>.</summary>
/// <param name="Id">File name stem of the icon files and the end of the program id and of the macOS icon name.</param>
/// <param name="Label">The mark printed on the page.</param>
/// <param name="Name">What the group is called in the macOS document types.</param>
/// <param name="Extensions">With the leading dot, all in <see cref="IntegrationExtensions.All"/>.</param>
/// <param name="MimeTypes">The shared-mime-info types that get the icon on Linux.</param>
public sealed record FileTypeGroup(string Id, string Label, string Name, IReadOnlyList<string> Extensions, IReadOnlyList<string> MimeTypes)
{
    /// <summary>The Windows icon, <c>icons\{Id}.ico</c> next to the program.</summary>
    public string IcoFile => Id + ".ico";

    /// <summary>The icon file in <c>Contents/Resources</c> of the macOS bundle without the extension.</summary>
    public string MacIconName => MacBundle.IconName + "-" + Id;

    /// <summary>The icon name in the hicolor theme.</summary>
    public string LinuxIconName => "textida-" + Id;
}

/// <summary>The groups of document types that have an own icon, and where each system keeps the icon files. The tool <c>tools/MakeIcon.cs</c> draws the files from <see cref="Groups"/>' ids and labels.</summary>
public static class FileTypeIcons
{
    /// <summary>The folder next to the Windows program that holds the .ico files.</summary>
    public const string WindowsFolder = "icons";

    /// <summary>Sizes of the PNG files the Linux package installs under <c>hicolor/{n}x{n}/mimetypes</c>; the SVG goes to <c>scalable</c>.</summary>
    public static readonly IReadOnlyList<int> LinuxSizes = [48, 256];

    public static readonly IReadOnlyList<FileTypeGroup> Groups =
    [
        new("md", "MD", "Markdown document", [".md"], ["text/markdown", "text/x-markdown"]),
        new("data", "JSON", "Data file", [".json", ".yml", ".yaml", ".xml", ".csv"], ["application/json", "application/yaml", "application/xml", "text/xml", "text/csv"]),
        new("text", "TXT", "Text document", [".txt"], ["text/plain"]),
        new("log", "LOG", "Log file", [".log"], ["text/x-log"]),
        new("web", "WEB", "Web file", [".html", ".css", ".js", ".ts"], ["text/html", "text/css", "text/javascript", "application/javascript"]),
        new("code", "CODE", "Source code", [".cs", ".php", ".py", ".sh", ".bat"], ["text/x-csharp", "text/x-python", "application/x-php", "application/x-shellscript"]),
        new("sql", "SQL", "SQL script", [".sql"], ["application/sql", "text/x-sql"]),
        new("config", "INI", "Configuration file", [".ini", ".cfg"], ["text/x-ini"]),
    ];

    /// <summary>The group of <paramref name="extension"/> (with the dot, any case), or null; such an extension keeps the program id <c>Textida.File</c> and the icon of the program.</summary>
    public static FileTypeGroup? GroupOf(string extension) =>
        Groups.FirstOrDefault(g => g.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));

    /// <summary>The program id of a group on Windows, e.g. <c>Textida.File.md</c>.</summary>
    public static string ProgIdOf(FileTypeGroup group, string baseProgId) => baseProgId + "." + group.Id;

    /// <summary>The files of the Linux package, relative to <c>share</c>: the icons that are installed from <paramref name="assetFolder"/> (<c>{id}-{n}.png</c> and <c>{id}.svg</c>), keyed by their target.</summary>
    public static IReadOnlyDictionary<string, string> LinuxIconFiles(string assetFolder)
    {
        var files = new Dictionary<string, string>();
        foreach (var g in Groups)
        {
            foreach (var n in LinuxSizes) files[$"icons/hicolor/{n}x{n}/mimetypes/{g.LinuxIconName}.png"] = Path.Combine(assetFolder, $"{g.Id}-{n}.png");
            files[$"icons/hicolor/scalable/mimetypes/{g.LinuxIconName}.svg"] = Path.Combine(assetFolder, g.Id + ".svg");
        }
        return files;
    }

    /// <summary>The shared-mime-info file <c>mime/packages/textida.xml</c>: it adds the icon to the types of each group and defines no type or glob of its own, so no other program loses a file type.</summary>
    public static string LinuxMimeXml()
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\">\n");
        foreach (var g in Groups)
            foreach (var mime in g.MimeTypes)
                xml.Append("  <mime-type type=\"").Append(SecurityElement.Escape(mime)).Append("\">\n    <icon name=\"").Append(g.LinuxIconName).Append("\"/>\n  </mime-type>\n");
        return xml.Append("</mime-info>\n").ToString();
    }
}
