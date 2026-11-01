using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Core.Data;

/// <summary>The language a JSON document is turned into types of.</summary>
public enum TypeLanguage { Go, CSharp, TypeScript }

/// <summary>
/// Turns a JSON document into the types that can read it: a Go struct, a C# class or a TypeScript interface. The type of a field is taken from every value the document has for it: the objects of an array are merged
/// into one type, a field some of them lack (or hold null in) is optional, a number is an integer when every value is one that fits in 64 bits and a floating point number otherwise, a field that is always null or
/// an empty array has no known type and gets the open one of the language. Nested objects become types of their own, named after the field.
/// </summary>
public static class JsonTypeGenerator
{
    private enum Kind { Unknown, Bool, Integer, Number, String, Object, Array }

    private sealed class Shape
    {
        public Kind Kind;
        public bool Nullable;
        public Dictionary<string, Shape> Fields = [];
        public List<string> Order = [];
        public Shape? Item;
        public int Seen;
    }

    /// <summary>Generates the types for <paramref name="json"/>; the root type is <paramref name="rootName"/>. Throws <see cref="JsonException"/> for text that is not JSON.</summary>
    public static string Generate(string json, TypeLanguage language, string rootName = "Root")
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var shape = new Shape();
        Merge(shape, doc.RootElement);
        var writer = new Writer(language);
        return writer.Write(shape, Pascal(rootName, "Root"));
    }

    private static void Merge(Shape shape, JsonElement value)
    {
        shape.Seen++;
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                shape.Nullable = true;
                break;
            case JsonValueKind.True or JsonValueKind.False:
                shape.Kind = Join(shape.Kind, Kind.Bool);
                break;
            case JsonValueKind.Number:
                shape.Kind = Join(shape.Kind, value.TryGetInt64(out _) ? Kind.Integer : Kind.Number);
                break;
            case JsonValueKind.String:
                shape.Kind = Join(shape.Kind, Kind.String);
                break;
            case JsonValueKind.Object:
                shape.Kind = Join(shape.Kind, Kind.Object);
                var present = new HashSet<string>();
                foreach (var property in value.EnumerateObject())
                {
                    if (!shape.Fields.TryGetValue(property.Name, out var field))
                    {
                        shape.Fields[property.Name] = field = new Shape();
                        shape.Order.Add(property.Name);
                        if (shape.Seen > 1) field.Nullable = true; // the objects before this one had no such field
                    }
                    present.Add(property.Name);
                    Merge(field, property.Value);
                }
                foreach (var name in shape.Order) if (!present.Contains(name)) shape.Fields[name].Nullable = true;
                break;
            case JsonValueKind.Array:
                shape.Kind = Join(shape.Kind, Kind.Array);
                shape.Item ??= new Shape();
                foreach (var item in value.EnumerateArray()) Merge(shape.Item, item);
                break;
        }
    }

    private static Kind Join(Kind a, Kind b)
    {
        if (a == Kind.Unknown || a == b) return b;
        if (a is Kind.Integer or Kind.Number && b is Kind.Integer or Kind.Number) return Kind.Number;
        return Kind.Unknown; // values of different kinds have no one type
    }

    private sealed class Writer(TypeLanguage language)
    {
        private readonly List<(string Name, Shape Shape)> _types = [];
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public string Write(Shape root, string rootName)
        {
            string top = TypeOf(root, rootName, optional: false);
            var sb = new StringBuilder();
            if (language == TypeLanguage.CSharp) sb.Append("using System.Text.Json.Serialization;\n\n");
            if (root.Kind != Kind.Object)
            {
                string alias = language switch { TypeLanguage.Go => $"type {rootName} {top}\n", TypeLanguage.CSharp => $"// the document is {top}\n", _ => $"export type {rootName} = {top};\n" };
                sb.Append(alias);
                if (_types.Count > 0) sb.Append('\n');
            }
            for (int i = 0; i < _types.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(Declare(_types[i].Name, _types[i].Shape));
            }
            return sb.ToString();
        }

        private string Unique(string name)
        {
            string candidate = name;
            for (int n = 2; !_names.Add(candidate); n++) candidate = name + n.ToString(CultureInfo.InvariantCulture);
            return candidate;
        }

        /// <summary>The type of a value in the language; objects are queued as types of their own under a name made from <paramref name="hint"/>.</summary>
        private string TypeOf(Shape shape, string hint, bool optional)
        {
            string type = shape.Kind switch
            {
                Kind.Bool => language switch { TypeLanguage.Go => "bool", TypeLanguage.CSharp => "bool", _ => "boolean" },
                Kind.Integer => language switch { TypeLanguage.Go => "int64", TypeLanguage.CSharp => "long", _ => "number" },
                Kind.Number => language switch { TypeLanguage.Go => "float64", TypeLanguage.CSharp => "double", _ => "number" },
                Kind.String => language == TypeLanguage.Go || language == TypeLanguage.CSharp ? "string" : "string",
                Kind.Object => Queue(shape, hint),
                Kind.Array => ArrayOf(shape, hint),
                _ => language switch { TypeLanguage.Go => "interface{}", TypeLanguage.CSharp => "object", _ => "unknown" },
            };
            if (!optional) return type;
            return language switch
            {
                TypeLanguage.Go => shape.Kind is Kind.Array or Kind.Unknown ? type : "*" + type,
                TypeLanguage.CSharp => shape.Kind is Kind.Unknown ? type : type + "?",
                _ => type,
            };
        }

        private string ArrayOf(Shape shape, string hint)
        {
            var item = shape.Item ?? new Shape();
            string element = TypeOf(item, Singular(hint), optional: false);
            return language switch { TypeLanguage.Go => "[]" + element, TypeLanguage.CSharp => $"List<{element}>", _ => item.Kind == Kind.Unknown ? "unknown[]" : element.Contains(' ') ? $"({element})[]" : element + "[]" };
        }

        private string Queue(Shape shape, string hint)
        {
            string name = Unique(Pascal(hint, "Item"));
            _types.Add((name, shape));
            return name;
        }

        private string Declare(string name, Shape shape)
        {
            var sb = new StringBuilder();
            var used = new HashSet<string>(StringComparer.Ordinal);
            switch (language)
            {
                case TypeLanguage.Go: sb.Append($"type {name} struct {{\n"); break;
                case TypeLanguage.CSharp: sb.Append($"public class {name}\n{{\n"); break;
                default: sb.Append($"export interface {name} {{\n"); break;
            }
            // the types of the fields are decided first so the nested types come out in the order of the fields
            var lines = new List<string>();
            foreach (var key in shape.Order)
            {
                var field = shape.Fields[key];
                bool optional = field.Nullable;
                string id = Pascal(key, "Field");
                for (int n = 2; !used.Add(id); n++) id = Pascal(key, "Field") + n.ToString(CultureInfo.InvariantCulture);
                string type = TypeOf(field, key, optional);
                lines.Add(language switch
                {
                    TypeLanguage.Go => $"\t{id} {type} `json:\"{key}{(optional ? ",omitempty" : "")}\"`",
                    TypeLanguage.CSharp => $"    [JsonPropertyName({Quote(key)})]\n    public {type} {id} {{ get; set; }}",
                    _ => $"  {(IsIdentifier(key) ? key : Quote(key))}{(optional ? "?" : "")}: {type};",
                });
            }
            sb.Append(string.Join("\n", lines));
            if (lines.Count > 0) sb.Append('\n');
            sb.Append(language == TypeLanguage.Go ? "}\n" : language == TypeLanguage.CSharp ? "}\n" : "}\n");
            return sb.ToString();
        }
    }

    private static string Quote(string s) => "\"" + System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode(s) + "\"";

    private static bool IsIdentifier(string s) => s.Length > 0 && !char.IsDigit(s[0]) && s.All(c => char.IsLetterOrDigit(c) || c is '_' or '$');

    /// <summary>A name in PascalCase made of the letters and digits of <paramref name="text"/> (words start at every character that is not one); <paramref name="fallback"/> when nothing is left.</summary>
    internal static string Pascal(string text, string fallback)
    {
        var sb = new StringBuilder();
        bool upper = true;
        foreach (char c in text)
        {
            if (!char.IsLetterOrDigit(c)) { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        if (sb.Length == 0) return fallback;
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    private static string Singular(string name) =>
        name.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && name.Length > 3 ? name[..^3] + "y"
        : name.EndsWith('s') && name.Length > 1 && !name.EndsWith("ss", StringComparison.OrdinalIgnoreCase) ? name[..^1] : name;
}
