using System.Buffers;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Plugins.TextTools;

/// <summary>
/// Selected cells of a table: the <paramref name="Header"/> of the selected columns (null without one), the rows and the 0-based index of the first selected column
/// (a column without a header name is called by its spreadsheet letter). Every value is text; only the SQL form looks at its type (see <see cref="TableExport.Sql"/>).
/// </summary>
public sealed record TableData(int FirstColumn, int Columns, IReadOnlyList<string>? Header, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>The name of a selected column for a header that has none: A, B, ... Z, AA, as a spreadsheet writes it.</summary>
    public string Letter(int column)
    {
        var letters = new StringBuilder();
        for (int n = FirstColumn + column + 1; n > 0; n = (n - 1) / 26) letters.Insert(0, (char)('A' + (n - 1) % 26));
        return letters.ToString();
    }
}

/// <summary>
/// The selected cells written as a Markdown table, SQL <c>INSERT</c> statements, a JSON array or an HTML table. Every value is escaped for the form it goes
/// into, so a cell holding an apostrophe, a quote, a pipe, an angle bracket or a line break stays one cell with its own text.
/// </summary>
public static class TableExport
{
    // ---- Markdown ----

    private const string MarkdownSpecial = "\\|`*_[]<>&~";

    /// <summary>A pipe table (GitHub flavour). A cell's special characters are escaped with a backslash and a line break becomes <c>&lt;br&gt;</c>, which the table row allows.</summary>
    public static string Markdown(TableData t)
    {
        var text = new StringBuilder();
        text.Append("| ").AppendJoin(" | ", Enumerable.Range(0, t.Columns).Select(c => MarkdownCell(t.Header is null ? t.Letter(c) : t.Header[c]))).Append(" |\n");
        text.Append("| ").AppendJoin(" | ", Enumerable.Repeat("---", t.Columns)).Append(" |\n");
        foreach (var row in t.Rows) text.Append("| ").AppendJoin(" | ", row.Select(MarkdownCell)).Append(" |\n");
        return text.ToString();
    }

    private static string MarkdownCell(string value)
    {
        var cell = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < value.Length && value[i + 1] == '\n') i++;
                cell.Append("<br>");
            }
            else
            {
                if (MarkdownSpecial.Contains(c)) cell.Append('\\');
                cell.Append(c);
            }
        }
        return cell.ToString();
    }

    // ---- SQL ----

    /// <summary>
    /// One <c>INSERT</c> per row, values as string literals with a quote doubled and names as quoted identifiers. The column list is the header; without a header,
    /// or with a column that has no name, the values go in column order. <paramref name="dialect"/> is an id of the <c>sql.dialect</c> setting: <c>mysql</c> quotes
    /// names with backticks and doubles a backslash (MySQL reads one as an escape), <c>tsql</c> quotes names with brackets and writes <c>N'...'</c> so Unicode text
    /// survives; any other id writes standard SQL (double quotes, nothing else changed).
    /// Values are typed by what the cell holds: an empty cell is <c>NULL</c>, a number in the invariant form (<see cref="IsInvariantNumber"/>) is written as it is,
    /// and everything else is a string literal. So "007", "1,5" and " 3" stay strings, and a column of whole numbers loads into a numeric column.
    /// </summary>
    public static string Sql(TableData t, string table, string dialect = "generic")
    {
        Func<string, string> identifier = dialect switch
        {
            "mysql" => n => "`" + n.Replace("`", "``") + "`",
            "tsql" => n => "[" + n.Replace("]", "]]") + "]",
            _ => n => "\"" + n.Replace("\"", "\"\"") + "\"",
        };
        Func<string, string> literal = dialect switch
        {
            "mysql" => v => "'" + v.Replace("\\", "\\\\").Replace("'", "''") + "'",
            "tsql" => v => "N'" + v.Replace("'", "''") + "'",
            _ => v => "'" + v.Replace("'", "''") + "'",
        };
        string Value(string v) => v.Length == 0 ? "NULL" : IsInvariantNumber(v) ? v : literal(v);
        var head = $"INSERT INTO {identifier(table)}" + (t.Header is null || t.Header.Any(h => h.Length == 0) ? "" : " (" + string.Join(", ", t.Header.Select(identifier)) + ")") + " VALUES (";
        var text = new StringBuilder();
        foreach (var row in t.Rows) text.Append(head).AppendJoin(", ", row.Select(Value)).Append(");\n");
        return text.ToString();
    }

    /// <summary>
    /// A number in the form every SQL dialect and JSON read the same: an optional minus, digits without a leading zero, an optional fraction with a dot, an optional exponent.
    /// Anything else (a plus sign, a leading zero, a thousands separator, a decimal comma, spaces) is text.
    /// </summary>
    public static bool IsInvariantNumber(string value)
    {
        int i = 0, n = value.Length;
        if (i < n && value[i] == '-') i++;
        int digits = i;
        while (i < n && value[i] is >= '0' and <= '9') i++;
        if (i == digits || (value[digits] == '0' && i - digits > 1) || i - digits > 38) return false;
        if (i < n && value[i] == '.')
        {
            int fraction = ++i;
            while (i < n && value[i] is >= '0' and <= '9') i++;
            if (i == fraction) return false;
        }
        if (i < n && value[i] is 'e' or 'E')
        {
            i++;
            if (i < n && value[i] is '+' or '-') i++;
            int exponent = i;
            while (i < n && value[i] is >= '0' and <= '9') i++;
            if (i == exponent || i - exponent > 4) return false;
        }
        return i == n;
    }

    // ---- JSON ----

    /// <summary>
    /// An array of objects keyed by the header when every name is there and none repeats, else an array of arrays. All values are strings.
    /// </summary>
    public static string Json(TableData t)
    {
        bool keyed = t.Header is not null && t.Header.All(h => h.Length > 0) && t.Header.Distinct(StringComparer.Ordinal).Count() == t.Header.Count;
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartArray();
            foreach (var row in t.Rows)
            {
                if (keyed)
                {
                    w.WriteStartObject();
                    for (int c = 0; c < row.Count; c++) w.WriteString(t.Header![c], row[c]);
                    w.WriteEndObject();
                }
                else
                {
                    w.WriteStartArray();
                    foreach (var value in row) w.WriteStringValue(value);
                    w.WriteEndArray();
                }
            }
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    // ---- HTML ----

    /// <summary>A <c>table</c> element; <c>thead</c> only with a header. The text of a cell has <c>&amp; &lt; &gt; " '</c> as entities and a line break as <c>&lt;br&gt;</c>.</summary>
    public static string Html(TableData t)
    {
        var text = new StringBuilder("<table>\n");
        if (t.Header is not null) text.Append("<thead>\n<tr>").AppendJoin("", t.Header.Select(h => "<th>" + HtmlText(h) + "</th>")).Append("</tr>\n</thead>\n");
        text.Append("<tbody>\n");
        foreach (var row in t.Rows) text.Append("<tr>").AppendJoin("", row.Select(v => "<td>" + HtmlText(v) + "</td>")).Append("</tr>\n");
        return text.Append("</tbody>\n</table>\n").ToString();
    }

    private static string HtmlText(string value)
    {
        var text = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '&': text.Append("&amp;"); break;
                case '<': text.Append("&lt;"); break;
                case '>': text.Append("&gt;"); break;
                case '"': text.Append("&quot;"); break;
                case '\'': text.Append("&#39;"); break;
                case '\r':
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    text.Append("<br>");
                    break;
                case '\n': text.Append("<br>"); break;
                default: text.Append(value[i]); break;
            }
        }
        return text.ToString();
    }
}
