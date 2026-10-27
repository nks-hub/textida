using System.Buffers;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Plugins.TextTools;

/// <summary>
/// Selected cells of a table: the <paramref name="Header"/> of the selected columns (null without one), the rows and the 0-based index of the first selected column
/// (a column without a header name is called by its spreadsheet letter). Every value is text; no type is guessed.
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
    /// One <c>INSERT</c> per row, values as string literals (a quote doubled, nothing else changed) and names as quoted identifiers (standard SQL double quotes).
    /// The column list is the header; without a header, or with a column that has no name, the values go in column order.
    /// </summary>
    public static string Sql(TableData t, string table)
    {
        var head = $"INSERT INTO {Identifier(table)}" + (t.Header is null || t.Header.Any(h => h.Length == 0) ? "" : " (" + string.Join(", ", t.Header.Select(Identifier)) + ")") + " VALUES (";
        var text = new StringBuilder();
        foreach (var row in t.Rows) text.Append(head).AppendJoin(", ", row.Select(v => "'" + v.Replace("'", "''") + "'")).Append(");\n");
        return text.ToString();
    }

    private static string Identifier(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

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
