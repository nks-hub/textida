using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Plugins.TextTools;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P5.1.2: the selected cells copied as a Markdown table, SQL INSERT, JSON and HTML; the forms alone, then the commands through a real plugin process.</summary>
public partial class TextToolsTests
{
    /// <summary>One value for every character that needs escaping somewhere: apostrophe, double quote, pipe, angle bracket, ampersand, backslash, a line break.</summary>
    static readonly string[] Nasty = ["it's", "say \"hi\"", "a|b", "<b>x</b>", "Tom & Jerry", "C:\\dir\\_x", "line1\nline2", "crlf\r\nend", "", "plain"];

    static TableData Three(bool header = true) =>
        new(0, 3, header ? ["id", "na'me", "no|te"] : null, [
            [Nasty[0], Nasty[1], Nasty[2]],
            [Nasty[3], Nasty[4], Nasty[5]],
            [Nasty[6], Nasty[7], Nasty[8]],
            [Nasty[9], "1", "2"],
        ]);

    static string Unbreak(string value) => value.Replace("\r\n", "\n").Replace("\r", "\n");

    static IEnumerable<string> Expected(TableData t) => t.Rows.SelectMany(r => r).Select(Unbreak);

    // ---- Markdown ----

    /// <summary>Reads a pipe table back: cells split on pipes that no backslash escapes, backslashes removed, <c>&lt;br&gt;</c> a line break.</summary>
    static List<List<string>> ParsePipeTable(string markdown) => [.. markdown.TrimEnd('\n').Split('\n').Select(line =>
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (int i = 1; i < line.Length; i++)
        {
            if (line[i] == '\\') cell.Append(line[++i]);
            else if (line[i] == '|') { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(line[i]);
        }
        Assert.True(cell.ToString().Trim().Length == 0, "text after the last pipe");
        return cells.Select(c => c.Replace("<br>", "\n")).ToList();
    })];

    [Fact]
    public void Markdown_is_a_valid_table_and_each_value_survives_the_escaping()
    {
        var table = Three();
        var markdown = TableExport.Markdown(table);
        var lines = ParsePipeTable(markdown);

        Assert.Equal(2 + table.Rows.Count, lines.Count);
        Assert.All(lines, l => Assert.Equal(3, l.Count));
        Assert.Equal(["id", "na'me", "no|te"], lines[0]);
        Assert.Equal(["---", "---", "---"], lines[1]);
        Assert.Equal(Expected(table), lines.Skip(2).SelectMany(l => l));
        Assert.Equal(markdown.Count(c => c == '\n'), lines.Count); // a line break in a cell never splits the row
        Assert.Contains("a\\|b", markdown);
        Assert.Contains("\\<b\\>", markdown);
    }

    [Fact]
    public void Markdown_without_a_header_names_the_columns_by_letter_from_the_first_selected_one()
    {
        var table = new TableData(27, 2, null, [["x", "y"]]); // columns AB and AC
        Assert.Equal(["AB", "AC"], ParsePipeTable(TableExport.Markdown(table))[0]);
    }

    // ---- SQL ----

    /// <summary>
    /// Reads INSERT statements back as the database of the dialect would: names in double quotes (backticks for <c>mysql</c>, brackets for <c>tsql</c>), values in single
    /// quotes with a doubled quote for one (<c>N</c> before them for <c>tsql</c>; for <c>mysql</c> a backslash starts an escape, so a lone one is lost).
    /// </summary>
    static List<(string Table, List<string> Columns, List<string> Values)> ParseInserts(string sql, string dialect = "generic")
    {
        var (open, close) = dialect switch { "mysql" => ('`', '`'), "tsql" => ('[', ']'), _ => ('"', '"') };
        var result = new List<(string, List<string>, List<string>)>();
        int i = 0;
        string Quoted(char start, char end, bool escapes = false)
        {
            Assert.Equal(start, sql[i++]);
            var text = new StringBuilder();
            while (true)
            {
                if (escapes && sql[i] == '\\')
                {
                    char e = sql[i + 1];
                    // MySQL keeps the backslash of \% and \_ and drops it before any other character
                    text.Append(e switch { 'n' => "\n", 'r' => "\r", 't' => "\t", '0' => "\0", 'b' => "\b", 'Z' => "\u001a", '%' or '_' => "\\" + e, _ => e.ToString() });
                    i += 2;
                    continue;
                }
                if (sql[i] == end)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == end) { text.Append(end); i += 2; continue; }
                    i++;
                    return text.ToString();
                }
                text.Append(sql[i++]);
            }
        }
        string Name() => Quoted(open, close);
        string Value()
        {
            if (dialect == "tsql") Assert.Equal('N', sql[i++]);
            return Quoted('\'', '\'', escapes: dialect == "mysql");
        }
        void Skip(string expected)
        {
            while (sql[i] == ' ') i++;
            Assert.Equal(expected, sql.Substring(i, expected.Length));
            i += expected.Length;
        }
        while (i < sql.Length)
        {
            Skip("INSERT INTO ");
            var table = Name();
            var columns = new List<string>();
            Assert.Equal(' ', sql[i++]);
            if (sql[i] == '(')
            {
                i++;
                do { columns.Add(Name()); } while (sql[i] == ',' && ++i > 0 && sql[i++] == ' ');
                Skip(")");
            }
            Skip("VALUES (");
            var values = new List<string>();
            do { values.Add(Value()); } while (sql[i] == ',' && ++i > 0 && sql[i++] == ' ');
            Skip(");");
            Assert.Equal('\n', sql[i++]);
            result.Add((table, columns, values));
        }
        return result;
    }

    [Fact]
    public void Sql_inserts_each_row_with_every_value_in_one_literal()
    {
        var table = Three();
        var statements = ParseInserts(TableExport.Sql(table, "my\"table"));

        Assert.Equal(table.Rows.Count, statements.Count);
        Assert.All(statements, s =>
        {
            Assert.Equal("my\"table", s.Table);
            Assert.Equal(["id", "na'me", "no|te"], s.Columns);
        });
        Assert.Equal(table.Rows.SelectMany(r => r), statements.SelectMany(s => s.Values));
        Assert.Contains("'it''s'", TableExport.Sql(table, "t"));
        Assert.Contains("\"na'me\"", TableExport.Sql(table, "t"));
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("postgres")]
    [InlineData("sqlite")]
    [InlineData("mysql")]
    [InlineData("tsql")]
    public void Sql_of_each_dialect_reads_back_to_the_same_names_and_values(string dialect)
    {
        var table = Three();
        const string name = "my\"`]table";
        var statements = ParseInserts(TableExport.Sql(table, name, dialect), dialect);

        Assert.Equal(table.Rows.Count, statements.Count);
        Assert.All(statements, s =>
        {
            Assert.Equal(name, s.Table);
            Assert.Equal(["id", "na'me", "no|te"], s.Columns);
        });
        Assert.Equal(table.Rows.SelectMany(r => r), statements.SelectMany(s => s.Values));
    }

    [Fact]
    public void Mysql_doubles_a_backslash_tsql_marks_values_as_unicode_and_the_others_leave_it()
    {
        var one = new TableData(0, 1, ["path"], [["C:\\dir"]]);
        Assert.Equal("INSERT INTO `t` (`path`) VALUES ('C:\\\\dir');\n", TableExport.Sql(one, "t", "mysql"));
        Assert.Equal("INSERT INTO [t] ([path]) VALUES (N'C:\\dir');\n", TableExport.Sql(one, "t", "tsql"));
        Assert.Equal("INSERT INTO \"t\" (\"path\") VALUES ('C:\\dir');\n", TableExport.Sql(one, "t", "postgres"));
        Assert.Equal(TableExport.Sql(one, "t"), TableExport.Sql(one, "t", "no-such-dialect"));
    }

    [Fact]
    public void Sql_lists_columns_only_when_every_one_has_a_name()
    {
        Assert.Empty(ParseInserts(TableExport.Sql(Three(header: false), "t"))[0].Columns);
        var unnamed = new TableData(0, 2, ["a", ""], [["1", "2"]]);
        Assert.Empty(ParseInserts(TableExport.Sql(unnamed, "t"))[0].Columns);
    }

    // ---- JSON ----

    [Fact]
    public void Json_is_an_array_of_objects_by_header_or_an_array_of_arrays()
    {
        var table = Three();
        using (var objects = JsonDocument.Parse(TableExport.Json(table)))
        {
            var rows = objects.RootElement.EnumerateArray().ToList();
            Assert.Equal(table.Rows.Count, rows.Count);
            Assert.Equal(table.Rows.SelectMany(r => r), rows.SelectMany(r => r.EnumerateObject().Select(p => p.Value.GetString())));
            Assert.Equal(["id", "na'me", "no|te"], rows[0].EnumerateObject().Select(p => p.Name));
        }
        foreach (var other in new[] { Three(header: false), new TableData(0, 2, ["a", "a"], [["1", "2"]]), new TableData(0, 2, ["a", ""], [["1", "2"]]) })
        {
            using var arrays = JsonDocument.Parse(TableExport.Json(other));
            Assert.Equal(other.Rows.SelectMany(r => r), arrays.RootElement.EnumerateArray().SelectMany(r => r.EnumerateArray().Select(v => v.GetString())));
        }
    }

    // ---- HTML ----

    [Fact]
    public void Html_is_a_table_whose_text_is_entities_and_each_value_survives()
    {
        var table = Three();
        var html = TableExport.Html(table);
        string Cells(string tag) => string.Join("\u0001", Regex.Matches(html, $"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value.Replace("<br>", "\n"))));

        Assert.Equal("id\u0001na'me\u0001no|te", Cells("th"));
        Assert.Equal(string.Join("\u0001", Expected(table)), Cells("td"));
        var text = Regex.Replace(html, "<[a-z/]+>", "");
        Assert.DoesNotContain('<', text);
        Assert.DoesNotContain('"', text);
        Assert.DoesNotContain('\'', text);
        Assert.Contains("&lt;b&gt;", html);
        Assert.Contains("it&#39;s", html);
        Assert.Contains("say &quot;hi&quot;", html);
        Assert.Contains("Tom &amp; Jerry", html);
        Assert.DoesNotContain("<thead>", TableExport.Html(Three(header: false)));
    }

    // ---- the commands, through the plugin process ----

    static TableSelectionData Selected(TableData t, long firstColumn = 0) => new((int)firstColumn, t.Columns,
        t.Header?.Select(h => new TableCell(h, new ByteRange(0, 0))).ToList(),
        t.Rows.Select((r, i) => new TableRow(i + 1, [.. r.Select(v => new TableCell(v, new ByteRange(0, 0)))])));

    [Fact]
    public async Task The_four_commands_write_their_form_to_the_clipboard_for_the_selected_three_columns()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), TextCodec.Utf8);
        var table = Three();
        rig.Tools.Table = () => Selected(table);

        Assert.Equal("completed", await rig.RunAsync("table.copyAs.markdown"));
        Assert.Equal("completed", await rig.RunAsync("table.copyAs.sql", extra: ",\"table\":\"people\""));
        Assert.Equal("completed", await rig.RunAsync("table.copyAs.json"));
        Assert.Equal("completed", await rig.RunAsync("table.copyAs.html"));

        Assert.Equal(4, rig.Tools.Clipboard.Count);
        Assert.Equal(TableExport.Markdown(table), rig.Tools.Clipboard[0].Text);
        Assert.Null(rig.Tools.Clipboard[0].Html);
        Assert.Equal(TableExport.Sql(table, "people"), rig.Tools.Clipboard[1].Text);
        Assert.Equal(TableExport.Json(table), rig.Tools.Clipboard[2].Text);
        Assert.Equal((TableExport.Html(table), TableExport.Html(table)), rig.Tools.Clipboard[3]);
        Assert.Contains("3 columns", rig.LastNotice);
    }

    [Fact]
    public async Task A_form_longer_than_the_clipboard_limit_copies_nothing_and_the_host_refuses_with_the_limit()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), TextCodec.Utf8);
        var rows = Enumerable.Range(0, 200).Select(i => (IReadOnlyList<string>)[new string('x', 10)]).ToList();
        rig.Tools.Table = () => Selected(new TableData(0, 1, ["v"], rows));

        var refused = await Assert.ThrowsAsync<NksHub.Textida.Rpc.RpcException>(() => rig.RunAsync("table.copyAs.markdown"));
        Assert.Contains("at most 1000 characters", refused.Message);
        Assert.Empty(rig.Tools.Clipboard);

        rig.Tools.Table = () => Selected(new TableData(0, 1, ["v"], [["short"]]));
        Assert.Equal("completed", await rig.RunAsync("table.copyAs.markdown"));
        Assert.Single(rig.Tools.Clipboard);
    }

    [Fact]
    public async Task A_selection_of_an_older_revision_copies_nothing_and_a_document_without_a_table_says_so()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), TextCodec.Utf8);
        rig.Tools.Table = () => Selected(Three());
        var old = rig.Doc.Revision;
        rig.Doc.Insert(0, Utf8("y"));

        Assert.Equal("stale", await rig.RunAsync("table.copyAs.markdown", revision: old));
        Assert.Empty(rig.Tools.Clipboard);

        rig.Tools.Table = null;
        Assert.Equal("failed", await rig.RunAsync("table.copyAs.json"));
        Assert.Equal("The document is not shown as a table.", rig.LastNotice);
        Assert.Empty(rig.Tools.Clipboard);
    }

    [Fact]
    public async Task The_sql_form_asks_for_the_table_name_and_a_dismissed_question_copies_nothing()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), TextCodec.Utf8);
        rig.Tools.Table = () => Selected(Three());
        InputRequest? asked = null;
        rig.Recorded.Input = r => { asked = r; return null; };
        Assert.Equal("cancelled", await rig.RunAsync("table.copyAs.sql"));
        Assert.Empty(rig.Tools.Clipboard);
        Assert.Equal("Table name", asked!.Label);

        rig.Recorded.Input = _ => "orders";
        Assert.Equal("completed", await rig.RunAsync("table.copyAs.sql"));
        Assert.StartsWith("INSERT INTO \"orders\" ", rig.Tools.Clipboard[0].Text);
    }

    [Fact]
    public async Task The_sql_form_writes_the_dialect_the_editor_names()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), TextCodec.Utf8);
        var table = Three();
        rig.Tools.Table = () => Selected(table);

        Assert.Equal("completed", await rig.RunAsync("table.copyAs.sql", extra: ",\"table\":\"people\",\"dialect\":\"mysql\""));
        Assert.Equal("completed", await rig.RunAsync("table.copyAs.sql", extra: ",\"table\":\"people\",\"dialect\":\"tsql\""));

        Assert.Equal(TableExport.Sql(table, "people", "mysql"), rig.Tools.Clipboard[0].Text);
        Assert.Equal(TableExport.Sql(table, "people", "tsql"), rig.Tools.Clipboard[1].Text);
        Assert.StartsWith("INSERT INTO `people` ", rig.Tools.Clipboard[0].Text);
    }
}
