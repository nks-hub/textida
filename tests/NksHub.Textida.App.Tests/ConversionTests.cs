using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Conversions;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Clips;
using NksHub.Textida.Workbench.Commands;
using AdapterCarets = NksHub.Textida.AvaloniaEditAdapter.Carets;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P4.2.1, P4.2.2 and P4.11.1 through the running window: converters from a data plugin over the selections with a preview and one undo step, the list of
/// special conversions, and an edit recipe over the open file with a preview and a single undo back to the original text.
/// </summary>
[Collection("LocState")]
public class ConversionTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    private const string Header = "\"schemaVersion\":1,\"id\":\"acme.conv\",\"name\":\"Acme converters\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1}";

    public ConversionTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _app.Clock = () => new DateTime(2026, 10, 27, 13, 5, 9);
        // the list then holds only the converters a test installs; the tests of the special conversions set them back
        _app.SpecialConversions = [];
    }

    public void Dispose()
    {
        _app.SpecialConversions = SpecialConversions.All;
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string PluginsDirectory => Path.Combine(_app.ProfileDirectory, "plugins");

    private void Install(string folder, string manifest)
    {
        var dir = Path.Combine(PluginsDirectory, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), manifest, Encoding.UTF8);
        _app.ReloadClips();
    }

    private static string Manifest(string contributes) => "{" + Header + ",\"contributes\":{" + contributes + "}}";

    private DocTab Open(string text)
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        Dispatcher.UIThread.RunJobs();
        tab.Doc.FlushEol();
        tab.Editor.TextArea.Focus();
        tab.Editor.CaretOffset = 0;
        return tab;
    }

    private static int Steps(DocTab t) => t.Doc.Doc.UndoDepth - 1;
    private static string Text(DocTab t) => t.Editor!.Document.Text;
    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();
    private void Undo() => TestApp.Press(_w, Key.Z, TestApp.Mod);

    private void Pick(string title)
    {
        _w.SetPaletteQuery(title);
        _w.RunPaletteSelection();
    }

    private static List<Sel> SelectionsOf(DocTab t) => t.Editor!.GetSelections().OrderBy(s => s.Start).ToList();

    private const string Entities = "\"converters\":[{\"id\":\"html\",\"title\":\"HTML entities\",\"command\":\"acme.conv.html\"," +
        "\"rules\":[{\"find\":\"&\",\"replace\":\"&amp;\"},{\"find\":\"<\",\"replace\":\"&lt;\"},{\"find\":\">\",\"replace\":\"&gt;\"}]," +
        "\"samples\":[{\"input\":\"a<b & c\",\"expected\":\"a&lt;b &amp; c\"}]}]," +
        "\"commands\":[{\"id\":\"acme.conv.html\",\"title\":\"HTML entities\"}]";

    // ---- P4.2.1 user converters ----

    [AvaloniaFact]
    public void A_converter_from_a_data_plugin_converts_every_selection_with_a_preview_as_one_undo_step_and_the_selections_stay_on_the_new_text()
    {
        Install("acme", Manifest(Entities));
        Assert.Empty(_app.Clips.Problems);
        var tab = Open("<a> and <b> and <c>");
        tab.Editor!.SetCarets(AdapterCarets.Create([new Sel(0, 3), new Sel(8, 11)], 1));
        int steps = Steps(tab);

        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.convert"));
        Assert.Equal("HTML entities", Assert.Single(_w.QuickResults).Title);
        Assert.Equal("&lt;a&gt;", _w.PalettePreviewText);
        _w.RunPaletteSelection();

        Assert.Equal("&lt;a&gt; and &lt;b&gt; and <c>", Text(tab));
        Assert.Equal(steps + 1, Steps(tab));
        Assert.Equal([new Sel(0, 9), new Sel(14, 23)], SelectionsOf(tab));

        Undo();
        Assert.Equal("<a> and <b> and <c>", Text(tab));
    }

    [AvaloniaFact]
    public void With_nothing_selected_the_list_previews_the_sample_and_the_whole_file_is_converted()
    {
        Install("acme", Manifest(Entities));
        var tab = Open("x < y");
        Run("nks.edit.convert");
        Assert.Equal("a&lt;b &amp; c", _w.PalettePreviewText);
        _w.RunPaletteSelection();
        Assert.Equal("x &lt; y", Text(tab));
        Assert.Equal([new Sel(0, 8)], SelectionsOf(tab));
    }

    [AvaloniaFact]
    public void The_command_a_converter_names_converts_at_once_and_a_text_that_does_not_change_says_so()
    {
        Install("acme", Manifest(Entities));
        var tab = Open("plain");
        int steps = Steps(tab);
        Assert.Equal(CommandOutcome.Handled, Run("acme.conv.html"));
        Assert.Equal("plain", Text(tab));
        Assert.Equal(steps, Steps(tab));
        Assert.Contains(Loc.Get(K.StatusNothingToChange), _w.StatusLeftText);

        tab.Editor!.Document.Text = "a & b";
        Run("acme.conv.html");
        Assert.Equal("a &amp; b", Text(tab));
    }

    [AvaloniaFact]
    public void An_invalid_converter_is_refused_at_install_and_nothing_of_the_plugin_is_offered()
    {
        Install("bad", Manifest("\"converters\":[{\"id\":\"bad\",\"title\":\"Bad\",\"rules\":[{\"find\":\"a\",\"replace\":\"b\"}]," +
            "\"samples\":[{\"input\":\"a\",\"expected\":\"a\"}]}]"));
        Assert.Single(_app.Clips.Problems);
        Assert.Contains("sample 1", _app.Clips.Problems[0]);
        Assert.Empty(_app.Clips.Converters);
        Open("a");
        Run("nks.edit.convert");
        Assert.Empty(_w.QuickResults);
        Assert.Contains(Loc.Get(K.ConvertEmpty), _w.PaletteEmptyText);
    }

    [AvaloniaFact]
    public void Repeat_last_command_runs_the_last_conversion_without_the_list()
    {
        Install("acme", Manifest(Entities));
        var tab = Open("a<b\nc<d");
        tab.Editor!.Select(0, 3);
        Run("nks.edit.convert");
        _w.RunPaletteSelection();
        Assert.Equal("a&lt;b\nc<d", Text(tab));

        tab.Editor.Select(7, 3);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.repeatLast"));
        Assert.Equal("a&lt;b\nc&lt;d", Text(tab));
    }

    // ---- P4.2.2 special conversions ----

    [AvaloniaFact]
    public void A_special_conversion_runs_over_several_selections_as_one_step_and_the_selections_stay_on_the_converted_text()
    {
        _app.SpecialConversions = [new SpecialConversion("test.double", "cmd.edit.convert", (s, _) => s + s)];
        var tab = Open("ab cd ef");
        tab.Editor!.SetCarets(AdapterCarets.Create([new Sel(0, 2), new Sel(6, 8)], 1));
        int steps = Steps(tab);
        Run("nks.edit.convert");
        _w.RunPaletteSelection();

        Assert.Equal("abab cd efef", Text(tab));
        Assert.Equal([new Sel(0, 4), new Sel(8, 12)], SelectionsOf(tab));
        Assert.Equal(steps + 1, Steps(tab));
        Undo();
        Assert.Equal("ab cd ef", Text(tab));
        Assert.Equal([new Sel(0, 2), new Sel(6, 8)], SelectionsOf(tab));
    }

    [AvaloniaFact]
    public void Every_shipped_special_conversion_is_a_repeatable_command_in_a_menu_with_its_title_in_every_language()
    {
        var wb = _app.Workbench;
        var inMenus = wb.Commands.CommandsInMenus();
        foreach (var conversion in SpecialConversions.All)
        {
            Assert.True(wb.Commands.TryGet(conversion.CommandId, out var command), conversion.CommandId);
            Assert.Equal(conversion.TitleKey, command.TitleKey);
            Assert.True(command.Repeatable && command.Macro, conversion.CommandId);
            Assert.True(wb.Commands.HasHandler(conversion.CommandId), conversion.CommandId);
            Assert.Contains(conversion.CommandId, inMenus);
            foreach (var language in Loc.Languages)
                Assert.True(StringTable.LoadBuiltin(language).HasOwn(conversion.TitleKey), $"{language}: {conversion.TitleKey}");
        }
    }

    [AvaloniaFact]
    public void Every_shipped_special_conversion_converts_two_selections_as_one_step_keeps_them_on_the_new_text_and_undoes()
    {
        foreach (var conversion in SpecialConversions.All)
        {
            var sample = conversion.Sample!;
            var tab = Open(sample + "\n" + sample);
            var before = new List<Sel> { new(0, sample.Length), new(sample.Length + 1, 2 * sample.Length + 1) };
            tab.Editor!.SetCarets(AdapterCarets.Create(before, 1));
            var converted = conversion.Convert(sample, new ConversionContext(tab.Editor.Settings.TabSize, 0))!;
            int steps = Steps(tab);

            Assert.Equal(CommandOutcome.Handled, Run(conversion.CommandId));
            Assert.Equal(converted + "\n" + converted, Text(tab));
            Assert.Equal([new Sel(0, converted.Length), new Sel(converted.Length + 1, 2 * converted.Length + 1)], SelectionsOf(tab));
            Assert.Equal(steps + 1, Steps(tab));

            Undo();
            Assert.Equal(sample + "\n" + sample, Text(tab));
            Assert.Equal(before, SelectionsOf(tab));
        }
    }

    [AvaloniaFact]
    public void A_special_conversion_that_cannot_read_one_selection_changes_none_and_says_why()
    {
        var tab = Open("12 cat");
        tab.Editor!.SetCarets(AdapterCarets.Create([new Sel(0, 2), new Sel(3, 6)], 1));
        int steps = Steps(tab);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.convert.decToHex"));
        Assert.Equal("12 cat", Text(tab));
        Assert.Equal(steps, Steps(tab));
        Assert.Contains(Loc.Get(K.ConvertSpecialRefused), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void The_list_offers_the_special_conversions_with_a_preview_of_their_sample_and_tabs_follow_the_column_of_the_selection()
    {
        _app.SpecialConversions = SpecialConversions.All;
        var tab = Open("ab\tc");
        Run("nks.edit.convert");
        Assert.Equal(SpecialConversions.All.Count, _w.QuickResults.Count);
        _w.SetPaletteQuery(_app.Workbench.Strings.Get("cmd.edit.convert.removeAccents"));
        Assert.Equal("Prilis zlutoucky kun", _w.PalettePreviewText);
        _w.ClosePalette();

        int size = tab.Editor!.Settings.TabSize;
        tab.Editor.Select(1, 3);
        Run("nks.edit.convert.tabsToSpaces");
        Assert.Equal("ab" + new string(' ', size - 2 % size) + "c", Text(tab));
    }

    [AvaloniaFact]
    public void Every_special_conversion_is_in_the_command_palette_under_its_title_and_in_the_edit_menu_submenu()
    {
        _app.SpecialConversions = SpecialConversions.All;
        var tab = Open("a b");
        tab.Editor!.Select(0, 3);
        var inMenu = _w.MenuModel.Single(g => g.Label == "Úpravy").Items.OfType<MenuGroup>().SelectMany(g => g.Items.OfType<MenuCommand>()).Select(c => c.CommandId).ToList();
        foreach (var conversion in SpecialConversions.All)
        {
            Assert.Contains(conversion.CommandId, inMenu);
            _w.ShowPalette();
            _w.SetPaletteQuery(_app.Workbench.Strings.Get(conversion.TitleKey));
            Assert.Contains(_w.PaletteResults, r => r.CommandId == conversion.CommandId && r.Enabled);
            _w.ClosePalette();
        }

        _w.ShowPalette();
        _w.SetPaletteQuery(_app.Workbench.Strings.Get("cmd.edit.convert.urlEncode"));
        _w.RunPaletteSelection();
        Assert.Equal("a%20b", Text(tab));
    }

    [AvaloniaFact]
    public void A_special_conversion_recorded_in_a_macro_plays_over_other_selections_as_one_undo_step()
    {
        var registry = _app.Workbench.Commands;
        var recorded = Open("a b");
        recorded.Editor!.Select(0, 3);
        Assert.Equal(CommandOutcome.Handled, Run("nks.macro.record"));
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.convert.urlEncode"));
        Assert.Equal(CommandOutcome.Handled, Run("nks.macro.stop"));
        Assert.Equal(["nks.edit.convert.urlEncode"], registry.LastMacro.Select(s => s.Command));

        var played = Open("x y\nz w");
        played.Editor!.SetCarets(AdapterCarets.Create([new Sel(0, 3), new Sel(4, 7)], 1));
        int steps = Steps(played);
        Assert.Equal(CommandOutcome.Handled, Run("nks.macro.playLast"));
        Assert.Equal("x%20y\nz%20w", Text(played));
        Assert.Equal(steps + 1, Steps(played));
        Undo();
        Assert.Equal("x y\nz w", Text(played));
    }

    [AvaloniaTheory]
    [InlineData(4, 3, 3)]
    [InlineData(8, 7, 7)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 2, 2)]
    public void Tabs_to_spaces_uses_the_tab_size_of_the_document_and_the_real_column_of_each_selection_on_one_line(int tabSize, int firstSpaces, int secondSpaces)
    {
        var expected = "a" + new string(' ', firstSpaces) + "b" + new string(' ', secondSpaces) + "c";
        var tab = Open("a\tb\tc");
        tab.Editor!.ApplyOptions(tab.Editor.Settings with { TabSize = tabSize });
        tab.Editor.SetCarets(AdapterCarets.Create([new Sel(1, 2), new Sel(3, 4)], 1));
        int steps = Steps(tab);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.convert.tabsToSpaces"));
        Assert.Equal(expected, Text(tab));
        Assert.Equal(steps + 1, Steps(tab));
        var first = new Sel(1, expected.IndexOf('b'));
        Assert.Equal([first, new Sel(first.End + 1, expected.IndexOf('c'))], SelectionsOf(tab));
    }

    [AvaloniaFact]
    public void Tabs_to_spaces_restarts_the_column_on_every_line_of_the_document()
    {
        var tab = Open("ab\tc\n\td\te");
        tab.Editor!.ApplyOptions(tab.Editor.Settings with { TabSize = 8 });
        tab.Editor.SetCarets(AdapterCarets.Create([new Sel(2, 3), new Sel(5, 6), new Sel(7, 8)], 1));
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.convert.tabsToSpaces"));
        Assert.Equal("ab" + new string(' ', 6) + "c\n" + new string(' ', 8) + "d" + new string(' ', 7) + "e", Text(tab));
    }

    [AvaloniaFact]
    public void The_start_columns_of_selections_count_tab_stops_per_line_and_stay_linear_for_many_selections_on_one_line()
    {
        var small = new AvaloniaEdit.Document.TextDocument("a\tb\nc\td");
        List<Sel> starts = [new Sel(1, 2), new Sel(3, 4), new Sel(4, 5), new Sel(5, 6), new Sel(6, 7)];
        Assert.Equal([1, 9, 0, 1, 8], MainWindow.StartColumns(small, starts, 8));
        Assert.Equal([1, 5, 0, 1, 4], MainWindow.StartColumns(small, starts, 4));

        const int count = 30_000;
        var line = new AvaloniaEdit.Document.TextDocument(string.Concat(Enumerable.Repeat("a\t", count)));
        var targets = Enumerable.Range(0, count).Select(i => new Sel(2 * i + 1, 2 * i + 2)).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var columns = MainWindow.StartColumns(line, targets, 4);
        watch.Stop();
        Assert.Equal(count, columns.Length);
        Assert.All(columns.Select((c, i) => (c, i)), x => Assert.Equal(x.i * 4 + 1, x.c));
        // one pass takes milliseconds; counting from the line start for every selection reads about 9e8 characters, tens of seconds
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    // ---- P4.11.1 edit recipe ----

    private const string Columns = "\"converters\":[{\"id\":\"up\",\"title\":\"Upper\",\"rules\":[{\"find\":\"\\\\w+\",\"replace\":\"\\\\U$&\",\"regex\":true}]}]," +
        "\"recipes\":[{\"id\":\"col\",\"title\":\"Upper column\",\"command\":\"acme.conv.col\",\"steps\":[{\"kind\":\"columns\",\"from\":4,\"to\":5},{\"kind\":\"convert\",\"converter\":\"up\"}]}]," +
        "\"commands\":[{\"id\":\"acme.conv.col\",\"title\":\"Upper column\"}]";

    private const string Table = "id=ab;x\nid=cd;y\nid=ef;z";

    [AvaloniaFact]
    public void A_recipe_selects_a_column_edits_it_shows_the_preview_applies_it_and_one_undo_returns_the_original_file()
    {
        Install("acme", Manifest(Columns));
        Assert.Empty(_app.Clips.Problems);
        var tab = Open(Table);
        int steps = Steps(tab);
        _dialogs.Answers.Enqueue(0);

        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.runRecipe"));
        Assert.Equal("Upper column", Assert.Single(_w.QuickResults).Title);
        Assert.StartsWith(Loc.Plural(K.RecipeChanges, 3, ("title", "Upper column")), _w.PalettePreviewText);
        Assert.Contains("ab → AB", _w.PalettePreviewText);
        _w.RunPaletteSelection();

        var asked = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Plural(K.RecipeChanges, 3, ("title", "Upper column")), asked.Message);
        Assert.Equal(3, asked.Details!.Count);
        Assert.Equal(Loc.Get(K.RecipeLine, ("line", 1), ("column", 4), ("before", "ab"), ("after", "AB")), asked.Details[0]);

        Assert.Equal("id=AB;x\nid=CD;y\nid=EF;z", Text(tab));
        Assert.Equal(steps + 1, Steps(tab));
        Assert.Equal([new Sel(3, 5), new Sel(11, 13), new Sel(19, 21)], SelectionsOf(tab));

        Undo();
        Assert.Equal(Table, Text(tab));
        Assert.Equal(steps, Steps(tab));
    }

    [AvaloniaFact]
    public void Cancelling_the_preview_or_a_file_that_changed_meanwhile_writes_nothing()
    {
        Install("acme", Manifest(Columns));
        var tab = Open(Table);
        _dialogs.Answers.Enqueue(1);
        Run("nks.edit.runRecipe");
        _w.RunPaletteSelection();
        Assert.Equal(Table, Text(tab));

        _dialogs.Answers.Enqueue(0);
        _dialogs.WhileAsking = () => tab.Editor!.Document.Insert(0, "!");
        Run("nks.edit.runRecipe");
        _w.RunPaletteSelection();
        Assert.Equal("!" + Table, Text(tab));
        Assert.Contains(Loc.Get(K.RecipeStale), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void A_recipe_works_inside_the_selection_and_the_command_it_names_runs_it_with_the_preview()
    {
        Install("acme", Manifest(Columns));
        var tab = Open(Table);
        tab.Editor!.Select(0, Table.IndexOf("id=cd", StringComparison.Ordinal));
        _dialogs.Answers.Enqueue(0);
        Assert.Equal(CommandOutcome.Handled, Run("acme.conv.col"));
        Assert.Equal("id=AB;x\nid=cd;y\nid=ef;z", Text(tab));
        Assert.Single(_dialogs.Asked);
    }

    [AvaloniaFact]
    public void Repeat_last_command_applies_the_last_recipe_without_the_preview()
    {
        Install("acme", Manifest(Columns));
        var tab = Open(Table);
        tab.Editor!.Select(0, 8);
        _dialogs.Answers.Enqueue(0);
        Run("nks.edit.runRecipe");
        _w.RunPaletteSelection();
        Assert.Equal("id=AB;x\nid=cd;y\nid=ef;z", Text(tab));

        tab.Editor.Select(8, 8);
        Run("nks.edit.repeatLast");
        Assert.Equal("id=AB;x\nid=CD;y\nid=ef;z", Text(tab));
        Assert.Single(_dialogs.Asked);
    }

    [AvaloniaFact]
    public void A_recipe_that_changes_nothing_or_cannot_run_writes_nothing_and_says_why()
    {
        Install("acme", Manifest("\"recipes\":[{\"id\":\"v\",\"title\":\"Needs file\",\"steps\":[{\"kind\":\"set\",\"text\":\"${file.name}\"}]}," +
            "{\"id\":\"n\",\"title\":\"No match\",\"steps\":[{\"kind\":\"matches\",\"find\":\"zzz\"},{\"kind\":\"set\",\"text\":\"x\"}]}]"));
        var tab = Open("text");
        int steps = Steps(tab);
        Run("nks.edit.runRecipe");
        Assert.Contains("file.name", _w.PalettePreviewText);
        Pick("Needs file");
        Assert.Contains("file.name", _w.StatusLeftText);
        Run("nks.edit.runRecipe");
        Pick("No match");
        Assert.Contains(Loc.Get(K.StatusNothingToChange), _w.StatusLeftText);
        Assert.Equal("text", Text(tab));
        Assert.Equal(steps, Steps(tab));
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public void A_recipe_gives_the_same_value_for_a_variable_as_a_clip_does()
    {
        File.WriteAllText(Path.Combine(_app.ProfileDirectory, UserVariables.FileName), "{\"author\":\"Ann\",\"date\":\"my date\"}");
        Install("acme", Manifest("\"clips\":[{\"id\":\"c\",\"title\":\"Clip\",\"text\":\"${author}|${date}|${selection}|${user.name}\"}]," +
            "\"recipes\":[{\"id\":\"r\",\"title\":\"Recipe\",\"steps\":[{\"kind\":\"set\",\"text\":\"${author}|${date}|${selection}|${user.name}\"}]}]"));
        var viaClip = Open("part");
        viaClip.Editor!.Select(0, 4);
        Run("nks.edit.insertClip");
        Pick("Clip");

        var viaRecipe = Open("part");
        viaRecipe.Editor!.Select(0, 4);
        _dialogs.Answers.Enqueue(0);
        Run("nks.edit.runRecipe");
        Pick("Recipe");

        Assert.Equal(Text(viaClip), Text(viaRecipe));
        Assert.StartsWith("Ann|my date|part|", Text(viaRecipe));
    }

    [AvaloniaFact]
    public void An_invalid_recipe_is_refused_at_install()
    {
        Install("bad", Manifest("\"recipes\":[{\"id\":\"r\",\"title\":\"R\",\"steps\":[{\"kind\":\"convert\",\"converter\":\"nope\"}]}]"));
        Assert.Empty(_app.Clips.Recipes);
        Assert.Contains("unknown converter", Assert.Single(_app.Clips.Problems));
    }

    // ---- the commands ----

    [AvaloniaFact]
    public void The_two_commands_have_a_handler_a_title_in_every_language_and_a_place_in_a_menu_and_are_repeatable()
    {
        var wb = _app.Workbench;
        var inMenus = wb.Commands.CommandsInMenus();
        foreach (var id in new[] { "nks.edit.convert", "nks.edit.runRecipe" })
        {
            Assert.True(wb.Commands.TryGet(id, out var command), id);
            Assert.True(command.Repeatable, id);
            Assert.True(wb.Commands.HasHandler(id), id);
            Assert.Contains(id, inMenus);
            Assert.NotEqual(command.TitleKey, wb.Strings.Get(command.TitleKey));
        }
    }

    [AvaloniaFact]
    public void A_read_only_editor_refuses_both()
    {
        Install("acme", Manifest(Entities + ",\"recipes\":[{\"id\":\"r\",\"title\":\"R\",\"steps\":[{\"kind\":\"set\",\"text\":\"x\"}]}]"));
        var tab = Open("<a>");
        tab.Editor!.IsReadOnly = true;
        Assert.Equal(CommandOutcome.NotHandled, Run("nks.edit.convert"));
        Assert.Equal(CommandOutcome.NotHandled, Run("nks.edit.runRecipe"));
        Assert.Equal("<a>", Text(tab));
    }
}
