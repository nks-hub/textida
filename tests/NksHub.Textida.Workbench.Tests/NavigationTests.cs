using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.Workbench.Tests;

public class CalculatorTests
{
    [Theory]
    [InlineData("1+2*3", "7")]
    [InlineData(" (1+2)*3 ", "9")]
    [InlineData("10/4", "2.5")]
    [InlineData("0.1+0.2", "0.3")]
    [InlineData("-3+5", "2")]
    [InlineData("2^10", "1024")]
    [InlineData("2^-1", "0.5")]
    [InlineData("17%5", "2")]
    [InlineData("1/3", "0.333333333333")]
    [InlineData("--4", "4")]
    public void Evaluates_arithmetic_with_the_usual_precedence(string expression, string expected)
    {
        var result = Calculator.Evaluate(expression);
        Assert.True(result.Ok, expression);
        Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("1/0", CalculatorError.DivisionByZero)]
    [InlineData("5%0", CalculatorError.DivisionByZero)]
    [InlineData("0^-1", CalculatorError.DivisionByZero)]
    [InlineData("1+", CalculatorError.Syntax)]
    [InlineData("(1+2", CalculatorError.Syntax)]
    [InlineData("1 2", CalculatorError.Syntax)]
    [InlineData("1.2.3", CalculatorError.Syntax)]
    [InlineData("", CalculatorError.Empty)]
    [InlineData("   ", CalculatorError.Empty)]
    [InlineData("99999999999999999999999999*99999999999999999999", CalculatorError.Overflow)]
    [InlineData("10^400", CalculatorError.Overflow)]
    public void Invalid_expressions_and_division_by_zero_give_an_error_and_no_value(string expression, CalculatorError error)
    {
        var result = Calculator.Evaluate(expression);
        Assert.False(result.Ok);
        Assert.Equal(error, result.Error);
    }

    [Theory]
    [InlineData("System.Diagnostics.Process.Start(\"cmd\")")]
    [InlineData("1; DROP TABLE x")]
    [InlineData("$(whoami)")]
    [InlineData("Math.Sqrt(4)")]
    [InlineData("1e3")]
    [InlineData("`calc`")]
    public void Text_that_is_not_arithmetic_is_never_run_as_anything(string text)
    {
        Assert.False(Calculator.Evaluate(text).Ok);
    }

    [Fact]
    public void A_very_long_or_deeply_nested_expression_is_refused_without_overflowing_the_stack()
    {
        Assert.Equal(CalculatorError.TooComplex, Calculator.Evaluate(new string('(', 100) + "1" + new string(')', 100)).Error);
        Assert.Equal(CalculatorError.TooComplex, Calculator.Evaluate(new string('1', Calculator.MaxLength + 1)).Error);
        Assert.Equal(CalculatorError.TooComplex, Calculator.Evaluate(new string('-', 150) + "1").Error);
    }
}

public class PathIndexTests
{
    private static readonly string[] Files =
    [
        "src/Mvc/Controllers/HomeController.cs",
        "src/Mvc/Views/Home/Index.cshtml",
        "src/Data/Repository.cs",
        "docs/README.md",
        "tests/Mvc.Tests/ControllerTests.cs",
        "src/Přehled/Úvod.txt",
    ];

    private static IReadOnlyList<string> Find(string query) => new PathIndex(Files).Query(query, 10).Select(m => Files[m.Index]).ToList();

    [Fact]
    public void Words_match_parts_of_the_path_in_any_order()
    {
        var hits = Find("mvc ctrl");
        Assert.Equal("src/Mvc/Controllers/HomeController.cs", hits[0]);
        Assert.Contains("tests/Mvc.Tests/ControllerTests.cs", hits);
        Assert.DoesNotContain("docs/README.md", hits);
        Assert.Equal("src/Mvc/Controllers/HomeController.cs", Find("ctrl mvc")[0]);
    }

    [Fact]
    public void The_start_of_the_file_name_beats_a_match_somewhere_in_the_path()
    {
        Assert.Equal("docs/README.md", Find("read")[0]);
        Assert.Equal("src/Data/Repository.cs", Find("repo")[0]);
    }

    [Fact]
    public void Accents_and_case_do_not_matter_and_an_empty_query_matches_nothing()
    {
        Assert.Equal("src/Přehled/Úvod.txt", Find("PREHLED uvod")[0]);
        Assert.Empty(new PathIndex(Files).Query("  ", 10));
        Assert.Empty(Find("qqqqq"));
    }

    [Fact]
    public void A_hundred_thousand_paths_are_matched_within_a_fraction_of_a_second()
    {
        var paths = Enumerable.Range(0, 100_000).Select(i => $"src/module{i % 500}/folder{i % 37}/File{i}.cs").ToList();
        var index = new PathIndex(paths);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var hits = index.Query("mod12 file99", 50);
        sw.Stop();
        Assert.NotEmpty(hits);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"{sw.ElapsedMilliseconds} ms");
    }
}

public class PaletteReasonTests
{
    private static (WorkbenchHost Host, StringTable Strings) Setup(string language) => (WorkbenchHost.CreateBuiltin(), StringTable.LoadBuiltin(language));

    [Fact]
    public void Searching_save_in_czech_finds_every_save_command_with_its_shortcut()
    {
        var (host, strings) = Setup("cs");
        var ctx = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.HasDocument, true);
        var hits = CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.Windows), strings, ctx, "ulož", 20);
        var ids = hits.Select(h => h.CommandId).ToList();
        Assert.Contains("nks.file.save", ids);
        Assert.Contains("nks.file.saveAs", ids);
        Assert.Contains("nks.file.saveAll", ids);
        Assert.Equal("Ctrl+S", hits.Single(h => h.CommandId == "nks.file.save").Shortcut);
        Assert.Equal("Ctrl+Shift+S", hits.Single(h => h.CommandId == "nks.file.saveAs").Shortcut);
        Assert.All(hits.Where(h => h.CommandId.StartsWith("nks.file.save", StringComparison.Ordinal)), h => Assert.True(h.Enabled));
        // a title that holds the text is the only kind of hit; letters scattered over other titles ("Vložit", "Nový soubor ve složce") are not
        Assert.All(hits, h => Assert.Contains("uloz", NksHub.Textida.Workbench.Matching.TextMatching.Fold(h.Title), StringComparison.Ordinal));
        Assert.Equal("nks.edit.duplicateLine", CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.Windows), Setup("en").Strings, ctx, "dupl ln")[0].CommandId);
    }

    [Theory]
    [InlineData("en", "No document is open")]
    [InlineData("cs", "Není otevřený žádný dokument")]
    [InlineData("sk", "Nie je otvorený žiadny dokument")]
    [InlineData("de", "Kein Dokument geöffnet")]
    public void A_command_that_cannot_run_without_a_document_says_so_in_every_language(string language, string expected)
    {
        var (host, strings) = Setup(language);
        var hits = CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.Windows), strings, new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.ReadOnly, true), "", 1000);
        foreach (var id in new[] { "nks.file.save", "nks.edit.undo", "nks.search.relatedFile" })
        {
            var item = hits.Single(h => h.CommandId == id);
            Assert.False(item.Enabled);
            Assert.Equal(expected, item.DisabledReason);
        }
    }

    [Fact]
    public void The_reason_names_the_missing_selection_the_read_only_document_and_the_missing_folder()
    {
        var (host, strings) = Setup("en");
        var keymap = host.BuildKeymap(Platform.Windows);
        var open = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.HasDocument, true).Set(ContextKeys.HasSelection, false).Set(ContextKeys.ReadOnly, false);
        var hits = CommandPalette.Query(host.Commands, keymap, strings, open, "", 1000);
        Assert.Equal("Nothing is selected", hits.Single(h => h.CommandId == "nks.edit.upperCase").DisabledReason);
        Assert.Equal("No folder is open", hits.Single(h => h.CommandId == "nks.files.newFile").DisabledReason);
        Assert.Equal("Nothing is selected in the file panel", hits.Single(h => h.CommandId == "nks.files.rename").DisabledReason);
        Assert.Null(hits.Single(h => h.CommandId == "nks.file.save").DisabledReason);

        var readOnly = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.HasDocument, true).Set(ContextKeys.ReadOnly, true);
        Assert.Equal("The document is read-only", CommandPalette.Query(host.Commands, keymap, strings, readOnly, "", 1000).Single(h => h.CommandId == "nks.edit.deleteLine").DisabledReason);
    }

    [Fact]
    public void The_new_commands_have_keys_and_menu_entries_and_the_file_panel_owns_ctrl_shift_e()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var win = host.BuildKeymap(Platform.Windows);
        Assert.Equal("Ctrl+P", win.ShortcutsFor("nks.search.gotoAnything").First());
        Assert.Equal("Ctrl+Shift+E", win.ShortcutsFor("nks.view.toggleFilePanel").First());
        Assert.Equal("Alt+Left", win.ShortcutsFor("nks.search.jumpBack").First());
        Assert.Equal("⌘P", host.BuildKeymap(Platform.MacOS).ShortcutsFor("nks.search.gotoAnything").First());
        Assert.Equal("⇧⌘E", host.BuildKeymap(Platform.MacOS).ShortcutsFor("nks.view.toggleFilePanel").First());
        Assert.Empty(host.Validate(win));
        var inMenus = host.Commands.CommandsInMenus();
        Assert.Contains("nks.search.gotoAnything", inMenus);
        Assert.Contains("nks.view.toggleFilePanel", inMenus);
    }
}
