using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Workbench.Clips;

/// <summary>Facts about the place a variable is used in: the file, the selection, the clock and the user's own variables.</summary>
public sealed class VariableContext
{
    /// <summary>Full path of the document; null for a document that is not saved.</summary>
    public string? FilePath { get; init; }

    public string Selection { get; init; } = "";

    public DateTime Now { get; init; } = DateTime.Now;

    /// <summary>.NET custom format of <c>datetime</c>; the app passes the setting Date and time format.</summary>
    public string DateTimeFormat { get; init; } = "yyyy-MM-dd HH:mm";

    /// <summary>Culture for the month and day names of <c>datetime</c>.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    public string UserName { get; init; } = Environment.UserName;

    /// <summary>The user's own variables (see <see cref="UserVariables"/>).</summary>
    public IReadOnlyDictionary<string, string> User { get; init; } = new Dictionary<string, string>();

    /// <summary>The same facts for another selection (each caret of a clip has its own <c>selection</c>).</summary>
    public VariableContext WithSelection(string selection) => new()
    {
        FilePath = FilePath, Selection = selection, Now = Now, DateTimeFormat = DateTimeFormat, Culture = Culture, UserName = UserName, User = User,
    };
}

/// <summary>
/// The one place a variable name gets its value, for clips, recipes and external tools alike. Order of precedence, strongest first:
/// 1. values the user typed into the form of the clip (<c>fields</c>); 2. the user's own variables; 3. the built-in variables.
/// A name has the same meaning everywhere; a value is computed once per scope, so <c>guid</c> used twice is the same GUID.
/// Built-in names: <c>file.path</c>, <c>file.dir</c>, <c>file.name</c> (with extension), <c>file.base</c>, <c>file.ext</c> (with the dot),
/// <c>date</c> (yyyy-MM-dd), <c>time</c> (HH:mm:ss), <c>datetime</c>, <c>guid</c>, <c>selection</c>, <c>user.name</c>.
/// </summary>
public sealed class VariableScope
{
    public static readonly IReadOnlyList<string> BuiltinNames =
        ["file.path", "file.dir", "file.name", "file.base", "file.ext", "date", "time", "datetime", "guid", "selection", "user.name"];

    private readonly VariableContext _context;
    private readonly IReadOnlyDictionary<string, string> _fields;
    private readonly Dictionary<string, string?> _builtin = new(StringComparer.Ordinal);

    public VariableScope(VariableContext context, IReadOnlyDictionary<string, string>? fields = null)
    {
        _context = context;
        _fields = fields ?? new Dictionary<string, string>();
    }

    /// <summary>The value of <paramref name="name"/> and where it came from, or false when no layer has one (an unsaved document has no <c>file.*</c>).</summary>
    public bool TryGet(string name, out string value, out VariableSource source)
    {
        if (_fields.TryGetValue(name, out var typed)) { value = typed; source = VariableSource.Field; return true; }
        if (_context.User.TryGetValue(name, out var own)) { value = own; source = VariableSource.User; return true; }
        source = VariableSource.Builtin;
        if (!_builtin.TryGetValue(name, out var computed)) _builtin[name] = computed = Compute(name);
        value = computed ?? "";
        return computed is not null;
    }

    public bool TryGet(string name, out string value) => TryGet(name, out value, out _);

    private string? Compute(string name)
    {
        var path = _context.FilePath;
        return name switch
        {
            "file.path" => path,
            "file.dir" => path is null ? null : Path.GetDirectoryName(path) ?? "",
            "file.name" => path is null ? null : Path.GetFileName(path),
            "file.base" => path is null ? null : Path.GetFileNameWithoutExtension(path),
            "file.ext" => path is null ? null : Path.GetExtension(path),
            "date" => _context.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "time" => _context.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            "datetime" => FormatDateTime(),
            "guid" => Guid.NewGuid().ToString(),
            "selection" => _context.Selection,
            "user.name" => _context.UserName,
            _ => null,
        };
    }

    private string? FormatDateTime()
    {
        try { return _context.Now.ToString(_context.DateTimeFormat, _context.Culture); }
        catch (FormatException) { return null; }
    }
}

public enum VariableSource { Field, User, Builtin }

/// <param name="Text">The expanded text with the line breaks the caller asked for; caret marks are removed.</param>
/// <param name="Carets">UTF-16 offsets in <paramref name="Text"/> of the caret marks, in the order they stand.</param>
/// <param name="Missing">Names with no value in the scope.</param>
/// <param name="SyntaxError">What is wrong with the template itself (an unclosed or empty <c>${</c>), or null.</param>
public sealed record ExpandResult(string Text, IReadOnlyList<int> Carets, IReadOnlyList<string> Missing, string? SyntaxError)
{
    public bool Ok => Missing.Count == 0 && SyntaxError is null;
}

/// <summary>
/// The template language of clips and file templates, also used for the arguments of tools. <c>${name}</c> is a variable, <c>$|</c> marks where a caret
/// goes (as often as needed), <c>$$</c> is a dollar sign; any other <c>$</c> stays as it is.
/// </summary>
public static class Variables
{
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64 || !char.IsAsciiLetter(name[0])) return false;
        foreach (var c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '.') return false;
        return name[^1] != '.' && !name.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>Expands <paramref name="template"/>; every line break in the template and in the values becomes <paramref name="newLine"/>.</summary>
    public static ExpandResult Expand(string template, VariableScope scope, string newLine = "\n")
    {
        var sb = new StringBuilder(template.Length + 16);
        var carets = new List<int>();
        var missing = new List<string>();
        string? error = null;

        void Append(ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
                if (s[i] is '\r' or '\n') sb.Append(newLine); else sb.Append(s[i]);
            }
        }

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < template.Length && template[i + 1] == '\n') i++;
                sb.Append(newLine);
                continue;
            }
            if (c != '$' || i + 1 >= template.Length) { sb.Append(c); continue; }
            char next = template[i + 1];
            if (next == '$') { sb.Append('$'); i++; }
            else if (next == '|') { carets.Add(sb.Length); i++; }
            else if (next == '{')
            {
                int close = template.IndexOf('}', i + 2);
                if (close < 0) { error ??= "unclosed ${"; Append(template.AsSpan(i)); break; }
                var name = template.Substring(i + 2, close - i - 2).Trim();
                if (!IsValidName(name)) error ??= $"bad variable name '{name}'";
                else if (scope.TryGet(name, out var value)) Append(value);
                else if (!missing.Contains(name)) missing.Add(name);
                i = close;
            }
            else sb.Append('$');
        }
        return new ExpandResult(sb.ToString(), carets, missing, error);
    }

    /// <summary>Checks the syntax of a template without values; null when it is fine.</summary>
    public static string? CheckSyntax(string template) =>
        Expand(template, new VariableScope(new VariableContext(), new AnyName())).SyntaxError;

    /// <summary>Escapes literal text so that <see cref="Expand"/> gives it back unchanged.</summary>
    public static string Escape(string text) => text.Replace("$", "$$", StringComparison.Ordinal);

    /// <summary>The names a template uses (without duplicates, in order of appearance), for checks without values.</summary>
    public static IReadOnlyList<string> NamesIn(string template)
    {
        var names = new List<string>();
        for (int i = 0; i + 1 < template.Length; i++)
        {
            if (template[i] != '$') continue;
            if (template[i + 1] == '$' || template[i + 1] == '|') { i++; continue; }
            if (template[i + 1] != '{') continue;
            int close = template.IndexOf('}', i + 2);
            if (close < 0) break;
            var name = template.Substring(i + 2, close - i - 2).Trim();
            if (IsValidName(name) && !names.Contains(name)) names.Add(name);
            i = close;
        }
        return names;
    }

    /// <summary>A dictionary that has every name; syntax checks use it so they never report a missing value.</summary>
    private sealed class AnyName : IReadOnlyDictionary<string, string>
    {
        public string this[string key] => "";
        public IEnumerable<string> Keys => [];
        public IEnumerable<string> Values => [];
        public int Count => 0;
        public bool ContainsKey(string key) => true;
        public bool TryGetValue(string key, out string value) { value = ""; return true; }
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>
/// The user's own variables: one JSON object of name and text in the profile folder (<c>variables.json</c>). A damaged file gives no variables and a reason.
/// </summary>
public static class UserVariables
{
    public const string FileName = "variables.json";

    public static (IReadOnlyDictionary<string, string> Values, string? Problem) Load(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return (values, null);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (new Dictionary<string, string>(), "the file must hold one JSON object");
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!Variables.IsValidName(p.Name)) return (new Dictionary<string, string>(), $"'{p.Name}' is not a valid variable name");
                if (p.Value.ValueKind != JsonValueKind.String) return (new Dictionary<string, string>(), $"'{p.Name}' must be a text");
                values[p.Name] = p.Value.GetString()!;
            }
            return (values, null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return (new Dictionary<string, string>(), e.Message);
        }
    }
}
