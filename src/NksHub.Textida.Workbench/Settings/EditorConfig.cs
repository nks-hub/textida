using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Workbench.Settings;

/// <summary>One <c>[glob]</c> section of an EditorConfig file with its properties in file order. Keys are lowercase.</summary>
public sealed record EditorConfigSection(string Glob, IReadOnlyList<KeyValuePair<string, string>> Properties);

/// <summary>
/// A parsed <c>.editorconfig</c> file (EditorConfig specification 0.17): a UTF-8 byte order mark is ignored, blank lines and lines starting
/// with <c>#</c> or <c>;</c> are skipped (a comment never starts in the middle of a line), whitespace around names, values and the
/// section header is trimmed, property names are lowercased, and so are the values of the properties the specification defines.
/// <c>root = true</c> is read only before the first section. A line that is neither a section nor <c>name = value</c> is ignored.
/// </summary>
public sealed class EditorConfigFile
{
    /// <summary>Properties whose values are case insensitive and therefore lowercased.</summary>
    public static IReadOnlySet<string> KnownProperties { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "root", "indent_style", "indent_size", "tab_width", "end_of_line", "charset", "trim_trailing_whitespace", "insert_final_newline", "max_line_length",
    };

    private EditorConfigFile(bool isRoot, IReadOnlyList<EditorConfigSection> sections)
    {
        IsRoot = isRoot;
        Sections = sections;
    }

    /// <summary>True when the preamble says <c>root = true</c>: files further up are not read.</summary>
    public bool IsRoot { get; }
    public IReadOnlyList<EditorConfigSection> Sections { get; }

    public static EditorConfigFile Parse(string text)
    {
        if (text.StartsWith('﻿')) text = text[1..];
        bool root = false;
        string? glob = null;
        var current = new List<KeyValuePair<string, string>>();
        var sections = new List<EditorConfigSection>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                if (glob is not null) sections.Add(new EditorConfigSection(glob, current));
                glob = line[1..^1];
                current = [];
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim().ToLowerInvariant(), value = line[(eq + 1)..].Trim();
            if (KnownProperties.Contains(key)) value = value.ToLowerInvariant();
            if (glob is null)
            {
                if (key == "root") root = value == "true";
            }
            else current.Add(new(key, value));
        }
        if (glob is not null) sections.Add(new EditorConfigSection(glob, current));
        return new EditorConfigFile(root, sections);
    }
}

/// <summary>
/// EditorConfig glob matching. <c>*</c> matches within one path segment, <c>**</c> across segments, <c>?</c> one character other than
/// <c>/</c>, <c>[abc]</c>, <c>[a-z]</c> and <c>[!abc]</c> a character (a bracket that contains a <c>/</c> is literal),
/// <c>{a,b}</c> alternatives (may be empty and nested) and <c>{1..9}</c> an integer range. Braces are literal when they do not pair up and
/// around a single word, and <c>\</c> makes the next character literal. A glob without <c>/</c> matches at any depth below the directory
/// of the file; with a <c>/</c> (a leading one is ignored) it is relative to that directory. Matching is case sensitive.
/// </summary>
public static class EditorConfigGlob
{
    private sealed record Compiled(Regex Regex, IReadOnlyList<(long Min, long Max)> Ranges);

    private static readonly Regex NumericRange = new(@"^([+-]?(?:0|[1-9]\d*))\.\.([+-]?(?:0|[1-9]\d*))$", RegexOptions.CultureInvariant);

    /// <summary>True when <paramref name="path"/> (full, with '/' separators) is matched by <paramref name="glob"/> of the file in <paramref name="directory"/>.</summary>
    public static bool IsMatch(string glob, string directory, string path)
    {
        var compiled = Cache.GetOrAdd((glob, directory), key => Compile(key.Glob, key.Directory));
        if (compiled is null) return false;
        var m = compiled.Regex.Match(path);
        if (!m.Success) return false;
        for (int i = 0; i < compiled.Ranges.Count; i++)
        {
            if (!long.TryParse(m.Groups["n" + i].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)) return false;
            if (n < Math.Min(compiled.Ranges[i].Min, compiled.Ranges[i].Max) || n > Math.Max(compiled.Ranges[i].Min, compiled.Ranges[i].Max)) return false;
        }
        return true;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Glob, string Directory), Compiled?> Cache = new();

    private static Compiled? Compile(string glob, string directory)
    {
        string full = glob.Contains('/') ? glob.TrimStart('/') : "**/" + glob;
        var ranges = new List<(long, long)>();
        string dir = Regex.Escape(directory.TrimEnd('/'));
        // "dir/**/name" matches name directly in dir as well as at any depth below
        string pattern = full.StartsWith("**/", StringComparison.Ordinal)
            ? "^" + dir + "(?:/|/.*/)" + Translate(full[3..], ranges, nested: false) + "$"
            : "^" + dir + "/" + Translate(full, ranges, nested: false) + "$";
        try
        {
            return new Compiled(new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline), [.. ranges.Select(r => (r.Item1, r.Item2))]);
        }
        catch (ArgumentException)
        {
            return null; // a glob that cannot be translated matches nothing
        }
    }

    private static string Translate(string p, List<(long, long)> ranges, bool nested)
    {
        var sb = new StringBuilder();
        int level = 0;
        bool braces = CountUnescaped(p, '{') == CountUnescaped(p, '}');
        for (int i = 0; i < p.Length; i++)
        {
            char c = p[i];
            switch (c)
            {
                case '\\' when i + 1 < p.Length:
                    sb.Append(Regex.Escape(p[++i].ToString()));
                    break;
                case '*':
                    if (i + 1 < p.Length && p[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i++;
                    }
                    else sb.Append("[^/]*");
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                case '[':
                    i = Bracket(p, i, sb);
                    break;
                case '{':
                    {
                        int close = FindSimpleClose(p, i + 1);
                        if (close >= 0)
                        {
                            string inner = p[(i + 1)..close];
                            var range = NumericRange.Match(inner);
                            if (range.Success)
                            {
                                sb.Append($"(?<n{ranges.Count}>[+-]?(?:0|[1-9]\\d*))");
                                ranges.Add((long.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture), long.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture)));
                            }
                            else sb.Append("\\{").Append(Translate(inner, ranges, nested: true)).Append("\\}");
                            i = close;
                        }
                        else if (braces)
                        {
                            sb.Append("(?:");
                            level++;
                        }
                        else sb.Append("\\{");
                        break;
                    }
                case ',':
                    sb.Append(level > 0 ? "|" : "\\,");
                    break;
                case '}':
                    if (level > 0)
                    {
                        sb.Append(')');
                        level--;
                    }
                    else sb.Append("\\}");
                    break;
                case '/':
                    if (string.CompareOrdinal(p, i + 1, "**/", 0, 3) == 0)
                    {
                        sb.Append("(?:/|/.*/)");
                        i += 3;
                    }
                    else sb.Append('/');
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        while (level-- > 0) sb.Append(')');
        return sb.ToString();
    }

    /// <summary>End of a brace group without a comma (a single word, empty, or a number range) that starts at <paramref name="from"/>; -1 when there is a comma or no closing brace.</summary>
    private static int FindSimpleClose(string p, int from)
    {
        for (int i = from; i < p.Length; i++)
        {
            if (p[i] == '\\') i++;
            else if (p[i] == ',') return -1;
            else if (p[i] == '}') return i;
        }
        return -1;
    }

    private static int CountUnescaped(string p, char c)
    {
        int n = 0;
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i] == '\\') i++;
            else if (p[i] == c) n++;
        }
        return n;
    }

    /// <summary>Translates a bracket expression starting at <paramref name="open"/>; returns the index of its last character. A '[' without a closing ']' or with a '/' inside stays a literal.</summary>
    private static int Bracket(string p, int open, StringBuilder sb)
    {
        int j = open + 1;
        bool negate = j < p.Length && p[j] is '!' or '^';
        if (negate) j++;
        int start = j;
        for (; j < p.Length; j++)
        {
            if (p[j] == '\\') j++;
            else if (p[j] is '/' or ']') break;
        }
        if (j >= p.Length || p[j] != ']' || j == start)
        {
            sb.Append("\\[");
            return open;
        }
        sb.Append(negate ? "[^/" : "[");
        for (int k = start; k < j; k++)
        {
            char c = p[k];
            if (c == '\\' && k + 1 < j) c = p[++k];
            else if (c == '-' && k > start && k < j - 1)
            {
                sb.Append('-');
                continue;
            }
            if (c is '\\' or ']' or '[' or '^' or '-') sb.Append('\\');
            sb.Append(c);
        }
        sb.Append(']');
        return j;
    }
}

/// <summary>Reads the <c>.editorconfig</c> file of a directory; abstracts the disk so the resolution can be tested.</summary>
public interface IEditorConfigFiles
{
    /// <summary>Text of the <c>.editorconfig</c> in <paramref name="directory"/> (full path, '/' separators), or null when there is none.</summary>
    string? Read(string directory);
}

/// <summary>Reads <c>.editorconfig</c> files from disk.</summary>
public sealed class DiskEditorConfigFiles : IEditorConfigFiles
{
    public string? Read(string directory)
    {
        try
        {
            string path = Path.Combine(directory, ".editorconfig");
            return File.Exists(path) ? File.ReadAllText(path, new UTF8Encoding(false)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>The properties that apply to one file and where each one comes from.</summary>
/// <param name="Properties">Final values (lowercase names), after <c>unset</c> and the indent/tab defaults of the specification.</param>
/// <param name="Origins">For every property the <c>.editorconfig</c> file whose section set it last (derived defaults name the file that caused them).</param>
/// <param name="Files">The files that were read, nearest first; empty when the file is not under any <c>.editorconfig</c>.</param>
public sealed record EditorConfigResult(IReadOnlyDictionary<string, string> Properties, IReadOnlyDictionary<string, string> Origins, IReadOnlyList<string> Files)
{
    public string? Get(string name) => Properties.GetValueOrDefault(name);
    public static EditorConfigResult Empty { get; } = new(new Dictionary<string, string>(), new Dictionary<string, string>(), []);
}

public static class EditorConfigResolver
{
    /// <summary>
    /// Reads the <c>.editorconfig</c> files from the directory of <paramref name="filePath"/> upwards until one says <c>root = true</c> and applies
    /// them from the topmost down: a nearer file and a later section override an earlier one; the value <c>unset</c> removes a property.
    /// </summary>
    public static EditorConfigResult Resolve(string filePath, IEditorConfigFiles files)
    {
        string path = filePath.Replace('\\', '/');
        var chain = new List<(string Dir, EditorConfigFile File)>();
        for (string dir = ParentOf(path); ; dir = ParentOf(dir))
        {
            bool drive = dir.Length == 2 && dir[1] == ':';
            if (files.Read(dir.Length == 0 ? "/" : drive ? dir + "/" : dir) is { } text)
            {
                var file = EditorConfigFile.Parse(text);
                chain.Add((dir, file));
                if (file.IsRoot) break;
            }
            if (dir.Length == 0 || drive) break;
        }

        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var (dir, file) = chain[i];
            string origin = (dir.Length == 0 ? "" : dir) + "/.editorconfig";
            foreach (var section in file.Sections)
            {
                if (!EditorConfigGlob.IsMatch(section.Glob, dir, path)) continue;
                foreach (var (key, value) in section.Properties)
                {
                    if (value == "unset")
                    {
                        props.Remove(key);
                        origins.Remove(key);
                    }
                    else
                    {
                        props[key] = value;
                        origins[key] = origin;
                    }
                }
            }
        }
        Defaults(props, origins);
        return new EditorConfigResult(props, origins, [.. chain.Select(c => (c.Dir.Length == 0 ? "" : c.Dir) + "/.editorconfig")]);
    }

    private static string ParentOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash <= 0 ? "" : path[..slash];
    }

    /// <summary>Spec: tab style without a size means indent_size = tab; a numeric indent_size gives tab_width; indent_size = tab takes tab_width.</summary>
    private static void Defaults(Dictionary<string, string> props, Dictionary<string, string> origins)
    {
        if (props.GetValueOrDefault("indent_style") == "tab" && !props.ContainsKey("indent_size"))
        {
            props["indent_size"] = "tab";
            origins["indent_size"] = origins["indent_style"];
        }
        if (props.GetValueOrDefault("indent_size") == "tab" && props.TryGetValue("tab_width", out var width))
        {
            props["indent_size"] = width;
            origins["indent_size"] = origins["tab_width"];
        }
        if (!props.ContainsKey("tab_width") && props.TryGetValue("indent_size", out var size) && size != "tab")
        {
            props["tab_width"] = size;
            origins["tab_width"] = origins["indent_size"];
        }
    }
}

/// <summary>
/// Maps EditorConfig properties to settings and plugs them into the <see cref="SettingSource.EditorConfig"/> slot of a <see cref="SettingsStack"/>,
/// so the settings page and the status bar show the <c>.editorconfig</c> file as the source of a value
/// (<see cref="ResolvedSetting.LayerName"/> is the nearest file that contributed).
/// <list type="bullet">
/// <item><c>indent_style</c> to <c>editor.insertSpaces</c>, <c>indent_size</c> to <c>editor.indentSize</c> (<c>tab</c> only when <c>tab_width</c> gives a number), <c>tab_width</c> to <c>editor.tabSize</c></item>
/// <item><c>trim_trailing_whitespace</c> to <c>files.trimTrailingWhitespace</c> and <c>insert_final_newline</c> to <c>files.insertFinalNewline</c></item>
/// <item><c>end_of_line</c> to <c>files.eol</c> (<c>lf</c> and <c>crlf</c>; the setting offers no CR-only choice) and <c>charset</c> to <c>files.encoding</c> (<c>latin1</c> is <c>windows-1252</c>)</item>
/// </list>
/// Values outside the setting's range are left out (the stack would skip them anyway). The settings are the editor's configuration;
/// whether a file's line endings or encoding change is decided by <see cref="NewFileDefaults"/> (new files) and explicit actions, never by saving.
/// </summary>
public static class EditorConfigSettings
{
    public static JsonObject ToLayer(EditorConfigResult result)
    {
        var layer = new JsonObject();
        var p = result.Properties;
        if (p.GetValueOrDefault("indent_style") is { } style && style is "tab" or "space") layer["editor.insertSpaces"] = style == "space";
        if (Number(p.GetValueOrDefault("indent_size")) is { } size) layer["editor.indentSize"] = size;
        if (Number(p.GetValueOrDefault("tab_width")) is { } width) layer["editor.tabSize"] = width;
        if (Bool(p.GetValueOrDefault("trim_trailing_whitespace")) is { } trim) layer["files.trimTrailingWhitespace"] = trim;
        if (Bool(p.GetValueOrDefault("insert_final_newline")) is { } newline) layer["files.insertFinalNewline"] = newline;
        if (p.GetValueOrDefault("end_of_line") is "lf" or "crlf") layer["files.eol"] = p["end_of_line"];
        if (p.GetValueOrDefault("charset") is { } charset && Charsets.TryGetValue(charset, out var name)) layer["files.encoding"] = name;
        return layer;
    }

    private static readonly Dictionary<string, string> Charsets = new(StringComparer.Ordinal)
    {
        ["utf-8"] = "utf-8", ["utf-8-bom"] = "utf-8-bom", ["utf-16le"] = "utf-16le", ["utf-16be"] = "utf-16be", ["latin1"] = "windows-1252",
    };

    private static long? Number(string? value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long n) ? n : null;
    private static bool? Bool(string? value) => value switch { "true" => true, "false" => false, _ => null };

    /// <summary>Puts the properties of a file into the EditorConfig slot, replacing what the previous document left there.</summary>
    public static void Apply(SettingsStack stack, EditorConfigResult result) =>
        stack.SetLayer(SettingSource.EditorConfig, result.Files.Count == 0 ? null : ToLayer(result), name: result.Files.Count == 0 ? null : result.Files[0]);

    /// <summary>
    /// Line ending and encoding EditorConfig asks for a file that is created now. Existing files keep theirs: saving never converts; changing them is an
    /// explicit command (<c>Eol.PreviewNormalize</c>, "Convert to encoding"), which may take these values as its suggestion.
    /// </summary>
    public static (string? EndOfLine, string? Encoding) NewFileDefaults(EditorConfigResult result)
    {
        string? eol = result.Get("end_of_line") is "lf" or "crlf" or "cr" ? result.Get("end_of_line") : null;
        return (eol, ToLayer(result)["files.encoding"]?.GetValue<string>());
    }
}
