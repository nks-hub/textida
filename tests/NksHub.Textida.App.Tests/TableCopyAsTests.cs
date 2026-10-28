using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P5.1.3 and P5.1.2 in the app: the selected cells as a model, copy as Markdown, SQL (in the dialect of the document), JSON and HTML through the plugin process,
/// own forms as converters of a data plugin, and hidden values that never reach the clipboard.
/// </summary>
[Collection("LocState")]
public class TableCopyAsTests : IDisposable
{
    private const string Sample = "id,name,note\n1,Ann,\"it's\"\n2,\"Bo\"\"b\",a|b\n3,Cyd,\"x\ny\"\n";
    private const string Env = "KEY=hunter2\nOTHER=\"a b\"\n";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public TableCopyAsTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        Loc.SetLanguage("en");
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private (DocTab Tab, CsvView View) OpenTable(string name, string text)
    {
        var tab = _w.OpenFile(_temp.File(name, text), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CommandOutcome.Handled, _app.Workbench.Commands.ExecuteAsync("nks.view.table", new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult());
        var view = Assert.IsType<CsvView>(_w.ActiveTable);
        _w.UpdateLayout();
        while (!view.RowsComplete) view.Tick();
        return (tab, view);
    }

    private static async Task<string?> ClipboardAfterAsync(MainWindow w, string before)
    {
        var sw = Stopwatch.StartNew();
        string? now;
        while ((now = await w.Clipboard!.TryGetTextAsync()) == before && sw.ElapsedMilliseconds < 5000)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        return now;
    }

    private async Task<string?> CopyAsAsync(string command)
    {
        await _w.Clipboard!.SetTextAsync("before");
        Assert.True(await _w.CopyTableAsAsync(command));
        return await ClipboardAfterAsync(_w, "before");
    }

    private static string Bytes(DocTab tab) => Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));

    [AvaloniaFact]
    public void The_selected_columns_come_as_values_header_row_numbers_and_the_bytes_of_each_cell()
    {
        var (tab, view) = OpenTable("data.csv", Sample);
        view.Select(0, 1, 1, 2); // rows 1 and 2, columns name and note
        var data = view.SelectionData();

        Assert.Equal((1, 2), (data.FirstColumn, data.Columns));
        Assert.Equal(["name", "note"], data.Header!.Select(c => c.Value));
        var rows = data.Rows.ToList();
        Assert.Equal([1, 2], rows.Select(r => r.Number));
        Assert.Equal(["Ann", "it's", "Bo\"b", "a|b"], rows.SelectMany(r => r.Cells).Select(c => c.Value));
        var all = tab.Doc.Doc;
        Assert.Equal("\"it's\"", Encoding.UTF8.GetString(all.ReadBytes(rows[0].Cells[1].Range)));
        Assert.Equal("Ann", Encoding.UTF8.GetString(all.ReadBytes(rows[0].Cells[0].Range)));

        view.ApplyFilter("Cyd", 1);
        view.SelectAll();
        Assert.Equal([3], view.SelectionData().Rows.Select(r => r.Number));
    }

    [AvaloniaFact]
    public async Task All_four_forms_reach_the_clipboard_with_the_values_of_the_selected_columns()
    {
        var (tab, view) = OpenTable("data.csv", Sample);
        view.SelectAll();

        Assert.Equal("| id | name | note |\n| --- | --- | --- |\n| 1 | Ann | it's |\n| 2 | Bo\"b | a\\|b |\n| 3 | Cyd | x<br>y |\n", await CopyAsAsync("table.copyAs.markdown"));

        var json = await CopyAsAsync("table.copyAs.json");
        using (var parsed = System.Text.Json.JsonDocument.Parse(json!))
            Assert.Equal(["1", "Ann", "it's", "2", "Bo\"b", "a|b", "3", "Cyd", "x\ny"], parsed.RootElement.EnumerateArray().SelectMany(r => r.EnumerateObject().Select(p => p.Value.GetString())));
        Assert.Contains(_app.ClipboardHistory.Items, item => item == json);

        var html = await CopyAsAsync("table.copyAs.html");
        Assert.Contains("<th>note</th>", html);
        Assert.Contains("<td>it&#39;s</td>", html);
        Assert.Contains("<td>Bo&quot;b</td>", html);
        Assert.Contains("<td>x<br>y</td>", html);

        _dialogs.Prompts.Enqueue("people");
        var sql = await CopyAsAsync("table.copyAs.sql");
        Assert.Contains("INSERT INTO \"people\" (\"id\", \"name\", \"note\") VALUES ('1', 'Ann', 'it''s');\n", sql);
        Assert.Contains("VALUES ('3', 'Cyd', 'x\ny');\n", sql);
        Assert.Equal(Sample, Bytes(tab));
    }

    [AvaloniaFact]
    public async Task The_html_form_is_on_the_clipboard_as_html_too()
    {
        var (_, view) = OpenTable("data.csv", Sample);
        view.SelectAll();
        var html = await CopyAsAsync("table.copyAs.html");

        var data = await _w.Clipboard!.TryGetDataAsync();
        var item = Assert.Single(data!.Items);
        Assert.Contains(ClipboardHtml.Format, item.Formats);
        var raw = await item.TryGetRawAsync(ClipboardHtml.Format);
        var text = Encoding.UTF8.GetString(Assert.IsType<byte[]>(raw));
        Assert.Contains(html!.Trim(), text);
    }

    [AvaloniaFact]
    public void The_windows_form_of_html_names_where_the_fragment_is_in_bytes()
    {
        var bytes = ClipboardHtml.CfHtml("<b>é</b>");
        var text = Encoding.UTF8.GetString(bytes);
        int Offset(string name) => int.Parse(text[(text.IndexOf(name + ":", StringComparison.Ordinal) + name.Length + 1)..][..10]);

        Assert.Equal("<html>", Encoding.UTF8.GetString(bytes, Offset("StartHTML"), 6));
        Assert.Equal("<b>é</b>", Encoding.UTF8.GetString(bytes, Offset("StartFragment"), Offset("EndFragment") - Offset("StartFragment")));
        Assert.Equal(bytes.Length, Offset("EndHTML"));
    }

    [AvaloniaFact]
    public async Task A_hidden_value_of_an_env_file_never_reaches_the_clipboard_by_copy_cut_or_copy_as()
    {
        var (tab, view) = OpenTable(".env", Env);
        view.SelectAll();
        Assert.True(tab.Surface!.SecretsHidden);

        await _w.Clipboard!.SetTextAsync("before");
        _app.Workbench.Commands.ExecuteAsync("nks.edit.copy", new ShellContext(_w, forMenu: false)).AsTask().GetAwaiter().GetResult();
        Assert.Equal(SecretValues.Placeholder + "\n" + SecretValues.Placeholder, await ClipboardAfterAsync(_w, "before"));

        await _w.Clipboard.SetTextAsync("before");
        _app.Workbench.Commands.ExecuteAsync("nks.edit.cut", new ShellContext(_w, forMenu: false)).AsTask().GetAwaiter().GetResult();
        await ClipboardAfterAsync(_w, "before");
        Assert.Equal(Env, Bytes(tab)); // a cut that touches a hidden value deletes nothing

        var markdown = await CopyAsAsync("table.copyAs.markdown");
        Assert.DoesNotContain("hunter2", markdown);
        Assert.DoesNotContain("a b", markdown);
        Assert.Contains("*", markdown);
        Assert.Empty(_dialogs.Asked); // nothing was asked: the plugin was not given the values
        Assert.DoesNotContain(_app.ClipboardHistory.Items, item => item.Contains("hunter2", StringComparison.Ordinal));

        tab.Surface.ToggleSecrets();
        await _w.Clipboard.SetTextAsync("before");
        _app.Workbench.Commands.ExecuteAsync("nks.edit.copy", new ShellContext(_w, forMenu: false)).AsTask().GetAwaiter().GetResult();
        Assert.Equal("KEY=hunter2\n\"OTHER=\"\"a b\"\"\"", await ClipboardAfterAsync(_w, "before")); // the second cell holds quotes, so it is quoted as a spreadsheet does
    }

    [AvaloniaFact]
    public async Task The_sql_form_follows_the_dialect_of_the_folder()
    {
        _temp.File(".editorconfig", "root = true\n[*]\nsql_dialect = mysql\n");
        var (tab, view) = OpenTable("data.csv", Sample);
        Assert.Equal("mysql", tab.Doc.SqlDialect);
        view.SelectAll();

        _dialogs.Prompts.Enqueue("people");
        var sql = await CopyAsAsync("table.copyAs.sql");
        Assert.Contains("INSERT INTO `people` (`id`, `name`, `note`) VALUES ('1', 'Ann', 'it''s');\n", sql);
    }

    [AvaloniaFact]
    public async Task The_sql_form_follows_the_global_setting_and_the_dialect_chosen_for_the_file_beats_it()
    {
        Assert.Null(_app.Workbench.Set("sql.dialect", JsonValue.Create("tsql")));
        var (tab, view) = OpenTable("data.csv", Sample);
        Assert.Equal("tsql", tab.Doc.SqlDialect);
        view.SelectAll();

        _dialogs.Prompts.Enqueue("people");
        var sql = await CopyAsAsync("table.copyAs.sql");
        Assert.Contains("INSERT INTO [people] ([id], [name], [note]) VALUES (N'1', N'Ann', N'it''s');\n", sql);

        tab.Doc.ChooseSqlDialect("sqlite");
        _dialogs.Prompts.Enqueue("people");
        sql = await CopyAsAsync("table.copyAs.sql");
        Assert.Contains("INSERT INTO \"people\" (\"id\", \"name\", \"note\") VALUES ('1', 'Ann', 'it''s');\n", sql);
    }

    [AvaloniaFact]
    public async Task A_selection_with_more_values_than_the_table_api_gives_copies_nothing_and_the_window_names_the_limit()
    {
        static string Row(int i) => $"{i},{new string('a', 55)},{new string('b', 55)}\n";
        var (_, view) = OpenTable("big.csv", "id,name,note\n" + string.Concat(Enumerable.Range(1, 10_000).Select(Row)));
        view.SelectAll();
        await _w.Clipboard!.SetTextAsync("before");

        await _w.CopyTableAsAsync("table.copyAs.markdown");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("before", await _w.Clipboard.TryGetTextAsync());
        Assert.Contains(_w.Notices, n => n.Message.Contains("at most " + PluginLimitsPlaceholders.ClipboardMaxChars + " characters of values", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task A_form_longer_than_the_clipboard_limit_from_a_small_selection_copies_nothing_and_the_window_names_the_limit()
    {
        // 550 000 asterisks are under the limit of the selection and of one message, but Markdown writes a backslash before each: 1.1 million characters
        var (_, view) = OpenTable("cells.csv", "a\n" + new string('*', 550_000) + "\n");
        view.SelectAll();
        await _w.Clipboard!.SetTextAsync("before");

        await _w.CopyTableAsAsync("table.copyAs.markdown");
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("before", await _w.Clipboard.TryGetTextAsync());
        Assert.Contains(_w.Notices, n => n.Message.Contains("clipboard takes at most " + PluginLimitsPlaceholders.ClipboardMaxChars + " characters", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Three_columns_of_a_wider_table_are_copied_in_all_four_forms_and_nothing_of_the_other_columns()
    {
        var (tab, view) = OpenTable("wide.csv", "a,b,c,d,e\n1,\"x<y & 'z'\",3,\"4|4\",5\n6,7,8,9,10\n");
        view.Select(0, 1, 1, 3);

        Assert.Equal("| b | c | d |\n| --- | --- | --- |\n| x\\<y \\& 'z' | 3 | 4\\|4 |\n| 7 | 8 | 9 |\n", await CopyAsAsync("table.copyAs.markdown"));

        _dialogs.Prompts.Enqueue("t");
        Assert.Equal("INSERT INTO \"t\" (\"b\", \"c\", \"d\") VALUES ('x<y & ''z''', '3', '4|4');\nINSERT INTO \"t\" (\"b\", \"c\", \"d\") VALUES ('7', '8', '9');\n", await CopyAsAsync("table.copyAs.sql"));

        using (var json = System.Text.Json.JsonDocument.Parse((await CopyAsAsync("table.copyAs.json"))!))
        {
            var rows = json.RootElement.EnumerateArray().ToList();
            Assert.Equal(["b", "c", "d"], rows[0].EnumerateObject().Select(p => p.Name));
            Assert.Equal(["x<y & 'z'", "3", "4|4", "7", "8", "9"], rows.SelectMany(r => r.EnumerateObject().Select(p => p.Value.GetString())));
        }

        var html = await CopyAsAsync("table.copyAs.html");
        Assert.Contains("<tr><th>b</th><th>c</th><th>d</th></tr>", html);
        Assert.Contains("<tr><td>x&lt;y &amp; &#39;z&#39;</td><td>3</td><td>4|4</td></tr>", html);
        Assert.DoesNotContain(">a<", html);
        Assert.DoesNotContain(">e<", html);
        Assert.DoesNotContain(">10<", html);
        Assert.Equal("a,b,c,d,e\n1,\"x<y & 'z'\",3,\"4|4\",5\n6,7,8,9,10\n", Bytes(tab));
    }

    // ---- own forms: converters of a data plugin marked for tables ----

    private const string Exporters = "{\"schemaVersion\":1,\"id\":\"acme.export\",\"name\":\"Acme exporters\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"converters\":[" +
        "{\"id\":\"wiki\",\"title\":\"Wiki table\",\"table\":true,\"rules\":[{\"find\":\"\\t\",\"replace\":\" || \"},{\"find\":\"^(.+)$\",\"replace\":\"|| $1 ||\",\"regex\":true}]," +
        "\"samples\":[{\"input\":\"a\\tb\",\"expected\":\"|| a || b ||\"}]}," +
        "{\"id\":\"upper\",\"title\":\"Upper case\",\"rules\":[{\"find\":\"a\",\"replace\":\"A\"}]}]}}";

    private ConverterEntry InstallExporters()
    {
        var dir = Path.Combine(_app.ProfileDirectory, "plugins", "acme.export");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), Exporters, Encoding.UTF8);
        _app.ReloadClips();
        Assert.Empty(_app.Clips.Problems);
        return Assert.IsType<ConverterEntry>(_app.Clips.FindConverter("acme.export", "wiki"));
    }

    [AvaloniaFact]
    public async Task A_converter_marked_for_tables_is_offered_after_the_built_in_forms_and_copies_the_cells_in_its_form()
    {
        var entry = InstallExporters();
        var (tab, view) = OpenTable("people.csv", "id,name\n1,Ann\n2,Bo\n");
        view.SelectAll();

        var menu = Assert.IsType<MenuFlyout>(_w.ShowTableCopyAsMenu());
        var titles = menu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        menu.Hide();
        Assert.Equal(MainWindow.TableCopyForms.Length + 1, titles.Count);
        Assert.Equal("Wiki table", titles[^1]);
        Assert.DoesNotContain("Upper case", titles); // a converter without "table" stays out

        await _w.Clipboard!.SetTextAsync("before");
        Assert.True(_w.CopyTableAs(entry));
        Assert.Equal("|| id || name ||\n|| 1 || Ann ||\n|| 2 || Bo ||", await ClipboardAfterAsync(_w, "before"));
        Assert.Contains(_app.ClipboardHistory.Items, item => item.StartsWith("|| id", StringComparison.Ordinal));
        Assert.Equal("id,name\n1,Ann\n2,Bo\n", Bytes(tab));
    }

    [AvaloniaFact]
    public async Task A_converter_gets_a_hidden_value_of_an_env_file_only_as_the_placeholder()
    {
        var entry = InstallExporters();
        var (_, view) = OpenTable(".env", Env);
        view.SelectAll();

        await _w.Clipboard!.SetTextAsync("before");
        Assert.True(_w.CopyTableAs(entry));
        Assert.Equal($"|| {SecretValues.Placeholder} ||\n|| {SecretValues.Placeholder} ||", await ClipboardAfterAsync(_w, "before"));
        Assert.DoesNotContain(_app.ClipboardHistory.Items, item => item.Contains("hunter2", StringComparison.Ordinal));
    }
}
