using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace NksHub.Textida.Core.Files;

/// <summary>The sort of an entry of the file tree: one of a small set the panel has an icon and a colour for.</summary>
public enum FileKind { Folder, File, Text, Code, Markup, Data, Style, Image, Archive, Binary }

/// <summary>Tells which <see cref="FileKind"/> a name belongs to, by extension and, for files without one, by well-known name. Nothing is read from the disk.</summary>
public static class FileKinds
{
    private static readonly FrozenDictionary<string, FileKind> ByExtension = Build(
        (FileKind.Code, "cs csx vb fs fsx fsi c h cc cpp cxx hpp hh hxx m mm java kt kts scala groovy gradle js mjs cjs jsx ts tsx py pyw rb php pl pm go rs swift dart lua r jl sh bash zsh fish ps1 psm1 psd1 bat cmd sql vue svelte asm s hs ml ex exs erl clj cljs coffee"),
        (FileKind.Markup, "html htm xhtml xml xaml axaml xsl xslt xsd dtd csproj vbproj fsproj props targets resx cshtml razor vbhtml plist wsdl rss atom"),
        (FileKind.Style, "css scss sass less styl"),
        (FileKind.Data, "json jsonc json5 jsonl ndjson yaml yml toml ini cfg conf config env properties csv tsv lock editorconfig gitignore gitattributes npmrc"),
        (FileKind.Text, "txt md markdown rst adoc tex log nfo diff patch"),
        (FileKind.Image, "png jpg jpeg gif bmp ico icns webp tif tiff svg heic avif psd"),
        (FileKind.Archive, "zip 7z rar tar gz tgz bz2 xz zst jar war nupkg whl cab iso"),
        (FileKind.Binary, "exe dll so dylib bin dat o obj lib a pdb class pyc wasm sys ttf otf woff woff2 pdf doc docx xls xlsx ppt pptx odt ods mp3 mp4 mkv avi wav ogg flac mov db sqlite sqlite3 msi"));

    private static readonly FrozenDictionary<string, FileKind> ByName = Build(
        (FileKind.Text, "readme license licence copying notice authors changelog todo"),
        (FileKind.Code, "dockerfile makefile rakefile gemfile jenkinsfile vagrantfile"));

    private static FrozenDictionary<string, FileKind> Build(params (FileKind Kind, string Words)[] groups) =>
        groups.SelectMany(g => g.Words.Split(' ').Select(w => KeyValuePair.Create(w, g.Kind))).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static FileKind Of(string name, bool isDirectory)
    {
        if (isDirectory) return FileKind.Folder;
        int dot = name.LastIndexOf('.');
        // ".gitignore" has the extension "gitignore"; a name that ends with a dot has none
        if (dot >= 0 && dot < name.Length - 1 && ByExtension.TryGetValue(name[(dot + 1)..], out var kind)) return kind;
        return ByName.TryGetValue(name, out kind) ? kind : FileKind.File;
    }
}

/// <summary>How a name is written in the file tree.</summary>
public static class FileNames
{
    /// <summary>
    /// The name as the tree shows it. A code point no font draws (a private-use character, an unassigned one, a control character, a lone surrogate) is written as its number,
    /// <c>‹U+F03A›</c>, so a name made by a tool that mapped a character such as a colon into the private-use area reads as text and not as a box.
    /// </summary>
    public static string Display(string name)
    {
        if (!name.AsSpan().ContainsAnyExceptInRange((char)0x20, (char)0x7E)) return name; // printable ASCII, the common case
        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsHighSurrogate(name[i]) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
            {
                int cp = char.ConvertToUtf32(name[i], name[i + 1]);
                if (IsUndrawable(CharUnicodeInfo.GetUnicodeCategory(cp))) sb.Append("‹U+").Append(cp.ToString("X4", CultureInfo.InvariantCulture)).Append('›');
                else sb.Append(name[i]).Append(name[i + 1]);
                i++;
            }
            else if (IsUndrawable(CharUnicodeInfo.GetUnicodeCategory(name[i])) || char.IsSurrogate(name[i]))
                sb.Append("‹U+").Append(((int)name[i]).ToString("X4", CultureInfo.InvariantCulture)).Append('›');
            else sb.Append(name[i]);
        }
        return sb.ToString();
    }

    private static bool IsUndrawable(UnicodeCategory category) => category is UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Control;
}
