using System.Text;

namespace NksHub.Textida.Core.Tools;

/// <summary>A tool cannot be started: an unknown variable, an executable that is missing or refused, a directory that does not exist.</summary>
public sealed class ToolException(string message) : Exception(message);

/// <summary>
/// Variables of a tool definition. <c>${Name}</c> is replaced by the value; <c>$$</c> is a literal dollar sign. The result of one argument stays one
/// argument whatever the value holds (spaces, quotes), because arguments are never joined into a command line that a shell would split again.
/// </summary>
public static class ToolVariables
{
    /// <summary>Names the editor itself provides; user variables (P4.1.3) are added through the lookup of <see cref="Expand"/>.</summary>
    public static readonly IReadOnlyList<string> BuiltIn = ["File", "FileDir", "FileName", "FileBase", "FileExt", "Line", "Column"];

    /// <summary>The built-in variables for a document; those that do not apply (no file yet) are left out, so using them is an error before anything runs.</summary>
    public static Func<string, string?> ForDocument(string? path, long line = 1, long column = 1)
    {
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
        return name => values.GetValueOrDefault(name);
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
            string name = template[(i + 2)..close];
            sb.Append(lookup(name) ?? throw new ToolException($"Unknown or unavailable variable '${{{name}}}'."));
            i = close;
        }
        return sb.ToString();
    }
}
