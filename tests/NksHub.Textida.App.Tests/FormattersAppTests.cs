using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P4.6.3, P4.6.5 and P4.7.2 through the running window: the SQL dialect of a file, a folder and the global setting with its formatter, HTML entities and HTML Tidy
/// as real plugin processes, and Alt+F1 help for the language from a data plugin.
/// </summary>
[Collection("LocState")]
public class FormattersAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public FormattersAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        _app.UriOpener = null;
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static void Settle()
    {
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
    }

    private DocTab Open(string name, string content)
    {
        var tab = _w.OpenFile(_temp.File(name, content), null);
        _w.UpdateLayout();
        Settle();
        tab.Editor!.TextArea.Focus();
        return tab;
    }

    private async Task RunAsync(string command)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, command);
        Assert.Equal(CommandOutcome.Handled, await run);
        await Until(() => !_app.Plugins.IsBusy, "the plugin command to finish");
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;
    private static int Steps(DocTab t) => t.Doc.Doc.UndoDepth - 1;
    private void Undo()
    {
        _w.Undo();
        Settle();
    }

    private bool HasKeywordColor(DocTab tab, int line)
    {
        _w.SelectTab(tab);
        _w.UpdateLayout();
        Settle();
        tab.Editor!.TextArea.TextView.EnsureVisualLines();
        var colors = tab.Editor.TextArea.TextView.GetVisualLine(line)!.Elements.Select(e => e.TextRunProperties.ForegroundBrush).OfType<ISolidColorBrush>().Select(b => b.Color).ToHashSet();
        var light = (ISolidColorBrush)SyntaxPalette.Light.GetBrush("keyword")!;
        var dark = (ISolidColorBrush)SyntaxPalette.Dark.GetBrush("keyword")!;
        return colors.Contains(light.Color) || colors.Contains(dark.Color);
    }

    // ---- P4.6.3 SQL without a connection ----

    [AvaloniaFact]
    public async Task Changing_the_dialect_of_a_folder_changes_the_colors_of_every_sql_file_in_it()
    {
        var config = Open(".editorconfig", "root = true\n[*.sql]\nsql_dialect = mysql\n");
        var a = Open("a.sql", "auto_increment\n");
        var b = Open("b.sql", "auto_increment\n");
        Assert.Equal(["mysql", "mysql"], [a.Doc.SqlDialect, b.Doc.SqlDialect]);
        Assert.True(HasKeywordColor(a, 1) && HasKeywordColor(b, 1));

        config.Editor!.Document.Text = "root = true\n[*.sql]\nsql_dialect = postgres\n";
        Assert.True(await _w.SaveAsync(config));
        _w.UpdateLayout();
        Settle();

        Assert.Equal(["postgres", "postgres"], [a.Doc.SqlDialect, b.Doc.SqlDialect]);
        Assert.False(HasKeywordColor(a, 1) || HasKeywordColor(b, 1));
    }

    [AvaloniaFact]
    public void The_file_beats_the_folder_which_beats_the_global_setting_and_nothing_set_is_generic()
    {
        var plain = Open("p.sql", "auto_increment\n");
        Assert.Equal("generic", plain.Doc.SqlDialect);
        Assert.False(HasKeywordColor(plain, 1));

        _app.Workbench.Set("sql.dialect", JsonValue.Create("mysql"));
        _w.OptionsFor(plain);
        Settle();
        Assert.Equal("mysql", plain.Doc.SqlDialect);
        Assert.True(HasKeywordColor(plain, 1));

        plain.Doc.ChooseSqlDialect("sqlite");
        Settle();
        Assert.Equal("sqlite", plain.Doc.SqlDialect);
        Assert.False(HasKeywordColor(plain, 1));

        plain.Doc.ChooseSqlDialect(null);
        Assert.Equal("mysql", plain.Doc.SqlDialect);
    }

    [AvaloniaFact]
    public async Task The_dialect_of_a_file_is_chosen_from_a_list_and_can_be_handed_back()
    {
        var tab = Open("d.sql", "select 1\n");
        var run = _w.ChooseSqlDialectAsync();
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the dialect pick");
        Assert.Equal(["Podle složky a globálního nastavení", "Generic", "MySQL", "PostgreSQL", "SQLite", "T-SQL"], _w.QuickResults.Select(r => r.Title));
        _w.SetPaletteQuery("SQLite");
        _w.RunPaletteSelection();
        await run;
        Assert.Equal("sqlite", tab.Doc.SqlDialect);
    }

    [AvaloniaFact]
    public async Task Formatting_changes_only_the_selection_in_one_undo_step_and_the_preview_asks_first()
    {
        const string before = "-- keep   this\nselect a,b from   t where a=1;\nselect   1   from u;\n";
        var tab = Open("f.sql", before);
        int from = before.IndexOf("select a", StringComparison.Ordinal), to = before.IndexOf("a=1;", StringComparison.Ordinal) + 4;
        tab.Editor!.Select(from, to - from);
        int steps = Steps(tab);

        var preview = _app.Workbench.Commands.ExecuteAsync("nks.edit.previewFormatSql", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the change review");
        Assert.Equal(before, Text(tab));
        _w.ResultsView!.CancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Until(() => preview.IsCompleted && !_app.Plugins.IsBusy, "the rejected proposal");
        Assert.Equal(before, Text(tab));
        _w.SelectTab(tab);
        Settle();
        tab.Editor!.TextArea.Focus();

        await RunAsync("nks.edit.formatSql");
        var formatted = Text(tab);
        Assert.Equal("-- keep   this\n" + "SELECT\n    a,\n    b\nFROM t\nWHERE a = 1;" + "\nselect   1   from u;\n", formatted);
        Assert.StartsWith(before[..from], formatted);
        Assert.EndsWith(before[to..], formatted);
        Assert.Equal(steps + 1, Steps(tab));

        Undo();
        Assert.Equal(before, Text(tab));
    }

    [AvaloniaFact]
    public async Task Formatting_follows_the_dialect_of_the_file()
    {
        var tab = Open("m.sql", "create table t (id int auto_increment)");
        await RunAsync("nks.edit.formatSql");
        Assert.Equal("create table t (id int auto_increment)".Replace("create table", "CREATE TABLE").Replace(" int ", " INT "), Text(tab));
        tab.Doc.ChooseSqlDialect("mysql");
        await RunAsync("nks.edit.formatSql");
        Assert.Equal("CREATE TABLE t (id INT AUTO_INCREMENT)", Text(tab));
    }

    // ---- P4.6.5 HTML entities and HTML Tidy ----

    [AvaloniaFact]
    public async Task Entities_convert_the_selection_and_back_each_in_one_undo_step()
    {
        const string source = "x <b class=\"a\">Tom & 'Jerry'</b> y";
        var tab = Open("e.html", source);
        tab.Editor!.Select(2, source.Length - 4);
        int steps = Steps(tab);

        await RunAsync("nks.edit.htmlEncode");
        Assert.Equal("x &lt;b class=&quot;a&quot;&gt;Tom &amp; &#39;Jerry&#39;&lt;/b&gt; y", Text(tab));
        Assert.Equal(steps + 1, Steps(tab));

        tab.Editor.Select(2, Text(tab).Length - 4);
        await RunAsync("nks.edit.htmlDecode");
        Assert.Equal(source, Text(tab));
        Assert.Equal(steps + 2, Steps(tab));
    }

    [AvaloniaFact]
    public async Task Tidy_that_is_not_found_says_where_it_looked_and_changes_nothing()
    {
        var missing = Path.Combine(_temp.Path, "nowhere", "tidy-missing");
        _app.Workbench.Set("html.tidyPath", JsonValue.Create(missing));
        var tab = Open("t.html", "<p>x");
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", _temp.Path);
        try { await RunAsync("nks.edit.htmlTidy"); }
        finally { Environment.SetEnvironmentVariable("PATH", oldPath); }

        Assert.Equal("<p>x", Text(tab));
        Assert.Contains(_w.Notices, n => n.Message.Contains(missing, StringComparison.Ordinal) && n.Message.Contains("PATH", StringComparison.Ordinal) && n.Message.Contains("tools", StringComparison.Ordinal));
        Assert.Contains(_app.Output.Read(OutputChannel.Tasks), l => l.Text.Contains(missing, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Tidy_returns_the_result_as_a_proposal_and_its_messages_go_to_the_output_window()
    {
        var tidy = Environment.GetEnvironmentVariable("TEXTIDA_TEST_TIDY");
        Assert.SkipUnless(tidy is not null && File.Exists(tidy), "Set TEXTIDA_TEST_TIDY to the path of an HTML Tidy program to run this test.");
        _app.Workbench.Set("html.tidyPath", JsonValue.Create(tidy));
        var tab = Open("t.html", "<p>hi <b>x</p><img src=a>");
        int steps = Steps(tab);

        var first = _app.Workbench.Commands.ExecuteAsync("nks.edit.htmlTidy", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the change review");
        Assert.Equal("<p>hi <b>x</p><img src=a>", Text(tab));
        Assert.Contains(_app.Output.Read(OutputChannel.Tasks), l => l.Text.Contains("Warning: missing </b> before </p>", StringComparison.Ordinal));
        _w.ResultsView!.CancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Until(() => first.IsCompleted, "the rejected proposal");
        Assert.Equal("<p>hi <b>x</p><img src=a>", Text(tab));

        _w.SelectTab(tab);
        Settle();
        tab.Editor!.TextArea.Focus();
        var second = _app.Workbench.Commands.ExecuteAsync("nks.edit.htmlTidy", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true && _w.ResultsView.ApplyButton.IsVisible, "the second change review");
        _w.ApplyPlan();
        await Until(() => second.IsCompleted && Text(tab).Contains("<!DOCTYPE html>", StringComparison.Ordinal), "the applied proposal");
        Assert.Contains("<p>hi <b>x</b></p>", Text(tab));
        _w.SelectTab(tab);
        Assert.Equal(steps + 1, Steps(tab));
        Undo();
        Assert.Equal("<p>hi <b>x</p><img src=a>", Text(tab));
    }

    // ---- P4.7.2 help for a language ----

    private void InstallHelp(string helps, string? fileName = null, string? fileText = null)
    {
        var dir = Path.Combine(_app.ProfileDirectory, "plugins", "acme-help");
        Directory.CreateDirectory(dir);
        if (fileName is not null) File.WriteAllText(Path.Combine(dir, fileName), fileText!);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName),
            "{\"schemaVersion\":1,\"id\":\"acme.help\",\"name\":\"Acme help\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"languageHelp\":[" + helps + "]}}", Encoding.UTF8);
        _app.ReloadClips();
    }

    private const string PhpUrl = "{\"id\":\"php-manual\",\"title\":\"PHP manual\",\"language\":\"php\",\"kind\":\"url\",\"target\":\"https://docs.example.org/${language}/${word}.html\"}";

    [AvaloniaFact]
    public async Task Alt_F1_over_a_php_function_opens_the_set_source_only_after_the_user_confirms()
    {
        InstallHelp(PhpUrl);
        Assert.Empty(_app.Clips.Problems);
        var opened = new List<Uri>();
        _app.UriOpener = uri => { opened.Add(uri); return Task.FromResult(true); };
        var tab = Open("a.php", "<?php\n$x = array_map($f, $y);\n");
        tab.Editor!.CaretOffset = tab.Editor.Text.IndexOf("array_map", StringComparison.Ordinal) + 4;

        _dialogs.Answers.Enqueue(1);
        TestApp.Press(_w, Key.F1, RawInputModifiers.Alt);
        await Until(() => _dialogs.Asked.Count == 1, "the question");
        Assert.Contains("https://docs.example.org/php/array_map.html", _dialogs.Asked[0].Message);
        Assert.Empty(opened);

        _dialogs.Answers.Enqueue(0);
        await _w.ShowLanguageHelpAsync();
        Assert.Equal([new Uri("https://docs.example.org/php/array_map.html")], opened);
    }

    [AvaloniaFact]
    public async Task A_file_source_opens_in_the_editor_and_several_sources_are_picked_from_a_list()
    {
        InstallHelp(PhpUrl + ",{\"id\":\"php-notes\",\"title\":\"Notes\",\"language\":\"php\",\"kind\":\"file\",\"target\":\"notes.md\"}", "notes.md", "# Notes\n");
        Assert.Empty(_app.Clips.Problems);
        var tab = Open("b.php", "<?php\nstrlen($s);\n");
        tab.Editor!.CaretOffset = tab.Editor.Text.IndexOf("strlen", StringComparison.Ordinal);

        var run = _w.ShowLanguageHelpAsync();
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the source pick");
        Assert.Equal(["PHP manual", "Notes"], _w.QuickResults.Select(r => r.Title));
        _w.SetPaletteQuery("Notes");
        _w.RunPaletteSelection();
        await run;
        Assert.Equal("notes.md", Path.GetFileName(_w.Active!.Path));
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task Without_a_source_or_a_word_the_status_bar_says_so()
    {
        InstallHelp(PhpUrl);
        var css = Open("c.css", "a { color: red }");
        await _w.ShowLanguageHelpAsync();
        Assert.Contains("CSS", _w.StatusMessageText);

        var php = Open("c.php", "<?php\n  \n");
        php.Editor!.CaretOffset = php.Editor.Text.IndexOf("  ", StringComparison.Ordinal) + 1;
        await _w.ShowLanguageHelpAsync();
        Assert.Equal(Loc.Get(K.LangHelpNoWord), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void A_source_with_a_bad_address_or_a_missing_file_is_refused_when_the_plugin_is_read()
    {
        InstallHelp("{\"id\":\"a\",\"title\":\"A\",\"language\":\"php\",\"kind\":\"url\",\"target\":\"ftp://example.org/${word}\"},"
            + "{\"id\":\"b\",\"title\":\"B\",\"language\":\"php\",\"kind\":\"file\",\"target\":\"..\\\\outside.md\"},"
            + "{\"id\":\"c\",\"title\":\"C\",\"language\":\"php\",\"kind\":\"file\",\"target\":\"missing.md\"},"
            + "{\"id\":\"d\",\"title\":\"D\",\"language\":\"php\",\"kind\":\"mail\",\"target\":\"x\"}");
        Assert.Empty(_app.Clips.Helps);
        var problem = Assert.Single(_app.Clips.Problems);
        Assert.Contains("http or https", problem);
        Assert.Contains("leaves the plugin folder", problem);
        Assert.Contains("missing.md", problem);
        Assert.Contains("kind must be", problem);
    }
}
