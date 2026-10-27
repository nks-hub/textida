using System.Globalization;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>One finding of a check against a schema. <see cref="Code"/> is the keyword that failed; <see cref="Detail"/> is English text for logs.</summary>
public sealed record SchemaIssue(string Code, string Detail, int Start, int End, string Pointer);

/// <summary>Limits the caller decides; there are no defaults.</summary>
public sealed record SchemaOptions(int MaxDepth, TimeSpan RegexTimeout);

/// <summary>
/// Checks a <see cref="DataNode"/> against a JSON Schema (draft-07 / 2019-09 / 2020-12 subset): type, enum, const, properties, patternProperties, additionalProperties,
/// required, propertyNames, min/maxProperties, items (schema or array), prefixItems, additionalItems, contains, min/maxItems, uniqueItems, min/maxLength, pattern,
/// minimum, maximum, exclusiveMinimum, exclusiveMaximum, multipleOf, allOf, anyOf, oneOf, not, if/then/else and local <c>$ref</c> (<c>#</c>, <c>#/...</c>).
/// <c>format</c>, remote references and <c>unevaluated*</c> are ignored.
/// </summary>
public sealed class SchemaValidator(DataNode root, SchemaOptions options)
{
    public List<SchemaIssue> Validate(DataNode data)
    {
        var issues = new List<SchemaIssue>();
        Check(root, data, "", issues, 0);
        return issues;
    }

    private static string Join(string pointer, string part) => pointer + "/" + part.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private DataNode? Resolve(string reference)
    {
        if (reference == "#") return root;
        if (!reference.StartsWith("#/", StringComparison.Ordinal)) return null;
        var node = root;
        foreach (var raw in reference[2..].Split('/'))
        {
            var part = Uri.UnescapeDataString(raw).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            node = node.Kind == DataKind.Array && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var ix) && ix < node.Items.Count ? node.Items[ix] : node.Get(part);
            if (node is null) return null;
        }
        return node;
    }

    private void Add(List<SchemaIssue> issues, string code, string detail, int start, int end, string pointer) => issues.Add(new(code, detail, start, end, pointer));

    private bool Matches(DataNode schema, DataNode data, string pointer, int depth)
    {
        var tmp = new List<SchemaIssue>();
        Check(schema, data, pointer, tmp, depth + 1);
        return tmp.Count == 0;
    }

    private static bool TypeIs(string type, DataNode d) => type switch
    {
        "object" => d.Kind == DataKind.Object,
        "array" => d.Kind == DataKind.Array,
        "string" => d.Kind == DataKind.String,
        "number" => d.Kind == DataKind.Number,
        "integer" => d.IsInteger,
        "boolean" => d.Kind == DataKind.Boolean,
        "null" => d.Kind == DataKind.Null,
        _ => true,
    };

    private void Check(DataNode schema, DataNode d, string ptr, List<SchemaIssue> issues, int depth)
    {
        if (depth > options.MaxDepth) return;
        if (schema.Kind == DataKind.Boolean)
        {
            if (!schema.Bool) Add(issues, "false", "No value is allowed here.", d.Start, d.End, ptr);
            return;
        }
        if (schema.Kind != DataKind.Object) return;

        if (schema.Get("$ref") is { Kind: DataKind.String } r && Resolve(r.Text) is { } target) Check(target, d, ptr, issues, depth + 1);

        if (schema.Get("type") is { } t)
        {
            var types = t.Kind == DataKind.String ? [t.Text] : t.Items.Where(i => i.Kind == DataKind.String).Select(i => i.Text).ToList();
            if (types.Count > 0 && !types.Any(x => TypeIs(x, d)))
            {
                Add(issues, "type", $"Expected {string.Join(" or ", types)}.", d.Start, d.End, ptr);
                return;
            }
        }
        if (schema.Get("enum") is { Kind: DataKind.Array } en && !en.Items.Any(d.SameValue)) Add(issues, "enum", "The value is not one of the allowed values.", d.Start, d.End, ptr);
        if (schema.Get("const") is { } c && !c.SameValue(d)) Add(issues, "const", "The value differs from the required constant.", d.Start, d.End, ptr);

        if (schema.Get("allOf") is { Kind: DataKind.Array } all) foreach (var s in all.Items) Check(s, d, ptr, issues, depth + 1);
        if (schema.Get("anyOf") is { Kind: DataKind.Array } any && !any.Items.Any(s => Matches(s, d, ptr, depth)))
            Add(issues, "anyOf", "The value matches none of the allowed alternatives.", d.Start, d.End, ptr);
        if (schema.Get("oneOf") is { Kind: DataKind.Array } one)
        {
            int n = one.Items.Count(s => Matches(s, d, ptr, depth));
            if (n != 1) Add(issues, "oneOf", n == 0 ? "The value matches none of the allowed alternatives." : "The value matches more than one alternative.", d.Start, d.End, ptr);
        }
        if (schema.Get("not") is { } not && Matches(not, d, ptr, depth)) Add(issues, "not", "The value matches a schema that is not allowed.", d.Start, d.End, ptr);
        if (schema.Get("if") is { } cond)
        {
            if (Matches(cond, d, ptr, depth)) { if (schema.Get("then") is { } th) Check(th, d, ptr, issues, depth + 1); }
            else if (schema.Get("else") is { } el) Check(el, d, ptr, issues, depth + 1);
        }

        switch (d.Kind)
        {
            case DataKind.Object: CheckObject(schema, d, ptr, issues, depth); break;
            case DataKind.Array: CheckArray(schema, d, ptr, issues, depth); break;
            case DataKind.String: CheckString(schema, d, ptr, issues); break;
            case DataKind.Number: CheckNumber(schema, d, ptr, issues); break;
        }
    }

    private void CheckObject(DataNode schema, DataNode d, string ptr, List<SchemaIssue> issues, int depth)
    {
        if (schema.Get("required") is { Kind: DataKind.Array } req)
            foreach (var name in req.Items.Where(i => i.Kind == DataKind.String).Select(i => i.Text))
                if (d.Get(name) is null) Add(issues, "required", $"The property '{name}' is required.", d.Start, d.Start + 1, ptr);
        if (schema.Get("minProperties") is { Kind: DataKind.Number } minP && d.Members.Count < minP.Number) Add(issues, "minProperties", $"At least {minP.Text} properties are required.", d.Start, d.End, ptr);
        if (schema.Get("maxProperties") is { Kind: DataKind.Number } maxP && d.Members.Count > maxP.Number) Add(issues, "maxProperties", $"At most {maxP.Text} properties are allowed.", d.Start, d.End, ptr);

        var props = schema.Get("properties");
        var patterns = schema.Get("patternProperties");
        var additional = schema.Get("additionalProperties");
        foreach (var m in d.Members)
        {
            var childPtr = Join(ptr, m.Name);
            bool known = false;
            if (props?.Get(m.Name) is { } ps) { known = true; Check(ps, m.Value, childPtr, issues, depth + 1); }
            if (patterns is { Kind: DataKind.Object })
                foreach (var p in patterns.Members)
                    if (IsMatch(p.Name, m.Name)) { known = true; Check(p.Value, m.Value, childPtr, issues, depth + 1); }
            if (!known && additional is not null)
            {
                if (additional.Kind == DataKind.Boolean) { if (!additional.Bool) Add(issues, "additionalProperties", $"The property '{m.Name}' is not allowed.", m.NameStart, m.NameEnd, childPtr); }
                else Check(additional, m.Value, childPtr, issues, depth + 1);
            }
            if (schema.Get("propertyNames") is { } pn)
            {
                var nameNode = new DataNode(DataKind.String, m.NameStart, m.NameEnd) { Text = m.Name };
                if (!Matches(pn, nameNode, childPtr, depth)) Add(issues, "propertyNames", $"The property name '{m.Name}' is not allowed.", m.NameStart, m.NameEnd, childPtr);
            }
        }
    }

    private void CheckArray(DataNode schema, DataNode d, string ptr, List<SchemaIssue> issues, int depth)
    {
        if (schema.Get("minItems") is { Kind: DataKind.Number } minI && d.Items.Count < minI.Number) Add(issues, "minItems", $"At least {minI.Text} items are required.", d.Start, d.End, ptr);
        if (schema.Get("maxItems") is { Kind: DataKind.Number } maxI && d.Items.Count > maxI.Number) Add(issues, "maxItems", $"At most {maxI.Text} items are allowed.", d.Start, d.End, ptr);
        if (schema.Get("uniqueItems") is { Kind: DataKind.Boolean, Bool: true })
            for (int i = 1; i < d.Items.Count; i++)
                for (int j = 0; j < i; j++)
                    if (d.Items[i].SameValue(d.Items[j])) { Add(issues, "uniqueItems", "The items must be unique.", d.Items[i].Start, d.Items[i].End, Join(ptr, i.ToString(CultureInfo.InvariantCulture))); break; }

        int tuple = 0;
        var items = schema.Get("items");
        if (schema.Get("prefixItems") is { Kind: DataKind.Array } prefix) tuple = prefix.Items.Count;
        else if (items is { Kind: DataKind.Array }) { prefix = items; tuple = items.Items.Count; items = schema.Get("additionalItems"); }
        else prefix = null!;
        for (int i = 0; i < d.Items.Count; i++)
        {
            var childPtr = Join(ptr, i.ToString(CultureInfo.InvariantCulture));
            if (i < tuple) Check(prefix.Items[i], d.Items[i], childPtr, issues, depth + 1);
            else if (items is not null) Check(items, d.Items[i], childPtr, issues, depth + 1);
        }
        if (schema.Get("contains") is { } contains && !d.Items.Any(i => Matches(contains, i, ptr, depth)))
            Add(issues, "contains", "No item matches the required schema.", d.Start, d.End, ptr);
    }

    private void CheckString(DataNode schema, DataNode d, string ptr, List<SchemaIssue> issues)
    {
        int codePoints = d.Text.EnumerateRunes().Count();
        if (schema.Get("minLength") is { Kind: DataKind.Number } minL && codePoints < minL.Number) Add(issues, "minLength", $"At least {minL.Text} characters are required.", d.Start, d.End, ptr);
        if (schema.Get("maxLength") is { Kind: DataKind.Number } maxL && codePoints > maxL.Number) Add(issues, "maxLength", $"At most {maxL.Text} characters are allowed.", d.Start, d.End, ptr);
        if (schema.Get("pattern") is { Kind: DataKind.String } pat && !IsMatch(pat.Text, d.Text, defaultWhenInvalid: true))
            Add(issues, "pattern", $"The text does not match the pattern '{pat.Text}'.", d.Start, d.End, ptr);    }

    private void CheckNumber(DataNode schema, DataNode d, string ptr, List<SchemaIssue> issues)
    {
        double v = d.Number;
        if (schema.Get("minimum") is { Kind: DataKind.Number } min && v < min.Number) Add(issues, "minimum", $"The value must be at least {min.Text}.", d.Start, d.End, ptr);
        if (schema.Get("maximum") is { Kind: DataKind.Number } max && v > max.Number) Add(issues, "maximum", $"The value must be at most {max.Text}.", d.Start, d.End, ptr);
        if (schema.Get("exclusiveMinimum") is { Kind: DataKind.Number } xmin && v <= xmin.Number) Add(issues, "exclusiveMinimum", $"The value must be greater than {xmin.Text}.", d.Start, d.End, ptr);
        if (schema.Get("exclusiveMaximum") is { Kind: DataKind.Number } xmax && v >= xmax.Number) Add(issues, "exclusiveMaximum", $"The value must be less than {xmax.Text}.", d.Start, d.End, ptr);
        if (schema.Get("multipleOf") is { Kind: DataKind.Number } mul && mul.Number > 0)
        {
            double q = v / mul.Number;
            if (Math.Abs(q - Math.Round(q)) > 1e-9) Add(issues, "multipleOf", $"The value must be a multiple of {mul.Text}.", d.Start, d.End, ptr);
        }
    }

    private bool IsMatch(string pattern, string input, bool defaultWhenInvalid = false)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.CultureInvariant, options.RegexTimeout); }
        catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException) { return defaultWhenInvalid; }
    }
}
