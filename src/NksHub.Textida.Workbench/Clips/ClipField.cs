using System.Globalization;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Workbench.Clips;

public enum ClipFieldType { Text, Integer, Number, Color, Date, File, Choice }

/// <summary>Why a typed value is refused. The interface turns each into a sentence in the user's language.</summary>
public enum FieldProblem { None, Empty, NotInteger, NotNumber, NotColor, NotDate, NotChoice, NoFile }

/// <summary>An input of the form of a clip or a file template. The value goes into the text wherever <c>${name}</c> stands.</summary>
public sealed class ClipField
{
    public string Name { get; set; } = "";

    /// <summary>Text shown next to the input; the name when empty.</summary>
    public string? Label { get; set; }

    /// <summary><c>text</c>, <c>integer</c>, <c>number</c>, <c>color</c> (#RGB, #RGBA, #RRGGBB or #RRGGBBAA), <c>date</c> (yyyy-MM-dd), <c>file</c> (an existing file) or <c>choice</c>.</summary>
    public string Type { get; set; } = "text";

    /// <summary>Starting value; it may use variables (for example <c>${date}</c>).</summary>
    public string? Default { get; set; }

    /// <summary>The allowed values of a <c>choice</c>.</summary>
    public List<string> Choices { get; set; } = [];

    /// <summary>An empty value is refused unless this is false.</summary>
    public bool Required { get; set; } = true;
}

public static partial class ClipFields
{
    public static bool TryParseType(string? type, out ClipFieldType result)
    {
        result = type switch
        {
            "text" => ClipFieldType.Text,
            "integer" => ClipFieldType.Integer,
            "number" => ClipFieldType.Number,
            "color" => ClipFieldType.Color,
            "date" => ClipFieldType.Date,
            "file" => ClipFieldType.File,
            "choice" => ClipFieldType.Choice,
            _ => (ClipFieldType)(-1),
        };
        return (int)result >= 0;
    }

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();

    /// <summary>Problems with the definitions of a form (empty when fine), for the loader of plugins.</summary>
    public static IEnumerable<string> Check(IReadOnlyList<ClipField> fields)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in fields)
        {
            if (!Variables.IsValidName(f.Name)) yield return $"field name '{f.Name}' is not valid";
            else if (!seen.Add(f.Name)) yield return $"field '{f.Name}' is listed twice";
            if (!TryParseType(f.Type, out var type)) yield return $"field '{f.Name}' has the unknown type '{f.Type}'";
            else if (type == ClipFieldType.Choice && f.Choices.Count == 0) yield return $"choice field '{f.Name}' needs choices";
            else if (type != ClipFieldType.Choice && f.Choices.Count > 0) yield return $"field '{f.Name}' lists choices but is not a choice";
            if (f.Default is { } d && Variables.CheckSyntax(d) is { } why) yield return $"field '{f.Name}' default: {why}";
        }
    }

    /// <summary>Checks one typed value against the type of its field.</summary>
    public static FieldProblem Validate(ClipField field, string value)
    {
        if (value.Length == 0) return field.Required ? FieldProblem.Empty : FieldProblem.None;
        if (!TryParseType(field.Type, out var type)) return FieldProblem.None;
        return type switch
        {
            ClipFieldType.Integer => long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? FieldProblem.None : FieldProblem.NotInteger,
            ClipFieldType.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? FieldProblem.None : FieldProblem.NotNumber,
            ClipFieldType.Color => ColorPattern().IsMatch(value) ? FieldProblem.None : FieldProblem.NotColor,
            ClipFieldType.Date => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? FieldProblem.None : FieldProblem.NotDate,
            ClipFieldType.File => FileExists(value) ? FieldProblem.None : FieldProblem.NoFile,
            ClipFieldType.Choice => field.Choices.Contains(value, StringComparer.Ordinal) ? FieldProblem.None : FieldProblem.NotChoice,
            _ => FieldProblem.None,
        };
    }

    private static bool FileExists(string path)
    {
        try { return File.Exists(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>Every field that is refused, with the reason; empty when the form is valid. A field the values do not mention counts as empty.</summary>
    public static IReadOnlyDictionary<string, FieldProblem> Validate(IReadOnlyList<ClipField> fields, IReadOnlyDictionary<string, string> values)
    {
        var problems = new Dictionary<string, FieldProblem>(StringComparer.Ordinal);
        foreach (var f in fields)
            if (Validate(f, values.TryGetValue(f.Name, out var v) ? v : "") is var p and not FieldProblem.None) problems[f.Name] = p;
        return problems;
    }

    /// <summary>The values a form starts with: the default (variables expanded in the scope without form values), or the first choice.</summary>
    public static Dictionary<string, string> Defaults(IReadOnlyList<ClipField> fields, VariableScope scope)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in fields)
        {
            string value = f.Default is { } d ? Variables.Expand(d, scope).Text : TryParseType(f.Type, out var t) && t == ClipFieldType.Choice && f.Choices.Count > 0 ? f.Choices[0] : "";
            values[f.Name] = value;
        }
        return values;
    }
}
