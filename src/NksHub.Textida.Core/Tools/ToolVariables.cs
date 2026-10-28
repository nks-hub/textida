using System.Text;

namespace NksHub.Textida.Core.Tools;

/// <summary>A tool cannot be started: an unknown variable, an executable that is missing or refused, a directory that does not exist.</summary>
public sealed class ToolException(string message) : Exception(message)
{
    /// <summary>The program (or the path the user gave for it) does not exist, as opposed to a program that failed.</summary>
    public bool Missing { get; init; }
}

/// <summary>What a declared variable may hold: a <c>Text</c> is one line, a <c>Path</c> is one line without the characters no path can have.</summary>
public enum ToolVariableType { Text, Path }

/// <summary>
/// A variable a tool definition declares. <paramref name="Value"/> may itself use variables; without it the value comes from the lookup (user or built-in variable
/// of that name). The value must fit <paramref name="Type"/>, or the tool does not start.
/// </summary>
public sealed record ToolVariableDecl(string Name, ToolVariableType Type, string? Value = null);

/// <summary>
/// Variables of a tool definition. <c>${Name}</c> is replaced by the value; <c>$$</c> is a literal dollar sign. The result of one argument stays one
/// argument whatever the value holds (spaces, quotes), because arguments are never joined into a command line that a shell would split again.
/// </summary>
public static class ToolVariables
{
    /// <summary>Names only tools have; the names shared with clips and recipes (P4.1.3) come in through the <c>shared</c> lookup of <see cref="ForDocument"/>.</summary>
    public static readonly IReadOnlyList<string> BuiltIn = ["File", "FileDir", "FileName", "FileBase", "FileExt", "Line", "Column", "Selection", "Word", "ProjectRoot"];

    /// <summary>
    /// The built-in variables for a document; those that do not apply (no file yet, no selection) are left out, so using them is an error before anything runs.
    /// <paramref name="shared"/> gives the variables clips and recipes use as well (the user's own, then <c>file.path</c>, <c>date</c> and the rest);
    /// it is asked first, so a user variable named like a tool variable wins and a name means the same in a clip, a recipe and a tool.
    /// <paramref name="selection"/> and <paramref name="word"/> (the selected text and the word under the caret) are asked for only when a tool uses them.
    /// <c>ProjectRoot</c> is the nearest folder at or above the file that has <c>.textida</c> or <c>.git</c>.
    /// </summary>
    public static Func<string, string?> ForDocument(string? path, long line = 1, long column = 1, Func<string, string?>? shared = null,
        Func<string?>? selection = null, Func<string?>? word = null)
    {
        Lazy<string?> root = new(() => path is null ? null : FindProjectRoot(Path.GetDirectoryName(Path.GetFullPath(path))));
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Line"] = line.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Column"] = column.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (path is not null)
        {
            string full = Path.GetFullPath(path);
            values["File"] = full;
            values["FileDir"] = Path.GetDirectoryName(full) ?? "";
            values["FileName"] = Path.GetFileName(full);
            values["FileBase"] = Path.GetFileNameWithoutExtension(full);
            values["FileExt"] = Path.GetExtension(full);
        }
        return name => shared?.Invoke(name) ?? name switch
        {
            "Selection" => selection?.Invoke(),
            "Word" => word?.Invoke(),
            "ProjectRoot" => root.Value,
            _ => values.GetValueOrDefault(name),
        };
    }

    /// <summary>A folder that marks the root of a project: it has a <c>.textida</c> folder or a <c>.git</c> folder or file.</summary>
    public static bool IsProjectRoot(string directory) =>
        Directory.Exists(Path.Combine(directory, ".textida")) || Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git"));

    /// <summary>The nearest folder at or above <paramref name="fromDirectory"/> for which <see cref="IsProjectRoot"/> is true, or null.</summary>
    public static string? FindProjectRoot(string? fromDirectory)
    {
        try
        {
            for (var dir = fromDirectory is null ? null : new DirectoryInfo(Path.GetFullPath(fromDirectory)); dir is not null; dir = dir.Parent)
                if (IsProjectRoot(dir.FullName)) return dir.FullName;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* no project root */ }
        return null;
    }

    /// <summary>Why <paramref name="value"/> is not a <paramref name="type"/>, or null when it is.</summary>
    public static string? Check(ToolVariableType type, string value)
    {
        if (value.IndexOfAny(['\0', '\n', '\r']) >= 0) return "it has a line break";
        if (type == ToolVariableType.Text) return null;
        if (value.Trim().Length == 0) return "it is empty";
        foreach (char c in value)
        {
            if (char.IsControl(c)) return "it has a control character";
            if (c is '*' or '?' or '"' or '<' or '>' or '|') return $"it has the character '{c}'";
        }
        return null;
    }

    /// <summary>
    /// Adds the variables a definition declares on top of <paramref name="lookup"/> and checks each value against its type. Throws <see cref="ToolException"/>
    /// naming the variable, so a wrong value is refused before anything starts. A declared variable with no value and none in the lookup stays unavailable.
    /// </summary>
    public static Func<string, string?> Declare(IReadOnlyList<ToolVariableDecl>? declared, Func<string, string?> lookup)
    {
        if (declared is not { Count: > 0 }) return lookup;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var d in declared)
        {
            if (d.Name.Length == 0 || !char.IsAsciiLetter(d.Name[0]) || !d.Name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                throw new ToolException($"'{d.Name}' is not a valid variable name.");
            if (values.ContainsKey(d.Name)) throw new ToolException($"The variable '${{{d.Name}}}' is declared twice.");
            string? value = d.Value is { } text ? Expand(text, lookup) : lookup(d.Name);
            if (value is null) continue;
            if (Check(d.Type, value) is { } why)
                throw new ToolException($"The variable '${{{d.Name}}}' must be a {d.Type.ToString().ToLowerInvariant()}, but {why}.");
            values[d.Name] = value;
        }
        return name => values.TryGetValue(name, out var v) ? v : lookup(name);
    }

    /// <summary>Replaces the variables of <paramref name="template"/>; throws <see cref="ToolException"/> for an unknown or unavailable one and for a <c>${</c> that is never closed.</summary>
    public static string Expand(string template, Func<string, string?> lookup)
    {
        if (template.IndexOf('$') < 0) return template;
        var sb = new StringBuilder(template.Length + 16);
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c != '$' || i + 1 >= template.Length) { sb.Append(c); continue; }
            if (template[i + 1] == '$') { sb.Append('$'); i++; continue; }
            if (template[i + 1] != '{') { sb.Append(c); continue; }
            int close = template.IndexOf('}', i + 2);
            if (close < 0) throw new ToolException($"Variable not closed in '{template}'.");
            string name = template[(i + 2)..close].Trim(); // ${ name } as in clips
            sb.Append(lookup(name) ?? throw new ToolException($"Unknown or unavailable variable '${{{name}}}'."));
            i = close;
        }
        return sb.ToString();
    }
}
