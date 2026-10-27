using System.Text.Json;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// One finding as a linter reports it, before it is placed in the text. Lines and columns are 1-based. A column counts bytes where <see cref="ByteColumns"/> is set and characters otherwise;
/// <see cref="ByteStart"/> and <see cref="ByteEnd"/> are offsets in the file where the tool gives them (they win over the columns). No column means the whole line.
/// </summary>
public sealed record LintFinding(string? File, int Line, int? Column, int? EndLine, int? EndColumn, long? ByteStart, long? ByteEnd, bool ByteColumns, string SeverityWord, string Message, string? Code);

/// <summary>What a parser made of the output: <see cref="Readable"/> is false when the text is not in the format the linter is documented to write.</summary>
public sealed record LintParse(bool Readable, IReadOnlyList<LintFinding> Findings, string Problem = "");

/// <summary>
/// The parsers of the output formats named in <see cref="CatalogLinter.OutputFormat"/>. They read the machine-readable output of PHPStan, Psalm, golangci-lint, Clippy, clang-tidy and RuboCop
/// from the lines a tool wrote (standard output and error mixed, so a JSON value is looked for at the start of a line). The shapes come from the documentation of the tools; no real tool was run.
/// </summary>
public static class LinterOutput
{
    public static readonly IReadOnlyList<string> Formats = ["phpstan-json", "psalm-json", "golangci-json", "cargo-json", "gcc-text", "rubocop-json"];

    public static LintParse Parse(string format, IReadOnlyList<string> lines) => format switch
    {
        "phpstan-json" => Json(lines, PhpStan),
        "psalm-json" => Json(lines, Psalm),
        "golangci-json" => Json(lines, GolangCi),
        "rubocop-json" => Json(lines, RuboCop),
        "cargo-json" => CargoJson(lines),
        "gcc-text" => GccText(lines),
        _ => new LintParse(false, [], $"No parser for the format '{format}'."),
    };

    // ---- JSON ----

    /// <summary>The first JSON value that starts a line and parses; text before and after it (progress, a summary) is ignored.</summary>
    private static LintParse Json(IReadOnlyList<string> lines, Func<JsonElement, LintParse> read)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            string first = lines[i].TrimStart();
            if (first.Length == 0 || first[0] is not ('{' or '[')) continue;
            var text = string.Join('\n', lines.Skip(i));
            try
            {
                var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(text.TrimStart()));
                using var doc = JsonDocument.ParseValue(ref reader);
                return read(doc.RootElement);
            }
            catch (JsonException) { /* not the value; try the next line that looks like a start */ }
            catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException) { return new LintParse(false, [], "The JSON does not have the documented shape."); }
        }
        return new LintParse(false, [], "No JSON in the output.");
    }

    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int? Int(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;
    private static long? Long(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;
    private static IEnumerable<JsonElement> Items(JsonElement e, string name) => Prop(e, name) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];
    private static JsonElement Prop(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    /// <summary>PHPStan: <c>{"totals":…,"files":{"path":{"errors":1,"messages":[{"message":…,"line":12,"identifier":…}]}},"errors":[…]}</c>. It reports a line and no column.</summary>
    private static LintParse PhpStan(JsonElement root)
    {
        // PHP writes an empty "files" as [] and a filled one as an object
        if (root.ValueKind != JsonValueKind.Object || Prop(root, "files").ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return new LintParse(false, [], "No \"files\" member.");
        var found = new List<LintFinding>();
        foreach (var file in Prop(root, "files") is { ValueKind: JsonValueKind.Object } files ? files.EnumerateObject() : Enumerable.Empty<JsonProperty>())
            foreach (var m in Items(file.Value, "messages"))
                found.Add(new LintFinding(file.Name, Int(m, "line") ?? 1, null, null, null, null, null, false, "error", Str(m, "message"), NullIfEmpty(Str(m, "identifier"))));
        var general = Prop(root, "errors");
        if (found.Count == 0 && general.ValueKind == JsonValueKind.Array && general.GetArrayLength() > 0)
            return new LintParse(false, [], general[0].ValueKind == JsonValueKind.String ? general[0].GetString()! : "PHPStan reported an error.");
        return new LintParse(true, found);
    }

    /// <summary>Psalm: an array of <c>{"severity":"error","line_from":3,"column_from":5,"line_to":3,"column_to":9,"type":…,"message":…,"file_path":…}</c>; the last column is the one after the text.</summary>
    private static LintParse Psalm(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) return new LintParse(false, [], "The output is not an array.");
        var found = root.EnumerateArray().Select(i => new LintFinding(Str(i, "file_path"), Int(i, "line_from") ?? 1, Int(i, "column_from"), Int(i, "line_to"), Int(i, "column_to"), null, null, false,
            Str(i, "severity"), Str(i, "message"), NullIfEmpty(Str(i, "type")))).ToList();
        return new LintParse(true, found);
    }

    /// <summary>golangci-lint: <c>{"Issues":[{"FromLinter":…,"Text":…,"Severity":"","Pos":{"Filename":…,"Line":5,"Column":2}}]}</c>; <c>Issues</c> is null when nothing was found. Go counts columns in bytes.</summary>
    private static LintParse GolangCi(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Issues", out var issues)) return new LintParse(false, [], "No \"Issues\" member.");
        if (issues.ValueKind == JsonValueKind.Null) return new LintParse(true, []);
        if (issues.ValueKind != JsonValueKind.Array) return new LintParse(false, [], "\"Issues\" is not an array.");
        var found = issues.EnumerateArray().Select(i =>
        {
            var pos = Prop(i, "Pos");
            return new LintFinding(Str(pos, "Filename"), Int(pos, "Line") ?? 1, Int(pos, "Column"), null, null, null, null, true, Str(i, "Severity"), Str(i, "Text"), NullIfEmpty(Str(i, "FromLinter")));
        }).ToList();
        return new LintParse(true, found);
    }

    /// <summary>RuboCop: <c>{"files":[{"path":…,"offenses":[{"severity":"convention","message":…,"cop_name":…,"location":{"start_line":1,"start_column":5,"length":3}}]}]}</c>.</summary>
    private static LintParse RuboCop(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || Prop(root, "files").ValueKind != JsonValueKind.Array) return new LintParse(false, [], "No \"files\" array.");
        var found = new List<LintFinding>();
        foreach (var file in root.GetProperty("files").EnumerateArray())
            foreach (var o in Items(file, "offenses"))
            {
                var at = Prop(o, "location");
                int line = Int(at, "start_line") ?? Int(at, "line") ?? 1;
                int? column = Int(at, "start_column") ?? Int(at, "column");
                int? length = Int(at, "length");
                int? endLine = Int(at, "last_line") is { } last && last != line ? last : null;
                int? endColumn = endLine is null && column is { } start && length is { } width ? start + width : null;
                found.Add(new LintFinding(Str(file, "path"), line, column, endLine, endColumn, null, null, false, Str(o, "severity"), Str(o, "message"), NullIfEmpty(Str(o, "cop_name"))));
            }
        return new LintParse(true, found);
    }

    /// <summary>
    /// <c>cargo clippy --message-format=json</c>: one object per line; those with <c>"reason":"compiler-message"</c> carry <c>message.level</c>, <c>message.code.code</c> and <c>message.spans</c>.
    /// The primary span gives the file and the byte range. A message without a span (a summary such as "1 warning emitted") has no place and is left out.
    /// </summary>
    private static LintParse CargoJson(IReadOnlyList<string> lines)
    {
        var found = new List<LintFinding>();
        bool any = false;
        foreach (var line in lines)
        {
            string text = line.TrimStart();
            if (text.Length == 0 || text[0] != '{') continue;
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (Str(root, "reason") is not { Length: > 0 } reason) continue;
                any = true;
                if (reason != "compiler-message") continue;
                var message = Prop(root, "message");
                var spans = Prop(message, "spans");
                if (spans.ValueKind != JsonValueKind.Array) continue;
                var primary = spans.EnumerateArray().Where(s => Prop(s, "is_primary").ValueKind == JsonValueKind.True).Cast<JsonElement?>().FirstOrDefault();
                if (primary is not { } span) continue;
                found.Add(new LintFinding(Str(span, "file_name"), Int(span, "line_start") ?? 1, Int(span, "column_start"), Int(span, "line_end"), Int(span, "column_end"), Long(span, "byte_start"), Long(span, "byte_end"), false,
                    Str(message, "level"), Str(message, "message"), NullIfEmpty(Str(Prop(message, "code"), "code"))));
            }
            catch (JsonException) { /* a line that only looks like JSON */ }
        }
        return any || lines.All(l => l.Trim().Length == 0) ? new LintParse(true, found) : new LintParse(false, [], "No cargo JSON messages in the output.");
    }

    // ---- text ----

    private static readonly Regex Gcc = new(@"^(?<file>.+?):(?<line>\d+):(?<column>\d+): (?<severity>[A-Za-z][A-Za-z ]*?): (?<message>.*?)(?: \[(?<code>[^\]\s]+)\])?\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The compiler style line <c>file:line:column: severity: message [check]</c> that clang-tidy writes; the source line and the caret under it are not findings. Columns are bytes.</summary>
    private static LintParse GccText(IReadOnlyList<string> lines)
    {
        var found = new List<LintFinding>();
        foreach (var line in lines)
        {
            Match m;
            try { m = Gcc.Match(line); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!m.Success) continue;
            found.Add(new LintFinding(m.Groups["file"].Value, int.Parse(m.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(m.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture),
                null, null, null, null, true, m.Groups["severity"].Value, m.Groups["message"].Value, m.Groups["code"].Success ? m.Groups["code"].Value : null));
        }
        return new LintParse(true, found);
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
}
